using UnityEngine;

public class BoatAIController : MonoBehaviour
{
    private enum State { MoveToTower, Combat }

    [Header("Data")]
    [SerializeField] private BoatData data;
    [Header("References")]
    [SerializeField] private BoatTargetController targetController;
    [SerializeField] private BoatMovementController movement;
    [SerializeField] private BoatHealth health;
    [SerializeField] private BoatCombatBase combat;

    [Header("Move To Tower")]
    [SerializeField] private float towerStoppingDistance = 1f;
   
    private State state = State.MoveToTower;
    private bool initialized;
    private BoatStats stats;
    private Collider selfCollider;

    public BoatStats Stats => stats;

    private void Awake()
    {
        selfCollider = GetComponent<Collider>();
    }

    private void Start()
    {
        if (!initialized)
            Initialize(data);
    }

    public void Initialize(BoatData boatData)
    {
        data = boatData;

        if (data == null)
        {
            Debug.LogError($"{name}: chưa gán BoatData!");
            return;
        }

        // Chỉ tàu của người chơi mới được hưởng cấp tàu và nâng cấp đã mua
        bool usePlayerProgress = targetController.Team == BoatTeam.Ally;
        stats = BoatStats.Create(data, usePlayerProgress);

        movement.ApplyLevel(data, stats.Level);
        targetController.Configure(data.detectionRadius);

        if (health != null)
            health.Setup(data, stats);

        if (combat != null)
            combat.Setup(data, stats);

        initialized = true;
    }

    private void Update()
    {
        if (!initialized || combat == null)
            return;

        if (!GameController.Instance.IsBattleStarted)
        {
            return;
        }

        State desired = targetController.HasBoatTarget ? State.Combat : State.MoveToTower;

        if (desired != state)
            EnterState(desired);

        if (state == State.Combat)
        {
            Transform enemy = targetController.CurrentBoatTarget;
            if (enemy != null)
                combat.Tick(enemy);
        }
        else
        {
            UpdateMoveToTower();
        }
    }

    private void EnterState(State newState)
    {
        state = newState;

        if (state == State.Combat)
        {
            movement.SetSpeedMultiplier(combat.CombatSpeedMultiplier);
            combat.OnEnterCombat(targetController.CurrentBoatTarget);
        }
        else
        {
            movement.SetSpeedMultiplier(1f);
        }
    }

    private void UpdateMoveToTower()
    {
        Transform tower = targetController.CurrentTarget;

        if (tower == null)
        {
            movement.Stop();
            return;
        }

        // Dừng theo mép collider của trụ và của tàu, không phải theo tâm,
        // để tàu không ép vào trụ mãi khi trụ to hơn towerStoppingDistance
        Vector3 point = tower.position;
        float stopDistance = towerStoppingDistance;

        Collider towerCollider = tower.GetComponentInChildren<Collider>();
        if (towerCollider != null)
            point = towerCollider.bounds.ClosestPoint(transform.position);

        if (selfCollider != null)
        {
            Vector3 ext = selfCollider.bounds.extents;
            stopDistance += Mathf.Max(ext.x, ext.z);
        }

        movement.MoveTowards(point, stopDistance);
    }
}