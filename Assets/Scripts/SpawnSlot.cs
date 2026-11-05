using UnityEngine;

public class SpawnSlot : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField] private Transform spawnPoint;

    // Tàu bị Destroy (chết, Banger tự nổ...) thì CurrentObject == null -> ô tự trống lại
    public bool IsOccupied => CurrentObject != null;
    public GameObject CurrentObject { get; private set; }

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

    public void Clear()
    {
        if (CurrentObject != null)
            Destroy(CurrentObject);

        CurrentObject = null;
    }
}
