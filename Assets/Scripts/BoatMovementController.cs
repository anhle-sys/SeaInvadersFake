using DG.Tweening;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BoatMovementController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [Tooltip("Tốc độ quay (độ/giây) khi Steer thường")]
    [SerializeField] private float rotateSpeed = 120f;

    [Header("Mass")]
    [Tooltip("Tàu nặng hơn mass chuẩn: quay chậm hơn, vòng cua rộng hơn")]
    [SerializeField] private bool massAffectsRotation = true;

    [Header("Visual (DOTween)")]
    [Tooltip("Model con của tàu. KHÔNG phải root có Rigidbody")]
    [SerializeField] private Transform visual;
    [SerializeField] private float bobHeight = 0.08f;
    [SerializeField] private float bobDuration = 1.2f;
    [Tooltip("Âm = ngóc mũi lên khi chạy")]
    [SerializeField] private float movingPitch = -4f;
    [SerializeField] private float pitchSmoothTime = 0.35f;

    // --- Movement ---
    private Rigidbody rb;
    private Vector3 desiredDirection;
    private bool hasInput;
    private float steerRadius;        // > 0: cua theo cung tròn bán kính này; 0: dùng rotateSpeed
    private float speedMultiplier = 1f;
    private float referenceMass = 1f;
    private float radiusScale = 1f;   // cache: tàu nặng -> vòng cua rộng
    private float rotScale = 1f;      // cache: tàu nặng -> quay chậm

    // --- Visual ---
    private Tween bobTween;
    private Tween pitchTween;
    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private bool visualMoving;

    public bool IsMoving => hasInput;
    private bool wasBattleStarted;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.isKinematic = true;

        referenceMass = rb.mass;
        RefreshMassScales();

        if (visual == null)
        {
            Debug.LogWarning($"{name}: chưa gán Visual, hiệu ứng DOTween sẽ bị tắt.", this);
            return;
        }

        baseLocalPosition = visual.localPosition;
        baseLocalRotation = visual.localRotation;
    }

    private void OnEnable() => StartBobbing();
    private void OnDisable() => KillVisualTweens();
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
        hasInput = true;
    }

    public bool MoveTowards(Vector3 point, float stoppingDistance = 0.5f)
    {
        Vector3 to = point - transform.position;
        to.y = 0f;

        if (to.sqrMagnitude <= stoppingDistance * stoppingDistance)
        {
            Stop();
            return true;
        }

        Steer(to);
        return false;
    }

    public void Stop() => hasInput = false;

    public void ResetMovement()
    {
        hasInput = false;
        desiredDirection = Vector3.zero;
        steerRadius = 0f;
        speedMultiplier = 1f;

        if (rb == null)
            rb = GetComponent<Rigidbody>();
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        ResetVisual();
    }
    private void FixedUpdate()
    {
        bool started = GameController.Instance.IsBattleStarted;

        if (started != wasBattleStarted)
        {
            wasBattleStarted = started;
            rb.isKinematic = !started;   // chưa đánh thì kinematic, đánh thì vật lý
        }

        if (!started) return;

        float yVel = rb.linearVelocity.y;
        SetVisualMoving(hasInput);

        if (!hasInput)
        {
            rb.angularVelocity = Vector3.zero;
            rb.linearVelocity = new Vector3(0f, yVel, 0f);
            return;
        }

        float speed = moveSpeed * speedMultiplier;

        float turnSpeedDeg = steerRadius > 0f
            ? speed / Mathf.Max(steerRadius * radiusScale, 0.1f) * Mathf.Rad2Deg
            : rotateSpeed * rotScale;

        Quaternion newRotation = Quaternion.RotateTowards(
            rb.rotation,
            Quaternion.LookRotation(desiredDirection, Vector3.up),
            turnSpeedDeg * Time.fixedDeltaTime);

        rb.MoveRotation(newRotation);
        rb.angularVelocity = Vector3.zero;

        Vector3 v = newRotation * Vector3.forward * speed;
        rb.linearVelocity = new Vector3(v.x, yVel, v.z);
    }

    public void RefreshMassScales()
    {
        float massRatio = Mathf.Max(rb.mass, 0.01f) / Mathf.Max(referenceMass, 0.01f);

        if (massAffectsRotation)
        {
            float sqrt = Mathf.Sqrt(massRatio);
            radiusScale = Mathf.Clamp(sqrt, 0.5f, 3f);
            rotScale = Mathf.Clamp(1f / sqrt, 0.4f, 2f);
        }
        else
        {
            radiusScale = 1f;
            rotScale = 1f;
        }
    }
    private void SetVisualMoving(bool moving)
    {
        if (visual == null || moving == visualMoving) return;
        visualMoving = moving;

        float pitch = moving ? movingPitch : 0f;

        pitchTween?.Kill();
        pitchTween = visual
            .DOLocalRotateQuaternion(baseLocalRotation * Quaternion.Euler(pitch, 0f, 0f), pitchSmoothTime)
            .SetEase(Ease.InOutSine)
            .SetLink(gameObject);
    }

    private void StartBobbing()
    {
        if (visual == null) return;

        bobTween?.Kill();
        visual.localPosition = baseLocalPosition + Vector3.down * bobHeight;

        bobTween = visual
            .DOLocalMoveY(baseLocalPosition.y + bobHeight, bobDuration)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetDelay(Random.value * bobDuration)
            .SetLink(gameObject);
    }

    private void KillVisualTweens()
    {
        bobTween?.Kill();
        pitchTween?.Kill();
    }

    private void ResetVisual()
    {
        if (visual == null) return;

        KillVisualTweens();
        visualMoving = false;
        visual.localRotation = baseLocalRotation;
        visual.localPosition = baseLocalPosition;
        StartBobbing();
    }
}