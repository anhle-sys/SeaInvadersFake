using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HolePatternConfig))]
public class HolePatternConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var config = (HolePatternConfig)target;
        EditorGUILayout.Space();

        string problem = HolePatternBaker.Validate(config);
        if (problem != null)
            EditorGUILayout.HelpBox(problem, MessageType.Warning);

        using (new EditorGUI.DisabledScope(problem != null))
        {
            if (GUILayout.Button("Bake", GUILayout.Height(32)))
                HolePatternBaker.Bake(config);
        }

        if (!string.IsNullOrEmpty(config.lastBakeReport))
            EditorGUILayout.HelpBox(config.lastBakeReport, MessageType.Info);
    }
}

public static class HolePatternBaker
{
    // Vùng lấp có hơn số tam giác này mới tính là 1 lỗ thật (bỏ qua mảnh li ti do viền răng cưa).
    private const int MinRegionTriangles = 20;

    public const string DefaultFolder = "Assets/MeshEditor/HolePatterns";

    private struct HolePoint
    {
        public Vector3 origin;
        public Vector3 direction;
        public int index;
        public float sideSign;
    }

    private sealed class RayMesh
    {
        public Vector3[] vertices;
        public int[][] submeshTriangles;
        public Bounds bounds;
    }

    // Trạng thái của 1 lần bake.
    private sealed class BakeState
    {
        public HolePatternConfig config;
        public Transform hull;
        public RayMesh rayMesh;
        public Mesh current;
        public int bigRegions;
        public int done;
        public int total;
        public StringBuilder report;
        public List<HoleLayerSeed> seeds = new List<HoleLayerSeed>();
    }

    public static string Validate(HolePatternConfig c)
    {
        if (c.sourceMesh == null) return "Chưa gán Source Mesh (mesh gốc chưa cắt).";
        if (!c.sourceMesh.isReadable) return "Source Mesh chưa bật Read/Write.";
        if (c.mask == null) return "Chưa gán Mask.";
        if (!c.mask.isReadable) return "Mask chưa bật Read/Write trong import settings.";
        bool anyHoles = (c.manualHoles != null && c.manualHoles.Count > 0);
        if (c.groups != null)
            foreach (var g in c.groups)
                anyHoles |= g != null && g.enabled;
        if (!anyHoles) return "Chưa có lỗ nào (cắt tay trong Mesh Editor hoặc thêm nhóm lỗ).";
        return null;
    }

