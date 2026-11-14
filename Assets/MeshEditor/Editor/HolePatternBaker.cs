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

    public static string Validate(HolePatternConfig c)
    {
        if (c.sourceMesh == null) return "Chưa gán Source Mesh (mesh gốc chưa cắt).";
        if (!c.sourceMesh.isReadable) return "Source Mesh chưa bật Read/Write.";
        if (c.prefab == null) return "Chưa gán Prefab.";
        if (c.mask == null) return "Chưa gán Mask.";
        if (!c.mask.isReadable) return "Mask chưa bật Read/Write trong import settings.";
        if (c.groups == null || c.groups.Count == 0) return "Chưa có nhóm lỗ nào.";
        return null;
    }

    public static void Bake(HolePatternConfig config)
    {
        string problem = Validate(config);
        if (problem != null)
        {
            Debug.LogError("Hole Pattern Bake: " + problem, config);
            return;
        }

        string prefabPath = AssetDatabase.GetAssetPath(config.prefab);
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        var report = new StringBuilder();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Mesh current = config.sourceMesh;

        try
        {
            Transform hull = FindHull(root, config.hullPath);
            if (hull == null)
            {
                Debug.LogError("Hole Pattern Bake: không tìm thấy object thân tàu trong prefab.", config);
                return;
            }

            RayMesh rayMesh = BuildRayMesh(config.sourceMesh);
            int bigRegions = CountBigRegions(current);

            var displayGroups = new List<HolePatternConfig.HoleGroup>();
            var sourceOnlyGroups = new List<HolePatternConfig.HoleGroup>();
            foreach (var g in config.groups)
            {
                if (g == null || !g.enabled) continue;
                (g.includeInDisplay ? displayGroups : sourceOnlyGroups).Add(g);
            }

            int totalPoints = 0;
            foreach (var g in config.groups)
                if (g != null && g.enabled) totalPoints += GeneratePoints(g, rayMesh.bounds).Count;
            int done = 0;

            foreach (var g in displayGroups)
                current = CutGroup(config, g, hull, rayMesh, current, ref bigRegions, report, ref done, totalPoints);

            Mesh display = SaveMesh(config, current, ref config.displayOutput, "_Display");

            Mesh source = display;
            if (sourceOnlyGroups.Count > 0)
            {
                foreach (var g in sourceOnlyGroups)
                    current = CutGroup(config, g, hull, rayMesh, current, ref bigRegions, report, ref done, totalPoints);

                source = SaveMesh(config, current, ref config.sourceOutput, "_Source");
            }
            else if (config.sourceOutput != null && config.sourceOutput != display)
            {
                report.AppendLine("Lưu ý: không có nhóm chỉ-nguồn, Source Output cũ không được cập nhật (dùng Display làm nguồn).");
            }

            if (config.assignToPrefab)
            {
                AssignToPrefab(hull, display, source);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                report.AppendLine("Đã gán vào prefab: MeshFilter = " + display.name + ", Source Mesh = " + source.name + ".");
            }

            report.Insert(0,
                $"Bake xong trong {stopwatch.Elapsed.TotalSeconds:F1}s. " +
                $"Display: {display.vertexCount} đỉnh. Source: {source.vertexCount} đỉnh. Lỗ thật (vùng lớn): {bigRegions}.\n");

            config.lastBakeReport = report.ToString().TrimEnd();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            RuntimeMeshFillController.ClearSharedCache();
            Debug.Log("Hole Pattern Bake (" + config.name + "):\n" + config.lastBakeReport, config);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            PrefabUtility.UnloadPrefabContents(root);

            if (current != null && current != config.sourceMesh && !AssetDatabase.Contains(current))
                Object.DestroyImmediate(current);
        }
    }

    private static Mesh CutGroup(
        HolePatternConfig config, HolePatternConfig.HoleGroup group, Transform hull, RayMesh rayMesh,
        Mesh current, ref int bigRegions, StringBuilder report, ref int done, int totalPoints)
    {
        List<HolePoint> points = GeneratePoints(group, rayMesh.bounds);
        int cut = 0, skipped = 0, retried = 0, failed = 0;
        Vector3 alongDir = AxisVector(group.alongAxis);
        Matrix4x4 normalToWorld = hull.worldToLocalMatrix.transpose;

        foreach (HolePoint point in points)
        {
            done++;
            if (EditorUtility.DisplayCancelableProgressBar(
                    "Hole Pattern Bake", $"{group.name}: lỗ {done}/{totalPoints}", done / (float)Mathf.Max(1, totalPoints)))
                throw new System.OperationCanceledException("Bake bị hủy.");

            bool accepted = false;
            bool anyHit = false;

            for (int attempt = 0; attempt <= config.maxRetries && !accepted; attempt++)
            {
                // Thử lại: dịch nhẹ dọc thân tàu (luân phiên 2 phía) và đổi góc xoay mask.
                float shift = attempt == 0 ? 0f : ((attempt % 2 == 1) ? 1f : -1f) * ((attempt + 1) / 2) * group.alongStep * 0.08f;
                Vector3 origin = point.origin + alongDir * shift;

                if (!Raycast(rayMesh, origin, point.direction, out Vector3 hit, out Vector3 normal, out int submesh))
                    continue;

                if (group.requiredSubmesh >= 0 && submesh != group.requiredSubmesh)
                    continue;

                if (Mathf.Abs(Vector3.Dot(normal, point.direction)) < group.minSurfaceAlignment)
                    continue;

                anyHit = true;

                Vector3 worldPoint = hull.TransformPoint(hit);
                Vector3 worldNormal = normalToWorld.MultiplyVector(normal).normalized;
                if (Vector3.Dot(worldNormal, hull.TransformDirection(-point.direction)) < 0f)
                    worldNormal = -worldNormal;

                float rotation = (point.index * group.rotationStep + (point.sideSign < 0f ? 23f : 0f) + attempt * 37f) % 360f;

                Mesh result = MeshGeometry.RemoveExplosiveMask(
                    current, hull, worldPoint, worldNormal, config.mask, group.maskSize,
                    config.alphaThreshold, group.projectionDepth, 0.1f, config.edgeDetail, rotation);

                if (result == null)
                    continue;

                // Chỉ nhận nhát cắt tạo được thêm ít nhất 1 vùng lấp kín (lỗ không bị khe nứt).
                int newBig = CountBigRegions(result);
                if (newBig > bigRegions)
                {
                    if (current != config.sourceMesh && !AssetDatabase.Contains(current))
                        Object.DestroyImmediate(current);

                    current = result;
                    bigRegions = newBig;
                    accepted = true;
                    cut++;
                    if (attempt > 0) retried++;
                }
                else
                {
                    Object.DestroyImmediate(result);
                }
            }

            if (!anyHit) skipped++;
            else if (!accepted) failed++;
        }

        report.AppendLine($"• {group.name}: cắt {cut} lỗ" +
                          (retried > 0 ? $" (thử lại {retried})" : "") +
                          (skipped > 0 ? $", bỏ qua {skipped} điểm không trúng bề mặt phù hợp" : "") +
                          (failed > 0 ? $", {failed} lỗ không tạo được vùng lấp" : "") + ".");
        return current;
    }

    // ---- Điểm đặt lỗ -------------------------------------------------------------

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

    // ---- Raycast vào mesh gốc ------------------------------------------------------

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

    // ---- Lưu / gán -------------------------------------------------------------------

    private static Mesh SaveMesh(HolePatternConfig config, Mesh baked, ref Mesh output, string suffix)
    {
        Mesh copy = Object.Instantiate(baked);

        if (output == null)
        {
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(config));
            string path = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(folder, config.sourceMesh.name + suffix + ".asset").Replace('\\', '/'));
            copy.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            output = copy;
        }
        else
        {
            // Ghi đè giữ nguyên GUID để prefab / MeshStateController vẫn trỏ đúng.
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

    private static void AssignToPrefab(Transform hull, Mesh display, Mesh source)
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
}
