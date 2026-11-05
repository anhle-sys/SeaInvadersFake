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
}