
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
public class RuntimeMeshFillController : MonoBehaviour
{
    [Header("Source Mesh")]
    [SerializeField] private Mesh sourceMesh;

    [SerializeField] private float weldTolerance = 0.0001f;

    private MeshRenderer sourceRenderer;

    private Transform fillRoot;

    private readonly Dictionary<int, MeshRenderer> fillRenderers = new Dictionary<int, MeshRenderer>();

    private readonly List<Mesh> generatedMeshes = new List<Mesh>();

    private void Awake()
    {
        MeshFilter sourceFilter = GetComponent<MeshFilter>();
        sourceRenderer = GetComponent<MeshRenderer>();

        if (sourceMesh == null)
            sourceMesh = sourceFilter.sharedMesh;

        if (sourceMesh == null)
        {
            Debug.LogError("Source Mesh is missing!", this);
            return;
        }

        if (sourceRenderer == null)
        {
            Debug.LogError("MeshRenderer is missing!", this);
            return;
        }

        GenerateFillRegions();
    }

    private void GenerateFillRegions()
    {
        List<FillRegion> regions;

        Mesh temporaryMesh = MeshSurfacePatcher.FillClosedHoles(
            sourceMesh,
            out regions,
            weldTolerance
        );

        if (temporaryMesh != null)
            Destroy(temporaryMesh);

        if (regions == null || regions.Count == 0)
        {
            Debug.LogWarning("No Fill Regions found.", this);
            return;
        }

        // Tạo container chứa các Fill Region.
        GameObject root = new GameObject("Runtime_Fill_Regions");

        fillRoot = root.transform;
        fillRoot.SetParent(transform, false);

        Material[] materials = sourceRenderer.sharedMaterials;

        foreach (FillRegion region in regions)
        {
            Mesh regionMesh = BuildRegionMesh(sourceMesh, region);

            if (regionMesh == null)
                continue;

            GameObject regionObject = new GameObject(
                "FillRegion_" + region.regionId
            );

            regionObject.transform.SetParent(fillRoot, false);

            MeshFilter filter = regionObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = regionObject.AddComponent<MeshRenderer>();

            filter.sharedMesh = regionMesh;

            // Lấy Material tương ứng với Submesh.
            int materialIndex = region.submeshIndex;

            if (materialIndex >= 0 &&
                materialIndex < materials.Length)
            {
                renderer.sharedMaterial = materials[materialIndex];
            }

            renderer.enabled = region.isVisible;

            fillRenderers[region.regionId] = renderer;
            generatedMeshes.Add(regionMesh);
        }

        Debug.Log(
            $"Generated {fillRenderers.Count} Fill Regions.",
            this
        );
    }

    private Mesh BuildRegionMesh(Mesh source, FillRegion region)
    {
        if (source == null ||
            region == null ||
            region.triangleIndices == null)
            return null;

        Vector3[] sourceVertices = source.vertices;

        var vertexMap = new Dictionary<int, int>();

        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        // Chỉ lấy các vertex thực sự được sử dụng bởi Region.
        foreach (int originalIndex in region.triangleIndices)
        {
            if (originalIndex < 0 ||
                originalIndex >= sourceVertices.Length)
                return null;

            if (!vertexMap.TryGetValue(originalIndex, out int newIndex))
            {
                newIndex = vertices.Count;

                vertexMap.Add(originalIndex, newIndex);
                vertices.Add(sourceVertices[originalIndex]);
            }

            triangles.Add(newIndex);
        }

        if (triangles.Count < 3)
            return null;

        Mesh result = new Mesh
        {
            name = "FillRegion_" + region.regionId
        };

        if (vertices.Count > 65535)
            result.indexFormat = IndexFormat.UInt32;

        result.SetVertices(vertices);

        // Sao chép các dữ liệu vertex phổ biến.
        Vector3[] normals = source.normals;

        if (normals.Length == sourceVertices.Length)
        {
            var newNormals = new List<Vector3>();

            foreach (var pair in vertexMap)
                newNormals.Add(normals[pair.Key]);

            result.SetNormals(newNormals);
        }

        Vector4[] tangents = source.tangents;

        if (tangents.Length == sourceVertices.Length)
        {
            var newTangents = new List<Vector4>();

            foreach (var pair in vertexMap)
                newTangents.Add(tangents[pair.Key]);

            result.SetTangents(newTangents);
        }

        Color32[] colors = source.colors32;

        if (colors.Length == sourceVertices.Length)
        {
            var newColors = new List<Color32>();

            foreach (var pair in vertexMap)
                newColors.Add(colors[pair.Key]);

            result.SetColors(newColors);
        }

        // Sao chép các UV channel.
        for (int channel = 0; channel < 8; channel++)
        {
            var sourceUV = new List<Vector4>();

            source.GetUVs(channel, sourceUV);

            if (sourceUV.Count != sourceVertices.Length)
                continue;

            var newUV = new List<Vector4>();

            foreach (var pair in vertexMap)
                newUV.Add(sourceUV[pair.Key]);

            result.SetUVs(channel, newUV);
        }

        result.SetTriangles(triangles, 0, false);

        if (normals.Length != sourceVertices.Length)
            result.RecalculateNormals();

        result.RecalculateBounds();

        return result;
    }

    // Bật hoặc tắt một Region.
    public void SetFillVisible(int regionId, bool visible)
    {
        if (!fillRenderers.TryGetValue(regionId, out MeshRenderer renderer))
        {
            Debug.LogWarning(
                $"Fill Region {regionId} not found!",
                this
            );

            return;
        }

        renderer.enabled = visible;
    }

    public void EnableFill(int regionId)
    {
        SetFillVisible(regionId, true);
    }

    public void DisableFill(int regionId)
    {
        SetFillVisible(regionId, false);
    }

    public void EnableAllFill()
    {
        foreach (MeshRenderer renderer in fillRenderers.Values)
            renderer.enabled = true;
    }

    public void DisableAllFill()
    {
        foreach (MeshRenderer renderer in fillRenderers.Values)
            renderer.enabled = false;
    }

    public int GetFillRegionCount()
    {
        return fillRenderers.Count;
    }

    private void OnDestroy()
    {
        foreach (Mesh mesh in generatedMeshes)
        {
            if (mesh != null)
                Destroy(mesh);
        }

        generatedMeshes.Clear();

        if (fillRoot != null)
            Destroy(fillRoot.gameObject);
    }
}