    public static bool Bake(HolePatternConfig config)
    {
        string problem = Validate(config);
        if (problem != null)
        {
            Debug.LogError("Hole Pattern Bake: " + problem, config);
            return false;
        }

        string prefabPath = config.prefab != null ? AssetDatabase.GetAssetPath(config.prefab) : null;
        GameObject root = prefabPath != null ? PrefabUtility.LoadPrefabContents(prefabPath) : null;
        GameObject tempHull = null;

        var state = new BakeState
        {
            config = config,
            current = config.sourceMesh,
            report = new StringBuilder()
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        bool success = false;

        try
        {
            if (root != null)
            {
                state.hull = FindHull(root, config.hullPath);
                if (state.hull == null)
                {
                    Debug.LogError("Hole Pattern Bake: không tìm thấy object thân tàu '" + config.hullPath + "' trong prefab.", config);
                    return false;
                }
            }
            else
            {
                // Không có prefab: dựng transform tạm với scale của thân tàu.
                tempHull = new GameObject("HolePatternBakeHull") { hideFlags = HideFlags.HideAndDontSave };
                tempHull.transform.localScale = config.fallbackHullScale;
                state.hull = tempHull.transform;
            }

            state.rayMesh = BuildRayMesh(config.sourceMesh);
            state.bigRegions = CountBigRegions(state.current);

            var manualDisplay = new List<HolePatternConfig.ManualHole>();
            var manualSource = new List<HolePatternConfig.ManualHole>();
            if (config.manualHoles != null)
                foreach (var h in config.manualHoles)
                    (h.includeInDisplay ? manualDisplay : manualSource).Add(h);

            var groupDisplay = new List<HolePatternConfig.HoleGroup>();
            var groupSource = new List<HolePatternConfig.HoleGroup>();
            foreach (var g in config.groups)
                if (g != null && g.enabled)
                    (g.includeInDisplay ? groupDisplay : groupSource).Add(g);

            state.total = manualDisplay.Count + manualSource.Count;
            foreach (var g in groupDisplay) state.total += GeneratePoints(g, state.rayMesh.bounds).Count;
            foreach (var g in groupSource) state.total += GeneratePoints(g, state.rayMesh.bounds).Count;

            CutManual(state, manualDisplay, "Lỗ cắt tay");
            foreach (var g in groupDisplay)
                CutGroup(state, g);

            Mesh display = SaveMesh(config, state.current, ref config.displayOutput, "_Display");

            Mesh source = display;
            if (manualSource.Count > 0 || groupSource.Count > 0)
            {
                CutManual(state, manualSource, "Lỗ cắt tay (chỉ nguồn)");
                foreach (var g in groupSource)
                    CutGroup(state, g);

                source = SaveMesh(config, state.current, ref config.sourceOutput, "_Source");
            }

            config.bakedLayerSeeds = state.seeds;

            if (config.assignToPrefab && root != null)
            {
                AssignToHull(state.hull, display, source, config);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                state.report.AppendLine("Đã gán vào prefab: MeshFilter = " + display.name + ", Source Mesh = " + source.name + ".");
            }

            state.report.Insert(0,
                $"Bake xong trong {stopwatch.Elapsed.TotalSeconds:F1}s. " +
                $"Display: {display.vertexCount} đỉnh. Source: {source.vertexCount} đỉnh. Lỗ thật (vùng lớn): {state.bigRegions}. " +
                $"Lỗ 2 lớp: {state.seeds.Count}.\n");

            config.lastBakeReport = state.report.ToString().TrimEnd();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            RuntimeMeshFillController.ClearSharedCache();
            Debug.Log("Hole Pattern Bake (" + config.name + "):\n" + config.lastBakeReport, config);
            success = true;
        }
        catch (System.OperationCanceledException)
        {
            Debug.LogWarning("Hole Pattern Bake: đã hủy.", config);
        }
        finally
        {
            EditorUtility.ClearProgressBar();

            if (root != null)
                PrefabUtility.UnloadPrefabContents(root);
            if (tempHull != null)
                Object.DestroyImmediate(tempHull);

            if (state.current != null && state.current != config.sourceMesh && !AssetDatabase.Contains(state.current))
                Object.DestroyImmediate(state.current);
        }

        return success;
    }

    // ---- Cắt ---------------------------------------------------------------------------

    private static void CutManual(BakeState s, List<HolePatternConfig.ManualHole> holes, string label)
    {
        if (holes.Count == 0)
            return;

        int cut = 0, retried = 0, failed = 0;
        Matrix4x4 normalToWorld = s.hull.worldToLocalMatrix.transpose;

        foreach (var h in holes)
        {
            Progress(s, label);

            Vector3 worldPoint = s.hull.TransformPoint(h.localPoint);
            Vector3 worldNormal = normalToWorld.MultiplyVector(h.localNormal).normalized;

            // Hướng dịch khi thử lại: vuông góc pháp tuyến.
            Vector3 tangent = Vector3.Cross(worldNormal, Mathf.Abs(worldNormal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;

            int attempts = TryCut(s, attempt =>
            {
                float shift = Shift(attempt) * h.maskSize * 0.08f;
                return new CutRequest
                {
                    worldPoint = worldPoint + tangent * shift,
                    worldNormal = worldNormal,
                    maskSize = h.maskSize,
                    depth = h.projectionDepth,
                    rotation = h.rotation + attempt * 37f,
                    bigScale = h.bigMaskScale,
                    valid = true
                };
            });

            if (attempts < 0) failed++;
            else { cut++; if (attempts > 0) retried++; }
        }

        s.report.AppendLine($"• {label}: cắt {cut}/{holes.Count} lỗ" +
                            (retried > 0 ? $" (thử lại {retried})" : "") +
                            (failed > 0 ? $", {failed} lỗ không tạo được vùng lấp" : "") + ".");
    }

    private static void CutGroup(BakeState s, HolePatternConfig.HoleGroup group)
    {
        List<HolePoint> points = GeneratePoints(group, s.rayMesh.bounds);
        int cut = 0, skipped = 0, retried = 0, failed = 0;
        Vector3 alongDir = AxisVector(group.alongAxis);
        Matrix4x4 normalToWorld = s.hull.worldToLocalMatrix.transpose;

        foreach (HolePoint point in points)
        {
            Progress(s, group.name);
            bool anyHit = false;

            int attempts = TryCut(s, attempt =>
            {
                Vector3 origin = point.origin + alongDir * (Shift(attempt) * group.alongStep * 0.08f);

                if (!Raycast(s.rayMesh, origin, point.direction, out Vector3 hit, out Vector3 normal, out int submesh) ||
                    (group.requiredSubmesh >= 0 && submesh != group.requiredSubmesh) ||
                    Mathf.Abs(Vector3.Dot(normal, point.direction)) < group.minSurfaceAlignment)
                    return default;

                anyHit = true;

                Vector3 worldNormal = normalToWorld.MultiplyVector(normal).normalized;
                if (Vector3.Dot(worldNormal, s.hull.TransformDirection(-point.direction)) < 0f)
                    worldNormal = -worldNormal;

                return new CutRequest
                {
                    worldPoint = s.hull.TransformPoint(hit),
                    worldNormal = worldNormal,
                    maskSize = group.maskSize,
                    depth = group.projectionDepth,
                    rotation = point.index * group.rotationStep + (point.sideSign < 0f ? 23f : 0f) + attempt * 37f,
                    bigScale = group.bigMaskScale,
                    valid = true
                };
            });

            if (!anyHit) skipped++;
            else if (attempts < 0) failed++;
            else { cut++; if (attempts > 0) retried++; }
        }

        s.report.AppendLine($"• {group.name}: cắt {cut} lỗ" +
                            (retried > 0 ? $" (thử lại {retried})" : "") +
                            (skipped > 0 ? $", bỏ qua {skipped} điểm không trúng bề mặt phù hợp" : "") +
                            (failed > 0 ? $", {failed} lỗ không tạo được vùng lấp" : "") + ".");
    }

    private struct CutRequest
    {
        public Vector3 worldPoint;
        public Vector3 worldNormal;
        public float maskSize;
        public float depth;
        public float rotation;
        public float bigScale;
        public bool valid;
    }

    // Thử cắt tối đa (maxRetries + 1) lần. Chỉ nhận nhát cắt tạo thêm ít nhất 1 vùng lấp kín
    // (lỗ không bị khe nứt). Trả về số lần thử lại đã dùng, hoặc -1 nếu không thành công.
    private static int TryCut(BakeState s, System.Func<int, CutRequest> makeRequest)
    {
        for (int attempt = 0; attempt <= s.config.maxRetries; attempt++)
        {
            CutRequest r = makeRequest(attempt);
            if (!r.valid)
                continue;

            // Lỗ 2 lớp: thân tàu bị cắt theo lớp to, lõi bé (maskSize) được tách khi chạy game.
            float bigScale = Mathf.Max(1f, r.bigScale);

            Mesh result = MeshGeometry.RemoveExplosiveMask(
                s.current, s.hull, r.worldPoint, r.worldNormal, s.config.mask, r.maskSize * bigScale,
                s.config.alphaThreshold, r.depth, 0.1f, s.config.edgeDetail, r.rotation % 360f);

            if (result == null)
                continue;

            int newBig = CountBigRegions(result);
            if (newBig > s.bigRegions)
            {
                if (s.current != s.config.sourceMesh && !AssetDatabase.Contains(s.current))
                    Object.DestroyImmediate(s.current);

                s.current = result;
                s.bigRegions = newBig;

                if (bigScale > 1.001f)
                {
                    s.seeds.Add(new HoleLayerSeed
                    {
                        localPoint = s.hull.InverseTransformPoint(r.worldPoint),
                        localNormal = s.hull.localToWorldMatrix.transpose.MultiplyVector(r.worldNormal).normalized,
                        maskSize = r.maskSize,
                        bigScale = bigScale,
                        projectionDepth = r.depth,
                        rotation = r.rotation % 360f
                    });
                }

                return attempt;
            }

            Object.DestroyImmediate(result);
        }

        return -1;
    }

    // 0, +1, -1, +2, -2, ...
    private static float Shift(int attempt)
    {
        if (attempt == 0) return 0f;
        return ((attempt % 2 == 1) ? 1f : -1f) * ((attempt + 1) / 2);
    }

    private static void Progress(BakeState s, string label)
    {
        s.done++;
        if (EditorUtility.DisplayCancelableProgressBar(
                "Hole Pattern Bake", $"{label}: lỗ {s.done}/{s.total}", s.done / (float)Mathf.Max(1, s.total)))
            throw new System.OperationCanceledException();
    }

    // ---- Điểm đặt lỗ theo bố cục ----------------------------------------------------------

    private static List<HolePoint> GeneratePoints(HolePatternConfig.HoleGroup g, Bounds bounds)
    {
        var points = new List<HolePoint>();
        Vector3 along = AxisVector(g.alongAxis);
        Vector3 up = Vector3.up;
        Vector3 across = g.alongAxis == HolePatternConfig.Axis.X ? Vector3.forward : Vector3.right;
        float margin = bounds.size.magnitude;
        int index = 0;

        var alongValues = new List<float>();
        if (g.alongStep > 0f)
            for (float s = g.alongFrom; s <= g.alongTo + g.alongStep * 0.001f; s += g.alongStep)
                alongValues.Add(s);

        if (g.placement == HolePatternConfig.Placement.Side)
        {
            var heights = g.offsets != null && g.offsets.Count > 0 ? g.offsets : new List<float> { bounds.center.y };
            var pairs = new List<Vector2>();
            for (int k = 0; k < alongValues.Count; k++)
                pairs.Add(new Vector2(alongValues[k], heights[k % heights.Count]));
            if (g.extraPoints != null)
                pairs.AddRange(g.extraPoints);

            foreach (float sign in new[] { 1f, -1f })
            {
                if (g.sides == HolePatternConfig.SideMode.Positive && sign < 0f) continue;
                if (g.sides == HolePatternConfig.SideMode.Negative && sign > 0f) continue;

                float acrossStart = Vector3.Dot(bounds.center, across) + sign * (Vector3.Dot(bounds.extents, Abs(across)) + margin);
                int k = 0;
                foreach (Vector2 p in pairs)
                {
                    points.Add(new HolePoint
                    {
                        origin = along * p.x + up * p.y + across * acrossStart,
                        direction = -across * sign,
                        index = k++,
                        sideSign = sign
                    });
                }
            }
        }
        else
        {
            var rows = g.offsets != null && g.offsets.Count > 0 ? g.offsets : new List<float> { Vector3.Dot(bounds.center, across) };
            var pairs = new List<Vector2>();
            for (int r = 0; r < rows.Count; r++)
                foreach (float s in alongValues)
                    pairs.Add(new Vector2(s + (r % 2 == 1 ? g.oddRowShift : 0f), rows[r]));
            if (g.extraPoints != null)
                pairs.AddRange(g.extraPoints);

            float top = bounds.max.y + margin;
            foreach (Vector2 p in pairs)
            {
                points.Add(new HolePoint
                {
                    origin = along * p.x + across * p.y + up * top,
                    direction = Vector3.down,
                    index = index++,
                    sideSign = 1f
                });
            }
        }

        return points;
    }

    private static Vector3 AxisVector(HolePatternConfig.Axis axis)
    {
        switch (axis)
        {
            case HolePatternConfig.Axis.X: return Vector3.right;
            case HolePatternConfig.Axis.Y: return Vector3.up;
            default: return Vector3.forward;
        }
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    // ---- Raycast vào mesh gốc ---------------------------------------------------------------

    private static RayMesh BuildRayMesh(Mesh mesh)
    {
        var rm = new RayMesh
        {
            vertices = mesh.vertices,
            submeshTriangles = new int[mesh.subMeshCount][],
            bounds = mesh.bounds
        };

        for (int s = 0; s < mesh.subMeshCount; s++)
            rm.submeshTriangles[s] = mesh.GetTopology(s) == MeshTopology.Triangles ? mesh.GetTriangles(s) : null;

        return rm;
    }

    private static bool Raycast(RayMesh rm, Vector3 origin, Vector3 dir, out Vector3 hit, out Vector3 normal, out int submesh)
    {
        hit = Vector3.zero;
        normal = Vector3.zero;
        submesh = -1;
        float best = float.MaxValue;
        Vector3[] v = rm.vertices;

        for (int s = 0; s < rm.submeshTriangles.Length; s++)
        {
            int[] t = rm.submeshTriangles[s];
            if (t == null) continue;

            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], e1 = v[t[i + 1]] - a, e2 = v[t[i + 2]] - a;
                Vector3 p = Vector3.Cross(dir, e2);
                float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-12f) continue;

                float inv = 1f / det;
                Vector3 tv = origin - a;
                float u = Vector3.Dot(tv, p) * inv;
                if (u < 0f || u > 1f) continue;

                Vector3 q = Vector3.Cross(tv, e1);
                float w = Vector3.Dot(dir, q) * inv;
                if (w < 0f || u + w > 1f) continue;

                float dist = Vector3.Dot(e2, q) * inv;
                if (dist < 0f || dist >= best) continue;

                best = dist;
                submesh = s;
                normal = Vector3.Cross(e1, e2).normalized;
            }
        }

        if (submesh < 0)
            return false;

        hit = origin + dir * best;
        return true;
    }

    private static int CountBigRegions(Mesh mesh)
    {
        Mesh filled = MeshSurfacePatcher.FillClosedHoles(mesh, out List<FillRegion> regions, 0.0001f);
        if (filled != null)
            Object.DestroyImmediate(filled);

        int count = 0;
        if (regions != null)
            foreach (FillRegion r in regions)
                if (r.triangleIndices.Count / 3 > MinRegionTriangles)
                    count++;
        return count;
    }

    // ---- Lưu / gán -------------------------------------------------------------------------

    private static Mesh SaveMesh(HolePatternConfig config, Mesh baked, ref Mesh output, string suffix)
    {
        Mesh copy = Object.Instantiate(baked);

        if (output == null)
        {
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(config)).Replace('\\', '/');
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + config.sourceMesh.name + suffix + ".asset");
            copy.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            output = copy;
        }
        else
        {
            // Ghi đè giữ nguyên GUID để prefab vẫn trỏ đúng.
            string name = output.name;
            EditorUtility.CopySerialized(copy, output);
            output.name = name;
            Object.DestroyImmediate(copy);

            // CopySerialized không đẩy dữ liệu mới lên GPU trong phiên editor: gán lại để buộc upload.
            Vector3[] vertices = output.vertices;
            output.vertices = vertices;
            for (int s = 0; s < output.subMeshCount; s++)
                output.SetTriangles(output.GetTriangles(s), s, false);
            output.RecalculateBounds();
            output.UploadMeshData(false);
        }

