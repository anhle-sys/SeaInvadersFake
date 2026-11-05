using DG.Tweening;
using UnityEngine;

/// <summary>
/// Hiệu ứng hình ảnh của tàu (không ảnh hưởng vật lý):
/// nhấp nhô (DOTween), ngóc/chúi mũi theo tốc độ và gia tốc, nghiêng thân khi cua.
/// Chỉ đọc trạng thái từ BoatMovementController.
/// </summary>
[RequireComponent(typeof(BoatMovementController))]
public class BoatVisualFx : MonoBehaviour
{
    private const float AccelerationPitch = -0.8f;  // ngóc/chúi thêm theo gia tốc (độ cho mỗi đơn vị/giây²)
    private const float MaxAccelerationPitch = 4f;
    private const float SmoothTime = 0.3f;

    [Tooltip("Model con của tàu. KHÔNG phải root có Rigidbody")]
    [SerializeField] private Transform visual;
    [SerializeField] private float bobHeight = 0.08f;
    [SerializeField] private float bobDuration = 1.2f;
    [Tooltip("Âm = ngóc mũi lên khi chạy hết tốc")]
    [SerializeField] private float movingPitch = -4f;
    [Tooltip("Góc nghiêng thân tối đa khi cua")]
    [SerializeField] private float maxRoll = 10f;

    private BoatMovementController movement;
    private Tween bobTween;
    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private float pitch, pitchVel;
    private float roll, rollVel;

    private void Awake()
    {
        movement = GetComponent<BoatMovementController>();
        if (visual == null)
        {
            Debug.LogWarning($"{name}: chưa gán Visual cho BoatVisualFx, hiệu ứng hình ảnh sẽ bị tắt.", this);
            return;
        }

        baseLocalPosition = visual.localPosition;
        baseLocalRotation = visual.localRotation;
    }

    private void OnEnable() => StartBobbing();
    private void OnDisable() => bobTween?.Kill();

    private void LateUpdate()
    {
        if (visual == null)
            return;

        float speed01 = movement.SpeedRatio;

        // Ngóc mũi theo tốc độ + theo gia tốc (tăng tốc ngóc lên, phanh chúi xuống)
        float targetPitch = movingPitch * speed01
                          + Mathf.Clamp(movement.Acceleration * AccelerationPitch, -MaxAccelerationPitch, MaxAccelerationPitch);

        // Nghiêng thân vào trong vòng cua, mạnh hơn khi chạy nhanh
        float targetRoll = -movement.TurnRatio * maxRoll * speed01;

        pitch = Mathf.SmoothDamp(pitch, targetPitch, ref pitchVel, SmoothTime);
        roll = Mathf.SmoothDamp(roll, targetRoll, ref rollVel, SmoothTime);

        visual.localRotation = baseLocalRotation * Quaternion.Euler(pitch, 0f, roll);
    }

    public void ResetVisual()
    {
        if (visual == null) return;

        bobTween?.Kill();
        pitch = pitchVel = roll = rollVel = 0f;
        visual.localRotation = baseLocalRotation;
        visual.localPosition = baseLocalPosition;
        StartBobbing();
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
}
