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
    [SerializeField] private GameObject botUI;
    public bool IsBattleStarted;
    private bool fightPending;   // đã bấm Fight, đang chờ hiệu ứng buff trước khi vào trận

    // An toàn khi chưa có / đã hủy GameController (đổi scene, test prefab ở scene khác)
    public static bool BattleStarted => Instance != null && Instance.IsBattleStarted;
    // Còn được mua tàu / nâng cấp: chưa vào trận và chưa bấm Fight
    public static bool CanPrepare => Instance != null && !Instance.IsBattleStarted && !Instance.fightPending;

    public static event Action OnBattleStarted;
    [Header("Spawn Slots")]
    [SerializeField] private SpawnSlot[] spawnSlots;
    public SpawnSlot[] SpawnSlots => spawnSlots;
    [SerializeField] public Transform enemyTower;
    [SerializeField] public Transform allyTower;
    [Header("Economy")]
    [SerializeField] private int startingGold = 500;
    [Header("Boats")]
    [SerializeField] private BoatData[] boatCatalog;
    [Header("Round")]
    [SerializeField] private RoundUI roundUI;
    [SerializeField] private Button replayBtn;
    [SerializeField] private float roundCheckInterval = 0.5f;

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
        replayBtn.onClick.AddListener(LoadScene);
    }

    private void Start()
    {
        if (!GameData.IsBattleActive)
            GameData.StartNewBattle(roundUI != null ? roundUI.RoundCount : 1);

        if (roundUI != null) roundUI.Refresh();
        popupManager.Init();
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

        if (enemies == 0) { EndRound(true); PopupManager.WinPopup.Show(); }
        else if (allies == 0) { EndRound(false); PopupManager.LosePopup.Show(); }
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

    public void LoadScene()
    {
        GameData.SaveIfDirty();
        DOTween.KillAll();
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            "Battle",
            UnityEngine.SceneManagement.LoadSceneMode.Single
        );
    }

    public void SpawnOwnedBoats()
    {
        foreach (var slot in spawnSlots)
            if (slot != null) slot.Init();   // nhận diện tàu đặt sẵn trong scene

        foreach (var owned in GameData.GetOwnedBoats())
        {
            BoatData data = Array.Find(boatCatalog, d => d != null && d.id == owned.BoatId);
            int index = IsEmptySlot(owned.Slot) ? owned.Slot : FindEmptySlotIndex();

            if (data == null || index < 0 || spawnSlots[index].SpawnBoat(data, owned.InstanceId) == null)
                Debug.LogError($"GameController: không spawn được tàu '{owned.InstanceId}' ({owned.BoatId}).");
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
        if (IsBattleStarted || fightPending)
            return;

        fightPending = true;   // khóa mua tàu / nâng cấp trong lúc chờ vào trận
        fightBtn.interactable = false;

        BuffZone zone = timingBar != null ? timingBar.StopAndGetZone() : null;
        ShowImageBuff();
        GoldBuff.Set(zone != null ? zone.buffValue : 1f);
        // SetLink: GameController bị hủy (đổi scene) thì không gọi StartGame trên object đã chết
        DOVirtual.DelayedCall(0.75f, StartGame).SetLink(gameObject);
    }
    public void StartGame()
    {
        if (IsBattleStarted)
            return;

        // Popup nâng cấp / mua tàu còn mở thì đóng lại, không để nó treo trên màn hình trong trận
        if (popupManager != null)
        {
            if (popupManager.UpgratePopup != null && popupManager.UpgratePopup.IsOpen)
                popupManager.UpgratePopup.Hide();
            if (popupManager.PurchasePopup != null && popupManager.PurchasePopup.IsOpen)
                popupManager.PurchasePopup.Hide();
        }

        IsBattleStarted = true;
        state = GameState.Battle;
        GameData.CommitPurchases();   // tàu/nâng cấp đã mua giờ được dùng: chốt và lưu
        OnBattleStarted?.Invoke();
        botUI.SetActive(false);
        cameraController.ChangeCameraByState();
    }
    private void ShowImageBuff()
    {
        if (imageBuff == null)
            return;

        imageBuff.gameObject.SetActive(true);

        imageBuff.transform.localScale = Vector3.zero;

        imageBuff.transform.DOScale(Vector3.one, 0.3f)
            .SetEase(Ease.OutBack)
            .SetLink(imageBuff.gameObject)
            .OnComplete(() =>
            {
                imageBuff.transform.DOScale(Vector3.zero, 0.2f)
                    .SetDelay(0.5f)
                    .SetEase(Ease.InBack)
                    .SetLink(imageBuff.gameObject)
                    .OnComplete(() =>
                    {
                        imageBuff.gameObject.SetActive(false);
                    });
            });
    }
}