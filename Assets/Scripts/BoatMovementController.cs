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
    // Chọn chiều quay đầu: cua gắt mà mạn phía đó bị tàu đồng đội chắn thì quay vòng sang phía trống
    private const float TurnChoiceAngle = 60f;        // góc lệch từ mức này mới cân nhắc đổi chiều quay
    private const float TurnReleaseAngle = 25f;       // góc lệch dưới mức này thì bỏ chiều đã chọn
    private const float SideBlockThreshold = 0.15f;   // mức bị chắn tối thiểu để đổi chiều
    private const float TurnHoldTime = 0.6f;          // giữ chiều đã chọn tối thiểu bao lâu (chống lật qua lại)
    // Kẹt (bị đồng đội ép / chắn trước mũi): muốn chạy mà không nhích được -> lùi ra để có chỗ quay đầu
    private const float AllyPassWeight = 1.5f;        // độ mạnh bẻ lái vòng qua đồng đội chắn trước mũi
    private const float StallSpeed = 0.5f;            // tốc độ dọc thân dưới mức này coi như đứng yên
    private const float StallAngle = 30f;             // hướng cần đi lệch quá góc này mà không nhích được là kẹt
    private const float StallTime = 0.5f;             // kẹt liên tục bao lâu thì lùi
    private const float ReverseTime = 0.8f;           // thời gian lùi
    private const float ReverseSpeedRatio = 0.3f;     // tốc độ lùi = moveSpeed × hệ số này

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
    private int turnSide;             // 0: tự do, +1: quay phải, -1: quay trái
    private float turnHoldTimer;
    private float stallTimer;
    private float reverseTimer;       // > 0: đang lùi để thoát kẹt
    private bool yielding;            // đang phanh nhường đồng đội chắn trước mũi

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
        steerRadius = 0f;
        speedMultiplier = 1f;
        arrivalFactor = 1f;
        currentSpeed = 0f;
        currentYawRate = 0f;
        lastAcceleration = 0f;
        hasPrevHeading = false;
        headingRate = 0f;
        turnSide = 0;
        turnHoldTimer = 0f;
        stallTimer = 0f;
        reverseTimer = 0f;
        yielding = false;
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

        // Đồng đội nằm trên đường chạy: phanh kịp trước nó thay vì húc đẩy nó đi
        float allyFree = sensors.AllyAheadFree;
        yielding = allyFree < float.MaxValue && Mathf.Sqrt(2f * brakeDecel * 0.8f * allyFree) < moveSpeed * speedMultiplier;
        if (yielding)
            obstacleSpeedLimit = Mathf.Min(obstacleSpeedLimit, Mathf.Sqrt(2f * brakeDecel * 0.8f * allyFree));
        obstacleSteer += sensors.AllyAheadSteer * AllyPassWeight;

        float angle = hasInput ? UpdateHeading(forward, obstacleSteer, dt) : 0f;
        if (!hasInput)
        {
            hasPrevHeading = false;
            headingRate = 0f;
        }

        float maxSpeed = moveSpeed * speedMultiplier;
        UpdateStall(angle, measuredForward, allyFree, dt);
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

        return ChooseTurnSide(angle, dt);
    }

    // Cua gắt: chọn chiều quay rồi giữ đến khi gần đúng hướng. Mạn phía muốn quay bị tàu đồng đội
    // chắn thì quay vòng sang phía còn trống (góc > 180°) thay vì cố ép mũi vào tàu kia.
    private float ChooseTurnSide(float angle, float dt)
    {
        float absAngle = Mathf.Abs(angle);
        turnHoldTimer -= dt;

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
            else if (absAngle > 170f && Mathf.Abs(currentYawRate) > 1f)
                turnSide = currentYawRate > 0f ? 1 : -1;   // mục tiêu ngay sau lưng: giữ chiều đang quay
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

    private void UpdateSpeed(BoatHandlingProfile p, float angle, float maxSpeed, float measuredForward,
                             float obstacleSpeedLimit, float brakeDecel, float dt)
    {
        bool reversing = reverseTimer > 0f;
        float reverseSpeed = moveSpeed * ReverseSpeedRatio;

        float targetSpeed = 0f;
        if (reversing)
        {
            targetSpeed = -reverseSpeed;
            obstacleSpeedLimit = float.MaxValue;   // vật cản/đồng đội phía trước không cản việc lùi
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

    // Muốn chạy mà thân tàu không nhích (bị đồng đội ép, hoặc đồng đội chắn sát mũi) trong khi phải
    // bẻ lái nhiều -> lùi ra một đoạn ngắn để có chỗ quay đầu. Va chạm vật lý vẫn giữ nguyên.
    private void UpdateStall(float angle, float measuredForward, float allyFree, float dt)
    {
        if (reverseTimer > 0f)
        {
            reverseTimer -= dt;
            if (!hasInput || sensors.AllyBehind) reverseTimer = 0f;
            return;
        }

        bool blockedByAlly = allyFree <= 0.05f;
        bool stalled = hasInput && measuredForward < StallSpeed
                       && (Mathf.Abs(angle) > StallAngle || blockedByAlly);
        stallTimer = stalled ? stallTimer + dt : 0f;

        if (stallTimer >= StallTime && !sensors.AllyBehind)
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

        // Đang né / nhường / lùi khỏi đồng đội (thường gần đứng yên): vẫn cho xoay đủ để thoát ra
        if (turnSide != 0 || yielding || reverseTimer > 0f)
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
