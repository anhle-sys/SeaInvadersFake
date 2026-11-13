using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class MeshSurfaceClipper
{
    private const float Epsilon = 0.00001f;

    public static Mesh ApplyMask(
        Mesh source, 
        Transform meshTransform,
        Vector3 hitPosition,
        Vector3 hitNormal,
        float radius,
        LocalProjectionBuilder projection,
        TextureMaskProcessor.MaskData mask,
        float minimumNormalDot,
        float planeTolerance)
    {
        if (source == null || !source.isReadable || meshTransform == null ||
            mask == null || radius <= 0f)
            return null;

        Vector3[] sourceVertices = source.vertices;
        Vector3[] sourceNormals = source.normals;
        Vector4[] sourceTangents = source.tangents;
        Color[] sourceColors = source.colors;
        Vector2[] sourceUV0 = source.uv;
        Vector2[] sourceUV1 = source.uv2;

        RuntimeMeshBuilder builder = new RuntimeMeshBuilder(source);
        builder.PreloadSourceVertices(
            sourceVertices, sourceNormals, sourceTangents, sourceColors, sourceUV0, sourceUV1);
        bool removedAny = false;

        int boxMinX = int.MaxValue, boxMinY = int.MaxValue, boxMaxX = -1, boxMaxY = -1;
        for (int my = 0; my < mask.Height; my++)
        {
            for (int mx = 0; mx < mask.Width; mx++)
            {
                if (!mask.IsDamaged(mx, my))
                    continue;

                if (mx < boxMinX) boxMinX = mx;
                if (my < boxMinY) boxMinY = my;
                if (mx > boxMaxX) boxMaxX = mx;
                if (my > boxMaxY) boxMaxY = my;
            }
        }

        if (boxMaxX < 0)
            return null;

        float rectMinX = boxMinX, rectMaxX = boxMaxX + 1f;
        float rectMinY = boxMinY, rectMaxY = boxMaxY + 1f;

        // Vùng ảnh hưởng (hình hộp bao quanh hình vuông mask + dung sai mặt phẳng).
        Matrix4x4 localToWorld = meshTransform.localToWorldMatrix;
        float regionExtent = radius * 1.5f + planeTolerance;
        Vector3 regionMin = hitPosition - Vector3.one * regionExtent;
        Vector3 regionMax = hitPosition + Vector3.one * regionExtent;

        for (int submesh = 0; submesh < source.subMeshCount; submesh++)
        {
            if (source.GetTopology(submesh) != MeshTopology.Triangles)
                continue;

            int[] triangles = source.GetTriangles(submesh);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int ia = triangles[i];
                int ib = triangles[i + 1];
                int ic = triangles[i + 2];

                Vector3 wa = localToWorld.MultiplyPoint3x4(sourceVertices[ia]);
                Vector3 wb = localToWorld.MultiplyPoint3x4(sourceVertices[ib]);
                Vector3 wc = localToWorld.MultiplyPoint3x4(sourceVertices[ic]);

                // Tam giác nằm hẳn ngoài vùng ảnh hưởng: giữ nguyên chỉ số, bỏ qua mọi xử lý nặng.
                if (!OverlapsRegion(wa, wb, wc, regionMin, regionMax))
                {
                    builder.AddSharedTriangle(submesh, ia, ib, ic);
                    continue;
                }

                Vector3 faceNormal = Vector3.Cross(wb - wa, wc - wa).normalized;
                Vector3 centroid = (wa + wb + wc) / 3f;

                bool sameSurface = Vector3.Dot(faceNormal, hitNormal.normalized) >= minimumNormalDot;
                bool closeToPlane = Mathf.Abs(projection.SignedDistanceToPlane(centroid)) <= planeTolerance;

                if (!sameSurface || !closeToPlane)
                {
                    builder.AddSharedTriangle(submesh, ia, ib, ic);
                    continue;
                }

                RuntimeMeshBuilder.VertexData a = MakeVertex(
                    sourceVertices, sourceNormals, sourceTangents, sourceColors,
                    sourceUV0, sourceUV1, ia, meshTransform, projection, radius, mask.Width, mask.Height);

                RuntimeMeshBuilder.VertexData b = MakeVertex(
                    sourceVertices, sourceNormals, sourceTangents, sourceColors,
                    sourceUV0, sourceUV1, ib, meshTransform, projection, radius, mask.Width, mask.Height);

                RuntimeMeshBuilder.VertexData c = MakeVertex(
                    sourceVertices, sourceNormals, sourceTangents, sourceColors,
                    sourceUV0, sourceUV1, ic, meshTransform, projection, radius, mask.Width, mask.Height);

                List<RuntimeMeshBuilder.VertexData> triangle = new List<RuntimeMeshBuilder.VertexData>(3)
                {
                    a, b, c
                };

                // Keep all portions outside the mask rectangle.
                List<RuntimeMeshBuilder.VertexData> remaining = KeepOutsideMaskRectangle(
                    triangle, rectMinX, rectMaxX, rectMinY, rectMaxY, builder, submesh);

                if (remaining.Count >= 3)
                {
                    float minX = float.MaxValue, minY = float.MaxValue;
                    float maxX = float.MinValue, maxY = float.MinValue;

                    for (int p = 0; p < remaining.Count; p++)
                    {
                        Vector2 q = remaining[p].MaskPoint;
                        minX = Mathf.Min(minX, q.x);
                        minY = Mathf.Min(minY, q.y);
                        maxX = Mathf.Max(maxX, q.x);
                        maxY = Mathf.Max(maxY, q.y);
                    }

                    int minCellX = Mathf.Clamp(Mathf.FloorToInt(minX), 0, mask.Width - 1);
                    int minCellY = Mathf.Clamp(Mathf.FloorToInt(minY), 0, mask.Height - 1);
                    int maxCellX = Mathf.Clamp(Mathf.CeilToInt(maxX) - 1, 0, mask.Width - 1);
                    int maxCellY = Mathf.Clamp(Mathf.CeilToInt(maxY) - 1, 0, mask.Height - 1);

                    for (int y = minCellY; y <= maxCellY; y++)
                    {
                        for (int x = minCellX; x <= maxCellX; x++)
                        {
                            List<RuntimeMeshBuilder.VertexData> cellPolygon = ClipToCell(
                                remaining, x, y);

                            if (cellPolygon.Count < 3)
                                continue;

                            if (mask.IsDamaged(x, y))
                            {
                                removedAny = true;
                                continue;
                            }

                            AddPolygon(builder, submesh, cellPolygon);
                        }
                    }
                }
            }
        }

        if (!removedAny)
            return null;

        Mesh result = builder.Build(source.name + "_RuntimeDestroyed");
        if (result != null)
        {
            result.bindposes = source.bindposes;
        }

        return result;
    }

    private static bool OverlapsRegion(Vector3 a, Vector3 b, Vector3 c, Vector3 min, Vector3 max)
    {
        if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < min.x || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > max.x) return false;
        if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < min.y || Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > max.y) return false;
        if (Mathf.Max(a.z, Mathf.Max(b.z, c.z)) < min.z || Mathf.Min(a.z, Mathf.Min(b.z, c.z)) > max.z) return false;
        return true;
    }

    private static RuntimeMeshBuilder.VertexData MakeVertex(
        Vector3[] vertices,
        Vector3[] normals,
        Vector4[] tangents,
        Color[] colors,
        Vector2[] uv0,
        Vector2[] uv1,
        int index,
        Transform meshTransform,
        LocalProjectionBuilder projection,
        float radius,
        int width,
        int height)
    {
        Vector3 localPosition = vertices[index];
        Vector3 worldPosition = meshTransform.TransformPoint(localPosition);
        Vector2 plane = projection.WorldToPlane(worldPosition);

        Vector2 maskPoint = new Vector2(
            (plane.x / (2f * radius) + 0.5f) * width,
            (plane.y / (2f * radius) + 0.5f) * height);

        Vector3 normal = normals != null && normals.Length == vertices.Length
            ? normals[index]
            : Vector3.zero;

        Vector4 tangent = tangents != null && tangents.Length == vertices.Length
            ? tangents[index]
            : Vector4.zero;

        Color color = colors != null && colors.Length == vertices.Length
            ? colors[index]
            : Color.white;

        Vector2 u0 = uv0 != null && uv0.Length == vertices.Length
            ? uv0[index]
            : Vector2.zero;

        Vector2 u1 = uv1 != null && uv1.Length == vertices.Length
            ? uv1[index]
            : Vector2.zero;

        return new RuntimeMeshBuilder.VertexData(
            localPosition, normal, tangent, color, u0, u1, maskPoint);
    }

    private static List<RuntimeMeshBuilder.VertexData> KeepOutsideMaskRectangle(
        List<RuntimeMeshBuilder.VertexData> polygon,
        float rectMinX,
        float rectMaxX,
        float rectMinY,
        float rectMaxY,
        RuntimeMeshBuilder builder,
        int submesh)
    {
        List<RuntimeMeshBuilder.VertexData> inside = polygon;

        // Partition outside regions without overlapping corner areas.
        AddPolygon(builder, submesh, ClipHalfPlane(inside, 0, rectMinX, false));
        inside = ClipHalfPlane(inside, 0, rectMinX, true);

        AddPolygon(builder, submesh, ClipHalfPlane(inside, 0, rectMaxX, true));
        inside = ClipHalfPlane(inside, 0, rectMaxX, false);

        AddPolygon(builder, submesh, ClipHalfPlane(inside, 1, rectMinY, false));
        inside = ClipHalfPlane(inside, 1, rectMinY, true);

        AddPolygon(builder, submesh, ClipHalfPlane(inside, 1, rectMaxY, true));
        inside = ClipHalfPlane(inside, 1, rectMaxY, false);

        return inside;
    }

    private static List<RuntimeMeshBuilder.VertexData> ClipToCell(
        List<RuntimeMeshBuilder.VertexData> polygon,
        int x,
        int y)
    {
        List<RuntimeMeshBuilder.VertexData> result = polygon;
        result = ClipHalfPlane(result, 0, x, true);
        result = ClipHalfPlane(result, 0, x + 1f, false);
        result = ClipHalfPlane(result, 1, y, true);
        result = ClipHalfPlane(result, 1, y + 1f, false);
        return result;
    }

    // axis 0 = X, axis 1 = Y. keepGreater selects coordinate >= boundary.
    private static List<RuntimeMeshBuilder.VertexData> ClipHalfPlane(
        List<RuntimeMeshBuilder.VertexData> input,
        int axis,
        float boundary,
        bool keepGreater)
    {
        List<RuntimeMeshBuilder.VertexData> output =
            new List<RuntimeMeshBuilder.VertexData>();

        if (input == null || input.Count == 0)
            return output;

        for (int i = 0; i < input.Count; i++)
        {
            RuntimeMeshBuilder.VertexData current = input[i];
            RuntimeMeshBuilder.VertexData next = input[(i + 1) % input.Count];

            float a = axis == 0 ? current.MaskPoint.x : current.MaskPoint.y;
            float b = axis == 0 ? next.MaskPoint.x : next.MaskPoint.y;

            bool insideA = keepGreater ? a >= boundary - Epsilon : a <= boundary + Epsilon;
            bool insideB = keepGreater ? b >= boundary - Epsilon : b <= boundary + Epsilon;

            if (insideA && insideB)
            {
                output.Add(next);
            }
            else if (insideA && !insideB)
            {
                output.Add(Intersection(current, next, a, b, boundary));
            }
            else if (!insideA && insideB)
            {
                output.Add(Intersection(current, next, a, b, boundary));
                output.Add(next);
            }
        }

        return RemoveDuplicateAdjacent(output);
    }

    private static RuntimeMeshBuilder.VertexData Intersection(
        RuntimeMeshBuilder.VertexData a,
        RuntimeMeshBuilder.VertexData b,
        float coordinateA,
        float coordinateB,
        float boundary)
    {
        float denominator = coordinateB - coordinateA;
        float t = Mathf.Abs(denominator) < Epsilon
            ? 0f
            : (boundary - coordinateA) / denominator;

        return RuntimeMeshBuilder.VertexData.Lerp(a, b, Mathf.Clamp01(t));
    }

    private static List<RuntimeMeshBuilder.VertexData> RemoveDuplicateAdjacent(
        List<RuntimeMeshBuilder.VertexData> input)
    {
        if (input.Count < 2)
            return input;

        List<RuntimeMeshBuilder.VertexData> output =
            new List<RuntimeMeshBuilder.VertexData>(input.Count);

        for (int i = 0; i < input.Count; i++)
        {
            if (output.Count == 0 ||
                (output[output.Count - 1].MaskPoint - input[i].MaskPoint).sqrMagnitude > Epsilon * Epsilon)
            {
                output.Add(input[i]);
            }
        }

        if (output.Count > 1 &&
            (output[0].MaskPoint - output[output.Count - 1].MaskPoint).sqrMagnitude <= Epsilon * Epsilon)
        {
            output.RemoveAt(output.Count - 1);
        }

        return output;
    }

    private static void AddPolygon(
        RuntimeMeshBuilder builder,
        int submesh,
        List<RuntimeMeshBuilder.VertexData> polygon)
    {
        if (polygon == null || polygon.Count < 3)
            return;

        for (int i = 1; i < polygon.Count - 1; i++)
            builder.AddTriangle(submesh, polygon[0], polygon[i], polygon[i + 1]);
    }
}
