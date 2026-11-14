using UnityEngine;

public class PooledParticleFx : MonoBehaviour
{
    [Tooltip("Thời gian (giây) trước khi trả về pool, nên >= lifetime dài nhất của particle")]
    [SerializeField] private float lifetime = 1f;

    private ParticleSystem[] systems;

    private void Awake() => systems = GetComponentsInChildren<ParticleSystem>(true);

    private void OnEnable()
    {
        if (systems == null) systems = GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem ps in systems)
        {
            ps.Clear(false);
            ps.Play(false);
        }

        CancelInvoke(nameof(Release));
        Invoke(nameof(Release), lifetime);
    }

    private void OnDisable() => CancelInvoke(nameof(Release));

    private void Release() => SimplePool2.Despawn(gameObject);
}
