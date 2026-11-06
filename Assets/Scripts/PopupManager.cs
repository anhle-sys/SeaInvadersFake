using UnityEngine;

public class PopupManager : MonoBehaviour
{
    public PurchasePopup PurchasePopup { get; private set; }
    public UpgratePopup UpgratePopup { get; private set; }

    public GameObject botPopup;

    private void Awake()
    {
        // Trước đây 2 property này không bao giờ được gán -> luôn null
        PurchasePopup = GetComponentInChildren<PurchasePopup>(true);
        UpgratePopup = GetComponentInChildren<UpgratePopup>(true);
    }
}
