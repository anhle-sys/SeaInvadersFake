using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GunnerCombat : BoatCombatBase, IBoatMovementModifier
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
    private const float SlowDownRangeRatio = 1.3f;   // bắt đầu giảm tốc từ (attackRange × hệ số này)

    private const float TurnChoiceAngle = 60f;        // góc lệch từ mức này mới cân nhắc đổi chiều quay
    private const float TurnReleaseAngle = 25f;       // góc lệch dưới mức này thì bỏ chiều đã chọn
    private const float SideBlockThreshold = 0.15f;   // mức bị chắn tối thiểu để đổi chiều
    private const float TurnHoldTime = 0.6f;          // giữ chiều đã chọn tối thiểu bao lâu (chống lật qua lại)
    private const float AllyPushSpeedRatio = 0.35f;   // tàu được đi trước vòng qua / đẩy đồng đội với tốc độ tối thiểu = moveSpeed × hệ số này
    private const float AllyPushRange = 1f;           // khoảng trống trước mũi dưới mức này (không còn chỗ xoay) thì đẩy tàu kia ra thay vì vòng
    private const float AllyPushSteerRatio = 1f;      // khi đẩy, giữ lại phần này của lực bẻ sang mạn vượt (cọ chéo hất nó sang ngang)
    private const float AllyStuckSteerRatio = 2f;     // tàu nhường đứng chờ sát đồng đội: bẻ mạnh gấp mấy lần để quay mũi ra
    private const float AllyPassWeight = 1.5f;        // độ mạnh bẻ lái vòng qua đồng đội chắn trước mũi

    private float steerRadius;        // > 0: cua theo cung tròn bán kính này (chỉ khi đang combat); 0: dùng rotateSpeed
    private int turnSide;             // 0: tự do, +1: quay phải, -1: quay trái
    private float turnHoldTimer;
    private bool yielding;            // đang phanh nhường đồng đội chắn trước mũi
    private float pushSpeed;          // > 0: đang được đi trước, đẩy đồng đội với tốc độ tối thiểu này

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

        // Cắm logic di chuyển riêng của Gunner vào BoatMovementController
        if (movement != null)
            movement.Modifier = this;
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

        if (aimController != null && aimController.IsAimingAtTarget)
            TryAttack(enemy);

        steerRadius = data.turnRadius;   // BoatMovementController hỏi lại qua GetMaxYawRate
        movement.Steer(GetCombatDirection(enemy));

        // Chỉ chạy chậm khi địch đã trong tầm bắn (đang tấn công); ngoài tầm thì chạy hết tốc để áp sát.
        // Giảm dần trong một dải ngay ngoài tầm: không lao vọt qua địch, lượn ở mép tầm không bị giật tốc
        float dist = Vector3.Distance(transform.position, enemy.position);
        float outside = Mathf.InverseLerp(data.attackRange, data.attackRange * SlowDownRangeRatio, dist);
        movement.SetSpeedMultiplier(Mathf.Lerp(data.combatSpeedMultiplier * GetTurningSpeedFactor(enemy), 1f, outside));
    }

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

    public override void OnExitCombat()
    {
        steerRadius = 0f;   // ra khỏi trận: xoay theo rotateSpeed như bình thường
    }

    public void ModifyObstacle(in BoatMovementContext ctx, ref float speedLimit, ref Vector3 steer)
    {
        BoatSteeringSensors sensors = ctx.sensors;
        float allyFree = sensors.AllyAheadFree;
        bool allyAhead = allyFree < float.MaxValue;
        bool pushingThrough = false;
        pushSpeed = 0f;
        yielding = false;
        if (allyAhead)
        {
            float allyLimit = Mathf.Max(Mathf.Sqrt(2f * ctx.brakeDecel * 0.8f * allyFree) - Mathf.Max(sensors.AllyAheadClosing, 0f), 0f);
            if (!sensors.MustYield)
            {
                pushSpeed = ctx.moveSpeed * AllyPushSpeedRatio;
                allyLimit = Mathf.Max(allyLimit, pushSpeed);
                pushingThrough = allyFree < AllyPushRange;
            }
            yielding = allyLimit < ctx.maxSpeed;
            if (yielding)
                speedLimit = Mathf.Min(speedLimit, allyLimit);
        }
        float passWeight = pushingThrough ? AllyPassWeight * AllyPushSteerRatio
                         : yielding && allyFree < AllyPushRange ? AllyPassWeight * AllyStuckSteerRatio
                         : AllyPassWeight;
        steer += sensors.AllyAheadSteer * passWeight;
    }

    public float ModifyHeadingAngle(in BoatMovementContext ctx, float angle)
    {
        BoatSteeringSensors sensors = ctx.sensors;
        float absAngle = Mathf.Abs(angle);
        turnHoldTimer -= ctx.dt;

        if (absAngle < TurnReleaseAngle)
        {
            turnSide = 0;
            return angle;
        }

        float blockRight = sensors.RightBlock;
        float blockLeft = sensors.LeftBlock;
        int shortSide = angle >= 0f ? 1 : -1;
        float shortBlock = shortSide > 0 ? blockRight : blockLeft;
        float longBlock = shortSide > 0 ? blockLeft : blockRight;
        bool shortBlocked = shortBlock > SideBlockThreshold && shortBlock > longBlock;

        if (turnSide == 0)
        {
            if (absAngle >= TurnChoiceAngle && shortBlocked)
                turnSide = -shortSide;
            else if (absAngle > 170f && Mathf.Abs(ctx.currentYawRate) > 1f)
                turnSide = ctx.currentYawRate > 0f ? 1 : -1;   // mục tiêu ngay sau lưng: giữ chiều đang quay
            else
                return angle;
            turnHoldTimer = TurnHoldTime;
        }
        else if (turnHoldTimer <= 0f)
        {
            // Hết thời gian giữ: đường ngắn hết bị chắn thì quay về đường ngắn,
            // chiều đang quay lại bị chắn mà phía kia trống thì đổi chiều
            float sideBlock = turnSide > 0 ? blockRight : blockLeft;
            float otherBlock = turnSide > 0 ? blockLeft : blockRight;
            if (turnSide != shortSide && shortBlock < SideBlockThreshold * 0.5f)
                turnSide = 0;
            else if (sideBlock > SideBlockThreshold && sideBlock > otherBlock + SideBlockThreshold)
            {
                turnSide = -turnSide;
                turnHoldTimer = TurnHoldTime;
            }

            if (turnSide == 0)
                return angle;
        }

        return turnSide == shortSide ? angle : turnSide * (360f - absAngle);
    }

    public float ModifyMeasuredForward(in BoatMovementContext ctx, float measuredForward)
    {
        // Đang đẩy đồng đội ra: lực cản của nó không được xoá tốc độ đẩy về 0
        if (pushSpeed > 0f)
            return Mathf.Max(measuredForward, Mathf.Min(ctx.currentSpeed, pushSpeed));
        return measuredForward;
    }

    public bool TryOverrideSpeed(in BoatMovementContext ctx, float measuredForward, out float speed)
    {
        if (yielding && ctx.sensors.MustYield && measuredForward < 0f)
        {
            speed = measuredForward * Mathf.Exp(-ctx.profile.lateralGrip * ctx.dt);
            return true;
        }

        speed = 0f;
        return false;
    }

    public float GetMaxYawRate(in BoatMovementContext ctx, float turnAuthority)
    {
        float maxYawRate = steerRadius > 0f
            ? Mathf.Max(ctx.currentSpeed, ctx.maxSpeed * 0.3f) / Mathf.Max(steerRadius * ctx.radiusScale, 0.1f) * Mathf.Rad2Deg
            : ctx.rotateSpeed * ctx.rotScale;

        maxYawRate *= turnAuthority;

        // Đang né / nhường đồng đội (thường gần đứng yên): vẫn cho xoay đủ để thoát ra
        if (turnSide != 0 || yielding)
            maxYawRate = Mathf.Max(maxYawRate, ctx.rotateSpeed * ctx.rotScale * BoatMovementController.MinTurnAuthority);

        return maxYawRate;
    }

    public void OnMovementReset()
    {
        steerRadius = 0f;
        turnSide = 0;
        turnHoldTimer = 0f;
        yielding = false;
        pushSpeed = 0f;
    }

    private void TryAttack(Transform enemy)
    {
        if (attackTimer > 0f)
            return;

        if (Vector3.Distance(transform.position, enemy.position) > data.attackRange)
            return;

        attackTimer = data.attackCooldown;

        if (data.bulletPrefab == null)
        {
            Debug.LogWarning($"{data.name}: chưa gán bulletPrefab, tàu không bắn được.", data);
            return;
        }

        StartCoroutine(FireVolley(enemy, new List<Transform>(aimController.ReadyCannons)));
    }

    private IEnumerator FireVolley(Transform enemy, List<Transform> cannons)
    {
        foreach (Transform cannon in cannons)
        {
            if (enemy == null || cannon == null)
                yield break;

            GameObject bullet = SimplePool2.Spawn(data.bulletPrefab, cannon.position, cannon.rotation);
            Bullet b = bullet.GetComponent<Bullet>();
            if (b != null)
            {
                Rigidbody enemyRb = enemy.GetComponent<Rigidbody>();
                Vector3 enemyVelocity = enemyRb != null ? enemyRb.linearVelocity : Vector3.zero;
                b.Init(targetController.Team, enemy.position, enemyVelocity, stats != null ? stats.RollDamage() : 0f);
            }

            if (data.volleyDelay > 0f)
                yield return new WaitForSeconds(data.volleyDelay);
        }
    }
}