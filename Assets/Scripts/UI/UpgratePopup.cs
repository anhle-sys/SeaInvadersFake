using System;
using UnityEngine;

public class UpgratePopup : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject popup;
    [SerializeField] private GunnerUpgratePopup gunnerUpgratePopup;
    [SerializeField] private BangerUpgratePopup bangerUpgratePopup;
    [SerializeField] private CarrierUpgratePopup carrierUpgratePopup;

    public void Show(BoatData boatData)
    {
        popup.SetActive(true);
        SetupUI(boatData);
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
        popup.SetActive(false);
    }
    
    public void Back()
    {
        Hide();
    }
}
