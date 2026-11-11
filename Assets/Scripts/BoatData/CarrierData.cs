using UnityEngine;

[CreateAssetMenu(menuName = "Boats/Carrier Data", fileName = "CarrierData")]
public class CarrierData : BoatData
{
    [Header("Carrier")]
    public GameObject planePrefab;
    public GameObject planeBulletPrefab;
    public int maxPlanes = 3;
    [Tooltip("Nâng cấp Air đạt mỗi bậc trong danh sách này (bậc hiển thị trên popup: 2 = 2.0, 4 = 4.0) " +
             "thì tàu có thêm 1 máy bay")]
    public int[] planeUnlockAirTiers = { 2, 4 };

    // Số máy bay được thêm nhờ nâng cấp Air. airLevel = số lần đã nâng (GameData.GetUpgradeLevel)
    public int GetBonusPlanes(int airLevel)
    {
        StatUpgrade air = GetUpgrade(StatType.Air);
        if (air == null || planeUnlockAirTiers == null)
            return 0;

        int tier = air.GetTier(airLevel);
        int count = 0;
        foreach (int unlock in planeUnlockAirTiers)
            if (tier >= unlock)
                count++;
        return count;
    }
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