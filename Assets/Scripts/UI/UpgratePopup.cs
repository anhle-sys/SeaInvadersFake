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

    private void Awake()
    {
        boatClicked.OnRaised += Show;
    }

    private void OnDestroy()
    {
        boatClicked.OnRaised -= Show;
    }

    public void Show(BoatData boatData)
    {
        SetupUI(boatData);
        GameController.Instance.PopupManager.botPopup.SetActive(false);
        back.gameObject.SetActive(true);
        back.onClick.RemoveAllListeners();
        back.onClick.AddListener(() =>
        {
            Hide();
        });
    }

    private void SetupUI(BoatData boatData)
    {
        switch (boatData.boatType)
        {
            case BoatType.Gunner:
                gunnerUpgratePopup.gameObject.SetActive(true);
                bangerUpgratePopup.gameObject.SetActive(false);
                carrierUpgratePopup.gameObject.SetActive(false);
                gunnerUpgratePopup.SetupUI(boatData);
                break;
            case BoatType.Banger:
                gunnerUpgratePopup.gameObject.SetActive(false);
                bangerUpgratePopup.gameObject.SetActive(true);
                carrierUpgratePopup.gameObject.SetActive(false);
                bangerUpgratePopup.SetupUI(boatData);
                break;
            case BoatType.Carrier:
                gunnerUpgratePopup.gameObject.SetActive(false);
                bangerUpgratePopup.gameObject.SetActive(false);
                carrierUpgratePopup.gameObject.SetActive(true);
                carrierUpgratePopup.SetupUI(boatData);
                break;
            default:
                Debug.LogError($"Unsupported boat type: {boatData.boatType}");
                break;
        }
    }

    public void Hide()
    {
        gunnerUpgratePopup.gameObject.SetActive(false);
        bangerUpgratePopup.gameObject.SetActive(false);
        carrierUpgratePopup.gameObject.SetActive(false);
        back.gameObject.SetActive(false);
        GameController.Instance.PopupManager.botPopup.SetActive(true);
    }

    public void Back() => Hide();
}