using System;
using System.Collections.Generic;
using UnityEngine;

public static class GameData
{
    public const float CritChanceCap = 75f; // %
    public const float EvasionCap = 60f;    // %

    public static int Gold { get; private set; }
    private static readonly Dictionary<string, int> upgradeLevels = new();
    private static readonly Dictionary<string, int> boatLevels = new();
    public static event Action<int> OnGoldChanged;
    public static event Action<int> OnNotEnoughGold;
    public static event Action<string, StatType, int> OnUpgradeChanged;

    private const string SaveKey = "GAME_DATA";

    // Vàng đã trả cho tàu mua trong pha chuẩn bị nhưng trận chưa bắt đầu.
    // Tàu không được lưu, nên nếu thoát game lúc này thì phần vàng này phải được hoàn lại khi lưu.
    private static int uncommittedSpend;

    // Có thay đổi chưa ghi xuống đĩa (vd: thưởng giết địch) -> ghi khi pause/quit, không ghi mỗi lần giết
    private static bool dirty;

    // Khi tắt Domain Reload: xóa dữ liệu/subscriber cũ của lần Play trước
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Gold = 0;
        uncommittedSpend = 0;
        dirty = false;
        upgradeLevels.Clear();
        boatLevels.Clear();
        OnGoldChanged = null;
        OnNotEnoughGold = null;
        OnUpgradeChanged = null;
    }

    public static void AddGold(int amount)
    {
        Gold += amount;
        dirty = true;
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
        dirty = true;
        OnGoldChanged?.Invoke(Gold);
        return true;
    }

    /// <summary>Trả vàng mua tàu trong pha chuẩn bị. Chỉ thành chi tiêu thật khi gọi CommitPurchases().</summary>
    public static bool SpendGoldForPurchase(int amount)
    {
        if (!SpendGold(amount))
            return false;
        uncommittedSpend += amount;
        return true;
    }

    /// <summary>Hoàn lại vàng của một lần mua tàu thất bại.</summary>
    public static void RefundPurchase(int amount)
    {
        uncommittedSpend = Mathf.Max(0, uncommittedSpend - amount);
        AddGold(amount);
    }

    /// <summary>Gọi khi trận bắt đầu: tàu đã mua được dùng, chốt số vàng đã chi.</summary>
    public static void CommitPurchases()
    {
        uncommittedSpend = 0;
        Save();
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
        Mathf.Clamp(GetUpgradeBonus(data, StatType.Critical), 0f, CritChanceCap);   // % , có cap

    public static float GetEvasion(BoatData data) =>
        Mathf.Clamp(GetUpgradeBonus(data, StatType.Evasion), 0f, EvasionCap);
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
        // Tàu chưa vào trận không được lưu -> lưu kèm phần vàng đã trả cho chúng (coi như hoàn lại)
        var s = new SaveData { gold = Gold + uncommittedSpend };
        foreach (var kv in upgradeLevels) s.upgrades.Add(new Entry { key = kv.Key, level = kv.Value });
        foreach (var kv in boatLevels) s.boats.Add(new Entry { key = kv.Key, level = kv.Value });

        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(s));
        PlayerPrefs.Save();
        dirty = false;
    }

    public static void SaveIfDirty()
    {
        if (dirty)
            Save();
    }

    public static void Load(int defaultGold = 0)
    {
        upgradeLevels.Clear();
        boatLevels.Clear();
        uncommittedSpend = 0;
        dirty = false;

        SaveData s = null;
        if (PlayerPrefs.HasKey(SaveKey))
        {
            try
            {
                s = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(SaveKey));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"GameData: dữ liệu lưu bị hỏng, dùng dữ liệu mặc định. {e.Message}");
            }
        }

        if (s == null)
        {
            Gold = defaultGold;
            OnGoldChanged?.Invoke(Gold);
            return;
        }

        Gold = Mathf.Max(0, s.gold);
        if (s.upgrades != null)
            foreach (var e in s.upgrades)
                if (e != null && !string.IsNullOrEmpty(e.key)) upgradeLevels[e.key] = Mathf.Max(0, e.level);
        if (s.boats != null)
            foreach (var e in s.boats)
                if (e != null && !string.IsNullOrEmpty(e.key)) boatLevels[e.key] = Mathf.Max(0, e.level);

        OnGoldChanged?.Invoke(Gold);
    }

    public static void ResetAll()
    {
        Gold = 0;
        uncommittedSpend = 0;
        dirty = false;
        upgradeLevels.Clear();
        boatLevels.Clear();
        PlayerPrefs.DeleteKey(SaveKey);
        OnGoldChanged?.Invoke(Gold);
    }
}
