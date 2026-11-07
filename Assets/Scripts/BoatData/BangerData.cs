using UnityEngine;

[CreateAssetMenu(menuName = "Boats/Banger Data", fileName = "BangerData")]
public class BangerData : BoatData
{
    [Header("Banger")]
    public float explosionRadius = 3f;
    public float explosionDamage = 80f;
    public float triggerDistance = 1.5f;
    public LayerMask damageLayers;
    public GameObject explosionPrefab;

    [Header("Combat Movement")]
    [Range(0.05f, 1f)] public float chargeSpeedMultiplier = 1f;
    [Tooltip("Bật: lao vào địch là đạt tốc độ tối đa ngay, không có đoạn tăng tốc. Tắt: tăng tốc theo profile như tàu thường")]
    public bool chargeInstantSpeed = true;
    [Tooltip("Tốc độ còn lại khi cua gắt lúc lao vào địch (0.1..1). 1 = không chậm lại khi bẻ lái")]
    [Range(0.1f, 1f)] public float chargeSharpTurnSpeedFactor = 1f;
}