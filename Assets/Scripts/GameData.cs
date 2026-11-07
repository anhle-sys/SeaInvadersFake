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
    // Tàu người chơi đã mua (id riêng, loại tàu, ô spawn): lưu xuống đĩa để spawn lại ở các lượt chơi sau
    private static readonly List<OwnedEntry> ownedBoats = new();
    public static event Action<int> OnGoldChanged;
    public static event Action<int> OnNotEnoughGold;
    public static event Action<string, StatType, int> OnUpgradeChanged;

    private const string SaveKey = "GAME_DATA";

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
        ownedBoats.Clear();
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

    public static bool SpendGoldForPurchase(int amount)
    {
        if (!SpendGold(amount))
            return false;
        uncommittedSpend += amount;
        return true;
    }

    public static void RefundPurchase(int amount)
    {
        uncommittedSpend = Mathf.Max(0, uncommittedSpend - amount);
        AddGold(amount);
    }

    public static void CommitPurchases()
    {
        uncommittedSpend = 0;
        Save();
    }

    public static int GetBoatLevel(string boatId) =>
        boatLevels.TryGetValue(boatId, out var lv) ? lv : 0;

    public static void SetBoatLevel(string boatId, int level) =>
        boatLevels[boatId] = level;

    public static string NewBoatInstanceId(string boatId) =>
        $"{boatId}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";

    public static void ReleaseBoat(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
            return;

        string prefix = instanceId + ":";
        List<string> remove = null;
        foreach (var key in upgradeLevels.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                (remove ??= new List<string>()).Add(key);
        }

        if (remove == null)
            return;
        foreach (var key in remove)
            upgradeLevels.Remove(key);
    }

private static string Key(string instanceId, StatType stat) => $"{instanceId}:{stat}";

public static int GetUpgradeLevel(string instanceId, StatType stat)
    {
        if (string.IsNullOrEmpty(instanceId))
            return 0;
        return upgradeLevels.TryGetValue(Key(instanceId, stat), out var lv) ? lv : 0;
    }

public static float GetUpgradeBonus(BoatData data, string instanceId, StatType stat)
    {
        var up = data.GetUpgrade(stat);
        if (up == null) return 0f;
        return up.bonusPerLevel * GetUpgradeLevel(instanceId, stat);
    }

public static bool TryUpgrade(BoatData data, string instanceId, StatType stat)
    {
        if (data == null || string.IsNullOrEmpty(instanceId)) return false;

        var up = data.GetUpgrade(stat);
        if (up == null) return false;

        int cur = GetUpgradeLevel(instanceId, stat);
        if (cur >= up.maxLevel) return false;

        // Nâng cấp gắn với từng con tàu và không được lưu qua các lần chơi (tàu cũng vậy),
        // nên tính như tiền mua tàu: chưa vào trận mà thoát thì được hoàn lại vàng.
        if (!SpendGoldForPurchase(GetUpgradeCost(data, instanceId, stat))) return false;

        upgradeLevels[Key(instanceId, stat)] = cur + 1;
        OnUpgradeChanged?.Invoke(instanceId, stat, cur + 1);
        return true;
    }

public static int GetUpgradeCost(BoatData data, string instanceId, StatType stat)
    {
        var up = data.GetUpgrade(stat);
        if (up == null) return 0;

        int cur = GetUpgradeLevel(instanceId, stat);
        return Mathf.RoundToInt(up.baseCost * Mathf.Pow(up.costGrowth, cur));
    }

public static float GetDamage(BoatData data, string instanceId) =>
        data.GetLevel(GetBoatLevel(data.id)).damage + GetUpgradeBonus(data, instanceId, StatType.Weapon);

public static float GetArmor(BoatData data, string instanceId) =>
        GetUpgradeBonus(data, instanceId, StatType.Armor);

public static float GetCritChance(BoatData data, string instanceId) =>
        Mathf.Clamp(GetUpgradeBonus(data, instanceId, StatType.Critical), 0f, CritChanceCap);   // % , có cap   // % , có cap

public static float GetEvasion(BoatData data, string instanceId) =>
        Mathf.Clamp(GetUpgradeBonus(data, instanceId, StatType.Evasion), 0f, EvasionCap);
    [Serializable] private class Entry 
    { 
        public string key; 
        public int level; 
    }
    [Serializable] private class OwnedEntry 
    { 
        public string instanceId; 
        public string boatId; 
        public int slot; 
    }

    public readonly struct OwnedBoat
    {
        public readonly string InstanceId;
        public readonly string BoatId;
        public readonly int Slot;

        public OwnedBoat(string instanceId, string boatId, int slot)
        {
            InstanceId = instanceId;
            BoatId = boatId;
            Slot = slot;
        }
    }

    [Serializable]
    private class SaveData
    {
        public int gold;
        public List<Entry> upgrades = new();
        public List<Entry> boats = new();
        public List<OwnedEntry> owned = new();
    }

public static void Save()
    {
        // Tàu chưa vào trận không được lưu -> lưu kèm phần vàng đã trả cho chúng (coi như hoàn lại).
        // Nâng cấp gắn với từng tàu (instanceId) nên không lưu xuống đĩa.
        var s = new SaveData { gold = Gold + uncommittedSpend };
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
