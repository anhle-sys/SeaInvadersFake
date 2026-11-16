using System.Collections.Generic;
using UnityEngine;

public class BangerCombat : BoatCombatBase, IBoatMovementModifier
{
    private BangerData data;
    private BoatStats stats;
    private bool exploded;

    // Buffer dùng chung cho mọi vụ nổ: không cấp phát Collider[] / HashSet mới mỗi lần
    private static readonly Collider[] s_explosionHits = new Collider[128];
    private static readonly HashSet<IDamageable> s_damaged = new HashSet<IDamageable>();
    private static readonly Dictionary<IDamageable, float> s_nearest = new Dictionary<IDamageable, float>();
    private static bool s_explosionBusy;
    private static readonly Dictionary<GameObject, bool> s_pooledFx = new Dictionary<GameObject, bool>();

    private bool charging;   // đang lao vào địch: bỏ qua né đồng đội

    // Giữ nguyên tốc độ khi thấy địch: Banger vốn đã chạy nhanh, không tăng vọt tốc độ gây giật
    public override float CombatSpeedMultiplier => 1f;

    public override void Setup(BoatData boatData, BoatStats boatStats)
    {
        data = boatData as BangerData;
        stats = boatStats;
        if (data == null)
            Debug.LogError($"{name}: BangerCombat cần BangerData!");
        else if (movement != null)
            movement.Modifier = this;
    }

    public override void OnEnterCombat(Transform enemy) => charging = true;

    public override void OnExitCombat() => charging = false;

    public Vector3 ModifyAvoidance(in BoatMovementContext ctx, Vector3 avoidance)
        => charging ? Vector3.zero : avoidance;

    public void ModifySpeedTuning(in BoatMovementContext ctx, ref BoatSpeedTuning tuning)
    {
        if (!charging || data == null)
            return;

        // Không chậm lại khi bẻ lái về phía địch
        tuning.sharpTurnSpeedFactor = Mathf.Max(tuning.sharpTurnSpeedFactor, data.chargeSharpTurnSpeedFactor);
    }

    public void OnMovementReset() => charging = false;

    public override void Tick(Transform enemy)
    {
        if (data == null || exploded)
            return;

        Vector3 to = enemy.position - transform.position;
        to.y = 0f;

        if (to.magnitude <= data.triggerDistance)
        {
            Explode(enemy.GetComponentInParent<IDamageable>());
            return;
        }

        // Ngắm đón đầu: lao theo đường thẳng tới điểm gặp địch thay vì đuổi theo đuôi
        Vector3 aim = enemy.position;
        if (data.chargeLeadTarget && enemy.TryGetComponent(out Rigidbody enemyRb))
        {
            float chargeSpeed = Mathf.Max(movement.CurrentSpeed, 0.1f);
            aim = Bullet.PredictIntercept(transform.position, enemy.position, enemyRb.linearVelocity, chargeSpeed);
        }

        Vector3 dir = aim - transform.position;
        dir.y = 0f;
        movement.Steer(dir.sqrMagnitude > 0.001f ? dir : to);
    }

    private void OnCollisionEnter(Collision collision) => TryExplodeOnContact(collision);
    private void OnCollisionStay(Collision collision) => TryExplodeOnContact(collision);

    private void TryExplodeOnContact(Collision collision)
    {
        if (data == null || exploded || !GameController.BattleStarted)
            return;

        BoatTargetController other = collision.collider.GetComponentInParent<BoatTargetController>();
        if (other == null || other == targetController || other.Team == targetController.Team)
            return;

        Explode(other.GetComponentInParent<IDamageable>());
    }

