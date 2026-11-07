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
    public CameraController CameraController => cameraController;
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
    [Header("Boats")]
    [Tooltip("Tất cả BoatData người chơi có thể mua - dùng để tìm lại tàu đã lưu theo id")]
    [SerializeField] private BoatData[] boatCatalog;
    [Header("Round")]
    [SerializeField] private RoundUI roundUI;
    [SerializeField] private Button replayBtn;
    [SerializeField] private float roundCheckInterval = 0.5f;

    /// <summary>true = thắng round (hết tàu địch), false = thua (hết tàu ta).</summary>
    public static event Action<bool> OnRoundEnded;

    private bool roundOver;
    private float roundCheckTimer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        GoldBuff.Reset();
        GameData.Load(startingGold);
        fightBtn.onClick.AddListener(OnFightClicked);
        replayBtn.onClick.AddListener(Replay);
        replayBtn.gameObject.SetActive(false);
    }

    private void Start()
    {
        // Chưa có battle (lần đầu, hoặc battle trước đã xong): mở battle mới, số round = số ô trên UI
        if (!GameData.IsBattleActive)
            GameData.StartNewBattle(roundUI != null ? roundUI.RoundCount : 1);

        if (roundUI != null) roundUI.Refresh();
        SpawnOwnedBoats();
    }

    private void Update()
    {
        if (!IsBattleStarted || roundOver)
            return;

        roundCheckTimer -= Time.deltaTime;
        if (roundCheckTimer > 0f)
            return;
        roundCheckTimer = roundCheckInterval;

        int allies = 0, enemies = 0;
        foreach (var boat in FindObjectsByType<BoatHealth>(FindObjectsSortMode.None))
        {
            if (boat.IsDead) continue;
            if (boat.IsAlly) allies++; else enemies++;
        }

        if (enemies == 0) EndRound(true);
        else if (allies == 0) EndRound(false);
    }

    // Thắng: sang round sau (round cuối thì xong battle). Thua: đánh lại round này.
    private void EndRound(bool won)
    {
        roundOver = true;
        state = GameState.GameOver;

        if (won)
            GameData.CompleteCurrentRound();   // có Save bên trong
        else
            GameData.SaveIfDirty();

        if (roundUI != null) roundUI.Refresh();

        var label = replayBtn.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (label != null)
            label.text = !won ? "Replay" : GameData.IsBattleActive ? "Next Round" : "New Battle";
        replayBtn.gameObject.SetActive(true);

        OnRoundEnded?.Invoke(won);
    }

    // Load lại scene: tàu đã chốt được spawn lại, round lấy từ GameData
    public void Replay()
    {
        GameData.SaveIfDirty();
        DOTween.KillAll();
        UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.buildIndex);
    }

    // Đầu mỗi round: đặt lại toàn bộ tàu người chơi đã sở hữu vào ô spawn của chúng
    public void SpawnOwnedBoats()
    {
        foreach (var slot in spawnSlots)
            if (slot != null) slot.Init();   // nhận diện tàu đặt sẵn trong scene

        foreach (var owned in GameData.GetOwnedBoats())
        {
            BoatData data = Array.Find(boatCatalog, d => d != null && d.id == owned.BoatId);
            // Ô cũ không dùng được (đã có tàu / không còn) thì lấy ô trống đầu tiên
            int index = IsEmptySlot(owned.Slot) ? owned.Slot : FindEmptySlotIndex();

            if (data == null || index < 0 || spawnSlots[index].SpawnBoat(data, owned.InstanceId) == null)
                Debug.LogWarning($"GameController: không spawn được tàu '{owned.InstanceId}' ({owned.BoatId}).");
        }
    }

    public int FindEmptySlotIndex()
    {
        for (int i = 0; i < spawnSlots.Length; i++)
            if (IsEmptySlot(i))
                return i;
        return -1;
    }

    private bool IsEmptySlot(int i) =>
        i >= 0 && i < spawnSlots.Length && spawnSlots[i] != null && !spawnSlots[i].IsOccupied;

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused)
            GameData.SaveIfDirty();
    }

    private void OnApplicationQuit() => GameData.SaveIfDirty();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        OnBattleStarted = null;
        OnRoundEnded = null;
    }

    private void OnFightClicked()
    {
        fightBtn.interactable = false;

        BuffZone zone = timingBar.StopAndGetZone();
        ShowImageBuff();
        GoldBuff.Set(zone != null ? zone.buffValue : 1f);
        StartGame();
    }
    public void StartGame()
    {
        if (IsBattleStarted)
            return;

        IsBattleStarted = true;
        state = GameState.Battle;
        GameData.CommitPurchases();   // tàu/nâng cấp đã mua giờ được dùng: chốt và lưu
        OnBattleStarted?.Invoke();
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