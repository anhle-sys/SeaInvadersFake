using System.Collections.Generic;
using UnityEngine;

public class BangerCombat : BoatCombatBase
{
    private BangerData data;
    private bool exploded;

    public override float CombatSpeedMultiplier =>
        data != null ? data.chargeSpeedMultiplier : 1f;

    public override void Setup(BoatData boatData)
    {
        data = boatData as BangerData;
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

    private void Explode()
    {
        exploded = true;

        Collider[] cols = Physics.OverlapSphere(
            transform.position,
            data.explosionRadius,
            data.damageLayers,
            QueryTriggerInteraction.Ignore);

        HashSet<IDamageable> damaged = new HashSet<IDamageable>();

        foreach (Collider col in cols)
        {
            IDamageable target = col.GetComponentInParent<IDamageable>();

            if (target == null || target.Team == targetController.Team)
                continue;

            if (damaged.Add(target))
                target.TakeDamage(data.explosionDamage);
        }

        if (data.explosionPrefab != null)
            Instantiate(data.explosionPrefab, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }
}