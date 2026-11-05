using UnityEngine;

public enum BoatTeam
{
    Ally,
    Enemy
}

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

    private readonly Collider[] results = new Collider[50];

    public BoatTeam Team => team;

    public Transform CurrentTarget =>
    currentBoatTarget != null
        ? currentBoatTarget
        : Team == BoatTeam.Ally
            ? GameController.Instance.enemyTower
            : GameController.Instance.allyTower;

    public bool HasBoatTarget => currentBoatTarget != null;

    public Transform CurrentBoatTarget => currentBoatTarget;

    private void Update()
    {
        if (!GameController.Instance.IsBattleStarted)
            return;
        searchTimer += Time.deltaTime;

        if (searchTimer < searchInterval)
            return;

        searchTimer = 0f;
        FindEnemyBoat();
    }

    private void FindEnemyBoat()
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            searchRadius,
            results,
            boatLayer
        );

        if (currentBoatTarget != null &&
        currentBoatTarget.gameObject.activeInHierarchy)
        {
            float keepDistance = Vector3.Distance(
                transform.position, currentBoatTarget.position);

            if (keepDistance <= searchRadius * loseTargetMultiplier)
                return;
        }

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

        currentBoatTarget = nearestTarget;
    }

    public void Configure(float radius)
    {
        searchRadius = radius;
    }
}