    // primaryTarget: tàu địch đã kích nổ (tới đủ gần / va chạm). Nó luôn dính sát thương dù thân tàu chưa lọt vào
    // explosionRadius - trước đây triggerDistance (tâm-tâm) lớn hơn explosionRadius nên có lúc nổ mà không trúng ai
    private void Explode(IDamageable primaryTarget)
    {
        if (exploded)
            return;
        exploded = true;

        // Cảm tử: tắt ngay va chạm + di chuyển để không còn húc / bị đẩy trong frame còn lại trước khi Destroy
        foreach (Collider c in GetComponentsInChildren<Collider>())
            c.enabled = false;
        if (movement != null)
            movement.enabled = false;

        try
        {
            DealExplosionDamage(primaryTarget);

            if (data.explosionPrefab != null)
                SpawnExplosionFx();
        }
        finally
        {
            // Luôn tự hủy, kể cả khi gây sát thương bị lỗi giữa chừng (trước đây lỗi -> Banger kẹt lại sống mà không nổ nữa)
            Destroy(gameObject);
        }
    }

// Prefab có PooledParticleFx thì lấy từ SimplePool2 (tự trả pool), nếu không giữ nguyên Instantiate như cũ.
    private void SpawnExplosionFx()
    {
        GameObject prefab = data.explosionPrefab;
        if (!s_pooledFx.TryGetValue(prefab, out bool pooled))
        {
            pooled = prefab.GetComponent<PooledParticleFx>() != null;
            s_pooledFx[prefab] = pooled;
        }

        if (pooled)
            SimplePool2.Spawn(prefab, transform.position, Quaternion.identity);
        else
            Instantiate(prefab, transform.position, Quaternion.identity);
    }


private void DealExplosionDamage(IDamageable primaryTarget)
    {
        // Dùng buffer chung; nếu bị gọi lồng (TakeDamage kích nổ Banger khác) thì dùng bản cấp phát riêng cho an toàn.
        bool shared = !s_explosionBusy;
        Collider[] cols;
        int count;
        HashSet<IDamageable> damaged;

        if (shared)
        {
            s_explosionBusy = true;
            count = Physics.OverlapSphereNonAlloc(
                transform.position,
                data.explosionRadius,
                s_explosionHits,
                data.damageLayers,
                QueryTriggerInteraction.Ignore);
            cols = s_explosionHits;
            s_damaged.Clear();
            damaged = s_damaged;
        }
        else
        {
            cols = Physics.OverlapSphere(
                transform.position,
                data.explosionRadius,
                data.damageLayers,
                QueryTriggerInteraction.Ignore);
            count = cols.Length;
            damaged = new HashSet<IDamageable>();
        }

        Dictionary<IDamageable, float> nearest = shared ? s_nearest : new Dictionary<IDamageable, float>();

        try
        {
            float damage = stats != null ? stats.RollDamage() : data.GetLevel(0).damage;
            Vector3 center = transform.position;

            if (primaryTarget != null && primaryTarget.Team != targetController.Team && damaged.Add(primaryTarget))
                primaryTarget.TakeDamage(damage, center);

            // Gom theo tàu: một tàu nhiều collider chỉ tính 1 lần, lấy khoảng cách tới collider gần tâm nổ nhất
            nearest.Clear();
            for (int i = 0; i < count; i++)
            {
                IDamageable target = cols[i].GetComponentInParent<IDamageable>();

                if (target == null || target.Team == targetController.Team || damaged.Contains(target))
                    continue;

                // bounds.ClosestPoint an toàn với mọi loại collider (kể cả MeshCollider không convex)
                Vector3 offset = cols[i].bounds.ClosestPoint(center) - center;
                offset.y = 0f;
                float dist = offset.magnitude;

                if (!nearest.TryGetValue(target, out float best) || dist < best)
                    nearest[target] = dist;
            }

            // Sát thương lan: giảm tuyến tính từ 100% ở tâm xuống edgeDamageMultiplier ở mép vùng nổ
            float radius = Mathf.Max(data.explosionRadius, 0.01f);
            foreach (KeyValuePair<IDamageable, float> pair in nearest)
            {
                if (!damaged.Add(pair.Key))
                    continue;

                float falloff = Mathf.Lerp(1f, data.edgeDamageMultiplier, Mathf.Clamp01(pair.Value / radius));
                pair.Key.TakeDamage(damage * falloff, center);
            }
        }
        finally
        {
            if (shared)
            {
                System.Array.Clear(s_explosionHits, 0, count);
                s_damaged.Clear();
                s_nearest.Clear();
                s_explosionBusy = false;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (data == null)
            return;

        Gizmos.color = new Color(1f, 0.4f, 0f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, data.explosionRadius);
        Gizmos.color = new Color(1f, 1f, 0f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, data.triggerDistance);
    }
}
