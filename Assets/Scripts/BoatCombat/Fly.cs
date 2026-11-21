using UnityEngine;

public class Fly : MonoBehaviour
{
    private enum State { Docked, Attacking, Returning, Landing }

    [Tooltip("Thông số bay dùng chung (Assets/Resource/PlaneFlightProfile)")]
    [SerializeField] private FlightProfile profile;
    [Tooltip("Điểm bắn đạn. Để trống = vị trí máy bay")]
    [SerializeField] private Transform muzzle;

    // Các máy bay đang bật: né nhau bằng khoảng cách, không cần quét collider + GetComponentInParent mỗi frame
    private static readonly System.Collections.Generic.List<Fly> activePlanes = new System.Collections.Generic.List<Fly>();

    public bool IsFlying => state == State.Attacking || state == State.Returning;

    private BoatTeam team;
    private Transform target;
    private Rigidbody targetRb;
    private CarrierData data;
    private BoatStats stats;
    private Transform dockPoint;

    private State state = State.Docked;
    private bool extending;
    private float lifeTimer;
    private float attackTimer;
    private float rearmTimer;
    private float takeoffTimer;
    private float lostTimer;
    private bool tickedThisFrame;

    private float speed;
    private float yawRate;
    private float verticalSpeed;
    private float pitch;
    private float bank;

    private Vector3 dockedLocalScale = Vector3.one;
    private Vector3 dockedWorldScale = Vector3.one;
    private float scaleT;          // 0 = kích thước đỗ, 1 = kích thước bay
    private Vector3 lastDockPos;
    private Vector3 dockVelocity;

    private float CruiseY => profile.seaLevel + profile.flyHeight;
    private float AttackY => profile.seaLevel + Mathf.Min(profile.attackHeight, profile.flyHeight);
    public bool IsDocked => state == State.Docked;

    private float TurnRadius =>
        data.planeSpeed / Mathf.Max(data.planeTurnSpeed * Mathf.Deg2Rad, 0.01f);

    private void OnEnable() => activePlanes.Add(this);
    private void OnDisable() => activePlanes.Remove(this);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => activePlanes.Clear();

    public void Init(BoatTeam planeTeam, CarrierData carrierData, BoatStats carrierStats, Transform dock)
    {
        bool firstInit = data == null;
        team = planeTeam;
        data = carrierData;
        stats = carrierStats;

        if (!firstInit && state != State.Docked)
            return;

        dockPoint = dock;
        state = State.Docked;
        attackTimer = 0f;
        rearmTimer = 0f;
        if (firstInit)
        {
            dockedLocalScale = transform.localScale;
            dockedWorldScale = transform.lossyScale;
        }
        scaleT = 0f;
        if (dock != null)
            lastDockPos = dock.position;
    }

    public void SetTarget(Transform enemy)
    {
        if (enemy == target)
            return;
        target = enemy;
        targetRb = enemy != null ? enemy.GetComponent<Rigidbody>() : null;
        extending = false;
    }

    // Gọi từ CarrierCombat mỗi frame khi tàu sân bay đang thấy địch
    public void Tick(Transform enemy)
    {
        if (data == null || enemy == null || profile == null)
            return;

        switch (state)
        {
            case State.Docked:
                if (rearmTimer > 0f)
                    return; // đang nạp lại trên tàu
                Launch();
                break;

            case State.Returning:
                // Về vì mất dấu địch nhưng vẫn còn thời gian bay -> quay lại đánh
                if (lifeTimer <= 0f)
                    return;
                state = State.Attacking;
                break;

            case State.Landing:
                return;
        }

        SetTarget(enemy);
        tickedThisFrame = true;
    }

    private void Launch()
    {
        state = State.Attacking;
        lifeTimer = data.planeLifetime;
        takeoffTimer = profile.takeoffRollTime;
        extending = false;
        lostTimer = 0f;

        // Rời tàu với tốc độ ít nhất bằng tàu đang chạy, hướng theo mũi tàu
        Vector3 fwd = FlatForward();
        speed = Mathf.Max(data.planeSpeed * profile.takeoffSpeedRatio, Vector3.Dot(dockVelocity, fwd) + 1f);
        yawRate = 0f;
        verticalSpeed = 0f;
        transform.SetParent(null, true);
    }

    private void StartReturn()
    {
        state = State.Returning;
        target = null;
        targetRb = null;
        extending = false;
    }