        EditorUtility.SetDirty(output);
        return output;
    }

    public static void AssignToHull(Transform hull, Mesh display, Mesh source, HolePatternConfig config)
    {
        var filter = hull.GetComponent<MeshFilter>();
        if (filter != null)
            filter.sharedMesh = display;

        var fill = hull.GetComponent<RuntimeMeshFillController>();
        if (fill == null)
            fill = hull.gameObject.AddComponent<RuntimeMeshFillController>();

        var so = new SerializedObject(fill);
        so.FindProperty("sourceMesh").objectReferenceValue = source;
        so.ApplyModifiedPropertiesWithoutUndo();

        fill.SetHoleLayers(config.mask, config.alphaThreshold, config.edgeDetail, config.bakedLayerSeeds);
        EditorUtility.SetDirty(fill);
    }

    private static Transform FindHull(GameObject root, string hullPath)
    {
        if (!string.IsNullOrEmpty(hullPath))
        {
            Transform byPath = root.transform.Find(hullPath);
            if (byPath != null)
                return byPath;

            // Chỉ ghi tên: tìm ở mọi cấp.
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == hullPath)
                    return t;

            return null;
        }

        var fill = root.GetComponentInChildren<RuntimeMeshFillController>(true);
        if (fill != null)
            return fill.transform;

        var filter = root.GetComponentInChildren<MeshFilter>(true);
        return filter != null ? filter.transform : null;
    }

    // ---- Dùng từ Mesh Editor ----------------------------------------------------------------

    // Tìm config đã có cho object thân tàu này (cùng prefab + đường dẫn, hoặc mesh đang hiển thị là đầu ra của config).
    public static HolePatternConfig FindConfigFor(GameObject hullObject, Mesh currentMesh)
    {
        GetPrefabInfo(hullObject, out GameObject prefab, out string path);

        foreach (string guid in AssetDatabase.FindAssets("t:HolePatternConfig"))
        {
            var c = AssetDatabase.LoadAssetAtPath<HolePatternConfig>(AssetDatabase.GUIDToAssetPath(guid));
            if (c == null) continue;

            if (prefab != null && c.prefab == prefab &&
                (c.hullPath == path || c.hullPath == hullObject.name))
                return c;

            if (currentMesh != null && (c.displayOutput == currentMesh || c.sourceOutput == currentMesh))
                return c;
        }

        return null;
    }

    // Tạo config mới cho object thân tàu, với mesh gốc chưa cắt.
    public static HolePatternConfig CreateConfigFor(GameObject hullObject, Mesh originalMesh, Texture2D mask,
        float alphaThreshold, int edgeDetail)
    {
        if (!AssetDatabase.IsValidFolder(DefaultFolder))
            AssetDatabase.CreateFolder("Assets/MeshEditor", "HolePatterns");

        GetPrefabInfo(hullObject, out GameObject prefab, out string path);

        var c = ScriptableObject.CreateInstance<HolePatternConfig>();
        c.sourceMesh = originalMesh;
        c.prefab = prefab;
        c.hullPath = path;
        c.fallbackHullScale = hullObject.transform.lossyScale;
        c.mask = mask;
        c.alphaThreshold = alphaThreshold;
        c.edgeDetail = Mathf.Clamp(edgeDetail, 4, 10);
        c.assignToPrefab = prefab != null;

        string baseName = (prefab != null ? prefab.name : hullObject.name) + "_HolePattern";
        string assetPath = AssetDatabase.GenerateUniqueAssetPath(DefaultFolder + "/" + baseName + ".asset");
        AssetDatabase.CreateAsset(c, assetPath);
        AssetDatabase.SaveAssets();

        Debug.Log("Đã tạo Hole Pattern: " + assetPath, c);
        return c;
    }

    private static void GetPrefabInfo(GameObject hullObject, out GameObject prefab, out string path)
    {
        prefab = null;
        path = "";

        GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(hullObject);
        if (instanceRoot == null)
            return;

        prefab = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot);
        path = hullObject == instanceRoot
            ? ""
            : AnimationUtility.CalculateTransformPath(hullObject.transform, instanceRoot.transform);
    }
}
