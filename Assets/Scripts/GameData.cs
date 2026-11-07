using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dữ liệu người chơi. Có 2 lớp:
/// - <see cref="saved"/>: bản đã chốt, đúng là thứ ghi xuống đĩa (tàu sở hữu, nâng cấp, cấp tàu, tiến trình battle).
///   Chỉ đổi khi vào trận (CommitPurchases), khi Load, hoặc khi tiến trình battle thay đổi.
/// - boats / upgradeLevels: trạng thái đang chơi, gồm cả phần mua trong round chưa chốt.
/// Thoát game trước khi vào trận thì phần chưa chốt không được lưu và vàng đã trả được hoàn lại.
/// </summary>
public static class GameData
{
    public const float CritChanceCap = 75f; // %
    public const float EvasionCap = 60f;    // %
    private const string SaveKey = "GAME_DATA";

    public static int Gold { get; private set; }

    public static event Action<int> OnGoldChanged;
    public static event Action<int> OnNotEnoughGold;
    public static event Action<string, StatType, int> OnUpgradeChanged;

    private static SaveData saved = new();
    private static readonly List<OwnedBoat> boats = new();                    // tàu hiện có (đã chốt + mới mua)
    private static readonly Dictionary<string, int> upgradeLevels = new();    // "instanceId:stat" -> cấp
    private static readonly Dictionary<string, int> boatLevels = new();       // boatId -> cấp tàu
    private static int uncommittedSpend;  // vàng đã chi cho phần chưa chốt
    private static bool dirty;            // có thay đổi (vd: vàng thưởng) chưa ghi -> ghi khi pause/quit