    private void Land()
    {
        state = State.Docked;
        target = null;
        targetRb = null;
        speed = 0f;
        yawRate = 0f;
        verticalSpeed = 0f;
        pitch = 0f;
        bank = 0f;
        scaleT = 0f;
        rearmTimer = data != null ? data.launchInterval : 0f;
        transform.SetParent(dockPoint, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = dockedLocalScale;
    }

    private void Update()
    {
        if (dockPoint == null)
            return;

        float dt = Time.deltaTime;
        if (dt > 0f)
            dockVelocity = (dockPoint.position - lastDockPos) / dt;
        lastDockPos = dockPoint.position;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || data == null || profile == null)
            return;

        if (state == State.Docked)
        {
            if (rearmTimer > 0f)
                rearmTimer -= dt;
            tickedThisFrame = false;
            return;
        }

        if (attackTimer > 0f)
            attackTimer -= dt;
        lifeTimer -= dt;

        if (state == State.Attacking)
        {
            lostTimer = tickedThisFrame ? 0f : lostTimer + dt;
            if (lifeTimer <= 0f || target == null || !target.gameObject.activeInHierarchy || lostTimer > profile.lostTargetGraceTime)
                StartReturn();
        }
        tickedThisFrame = false;

        switch (state)
        {
            case State.Attacking: AttackStep(dt); break;
            case State.Returning: ReturnStep(dt); break;
            case State.Landing: LandingStep(dt); break;
        }
        if (state != State.Docked && this != null)
            UpdateScale(dt);
    }
    private void AttackStep(float dt)
    {
        Vector3 flatForward = FlatForward();

        // Cất cánh: chạy thẳng theo hướng tàu, lấy độ cao dần
        if (takeoffTimer > 0f)
        {
            takeoffTimer -= dt;
            FlyMove(dt, flatForward, CruiseY, data.planeSpeed, 1f);
            return;
        }

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;
        float angle = Vector3.Angle(flatForward, toTarget);

        // Chỉ bay ra vừa đủ xa để vòng lại là thẳng hàng với mục tiêu, không bay lan ra ngoài trận
        float extendDist = Mathf.Max(data.planeAttackRange * 1.2f, TurnRadius * profile.extendTurnRadiusMultiplier);
        if (!extending && dist < TurnRadius * 1.5f && angle > 70f)
            extending = true;
        else if (extending && dist > extendDist)
            extending = false;

        if (extending)
        {
            FlyMove(dt, flatForward, CruiseY, data.planeSpeed, 1f);
            return;
        }

        // Run: càng gần mục tiêu càng hạ xuống độ cao tấn công (bổ nhào), chỉ khi đã quay mũi về phía mục tiêu
        float diveStart = data.planeAttackRange * profile.diveStartMultiplier;
        float diveT = Mathf.InverseLerp(diveStart, data.planeAttackRange, dist);
        diveT *= Mathf.InverseLerp(60f, 20f, angle);
        float targetY = Mathf.Lerp(CruiseY, AttackY, diveT);

        // Vòng lại: mũi còn lệch xa mục tiêu thì cua gắt để quay về nhanh; đã hướng vào mục tiêu thì lái bình thường
        float turnBoost = Mathf.Lerp(1f, profile.repositionTurnBoost, Mathf.InverseLerp(30f, 90f, angle));

        // Lao vào bắn: chậm lại khi đã thẳng hàng và sắp vào tầm để ở trên mục tiêu lâu hơn
        float runT = Mathf.InverseLerp(data.planeAttackRange * 1.5f, data.planeAttackRange, dist)
                     * Mathf.InverseLerp(60f, 20f, angle);
        float runSpeed = data.planeSpeed * Mathf.Lerp(1f, profile.attackRunSpeedFactor, runT);

        flatForward = FlyMove(dt, toTarget, targetY, runSpeed, turnBoost);
        TryAttack(flatForward, toTarget);
    }

    private void ReturnStep(float dt)
    {
        if (dockPoint == null)
        {
            Destroy(gameObject); // tàu đã chìm / điểm đỗ mất
            return;
        }

        Vector3 dock = dockPoint.position;
        Vector3 toDock = dock - transform.position;
        Vector3 flat = new Vector3(toDock.x, 0f, toDock.z);
        float horiz = flat.magnitude;

        // Gần tàu: giảm tốc (vẫn nhanh hơn tàu một chút) và lái gắt hơn để không lượn vòng quanh điểm đỗ
        float approachT = Mathf.Clamp01(1f - horiz / profile.approachDistance);
        float dockSpeed = new Vector3(dockVelocity.x, 0f, dockVelocity.z).magnitude;
        float targetSpeed = Mathf.Lerp(data.planeSpeed, Mathf.Max(dockSpeed + 2f, data.planeSpeed * 0.35f), approachT);
        float turnBoost = Mathf.Lerp(1f, 2.5f, approachT);

        float targetY = Mathf.Lerp(dock.y, CruiseY, Mathf.Clamp01((horiz - profile.touchdownDistance) / profile.descendDistance));
        FlyMove(dt, flat, targetY, targetSpeed, turnBoost, 1f - approachT);

        if (toDock.magnitude <= profile.touchdownDistance)
            state = State.Landing;
    }

