using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class MeshEditorWindow : EditorWindow
{
    private GameObject target;

    private bool maskEnabled = true;
    private Texture2D explosiveMask;
    private float maskSize = 0.6f;
    private float alphaThreshold = 0.5f;
    private float projectionDepth = 0.15f;
    private float maskRotation = 0f;
    private int maxSubdivisionDepth = 8;

    // When disabled, a cut skips FillClosedHoles (ear-clipping can be slow on
    // large cut outlines). Use the "Fill Surface Holes" button to fill manually.
    private bool autoFill = true;

    private bool hasHit;
    private Vector3 hitPoint;
    private Vector3 hitNormal;

    private int hitSubmesh;
    private int hitTriangle;

    private MeshFilter meshFilter;

    private const string SaveFolder =
        "Assets/MeshEditor/Generated";

    // Surface Fill
    private Mesh baseMesh;
    private List<FillRegion> fillRegions =
        new List<FillRegion>();

    private bool showFillRegions = true;

    // The mesh currently shown that was built from baseMesh + fill patches.
    // While it is displayed, the next cut is applied to baseMesh instead, so
    // the (many, thin) patch triangles are never cut as real geometry.
    private Mesh filledDisplayMesh;

    private Vector2 scrollPosition;

    // ---- Raycast cache ---------------------------------------------------
    private Mesh rayCachedMesh;
    private int rayCachedVertexCount;
    private Vector3[] rayVertices;
    private int[][] raySubmeshTriangles;
    private Ray lastRay;
    private bool lastRayValid;
    private bool raycastDirty = true;

    // ---- Deferred cut (instant click feedback) ----------------------------
    // A click is acknowledged immediately (indicator drawn this frame) and the
    // cut runs on the next editor update, so the Scene View never looks frozen
    // without feedback. Clicks while a cut is pending are ignored, not queued.
    private enum CutState { Idle, Requested, Drawn }
    private CutState cutState = CutState.Idle;
    private Vector3 pendingPoint;
    private Vector3 pendingNormal;
    private float pendingRadius;

    // ---- Mask cache bookkeeping -----------------------------------------
    private Texture2D maskCacheTexture;
    private System.DateTime maskCacheStamp;

    [MenuItem("Tools/Edit Mesh/Mesh Editor")]
    public static void Open()
    {
        GetWindow<MeshEditorWindow>("Mesh Editor");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        raycastDirty = true;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;

        cutState = CutState.Idle;
        InvalidateRaycastCache();
        MeshGeometry.ClearMaskCache();
    }

    private void OnSelectionChange()
    {
        raycastDirty = true;
        Repaint();
        SceneView.RepaintAll();
    }

    private void InvalidateRaycastCache()
    {
        rayCachedMesh = null;
        rayVertices = null;
        raySubmeshTriangles = null;
        lastRayValid = false;
        raycastDirty = true;
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField(
            "INTERACTIVE MESH EDITOR",
            EditorStyles.boldLabel
        );

        EditorGUILayout.Space();

        target = (GameObject)EditorGUILayout.ObjectField(
            "Target",
            target,
            typeof(GameObject),
            true
        );

        if (GUILayout.Button("Use Selected Object"))
        {
            target = Selection.activeGameObject;

            ResetFillData();
            raycastDirty = true;
        }

        EditorGUILayout.Space();

        maskEnabled = EditorGUILayout.Toggle("Enable Explosive Mask", maskEnabled);

        Texture2D newMask = (Texture2D)EditorGUILayout.ObjectField(
            "Alpha Mask", explosiveMask, typeof(Texture2D), false);
        if (newMask != explosiveMask)
        {
            explosiveMask = newMask;
            MeshGeometry.ClearMaskCache();
        }

        maskSize = EditorGUILayout.Slider("Mask Size (World Units)", maskSize, 0.01f, 5f);
        alphaThreshold = EditorGUILayout.Slider("Alpha Threshold", alphaThreshold, 0f, 1f);
        projectionDepth = EditorGUILayout.Slider("Projection Depth", projectionDepth, 0.01f, 2f);
        maskRotation = EditorGUILayout.Slider("Mask Rotation", maskRotation, 0f, 360f);
        maxSubdivisionDepth = EditorGUILayout.IntSlider(
            new GUIContent("Edge Detail",
                "Cut outline resolution on the mask (8 = 1/64, 10 = 1/256 of the mask size). " +
                "Higher is finer but each step roughly doubles the cut time."),
            maxSubdivisionDepth, 4, 10);
        autoFill = EditorGUILayout.Toggle(
            new GUIContent("Auto Fill After Cut",
                "Turn off to make each cut faster. Use 'Fill Surface Holes' to fill manually."),
            autoFill);

        if (explosiveMask != null)
        {
            EditorGUILayout.LabelField("Mask Preview", EditorStyles.boldLabel);
            GUILayout.Label(explosiveMask, GUILayout.Width(120), GUILayout.Height(120));
            if (!explosiveMask.isReadable)
                EditorGUILayout.HelpBox("Enable Read/Write in the texture import settings.", MessageType.Error);
        }

        EditorGUILayout.Space();

        if (target == null)
        {
            EditorGUILayout.HelpBox(
                "Assign a GameObject containing MeshFilter.",
                MessageType.Warning
            );

            return;
        }

        meshFilter = target.GetComponent<MeshFilter>();

        if (meshFilter == null)
        {
            EditorGUILayout.HelpBox(
                "Target does not have MeshFilter.",
                MessageType.Error
            );

            return;
        }

        if (meshFilter.sharedMesh == null)
        {
            EditorGUILayout.HelpBox(
                "MeshFilter has no Mesh.",
                MessageType.Error
            );

            return;
        }

        if (!meshFilter.sharedMesh.isReadable)
        {
            EditorGUILayout.HelpBox(
                "Enable Read/Write in Model Import Settings.",
                MessageType.Error
            );

            return;
        }

        EditorGUILayout.HelpBox(
            "Move mouse over the model in Scene View and left-click to remove geometry.",
            MessageType.Info
        );

        EditorGUILayout.Space();

        if (GUILayout.Button("Fill Surface Holes"))
        {
            FillSurfaceHoles();
        }

        if (GUILayout.Button("Save Current Mesh"))
        {
            SaveCurrentMesh();
        }

        EditorGUILayout.Space();

        DrawFillRegions();

        if (GUILayout.Button("Refresh Scene View"))
        {
            MeshGeometry.ClearMaskCache();
            InvalidateRaycastCache();
            SceneView.RepaintAll();
        }
    }

    private void FillSurfaceHoles()
    {
        if (meshFilter == null ||
            meshFilter.sharedMesh == null)
            return;

        Mesh source = meshFilter.sharedMesh;

        Undo.RecordObject(
            meshFilter,
            "Fill Surface Holes"
        );

        ResetFillData();
        baseMesh = Instantiate(source);

        baseMesh.name = source.name + "_FillBase";

        fillRegions.Clear();

        Mesh result = MeshSurfacePatcher.FillClosedHoles(
            baseMesh,
            out fillRegions,
            0.0001f
        );

        if (result == null)
        {
            DestroyImmediate(baseMesh);
            baseMesh = null;

            fillRegions.Clear();

            Debug.LogWarning(
                "No valid closed surface holes found."
            );

            return;
        }

        meshFilter.sharedMesh = result;
        filledDisplayMesh = result;
        raycastDirty = true;

        EditorUtility.SetDirty(meshFilter);

        SceneView.RepaintAll();

        Repaint();

        Debug.Log(
            "Surface Fill completed. Regions: " +
            fillRegions.Count
        );
    }

    private void DrawFillRegions()
    {
        showFillRegions = EditorGUILayout.Foldout(
            showFillRegions,
            "Fill Regions (" + fillRegions.Count + ")",
            true
        );

        if (!showFillRegions)
            return;

        if (fillRegions.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No Fill Regions. Click Fill Surface Holes first.",
                MessageType.Info
            );

            return;
        }

        EditorGUILayout.BeginVertical(
            EditorStyles.helpBox
        );

        scrollPosition = EditorGUILayout.BeginScrollView(
            scrollPosition,
            GUILayout.MaxHeight(250)
        );

        foreach (FillRegion region in fillRegions)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(
                "Fill Region " + region.regionId
            );

            bool newVisible = EditorGUILayout.Toggle(
                region.isVisible,
                GUILayout.Width(20)
            );

            if (newVisible != region.isVisible)
            {
                SetFillRegionVisible(
                    region.regionId,
                    newVisible
                );
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        if (GUILayout.Button("Show All Fill Regions"))
        {
            SetAllFillRegionsVisible(true);
        }

        if (GUILayout.Button("Hide All Fill Regions"))
        {
            SetAllFillRegionsVisible(false);
        }
    }

    private void SetFillRegionVisible(
        int regionId,
        bool visible)
    {
        FillRegion region = fillRegions.Find(
            r => r.regionId == regionId
        );

        if (region == null || baseMesh == null)
            return;

        Undo.RecordObject(
            meshFilter,
            "Toggle Fill Region"
        );

        region.isVisible = visible;

        RebuildFilledMesh();
    }

    private void SetAllFillRegionsVisible(bool visible)
    {
        if (baseMesh == null)
            return;

        Undo.RecordObject(
            meshFilter,
            "Toggle All Fill Regions"
        );

        foreach (FillRegion region in fillRegions)
        {
            region.isVisible = visible;
        }

        RebuildFilledMesh();
    }

    private void RebuildFilledMesh()
    {
        if (baseMesh == null)
            return;

        Mesh rebuilt = MeshSurfacePatcher.BuildMeshWithRegions(
            baseMesh,
            fillRegions
        );

        if (rebuilt == null)
            return;

        meshFilter.sharedMesh = rebuilt;
        filledDisplayMesh = rebuilt;
        raycastDirty = true;

        EditorUtility.SetDirty(meshFilter);

        SceneView.RepaintAll();

        Repaint();
    }

    private void ResetFillData()
    {
        if (baseMesh != null)
        {
            DestroyImmediate(baseMesh);
            baseMesh = null;
        }

        filledDisplayMesh = null;
        fillRegions.Clear();
    }

    private void SaveCurrentMesh()
    {
        if (meshFilter == null ||
            meshFilter.sharedMesh == null)
            return;

        if (!Directory.Exists(SaveFolder))
        {
            Directory.CreateDirectory(SaveFolder);
            AssetDatabase.Refresh();
        }

        Mesh source = meshFilter.sharedMesh;

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Edited Mesh",
            source.name,
            "asset",
            "Choose where to save the mesh."
        );

        if (string.IsNullOrEmpty(path))
            return;

        Mesh savedMesh = Instantiate(source);
        savedMesh.name = source.name;

        AssetDatabase.CreateAsset(savedMesh, path);
        AssetDatabase.SaveAssets();

        Undo.RecordObject(
            meshFilter,
            "Save Edited Mesh"
        );

        meshFilter.sharedMesh = savedMesh;
        raycastDirty = true;

        EditorUtility.SetDirty(meshFilter);

        Selection.activeObject = savedMesh;

        Debug.Log("Mesh saved: " + path);
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!maskEnabled || target == null)
            return;

        meshFilter = target.GetComponent<MeshFilter>();

        if (meshFilter == null ||
            meshFilter.sharedMesh == null ||
            !meshFilter.sharedMesh.isReadable)
        {
            return;
        }

        sceneView.wantsMouseMove = true;

        Event e = Event.current;

        if (cutState != CutState.Idle)
        {
            DrawPendingCut(e);

            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            // Swallow clicks while the cut is pending/processing.
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
                e.Use();

            // Only start the work once the indicator has actually been drawn.
            if (e.type == EventType.Repaint && cutState == CutState.Requested)
            {
                cutState = CutState.Drawn;
                EditorApplication.delayCall += ExecutePendingCut;
            }

            return;
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(
            e.mousePosition
        );

        bool rayChanged =
            raycastDirty ||
            !lastRayValid ||
            (ray.origin - lastRay.origin).sqrMagnitude > 1e-10f ||
            (ray.direction - lastRay.direction).sqrMagnitude > 1e-10f;

        if (rayChanged)
        {
            lastRay = ray;
            lastRayValid = true;
            raycastDirty = false;

            hasHit = TryRaycastMesh(
                ray,
                meshFilter,
                out hitPoint,
                out hitNormal,
                out hitSubmesh,
                out hitTriangle
            );

            if (e.type != EventType.Repaint)
                sceneView.Repaint();
        }

        if (!hasHit)
            return;

        Handles.color = Color.cyan;
        Handles.DrawWireDisc(hitPoint, hitNormal, maskSize * 0.5f);
        Handles.DrawLine(hitPoint, hitPoint + hitNormal * 0.15f);

        if (explosiveMask != null)
        {
            Handles.BeginGUI();
            Rect previewRect = new Rect(e.mousePosition.x + 18f, e.mousePosition.y + 18f, 92f, 92f);
            GUI.color = Color.white;
            GUI.DrawTexture(previewRect, explosiveMask, ScaleMode.ScaleToFit, true);
            GUI.color = Color.white;
            Handles.EndGUI();
        }

        if (e.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(
                GUIUtility.GetControlID(
                    FocusType.Passive
                )
            );
        }

        if (e.type == EventType.MouseDown &&
            e.button == 0 &&
            !e.alt)
        {
            RequestCut(sceneView);

            e.Use();
        }
    }

    private void RequestCut(SceneView sceneView)
    {
        if (!hasHit || explosiveMask == null || !explosiveMask.isReadable)
        {
            RemoveGeometry(); // logs the reason
            return;
        }

        pendingPoint = hitPoint;
        pendingNormal = hitNormal;
        pendingRadius = maskSize * 0.5f;
        cutState = CutState.Requested;
        sceneView.Repaint();
    }

    private void DrawPendingCut(Event e)
    {
        if (e.type != EventType.Repaint)
            return;

        Color fill = new Color(1f, 0.55f, 0.1f, 0.35f);
        Handles.color = fill;
        Handles.DrawSolidDisc(pendingPoint, pendingNormal, pendingRadius);
        Handles.color = new Color(1f, 0.55f, 0.1f, 1f);
        Handles.DrawWireDisc(pendingPoint, pendingNormal, pendingRadius);

        Handles.BeginGUI();
        Vector2 gui = HandleUtility.WorldToGUIPoint(pendingPoint);
        GUI.Label(new Rect(gui.x + 12f, gui.y - 10f, 120f, 20f), "Cutting...", EditorStyles.whiteBoldLabel);
        Handles.EndGUI();
    }

    private void ExecutePendingCut()
    {
        if (cutState != CutState.Drawn)
            return;

        try
        {
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                hasHit = true;
                hitPoint = pendingPoint;
                hitNormal = pendingNormal;
                RemoveGeometry();
            }
        }
        finally
        {
            cutState = CutState.Idle;
            raycastDirty = true;
            SceneView.RepaintAll();
        }
    }

    private void EnsureRaycastCache(Mesh mesh)
    {
        if (rayCachedMesh == mesh &&
            rayVertices != null &&
            raySubmeshTriangles != null &&
            rayCachedVertexCount == mesh.vertexCount &&
            raySubmeshTriangles.Length == mesh.subMeshCount)
        {
            return;
        }

        rayCachedMesh = mesh;
        rayCachedVertexCount = mesh.vertexCount;
        rayVertices = mesh.vertices;
        raySubmeshTriangles = new int[mesh.subMeshCount][];

        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            raySubmeshTriangles[s] =
                mesh.GetTopology(s) == MeshTopology.Triangles
                    ? mesh.GetTriangles(s)
                    : null;
        }
    }

    private bool TryRaycastMesh(
        Ray ray,
        MeshFilter filter,
        out Vector3 point,
        out Vector3 normal,
        out int submeshIndex,
        out int triangleIndex)
    {
        point = Vector3.zero;
        normal = Vector3.up;

        submeshIndex = -1;
        triangleIndex = -1;

        Mesh mesh = filter.sharedMesh;

        EnsureRaycastCache(mesh);

        Transform meshTransform = filter.transform;

        // Transform the ray into local space once, instead of transforming
        // every triangle vertex into world space. The ray parameter t is the
        // same in both spaces because the direction is transformed with it.
        Matrix4x4 worldToLocal = meshTransform.worldToLocalMatrix;
        Vector3 localOrigin = worldToLocal.MultiplyPoint3x4(ray.origin);
        Vector3 localDirection = worldToLocal.MultiplyVector(ray.direction);

        // Cheap early-out: ray misses the mesh bounds entirely.
        Bounds bounds = mesh.bounds;
        bounds.Expand(Mathf.Max(bounds.size.magnitude * 0.001f, 1e-4f));
        if (!bounds.IntersectRay(new Ray(localOrigin, localDirection)))
            return false;

        float closestDistance = float.MaxValue;
        int bestSubmesh = -1;
        int bestIndex = -1;

        for (int s = 0; s < raySubmeshTriangles.Length; s++)
        {
            int[] triangles = raySubmeshTriangles[s];

            if (triangles == null)
                continue;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                if (!RayTriangle(
                    localOrigin,
                    localDirection,
                    rayVertices[triangles[i]],
                    rayVertices[triangles[i + 1]],
                    rayVertices[triangles[i + 2]],
                    out float distance))
                    continue;

                if (distance >= closestDistance)
                    continue;

                closestDistance = distance;
                bestSubmesh = s;
                bestIndex = i;
            }
        }

        if (bestSubmesh < 0)
            return false;

        int[] bestTriangles = raySubmeshTriangles[bestSubmesh];

        // World-space normal is only needed for the single closest triangle.
        Vector3 a = meshTransform.TransformPoint(rayVertices[bestTriangles[bestIndex]]);
        Vector3 b = meshTransform.TransformPoint(rayVertices[bestTriangles[bestIndex + 1]]);
        Vector3 c = meshTransform.TransformPoint(rayVertices[bestTriangles[bestIndex + 2]]);

        point = ray.GetPoint(closestDistance);

        normal = Vector3.Cross(
            b - a,
            c - a
        ).normalized;

        if (meshTransform.localToWorldMatrix.determinant < 0)
            normal = -normal;

        submeshIndex = bestSubmesh;
        triangleIndex = bestIndex / 3;

        return true;
    }

    private static bool RayTriangle(
        Vector3 origin,
        Vector3 direction,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out float distance)
    {
        distance = 0f;

        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;

        Vector3 p = Vector3.Cross(
            direction,
            edge2
        );

        float determinant = Vector3.Dot(
            edge1,
            p
        );

        if (Mathf.Abs(determinant) < 1e-9f)
            return false;

        float inv = 1f / determinant;

        Vector3 tvec = origin - a;

        float u = Vector3.Dot(tvec, p) * inv;

        if (u < 0f || u > 1f)
            return false;

        Vector3 q = Vector3.Cross(tvec, edge1);

        float v = Vector3.Dot(
            direction,
            q
        ) * inv;

        if (v < 0f || u + v > 1f)
            return false;

        distance = Vector3.Dot(edge2, q) * inv;

        return distance >= 0f;
    }

    // Drops the cached mask data if the same Texture2D asset was reimported/edited.
    private void SyncMaskCache()
    {
        System.DateTime stamp = default;
        string path = AssetDatabase.GetAssetPath(explosiveMask);

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            stamp = File.GetLastWriteTimeUtc(path);

        if (explosiveMask != maskCacheTexture || stamp != maskCacheStamp)
        {
            MeshGeometry.ClearMaskCache();
            maskCacheTexture = explosiveMask;
            maskCacheStamp = stamp;
        }
    }

    private void RemoveGeometry()
    {
        if (meshFilter == null || !hasHit)
            return;

        if (explosiveMask == null)
        {
            Debug.LogWarning("Assign an Explosive Alpha Mask first.");
            return;
        }

        if (!explosiveMask.isReadable)
        {
            Debug.LogError("Explosive Mask must have Read/Write Enabled.");
            return;
        }

        // Cut the unfilled base when the filled preview is displayed: cutting
        // the fill patches multiplied the work ~15x and their slivers left debris.
        Mesh source =
            baseMesh != null && filledDisplayMesh != null &&
            meshFilter.sharedMesh == filledDisplayMesh
                ? baseMesh
                : meshFilter.sharedMesh;

        SyncMaskCache();

        Mesh result = MeshGeometry.RemoveExplosiveMask(
            source, meshFilter.transform, hitPoint, hitNormal, explosiveMask,
            maskSize, alphaThreshold, projectionDepth, 0.1f,
            maxSubdivisionDepth, maskRotation
        );

        if (result == null)
        {
            Debug.LogWarning(
                "Mesh removal failed."
            );

            return;
        }

        Undo.RecordObject(
            meshFilter,
            "Remove Mesh Geometry"
        );

        // NOTE: the mesh is no longer written to disk on every click
        // (CreateAsset + SaveAssets was the most expensive part for large meshes).
        // Use "Save Current Mesh" when you want to keep the result as an asset.
        ResetFillData();

        Mesh display = result;

        if (autoFill)
        {
            // Keep the unfilled cut mesh as the base, then create independently
            // manageable Fill Regions from its new surface boundaries.
            baseMesh = result;

            Mesh filledResult = MeshSurfacePatcher.FillClosedHoles(
                baseMesh, out fillRegions, 0.0001f);

            if (filledResult != null)
            {
                display = filledResult;
                filledDisplayMesh = filledResult;
            }
            else
            {
                // Nothing was filled: the cut mesh itself is displayed, so it must
                // not also be tracked (and later destroyed) as the fill base.
                baseMesh = null;
                fillRegions.Clear();
            }
        }

        meshFilter.sharedMesh = display;

        EditorUtility.SetDirty(meshFilter);
        hasHit = false;
        raycastDirty = true;
        SceneView.RepaintAll();
        Repaint();

        Debug.Log("Explosive Mask removal completed. Fill Regions: " +
            fillRegions.Count + ". Use 'Save Current Mesh' to save it as an asset.");
    }
    public void SetFillVisible(int regionId, bool visible)
    {
        SetFillRegionVisible(regionId, visible);
    }

    public void EnableFill(int regionId)
    {
        SetFillRegionVisible(regionId, true);
    }

    public void DisableFill(int regionId)
    {
        SetFillRegionVisible(regionId, false);
    }

    public void EnableAllFill()
    {
        SetAllFillRegionsVisible(true);
    }

    public void DisableAllFill()
    {
        SetAllFillRegionsVisible(false);
    }
}
