using UnityEngine;

[System.Serializable]
public class BoatLevelStats
{
    public GameObject prefab;
    public float maxHealth = 100f;
    public float moveSpeed = 5f;
    public float rotateSpeed = 120f;
}

public abstract class BoatData : ScriptableObject
{
    [Header("Info")]
    public string displayName;

    [Header("Detection")]
    public float detectionRadius = 15f;
    public BoatLevelStats[] levels = new BoatLevelStats[1];

    public int MaxLevel => Mathf.Max(0, levels.Length - 1);

    public BoatLevelStats GetLevel(int level)
    {
        return levels[Mathf.Clamp(level, 0, levels.Length - 1)];
    }

    public GameObject GetPrefab(int level) => GetLevel(level).prefab;
    public float GetMaxHealth(int level) => GetLevel(level).maxHealth;

    public float GetReferenceMass()
    {
        var rb = levels[0].prefab != null ? levels[0].prefab.GetComponent<Rigidbody>() : null;
        return rb != null ? rb.mass : 1f;
    }
}