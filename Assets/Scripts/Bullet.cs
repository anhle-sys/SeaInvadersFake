using UnityEditor;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class Bullet : MonoBehaviour
{
    [Header("Bullet Settings")]
    [SerializeField] private float speed = 15f;
    [SerializeField] private float damage = 10f;
    [SerializeField] private float lifeTime = 5f;

    private Rigidbody rb;
    private BoatTeam ownerTeam;
    private float timer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public void Init(BoatTeam team, Vector3 targetPosition)
    {
        ownerTeam = team;
        timer = 0f;

        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.001f)
        {
            direction.Normalize();

            transform.rotation = Quaternion.LookRotation(direction);

            rb.linearVelocity = direction * speed;
        }
    }

    private void Update()
    {
        timer += Time.deltaTime;

        if (timer >= lifeTime)
            Despawn();
    }

    private void OnTriggerEnter(Collider other)
    {
        BoatTargetController target =
            other.GetComponentInParent<BoatTargetController>();

        if (target == null)
            return;

        if (target.Team == ownerTeam)
            return;

        BoatHealth health = other.GetComponentInParent<BoatHealth>();

        if (health != null)
            health.TakeDamage(damage);

        Despawn();
    }

    private void Despawn()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        Destroy(gameObject);
    }
}