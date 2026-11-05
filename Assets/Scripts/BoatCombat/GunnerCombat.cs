using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GunnerCombat : BoatCombatBase
{
    [SerializeField] private BoatAimController aimController;

    private GunnerData data;
    private BoatStats stats;
    private float attackTimer;

    // Orbit state
    private float orbitDirection;   // +1: địch ở mạn phải, -1: mạn trái, 0: chưa chốt
    private bool isPassing;
    private float passTimer;
    private const float PassFrontAngle = 60f;

    public override float CombatSpeedMultiplier =>
        data != null ? data.combatSpeedMultiplier : 0.4f;

    public override void Setup(BoatData boatData, BoatStats boatStats)
    {
        data = boatData as GunnerData;
        stats = boatStats;
        if (data == null)
        {
            Debug.LogError($"{name}: GunnerCombat cần GunnerData!");
            return;
        }

        if (aimController != null)
            aimController.Configure(data.attackRange);
    }

    public override void OnEnterCombat(Transform enemy)
    {
        if (data == null)
            return;

        // Chỉ lướt qua khi địch ở phía trước mũi tàu; địch ở bên/sau thì vào quỹ đạo luôn
        Vector3 to = enemy != null ? enemy.position - transform.position : Vector3.zero;
        to.y = 0f;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        // passMaxTime = 0: không lướt qua, bẻ lái ngay để hông tàu hướng về địch
        isPassing = data.passMaxTime > 0f
                    && to.sqrMagnitude > 0.001f && Vector3.Angle(forward, to) < PassFrontAngle;
        passTimer = 0f;

        switch (data.cannonSide)
        {
            case CannonSide.Right: orbitDirection = 1f; break;
            case CannonSide.Left: orbitDirection = -1f; break;
            default: orbitDirection = 0f; break;
        }

        // Chọn luôn mạn sẽ hướng về địch (cả khi lướt qua): địch đang lệch về bên nào thì để nó bên đó,
        // nên tàu lướt qua địch ở mạn đó rồi bẻ lái về phía địch, không phải đổi mạn
        if (orbitDirection == 0f && to.sqrMagnitude > 0.001f)
        {
            float side = Vector3.Dot(Vector3.Cross(Vector3.up, forward.normalized), to.normalized);
            orbitDirection = side >= 0f ? 1f : -1f;
        }
    }

    private float OrbitDistance => data.attackRange * data.combatDistanceRatio;

    public override void Tick(Transform enemy)
    {
        if (data == null)
            return;

        if (attackTimer > 0f)
            attackTimer -= Time.deltaTime;

        if (aimController.IsAimingAtTarget)
            TryAttack(enemy);

        movement.Steer(GetCombatDirection(enemy), data.turnRadius);
        movement.SetSpeedMultiplier(data.combatSpeedMultiplier * GetTurningSpeedFactor(enemy));
    }

    /// <summary>
    /// Tỉ lệ tốc độ trong combat (nhân với combatSpeedMultiplier):
    /// - Đang lướt qua địch: giữ tốc độ combat.
    /// - Địch còn xa ngoài vùng giao chiến: tăng dần lên tốc độ combat để tiến lại gần.
    /// - Trong vùng giao chiến: chạy chậm. Đang quay hông thì chậm nhất (turningSpeedFactor),
    ///   hông đã chĩa vào địch thì giữ chậm (engagedSpeedFactor) để lượn bắn.
    /// Khi địch chết / mất mục tiêu, AI thoát combat và trả tốc độ về bình thường để đi tìm địch khác.
    /// </summary>
    private float GetTurningSpeedFactor(Transform enemy)
    {
        if (isPassing)
            return 1f;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        Vector3 to = enemy.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist < 0.001f || forward.sqrMagnitude < 0.001f)
            return 1f;

        // 0° = địch ngay ngang hông, 90° = địch ngay trước/sau mũi
        float broadsideError = Mathf.Abs(90f - Vector3.Angle(forward, to));
        float turnT = Mathf.InverseLerp(data.turningFullSpeedAngle, data.turningSlowAngle, broadsideError);
        float engagedFactor = Mathf.Lerp(data.engagedSpeedFactor, data.turningSpeedFactor, turnT);

        // Địch ở xa hơn vòng lượn: tăng dần về tốc độ combat để áp sát
        float farT = Mathf.InverseLerp(OrbitDistance * 1.1f, OrbitDistance * 1.5f, dist);
        return Mathf.Lerp(engagedFactor, 1f, farT);
    }
    private Vector3 GetCombatDirection(Transform enemy)
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 to = enemy.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;

        if (dist < 0.01f)
            return forward;

        Vector3 toDir = to / dist;

        float sign = orbitDirection == 0f ? 1f : orbitDirection;

        if (isPassing)
        {
            passTimer += Time.deltaTime;

            // Kết thúc lướt ngay khi địch ngang hông / vừa ra sau (passDistance = 0), hoặc hết thời gian
            if (Vector3.Dot(forward, to) <= -data.passDistance || passTimer >= data.passMaxTime)
            {
                isPassing = false; // -> bẻ lái về phía địch theo quỹ đạo bên dưới
            }
            else
            {
                // Đi gần như thẳng, chỉ lệch đủ để lướt qua địch ở mạn đã chọn với khoảng cách passOffset
                // (mặc định = bán kính lượn), để khi ngang hông là đã ở đúng vòng lượn
                float offset = data.passOffset > 0f ? data.passOffset : OrbitDistance;
                float offsetAngle = Mathf.Asin(Mathf.Clamp01(offset / dist)) * Mathf.Rad2Deg;
                return Quaternion.AngleAxis(-sign * offsetAngle, Vector3.up) * toDir;
            }
        }

        float orbitDistance = OrbitDistance;
        float correction = Mathf.Clamp((dist - orbitDistance) * data.orbitCorrectionGain,
                                       -data.maxOutwardCorrection, data.maxOrbitCorrection);

        return Quaternion.AngleAxis(-sign * (90f - correction), Vector3.up) * toDir;
    }

    private void TryAttack(Transform enemy)
    {
        if (attackTimer > 0f)
            return;

        if (Vector3.Distance(transform.position, enemy.position) > data.attackRange)
            return;

        attackTimer = data.attackCooldown;
        StartCoroutine(FireVolley(enemy, new List<Transform>(aimController.ReadyCannons)));
    }

    private IEnumerator FireVolley(Transform enemy, List<Transform> cannons)
    {
        foreach (Transform cannon in cannons)
        {
            if (enemy == null || cannon == null)
                yield break;

            GameObject bullet = Instantiate(data.bulletPrefab, cannon.position, cannon.rotation);
            Bullet b = bullet.GetComponent<Bullet>();
            if (b != null)
            {
                Rigidbody enemyRb = enemy.GetComponent<Rigidbody>();
                Vector3 enemyVelocity = enemyRb != null ? enemyRb.linearVelocity : Vector3.zero;
                b.Init(targetController.Team, enemy.position, enemyVelocity, stats.RollDamage());
            }

            if (data.volleyDelay > 0f)
                yield return new WaitForSeconds(data.volleyDelay);
        }
    }
}