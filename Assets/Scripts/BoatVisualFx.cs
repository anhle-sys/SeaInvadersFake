using DG.Tweening;
using UnityEngine;

[RequireComponent(typeof(BoatMovementController))]
public class BoatVisualFx : MonoBehaviour
{
    private const float AccelerationPitch = -0.8f;  // ngóc/chúi thêm theo gia tốc (độ cho mỗi đơn vị/giây²)
    private const float MaxAccelerationPitch = 4f;
    private const float SmoothTime = 0.3f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorDimId = Shader.PropertyToID("_ColorDim");          // vùng tối của FlatKit Stylized Surface
    private static readonly int ColorDimExtraId = Shader.PropertyToID("_ColorDimExtra");

    [SerializeField] private Transform visual;
    [SerializeField] private float bobHeight = 0.08f;
    [SerializeField] private float bobDuration = 1.2f;
    [SerializeField] private float movingPitch = -4f;
    [SerializeField] private float maxRoll = 10f;

    [Header("Trúng đòn")]
    [Tooltip("Góc nghiêng tối đa khi trúng đòn (độ), nghiêng ra xa phía bị bắn")]
    [SerializeField] private float hitTilt = 9f;
    [Tooltip("Thân tàu bị đẩy lùi bao xa (đơn vị) theo hướng đạn")]
    [SerializeField] private float hitJolt = 0.18f;
    [Tooltip("Độ cứng lò xo hồi về: lớn = rung nhanh, ngắn")]
    [SerializeField] private float hitSpringStiffness = 260f;
    [Tooltip("Độ tắt dần: nhỏ = lắc lư nhiều lần")]
    [SerializeField] private float hitSpringDamping = 13f;
    [Tooltip("Sát thương bằng tỉ lệ này của máu tối đa thì hiệu ứng ở mức 100%")]
    [SerializeField] private float fullStrengthDamageRatio = 0.1f;

    [Header("Nháy màu")]
    [Tooltip("Màu nháy (HDR > 1 để sáng rực lên)")]
    [ColorUsage(false, true)]
    [SerializeField] private Color flashColor = new Color(1.9f, 0.9f, 0.8f);
    [Tooltip("Mức pha màu nháy lúc mạnh nhất (1 = phủ kín màu nháy, mất texture)")]
    [Range(0f, 1f)] [SerializeField] private float flashIntensity = 0.6f;
    [SerializeField] private float flashDuration = 0.16f;

    [Header("VFX va chạm")]
    [Tooltip("Prefab mảnh vụn / tia lửa tại điểm trúng (dùng pool)")]
    [SerializeField] private GameObject hitVfxPrefab;
    [Tooltip("Prefab mảnh gỗ văng ra tại điểm trúng (dùng pool). Trục Z của prefab hướng ra ngoài thân tàu")]
    [SerializeField] private GameObject woodVfxPrefab;

    private BoatMovementController movement;
    private BoatHealth health;
    private Collider hull;
    private Tween bobTween;
    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private float pitch, pitchVel;
    private float roll, rollVel;

    // Lò xo trúng đòn (cộng thêm lên tư thế lái)
    private float hitPitch, hitPitchVel;
    private float hitRoll, hitRollVel;
    private Vector2 hitOffset, hitOffsetVel;   // xz trong không gian cha của visual

    // Nháy màu
    private Renderer[] renderers;
    private Color[][] originalColors;          // [renderer][0: base, 1: dim, 2: dimExtra]
    private MaterialPropertyBlock mpb;
    private float flashT;

    private void Awake()
    {
        movement = GetComponent<BoatMovementController>();
        health = GetComponent<BoatHealth>();
        hull = GetComponent<Collider>();

        if (visual == null)
        {
            Debug.LogError($"{name}: chưa gán Visual cho BoatVisualFx, hiệu ứng hình ảnh sẽ bị tắt.", this);
            return;
        }

        baseLocalPosition = visual.localPosition;
        baseLocalRotation = visual.localRotation;
        CacheRenderers();
    }

    private void OnEnable()
    {
        StartBobbing();
        if (health != null)
            health.OnDamaged += HandleDamaged;
    }

    private void OnDisable()
    {
        bobTween?.Kill();
        if (health != null)
            health.OnDamaged -= HandleDamaged;
        flashT = 0f;
        ClearFlash();
    }

    private void LateUpdate()
    {
        if (visual == null)
            return;

        float dt = Time.deltaTime;
        float speed01 = movement.SpeedRatio;

        // Ngóc mũi theo tốc độ + theo gia tốc (tăng tốc ngóc lên, phanh chúi xuống)
        float targetPitch = movingPitch * speed01
                          + Mathf.Clamp(movement.Acceleration * AccelerationPitch, -MaxAccelerationPitch, MaxAccelerationPitch);

        // Nghiêng thân vào trong vòng cua, mạnh hơn khi chạy nhanh
        float targetRoll = -movement.TurnRatio * maxRoll * speed01;

        pitch = Mathf.SmoothDamp(pitch, targetPitch, ref pitchVel, SmoothTime);
        roll = Mathf.SmoothDamp(roll, targetRoll, ref rollVel, SmoothTime);

        UpdateHitSpring(dt);

        visual.localRotation = baseLocalRotation * Quaternion.Euler(pitch + hitPitch, 0f, roll + hitRoll);

        // Chỉ ghi x/z: trục y do tween nhấp nhô (DOLocalMoveY) điều khiển
        Vector3 lp = visual.localPosition;
        visual.localPosition = new Vector3(baseLocalPosition.x + hitOffset.x, lp.y, baseLocalPosition.z + hitOffset.y);

        UpdateFlash(dt);
    }

