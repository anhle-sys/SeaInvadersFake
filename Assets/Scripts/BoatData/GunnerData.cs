using UnityEngine;

public enum CannonSide { Right, Left, Both }

[CreateAssetMenu(menuName = "Boats/Gunner Data", fileName = "GunnerData")]
public class GunnerData : BoatData
{
    [Header("Gunner")]
    public GameObject bulletPrefab;
    public float attackRange = 8f;
    public float attackCooldown = 2f;
    public float volleyDelay = 0.12f;
    public CannonSide cannonSide = CannonSide.Both;

    [Header("Combat Movement")]
    [Range(0.05f, 1f)] public float combatSpeedMultiplier = 0.4f;
    [Range(0.3f, 1f)] public float combatDistanceRatio = 0.8f;
    public float radiusCorrection = 1.5f;
    public float turnRadius = 3f;
    public float passDistance = 3f;
    public float passMaxTime = 3f;
    public float orbitCorrectionGain = 8f;
    public float maxOrbitCorrection = 40f;
}