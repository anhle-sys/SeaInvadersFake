using System.Collections.Generic;
using UnityEngine;

public class BangerCombat : BoatCombatBase
{
    private BangerData data;
    private BoatStats stats;
    private bool exploded;

    public override float CombatSpeedMultiplier =>
        data != null ? data.chargeSpeedMultiplier : 1f;

    public override void Setup(BoatData boatData, BoatStats boatStats)
    {
        data = boatData as BangerData;
        stats = boatStats;
        if (data == null)
            Debug.LogError($"{name}: BangerCombat cần BangerData!");
    }

    public override void Tick(Transform enemy)
    {
        if (data == null || exploded)
            return;

        Vector3 to = enemy.position - transform.position;
        to.y = 0f;

        if (to.magnitude <= data.triggerDistance)
        {
            Explode();
            return;
        }

        movement.Steer(to);
    }

    // Hai tàu đều có collider vật lý nên đẩy nhau ra: khoảng cách tâm có thể không bao giờ
    // xuống dưới triggerDistance. Chạm vào tàu địch là nổ luôn.
    // Lưu ý: chỉ nhận được callback khi script nằm cùng GameObject với Rigidbody.
    private void OnCollisionEnter(Collision collision) => TryExplodeOnContact(collision);
    private void OnCollisionStay(Collision collision) => TryExplodeOnContact(collision);

    private void TryExplodeOnContact(Collision collision)
    {
        if (data == null || exploded || !GameController.Instance.IsBattleStarted)
            return;

        BoatTargetController other = collision.collider.GetComponentInParent<BoatTargetController>();
        if (other == null || other == targetController || other.Team == targetController.Team)
            return;

        Explode();
    }

    private void Explode()
    {
        exploded = true;

        Collider[] cols = Physics.OverlapSphere(
            transform.position,
            data.explosionRadius,
            data.damageLayers,
            QueryTriggerInteraction.Ignore);

        HashSet<IDamageable> damaged = new HashSet<IDamageable>();
        float damage = stats != null ? stats.RollDamage() : data.explosionDamage;

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
