using TMPro;
using UnityEngine;

public class PurchasePopup : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject popup;

    public void Show()
    {
        popup.SetActive(true);

    }

    public void Hide()
    {
        popup.SetActive(false);
    }
    public void PurchaseBoat(BoatData boatData)
    {
        bool success = GameController.Instance.Purchase(boatData);
    }
    public void Back()
    {
        Hide();
    }
}
