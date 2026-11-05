using UnityEngine;
using System;
using UnityEngine.UI;

public class GameController : MonoBehaviour
{
    public static GameController Instance { get; private set; }
    public GameDataController GameDataController { get; private set; }
    public bool IsBattleStarted;
    public static event Action OnBattleStarted;
    [Header("Spawn Slots")]
    [SerializeField] private SpawnSlot[] spawnSlots;
    [SerializeField] private Button startgame;
    [SerializeField] public Transform enemyTower;
    [SerializeField] public Transform allyTower;
    [Header("Economy")]
    [Tooltip("Số vàng khởi đầu khi chưa có file save")]
    [SerializeField] private int startingGold = 500;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        GameData.Load(startingGold);
    }
    public void StartBattle()
    {
        if (IsBattleStarted)
            return;

        IsBattleStarted = true;
        OnBattleStarted?.Invoke();
    }
    public bool Purchase(BoatData data)
    {
        // Chỉ được mua trong pha chuẩn bị
        if (data == null || IsBattleStarted)
            return false;

        int level = GameData.GetBoatLevel(data.id);
        GameObject prefab = data.GetPrefab(level);
        if (prefab == null)
            return false;

        SpawnSlot emptySlot = GetFirstEmptySlot();
        if (emptySlot == null)
            return false;

        if (!GameData.SpendGold(data.cost))
            return false;

        GameObject boat = emptySlot.Spawn(prefab);
        if (boat == null)
        {
            GameData.AddGold(data.cost); // spawn lỗi -> hoàn tiền
            return false;
        }

        GameData.Save();

        // Khởi tạo bằng đúng BoatData vừa mua (Start() sẽ bỏ qua vì đã initialized)
        BoatAIController ai = boat.GetComponent<BoatAIController>();
        if (ai != null)
            ai.Initialize(data);

        return true;
    }

    private SpawnSlot GetFirstEmptySlot()
    {
        foreach (SpawnSlot slot in spawnSlots)
        {
            if (slot != null && !slot.IsOccupied)
                return slot;
        }

        return null;
    }
    public void StartGame()
    {
        StartBattle();
        startgame.gameObject.SetActive(false);
    }
}