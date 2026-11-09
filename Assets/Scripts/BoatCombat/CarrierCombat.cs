using System.Collections.Generic;
using UnityEngine;

public class CarrierCombat : BoatCombatBase
{
    [SerializeField] private Transform[] launchPoints;

    [SerializeField] private float detectRange = 25f;

    [Tooltip("Mỗi lần chọn điểm mới, tàu lệch quanh địch một góc ngẫu nhiên trong khoảng này (độ). Góc càng lớn tàu càng chạy xéo, càng ít lộ hông.")]
    [SerializeField] private Vector2 roamAngleRange = new Vector2(60f, 120f);

    [Tooltip("Cách điểm đến bấy nhiêu thì chọn điểm mới (không giảm tốc khi tới).")]
    [SerializeField] private float roamArriveDistance = 2f;

    [SerializeField] private List<Fly> planes = new List<Fly>();
    [SerializeField] private float roamEnterMultiplier = 1.1f;
    [SerializeField] private float roamExitMultiplier = 1.25f;

    private bool isRoaming;

    private const float AttackRangeRatio = 1.1f;

    private CarrierData data;
    private BoatStats stats;
    private float orbitDirection = 1f;
    private float roamAngle;        // góc của điểm đến quanh địch (độ, 0 = +Z)
    private bool hasRoamAngle;
    private int launchPointIndex;

    public override float CombatSpeedMultiplier =>
        data != null ? data.combatSpeedMultiplier : 0.3f;

    public override void Setup(BoatData boatData, BoatStats boatStats)
    {
        data = boatData as CarrierData;
        stats = boatStats;
        if (data == null)
        {
            Debug.LogError($"{name}: CarrierCombat cần CarrierData!");
            return;
        }

        foreach (var plane in planes)
        {
            if (plane == null)
                continue;

            Transform dock = plane.transform.parent != null ? plane.transform.parent : transform;
            plane.Init(targetController.Team, data, stats, dock);
        }
    }

    public override void OnEnterCombat(Transform enemy)
    {
        orbitDirection = Random.value < 0.5f ? 1f : -1f;
        hasRoamAngle = false;
        isRoaming = false;
    }

    public override void OnExitCombat()
    {
        hasRoamAngle = false;
    }

    public override void Tick(Transform enemy)
    {
        if (data == null || enemy == null)
            return;

        movement.SetSpeedMultiplier(data.combatSpeedMultiplier);

        Vector3 toEnemy = enemy.position - transform.position;
        toEnemy.y = 0f;

        float dist = toEnemy.magnitude;

        if (!isRoaming)
        {
            if (dist <= data.keepDistance * roamEnterMultiplier)
            {
                isRoaming = true;
                hasRoamAngle = false;
            }
        }
        else
        {
            if (dist > data.keepDistance * roamExitMultiplier)
            {
                isRoaming = false;
                hasRoamAngle = false;
            }
        }

        if (!isRoaming)
        {
            movement.Steer(toEnemy.normalized);
        }
        else
        {
            Roam(enemy);
        }

        if (dist > detectRange)
            return;

        for (int i = planes.Count - 1; i >= 0; i--)
        {
            if (planes[i] == null)
            {
                planes.RemoveAt(i);
                continue;
            }

            planes[i].Tick(enemy);
        }
    }

    private void Roam(Transform enemy)
    {
        Vector3 enemyPos = enemy.position;

        if (!hasRoamAngle || FlatDistanceTo(RoamPoint(enemyPos)) <= roamArriveDistance)
            PickRoamAngle(enemyPos);

        // Điểm đến tính lại theo vị trí địch mỗi frame nên địch di chuyển thì vòng đi theo
        Vector3 toPoint = RoamPoint(enemyPos) - transform.position;
        toPoint.y = 0f;
        movement.Steer(toPoint);
    }

    private void PickRoamAngle(Vector3 enemyPos)
    {
        Vector3 fromEnemy = transform.position - enemyPos;
        fromEnemy.y = 0f;
        float current = fromEnemy.sqrMagnitude > 0.0001f
            ? Mathf.Atan2(fromEnemy.x, fromEnemy.z) * Mathf.Rad2Deg
            : transform.eulerAngles.y;

        // Thỉnh thoảng đổi chiều để đường đi đỡ đoán trước
        if (Random.value < 0.25f)
            orbitDirection = -orbitDirection;

        float step = Random.Range(roamAngleRange.x, roamAngleRange.y);
        roamAngle = current + orbitDirection * step;
        hasRoamAngle = true;
    }

    private Vector3 RoamPoint(Vector3 enemyPos)
    {
        float r = roamAngle * Mathf.Deg2Rad;
        return enemyPos + new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r)) * data.keepDistance;
    }

    private float FlatDistanceTo(Vector3 point)
    {
        Vector3 d = point - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    public bool AddPlaneFromUpgrade()
    {
        if (data == null || data.planePrefab == null)
        {
            Debug.LogWarning($"{name}: thiếu data hoặc planePrefab.");
            return false;
        }

        planes.RemoveAll(p => p == null);
        if (planes.Count >= data.maxPlanes)
            return false;

        Transform point = NextLaunchPoint();
        GameObject go = Instantiate(data.planePrefab, point.position, point.rotation, point);

        if (!go.TryGetComponent(out Fly fly))
        {
            Debug.LogError("Plane prefab thiếu component Fly!");
            Destroy(go);
            return false;
        }

        fly.Init(targetController.Team, data, stats, point);
        planes.Add(fly);
        return true;
    }

    private Transform NextLaunchPoint()
    {
        if (launchPoints == null || launchPoints.Length == 0)
            return transform;

        for (int i = 0; i < launchPoints.Length; i++)
        {
            Transform p = launchPoints[launchPointIndex % launchPoints.Length];
            launchPointIndex++;
            if (p != null)
                return p;
        }
        return transform;
    }
}