using System;
using UnityEngine;

public class BoatHealth : MonoBehaviour, IDamageable, IHealthBarTarget
{
    private const float MinDamageRatio = 0.1f;

    [SerializeField] private BoatTargetController targetController;
    [Tooltip("Mở các Fill Region (lộ lỗ thủng) quanh điểm trúng đạn, bán kính theo sát thương. Để trống sẽ tự tìm trong con.")]
    [SerializeField] private RuntimeMeshFillController fillController;
    [SerializeField] private Vector3 barOffset = new Vector3(0, 2f, 0);

    private BoatData data;
    private BoatStats stats;
    private float health;
    private bool isDead;
    private bool registered;

    public event Action<float> OnHealthChanged;
    public event Action OnDied;
    // (tỉ lệ sát thương / máu tối đa, điểm trúng, hướng lực tác động đã chuẩn hóa trên mặt phẳng XZ - có thể = 0)
    public event Action<float, Vector3, Vector3> OnDamaged;
    public bool IsAlly => Team == BoatTeam.Ally;
    public BoatTeam Team => targetController.Team;
    public bool IsDead => isDead;
    public Vector3 BarWorldPosition => transform.position + barOffset;

    // Các tàu đang bật trong scene: GameController đếm tàu còn sống mà không cần FindObjectsByType (tạo mảng mới mỗi lần)
    private static readonly System.Collections.Generic.List<BoatHealth> active = new System.Collections.Generic.List<BoatHealth>();
    public static System.Collections.Generic.IReadOnlyList<BoatHealth> Active => active;

    private void OnEnable() => active.Add(this);
    private void OnDisable() => active.Remove(this);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active.Clear();

    private void Awake()
    {
        // Quên kéo tham chiếu trong Inspector thì tự lấy, tránh NullReference ở Team / IsAlly
        if (targetController == null)
            targetController = GetComponent<BoatTargetController>();

        if (fillController == null)
            fillController = GetComponentInChildren<RuntimeMeshFillController>(true);
    }

    public void Setup(BoatData boatData, BoatStats boatStats)
    {
        data = boatData;
        stats = boatStats;
        health = boatStats.MaxHealth;
        isDead = false;

        // Không đăng ký 2 thanh máu nếu Setup bị gọi lại
        if (!registered && GameController.Instance != null && GameController.Instance.HealthBarManager != null)
        {
            GameController.Instance.HealthBarManager.Register(this);
            registered = true;
        }
    }

    public void UpdateStats(BoatStats boatStats)
    {
        if (boatStats == null || isDead)
            return;

        float ratio = stats != null && stats.MaxHealth > 0f ? health / stats.MaxHealth : 1f;
        stats = boatStats;
        health = stats.MaxHealth * ratio;
        OnHealthChanged?.Invoke(Mathf.Clamp01(ratio));
    }

    public void TakeDamage(float amount) => TakeDamage(amount, transform.position);

    // Vụ nổ / nguồn không có hướng bay: đẩy từ nguồn ra xa tâm tàu
    public void TakeDamage(float amount, Vector3 sourcePosition) =>
        ApplyDamage(amount, sourcePosition, transform.position - sourcePosition, true);

    public void TakeDamage(float amount, Vector3 sourcePosition, Vector3 hitDirection) =>
        ApplyDamage(amount, sourcePosition, hitDirection, false);

    private void ApplyDamage(float amount, Vector3 sourcePosition, Vector3 hitDirection, bool isExplosion)
    {
        if (isDead || stats == null)
            return;

        if (UnityEngine.Random.value * 100f < stats.Evasion)
            return;

        amount = Mathf.Max(
            amount - stats.Armor,
            amount * MinDamageRatio
        );

        Vector3 impactDirection = hitDirection; // hướng 3D gốc cho Fill Region (hitDirection bên dưới bị ép y = 0)

        health -= amount;

        health = Mathf.Max(health, 0f);

        OnHealthChanged?.Invoke(
            Mathf.Clamp01(health / Mathf.Max(stats.MaxHealth, 0.01f))
        );

        hitDirection.y = 0f;
        hitDirection = hitDirection.sqrMagnitude > 0.0001f ? hitDirection.normalized : Vector3.zero;

        OnDamaged?.Invoke(
            amount / Mathf.Max(stats.MaxHealth, 0.01f),
            sourcePosition,
            hitDirection
        );

        if (health > 0f && fillController != null)
            fillController.ApplyHit(
                sourcePosition,
                impactDirection,
                amount / Mathf.Max(stats.MaxHealth, 0.01f),
                isExplosion);

        if (health <= 0f)
            Die();
    }

    private void Die()
    {
        isDead = true;
        OnDied?.Invoke();

        if (Team == BoatTeam.Enemy && data != null && data.killReward > 0)
        {
            int reward = Mathf.RoundToInt(data.killReward * GoldBuff.Multiplier);
            // Không ghi đĩa mỗi lần giết (giật trên mobile); GameController lưu khi pause/thoát
            GameData.AddGold(reward);
        }

        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (!isDead)
        {
            isDead = true;
            OnDied?.Invoke();
        }

        OnDied = null;
        OnHealthChanged = null;
        OnDamaged = null;
    }
}
