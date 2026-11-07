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
    }

    private void Start() => SpawnOwnedBoats();

    // Đầu mỗi round: đặt lại toàn bộ tàu người chơi đã sở hữu vào ô spawn của chúng
    public void SpawnOwnedBoats()
    {
        if (spawnSlots == null)
            return;

        foreach (var slot in spawnSlots)
            if (slot != null) slot.Init();   // nhận diện tàu đặt sẵn trong scene

        foreach (var owned in GameData.GetOwnedBoats())
        {
            BoatData data = FindBoatData(owned.BoatId);
            if (data == null)
            {
                Debug.LogWarning($"GameController: không tìm thấy BoatData id '{owned.BoatId}' trong boatCatalog.");
                continue;
            }

            GameObject prefab = data.GetPrefab(GameData.GetBoatLevel(data.id));
            if (prefab == null)
                continue;

            int index = owned.Slot;
            if (index < 0 || index >= spawnSlots.Length || spawnSlots[index] == null || spawnSlots[index].IsOccupied)
                index = FindEmptySlotIndex();
            if (index < 0)
            {
                Debug.LogWarning($"GameController: hết ô trống để spawn tàu '{owned.InstanceId}'.");
                continue;
            }

            SpawnSlot slot = spawnSlots[index];
            GameObject boat = slot.Spawn(prefab);
            if (boat == null)
                continue;

            if (slot.plane != null)
                slot.plane.SetActive(false);

            BoatAIController ai = boat.GetComponent<BoatAIController>();
            if (ai != null)
                ai.Initialize(data, owned.InstanceId);

            if (index != owned.Slot)
                GameData.SetBoatSlot(owned.InstanceId, index);
        }
    }

    private BoatData FindBoatData(string boatId)
    {
        if (boatCatalog == null)
            return null;
        foreach (var d in boatCatalog)
            if (d != null && d.id == boatId)
                return d;
        return null;
    }

    private int FindEmptySlotIndex()
    {
        for (int i = 0; i < spawnSlots.Length; i++)
            if (spawnSlots[i] != null && !spawnSlots[i].IsOccupied)
                return i;
        return -1;
    }

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
    }

    public void StartBattle()
    {
        if (IsBattleStarted)
            return;

        IsBattleStarted = true;
        GameData.CommitPurchases();   // tàu đã mua giờ được dùng: chốt số vàng đã chi
        OnBattleStarted?.Invoke();
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