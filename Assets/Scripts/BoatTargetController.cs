using UnityEngine;

public enum BoatTeam
{
    Ally,
    Enemy
}

[DefaultExecutionOrder(-50)]
public class BoatTargetController : MonoBehaviour
{
    [Header("Team")]
    [SerializeField] private BoatTeam team;

    [Header("Boat Detection")]
    [SerializeField] private float searchRadius = 15f;
    [SerializeField] private float searchInterval = 0.3f;
    [SerializeField] private LayerMask boatLayer;
    [SerializeField] private float loseTargetMultiplier = 1.3f;

    private Transform currentBoatTarget;
    private float searchTimer;
    private bool hadTarget;

    private readonly Collider[] results = new Collider[50];

    // Mọi tàu đang hoạt động, để tìm địch trên cả bản đồ mà không phải FindObjects mỗi lần
    private static readonly System.Collections.Generic.List<BoatTargetController> activeBoats =
        new System.Collections.Generic.List<BoatTargetController>();

    // Bật khi tàu đã tới trụ / hết việc: không thấy địch trong searchRadius thì đi săn tàu địch gần nhất trên cả bản đồ.
    // Không có cái này, hai bên có thể đứng ở trụ của nhau mãi mà không bao giờ thấy nhau -> trận không kết thúc
    public bool HuntAnywhere { get; set; }

    private void OnEnable() => activeBoats.Add(this);
    private void OnDisable() => activeBoats.Remove(this);

    public BoatTeam Team => team;

    public Transform CurrentTarget
    {
        get
        {
            if (currentBoatTarget != null)
                return currentBoatTarget;

            GameController gc = GameController.Instance;
            if (gc == null)
                return null;
            return Team == BoatTeam.Ally ? gc.enemyTower : gc.allyTower;
        }
    }

    public bool HasBoatTarget => currentBoatTarget != null;

    public Transform CurrentBoatTarget => currentBoatTarget;

    private void Update()
    {
        if (!GameController.BattleStarted)
            return;
        searchTimer += Time.deltaTime;

        // Mục tiêu vừa chết: tìm mục tiêu mới ngay, không để tàu quay về hướng trụ rồi lại quay ra (giật hướng)
        bool lostTarget = hadTarget
                          && (currentBoatTarget == null || !currentBoatTarget.gameObject.activeInHierarchy);

        if (searchTimer < searchInterval && !lostTarget)
            return;

        searchTimer = 0f;
        FindEnemyBoat();
        hadTarget = currentBoatTarget != null;
    }

    private void FindEnemyBoat()
    {
        // Còn giữ mục tiêu cũ thì khỏi quét vật lý (trước đây quét xong mới kiểm tra -> quét thừa)
        if (currentBoatTarget != null &&
        currentBoatTarget.gameObject.activeInHierarchy)
        {
            float keepDistance = Vector3.Distance(
                transform.position, currentBoatTarget.position);

            if (keepDistance <= searchRadius * loseTargetMultiplier)
                return;
        }

        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            searchRadius,
            results,
            boatLayer
        );

        Transform nearestTarget = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider col = results[i];

            if (col == null)
                continue;

            BoatTargetController other =
                col.GetComponentInParent<BoatTargetController>();

            if (other == null || other == this)
                continue;

            if (other.Team == Team)
                continue;

            if (!other.gameObject.activeInHierarchy)
                continue;

            float distance = Vector3.Distance(
                transform.position,
                other.transform.position
            );

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestTarget = other.transform;
            }
        }

        if (nearestTarget == null && HuntAnywhere)
        {
            foreach (BoatTargetController other in activeBoats)
            {
                if (other == null || other == this || other.Team == Team)
                    continue;

                float distance = Vector3.Distance(transform.position, other.transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestTarget = other.transform;
                }
            }
        }

        currentBoatTarget = nearestTarget;
    }

    public void Configure(float radius)
    {
        searchRadius = radius;
    }
}