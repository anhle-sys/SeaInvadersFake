using UnityEngine;

// Show / Hide / Purchase được gắn vào Button trong scene (OnClick) -> không đổi tên 3 hàm này
public class PurchasePopup : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private Vector3EventChannel oceanClicked; // kéo asset OceanClicked vào
    [SerializeField] private GameObject content;
    [SerializeField] private GameObject backButton;

    public bool IsOpen => content != null && content.activeSelf;

    public void Show()
    {
        GameController gc = GameController.Instance;
        if (gc == null || !GameController.CanPrepare)
            return;

        content.SetActive(true);
        backButton.SetActive(true);
        PopupManager.SetBottomBarVisible(false);
        ShowFreeSlotMarkers(gc);
    }

    public void Hide()
    {
        content.SetActive(false);
        backButton.SetActive(false);
        PopupManager.SetBottomBarVisible(true);

        GameController gc = GameController.Instance;
        if (gc != null)
            HideSlotMarkers(gc);
    }

    public void Purchase(BoatData data)
    {
        GameController gc = GameController.Instance;
        if (data == null || gc == null || !GameController.CanPrepare)
            return;

        int slotIndex = gc.FindEmptySlotIndex();
        if (slotIndex < 0 || !GameData.SpendGoldForPurchase(data.cost))
            return;

        // Mỗi lần mua một id riêng -> nâng cấp của tàu này không dùng chung với tàu cùng loại khác
        string instanceId = GameData.NewBoatInstanceId(data.id);
        if (gc.SpawnSlots[slotIndex].SpawnBoat(data, instanceId) == null)
        {
            GameData.RefundPurchase(data.cost);
            return;
        }

        // Chưa chốt: vào trận sẽ được lưu và spawn lại ở các round sau
        GameData.AddPendingBoat(instanceId, data.id, slotIndex);
    }

    // Hiện ô đánh dấu ở các slot còn trống để người chơi biết tàu sẽ xuất hiện ở đâu
    private static void ShowFreeSlotMarkers(GameController gc)
    {
        foreach (var slot in gc.SpawnSlots)
        {
            if (slot == null)
                continue;

            slot.Init();
            if (slot.plane != null)
                slot.plane.SetActive(!slot.IsOccupied);
        }
    }

    private static void HideSlotMarkers(GameController gc)
    {
        foreach (var slot in gc.SpawnSlots)
            if (slot != null && slot.plane != null)
                slot.plane.SetActive(false);
    }
}
