using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
public class DestructionController : MonoBehaviour
{
    [SerializeField] private DestructionSettings settings;
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private MeshCollider meshCollider;

    [Header("Projection")]
    [Tooltip("Optional direction used to orient the projection. If unset, Transform.right is used.")]
    [SerializeField] private Transform projectionOrientation;

    private RuntimeMeshState meshState;

    public Mesh CurrentMesh => meshState != null ? meshState.CurrentMesh : null;

    private void Awake()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();

        if (meshCollider == null)
            meshCollider = GetComponent<MeshCollider>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError("DestructionController requires a MeshFilter with a Mesh.", this);
            enabled = false;
            return;
        }

        if (!meshFilter.sharedMesh.isReadable)
        {
            Debug.LogError(
                "The target mesh is not readable. Enable Read/Write in the model import settings.",
                this);
            enabled = false;
            return;
        }

        meshState = new RuntimeMeshState(meshFilter, meshCollider);
    }

    public bool ApplyDestruction(float damage, Vector3 hitPosition, Vector3 hitNormal)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Runtime mesh destruction can only run in Play Mode.", this);
            return false;
        }

        if (settings == null || settings.destructionMask == null)
        {
            Debug.LogError("Assign a DestructionSettings asset and its destruction mask.", this);
            return false;
        }

        if (meshState == null || meshState.CurrentMesh == null)
            Initialize();

        if (meshState == null || meshState.CurrentMesh == null)
            return false;

        float radius = settings.GetRadius(damage);
        if (radius <= 0f)
            return false;

        if (!TextureMaskProcessor.TryCreate(
            settings.destructionMask,
            settings.damageThreshold,
            out TextureMaskProcessor.MaskData mask))
        {
            return false;
        }

        Vector3 normal = hitNormal.sqrMagnitude > 0.000001f
            ? hitNormal.normalized
            : transform.up;

        Vector3 preferredRight = projectionOrientation != null
            ? projectionOrientation.right
            : transform.right;

        LocalProjectionBuilder projection = new LocalProjectionBuilder(
            hitPosition, normal, preferredRight);

        float planeTolerance = radius * settings.planeToleranceRadiusMultiplier;

        Mesh result = MeshSurfaceClipper.ApplyMask(
            meshState.CurrentMesh,
            transform,
            hitPosition,
            normal,
            radius,
            projection,
            mask,
            settings.minimumNormalDot,
            planeTolerance);

        if (result == null)
            return false;

        meshState.SetMesh(result);
        return true;
    }

    public bool ApplyDestruction(float damage, RaycastHit hit)
    {
        return ApplyDestruction(damage, hit.point, hit.normal);
    }
}
