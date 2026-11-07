using System;
using System.Collections.Generic;
using UnityEngine;

public static class GameData
{
    public const float CritChanceCap = 75f; // %
    public const float EvasionCap = 60f;    // %

    public static int Gold { get; private set; }

    // Cấp nâng cấp hiện tại (đang dùng trong game, gồm cả phần chưa chốt)
    private static readonly Dictionary<string, int> upgradeLevels = new();
    // Bản chốt của upgradeLevels tại lần vào trận gần nhất -> đây là phần được ghi xuống đĩa
    private static readonly Dictionary<string, int> committedUpgrades = new();
    private static readonly Dictionary<string, int> boatLevels = new();

    // Tàu đã chốt (đã từng vào trận): lưu xuống đĩa, spawn lại mỗi đầu round
    private static readonly List<OwnedEntry> ownedBoats = new();
    // Tàu vừa mua trong round này, chưa vào trận: thoát game thì không lưu và được hoàn vàng
    private static readonly List<OwnedEntry> pendingBoats = new();

    public static event Action<int> OnGoldChanged;
    public static event Action<int> OnNotEnoughGold;
    public static event Action<string, StatType, int> OnUpgradeChanged;

    private const string SaveKey = "GAME_DATA";

    private static int uncommittedSpend;

    // Có thay đổi chưa ghi xuống đĩa (vd: thưởng giết địch) -> ghi khi pause/quit, không ghi mỗi lần giết
    private static bool dirty;
    private static int currentBattle;
    private static int currentRound;
    private static int totalRounds;
    private static int completedRounds;
    private static bool isBattleActive;
    private static readonly List<BattleEntry> battleHistory = new();
    public static int CurrentBattle => currentBattle;
    public static int CurrentRound => currentRound;
    public static int TotalRounds => totalRounds;
    public static int CompletedRounds => completedRounds;
    public static bool IsBattleActive => isBattleActive;

    public static IReadOnlyList<BattleEntry> BattleHistory => battleHistory;

