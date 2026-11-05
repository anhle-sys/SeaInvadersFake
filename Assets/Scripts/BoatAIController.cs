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

        movement.ApplyLevel(data, 0);
        targetController.Configure(data.detectionRadius);

        if (health != null)
            health.SetMaxHealth(data.GetMaxHealth(0));

        if (combat != null)
            combat.Setup(data);

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

        movement.MoveTowards(tower.position, towerStoppingDistance);
    }
}