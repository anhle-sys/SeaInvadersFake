using System;
using UnityEngine;

public class PurchasePopup : MonoBehaviour
{
    [Header("Events")]
    [SerializeField] private Vector3EventChannel oceanClicked; // kéo asset OceanClicked vào
    [SerializeField] private GameObject content;
    [SerializeField] private GameObject backBtn;

    public void Show() 
    { 
        content.gameObject.SetActive(true); 
        backBtn.gameObject.SetActive(true);
        GameController.Instance.PopupManager.botPopup.SetActive(false);
        foreach (var spawn in GameController.Instance.SpawnSlots)
        {
            spawn.Init();
            if (spawn.IsOccupied)
            {
                spawn.plane.gameObject.SetActive(false);
            }
            else
            {
                spawn.plane.gameObject.SetActive(true);
            }
        }
    }

    public void Hide()
    {
        content.gameObject.SetActive(false);
        backBtn.gameObject.SetActive(false);
        GameController.Instance.PopupManager.botPopup.SetActive(true);
        foreach (var spawn in GameController.Instance.SpawnSlots) 
            spawn.plane.gameObject.SetActive(false);
        
    }
    public void Purchase(BoatData data)
    {
        GameController gc = GameController.Instance;
        if (data == null || gc.IsBattleStarted)
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