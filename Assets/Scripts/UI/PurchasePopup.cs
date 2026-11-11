using System;
using UnityEngine;

public class PurchasePopup : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private Vector3EventChannel oceanClicked; // kéo asset OceanClicked vào
    [SerializeField] private GameObject content;
    [SerializeField] private GameObject backBtn;

    public bool IsOpen => content != null && content.activeSelf;

    public void Show()
    { 
        GameController gc = GameController.Instance;
        if (gc == null || !GameController.CanPrepare)
            return;

        content.gameObject.SetActive(true);
        backBtn.gameObject.SetActive(true);
        SetBotPopupActive(gc, false);
        foreach (var spawn in gc.SpawnSlots)
        {
            if (spawn == null)
                continue;

            spawn.Init();
            if (spawn.plane != null)
                spawn.plane.SetActive(!spawn.IsOccupied);
        }
    }

    public void Hide()
    {
        content.gameObject.SetActive(false);
        backBtn.gameObject.SetActive(false);

        GameController gc = GameController.Instance;
        if (gc == null)
            return;

        SetBotPopupActive(gc, true);
        foreach (var spawn in gc.SpawnSlots)
            if (spawn != null && spawn.plane != null)
                spawn.plane.SetActive(false);
    }

    private static void SetBotPopupActive(GameController gc, bool active)
    {
        if (gc.PopupManager != null && gc.PopupManager.botPopup != null)
            gc.PopupManager.botPopup.SetActive(active);
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
}