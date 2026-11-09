using UnityEngine;

/// <summary>
/// Máy bay của tàu sân bay: cất cánh, bay lên độ cao flyHeight rồi bay vòng quanh mục tiêu
/// và bắn đạn chéo từ trên trời xuống. Gắn lên prefab máy bay (CarrierData.planePrefab).
/// Tốc độ, tốc độ xoay, thời gian sống, nhịp bắn lấy từ CarrierData.
/// </summary>
public class Fly : MonoBehaviour
{
    [Header("Bay")]
    [Tooltip("Độ cao bay (so với mặt biển y = seaLevel)")]
    [SerializeField] private float flyHeight = 10f;
    [SerializeField] private float seaLevel = 0f;
    [Tooltip("Tốc độ bay lên (đơn vị/giây) khi cất cánh")]
    [SerializeField] private float climbSpeed = 5f;
    [Tooltip("Góc ngóc mũi tối đa khi đang bay lên / hạ xuống (chỉ để nhìn)")]
    [SerializeField] private float maxPitch = 30f;
    [Tooltip("Góc nghiêng cánh tối đa khi cua (chỉ để nhìn)")]
    [SerializeField] private float maxBank = 35f;

    [Header("Bắn")]
    [Tooltip("Điểm bắn đạn. Để trống = vị trí máy bay")]
    [SerializeField] private Transform muzzle;
    [Tooltip("Chỉ bắn khi đã lên tới độ cao này (tỉ lệ so với flyHeight)")]
    [Range(0f, 1f)] [SerializeField] private float minFireHeightRatio = 0.8f;
    [Tooltip("Chỉ bắn khi mục tiêu nằm trong nón phía trước mũi (độ, tính trên mặt phẳng ngang)")]
    [SerializeField] private float fireConeAngle = 30f;

    private BoatTeam team;
    private Transform target;
    private Rigidbody targetRb;
    private CarrierData data;
    private BoatStats stats;

    private float lifeTimer;
    private float attackTimer;
    private float yawRate;   // độ/giây, để nghiêng cánh khi cua

    public void Init(BoatTeam planeTeam, Transform enemy, CarrierData carrierData, BoatStats carrierStats)
    {
        team = planeTeam;
        data = carrierData;
        stats = carrierStats;
        lifeTimer = data.planeLifetime;
        attackTimer = 0f;
        SetTarget(enemy);
    }

    public void SetTarget(Transform enemy)
    {
        target = enemy;
        targetRb = enemy != null ? enemy.GetComponent<Rigidbody>() : null;
    }

    private float CruiseY => seaLevel + flyHeight;

    private void Update()
    {
        if (data == null)
            return;

        float dt = Time.deltaTime;

        lifeTimer -= dt;
        if (lifeTimer <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        if (attackTimer > 0f)
            attackTimer -= dt;

        Vector3 pos = transform.position;

        // Hướng bay ngang (yaw): bám về phía mục tiêu, không có thì bay thẳng
        Vector3 flatForward = transform.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 toTarget = Vector3.zero;
        if (target != null)
        {
            toTarget = target.position - pos;
            toTarget.y = 0f;
        }

        float yawDelta = 0f;
        if (toTarget.sqrMagnitude > 0.001f)
        {
            float angle = Vector3.SignedAngle(flatForward, toTarget, Vector3.up);
            yawDelta = Mathf.Clamp(angle, -data.planeTurnSpeed * dt, data.planeTurnSpeed * dt);
            flatForward = Quaternion.AngleAxis(yawDelta, Vector3.up) * flatForward;
        }
        yawRate = dt > 0f ? yawDelta / dt : 0f;

        // Độ cao: bay lên dần tới CruiseY
        float vertical = Mathf.Clamp(CruiseY - pos.y, -climbSpeed * dt, climbSpeed * dt);

        pos += flatForward * data.planeSpeed * dt;
        pos.y += vertical;
        transform.position = pos;

        // Xoay để nhìn: ngóc mũi khi bay lên, nghiêng cánh khi cua
        float climbRate = dt > 0f ? vertical / dt : 0f;
        float pitch = -Mathf.Clamp(Mathf.Atan2(climbRate, Mathf.Max(data.planeSpeed, 0.01f)) * Mathf.Rad2Deg, -maxPitch, maxPitch);
        float bank = -Mathf.Clamp(yawRate / Mathf.Max(data.planeTurnSpeed, 1f), -1f, 1f) * maxBank;
        transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up) * Quaternion.Euler(pitch, 0f, bank);

        if (target != null)
            TryAttack(flatForward, toTarget);
    }

    private void TryAttack(Vector3 flatForward, Vector3 toTargetFlat)
    {
        if (attackTimer > 0f)
            return;

        if (transform.position.y < seaLevel + flyHeight * minFireHeightRatio)
            return;

        // planeAttackRange tính theo khoảng cách ngang: máy bay ở trên cao vẫn bắn được
        if (toTargetFlat.magnitude > data.planeAttackRange)
            return;

        if (Vector3.Angle(flatForward, toTargetFlat) > fireConeAngle)
            return;

        if (data.planeBulletPrefab == null)
        {
            Debug.LogWarning($"{data.name}: chưa gán planeBulletPrefab, máy bay không bắn được.", data);
            attackTimer = data.planeAttackInterval;
            return;
        }

        attackTimer = data.planeAttackInterval;

        Vector3 from = muzzle != null ? muzzle.position : transform.position;
        GameObject bullet = SimplePool2.Spawn(data.planeBulletPrefab, from, transform.rotation);
        Bullet b = bullet.GetComponent<Bullet>();
        if (b == null)
            return;

        // Ngắm đón đầu theo vận tốc mục tiêu rồi bắn chéo từ trên xuống
        Vector3 targetVelocity = targetRb != null ? targetRb.linearVelocity : Vector3.zero;
        Vector3 aim = PredictIntercept3D(from, target.position, targetVelocity, b.Speed);
        b.InitDirection(team, aim - from, stats != null ? stats.RollDamage() : 0f);
    }

    // Đón đầu theo khoảng cách 3D (đạn bay chéo xuống nên quãng đường dài hơn khoảng cách ngang)
    private static Vector3 PredictIntercept3D(Vector3 shooter, Vector3 target, Vector3 targetVelocity, float projectileSpeed)
    {
        if (projectileSpeed <= 0.01f || targetVelocity.sqrMagnitude < 0.0001f)
            return target;

        Vector3 aim = target;
        for (int i = 0; i < 3; i++)
        {
            float t = Vector3.Distance(shooter, aim) / projectileSpeed;
            aim = target + targetVelocity * t;
        }
        return aim;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 p = transform.position;
        Gizmos.DrawLine(new Vector3(p.x, seaLevel, p.z), new Vector3(p.x, seaLevel + flyHeight, p.z));
        if (data != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(new Vector3(p.x, seaLevel, p.z), data.planeAttackRange);
        }
    }
}
