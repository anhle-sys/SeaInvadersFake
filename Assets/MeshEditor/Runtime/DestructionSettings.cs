using UnityEngine;

[CreateAssetMenu(
    fileName = "DestructionSettings",
    menuName = "Mesh Destruction/Destruction Settings")]
public class DestructionSettings : ScriptableObject
{
    [Header("Mask")]
    [Tooltip("Texture grayscale: white = destroyed, black = preserved. Enable Read/Write.")]
    public Texture2D destructionMask;

    [Range(0f, 1f)]
    [Tooltip("Mask pixel brightness at or above this value is removed.")]
    public float damageThreshold = 0.5f;

    [Header("Damage Radius")]
    [Min(0.001f)]
    public float baseRadius = 0.25f;

    [Min(0.001f)]
    [Tooltip("Damage value that produces baseRadius.")]
    public float referenceDamage = 10f;

    [Min(0.001f)]
    public float maxRadius = 1.5f;

    [Header("Surface Filtering")]
    [Range(-1f, 1f)]
    [Tooltip("Only triangles whose normal dot hit normal is at least this value are considered.")]
    public float minimumNormalDot = 0.5f;

    [Min(0.001f)]
    [Tooltip("Maximum distance from the hit plane, expressed as a multiple of the destruction radius.")]
    public float planeToleranceRadiusMultiplier = 0.25f;

    [Header("Performance")]
    [Tooltip("Skip destruction if the mask is not readable. Recommended mask size for mobile: 64-256 pixels.")]
    public bool logWarnings = true;

    public float GetRadius(float damage)
    {
        if (damage <= 0f)
            return 0f;

        // Radius grows with the square root of damage.
        float radius = baseRadius * Mathf.Sqrt(damage / Mathf.Max(referenceDamage, 0.001f));
        return Mathf.Clamp(radius, baseRadius * 0.1f, maxRadius);
    }
}
