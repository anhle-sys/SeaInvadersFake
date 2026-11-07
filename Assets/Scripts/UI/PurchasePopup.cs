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
        if (data == null || GameController.Instance.IsBattleStarted)
            return;

        int level = GameData.GetBoatLevel(data.id);
        GameObject prefab = data.GetPrefab(level);
        if (prefab == null)
            return;

        SpawnSlot emptySlot = GetFirstEmptySlot();
        if (emptySlot == null)
            return;

        if (!GameData.SpendGoldForPurchase(data.cost))
            return;

        GameObject boat = emptySlot.Spawn(prefab);
        if (boat == null)
        {
            GameData.RefundPurchase(data.cost);
            return;
        }

        if (emptySlot.plane != null)
            emptySlot.plane.SetActive(false);

        // Mỗi lần mua một id riêng -> nâng cấp của tàu này không dùng chung với tàu cùng loại khác
        string instanceId = GameData.NewBoatInstanceId(data.id);

        BoatAIController ai = boat.GetComponent<BoatAIController>();
        if (ai != null)
            ai.Initialize(data, instanceId);

        // Ghi nhận tàu đã mua (chưa chốt): vào trận sẽ được lưu và spawn lại ở các round sau
        int slotIndex = System.Array.IndexOf(GameController.Instance.SpawnSlots, emptySlot);
        GameData.AddPendingBoat(instanceId, data.id, slotIndex);
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