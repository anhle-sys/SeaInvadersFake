using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class Bullet : MonoBehaviour
{
    [Header("Bullet Settings")]
    [SerializeField] private float speed = 15f;
    [SerializeField] private float lifeTime = 5f;

    private Rigidbody rb;
    private BoatTeam ownerTeam;
    private float damage;
    private float timer;

    public void Init(BoatTeam team, Vector3 targetPosition, float damageAmount) =>
        Init(team, targetPosition, Vector3.zero, damageAmount);

    public void Init(BoatTeam team, Vector3 targetPosition, Vector3 targetVelocity, float damageAmount)
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        ownerTeam = team;
        damage = damageAmount;
        timer = 0f;

        Vector3 aimPoint = PredictIntercept(transform.position, targetPosition, targetVelocity, speed);
        Vector3 direction = aimPoint - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.001f)
        {
            direction.Normalize();

            transform.rotation = Quaternion.LookRotation(direction);

            rb.linearVelocity = direction * speed;
        }
    }

    // Bắn theo hướng 3D (giữ trục y), vd: máy bay bắn chéo từ trên trời xuống
    public void InitDirection(BoatTeam team, Vector3 direction, float damageAmount)
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        ownerTeam = team;
        damage = damageAmount;
        timer = 0f;

        if (direction.sqrMagnitude > 0.001f)
        {
            direction.Normalize();
            transform.rotation = Quaternion.LookRotation(direction);
            rb.linearVelocity = direction * speed;
        }
    }

    public float Speed => speed;

    public static Vector3 PredictIntercept(Vector3 shooter, Vector3 target, Vector3 targetVelocity, float projectileSpeed)
    {
        Vector3 d = target - shooter; d.y = 0f;
        Vector3 v = targetVelocity; v.y = 0f;
        if (v.sqrMagnitude < 0.0001f || projectileSpeed <= 0.01f)
            return target;

        float a = v.sqrMagnitude - projectileSpeed * projectileSpeed;
        float b = 2f * Vector3.Dot(d, v);
        float c = d.sqrMagnitude;
        float t;

        if (Mathf.Abs(a) < 0.0001f)
        {
            t = Mathf.Abs(b) > 0.0001f ? -c / b : 0f;
        }
        else
        {
            float disc = b * b - 4f * a * c;
            if (disc < 0f)
                return target; // không đuổi kịp: bắn thẳng
            float sq = Mathf.Sqrt(disc);
            float t1 = (-b - sq) / (2f * a);
            float t2 = (-b + sq) / (2f * a);
            t = Mathf.Min(t1, t2);
            if (t <= 0f) t = Mathf.Max(t1, t2);
        }

        return t > 0f ? target + v * t : target;
    }

    private void Update()
    {
        timer += Time.deltaTime;

        if (timer >= lifeTime)
            Despawn();
    }

    private void OnTriggerEnter(Collider other)
    {
        // Đã trúng 1 tàu trong cùng bước vật lý / đã trả về pool: không gây sát thương lần nữa
        if (!gameObject.activeInHierarchy)
            return;

        BoatTargetController target =
            other.GetComponentInParent<BoatTargetController>();

        if (target == null)
            return;

        if (target.Team == ownerTeam)
            return;

        BoatHealth health = other.GetComponentInParent<BoatHealth>();

        if (health != null)
        {
            // Hướng bay của đạn (A->B): tàu giật đúng theo hướng này, không phải từ điểm trúng vào tâm tàu
            Vector3 travel = rb != null && rb.linearVelocity.sqrMagnitude > 0.0001f
                ? rb.linearVelocity
                : transform.forward;
            health.TakeDamage(damage, transform.position, travel);
        }

        Despawn();
    }

    private void Despawn()
    {
        // Đạn hết giờ mà chưa từng được Init (rb chưa gán) thì vẫn trả về pool được
        if (rb == null)
            rb = GetComponent<Rigidbody>();

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        SimplePool2.Despawn(gameObject);
    }
}