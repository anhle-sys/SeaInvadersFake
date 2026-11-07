using System.Collections.Generic;
using UnityEngine;

public class CarrierCombat : BoatCombatBase
{
    [Tooltip("Vị trí máy bay cất cánh (đường băng). Để trống sẽ dùng vị trí tàu.")]
    [SerializeField] private Transform launchPoint;

    private const float AttackRangeRatio = 1.1f;   // trong (keepDistance × hệ số này) thì coi là đang tấn công: chạy chậm hẳn
    private const float SlowDownRangeRatio = 1.5f; // bắt đầu giảm tốc từ (keepDistance × hệ số này)

    private CarrierData data;
    private BoatStats stats;
    private float launchTimer;
    private float orbitDirection = 1f;
    private readonly List<BoatPlane> planes = new List<BoatPlane>();

    public override float CombatSpeedMultiplier =>
        data != null ? data.combatSpeedMultiplier : 0.3f;

    public override void Setup(BoatData boatData, BoatStats boatStats)
    {
        data = boatData as CarrierData;
        stats = boatStats;
        if (data == null)
            Debug.LogError($"{name}: CarrierCombat cần CarrierData!");
    }

    public override void OnEnterCombat(Transform enemy)
    {
        orbitDirection = Random.value < 0.5f ? 1f : -1f;
        launchTimer = 0f; 
    }

    public override void Tick(Transform enemy)
    {
        if (data == null)
            return;

        movement.Steer(GetOrbitDirection(enemy, data.keepDistance, orbitDirection, data.radiusCorrection));

        // Chỉ chạy chậm khi đã vào vòng lượn quanh địch (đang tấn công); còn xa thì chạy hết tốc để áp sát.
        // Giảm dần từ ngoài vào để không lao vọt qua vòng lượn
        float dist = Vector3.Distance(transform.position, enemy.position);
        float outside = Mathf.InverseLerp(data.keepDistance * AttackRangeRatio, data.keepDistance * SlowDownRangeRatio, dist);
        movement.SetSpeedMultiplier(Mathf.Lerp(data.combatSpeedMultiplier, 1f, outside));

        planes.RemoveAll(p => p == null);

        launchTimer -= Time.deltaTime;
        if (launchTimer > 0f || planes.Count >= data.maxPlanes)
            return;

        launchTimer = data.launchInterval;

        int toLaunch = Mathf.Min(data.planesPerLaunch, data.maxPlanes - planes.Count);
        for (int i = 0; i < toLaunch; i++)
            LaunchPlane(enemy);
    }

    private void LaunchPlane(Transform enemy)
    {
        if (data.planePrefab == null)
        {
            Debug.LogWarning($"{data.name}: chưa gán planePrefab, tàu sân bay không phóng được máy bay.", data);
            return;
        }

        Transform point = launchPoint != null ? launchPoint : transform;

        GameObject go = Instantiate(data.planePrefab, point.position, point.rotation);
        BoatPlane plane = go.GetComponent<BoatPlane>();

        if (plane == null)
        {
            Debug.LogError("Plane prefab thiếu BoatPlane!");
            Destroy(go);
            return;
        }

        plane.Init(targetController.Team, enemy, data, stats);
        planes.Add(plane);
    }
}