using System.Collections.Generic;
using UnityEngine;

public class CarrierCombat : BoatCombatBase
{
    [SerializeField] private Transform[] launchPoints;

    [SerializeField] private float detectRange = 25f;

    [Tooltip("Tốc độ xoay tối đa của hướng lái mong muốn (độ/giây). Nhỏ = tàu đổi hướng êm, không giật")]
    [SerializeField] private float maxHeadingChangeRate = 30f;

    [SerializeField] private List<Fly> planes = new List<Fly>();

    [Tooltip("Xa hơn keepDistance × hệ số này thì lái tới điểm tiếp tuyến của vòng lượn (không lái thẳng vào địch)")]
    [SerializeField] private float tangentApproachMultiplier = 1.15f;
    [Tooltip("Khoảng giãn cách giữa các máy bay khi cất cánh lần đầu (giây)")]
    [SerializeField] private float launchStagger = 0.6f;

    private CarrierData data;
    private BoatStats stats;
    private float orbitDirection = 1f;
    private float combatTime;
    private int launchPointIndex;
    private Vector3 smoothedDir;
    private bool inCombat;

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
        if (!inCombat)
        {
            inCombat = true;
            combatTime = 0f;
            smoothedDir = FlatForward();
        }

        // Vào trận / đổi mục tiêu: chọn chiều lượn theo hướng tàu đang chạy (ít phải quay nhất),
        // không bắt tàu quay đầu ngược lại hay quay mũi về phía địch
        if (enemy != null)
            orbitDirection = ChooseOrbitDirection(enemy.position);
    }

    public override void OnExitCombat()
    {
        inCombat = false;
    }

    private float ChooseOrbitDirection(Vector3 enemyPos)
    {
        Vector3 fromEnemy = transform.position - enemyPos;
        fromEnemy.y = 0f;
        if (fromEnemy.sqrMagnitude < 0.0001f)
            return orbitDirection;

        // Tiếp tuyến chiều +1 tại vị trí hiện tại; cùng phía với mũi tàu thì lượn chiều +1
        Vector3 tangent = Vector3.Cross(Vector3.up, fromEnemy.normalized);
        return Vector3.Dot(FlatForward(), tangent) >= 0f ? 1f : -1f;
    }

    // Hướng lái: ở xa thì đi tới điểm tiếp tuyến của vòng lượn, gần thì lượn tròn quanh địch ở bán kính keepDistance
    private Vector3 GetCombatDirection(Transform enemy, Vector3 toEnemy, float dist, float keep)
    {
        if (dist > keep * tangentApproachMultiplier)
        {
            // Góc lệch khỏi hướng tới địch để đường đi chỉ "chạm" vòng tròn bán kính keep
            float alpha = Mathf.Asin(Mathf.Clamp01(keep / dist)) * Mathf.Rad2Deg;
            // Chiều lượn +1 đi theo Cross(up, fromEnemy) = quay toEnemy sang trái (âm) khi nhìn từ trên
            return Quaternion.AngleAxis(-alpha * orbitDirection, Vector3.up) * (toEnemy / dist);
        }

        // Lượn tròn + tự kéo về đúng bán kính (quá gần thì hướng chéo ra ngoài, không quay đầu)
        return GetOrbitDirection(enemy, keep, orbitDirection, data.radiusCorrection);
    }

    private Vector3 FlatForward()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
    }

    // Xoay dần hướng lái về hướng mong muốn với tốc độ giới hạn: đổi chế độ / đổi chiều lượn không làm hướng nhảy đột ngột
    private void SteerSmooth(Vector3 desired)
    {
        desired.y = 0f;
        if (desired.sqrMagnitude < 0.0001f)
            return;
        desired.Normalize();

        if (smoothedDir.sqrMagnitude < 0.0001f)
            smoothedDir = FlatForward();

        float maxRad = maxHeadingChangeRate * Mathf.Deg2Rad * Time.deltaTime;
        smoothedDir = Vector3.RotateTowards(smoothedDir, desired, maxRad, 0f);
        smoothedDir.y = 0f;
        smoothedDir.Normalize();
        movement.Steer(smoothedDir);
    }

    public override void Tick(Transform enemy)
    {
        if (data == null || enemy == null)
            return;

        combatTime += Time.deltaTime;
        movement.SetSpeedMultiplier(data.combatSpeedMultiplier);

        Vector3 toEnemy = enemy.position - transform.position;
        toEnemy.y = 0f;
        float dist = toEnemy.magnitude;
        float keep = data.keepDistance;

        SteerSmooth(GetCombatDirection(enemy, toEnemy, dist, keep));

        if (dist > detectRange)
            return;

        for (int i = planes.Count - 1; i >= 0; i--)
        {
            if (planes[i] == null)
            {
                planes.RemoveAt(i);
                continue;
            }

            // Máy bay cất cánh lần lượt, không chồng lên nhau
            if (combatTime < i * launchStagger && planes[i].IsDocked)
                continue;

            planes[i].Tick(enemy);
        }
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