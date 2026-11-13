using UnityEngine;

public sealed class RuntimeMeshState
{
    public Mesh CurrentMesh { get; private set; }

    private readonly MeshFilter meshFilter;
    private readonly MeshCollider meshCollider;

    public RuntimeMeshState(MeshFilter filter, MeshCollider collider)
    {
        meshFilter = filter;
        meshCollider = collider;

        if (meshFilter == null || meshFilter.sharedMesh == null)
            return;

        CurrentMesh = Object.Instantiate(meshFilter.sharedMesh);
        CurrentMesh.name = meshFilter.sharedMesh.name + "_Runtime";
        CurrentMesh.MarkDynamic();
        meshFilter.sharedMesh = CurrentMesh;

        if (meshCollider != null)
            DestructionColliderUpdater.Assign(meshCollider, CurrentMesh);
    }

    public void SetMesh(Mesh newMesh)
    {
        if (newMesh == null || meshFilter == null)
            return;

        Mesh previous = CurrentMesh;
        CurrentMesh = newMesh;
        meshFilter.sharedMesh = CurrentMesh;

        if (meshCollider != null)
            DestructionColliderUpdater.Assign(meshCollider, CurrentMesh);

        if (previous != null)
            Object.Destroy(previous);
    }
}