    // Khi tắt Domain Reload: xóa dữ liệu/subscriber cũ của lần Play trước
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Gold = 0;
        uncommittedSpend = 0;
        dirty = false;
        upgradeLevels.Clear();
        committedUpgrades.Clear();
        boatLevels.Clear();
        ownedBoats.Clear();
        pendingBoats.Clear();
        OnGoldChanged = null;
        OnNotEnoughGold = null;
        OnUpgradeChanged = null;
    }

    [Serializable]
    public class BattleEntry
    {
        public int battleNumber;
        public int totalRounds;
        public int completedRounds;
    }
    public static void StartNewBattle(int rounds)
    {
        currentBattle++;
        currentRound = 1;
        totalRounds = Mathf.Max(1, rounds);
        completedRounds = 0;
        isBattleActive = true;

        // Reset dữ liệu tạm thời của Battle trước nếu có
        upgradeLevels.Clear();
        pendingBoats.Clear();
        uncommittedSpend = 0;

        Save();

        Debug.LogError($"Start Battle {currentBattle} - {totalRounds} Rounds");
    }
    public static bool CompleteCurrentRound()
    {
        if (!isBattleActive)
            return false;

        completedRounds++;

        if (completedRounds >= totalRounds)
        {
            isBattleActive = false;

            battleHistory.Add(new BattleEntry
            {
                battleNumber = currentBattle,
                totalRounds = totalRounds,
                completedRounds = completedRounds
            });

            Debug.LogError($"Battle {currentBattle} Completed!");

            Save();
            return true;
        }

        currentRound++;

        Save();

        Debug.LogError($"Battle {currentBattle} - Round {currentRound}");

        return false;
    }

    #region Gold

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

    // Gọi khi bắt đầu trận: tàu mới mua + nâng cấp trở thành dữ liệu chính thức và được lưu
    public static void CommitPurchases()
    {
        ownedBoats.AddRange(pendingBoats);
        pendingBoats.Clear();

        committedUpgrades.Clear();
        foreach (var kv in upgradeLevels)
            committedUpgrades[kv.Key] = kv.Value;

        uncommittedSpend = 0;
        Save();
    }

    #endregion

    #region Boats

    public static int GetBoatLevel(string boatId) =>
        boatLevels.TryGetValue(boatId, out var lv) ? lv : 0;

    public static void SetBoatLevel(string boatId, int level)
    {
        boatLevels[boatId] = level;
        dirty = true;
    }

    public static string NewBoatInstanceId(string boatId) =>
        $"{boatId}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";

    // Ghi nhận tàu vừa mua vào ô spawn `slot` (chỉ số trong GameController.SpawnSlots)
    public static void AddPendingBoat(string instanceId, string boatId, int slot)
    {
        if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(boatId))
            return;
        if (FindIndex(ownedBoats, instanceId) >= 0 || FindIndex(pendingBoats, instanceId) >= 0)
            return;

        pendingBoats.Add(new OwnedEntry { instanceId = instanceId, boatId = boatId, slot = slot });
    }

    // Danh sách tàu đã chốt để spawn đầu round
    public static List<OwnedBoat> GetOwnedBoats()
    {
        var list = new List<OwnedBoat>(ownedBoats.Count);
        foreach (var e in ownedBoats)
            list.Add(new OwnedBoat(e.instanceId, e.boatId, e.slot));
        return list;
    }

    public static bool IsOwned(string instanceId) => FindIndex(ownedBoats, instanceId) >= 0;

    // Cập nhật ô spawn nếu tàu bị đặt sang ô khác (vd: ô cũ không còn hợp lệ)
    public static void SetBoatSlot(string instanceId, int slot)
    {
        int i = FindIndex(ownedBoats, instanceId);
        if (i >= 0)
        {
            if (ownedBoats[i].slot != slot) { ownedBoats[i].slot = slot; dirty = true; }
            return;
        }

        i = FindIndex(pendingBoats, instanceId);
        if (i >= 0)
            pendingBoats[i].slot = slot;
    }

    // Bỏ hẳn một tàu đã sở hữu (vd: bán tàu) cùng toàn bộ nâng cấp của nó
    public static void RemoveOwnedBoat(string instanceId)
    {
        int i = FindIndex(ownedBoats, instanceId);
        if (i < 0)
            return;
        ownedBoats.RemoveAt(i);
        RemoveUpgrades(upgradeLevels, instanceId);
        RemoveUpgrades(committedUpgrades, instanceId);
        dirty = true;
    }

    // Gọi khi GameObject tàu bị hủy. Tàu đã chốt vẫn thuộc về người chơi (chết trong trận
    // hay đổi scene đều spawn lại round sau) nên giữ nguyên; chỉ dọn tàu chưa chốt.
    public static void ReleaseBoat(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId) || IsOwned(instanceId))
            return;

        int i = FindIndex(pendingBoats, instanceId);
        if (i >= 0)
            pendingBoats.RemoveAt(i);

        RemoveUpgrades(upgradeLevels, instanceId);
    }

    private static int FindIndex(List<OwnedEntry> list, string instanceId)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i].instanceId == instanceId)
                return i;
        return -1;
    }

    private static void RemoveUpgrades(Dictionary<string, int> dict, string instanceId)
    {
        string prefix = instanceId + ":";
        List<string> remove = null;
        foreach (var key in dict.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                (remove ??= new List<string>()).Add(key);
        }

        if (remove == null)
            return;
        foreach (var key in remove)
            dict.Remove(key);
    }

    #endregion

    #region Upgrades / Stats

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

        // Nâng cấp chỉ được lưu khi vào trận (CommitPurchases); thoát trước đó thì hoàn vàng
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
        Mathf.Clamp(GetUpgradeBonus(data, instanceId, StatType.Critical), 0f, CritChanceCap); // %, có cap

    public static float GetEvasion(BoatData data, string instanceId) =>
        Mathf.Clamp(GetUpgradeBonus(data, instanceId, StatType.Evasion), 0f, EvasionCap);

    #endregion

    #region Save / Load

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

        // Battle Progress
        public int currentBattle;
        public int currentRound;
        public int totalRounds;
        public int completedRounds;
        public bool isBattleActive;

        public List<BattleEntry> battleHistory = new();
    }

    public static void Save()
    {
        var s = new SaveData
        {
            gold = Gold + uncommittedSpend,

            currentBattle = currentBattle,
            currentRound = currentRound,
            totalRounds = totalRounds,
            completedRounds = completedRounds,
            isBattleActive = isBattleActive
        };

        foreach (var kv in boatLevels)
            s.boats.Add(new Entry { key = kv.Key, level = kv.Value });

        foreach (var kv in committedUpgrades)
            s.upgrades.Add(new Entry { key = kv.Key, level = kv.Value });

        foreach (var e in ownedBoats)
            s.owned.Add(new OwnedEntry
            {
                instanceId = e.instanceId,
                boatId = e.boatId,
                slot = e.slot
            });

        s.battleHistory.AddRange(battleHistory);

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
        committedUpgrades.Clear();
        boatLevels.Clear();
        ownedBoats.Clear();
        pendingBoats.Clear();
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

        if (s.owned != null)
            foreach (var e in s.owned)
                if (e != null && !string.IsNullOrEmpty(e.instanceId) && !string.IsNullOrEmpty(e.boatId)
                    && FindIndex(ownedBoats, e.instanceId) < 0)
                    ownedBoats.Add(new OwnedEntry { instanceId = e.instanceId, boatId = e.boatId, slot = e.slot });

        // Chỉ nạp nâng cấp của tàu còn sở hữu (bỏ rác của tàu cũ)
        if (s.upgrades != null)
            foreach (var e in s.upgrades)
            {
                if (e == null || string.IsNullOrEmpty(e.key)) continue;
                int sep = e.key.LastIndexOf(':');
                if (sep <= 0 || !IsOwned(e.key.Substring(0, sep))) continue;

                int lv = Mathf.Max(0, e.level);
                upgradeLevels[e.key] = lv;
                committedUpgrades[e.key] = lv;
            }

        OnGoldChanged?.Invoke(Gold);
        currentBattle = s.currentBattle;
        currentRound = s.currentRound;
        totalRounds = s.totalRounds;
        completedRounds = s.completedRounds;
        isBattleActive = s.isBattleActive;

        battleHistory.Clear();

        if (s.battleHistory != null)
            battleHistory.AddRange(s.battleHistory);
    }

    public static void ResetAll()
    {
        Gold = 0;
        uncommittedSpend = 0;
        dirty = false;
        upgradeLevels.Clear();
        committedUpgrades.Clear();
        boatLevels.Clear();
        ownedBoats.Clear();
        pendingBoats.Clear();
        currentBattle = 0;
        currentRound = 0;
        totalRounds = 0;
        completedRounds = 0;
        isBattleActive = false;
        battleHistory.Clear();
        PlayerPrefs.DeleteKey(SaveKey);
        OnGoldChanged?.Invoke(Gold);
    }

    #endregion
}
