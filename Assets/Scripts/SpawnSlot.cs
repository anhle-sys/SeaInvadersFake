using UnityEngine;

public class SpawnSlot : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField] private Transform spawnPoint;

    public bool IsOccupied { get; private set; }
    public GameObject CurrentObject { get; private set; }

    public void Spawn(GameObject prefab)
    {
        if (IsOccupied || prefab == null || spawnPoint == null)
            return;

        CurrentObject = Instantiate(prefab, spawnPoint);

        Transform t = CurrentObject.transform;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.Euler(0f, -90f, 0f);

        IsOccupied = true;
    }

    public void Clear()
    {
        if (CurrentObject != null)
            Destroy(CurrentObject);

        CurrentObject = null;
        IsOccupied = false;
    }
}