using UnityEngine;

public class OceanClickable : MonoBehaviour, IClickable
{
    [SerializeField] private Vector3EventChannel channel; // kéo asset OceanClicked vào

    public void OnClicked(RaycastHit hit)
    {
        channel.Raise(hit.point);
    }
}