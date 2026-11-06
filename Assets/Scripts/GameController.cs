using UnityEngine;
using System;
using UnityEngine.UI;
using DG.Tweening;

public enum GameState
{
    Idle,
    Battle,
    Upgrade,
    GameOver
}

public class GameController : MonoBehaviour
{
    public static GameController Instance { get; private set; }
    public GameState state = GameState.Idle;
    [SerializeField] private PopupManager popupManager;
    public PopupManager PopupManager => popupManager;
    [SerializeField] private HealthBarManager healthBarManager;
    public HealthBarManager HealthBarManager => healthBarManager;
    [SerializeField] private CameraController cameraController;
    [SerializeField] private TimingBar timingBar;
    [SerializeField] private Button fightBtn;
    [SerializeField] private Image imageBuff;

    public bool IsBattleStarted;
    public static event Action OnBattleStarted;
    [Header("Spawn Slots")]
    [SerializeField] private SpawnSlot[] spawnSlots;
    public SpawnSlot[] SpawnSlots => spawnSlots;
    [SerializeField] public Transform enemyTower;
    [SerializeField] public Transform allyTower;
    [Header("Economy")]
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
        fightBtn.onClick.AddListener(OnFightClicked);
    }
    
    public void StartBattle()
    {
        if (IsBattleStarted)
            return;

        IsBattleStarted = true;
        OnBattleStarted?.Invoke();
    }
    
    private void OnFightClicked()
    {
        fightBtn.interactable = false;

        BuffZone zone = timingBar.StopAndGetZone();
        ShowImageBuff();
        GoldBuff.Set(zone.buffValue);
        StartGame();
    }
    public void StartGame()
    {
        state = GameState.Battle;
        StartBattle();
        timingBar.gameObject.SetActive(false);
        cameraController.ChangeCameraByState();
    }
    private void ShowImageBuff()
    {
        imageBuff.gameObject.SetActive(true);

        imageBuff.transform.localScale = Vector3.zero;

        imageBuff.transform.DOScale(Vector3.one, 0.3f)
            .SetEase(Ease.OutBack)
            .OnComplete(() =>
            {
                imageBuff.transform.DOScale(Vector3.zero, 0.2f)
                    .SetDelay(0.5f)
                    .SetEase(Ease.InBack)
                    .OnComplete(() =>
                    {
                        imageBuff.gameObject.SetActive(false);
                    });
            });
    }
}