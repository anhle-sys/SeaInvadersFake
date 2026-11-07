using UnityEngine;

public class PopupManager : MonoBehaviour
{
    public PurchasePopup PurchasePopup { get; private set; }
    public UpgratePopup UpgratePopup { get; private set; }
    public WinPopup WinPopup { get; private set; }
    public LosePopup LosePopup { get; private set; }

    public GameObject botPopup;

    private void Awake()
    {
        PurchasePopup = GetComponentInChildren<PurchasePopup>(true);
        UpgratePopup = GetComponentInChildren<UpgratePopup>(true);
        WinPopup = GetComponentInChildren<WinPopup>(true);
        LosePopup = GetComponentInChildren<LosePopup>(true);
    }
    public void Init()
    {
        WinPopup.Init();
        LosePopup.Init();
    }
}
