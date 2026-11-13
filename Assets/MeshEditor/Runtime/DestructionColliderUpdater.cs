using UnityEngine;

public static class DestructionColliderUpdater
{
    public static void Assign(MeshCollider collider, Mesh mesh)
    {
        if (collider == null)
            return;

        collider.sharedMesh = null;
        collider.sharedMesh = mesh;
    }
}
