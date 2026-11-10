using UnityEngine;

/// <summary>
/// Máy bay của tàu sân bay. Chu trình:
/// Docked -> cất cánh (chạy theo hướng tàu, lấy tốc độ rồi mới bay lên) -> Attacking
/// Attacking: Run (bổ nhào xuống độ cao tấn công, bắn khi mục tiêu ở trước mũi)
///            -> Extend (vượt qua mục tiêu thì kéo lên, bay thẳng ra xa) -> vòng lại Run
/// Hết thời gian bay / mất dấu địch -> Returning (về tàu, hạ dần độ cao, giảm tốc) -> Landing -> Docked (nạp lại)
/// </summary>
public class Fly : MonoBehaviour
{
    private enum State { Docked, Attacking, Returning, Landing }

    [Header("Kích thước")]
    [Tooltip("Scale thế giới khi đang bay = scale lúc đỗ trên tàu × hệ số này")]
    [SerializeField] private float flightScaleMultiplier = 2f;
    [Tooltip("Thời gian phóng to / thu nhỏ khi cất cánh / hạ cánh (giây)")]
    [SerializeField] private float scaleTransitionTime = 0.8f;

    [Header("Bay")]
    [Tooltip("Độ cao bay hành trình (so với mặt biển y = seaLevel)")]
    [SerializeField] private float flyHeight = 10f;
    [Tooltip("Độ cao khi bổ nhào bắn mục tiêu")]
    [SerializeField] private float attackHeight = 6f;
    [SerializeField] private float seaLevel = 0f;
    [Tooltip("Tốc độ lên / xuống tối đa (đơn vị/giây)")]
    [SerializeField] private float climbSpeed = 5f;
    [Tooltip("Gia tốc tiến (đơn vị/giây²)")]
    [SerializeField] private float acceleration = 6f;
    [Tooltip("Độ nhạy lái: tốc độ quay mong muốn = góc lệch × hệ số này (1/giây)")]
    [SerializeField] private float turnGain = 2.5f;
    [Tooltip("Gia tốc quay (độ/giây²). Nhỏ = vào / ra cua êm hơn")]
    [SerializeField] private float yawAcceleration = 300f;
    [Tooltip("Tốc độ còn lại khi cua gắt nhất (0..1)")]
    [Range(0.3f, 1f)][SerializeField] private float turnSpeedFactor = 0.8f;
    [Tooltip("Góc ngóc mũi tối đa (chỉ để nhìn)")]
    [SerializeField] private float maxPitch = 25f;
    [Tooltip("Góc nghiêng cánh tối đa khi cua (chỉ để nhìn)")]
    [SerializeField] private float maxBank = 40f;
    [Tooltip("Độ mượt của góc nghiêng / ngóc mũi (1/giây)")]
    [SerializeField] private float attitudeResponse = 5f;

    [Header("Cất cánh")]
    [Tooltip("Thời gian chạy thẳng theo hướng tàu trước khi bẻ lái (giây)")]
    [SerializeField] private float takeoffRollTime = 0.6f;
    [Tooltip("Tốc độ ban đầu khi rời tàu (tỉ lệ so với planeSpeed)")]
    [Range(0f, 1f)][SerializeField] private float takeoffSpeedRatio = 0.45f;

    [Header("Tấn công")]
    [Tooltip("Điểm bắn đạn. Để trống = vị trí máy bay")]
    [SerializeField] private Transform muzzle;
    [Tooltip("Chỉ bắn khi mục tiêu nằm trong nón phía trước mũi (độ, mặt phẳng ngang)")]
    [SerializeField] private float fireConeAngle = 25f;
    [Tooltip("Bắt đầu bổ nhào khi cách mục tiêu (planeAttackRange × hệ số này)")]
    [SerializeField] private float diveStartMultiplier = 2.5f;
    [Tooltip("Bay ra xa tới (bán kính cua × hệ số này) rồi mới vòng lại đánh tiếp")]
    [SerializeField] private float extendTurnRadiusMultiplier = 3f;
    [Tooltip("Mất dấu địch quá thời gian này mới quay về tàu (giây). Tránh bay qua bay lại khi địch ở mép tầm phát hiện")]
    [SerializeField] private float lostTargetGraceTime = 1f;

    [Header("Quay về tàu")]
    [Tooltip("Cách điểm đỗ (ngang) bao nhiêu thì bắt đầu hạ độ cao")]
    [SerializeField] private float descendDistance = 18f;
    [Tooltip("Cách điểm đỗ bao nhiêu thì bắt đầu giảm tốc và lái gắt hơn")]
    [SerializeField] private float approachDistance = 8f;
    [Tooltip("Cách điểm đỗ (3D) bao nhiêu thì vào chặng chạm boong")]
    [SerializeField] private float touchdownDistance = 2f;
    [SerializeField] private float landSnapDistance = 0.15f;

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

