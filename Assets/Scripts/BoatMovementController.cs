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

    [Tooltip("Thông số cảm giác lái. Tạo mới bằng Create > Boats > Handling Profile")]
    [SerializeField] private BoatHandlingProfile profile;

    private float moveSpeed = 5f;
    private float rotateSpeed = 120f;

    private Rigidbody rb;
    private BoatSteeringSensors sensors;
    private Vector3 desiredDirection;
    private bool hasInput;
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
        // Không cần làm mượt ở đây: gia tốc/phanh đã đưa tốc độ về dần.
        // > 1 cho phép tàu vượt moveSpeed (vd: Banger lao vào địch)
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
        currentSpeed = 0f;
        currentYawRate = 0f;
        lastAcceleration = 0f;
        hasPrevHeading = false;
        headingRate = 0f;
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

        sensors.UpdateAvoidance(p, measuredForward);
        float obstacleSpeedLimit = sensors.SenseObstacle(p, measuredForward, brakeDecel, out Vector3 obstacleSteer);

        // Logic riêng của từng loại tàu (vd: Gunner nhường / đẩy đồng đội) chỉnh giới hạn tốc độ và hướng né
        if (Modifier != null)
            Modifier.ModifyObstacle(MakeContext(dt, measuredForward, brakeDecel), ref obstacleSpeedLimit, ref obstacleSteer);

        float angle = hasInput ? UpdateHeading(forward, obstacleSteer, dt, measuredForward, brakeDecel) : 0f;
        if (!hasInput)
        {
            hasPrevHeading = false;
            headingRate = 0f;
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

        return Modifier != null
            ? Modifier.ModifyHeadingAngle(MakeContext(dt, measuredForward, brakeDecel), angle)
            : angle;
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
        targetSpeed = Mathf.Min(targetSpeed, obstacleSpeedLimit);

        float speedDiff = targetSpeed - measuredForward;
        bool speedingUp = speedDiff > 0f;
        float maxRate = speedingUp ? tuning.acceleration * rotScale : brakeDecel;
        float response = speedingUp ? tuning.speedResponse : p.brakeResponse;
        float newSpeed = measuredForward + Mathf.Clamp(speedDiff * response, -maxRate, maxRate) * dt;

        // Vật cản phía trước: bám theo đường phanh, nhưng không phanh gắt hơn deceleration
        newSpeed = Mathf.Min(newSpeed, Mathf.Max(obstacleSpeedLimit, measuredForward - brakeDecel * dt));
        newSpeed = Mathf.Max(newSpeed, 0f); // không chạy lùi

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

        float targetYawRate = hasInput
            ? Mathf.Clamp(angle * p.turnResponsiveness + headingRate * p.turnFeedForward, -maxYawRate, maxYawRate)
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
