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

    // Banger luôn chạy nhanh bằng moveSpeed (chỉnh trong levels). Thấy địch thì chỉ bẻ lái mượt về phía địch,
    // không đổi tốc độ, nên không bị giật. Độ gắt tăng tốc / bẻ lái chỉnh trong BangerHandlingProfile
    [Header("Combat Movement")]
    [Tooltip("Tốc độ còn lại khi cua gắt lúc lao vào địch (0.1..1). 1 = không chậm lại khi bẻ lái")]
    [Range(0.1f, 1f)] public float chargeSharpTurnSpeedFactor = 1f;
    [Tooltip("Ngắm đón đầu theo vận tốc địch để đường lao là đường thẳng thay vì đuổi theo đuôi")]
    public bool chargeLeadTarget = true;
}