    private float CruiseY => seaLevel + flyHeight;
    private float AttackY => seaLevel + Mathf.Min(attackHeight, flyHeight);
    public bool IsDocked => state == State.Docked;

    // Bán kính cua ở tốc độ tối đa
    private float TurnRadius =>
        data.planeSpeed / Mathf.Max(data.planeTurnSpeed * Mathf.Deg2Rad, 0.01f);

    public void Init(BoatTeam planeTeam, CarrierData carrierData, BoatStats carrierStats, Transform dock)
    {
        bool firstInit = data == null;
        team = planeTeam;
        data = carrierData;
        stats = carrierStats;

        // Init gọi lại khi nâng cấp tàu: máy bay đang bay thì chỉ cập nhật chỉ số, không reset vị trí / scale
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
        if (data == null || enemy == null)
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
        takeoffTimer = takeoffRollTime;
        extending = false;
        lostTimer = 0f;

        // Rời tàu với tốc độ ít nhất bằng tàu đang chạy, hướng theo mũi tàu
        Vector3 fwd = FlatForward();
        speed = Mathf.Max(data.planeSpeed * takeoffSpeedRatio, Vector3.Dot(dockVelocity, fwd) + 1f);
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
        if (dt <= 0f || data == null)
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
            if (lifeTimer <= 0f || target == null || !target.gameObject.activeInHierarchy || lostTimer > lostTargetGraceTime)
                StartReturn();
        }
        tickedThisFrame = false;

        switch (state)
        {
            case State.Attacking: AttackStep(dt); break;
            case State.Returning: ReturnStep(dt); break;
            case State.Landing: LandingStep(dt); break;
        }