    private void LandingStep(float dt)
    {
        if (dockPoint == null)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 pos = transform.position;
        Vector3 dock = dockPoint.position;
        float dist = Vector3.Distance(pos, dock);

        float closing = Mathf.Max(speed, dockVelocity.magnitude + 1.5f);
        if (dist <= Mathf.Max(profile.landSnapDistance, closing * dt))
        {
            Land();
            return;
        }

        // Bám theo tàu + tiến lại gần điểm đỗ
        pos += dockVelocity * dt;
        pos = Vector3.MoveTowards(pos, dock, Mathf.Max(speed - dockVelocity.magnitude, 1.5f) * dt);
        transform.position = pos;
        speed = Mathf.MoveTowards(speed, dockVelocity.magnitude, profile.acceleration * dt);

        UpdateAttitude(dt, 0f, 0f);
        Quaternion goal = dockPoint.rotation * Quaternion.Euler(pitch, 0f, bank);
        transform.rotation = Quaternion.Slerp(transform.rotation, goal, 1f - Mathf.Exp(-profile.attitudeResponse * dt));
    }
    private Vector3 FlatForward()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.0001f)
            f = Vector3.forward;
        return f.normalized;
    }

private Vector3 FlyMove(float dt, Vector3 desiredFlat, float targetY, float targetSpeed, float turnBoost, float avoidStrength = 1f)
    {
        Vector3 pos = transform.position;
        Vector3 flatForward = FlatForward();

        // Né máy bay khác: trộn vector đẩy ra xa vào hướng muốn bay, và tách độ cao một chút
        if (avoidStrength > 0.01f && desiredFlat.sqrMagnitude > 0.001f)
        {
            Vector3 sep = ComputeSeparation(pos, out float sepY);
            if (sep.sqrMagnitude > 0.0001f)
                desiredFlat = desiredFlat.normalized + sep * (profile.avoidWeight * avoidStrength);
            targetY += sepY * profile.avoidVertical * avoidStrength;
        }

        float maxTurn = data.planeTurnSpeed * turnBoost;
        float angle = desiredFlat.sqrMagnitude > 0.001f
            ? Vector3.SignedAngle(flatForward, desiredFlat, Vector3.up)
            : 0f;
        float desiredYawRate = Mathf.Clamp(angle * profile.turnGain, -maxTurn, maxTurn);
        yawRate = Mathf.MoveTowards(yawRate, desiredYawRate, profile.yawAcceleration * turnBoost * dt);
        flatForward = Quaternion.AngleAxis(yawRate * dt, Vector3.up) * flatForward;

        // Cua gắt thì chậm lại một chút
        float turnRatio = Mathf.Abs(yawRate) / Mathf.Max(data.planeTurnSpeed, 1f);
        float wantSpeed = targetSpeed * Mathf.Lerp(1f, profile.turnSpeedFactor, Mathf.Clamp01(turnRatio));
        speed = Mathf.MoveTowards(speed, wantSpeed, profile.acceleration * dt);

        // Lên / xuống: chỉ bay lên khi đã đủ tốc độ (lúc cất cánh)
        float lift = Mathf.InverseLerp(data.planeSpeed * profile.takeoffSpeedRatio * 0.9f, data.planeSpeed * 0.7f, speed);
        float wantVertical = Mathf.Clamp((targetY - pos.y) * 1.5f, -profile.climbSpeed, profile.climbSpeed);
        if (wantVertical > 0f)
            wantVertical *= lift;
        verticalSpeed = Mathf.MoveTowards(verticalSpeed, wantVertical, profile.climbSpeed * 2f * dt);

        pos += flatForward * speed * dt;
        pos.y += verticalSpeed * dt;
        transform.position = pos;

        UpdateAttitude(dt, verticalSpeed, yawRate);
        transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up) * Quaternion.Euler(pitch, 0f, bank);

        return flatForward;
    }

    // Tính vector đẩy ra xa các máy bay khác đang bay trong bán kính né
    private Vector3 ComputeSeparation(Vector3 pos, out float sepY)
    {
        sepY = 0f;
        Vector3 push = Vector3.zero;
        float radiusSqr = profile.avoidRadius * profile.avoidRadius;

        for (int i = 0; i < activePlanes.Count; i++)
        {
            Fly other = activePlanes[i];
            if (other == this || !other.IsFlying)
                continue;

            Vector3 away = pos - other.transform.position;
            if (away.sqrMagnitude > radiusSqr)
                continue;
            float dist = away.magnitude;
            if (dist < 0.001f)
            {
                // Trùng vị trí hoàn toàn: đẩy theo hướng ngẫu nhiên ổn định theo từng máy bay
                float a = (GetInstanceID() & 255) * Mathf.Deg2Rad * 1.40625f;
                away = new Vector3(Mathf.Cos(a), 0.5f, Mathf.Sin(a));
                dist = 0.001f;
            }

            float w = 1f - Mathf.Clamp01(dist / profile.avoidRadius); // càng gần càng đẩy mạnh
            Vector3 dir = away / Mathf.Max(dist, 0.001f);
            push += new Vector3(dir.x, 0f, dir.z) * w;
            sepY += Mathf.Clamp(dir.y, -1f, 1f) * w + (GetInstanceID() > other.GetInstanceID() ? 0.5f : -0.5f) * w;
        }

        sepY = Mathf.Clamp(sepY, -1f, 1f);
        return push;
    }

    private void UpdateAttitude(float dt, float climbRate, float currentYawRate)
    {
        float targetPitch = -Mathf.Clamp(
            Mathf.Atan2(climbRate, Mathf.Max(speed, 1f)) * Mathf.Rad2Deg, -profile.maxPitch, profile.maxPitch);
        float targetBank = -Mathf.Clamp(currentYawRate / Mathf.Max(data.planeTurnSpeed, 1f), -1f, 1f) * profile.maxBank;

        float k = 1f - Mathf.Exp(-profile.attitudeResponse * dt);
        pitch = Mathf.Lerp(pitch, targetPitch, k);
        bank = Mathf.Lerp(bank, targetBank, k);
    }

    private void UpdateScale(float dt)
    {
        float want;
        if (state == State.Landing)
            want = 0f;
        else if (state == State.Returning && dockPoint != null)
            want = Mathf.Clamp01(Vector3.Distance(transform.position, dockPoint.position) / profile.approachDistance);
        else
            want = 1f;

        float step = profile.scaleTransitionTime > 0.01f ? dt / profile.scaleTransitionTime : 1f;
        scaleT = Mathf.MoveTowards(scaleT, want, step);

        float s = Mathf.SmoothStep(0f, 1f, scaleT);
        transform.localScale = dockedWorldScale * Mathf.Lerp(1f, profile.flightScaleMultiplier, s); // không có parent: local = world
    }

    private void TryAttack(Vector3 flatForward, Vector3 toTargetFlat)
    {
        if (attackTimer > 0f || target == null)
            return;

        if (transform.position.y < profile.seaLevel + (AttackY - profile.seaLevel) * 0.7f)
            return;

        if (toTargetFlat.magnitude > data.planeAttackRange)
            return;

        if (Vector3.Angle(flatForward, toTargetFlat) > profile.fireConeAngle)
            return;

        if (data.planeBulletPrefab == null)
        {
            Debug.LogWarning($"{data.name}: chưa gán planeBulletPrefab, máy bay không bắn được.", data);
            attackTimer = data.planeAttackInterval;
            return;
        }

        attackTimer = data.planeAttackInterval;

        Vector3 from = muzzle != null ? muzzle.position : transform.position;
        GameObject bullet = SimplePool2.Spawn(data.planeBulletPrefab, from, transform.rotation);
        Bullet b = bullet.GetComponent<Bullet>();
        if (b == null)
            return;

        Vector3 targetVelocity = targetRb != null ? targetRb.linearVelocity : Vector3.zero;
        Vector3 aim = PredictIntercept3D(from, target.position, targetVelocity, b.Speed);
        b.InitDirection(team, aim - from, stats != null ? stats.RollDamage() : 0f);
    }

    private static Vector3 PredictIntercept3D(Vector3 shooter, Vector3 target, Vector3 targetVelocity, float projectileSpeed)
    {
        if (projectileSpeed <= 0.01f || targetVelocity.sqrMagnitude < 0.0001f)
            return target;

        Vector3 aim = target;
        for (int i = 0; i < 3; i++)
        {
            float t = Vector3.Distance(shooter, aim) / projectileSpeed;
            aim = target + targetVelocity * t;
        }
        return aim;
    }

    private void OnDrawGizmosSelected()
    {
        if (profile == null)
            return;
        Gizmos.color = Color.cyan;
        Vector3 p = transform.position;
        Gizmos.DrawLine(new Vector3(p.x, profile.seaLevel, p.z), new Vector3(p.x, profile.seaLevel + profile.flyHeight, p.z));
        if (data != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(new Vector3(p.x, profile.seaLevel, p.z), data.planeAttackRange);
        }
    }
}
