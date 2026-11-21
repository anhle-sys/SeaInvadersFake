using UnityEngine;

public class PopupManager : MonoBehaviour
{
    public PurchasePopup PurchasePopup { get; private set; }
    public UpgradePopup UpgradePopup { get; private set; }
    public WinPopup WinPopup { get; private set; }
    public LosePopup LosePopup { get; private set; }

    [Tooltip("Thanh dưới cùng (Fight, + tàu, Replay): ẩn khi đang mở popup mua / nâng cấp")]
    public GameObject bottomBar;

    private void Awake()
    {
        PurchasePopup = GetComponentInChildren<PurchasePopup>(true);
        UpgradePopup = GetComponentInChildren<UpgradePopup>(true);
        WinPopup = GetComponentInChildren<WinPopup>(true);
        LosePopup = GetComponentInChildren<LosePopup>(true);
    }

    public void BindResultButtons()
    {
        WinPopup.BindButtons();
        LosePopup.BindButtons();
    }

    // An toàn khi chưa có GameController / PopupManager (đổi scene, test ở scene khác)
    public static void SetBottomBarVisible(bool visible)
    {
        GameController gc = GameController.Instance;
        if (gc != null && gc.PopupManager != null && gc.PopupManager.bottomBar != null)
            gc.PopupManager.bottomBar.SetActive(visible);
    }
}
