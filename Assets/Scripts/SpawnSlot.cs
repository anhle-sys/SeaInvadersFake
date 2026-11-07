using UnityEngine;

public class SpawnSlot : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField] private Transform spawnPoint;

    public GameObject plane;
    public GameObject CurrentObject { get; private set; }

    public bool IsOccupied => CurrentObject != null;
    public void Init()
    {
        BoatCombatBase boatCombat = GetComponentInChildren<BoatCombatBase>();

        if (boatCombat != null)
        {
            CurrentObject = boatCombat.gameObject;
        }
        else
        {
            return;
        }
    }

    public GameObject Spawn(GameObject prefab)
    {
        if (IsOccupied || prefab == null || spawnPoint == null)
            return null;

        CurrentObject = Instantiate(prefab, spawnPoint);

        Transform t = CurrentObject.transform;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.Euler(0f, -90f, 0f);
        return CurrentObject;
    }

    // Spawn tàu người chơi theo cấp tàu hiện tại và gán id riêng (dùng cho mua tàu và spawn lại đầu round)
    public GameObject SpawnBoat(BoatData data, string instanceId)
    {
        GameObject boat = data != null ? Spawn(data.GetPrefab(GameData.GetBoatLevel(data.id))) : null;
        if (boat == null)
            return null;

        if (plane != null)
            plane.SetActive(false);

        if (boat.TryGetComponent(out BoatAIController ai))
            ai.Initialize(data, instanceId);

        return boat;
    }

    public void Clear()
    {
        if (CurrentObject != null)
            Destroy(CurrentObject);

        CurrentObject = null;
    }
}