    public void ResetVisual()
    {
        if (visual == null) return;

        bobTween?.Kill();
        pitch = pitchVel = roll = rollVel = 0f;
        hitPitch = hitPitchVel = hitRoll = hitRollVel = 0f;
        hitOffset = hitOffsetVel = Vector2.zero;
        flashT = 0f;
        ClearFlash();
        visual.localRotation = baseLocalRotation;
        visual.localPosition = baseLocalPosition;
        StartBobbing();
    }

    /// <summary>Phản ứng trúng đòn. strength01: 1 = cú đánh chuẩn; sourcePosition: nơi đạn/vụ nổ tới.</summary>
    public void PlayHit(float strength01, Vector3 sourcePosition)
    {
        if (visual == null)
            return;

        float s = Mathf.Clamp(strength01, 0.4f, 2f);

        // Hướng đạn tới trong không gian của cha visual (bỏ trục Y)
        Transform space = visual.parent != null ? visual.parent : transform;
        Vector3 from = space.InverseTransformDirection(sourcePosition - transform.position);
        from.y = 0f;
        Vector2 dir = from.sqrMagnitude > 0.0001f ? new Vector2(from.x, from.z).normalized : Random.insideUnitCircle.normalized;

        // Vận tốc ban đầu để biên độ đỉnh ~ giá trị mong muốn (lò xo tắt dần ~ ω)
        float kick = Mathf.Sqrt(hitSpringStiffness) * 1.4f;

        // Bị bắn mạn phải (x+) -> thân nghiêng sang trái (z+); bị bắn ở mũi (z+) -> mũi ngóc lên (x-)
        hitRollVel += dir.x * hitTilt * s * kick;
        hitPitchVel += -dir.y * hitTilt * s * kick;
        hitOffsetVel += -dir * hitJolt * s * kick;

        flashT = 1f;

        SpawnHitVfx(sourcePosition);
    }

    private void HandleDamaged(float damageRatio, Vector3 sourcePosition)
    {
        float strength = fullStrengthDamageRatio > 0f ? damageRatio / fullStrengthDamageRatio : 1f;
        PlayHit(strength, sourcePosition);
    }

    private void UpdateHitSpring(float dt)
    {
        if (dt <= 0f) return;
        // Chia nhỏ bước để lò xo cứng không bị nổ khi FPS thấp
        int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 8);
        float h = dt / steps;
        float k = hitSpringStiffness, c = hitSpringDamping;

        for (int i = 0; i < steps; i++)
        {
            hitPitchVel += (-k * hitPitch - c * hitPitchVel) * h;
            hitPitch += hitPitchVel * h;
            hitRollVel += (-k * hitRoll - c * hitRollVel) * h;
            hitRoll += hitRollVel * h;
            hitOffsetVel += (-k * hitOffset - c * hitOffsetVel) * h;
            hitOffset += hitOffsetVel * h;
        }
    }

    private void SpawnHitVfx(Vector3 sourcePosition)
    {
        if (hitVfxPrefab == null && woodVfxPrefab == null)
            return;

        // Điểm trúng: điểm gần nguồn nhất trên thân tàu (vụ nổ ở xa thì vẫn nổ trên vỏ tàu)
        Vector3 point = hull != null ? hull.ClosestPoint(sourcePosition) : sourcePosition;
        Vector3 outward = point - (hull != null ? hull.bounds.center : transform.position);
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f)
        {
            outward = sourcePosition - transform.position;
            outward.y = 0f;
        }
        Quaternion rot = outward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(outward.normalized, Vector3.up) : Quaternion.identity;

        if (hitVfxPrefab != null)
            SimplePool2.Spawn(hitVfxPrefab, point, rot);
        if (woodVfxPrefab != null)
            SimplePool2.Spawn(woodVfxPrefab, point, rot);
    }

    private void CacheRenderers()
    {
        renderers = visual.GetComponentsInChildren<Renderer>(true);
        originalColors = new Color[renderers.Length][];
        mpb = new MaterialPropertyBlock();

        for (int i = 0; i < renderers.Length; i++)
        {
            Material m = renderers[i].sharedMaterial;
            originalColors[i] = new[]
            {
                m != null && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white,
                m != null && m.HasProperty(ColorDimId) ? m.GetColor(ColorDimId) : Color.clear,
                m != null && m.HasProperty(ColorDimExtraId) ? m.GetColor(ColorDimExtraId) : Color.clear,
            };
        }
    }

    private void UpdateFlash(float dt)
    {
        if (flashT <= 0f || renderers == null)
            return;

        flashT = Mathf.Max(0f, flashT - dt / Mathf.Max(flashDuration, 0.01f));
        if (flashT <= 0f)
        {
            ClearFlash();
            return;
        }

        float t = flashT * flashT * flashIntensity;   // tắt nhanh lúc đầu, kéo đuôi
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            Material m = r.sharedMaterial;
            if (m == null) continue;

            r.GetPropertyBlock(mpb);
            if (m.HasProperty(BaseColorId)) mpb.SetColor(BaseColorId, Color.LerpUnclamped(originalColors[i][0], flashColor, t));
            if (m.HasProperty(ColorDimId)) mpb.SetColor(ColorDimId, Color.LerpUnclamped(originalColors[i][1], flashColor, t));
            if (m.HasProperty(ColorDimExtraId)) mpb.SetColor(ColorDimExtraId, Color.LerpUnclamped(originalColors[i][2], flashColor, t));
            r.SetPropertyBlock(mpb);
        }
    }

    // Bỏ property block để renderer quay lại SRP Batcher (không tốn draw call khi không nháy)
    private void ClearFlash()
    {
        if (renderers == null) return;
        foreach (Renderer r in renderers)
            if (r != null) r.SetPropertyBlock(null);
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
