using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GunnerCombat : BoatCombatBase, IBoatMovementModifier
{
    [SerializeField] private BoatAimController aimController;
    [SerializeField] private LayerMask allyMask = ~0;   // layer của thân tàu, để quét đồng đội gần

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

    // Né đồng đội có ưu tiên cao hơn
    private const float AllyAvoidRadius = 4f;         // tàu ưu tiên thấp bắt đầu né khi tàu ưu tiên cao vào trong bán kính này
    private const float DodgeSideWeight = 3f;         // lực bẻ lái sang ngang
    private const float DodgeAwayWeight = 1.5f;       // lực nhích ra xa tàu ưu tiên cao
    private const float DodgeSpeedRatio = 0.5f;       // tốc độ tối đa khi đang né = moveSpeed × hệ số này (không bao giờ lùi)
    private const int MaxAllyHits = 8;

    private float steerRadius;        // > 0: cua theo cung tròn bán kính này (chỉ khi đang combat); 0: dùng rotateSpeed
    private int turnSide;             // 0: tự do, +1: quay phải, -1: quay trái
    private float turnHoldTimer;

    private bool dodging;             // đang né tàu ưu tiên cao
    private int dodgeSide;            // chiều né đã chốt: +1 phải, -1 trái, 0 chưa chốt (giữ cho tới khi hết tàu cần né)

    private Rigidbody body;
    private readonly Collider[] allyHits = new Collider[MaxAllyHits];
    public int PriorityId => gameObject.GetInstanceID();

    private static bool HasPriority(int a, int b) => a < b;

    public override float CombatSpeedMultiplier =>
        data != null ? data.combatSpeedMultiplier : 0.4f;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

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

        // Chọn luôn mạn sẽ hướng về địch
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

        // Chỉ chạy chậm khi địch đã trong tầm bắn, ngoài tầm thì chạy hết tốc để áp sát.
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
        dodging = false;

        Vector3 away = Vector3.zero;
        if (body != null && GameController.BattleStarted && targetController != null)
            away = SenseHigherPriorityAllies();

        if (away.sqrMagnitude < 0.0001f)
        {
            dodgeSide = 0;   // hết tàu cần né -> lần sau chọn lại chiều
            return;
        }

        dodging = true;
        Vector3 awayDir = away.normalized;

        // Chốt chiều né một lần cho tới khi thoát (chống lật trái/phải khi tàu kia ở ngay trước mũi)
        if (dodgeSide == 0)
        {
            float side = Vector3.Dot(transform.right, awayDir);
            if (Mathf.Abs(side) < 0.05f)
                dodgeSide = gameObject.GetInstanceID() % 2 == 0 ? 1 : -1;
            else
                dodgeSide = side > 0f ? 1 : -1;
        }

        // Bẻ lái sang ngang + nhích ra xa tàu ưu tiên cao
        steer += transform.right * (dodgeSide * DodgeSideWeight) + awayDir * DodgeAwayWeight;

        // Chạy chậm vừa đủ để kịp xoay, không đứng yên, không lùi
        speedLimit = Mathf.Min(speedLimit, ctx.moveSpeed * DodgeSpeedRatio);
    }
    private Vector3 SenseHigherPriorityAllies()
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, AllyAvoidRadius, allyHits, allyMask, QueryTriggerInteraction.Ignore);

        int myId = PriorityId;
        Vector3 away = Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            Rigidbody otherBody = allyHits[i].attachedRigidbody;
            if (otherBody == null || otherBody == body)
                continue;

            if (!otherBody.TryGetComponent(out BoatTargetController other) || other.Team != targetController.Team)
                continue;

            if (!HasPriority(otherBody.gameObject.GetInstanceID(), myId))
                continue;   // mình ưu tiên cao hơn: không né, không nhường

            Vector3 fromOther = body.position - otherBody.position;
            fromOther.y = 0f;
            float dist = fromOther.magnitude;
            if (dist < 0.01f)
            {
                // Trùng vị trí: đẩy theo hướng ngang của mình để tách ra
                away += transform.right;
                continue;
            }

            float weight = 1f - Mathf.Clamp01(dist / AllyAvoidRadius);   // càng gần càng mạnh
            away += (fromOther / dist) * Mathf.Max(weight, 0.1f);
        }

        return away;
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

    // Không còn đẩy ở tốc độ tối thiểu / lùi nhường nên không cần can thiệp tốc độ đo được
    public float ModifyMeasuredForward(in BoatMovementContext ctx, float measuredForward) => measuredForward;

    public bool TryOverrideSpeed(in BoatMovementContext ctx, float measuredForward, out float speed)
    {
        speed = 0f;
        return false;
    }

    public float GetMaxYawRate(in BoatMovementContext ctx, float turnAuthority)
    {
        float maxYawRate = steerRadius > 0f
            ? Mathf.Max(ctx.currentSpeed, ctx.maxSpeed * 0.3f) / Mathf.Max(steerRadius * ctx.radiusScale, 0.1f) * Mathf.Rad2Deg
            : ctx.rotateSpeed * ctx.rotScale;

        maxYawRate *= turnAuthority;

        // Đang né tàu ưu tiên cao (thường chạy chậm): vẫn cho xoay đủ để thoát ra
        if (turnSide != 0 || dodging)
            maxYawRate = Mathf.Max(maxYawRate, ctx.rotateSpeed * ctx.rotScale * BoatMovementController.MinTurnAuthority);

        return maxYawRate;
    }

    public void OnMovementReset()
    {
        steerRadius = 0f;
        turnSide = 0;
        turnHoldTimer = 0f;
        dodging = false;
        dodgeSide = 0;
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