using UnityEngine;

public class Fly : MonoBehaviour
{
    private enum State { Docked, Attacking, Returning }

    [Header("Bay")]
    [Tooltip("Độ cao bay (so với mặt biển y = seaLevel)")]
    [SerializeField] private float flyHeight = 10f;
    [SerializeField] private float seaLevel = 0f;
    [Tooltip("Tốc độ bay lên / hạ xuống (đơn vị/giây)")]
    [SerializeField] private float climbSpeed = 5f;
    [Tooltip("Góc ngóc mũi tối đa khi đang bay lên / hạ xuống (chỉ để nhìn)")]
    [SerializeField] private float maxPitch = 30f;
    [Tooltip("Góc nghiêng cánh tối đa khi cua (chỉ để nhìn)")]
    [SerializeField] private float maxBank = 35f;

    [Header("Bắn")]
    [Tooltip("Điểm bắn đạn. Để trống = vị trí máy bay")]
    [SerializeField] private Transform muzzle;
    [Tooltip("Chỉ bắn khi đã lên tới độ cao này (tỉ lệ so với flyHeight)")]
    [Range(0f, 1f)][SerializeField] private float minFireHeightRatio = 0.8f;
    [Tooltip("Chỉ bắn khi mục tiêu nằm trong nón phía trước mũi (độ, tính trên mặt phẳng ngang)")]
    [SerializeField] private float fireConeAngle = 30f;

    [Header("Quay về tàu")]
    [Tooltip("Cách điểm đỗ (ngang) bao nhiêu thì bắt đầu hạ độ cao")]
    [SerializeField] private float descendDistance = 15f;
    [Tooltip("Vào trong bán kính này (và đủ thấp) thì đáp xuống")]
    [SerializeField] private float landRadius = 1.5f;
    [SerializeField] private float landHeightTolerance = 1.5f;

    private BoatTeam team;
    private Transform target;
    private Rigidbody targetRb;
    private CarrierData data;
    private BoatStats stats;
    private Transform dockPoint;

    private State state = State.Docked;
    private bool tickedThisFrame;
    private float lifeTimer;
    private float attackTimer;
    private float yawRate;

    private float CruiseY => seaLevel + flyHeight;

    public void Init(BoatTeam planeTeam, CarrierData carrierData, BoatStats carrierStats, Transform dock)
    {
        team = planeTeam;
        data = carrierData;
        stats = carrierStats;
        dockPoint = dock;
        state = State.Docked;
        attackTimer = 0f;
    }

    public void SetTarget(Transform enemy)
    {
        target = enemy;
        targetRb = enemy != null ? enemy.GetComponent<Rigidbody>() : null;
    }

    public void Tick(Transform enemy)
    {
        if (data == null)
            return;

        switch (state)
        {
            case State.Docked:
                Launch();
                goto case State.Attacking;

            case State.Attacking:
                if (enemy != target)
                    SetTarget(enemy);
                tickedThisFrame = true;
                AttackStep(Time.deltaTime);
                break;

            case State.Returning:
                break;
        }
    }

    private void Launch()
    {
        state = State.Attacking;
        lifeTimer = data.planeLifetime;
        transform.SetParent(null); // tách khỏi tàu để không bị kéo theo
    }

    private void StartReturn()
    {
        state = State.Returning;
        SetTarget(null);
    }

    private void Land()
    {
        state = State.Docked;
        SetTarget(null);
        yawRate = 0f;
        transform.SetParent(dockPoint);
        transform.position = dockPoint.position;
        transform.rotation = dockPoint.rotation;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;

        if (state == State.Attacking && !tickedThisFrame)
            StartReturn();   // carrier không còn detect địch -> về tàu

        if (state == State.Returning)
            ReturnStep(dt);

        tickedThisFrame = false;
    }

    private void AttackStep(float dt)
    {
        lifeTimer -= dt;
        if (lifeTimer <= 0f)
        {
            StartReturn();
            return;
        }

        if (attackTimer > 0f)
            attackTimer -= dt;

        Vector3 toTarget = Vector3.zero;
        if (target != null)
        {
            toTarget = target.position - transform.position;
            toTarget.y = 0f;
        }

        Vector3 flatForward = Fly_Move(dt, toTarget, CruiseY);

        if (target != null)
            TryAttack(flatForward, toTarget);
    }

    private void ReturnStep(float dt)
    {
        if (dockPoint == null)
        {
            Destroy(gameObject); // tàu đã chìm / điểm đỗ mất
            return;
        }

        if (attackTimer > 0f)
            attackTimer -= dt;

        Vector3 toDock = dockPoint.position - transform.position;
        toDock.y = 0f;
        float horiz = toDock.magnitude;

        float targetY = Mathf.Lerp(dockPoint.position.y, CruiseY, Mathf.Clamp01(horiz / descendDistance));
        Fly_Move(dt, toDock, targetY);

        float dy = Mathf.Abs(transform.position.y - dockPoint.position.y);
        if (horiz <= landRadius && dy <= landHeightTolerance)
            Land();
    }

    private Vector3 Fly_Move(float dt, Vector3 desiredFlat, float targetY)
    {
        Vector3 pos = transform.position;

        Vector3 flatForward = transform.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        float yawDelta = 0f;
        if (desiredFlat.sqrMagnitude > 0.001f)
        {
            float angle = Vector3.SignedAngle(flatForward, desiredFlat, Vector3.up);
            yawDelta = Mathf.Clamp(angle, -data.planeTurnSpeed * dt, data.planeTurnSpeed * dt);
            flatForward = Quaternion.AngleAxis(yawDelta, Vector3.up) * flatForward;
        }
        yawRate = dt > 0f ? yawDelta / dt : 0f;

        float vertical = Mathf.Clamp(targetY - pos.y, -climbSpeed * dt, climbSpeed * dt);

        pos += flatForward * data.planeSpeed * dt;
        pos.y += vertical;
        transform.position = pos;

        float climbRate = dt > 0f ? vertical / dt : 0f;
        float pitch = -Mathf.Clamp(Mathf.Atan2(climbRate, Mathf.Max(data.planeSpeed, 0.01f)) * Mathf.Rad2Deg, -maxPitch, maxPitch);
        float bank = -Mathf.Clamp(yawRate / Mathf.Max(data.planeTurnSpeed, 1f), -1f, 1f) * maxBank;
        transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up) * Quaternion.Euler(pitch, 0f, bank);

        return flatForward;
    }

    private void TryAttack(Vector3 flatForward, Vector3 toTargetFlat)
    {
        if (attackTimer > 0f)
            return;

        if (transform.position.y < seaLevel + flyHeight * minFireHeightRatio)
            return;

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

        Vector3 targetVelocity = targetRb != null ? targetRb.linearVelocity : Vector3.zero;
        Vector3 aim = PredictIntercept3D(from, target.position, targetVelocity, b.Speed);
        b.InitDirection(team, aim - from, stats != null ? stats.RollDamage() : 0f);
    }

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