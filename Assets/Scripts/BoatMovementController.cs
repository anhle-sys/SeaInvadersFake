using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BoatMovementController : MonoBehaviour
{
    // Các thông số hiếm khi cần chỉnh
    private const float FeedForwardSmoothing = 0.15f; // giây
    private const float MinTurnAuthority = 0.3f;      // khả năng bẻ lái khi gần đứng yên (0..1)
    private const float SharpTurnStartAngle = 20f;    // từ góc lệch này bắt đầu giảm tốc khi cua
    private const float SharpTurnFullAngle = 120f;    // góc lệch giảm tốc nhiều nhất
    private const float MinArrivalFactor = 0.15f;
    private const float BlockedMaxSpeed = 0.5f;       // tốc độ dưới mức này mà bị chặn -> coi như đã tới đích
    private const float BlockedMaxAngle = 45f;        // đích phải nằm trong góc này phía trước mũi tàu
    // Kẹt (vd: bị tàu khác ép, đối đầu nhau): muốn chạy mà không nhích được và đang phải bẻ lái nhiều
    private const float StallSpeed = 0.3f;            // tốc độ dọc thân dưới mức này coi như đứng yên
    private const float StallAngle = 30f;             // chỉ coi là kẹt khi hướng cần đi lệch quá góc này
    private const float StallTime = 0.4f;             // kẹt liên tục bao lâu thì lùi ra
    private const float ReverseTime = 0.7f;           // thời gian lùi
    private const float ReverseSpeedRatio = 0.2f;     // tốc độ lùi = moveSpeed × hệ số này

    [Tooltip("Thông số cảm giác lái. Tạo mới bằng Create > Boats > Handling Profile")]
    [SerializeField] private BoatHandlingProfile profile;

    private float moveSpeed = 5f;
    private float rotateSpeed = 120f;

    private Rigidbody rb;
    private BoatSteeringSensors sensors;
    private Vector3 desiredDirection;
    private bool hasInput;
    private float steerRadius;        // > 0: cua theo cung tròn bán kính này; 0: dùng rotateSpeed
    private float speedMultiplier = 1f;
    private float arrivalFactor = 1f; // 0..1, giảm khi gần đích
    private float currentSpeed;
    private float currentYawRate;     // độ/giây
    private float lastAcceleration;
    private bool wasBattleStarted;
    private float prevDesiredHeading;
    private bool hasPrevHeading;
    private float headingRate;    
    private float referenceMass = 1f;
    private float radiusScale = 1f;
    private float rotScale = 1f;
    private float stallTimer;
    private float reverseTimer;       // > 0: đang lùi để thoát kẹt

    public bool IsMoving => hasInput || Mathf.Abs(currentSpeed) > 0.1f;
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
    }

    public void ApplyLevel(BoatData data, int level)
    {
        BoatLevelStats s = data.GetLevel(level);
        moveSpeed = s.moveSpeed;
        rotateSpeed = s.rotateSpeed;
        referenceMass = data.GetReferenceMass();
        RefreshMassScales();
    }

    public void SetSpeedMultiplier(float multiplier)
    {
        // Không cần làm mượt ở đây: gia tốc/phanh đã đưa tốc độ về dần
        speedMultiplier = Mathf.Clamp(multiplier, 0.05f, 1f);
    }

    public void Steer(Vector3 direction) => Steer(direction, 0f);

    public void Steer(Vector3 direction, float turnRadius)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            hasInput = false;
            return;
        }

        desiredDirection = direction.normalized;
        steerRadius = turnRadius;
        arrivalFactor = 1f;
        hasInput = true;
    }

    public bool MoveTowards(Vector3 point, float stoppingDistance = 0.5f)
    {
        Vector3 to = point - transform.position;
        to.y = 0f;
        float distance = to.magnitude;

        // Đích nằm sau vật cản (vd: trụ đứng trên đảo đá): tàu đã sát bờ và không thể tới gần hơn
        // -> coi như đã tới, thay vì ép vào đá và kẹt mãi ở trạng thái "đang di chuyển"
        if (distance <= stoppingDistance || IsBlockedBefore(to, distance))
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

    private bool IsBlockedBefore(Vector3 toTarget, float distance)
    {
        if (sensors == null || !sensors.BlockedAhead || currentSpeed > BlockedMaxSpeed)
            return false;

        // Vật cản phải nằm giữa tàu và đích, và mũi tàu đang hướng về đích
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return sensors.ObstacleDistance < distance
            && Vector3.Angle(forward, toTarget) <= BlockedMaxAngle;
    }

    public void ResetMovement()
    {
        hasInput = false;
        desiredDirection = Vector3.zero;
        steerRadius = 0f;
        speedMultiplier = 1f;
        arrivalFactor = 1f;
        currentSpeed = 0f;
        currentYawRate = 0f;
        lastAcceleration = 0f;
        hasPrevHeading = false;
        headingRate = 0f;
        stallTimer = 0f;
        reverseTimer = 0f;

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
        bool started = GameController.Instance.IsBattleStarted;

        if (started != wasBattleStarted)
        {
            wasBattleStarted = started;
            rb.isKinematic = !started;   // chưa đánh thì kinematic, đánh thì vật lý
            currentSpeed = 0f;
            currentYawRate = 0f;
        }

        if (!started)
            return;

        float dt = Time.fixedDeltaTime;
        BoatHandlingProfile p = profile;
        Vector3 forward = rb.rotation * Vector3.forward;
        Vector3 right = rb.rotation * Vector3.right;
        Vector3 velocity = rb.linearVelocity;

        // Tốc độ thực tế dọc thân tàu (đã gồm va chạm): tàu bị chặn sẽ không "tích" tốc độ ảo
        float measuredForward = Vector3.Dot(velocity, forward);
        float lateral = Vector3.Dot(velocity, right);
        float brakeDecel = p.deceleration * rotScale;

        sensors.UpdateAvoidance(p, dt);
        float obstacleSpeedLimit = sensors.SenseObstacle(p, measuredForward, brakeDecel, out Vector3 obstacleSteer);

        float angle = hasInput ? UpdateHeading(forward, obstacleSteer, dt) : 0f;
        if (!hasInput)
        {
            hasPrevHeading = false;
            headingRate = 0f;
        }

        float maxSpeed = moveSpeed * speedMultiplier;
        UpdateStall(angle, measuredForward, dt);
        UpdateSpeed(p, angle, maxSpeed, measuredForward, obstacleSpeedLimit, brakeDecel, dt);
        Quaternion newRotation = UpdateYaw(p, angle, maxSpeed, dt);

        // Vận tốc: dọc thân = currentSpeed, ngang thì tắt dần (trôi nhẹ khi cua/va chạm)
        lateral *= Mathf.Exp(-p.lateralGrip * dt);
        Vector3 v = newRotation * Vector3.forward * currentSpeed + newRotation * Vector3.right * lateral;
        rb.linearVelocity = new Vector3(v.x, velocity.y, v.z);
    }

    private float UpdateHeading(Vector3 forward, Vector3 obstacleSteer, float dt)
    {
        Vector3 dir = desiredDirection;
        Vector3 push = sensors.Avoidance * profile.avoidanceWeight + obstacleSteer;
        if (push.sqrMagnitude > 0.0001f)
        {
            Vector3 blended = dir + push;
            blended.y = 0f;
            if (blended.sqrMagnitude > 0.0001f)
                dir = blended.normalized;
        }

        float angle = Vector3.SignedAngle(forward, dir, Vector3.up);

        // Đo tốc độ xoay của hướng mong muốn để bù trước (feed-forward)
        float heading = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        if (hasPrevHeading)
        {
            float rate = Mathf.DeltaAngle(prevDesiredHeading, heading) / dt;
            if (Mathf.Abs(rate) > 360f) rate = headingRate; // nhảy hướng (đổi mục tiêu...): bỏ qua
            headingRate = Mathf.Lerp(headingRate, rate, 1f - Mathf.Exp(-dt / FeedForwardSmoothing));
        }
        prevDesiredHeading = heading;
        hasPrevHeading = true;

        // Mục tiêu gần như ngay sau lưng: giữ chiều đang quay, tránh lật trái/phải liên tục
        if (Mathf.Abs(angle) > 170f && Mathf.Abs(currentYawRate) > 1f)
            angle = Mathf.Abs(angle) * Mathf.Sign(currentYawRate);

        return angle;
    }

    private void UpdateSpeed(BoatHandlingProfile p, float angle, float maxSpeed, float measuredForward,
                             float obstacleSpeedLimit, float brakeDecel, float dt)
    {
        bool reversing = reverseTimer > 0f;
        float reverseSpeed = moveSpeed * ReverseSpeedRatio;

        float targetSpeed = 0f;
        if (reversing)
        {
            targetSpeed = -reverseSpeed;
        }
        else if (hasInput)
        {
            float turnT = Mathf.InverseLerp(SharpTurnStartAngle, SharpTurnFullAngle, Mathf.Abs(angle));
            targetSpeed = maxSpeed * Mathf.Lerp(1f, p.sharpTurnSpeedFactor, turnT) * arrivalFactor;
        }
        targetSpeed = Mathf.Min(targetSpeed, obstacleSpeedLimit);

        float speedDiff = targetSpeed - measuredForward;
        bool speedingUp = speedDiff > 0f;
        float maxRate = speedingUp ? p.acceleration * rotScale : brakeDecel;
        float response = speedingUp ? p.speedResponse : p.brakeResponse;
        float newSpeed = measuredForward + Mathf.Clamp(speedDiff * response, -maxRate, maxRate) * dt;

        // Vật cản phía trước: bám theo đường phanh, nhưng không phanh gắt hơn deceleration
        newSpeed = Mathf.Min(newSpeed, Mathf.Max(obstacleSpeedLimit, measuredForward - brakeDecel * dt));
        newSpeed = Mathf.Max(newSpeed, reversing ? -reverseSpeed : 0f); // chỉ lùi khi đang thoát kẹt

        lastAcceleration = (newSpeed - currentSpeed) / dt;
        currentSpeed = newSpeed;
    }

    // Muốn chạy + phải bẻ lái nhiều mà thân tàu không nhích (bị tàu khác ép) -> lùi ra một đoạn
    // ngắn để có chỗ quay đầu. Va chạm vật lý vẫn giữ nguyên, chỉ là tàu không kẹt mãi.
    private void UpdateStall(float angle, float measuredForward, float dt)
    {
        if (reverseTimer > 0f)
        {
            reverseTimer -= dt;
            if (!hasInput) reverseTimer = 0f;
            return;
        }

        bool stalled = hasInput && measuredForward < StallSpeed && Mathf.Abs(angle) > StallAngle;
        stallTimer = stalled ? stallTimer + dt : 0f;

        if (stallTimer >= StallTime)
        {
            stallTimer = 0f;
            reverseTimer = ReverseTime;
        }
    }

    private Quaternion UpdateYaw(BoatHandlingProfile p, float angle, float maxSpeed, float dt)
    {
        float maxYawRate = steerRadius > 0f
            ? Mathf.Max(currentSpeed, maxSpeed * 0.3f) / Mathf.Max(steerRadius * radiusScale, 0.1f) * Mathf.Rad2Deg
            : rotateSpeed * rotScale;

        // Khả năng bẻ lái tăng theo tốc độ: tàu không xoay tròn tại chỗ
        float speedRatio = maxSpeed > 0.01f ? Mathf.Clamp01(Mathf.Abs(currentSpeed) / (maxSpeed * 0.5f)) : 0f;
        maxYawRate *= Mathf.Lerp(MinTurnAuthority, 1f, speedRatio);

        // Sàn tốc độ quay: cua theo bán kính thì tốc độ quay tỉ lệ với tốc độ chạy, nên tàu bị ép
        // đứng yên (va chạm) sẽ gần như không quay được -> vẫn cho xoay chậm tại chỗ để thoát ra
        maxYawRate = Mathf.Max(maxYawRate, rotateSpeed * rotScale * MinTurnAuthority);

        float targetYawRate = hasInput
            ? Mathf.Clamp(angle * p.turnResponsiveness + headingRate * p.turnFeedForward, -maxYawRate, maxYawRate)
            : 0f;
        float maxYawChange = p.yawAcceleration * rotScale;
        currentYawRate += Mathf.Clamp((targetYawRate - currentYawRate) * p.yawResponse, -maxYawChange, maxYawChange) * dt;

        rb.angularVelocity = new Vector3(0f, currentYawRate * Mathf.Deg2Rad, 0f);
        return rb.rotation * Quaternion.Euler(0f, currentYawRate * dt, 0f);
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