    // Khi tắt Domain Reload: xóa dữ liệu/subscriber cũ của lần Play trước
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ClearState();
        OnGoldChanged = null;
        OnNotEnoughGold = null;
        OnUpgradeChanged = null;
    }

    private static void ClearState()
    {
        Gold = 0;
        uncommittedSpend = 0;
        dirty = false;
        saved = new SaveData();
        boats.Clear();
        upgradeLevels.Clear();
        boatLevels.Clear();
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

    // Chi vàng cho tàu/nâng cấp: chưa vào trận mà thoát thì được hoàn lại
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

    // Vào trận: tàu mới mua + nâng cấp trở thành dữ liệu chính thức và được lưu
    public static void CommitPurchases()
    {
        foreach (var b in boats)
            b.pending = false;

        saved.owned = new List<OwnedBoat>(boats);
        saved.upgrades = ToEntries(upgradeLevels);
        uncommittedSpend = 0;
        Save();
    }

    // Bỏ toàn bộ phần mua chưa chốt: hoàn vàng, quay về bản đã chốt
    public static void DiscardPurchases()
    {
        if (uncommittedSpend > 0)
            RefundPurchase(uncommittedSpend);
        ApplySaved();
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

    // Tàu đã chốt, dùng để spawn lại đầu round
    public static IReadOnlyList<OwnedBoat> GetOwnedBoats() => saved.owned;

    // Ghi nhận tàu vừa mua vào ô spawn `slot` (chỉ số trong GameController.SpawnSlots)
    public static void AddPendingBoat(string instanceId, string boatId, int slot)
    {
        if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(boatId) || FindBoat(instanceId) != null)
            return;
        boats.Add(new OwnedBoat(instanceId, boatId, slot) { pending = true });
    }

    // Gọi khi GameObject tàu bị hủy. Tàu đã chốt vẫn thuộc về người chơi (chết trong trận
    // hay đổi scene đều spawn lại round sau) nên giữ nguyên; chỉ dọn tàu chưa chốt.
    public static void ReleaseBoat(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
            return;

        OwnedBoat boat = FindBoat(instanceId);
        if (boat != null && !boat.pending)
            return;

        if (boat != null)
            boats.Remove(boat);

        string prefix = instanceId + ":";
        var remove = new List<string>();
        foreach (var key in upgradeLevels.Keys)
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                remove.Add(key);
        foreach (var key in remove)
            upgradeLevels.Remove(key);
    }

    private static OwnedBoat FindBoat(string instanceId) =>
        boats.Find(b => b.InstanceId == instanceId);

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
        return up == null ? 0f : up.bonusPerLevel * GetUpgradeLevel(instanceId, stat);
    }

    public static int GetUpgradeCost(BoatData data, string instanceId, StatType stat)
    {
        var up = data.GetUpgrade(stat);
        if (up == null) return 0;
        return Mathf.RoundToInt(up.baseCost * Mathf.Pow(up.costGrowth, GetUpgradeLevel(instanceId, stat)));
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

    public static float GetDamage(BoatData data, string instanceId) =>
        data.GetLevel(GetBoatLevel(data.id)).damage + GetUpgradeBonus(data, instanceId, StatType.Weapon);

    public static float GetArmor(BoatData data, string instanceId) =>
        GetUpgradeBonus(data, instanceId, StatType.Armor);

    public static float GetCritChance(BoatData data, string instanceId) =>
        Mathf.Clamp(GetUpgradeBonus(data, instanceId, StatType.Critical), 0f, CritChanceCap);

    public static float GetEvasion(BoatData data, string instanceId) =>
        Mathf.Clamp(GetUpgradeBonus(data, instanceId, StatType.Evasion), 0f, EvasionCap);

    #endregion

    #region Battle progress

    public static int CurrentBattle => saved.currentBattle;
    public static int CurrentRound => saved.currentRound;
    public static int TotalRounds => saved.totalRounds;
    public static int CompletedRounds => saved.completedRounds;
    public static bool IsBattleActive => saved.isBattleActive;
    public static IReadOnlyList<BattleEntry> BattleHistory => saved.battleHistory;

    public static void StartNewBattle(int rounds)
    {
        DiscardPurchases(); // bỏ phần mua dở của battle trước (có hoàn vàng)

        saved.currentBattle++;
        saved.currentRound = 1;
        saved.totalRounds = Mathf.Max(1, rounds);
        saved.completedRounds = 0;
        saved.isBattleActive = true;
        Save();

        Debug.Log($"Start Battle {saved.currentBattle} - {saved.totalRounds} Rounds");
    }

    // Trả về true khi đã xong round cuối của battle
    public static bool CompleteCurrentRound()
    {
        if (!saved.isBattleActive)
            return false;

        saved.completedRounds++;
        bool finished = saved.completedRounds >= saved.totalRounds;

        if (finished)
        {
            saved.isBattleActive = false;
            saved.battleHistory.Add(new BattleEntry
            {
                battleNumber = saved.currentBattle,
                totalRounds = saved.totalRounds,
                completedRounds = saved.completedRounds
            });
            Debug.Log($"Battle {saved.currentBattle} Completed!");
        }
        else
        {
            saved.currentRound++;
            Debug.Log($"Battle {saved.currentBattle} - Round {saved.currentRound}");
        }

        Save();
        return finished;
    }

    #endregion

    #region Save / Load

    [Serializable]
    public class OwnedBoat
    {
        [SerializeField] private string instanceId;
        [SerializeField] private string boatId;
        [SerializeField] private int slot;
        [NonSerialized] internal bool pending; // mua trong round này, chưa vào trận

        public string InstanceId => instanceId;
        public string BoatId => boatId;
        public int Slot => slot;

        private OwnedBoat() { } // cho JsonUtility

        public OwnedBoat(string instanceId, string boatId, int slot)
        {
            this.instanceId = instanceId;
            this.boatId = boatId;
            this.slot = slot;
        }
    }

    [Serializable]
    public class BattleEntry
    {
        public int battleNumber;
        public int totalRounds;
        public int completedRounds;
    }

    [Serializable]
    private class Entry
    {
        public string key;
        public int level;
    }

    [Serializable]
    private class SaveData
    {
        public int gold;
        public List<Entry> upgrades = new();
        public List<Entry> boats = new();
        public List<OwnedBoat> owned = new();

        public int currentBattle;
        public int currentRound;
        public int totalRounds;
        public int completedRounds;
        public bool isBattleActive;
        public List<BattleEntry> battleHistory = new();
    }

    public static void Save()
    {
        saved.gold = Gold + uncommittedSpend; // phần chưa chốt không được lưu -> coi như hoàn vàng
        saved.boats = ToEntries(boatLevels);
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(saved));
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
        ClearState();

        SaveData s = Read();
        if (s != null)
        {
            saved = s;
            Gold = Mathf.Max(0, s.gold);
        }
        else
        {
            Gold = defaultGold;
        }

        foreach (var e in saved.boats)
            if (e != null && !string.IsNullOrEmpty(e.key))
                boatLevels[e.key] = Mathf.Max(0, e.level);

        ApplySaved();
        OnGoldChanged?.Invoke(Gold);
    }

    public static void ResetAll()
    {
        ClearState();
        PlayerPrefs.DeleteKey(SaveKey);
        OnGoldChanged?.Invoke(Gold);
    }

    private static SaveData Read()
    {
        if (!PlayerPrefs.HasKey(SaveKey))
            return null;
        try
        {
            var s = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(SaveKey));
            if (s == null) return null;
            s.owned ??= new List<OwnedBoat>();
            s.upgrades ??= new List<Entry>();
            s.boats ??= new List<Entry>();
            s.battleHistory ??= new List<BattleEntry>();
            s.owned.RemoveAll(b => b == null || string.IsNullOrEmpty(b.InstanceId) || string.IsNullOrEmpty(b.BoatId));
            return s;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"GameData: dữ liệu lưu bị hỏng, dùng dữ liệu mặc định. {e.Message}");
            return null;
        }
    }

    // Dựng lại tàu + nâng cấp đang chơi từ bản đã chốt
    private static void ApplySaved()
    {
        uncommittedSpend = 0;

        boats.Clear();
        boats.AddRange(saved.owned);

        // Chỉ nạp nâng cấp của tàu còn sở hữu (bỏ rác của tàu cũ)
        upgradeLevels.Clear();
        foreach (var e in saved.upgrades)
        {
            if (e == null || string.IsNullOrEmpty(e.key)) continue;
            int sep = e.key.LastIndexOf(':');
            if (sep > 0 && FindBoat(e.key.Substring(0, sep)) != null)
                upgradeLevels[e.key] = Mathf.Max(0, e.level);
        }
    }

    private static List<Entry> ToEntries(Dictionary<string, int> dict)
    {
        var list = new List<Entry>(dict.Count);
        foreach (var kv in dict)
            list.Add(new Entry { key = kv.Key, level = kv.Value });
        return list;
    }

    #endregion
}
