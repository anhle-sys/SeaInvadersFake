using UnityEngine;

[DisallowMultipleComponent]
public class DamageReceiver : MonoBehaviour
{
    [SerializeField] private DestructionController destructionController;

    private void Awake()
    {
        if (destructionController == null)
            destructionController = GetComponent<DestructionController>();
    }

    public bool TakeDamageAtHit(float damage, Vector3 hitPosition, Vector3 hitNormal)
    {
        if (destructionController == null)
        {
            Debug.LogWarning("DamageReceiver cannot find DestructionController.", this);
            return false;
        }

        return destructionController.ApplyDestruction(
            damage, hitPosition, hitNormal);
    }

    public bool TakeDamageAtHit(float damage, RaycastHit hit)
    {
        return TakeDamageAtHit(damage, hit.point, hit.normal);
    }
}
