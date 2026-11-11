using System.Collections.Generic;
using UnityEngine;

public class CarrierCombat : BoatCombatBase
{
    [SerializeField] private Transform[] launchPoints;

    [SerializeField] private float detectRange = 25f;

    [Tooltip("Tốc độ xoay tối đa của hướng lái mong muốn (độ/giây). Nhỏ = tàu đổi hướng êm, không giật")]
    [SerializeField] private float maxHeadingChangeRate = 30f;
    [Tooltip("Thời gian làm mượt hướng lái (giây): hướng lái vào/ra cua từ từ thay vì đổi tốc độ xoay đột ngột")]
    [SerializeField] private float headingSmoothTime = 0.6f;

    [SerializeField] private List<Fly> planes = new List<Fly>();

    [Tooltip("Khoảng giãn cách giữa các máy bay khi cất cánh lần đầu (giây)")]
    [SerializeField] private float launchStagger = 0.6f;

    private CarrierData data;
    private BoatStats stats;
    private float orbitDirection = 1f;
    private float combatTime;
    private int launchPointIndex;
    private float smoothedHeading;    // độ, quanh trục Y
    private float headingVelocity;    // độ/giây
    private Transform steerEnemy;
    private bool inCombat;
    private int basePlaneCount = -1;                                  // -1: chưa đếm
    private readonly List<Transform> usedDocks = new List<Transform>(); // điểm đỗ đã có máy bay

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
            if (basePlaneCount < 0)
                usedDocks.Add(dock);
        }

        // Số máy bay có sẵn trên prefab (chỉ đếm lần Setup đầu, trước khi thêm máy bay từ nâng cấp)
        if (basePlaneCount < 0)
        {
            planes.RemoveAll(p => p == null);
            basePlaneCount = planes.Count;
        }

        SpawnUpgradePlanes();
    }

    // Nâng cấp Air đạt các bậc trong CarrierData.planeUnlockAirTiers (2.0, 4.0...) thì có thêm máy bay.
    // Setup chạy lúc spawn tàu và mỗi lần mua nâng cấp, nên máy bay mới xuất hiện ngay và có lại ở round sau
    private void SpawnUpgradePlanes()
    {
        BoatAIController ai = GetComponent<BoatAIController>();
        if (ai == null || string.IsNullOrEmpty(ai.InstanceId))
            return; // tàu địch / tàu không có tiến trình người chơi

        int airLevel = GameData.GetUpgradeLevel(ai.InstanceId, StatType.Air);
        int wanted = basePlaneCount + data.GetBonusPlanes(airLevel);

        planes.RemoveAll(p => p == null);
        while (planes.Count < wanted && AddPlaneFromUpgrade()) { }
    }

    // Tàu sân bay bị hủy: hủy luôn các máy bay của nó. Máy bay đang đỗ là con của tàu nên tự mất theo,
    // còn máy bay đang bay đã tách khỏi tàu nên phải hủy riêng
    private void OnDestroy()
    {
        if (!gameObject.scene.isLoaded)
            return; // đang đổi scene: mọi thứ tự bị hủy

        foreach (Fly plane in planes)
            if (plane != null)
                Destroy(plane.gameObject);
    }

    public override void OnEnterCombat(Transform enemy)
    {
        if (!inCombat)
        {
            inCombat = true;
            combatTime = 0f;
            Vector3 f = FlatForward();
            smoothedHeading = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            headingVelocity = 0f;
        }

        steerEnemy = enemy;

        // Vào trận / đổi mục tiêu: chọn chiều lượn theo hướng tàu đang chạy (ít phải quay nhất),
        // không bắt tàu quay đầu ngược lại hay quay mũi về phía địch
        if (enemy != null)
            orbitDirection = ChooseOrbitDirection(enemy.position);
    }

    public override void OnExitCombat()
    {
        inCombat = false;
        steerEnemy = null;
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

    // Hướng lái: lượn tròn quanh địch ở bán kính keep, lệch dần vào trong / ra ngoài theo sai số bán kính.
    // Một công thức liên tục cho mọi khoảng cách (không có ngưỡng đổi chế độ) nên hướng không nhảy
    // khi tàu dao động quanh mép vòng lượn
    private Vector3 GetCombatDirection(Vector3 toEnemy, float dist, float keep)
    {
        if (dist < 0.01f || keep < 0.01f)
            return FlatForward();

        // Góc chếch vào phía địch so với tiếp tuyến (âm = chếch ra ngoài khi đang quá gần)
        float error = Mathf.Max((dist - keep) / keep, -1f);
        float inward = Mathf.Atan(error * data.radiusCorrection) * Mathf.Rad2Deg;

        // Ở xa: không chếch quá đường tiếp tuyến của vòng lượn (không lái thẳng vào địch)
        if (dist > keep)
            inward = Mathf.Min(inward, 90f - Mathf.Asin(keep / dist) * Mathf.Rad2Deg);

        // Chiều lượn +1 đi theo Cross(up, fromEnemy) = quay toEnemy sang trái (âm) khi nhìn từ trên
        return Quaternion.AngleAxis(-(90f - inward) * orbitDirection, Vector3.up) * (toEnemy / dist);
    }

    private Vector3 FlatForward()
    {
        Vector3 f = transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
    }

    // Lái trong FixedUpdate, cùng nhịp với BoatMovementController: nếu cập nhật hướng trong Update thì
    // mỗi bước vật lý thấy hướng đổi 0, 1 hoặc 2 frame một lần -> tốc độ xoay bù trước bị nhiễu -> tàu giật
    private void FixedUpdate()
    {
        if (!inCombat || data == null || steerEnemy == null)
            return;

        Vector3 toEnemy = steerEnemy.position - transform.position;
        toEnemy.y = 0f;
        Vector3 desired = GetCombatDirection(toEnemy, toEnemy.magnitude, data.keepDistance);
        float targetHeading = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;

        // Làm mượt kiểu lò xo tắt dần: tốc độ xoay của hướng lái tăng/giảm từ từ (không bật/tắt tức thì),
        // đổi mục tiêu / đổi chiều lượn không làm hướng nhảy đột ngột
        smoothedHeading = Mathf.SmoothDampAngle(smoothedHeading, targetHeading, ref headingVelocity,
                                                headingSmoothTime, maxHeadingChangeRate, Time.fixedDeltaTime);

        float rad = smoothedHeading * Mathf.Deg2Rad;
        movement.Steer(new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)));
    }

    public override void Tick(Transform enemy)
    {
        if (data == null || enemy == null)
            return;

        combatTime += Time.deltaTime;
        movement.SetSpeedMultiplier(data.combatSpeedMultiplier);
        steerEnemy = enemy;

        Vector3 toEnemy = enemy.position - transform.position;
        toEnemy.y = 0f;
        float dist = toEnemy.magnitude;

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

        // Ưu tiên điểm đỗ còn trống để máy bay mới không chồng lên máy bay đang đỗ
        foreach (Transform free in launchPoints)
        {
            if (free != null && !usedDocks.Contains(free))
            {
                usedDocks.Add(free);
                return free;
            }
        }

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