using UnityEngine;
using UnityEngine.UI;

public class UpgradePopup : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private BoatDataEventChannel boatClicked; // kéo asset BoatClicked vào

    [Header("References")]
    [SerializeField] private GunnerUpgradePopup gunnerPanel;
    [SerializeField] private BangerUpgradePopup bangerPanel;
    [SerializeField] private CarrierUpgradePopup carrierPanel;
    [SerializeField] private Button backButton;

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
            Debug.LogError("UpgradePopup: tàu được chọn chưa có InstanceId, không thể nâng cấp.");
            return;
        }

        // Chỉ nâng cấp trong pha chuẩn bị
        GameController gc = GameController.Instance;
        if (gc == null || !GameController.CanPrepare)
            return;

        selectedData = selection.Data;
        selectedInstanceId = selection.InstanceId;
        selectedBoat = selection.Boat;

        ShowPanelFor(selection.Data, selection.InstanceId);
        PopupManager.SetBottomBarVisible(false);

        if (backButton != null)
        {
            backButton.gameObject.SetActive(true);
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(Hide);
        }

        gc.state = GameState.Upgrade;
        if (gc.CameraController != null && selectedBoat != null)
            gc.CameraController.FocusOnBoat(selectedBoat);
    }

    // Bật đúng panel theo loại tàu, tắt 2 panel còn lại
    private void ShowPanelFor(BoatData boatData, string instanceId)
    {
        gunnerPanel.gameObject.SetActive(boatData.boatType == BoatType.Gunner);
        bangerPanel.gameObject.SetActive(boatData.boatType == BoatType.Banger);
        carrierPanel.gameObject.SetActive(boatData.boatType == BoatType.Carrier);

        switch (boatData.boatType)
        {
            case BoatType.Gunner:
                gunnerPanel.Refresh(boatData, instanceId);
                break;
            case BoatType.Banger:
                bangerPanel.Refresh(boatData, instanceId);
                break;
            case BoatType.Carrier:
                carrierPanel.Refresh(boatData, instanceId);
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

        gunnerPanel.gameObject.SetActive(false);
        bangerPanel.gameObject.SetActive(false);
        carrierPanel.gameObject.SetActive(false);
        if (backButton != null)
            backButton.gameObject.SetActive(false);
        PopupManager.SetBottomBarVisible(true);

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
}
