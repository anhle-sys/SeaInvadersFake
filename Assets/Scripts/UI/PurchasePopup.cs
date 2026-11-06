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
            if(spawn.IsOccupied)
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
        // Chỉ được mua trong pha chuẩn bị
        if (data == null || GameController.Instance.IsBattleStarted)
            return;

        int level = GameData.GetBoatLevel(data.id);
        GameObject prefab = data.GetPrefab(level);
        if (prefab == null)
            return;

        SpawnSlot emptySlot = GetFirstEmptySlot();
        if (emptySlot == null)
            return;

        // Tàu chưa được lưu: vàng chỉ bị chốt khi trận bắt đầu (GameData.CommitPurchases).
        // Thoát game trước đó thì lần sau mở lại sẽ được hoàn vàng.
        if (!GameData.SpendGoldForPurchase(data.cost))
            return;

        GameObject boat = emptySlot.Spawn(prefab);
        if (boat == null)
        {
            GameData.RefundPurchase(data.cost);
            return;
        }

        BoatAIController ai = boat.GetComponent<BoatAIController>();
        if (ai != null)
            ai.Initialize(data);

    }

    private SpawnSlot GetFirstEmptySlot()
    {
        foreach (SpawnSlot slot in GameController.Instance.SpawnSlots)
        {
            if (slot != null && !slot.IsOccupied)
                return slot;
        }

        return null;
    }
}