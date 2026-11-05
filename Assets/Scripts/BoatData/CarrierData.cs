using UnityEngine;

[CreateAssetMenu(menuName = "Boats/Carrier Data", fileName = "CarrierData")]
public class CarrierData : BoatData
{
    [Header("Carrier")]
    public GameObject planePrefab;
    public GameObject planeBulletPrefab;
    public int maxPlanes = 3;
    public int planesPerLaunch = 1;
    public float launchInterval = 4f;

    [Header("Plane")]
    public float planeSpeed = 8f;
    public float planeTurnSpeed = 120f;
    public float planeLifetime = 15f;
    public float planeAttackRange = 6f;
    public float planeAttackInterval = 1f;

    [Header("Combat Movement")]
    [Tooltip("Khoảng cách giữ với địch (tàu sân bay đứng xa)")]
    public float keepDistance = 12f;
    [Range(0.05f, 1f)] public float combatSpeedMultiplier = 0.3f;
    public float radiusCorrection = 1.5f;
}