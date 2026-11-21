using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BoatMovementController : MonoBehaviour
{
    // Các thông số hiếm khi cần chỉnh
    private const float FeedForwardSmoothing = 0.15f; // giây
    public const float MinTurnAuthority = 0.3f;      // khả năng bẻ lái khi gần đứng yên (0..1)
    private const float SharpTurnStartAngle = 20f;    // từ góc lệch này bắt đầu giảm tốc khi cua
    private const float SharpTurnFullAngle = 120f;    // góc lệch giảm tốc nhiều nhất
    private const float MinArrivalFactor = 0.15f;
    public const float MaxSpeedMultiplier = 4f;

    // Gỡ kẹt khi bị chặn mũi: lùi + xoay tại chỗ, rồi chạy chếch sang một bên để vòng qua
    private const float StuckSpeedRatio = 0.25f;      // chạy dưới (tốc độ muốn × hệ số này) coi như bị chặn
    private const float StuckSpeedMax = 0.3f;         // ...nhưng không quá mức này (đơn vị/giây)
    private const float StuckMinWantedSpeed = 0.5f;   // muốn chạy chậm hơn mức này thì không tính là kẹt
    private const float StuckDelay = 0.8f;            // bị chặn liên tục bao lâu thì bắt đầu gỡ
    private const float StuckRetryDelay = 2f;         // vừa gỡ xong mà kẹt lại thì chờ lâu hơn (không lùi tới lùi lui liên tục)
    private const float StuckRetryWindow = 8f;       // "vừa gỡ xong" kéo dài bao lâu
    private const float WaitingStuckDelay = 3f;       // tự phanh đứng chờ (nhường tàu khác) lâu tới mức này mới lùi

    // Tàu khác (đồng đội / địch) nằm trên đường chạy trước mũi: phanh giữ khoảng cách + bẻ lái vòng qua
    private const float AheadSteerWeight = 1f;        // độ mạnh bẻ lái vòng qua tàu chắn mũi
    private const float AheadSteerSmoothing = 0.35f;  // giây: lực bẻ lái vào/ra từ từ, không giật trái phải
    private const float AheadBrakeFactor = 0.8f;      // phanh sớm hơn một chút so với deceleration tối đa
    private const float UnstickReverseTime = 0.7f;    // thời gian lùi + xoay
    private const float UnstickSwingTime = 1.2f;      // thời gian chạy chếch để vòng qua sau khi lùi
    private const float UnstickReverseRatio = 0.4f;   // tốc độ lùi = moveSpeed × hệ số này
    private const float UnstickTurnAuthority = 0.7f;  // khả năng bẻ lái khi đang gỡ kẹt (0..1)
    private const float UnstickTurnAngle = 70f;       // góc bẻ lái tối thiểu khi đang lùi
    private const float UnstickSwingAngle = 45f;      // góc chếch khỏi hướng mong muốn khi vòng qua

    [Tooltip("Thông số cảm giác lái. Tạo mới bằng Create > Boats > Handling Profile")]
    [SerializeField] private BoatHandlingProfile profile;

    [Header("Bị đẩy lùi khi trúng đòn")]
    [Tooltip("Vận tốc đẩy lùi (đơn vị/giây) với cú đánh chuẩn. 0 = tắt")]
    [SerializeField] private float knockbackSpeed = 1.6f;
    [Tooltip("Tốc độ tắt dần của lực đẩy: lớn = dừng nhanh")]
    [SerializeField] private float knockbackDecay = 3.5f;
    [Tooltip("Sát thương bằng tỉ lệ này của máu tối đa thì đẩy ở mức 100%")]
    [SerializeField] private float knockbackFullDamageRatio = 0.1f;

    private float moveSpeed = 5f;
    private float rotateSpeed = 120f;

    private Rigidbody rb;
    private BoatSteeringSensors sensors;
    private BattleBounds bounds;
    private Vector3 desiredDirection;
    private bool hasInput;
    private float speedMultiplier = 1f;
    private float arrivalFactor = 1f; // 0..1, giảm khi gần đích
    private float currentSpeed;
    private float currentYawRate;     // độ/giây
    private float lastAcceleration;
    private bool wasBattleStarted;
    private float referenceMass = 1f;
    private float radiusScale = 1f;
    private float rotScale = 1f;
    private Vector3 aheadSteer;       // lực vòng qua tàu chắn mũi, đã làm mượt
    private bool steeringAroundBoat;  // đang bẻ lái vòng qua tàu chắn mũi

    // Đo tốc độ xoay của hướng mong muốn để bù trước khi bẻ lái (feed-forward)
    private struct HeadingTracker
    {
        public float prevDesired;
        public bool hasPrev;
        public float rate;            // độ/giây, đã làm mượt
    }
    private HeadingTracker heading;

    // Phát hiện bị chặn mũi và gỡ kẹt (lùi + xoay, rồi chạy chếch để vòng qua)
    private struct StuckState
    {
        public float wantedSpeed;     // tốc độ muốn chạy ở bước trước (chưa tính phanh vì vật cản)
        public float allowedSpeed;    // tốc độ được phép chạy ở bước trước (đã tính phanh vì vật cản / tàu chắn mũi)
        public float blockedTimer;    // bị tì mũi, không nhúc nhích được bao lâu
        public float waitingTimer;    // muốn chạy nhưng đang tự phanh đứng chờ bao lâu
        public float unstickTimer;    // > 0: đang gỡ kẹt
        public int unstickSide;       // chiều xoay khi gỡ kẹt: +1 phải, -1 trái, 0 chưa chốt
        public float retryTimer;      // > 0: vừa gỡ kẹt xong
    }
    private StuckState stuck;

    // Đẩy lùi khi trúng đòn: cộng thêm lên vận tốc lái, tách riêng để không lẫn vào tốc độ lái đo được
    private struct KnockbackState
    {
        public Vector3 applied;       // đã cộng vào rb ở bước vật lý trước
        public Vector3 pending;       // cú đánh mới, chờ bước vật lý kế tiếp
    }
    private KnockbackState knockback;
    private BoatHealth health;

    // Né cả tàu địch (Gunner / Carrier bật; Banger để tắt vì nó lao vào địch)
    public bool AvoidEnemies { get; set; }
    // Mục tiêu đang đánh: vẫn phanh / vòng qua nếu nó chắn mũi, nhưng không bị đẩy ra xa (tàu lượn quanh nó)
    public Transform CombatTarget { get; set; }
    // Tạm không lùi gỡ kẹt (vd: Gunner đang ngắm trúng địch, đứng yên bắn cũng được)
    public bool SuppressUnstick { get; set; }
    private bool boundsLimited;       // bước trước bị mép tầm nhìn camera giữ lại (không phải kẹt)

    private bool IsUnsticking => stuck.unstickTimer > 0f;
    private bool IsReversing => stuck.unstickTimer > UnstickSwingTime;

    // Logic di chuyển riêng của từng loại tàu
    public IBoatMovementModifier Modifier { get; set; }

    public bool IsMoving => hasInput || currentSpeed > 0.1f;
    public float CurrentSpeed => currentSpeed;
    public float Acceleration => lastAcceleration;
    public float SpeedRatio => Mathf.Clamp01(currentSpeed / Mathf.Max(moveSpeed * speedMultiplier, 0.01f));
    public float TurnRatio => Mathf.Clamp(currentYawRate / Mathf.Max(rotateSpeed * rotScale, 1f), -1f, 1f);

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.isKinematic = true;

        if (profile == null)
        {
            Debug.LogWarning($"{name}: chưa gán BoatHandlingProfile, dùng thông số mặc định.", this);
            profile = ScriptableObject.CreateInstance<BoatHandlingProfile>();
        }

        sensors = new BoatSteeringSensors(rb);
        referenceMass = rb.mass;
        RefreshMassScales();

        health = GetComponent<BoatHealth>();
    }

    private void OnEnable()
    {
        if (health != null)
            health.OnDamaged += HandleDamaged;
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDamaged -= HandleDamaged;
        knockback.applied = knockback.pending = Vector3.zero;
    }

    public void ApplyKnockback(Vector3 pushDirection, float strength01)
    {
        if (knockbackSpeed <= 0f || !GameController.BattleStarted)
            return;

        Vector3 away = pushDirection;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
            return;   // không rõ hướng (vd TakeDamage không có nguồn) thì bỏ qua

        // Tàu nặng (rotScale nhỏ) bị đẩy ít hơn
        float maxSpeed = knockbackSpeed * rotScale;
        knockback.pending += away.normalized * (maxSpeed * Mathf.Clamp(strength01, 0.4f, 2f));
        knockback.pending = Vector3.ClampMagnitude(knockback.pending, maxSpeed * 2.5f);
    }

    private void HandleDamaged(float damageRatio, Vector3 hitPoint, Vector3 hitDirection)
    {
        float strength = knockbackFullDamageRatio > 0f ? damageRatio / knockbackFullDamageRatio : 1f;
        ApplyKnockback(hitDirection, strength);
    }

    public void ApplyLevel(BoatData data, int level)
    {
        BoatLevelStats s = data.GetLevel(level);
        // Mỗi loại tàu có cảm giác lái riêng (Banger lao gắt, Gunner lượn...)
        if (data.handlingProfile != null)
            profile = data.handlingProfile;
        moveSpeed = s.moveSpeed;
        rotateSpeed = s.rotateSpeed;
        referenceMass = data.GetReferenceMass();
        RefreshMassScales();
    }

    public void SetSpeedMultiplier(float multiplier)
    {
        speedMultiplier = Mathf.Clamp(multiplier, 0.05f, MaxSpeedMultiplier);
    }

    public void Steer(Vector3 direction)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            hasInput = false;
            return;
        }

        desiredDirection = direction.normalized;
        arrivalFactor = 1f;
        hasInput = true;
    }

    public bool MoveTowards(Vector3 point, float stoppingDistance = 0.5f)
    {
        Vector3 to = point - transform.position;
        to.y = 0f;
        float distance = to.magnitude;

        if (distance <= stoppingDistance)
        {
            Stop();
            return true;
        }

        Steer(to);
        // Giảm tốc dần trong slowingDistance cuối, giữ tối thiểu để vẫn tới được đích
        arrivalFactor = profile.slowingDistance > 0.01f
            ? Mathf.Clamp((distance - stoppingDistance) / profile.slowingDistance, MinArrivalFactor, 1f)
            : 1f;
        return false;
    }

    public void Stop() => hasInput = false;

    public void ResetMovement()
    {
        hasInput = false;
        desiredDirection = Vector3.zero;
        speedMultiplier = 1f;
        arrivalFactor = 1f;
        CombatTarget = null;
        SuppressUnstick = false;
        currentSpeed = 0f;
        currentYawRate = 0f;
        lastAcceleration = 0f;
        heading.hasPrev = false;
        heading.rate = 0f;
        ResetStuck();
        knockback.applied = knockback.pending = Vector3.zero;
        Modifier?.OnMovementReset();
        sensors?.ResetPass();

        if (rb == null)
            rb = GetComponent<Rigidbody>();
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (TryGetComponent(out BoatVisualFx fx))
            fx.ResetVisual();
    }

    private void FixedUpdate()
    {
        bool started = GameController.BattleStarted;

        if (started != wasBattleStarted)
        {
            wasBattleStarted = started;
            rb.isKinematic = !started;   // chưa đánh thì kinematic, đánh thì vật lý
            currentSpeed = 0f;
            currentYawRate = 0f;
            ResetStuck();
            knockback.applied = knockback.pending = Vector3.zero;
        }

        if (!started)
            return;

        CameraController cameraController = GameController.Instance.CameraController;
        bounds = cameraController != null ? cameraController.BattleBounds : null;

        float dt = Time.fixedDeltaTime;
        BoatHandlingProfile p = profile;
        Vector3 forward = rb.rotation * Vector3.forward;
        Vector3 right = rb.rotation * Vector3.right;
        // Bỏ phần đẩy lùi đã cộng ở bước trước: lái chỉ đo vận tốc của chính nó
        Vector3 velocity = rb.linearVelocity - knockback.applied;

        // Tốc độ thực tế dọc thân tàu (đã gồm va chạm): tàu bị chặn sẽ không "tích" tốc độ ảo
        float measuredForward = Vector3.Dot(velocity, forward);
        float lateral = Vector3.Dot(velocity, right);
        float brakeDecel = p.deceleration * rotScale;

        sensors.AvoidEnemies = AvoidEnemies;
        sensors.IgnoreEnemy = CombatTarget;
        sensors.UpdateAvoidance(p, measuredForward);
        float obstacleSpeedLimit = sensors.SenseObstacle(p, measuredForward, brakeDecel, out Vector3 obstacleSteer);

        // Tàu khác chắn trước mũi: phanh theo khoảng trống còn lại và bẻ lái vòng qua, không húc nó đi
        obstacleSpeedLimit = Mathf.Min(obstacleSpeedLimit,
            AheadSpeedLimit(sensors.AllyAheadFree, sensors.AllyAheadClosing, brakeDecel),
            AheadSpeedLimit(sensors.EnemyAheadFree, sensors.EnemyAheadClosing, brakeDecel));
        aheadSteer = Vector3.Lerp(aheadSteer, sensors.AllyAheadSteer + sensors.EnemyAheadSteer,
                                  1f - Mathf.Exp(-dt / AheadSteerSmoothing));
        obstacleSteer += aheadSteer * AheadSteerWeight;

        if (Modifier != null)
            Modifier.ModifyObstacle(MakeContext(dt, measuredForward, brakeDecel), ref obstacleSpeedLimit, ref obstacleSteer);

        steeringAroundBoat = hasInput
            && aheadSteer.sqrMagnitude > 0.01f;

        UpdateStuck(measuredForward, dt);

        float angle = hasInput ? UpdateHeading(forward, obstacleSteer, dt, measuredForward, brakeDecel) : 0f;
        if (!hasInput)
        {
            heading.hasPrev = false;
            heading.rate = 0f;
        }

        float maxSpeed = moveSpeed * speedMultiplier;
        if (Modifier != null)
            measuredForward = Modifier.ModifyMeasuredForward(MakeContext(dt, measuredForward, brakeDecel), measuredForward);
        UpdateSpeed(p, angle, maxSpeed, measuredForward, obstacleSpeedLimit, brakeDecel, dt);

        if (Modifier != null && Modifier.TryOverrideSpeed(MakeContext(dt, measuredForward, brakeDecel), measuredForward, out float overriddenSpeed))
            currentSpeed = overriddenSpeed;

        Quaternion newRotation = UpdateYaw(p, angle, maxSpeed, measuredForward, brakeDecel, dt);

        // Vận tốc: dọc thân = currentSpeed, ngang thì tắt dần (trôi nhẹ khi cua/va chạm)
        lateral *= Mathf.Exp(-p.lateralGrip * dt);
        Vector3 v = newRotation * Vector3.forward * currentSpeed + newRotation * Vector3.right * lateral;

        // Đẩy lùi khi trúng đòn: tắt dần theo thời gian, cộng các cú đánh mới
        knockback.applied = knockback.applied * Mathf.Exp(-knockbackDecay * dt) + knockback.pending;
        knockback.pending = Vector3.zero;
        if (knockback.applied.sqrMagnitude < 0.0004f)
            knockback.applied = Vector3.zero;
        v += knockback.applied;

        // Không cho tàu chạy ra khỏi tầm nhìn của camera trận đấu
        boundsLimited = false;
        if (bounds != null)
        {
            Vector3 clamped = bounds.ClampVelocity(rb.position, v);
            boundsLimited = (clamped - v).sqrMagnitude > 0.01f;
            v = clamped;
        }
        rb.linearVelocity = new Vector3(v.x, velocity.y, v.z);
    }

    private float UpdateHeading(Vector3 forward, Vector3 obstacleSteer, float dt, float measuredForward, float brakeDecel)
    {
        Vector3 dir = desiredDirection;
        Vector3 avoidance = sensors.Avoidance;
        if (Modifier != null)
            avoidance = Modifier.ModifyAvoidance(MakeContext(dt, measuredForward, brakeDecel), avoidance);
        Vector3 push = avoidance * profile.avoidanceWeight + obstacleSteer;
        if (push.sqrMagnitude > 0.0001f)
        {
            Vector3 blended = dir + push;
            blended.y = 0f;
            if (blended.sqrMagnitude > 0.0001f)
                dir = blended.normalized;
        }

        // Gần mép tầm nhìn: bẻ lái quay vào trong
        if (bounds != null)
            dir = bounds.SteerInside(rb.position, dir);

        float angle = Vector3.SignedAngle(forward, dir, Vector3.up);

        // Đo tốc độ xoay của hướng mong muốn để bù trước (feed-forward)
        float desiredHeading = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        if (heading.hasPrev)
        {
            float rate = Mathf.DeltaAngle(heading.prevDesired, desiredHeading) / dt;
            if (Mathf.Abs(rate) > 360f) rate = heading.rate; // nhảy hướng (đổi mục tiêu...): bỏ qua
            heading.rate = Mathf.Lerp(heading.rate, rate, 1f - Mathf.Exp(-dt / FeedForwardSmoothing));
        }
        heading.prevDesired = desiredHeading;
        heading.hasPrev = true;

        if (Modifier != null)
            angle = Modifier.ModifyHeadingAngle(MakeContext(dt, measuredForward, brakeDecel), angle);

        return IsUnsticking ? ApplyUnstickAngle(angle) : angle;
    }

    // Tốc độ tối đa để còn kịp phanh trước tàu chắn mũi. closing > 0: nó đang lao về phía mình;
    // closing < 0: nó đang chạy cùng chiều -> được bám theo với đúng tốc độ của nó
    private static float AheadSpeedLimit(float free, float closing, float brakeDecel)
    {
        if (free == float.MaxValue)
            return float.MaxValue;
        return Mathf.Max(Mathf.Sqrt(2f * brakeDecel * AheadBrakeFactor * free) - closing, 0f);
    }

    // Bị chặn mũi (muốn chạy mà không nhúc nhích được) đủ lâu thì chuyển sang gỡ kẹt
    private void UpdateStuck(float measuredForward, float dt)
    {
        if (!hasInput)
        {
            ResetStuck();
            return;
        }

        if (IsUnsticking)
        {
            stuck.unstickTimer -= dt;
            return;
        }

        stuck.retryTimer -= dt;

        // Bị mép tầm nhìn giữ lại (tự bẻ lái quay vào) hoặc đang được yêu cầu đứng yên: không tính là kẹt
        if (boundsLimited || SuppressUnstick)
        {
            stuck.blockedTimer = 0f;
            stuck.waitingTimer = 0f;
            return;
        }

        // Kẹt thật: được phép chạy mà không nhúc nhích (bị vật khác tì vào mũi)
        bool blocked = stuck.allowedSpeed > StuckMinWantedSpeed
                     && measuredForward < Mathf.Min(StuckSpeedMax, stuck.allowedSpeed * StuckSpeedRatio);
        stuck.blockedTimer = blocked ? stuck.blockedTimer + dt : 0f;

        // Đứng chờ: muốn chạy nhưng tự phanh vì có tàu / vật cản trước mũi. Cứ đứng xoay mũi tìm đường,
        // chỉ khi chờ quá lâu mới lùi
        bool waiting = !blocked && stuck.wantedSpeed > StuckMinWantedSpeed && measuredForward < StuckSpeedMax;
        stuck.waitingTimer = waiting ? stuck.waitingTimer + dt : 0f;

        if (stuck.blockedTimer >= (stuck.retryTimer > 0f ? StuckRetryDelay : StuckDelay)
            || stuck.waitingTimer >= (stuck.retryTimer > 0f ? WaitingStuckDelay * 2f : WaitingStuckDelay))
        {
            stuck.blockedTimer = 0f;
            stuck.waitingTimer = 0f;
            stuck.unstickSide = 0;
            stuck.unstickTimer = UnstickReverseTime + UnstickSwingTime;
            stuck.retryTimer = StuckRetryWindow;
        }
    }

    private float ApplyUnstickAngle(float angle)
    {
        // Chốt chiều xoay một lần: về phía hướng mong muốn; nếu hướng đó ngay trước mũi (đang bị chặn)
        // thì né sang mạn ít bị chắn hơn, mặc định bên phải (hai tàu đối đầu cùng rẽ phải sẽ tránh được nhau)
        if (stuck.unstickSide == 0)
        {
            if (Mathf.Abs(angle) > 15f)
                stuck.unstickSide = angle > 0f ? 1 : -1;
            else
                stuck.unstickSide = sensors.RightBlock > sensors.LeftBlock + 0.1f ? -1 : 1;
        }

        // Đang lùi: xoay mạnh về chiều đã chốt để mũi tàu thoát khỏi vật chặn
        if (IsReversing)
            return stuck.unstickSide * Mathf.Max(angle * stuck.unstickSide, UnstickTurnAngle);

        // Lùi xong: chạy chếch khỏi hướng mong muốn để vòng qua thay vì đâm lại vào chỗ cũ
        return Mathf.DeltaAngle(0f, angle + stuck.unstickSide * UnstickSwingAngle);
    }

    private void ResetStuck()
    {
        stuck = default;
        aheadSteer = Vector3.zero;
    }

    private void UpdateSpeed(BoatHandlingProfile p, float angle, float maxSpeed, float measuredForward,
                             float obstacleSpeedLimit, float brakeDecel, float dt)
    {
        BoatSpeedTuning tuning = new BoatSpeedTuning
        {
            acceleration = p.acceleration,
            speedResponse = p.speedResponse,
            sharpTurnSpeedFactor = p.sharpTurnSpeedFactor
        };
        if (Modifier != null)
            Modifier.ModifySpeedTuning(MakeContext(dt, measuredForward, brakeDecel), ref tuning);

        float targetSpeed = 0f;
        if (hasInput)
        {
            float turnT = Mathf.InverseLerp(SharpTurnStartAngle, SharpTurnFullAngle, Mathf.Abs(angle));
            targetSpeed = maxSpeed * Mathf.Lerp(1f, tuning.sharpTurnSpeedFactor, turnT) * arrivalFactor;
        }
        stuck.wantedSpeed = targetSpeed;
        targetSpeed = Mathf.Min(targetSpeed, obstacleSpeedLimit);
        stuck.allowedSpeed = targetSpeed;

        float speedDiff = targetSpeed - measuredForward;
        bool speedingUp = speedDiff > 0f;
        float maxRate = speedingUp ? tuning.acceleration * rotScale : brakeDecel;
        float response = speedingUp ? tuning.speedResponse : p.brakeResponse;
        float newSpeed = measuredForward + Mathf.Clamp(speedDiff * response, -maxRate, maxRate) * dt;

        // Vật cản phía trước: bám theo đường phanh, nhưng không phanh gắt hơn deceleration
        newSpeed = Mathf.Min(newSpeed, Mathf.Max(obstacleSpeedLimit, measuredForward - brakeDecel * dt));
        newSpeed = Mathf.Max(newSpeed, 0f); // không chạy lùi

        // Ngoại lệ duy nhất: đang gỡ kẹt thì lùi chậm để mũi tàu có chỗ xoay
        if (IsReversing)
            newSpeed = Mathf.MoveTowards(currentSpeed, -moveSpeed * UnstickReverseRatio, brakeDecel * dt);

        lastAcceleration = (newSpeed - currentSpeed) / dt;
        currentSpeed = newSpeed;
    }

    private Quaternion UpdateYaw(BoatHandlingProfile p, float angle, float maxSpeed, float measuredForward, float brakeDecel, float dt)
    {
        // Khả năng bẻ lái tăng theo tốc độ: tàu không xoay tròn tại chỗ
        float speedRatio = maxSpeed > 0.01f ? Mathf.Clamp01(currentSpeed / (maxSpeed * 0.5f)) : 0f;
        float turnAuthority = Mathf.Lerp(MinTurnAuthority, 1f, speedRatio);

        // Logic riêng của từng loại tàu (vd: Gunner cua theo cung tròn) tự tính tốc độ xoay tối đa
        float maxYawRate = Modifier != null
            ? Modifier.GetMaxYawRate(MakeContext(dt, measuredForward, brakeDecel), turnAuthority)
            : rotateSpeed * rotScale * turnAuthority;

        // Đang vòng qua tàu chắn mũi (thường đã phanh chậm lại): vẫn cho xoay đủ để thoát ra
        if (steeringAroundBoat)
            maxYawRate = Mathf.Max(maxYawRate, rotateSpeed * rotScale * MinTurnAuthority);

        // Đang gỡ kẹt: cho xoay gần như tại chỗ, không phụ thuộc tốc độ
        if (IsUnsticking)
            maxYawRate = Mathf.Max(maxYawRate, rotateSpeed * rotScale * UnstickTurnAuthority);

        float targetYawRate = hasInput
            ? Mathf.Clamp(angle * p.turnResponsiveness + heading.rate * p.turnFeedForward, -maxYawRate, maxYawRate)
            : 0f;
        float maxYawChange = p.yawAcceleration * rotScale;
        currentYawRate += Mathf.Clamp((targetYawRate - currentYawRate) * p.yawResponse, -maxYawChange, maxYawChange) * dt;

        rb.angularVelocity = new Vector3(0f, currentYawRate * Mathf.Deg2Rad, 0f);
        return rb.rotation * Quaternion.Euler(0f, currentYawRate * dt, 0f);
    }

    private BoatMovementContext MakeContext(float dt, float measuredForward, float brakeDecel)
    {
        return new BoatMovementContext
        {
            profile = profile,
            sensors = sensors,
            dt = dt,
            moveSpeed = moveSpeed,
            maxSpeed = moveSpeed * speedMultiplier,
            rotateSpeed = rotateSpeed,
            rotScale = rotScale,
            radiusScale = radiusScale,
            brakeDecel = brakeDecel,
            measuredForward = measuredForward,
            currentSpeed = currentSpeed,
            currentYawRate = currentYawRate
        };
    }

    public void RefreshMassScales()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();

        float sqrt = Mathf.Sqrt(Mathf.Max(rb.mass, 0.01f) / Mathf.Max(referenceMass, 0.01f));
        radiusScale = Mathf.Clamp(sqrt, 0.5f, 3f);
        rotScale = Mathf.Clamp(1f / sqrt, 0.4f, 2f);
    }

    private void OnDrawGizmosSelected()
    {
        if (profile == null || profile.avoidanceRadius <= 0f) return;
        Gizmos.color = new Color(0f, 0.6f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, profile.avoidanceRadius);
    }
}
