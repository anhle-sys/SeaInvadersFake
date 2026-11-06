using System;
using System.Collections.Generic;
using UnityEngine;

public static class GameData
{
    public static int Gold { get; private set; }
    private static readonly Dictionary<string, int> upgradeLevels = new();
    private static readonly Dictionary<string, int> boatLevels = new();
    public static event Action<int> OnGoldChanged;
    public static event Action<int> OnNotEnoughGold;
    public static event Action<string, StatType, int> OnUpgradeChanged;

    private const string SaveKey = "GAME_DATA";
    public static void AddGold(int amount)
    {
        Gold += amount;
        OnGoldChanged?.Invoke(Gold);
    }

    public static bool SpendGold(int amount)
    {
        if (Gold < amount)
        {
            OnNotEnoughGold?.Invoke(amount);
            return false;
        }
        Gold -= amount;
        OnGoldChanged?.Invoke(Gold);
        return true;
    }
    public static int GetBoatLevel(string boatId) =>
        boatLevels.TryGetValue(boatId, out var lv) ? lv : 0;

    public static void SetBoatLevel(string boatId, int level) =>
        boatLevels[boatId] = level;
    private static string Key(string boatId, StatType stat) => $"{boatId}:{stat}";

    public static int GetUpgradeLevel(string boatId, StatType stat) =>
        upgradeLevels.TryGetValue(Key(boatId, stat), out var lv) ? lv : 0;

    public static float GetUpgradeBonus(BoatData data, StatType stat)
    {
        var up = data.GetUpgrade(stat);
        if (up == null) return 0f;
        return up.bonusPerLevel * GetUpgradeLevel(data.id, stat);
    }

    public static bool TryUpgrade(BoatData data, StatType stat)
    {
        var up = data.GetUpgrade(stat);
        if (up == null) return false;

        int cur = GetUpgradeLevel(data.id, stat);
        if (cur >= up.maxLevel) return false;
        if (!SpendGold(GetUpgradeCost(data, stat))) return false;

        upgradeLevels[Key(data.id, stat)] = cur + 1;
        OnUpgradeChanged?.Invoke(data.id, stat, cur + 1);
        Save();
        return true;
    }

    public static int GetUpgradeCost(BoatData data, StatType stat)
    {
        var up = data.GetUpgrade(stat);
        if (up == null) return 0;

        int cur = GetUpgradeLevel(data.id, stat);
        return Mathf.RoundToInt(up.baseCost * Mathf.Pow(up.costGrowth, cur));
    }

    public static float GetDamage(BoatData data) =>
    data.GetLevel(GetBoatLevel(data.id)).damage + GetUpgradeBonus(data, StatType.Weapon);

    public static float GetArmor(BoatData data) =>
        GetUpgradeBonus(data, StatType.Armor);

    public static float GetCritChance(BoatData data) =>
        Mathf.Clamp(GetUpgradeBonus(data, StatType.Critical), 0f, 75f);   // % , có cap

    public static float GetEvasion(BoatData data) =>
        Mathf.Clamp(GetUpgradeBonus(data, StatType.Evasion), 0f, 60f);
    [Serializable] private class Entry { public string key; public int level; }

    [Serializable]
    private class SaveData
    {
        public int gold;
        public List<Entry> upgrades = new();
        public List<Entry> boats = new();
    }

    public static void Save()
    {
        var s = new SaveData { gold = Gold };
        foreach (var kv in upgradeLevels) s.upgrades.Add(new Entry { key = kv.Key, level = kv.Value });
        foreach (var kv in boatLevels) s.boats.Add(new Entry { key = kv.Key, level = kv.Value });

        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(s));
        PlayerPrefs.Save();
    }

    public static void Load(int defaultGold = 0)
    {
        upgradeLevels.Clear();
        boatLevels.Clear();

        if (!PlayerPrefs.HasKey(SaveKey))
        {
            Gold = defaultGold;
            OnGoldChanged?.Invoke(Gold);
            return;
        }

        var s = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(SaveKey));
        Gold = s.gold;
        foreach (var e in s.upgrades) upgradeLevels[e.key] = e.level;
        foreach (var e in s.boats) boatLevels[e.key] = e.level;

        OnGoldChanged?.Invoke(Gold);
    }

    public static void ResetAll()
    {
        Gold = 0;
        upgradeLevels.Clear();
        boatLevels.Clear();
        PlayerPrefs.DeleteKey(SaveKey);
        OnGoldChanged?.Invoke(Gold);
    }
}