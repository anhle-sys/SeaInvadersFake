using System.Collections.Generic;
using UnityEngine;

public class BangerCombat : BoatCombatBase, IBoatMovementModifier
{
    private BangerData data;
    private BoatStats stats;
    private bool exploded;

    private bool charging;   // đang lao vào địch: bỏ qua né đồng đội

    public override float CombatSpeedMultiplier =>
        data != null ? data.chargeSpeedMultiplier : 1f;

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

        if (data.chargeInstantSpeed)
        {
            tuning.acceleration = 1000000f;
            tuning.speedResponse = 1f / Mathf.Max(ctx.dt, 0.0001f);
        }

        tuning.sharpTurnSpeedFactor = Mathf.Max(tuning.sharpTurnSpeedFactor, data.chargeSharpTurnSpeedFactor);
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
