using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class BoatLevelStats
{
    public GameObject prefab;
    public float damage = 10f;
    public float maxHealth = 100f;
    public float moveSpeed = 5f;
    public float rotateSpeed = 120f;
}
public enum BoatType
{
    None,
    Banger,
    Gunner,
    Carrier
}

public enum StatType
{
    Weapon,
    Armor,
    Critical,
    Evasion,
    Explosive,
    Air,
}

[System.Serializable]
public class StatUpgrade
{
    public StatType stat;
    public float bonusPerLevel = 2f;
    public int maxLevel = 10;
    public int baseCost = 100;
    public float costGrowth = 1.15f;
}

public abstract class BoatData : ScriptableObject
{
    [Header("Info")]
    public string id;
    public string displayName;
    public BoatType boatType;

    [Header("Economy")]
    public int cost = 100;
    public int killReward = 50;

    [Header("Movement")]
    [Tooltip("Thông số cảm giác lái riêng của loại tàu này. Để trống = dùng profile gắn trên prefab")]
    public BoatHandlingProfile handlingProfile;

    [Header("Detection")]
    public float detectionRadius = 15f;
    public BoatLevelStats[] levels = new BoatLevelStats[1];

    [Header("Upgrades")]
    public StatUpgrade[] upgrades;

    public int MaxLevel => Mathf.Max(0, levels.Length - 1);

    public BoatLevelStats GetLevel(int level)
    {
        return levels[Mathf.Clamp(level, 0, levels.Length - 1)];
    }

    public StatUpgrade GetUpgrade(StatType type)
    {
        if (upgrades == null) return null;

        for (int i = 0; i < upgrades.Length; i++)
        {
            if (upgrades[i].stat == type)
                return upgrades[i];
        }
        return null;
    }

    public GameObject GetPrefab(int level) => GetLevel(level).prefab;
    public float GetMaxHealth(int level) => GetLevel(level).maxHealth;

    public float GetReferenceMass()
    {
        var rb = levels[0].prefab != null ? levels[0].prefab.GetComponent<Rigidbody>() : null;
        return rb != null ? rb.mass : 1f;
    }
}