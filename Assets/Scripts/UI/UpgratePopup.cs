using UnityEngine;
using UnityEngine.UI;

public class UpgratePopup : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private BoatDataEventChannel boatClicked; // kéo asset BoatClicked vào

    [Header("References")]
    [SerializeField] private GunnerUpgratePopup gunnerUpgratePopup;
    [SerializeField] private BangerUpgratePopup bangerUpgratePopup;
    [SerializeField] private CarrierUpgratePopup carrierUpgratePopup;
    [SerializeField] private Button back;

    [Header("Focus & hiệu ứng")]
    [SerializeField] private UpgradeHitFx hitFx;

    private Transform selectedBoat;
    private BoatData selectedData;
    private string selectedInstanceId;   // id riêng của tàu đang chọn: nâng cấp tính theo từng tàu

    public bool IsOpen => selectedData != null;

    private void Awake()
    {
        if (boatClicked != null)
            boatClicked.OnRaised += Show;
        GameData.OnUpgradeChanged += HandleUpgradeChanged;
    }

    private void OnDestroy()
    {
        if (boatClicked != null)
            boatClicked.OnRaised -= Show;
        GameData.OnUpgradeChanged -= HandleUpgradeChanged;
    }

public void Show(BoatSelection selection)
    {
        if (selection.Data == null)
            return;

        if (string.IsNullOrEmpty(selection.InstanceId))
        {
            Debug.LogError("UpgratePopup: tàu được chọn chưa có InstanceId, không thể nâng cấp.");
            return;
        }

        // Chỉ nâng cấp trong pha chuẩn bị
        GameController gc = GameController.Instance;
        if (gc == null || gc.IsBattleStarted)
            return;

        selectedData = selection.Data;
        selectedInstanceId = selection.InstanceId;
        selectedBoat = selection.Boat;

        SetupUI(selection.Data, selection.InstanceId);
        SetBotPopupActive(false);

        if (back != null)
        {
            back.gameObject.SetActive(true);
            back.onClick.RemoveAllListeners();
            back.onClick.AddListener(Hide);
        }

        gc.state = GameState.Upgrade;
        if (gc.CameraController != null && selectedBoat != null)
            gc.CameraController.FocusOnBoat(selectedBoat);
    }

private void SetupUI(BoatData boatData, string instanceId)
    {
        switch (boatData.boatType)
        {
            case BoatType.Gunner:
                gunnerUpgratePopup.gameObject.SetActive(true);
                bangerUpgratePopup.gameObject.SetActive(false);
                carrierUpgratePopup.gameObject.SetActive(false);
                gunnerUpgratePopup.SetupUI(boatData, instanceId);
                break;
            case BoatType.Banger:
                gunnerUpgratePopup.gameObject.SetActive(false);
                bangerUpgratePopup.gameObject.SetActive(true);
                carrierUpgratePopup.gameObject.SetActive(false);
                bangerUpgratePopup.SetupUI(boatData, instanceId);
                break;
            case BoatType.Carrier:
                gunnerUpgratePopup.gameObject.SetActive(false);
                bangerUpgratePopup.gameObject.SetActive(false);
                carrierUpgratePopup.gameObject.SetActive(true);
                carrierUpgratePopup.SetupUI(boatData, instanceId);
                break;
            default:
                Debug.LogError($"Unsupported boat type: {boatData.boatType}");
                break;
        }
    }

    public void Hide()
    {
        bool wasOpen = IsOpen;
        selectedBoat = null;
        selectedData = null;
        selectedInstanceId = null;

        gunnerUpgratePopup.gameObject.SetActive(false);
        bangerUpgratePopup.gameObject.SetActive(false);
        carrierUpgratePopup.gameObject.SetActive(false);
        if (back != null)
            back.gameObject.SetActive(false);
        SetBotPopupActive(true);

        // Trả camera về góc nhìn chuẩn bị
        GameController gc = GameController.Instance;
        if (wasOpen && gc != null && gc.state == GameState.Upgrade)
        {
            gc.state = GameState.Idle;
            if (gc.CameraController != null)
                gc.CameraController.ChangeCameraByState();
        }
    }

    public void Back() => Hide();

private void HandleUpgradeChanged(string instanceId, StatType stat, int level)
    {
        if (selectedData == null || selectedInstanceId != instanceId || selectedBoat == null)
            return;

        if (hitFx != null)
            hitFx.Play(selectedBoat);
    }

    private static void SetBotPopupActive(bool active)
    {
        GameController gc = GameController.Instance;
        if (gc != null && gc.PopupManager != null && gc.PopupManager.botPopup != null)
            gc.PopupManager.botPopup.SetActive(active);
    }
}