        // Vừa đáp trong frame này: Land() đã trả scale về như lúc đỗ, không ghi đè
        if (state != State.Docked && this != null)
            UpdateScale(dt);
    }

    // ---------------- Tấn công ----------------

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

        // Vượt qua mục tiêu ở cự ly gần -> kéo lên, bay thẳng ra xa rồi mới vòng lại.
        // Nếu bẻ lái liên tục, mục tiêu nằm trong vòng cua và máy bay sẽ lượn tròn mãi không bắn được.
        float extendDist = Mathf.Max(data.planeAttackRange * 1.5f, TurnRadius * extendTurnRadiusMultiplier);
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
        float diveStart = data.planeAttackRange * diveStartMultiplier;
        float diveT = Mathf.InverseLerp(diveStart, data.planeAttackRange, dist);
        diveT *= Mathf.InverseLerp(60f, 20f, angle);
        float targetY = Mathf.Lerp(CruiseY, AttackY, diveT);

        flatForward = FlyMove(dt, toTarget, targetY, data.planeSpeed, 1f);
        TryAttack(flatForward, toTarget);
    }

    // ---------------- Về tàu ----------------

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
        float approachT = Mathf.Clamp01(1f - horiz / approachDistance);
        float dockSpeed = new Vector3(dockVelocity.x, 0f, dockVelocity.z).magnitude;
        float targetSpeed = Mathf.Lerp(data.planeSpeed, Mathf.Max(dockSpeed + 2f, data.planeSpeed * 0.35f), approachT);
        float turnBoost = Mathf.Lerp(1f, 2.5f, approachT);

        float targetY = Mathf.Lerp(dock.y, CruiseY, Mathf.Clamp01((horiz - touchdownDistance) / descendDistance));
        FlyMove(dt, flat, targetY, targetSpeed, turnBoost);

        if (toDock.magnitude <= touchdownDistance)
            state = State.Landing;
    }

    // Chạm boong: trượt vào điểm đỗ (điểm đỗ đang chạy theo tàu), xoay khớp hướng tàu
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
        if (dist <= Mathf.Max(landSnapDistance, closing * dt))
        {
            Land();
            return;
        }

        // Bám theo tàu + tiến lại gần điểm đỗ
        pos += dockVelocity * dt;
        pos = Vector3.MoveTowards(pos, dock, Mathf.Max(speed - dockVelocity.magnitude, 1.5f) * dt);
        transform.position = pos;
        speed = Mathf.MoveTowards(speed, dockVelocity.magnitude, acceleration * dt);

        UpdateAttitude(dt, 0f, 0f);
        Quaternion goal = dockPoint.rotation * Quaternion.Euler(pitch, 0f, bank);
        transform.rotation = Quaternion.Slerp(transform.rotation, goal, 1f - Mathf.Exp(-attitudeResponse * dt));
    }

    // ---------------- Chuyển động chung ----------------

    private Vector3 FlatForward()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.0001f)
            f = Vector3.forward;
        return f.normalized;
    }

    // Bay như máy bay thật: tốc độ quay và tốc độ lên/xuống thay đổi dần, không đổi hướng tức thì
    private Vector3 FlyMove(float dt, Vector3 desiredFlat, float targetY, float targetSpeed, float turnBoost)
    {
        Vector3 pos = transform.position;
        Vector3 flatForward = FlatForward();

        float maxTurn = data.planeTurnSpeed * turnBoost;
        float angle = desiredFlat.sqrMagnitude > 0.001f
            ? Vector3.SignedAngle(flatForward, desiredFlat, Vector3.up)
            : 0f;
        float desiredYawRate = Mathf.Clamp(angle * turnGain, -maxTurn, maxTurn);
        yawRate = Mathf.MoveTowards(yawRate, desiredYawRate, yawAcceleration * turnBoost * dt);
        flatForward = Quaternion.AngleAxis(yawRate * dt, Vector3.up) * flatForward;

        // Cua gắt thì chậm lại một chút
        float turnRatio = Mathf.Abs(yawRate) / Mathf.Max(data.planeTurnSpeed, 1f);
        float wantSpeed = targetSpeed * Mathf.Lerp(1f, turnSpeedFactor, Mathf.Clamp01(turnRatio));
        speed = Mathf.MoveTowards(speed, wantSpeed, acceleration * dt);

        // Lên / xuống: chỉ bay lên khi đã đủ tốc độ (lúc cất cánh)
        float lift = Mathf.InverseLerp(data.planeSpeed * takeoffSpeedRatio * 0.9f, data.planeSpeed * 0.7f, speed);
        float wantVertical = Mathf.Clamp((targetY - pos.y) * 1.5f, -climbSpeed, climbSpeed);
        if (wantVertical > 0f)
            wantVertical *= lift;
        verticalSpeed = Mathf.MoveTowards(verticalSpeed, wantVertical, climbSpeed * 2f * dt);

        pos += flatForward * speed * dt;
        pos.y += verticalSpeed * dt;
        transform.position = pos;

        UpdateAttitude(dt, verticalSpeed, yawRate);
        transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up) * Quaternion.Euler(pitch, 0f, bank);

        return flatForward;
    }

    private void UpdateAttitude(float dt, float climbRate, float currentYawRate)
    {
        float targetPitch = -Mathf.Clamp(
            Mathf.Atan2(climbRate, Mathf.Max(speed, 1f)) * Mathf.Rad2Deg, -maxPitch, maxPitch);
        float targetBank = -Mathf.Clamp(currentYawRate / Mathf.Max(data.planeTurnSpeed, 1f), -1f, 1f) * maxBank;

        float k = 1f - Mathf.Exp(-attitudeResponse * dt);
        pitch = Mathf.Lerp(pitch, targetPitch, k);
        bank = Mathf.Lerp(bank, targetBank, k);
    }

    // Phóng to khi rời tàu, thu nhỏ dần khi về gần tàu
    private void UpdateScale(float dt)
    {
        float want;
        if (state == State.Landing)
            want = 0f;
        else if (state == State.Returning && dockPoint != null)
            want = Mathf.Clamp01(Vector3.Distance(transform.position, dockPoint.position) / approachDistance);
        else
            want = 1f;

        float step = scaleTransitionTime > 0.01f ? dt / scaleTransitionTime : 1f;
        scaleT = Mathf.MoveTowards(scaleT, want, step);

        float s = Mathf.SmoothStep(0f, 1f, scaleT);
        transform.localScale = dockedWorldScale * Mathf.Lerp(1f, flightScaleMultiplier, s); // không có parent: local = world
    }

    // ---------------- Bắn ----------------

    private void TryAttack(Vector3 flatForward, Vector3 toTargetFlat)
    {
        if (attackTimer > 0f || target == null)
            return;

        // Không bắn khi chưa lên đủ cao (vừa cất cánh)
        if (transform.position.y < seaLevel + (AttackY - seaLevel) * 0.7f)
            return;

        if (toTargetFlat.magnitude > data.planeAttackRange)
            return;

        if (Vector3.Angle(flatForward, toTargetFlat) > fireConeAngle)
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
        Gizmos.color = Color.cyan;
        Vector3 p = transform.position;
        Gizmos.DrawLine(new Vector3(p.x, seaLevel, p.z), new Vector3(p.x, seaLevel + flyHeight, p.z));
        if (data != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(new Vector3(p.x, seaLevel, p.z), data.planeAttackRange);
        }
    }
}
