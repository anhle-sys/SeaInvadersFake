using UnityEngine;

public class BoatPlane : MonoBehaviour
{
    private BoatTeam team;
    private Transform target;
    private CarrierData data;
    private BoatStats stats;

    private float lifeTimer;
    private float attackTimer;

    public void Init(BoatTeam planeTeam, Transform enemy, CarrierData carrierData, BoatStats carrierStats)
    {
        team = planeTeam;
        target = enemy;
        data = carrierData;
        stats = carrierStats;
        lifeTimer = data.planeLifetime;
    }

    private void Update()
    {
        if (data == null)
            return;

        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        if (attackTimer > 0f)
            attackTimer -= Time.deltaTime;

        if (target != null)
        {
            Vector3 to = target.position - transform.position;
            to.y = 0f;

            if (to.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(to, Vector3.up),
                    data.planeTurnSpeed * Time.deltaTime);
            }

            TryAttack(to);
        }

        transform.position += transform.forward * data.planeSpeed * Time.deltaTime;
    }

    private void TryAttack(Vector3 toTarget)
    {
        if (attackTimer > 0f)
            return;

        if (toTarget.magnitude > data.planeAttackRange)
            return;

        if (Vector3.Angle(transform.forward, toTarget) > 15f)
            return;

        attackTimer = data.planeAttackInterval;

        if (data.planeBulletPrefab == null)
        {
            Debug.LogWarning($"{data.name}: chưa gán planeBulletPrefab, máy bay không bắn được.", data);
            return;
        }

        GameObject bullet = SimplePool2.Spawn(data.planeBulletPrefab, transform.position, transform.rotation);
        Bullet b = bullet.GetComponent<Bullet>();
        if (b != null)
        {
            Rigidbody targetRb = target.GetComponent<Rigidbody>();
            b.Init(team, target.position, targetRb != null ? targetRb.linearVelocity : Vector3.zero, stats.RollDamage());
        }
    }
}