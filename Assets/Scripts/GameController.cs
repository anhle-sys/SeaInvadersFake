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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
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
        if (data == null || data.GetPrefab(0) == null)
        {
            return false;
        }

        SpawnSlot emptySlot = GetFirstEmptySlot();

        if (emptySlot == null)
        {
            return false;
        }

        emptySlot.Spawn(data.GetPrefab(0));


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