using System.Collections.Generic;
using UnityEngine;

public class BangerCombat : BoatCombatBase, IBoatMovementModifier
{
    private BangerData data;
    private BoatStats stats;
    private bool exploded;

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
        exploded = true;

        Collider[] cols = Physics.OverlapSphere(
            transform.position,
            data.explosionRadius,
            data.damageLayers,
            QueryTriggerInteraction.Ignore);

        HashSet<IDamageable> damaged = new HashSet<IDamageable>();
        float damage = stats != null ? stats.RollDamage() : data.explosionDamage;

        if (primaryTarget != null && primaryTarget.Team != targetController.Team && damaged.Add(primaryTarget))
            primaryTarget.TakeDamage(damage, transform.position);

        foreach (Collider col in cols)
        {
            IDamageable target = col.GetComponentInParent<IDamageable>();

            if (target == null || target.Team == targetController.Team)
                continue;

            if (damaged.Add(target))
                target.TakeDamage(damage, transform.position);
        }

        if (data.explosionPrefab != null)
            Instantiate(data.explosionPrefab, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }
}
