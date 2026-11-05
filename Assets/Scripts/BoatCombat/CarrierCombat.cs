using System.Collections.Generic;
using UnityEngine;

public class CarrierCombat : BoatCombatBase
{
    [Tooltip("Vị trí máy bay cất cánh (đường băng). Để trống sẽ dùng vị trí tàu.")]
    [SerializeField] private Transform launchPoint;

    private CarrierData data;
    private float launchTimer;
    private float orbitDirection = 1f;
    private readonly List<BoatPlane> planes = new List<BoatPlane>();

    public override float CombatSpeedMultiplier =>
        data != null ? data.combatSpeedMultiplier : 0.3f;

    public override void Setup(BoatData boatData)
    {
        data = boatData as CarrierData;
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
        Transform point = launchPoint != null ? launchPoint : transform;

        GameObject go = Instantiate(data.planePrefab, point.position, point.rotation);
        BoatPlane plane = go.GetComponent<BoatPlane>();

        if (plane == null)
        {
            Debug.LogError("Plane prefab thiếu BoatPlane!");
            Destroy(go);
            return;
        }

        plane.Init(targetController.Team, enemy, data);
        planes.Add(plane);
    }
}