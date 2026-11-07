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
    public BoatData Data => data;

    // Id riêng của từng tàu người chơi (mỗi lần mua một id) để nâng cấp không dùng chung giữa các tàu cùng loại
    public string InstanceId { get; private set; }

    private Transform combatTarget;

    private void Awake()
    {
        selfCollider = GetComponent<Collider>();
    }

    private void OnEnable() => GameData.OnUpgradeChanged += HandleUpgradeChanged;
    private void OnDisable() => GameData.OnUpgradeChanged -= HandleUpgradeChanged;

    // Mua nâng cấp khi tàu đã đặt trên sân: tính lại chỉ số ngay, không đợi lần spawn sau
private void HandleUpgradeChanged(string instanceId, StatType stat, int level)
    {
        if (!initialized || data == null || InstanceId != instanceId || targetController.Team != BoatTeam.Ally)
            return;

        stats = BoatStats.Create(data, true, InstanceId);

        if (health != null)
            health.UpdateStats(stats);

        if (combat != null)
            combat.Setup(data, stats);
    }

private void OnDestroy() => GameData.ReleaseBoat(InstanceId);


    private void Start()
    {
        if (!initialized)
            Initialize(data);
    }

public void Initialize(BoatData boatData, string instanceId = null)
    {
        data = boatData;

        if (data == null)
        {
            Debug.LogError($"{name}: chưa gán BoatData!");
            return;
        }

        // Chỉ tàu của người chơi mới được hưởng cấp tàu và nâng cấp đã mua
        bool usePlayerProgress = targetController.Team == BoatTeam.Ally;

        if (usePlayerProgress)
        {
            // Tàu không được truyền id (vd: đặt sẵn trong scene) thì tự cấp một id riêng
            if (string.IsNullOrEmpty(instanceId))
                instanceId = !string.IsNullOrEmpty(InstanceId) ? InstanceId : GameData.NewBoatInstanceId(data.id);

            if (InstanceId != instanceId)
                GameData.ReleaseBoat(InstanceId);
            InstanceId = instanceId;
        }
        else
        {
            InstanceId = null;
        }

        stats = BoatStats.Create(data, usePlayerProgress, InstanceId);

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
        {
            EnterState(desired);
        }
        else if (state == State.Combat && targetController.CurrentBoatTarget != combatTarget)
        {
            // Đổi thẳng từ địch A sang địch B: chạy lại logic vào trận (chọn mạn, lướt qua...) cho mục tiêu mới
            EnterState(State.Combat);
        }

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
            combatTarget = targetController.CurrentBoatTarget;
            movement.SetSpeedMultiplier(combat.CombatSpeedMultiplier);
            combat.OnEnterCombat(combatTarget);
        }
        else
        {
            combatTarget = null;
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