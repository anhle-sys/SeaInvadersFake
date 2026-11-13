using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class MeshGeometry
{
    private const float Epsilon = 0.00001f;

    private struct VertexData
    {
        public Vector3 position;
        public Vector2 uv;
        public Vector2 maskUV;
        public Vector3 normal;

        public VertexData(Vector3 position, Vector2 uv, Vector2 maskUV = default, Vector3 normal = default)
        {
            this.position = position;
            this.uv = uv;
            this.maskUV = maskUV;
            this.normal = normal;
        }

        public static VertexData Lerp(
            VertexData a,
            VertexData b,
            float t)
        {
            return new VertexData(
                Vector3.LerpUnclamped(a.position, b.position, t),
                Vector2.LerpUnclamped(a.uv, b.uv, t),
                Vector2.LerpUnclamped(a.maskUV, b.maskUV, t),
                Vector3.LerpUnclamped(a.normal, b.normal, t)
            );
        }
    }

    private class MeshBuilder
    {
        private readonly List<Vector3> vertices;
        private readonly List<Vector2> uvs;

        private readonly List<Vector3> normals;

        private readonly List<List<int>> submeshTriangles = new();

        private readonly bool indexed;
        private readonly int sourceVertexCount;

        public MeshBuilder(int submeshCount)
        {
            vertices = new List<Vector3>();
            uvs = new List<Vector2>();

            for (int i = 0; i < submeshCount; i++)
            {
                submeshTriangles.Add(new List<int>());
            }
        }

        public MeshBuilder(
            int submeshCount,
            Vector3[] sourceVertices,
            Vector2[] sourceUV,
            Vector3[] sourceNormals)
        {
            indexed = true;
            sourceVertexCount = sourceVertices.Length;

            int capacity = sourceVertexCount + sourceVertexCount / 8 + 64;

            vertices = new List<Vector3>(capacity);
            vertices.AddRange(sourceVertices);

            uvs = new List<Vector2>(capacity);
            if (sourceUV != null && sourceUV.Length == sourceVertexCount)
            {
                uvs.AddRange(sourceUV);
            }
            else
            {
                for (int i = 0; i < sourceVertexCount; i++)
                    uvs.Add(Vector2.zero);
            }

            if (sourceNormals != null && sourceNormals.Length == sourceVertexCount)
            {
                normals = new List<Vector3>(capacity);
                normals.AddRange(sourceNormals);
            }

            for (int i = 0; i < submeshCount; i++)
            {
                submeshTriangles.Add(new List<int>());
            }
        }

        public void Reserve(int submeshIndex, int indexCount)
        {
            List<int> list = submeshTriangles[submeshIndex];
            if (list.Capacity < indexCount)
                list.Capacity = indexCount;
        }

        public void AddIndexedTriangle(int submeshIndex, int i0, int i1, int i2)
        {
            List<int> triangles = submeshTriangles[submeshIndex];
            triangles.Add(i0);
            triangles.Add(i1);
            triangles.Add(i2);
        }

        public void AddTriangle(
            int submeshIndex,
            VertexData a,
            VertexData b,
            VertexData c)
        {
            int start = vertices.Count;

            vertices.Add(a.position);
            vertices.Add(b.position);
            vertices.Add(c.position);

            uvs.Add(a.uv);
            uvs.Add(b.uv);
            uvs.Add(c.uv);

            if (normals != null)
            {
                normals.Add(a.normal);
                normals.Add(b.normal);
                normals.Add(c.normal);
            }

            List<int> triangles = submeshTriangles[submeshIndex];

            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        public Mesh Build(string meshName)
        {
            int totalTriangles = 0;

            foreach (var list in submeshTriangles)
                totalTriangles += list.Count;

            if (totalTriangles == 0)
                return null;

            if (indexed)
            {
                NormalizeAppendedNormals();
                CompactUnusedVertices();
            }

            Mesh mesh = new Mesh
            {
                name = meshName
            };

            if (vertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);

            mesh.subMeshCount = submeshTriangles.Count;

            for (int i = 0; i < submeshTriangles.Count; i++)
            {
                mesh.SetTriangles(
                    submeshTriangles[i],
                    i,
                    false
                );
            }

            if (normals != null)
                mesh.SetNormals(normals);
            else
                mesh.RecalculateNormals();

            mesh.RecalculateBounds();

            return mesh;
        }

        private void NormalizeAppendedNormals()
        {
            if (normals == null)
                return;

            for (int i = sourceVertexCount; i < normals.Count; i++)
            {
                Vector3 n = normals[i];
                normals[i] = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
            }
        }

        private void CompactUnusedVertices()
        {
            int count = vertices.Count;
            bool[] used = new bool[count];

            foreach (List<int> list in submeshTriangles)
            {
                for (int i = 0; i < list.Count; i++)
                    used[list[i]] = true;
            }

            int[] remap = new int[count];
            int next = 0;

            for (int i = 0; i < count; i++)
            {
                if (used[i])
                    remap[i] = next++;
            }

            if (next == count)
                return;

            // In-place compaction: destination index is always <= source index.
            for (int i = 0; i < count; i++)
            {
                if (!used[i])
                    continue;

                int d = remap[i];
                vertices[d] = vertices[i];
                uvs[d] = uvs[i];

                if (normals != null)
                    normals[d] = normals[i];
            }

            vertices.RemoveRange(next, count - next);
            uvs.RemoveRange(next, count - next);

            if (normals != null)
                normals.RemoveRange(next, count - next);

            foreach (List<int> list in submeshTriangles)
            {
                for (int i = 0; i < list.Count; i++)
                    list[i] = remap[list[i]];
            }
        }
    }

    private static Vector2 GetUV(Vector2[] uv, int index)
    {
        if (uv != null && uv.Length > index)
            return uv[index];

        return Vector2.zero;
    }

    private static VertexData GetVertex(
        Vector3[] vertices,
        Vector2[] uv,
        int index)
    {
        return new VertexData(
            vertices[index],
            GetUV(uv, index)
        );
    }

    private sealed class AlphaMaskData
    {
        public readonly int width;
        public readonly int height;
        private readonly byte[] solid;
        private readonly int[] summedArea;

        public AlphaMaskData(Texture2D texture, float threshold)
        {
            width = texture.width;
            height = texture.height;
            Color32[] pixels = texture.GetPixels32();
            solid = new byte[pixels.Length];
            summedArea = new int[(width + 1) * (height + 1)];
            byte thresholdByte = (byte)Mathf.Clamp(Mathf.RoundToInt(threshold * 255f), 0, 255);

            for (int y = 0; y < height; y++)
            {
                int rowSum = 0;
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    byte value = pixels[index].a >= thresholdByte ? (byte)1 : (byte)0;
                    solid[index] = value;
                    rowSum += value;
                    summedArea[(y + 1) * (width + 1) + (x + 1)] =
                        summedArea[y * (width + 1) + (x + 1)] + rowSum;
                }
            }
        }

        public bool IsSolid(Vector2 uv)
        {
            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
                return false;

            int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * width), 0, width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * height), 0, height - 1);
            return solid[y * width + x] != 0;
        }

        // Returns 0 = fully transparent, 1 = fully opaque, -1 = mixed.
        public int GetRectState(Vector2 a, Vector2 b, Vector2 c)
        {
            float minU = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
            float maxU = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float minV = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            float maxV = Mathf.Max(a.y, Mathf.Max(b.y, c.y));

            if (maxU < 0f || minU > 1f || maxV < 0f || minV > 1f)
                return 0;

            // A rectangle crossing the mask boundary is mixed because outside is transparent.
            bool crossesBoundary = minU < 0f || maxU > 1f || minV < 0f || maxV > 1f;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(minU * width), 0, width - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt(maxU * width), 0, width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(minV * height), 0, height - 1);
            int y1 = Mathf.Clamp(Mathf.FloorToInt(maxV * height), 0, height - 1);

            int rectWidth = x1 - x0 + 1;
            int rectHeight = y1 - y0 + 1;
            int sum = RectangleSum(x0, y0, x1, y1);
            int count = rectWidth * rectHeight;

            if (sum == 0 && !crossesBoundary) return 0;
            if (sum == count && !crossesBoundary) return 1;
            return -1;
        }

        private int RectangleSum(int x0, int y0, int x1, int y1)
        {
            int stride = width + 1;
            return summedArea[(y1 + 1) * stride + (x1 + 1)]
                 - summedArea[y0 * stride + (x1 + 1)]
                 - summedArea[(y1 + 1) * stride + x0]
                 + summedArea[y0 * stride + x0];
        }
    }

    // ---- Alpha mask cache -------------------------------------------------
    // Building AlphaMaskData (GetPixels32 + summed-area table) is O(width*height)
    // and used to run on every cut. It only depends on the texture and threshold.
    private static Texture2D cachedMaskTexture;
    private static int cachedMaskThreshold = -1;
    private static int cachedMaskWidth;
    private static int cachedMaskHeight;
    private static AlphaMaskData cachedMaskData;

    /// <summary>
    /// Drops the cached alpha mask data. Call this if the mask texture's
    /// pixels were modified/reimported while the same Texture2D instance is kept.
    /// </summary>
    public static void ClearMaskCache()
    {
        cachedMaskTexture = null;
        cachedMaskThreshold = -1;
        cachedMaskWidth = 0;
        cachedMaskHeight = 0;
        cachedMaskData = null;
    }

    private static AlphaMaskData GetMaskData(Texture2D texture, float threshold)
    {
        int thresholdByte = Mathf.Clamp(Mathf.RoundToInt(threshold * 255f), 0, 255);

        if (cachedMaskData != null &&
            cachedMaskTexture == texture &&
            cachedMaskThreshold == thresholdByte &&
            cachedMaskWidth == texture.width &&
            cachedMaskHeight == texture.height)
        {
            return cachedMaskData;
        }

        cachedMaskData = new AlphaMaskData(texture, threshold);
        cachedMaskTexture = texture;
        cachedMaskThreshold = thresholdByte;
        cachedMaskWidth = texture.width;
        cachedMaskHeight = texture.height;
        return cachedMaskData;
    }

    public static Mesh RemoveExplosiveMask(
        Mesh source,
        Transform meshTransform,
        Vector3 centerWorld,
        Vector3 hitNormalWorld,
        Texture2D alphaMask,
        float maskSizeWorld,
        float alphaThreshold = 0.5f,
        float projectionDepth = 0.15f,
        float minNormalDot = 0.1f,
        int maxSubdivisionDepth = 8,
        float rotationDegrees = 0f)
    {
        if (source == null || !source.isReadable || meshTransform == null ||
            alphaMask == null || !alphaMask.isReadable || maskSizeWorld <= 0f ||
            hitNormalWorld.sqrMagnitude < Epsilon * Epsilon)
            return null;

        hitNormalWorld.Normalize();
        Vector3 reference = Mathf.Abs(Vector3.Dot(hitNormalWorld, Vector3.up)) > 0.98f
            ? Vector3.right : Vector3.up;
        Vector3 right = Vector3.Cross(reference, hitNormalWorld).normalized;
        Vector3 up = Vector3.Cross(hitNormalWorld, right).normalized;
        Quaternion rotation = Quaternion.AngleAxis(rotationDegrees, hitNormalWorld);
        right = rotation * right;
        up = rotation * up;

        Vector3[] vertices = source.vertices;
        Vector2[] uv = source.uv;
        Vector3[] sourceNormals = source.normals;
        bool hasNormals = sourceNormals != null && sourceNormals.Length == vertices.Length;

        AlphaMaskData mask = GetMaskData(alphaMask, alphaThreshold);
        int depthLimit = Mathf.Clamp(maxSubdivisionDepth, 1, 10);

        // Per-vertex precompute: world position, mask UV and signed plane distance
        // are computed once per vertex instead of ~6 times (once per adjacent triangle).
        Matrix4x4 localToWorld = meshTransform.localToWorldMatrix;
        float invMaskSize = 1f / maskSizeWorld;
        int vertexCount = vertices.Length;
        Vector3[] worldPositions = new Vector3[vertexCount];
        Vector2[] maskUVs = new Vector2[vertexCount];
        float[] planeDistances = new float[vertexCount];

        for (int v = 0; v < vertexCount; v++)
        {
            Vector3 world = localToWorld.MultiplyPoint3x4(vertices[v]);
            Vector3 delta = world - centerWorld;
            worldPositions[v] = world;
            maskUVs[v] = new Vector2(
                Vector3.Dot(delta, right) * invMaskSize + 0.5f,
                Vector3.Dot(delta, up) * invMaskSize + 0.5f);
            planeDistances[v] = Vector3.Dot(delta, hitNormalWorld);
        }

        MeshBuilder builder = new MeshBuilder(
            source.subMeshCount, vertices, uv, hasNormals ? sourceNormals : null);

        for (int submesh = 0; submesh < source.subMeshCount; submesh++)
        {
            if (source.GetTopology(submesh) != MeshTopology.Triangles)
                continue;

            int[] triangles = source.GetTriangles(submesh);
            builder.Reserve(submesh, triangles.Length);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                Vector2 ua = maskUVs[i0];
                Vector2 ub = maskUVs[i1];
                Vector2 uc = maskUVs[i2];

                // Cheapest test first: triangle completely outside the opaque
                // mask area is kept untouched and re-uses its original vertices.
                int state = mask.GetRectState(ua, ub, uc);
                if (state == 0)
                {
                    builder.AddIndexedTriangle(submesh, i0, i1, i2);
                    continue;
                }

                Vector3 wa = worldPositions[i0];
                Vector3 wb = worldPositions[i1];
                Vector3 wc = worldPositions[i2];
                Vector3 triangleNormal = Vector3.Cross(wb - wa, wc - wa).normalized;
                float planeDistance = Mathf.Abs(
                    (planeDistances[i0] + planeDistances[i1] + planeDistances[i2]) / 3f);

                bool sameFacingSurface = Vector3.Dot(triangleNormal, hitNormalWorld) >= minNormalDot;
                bool withinProjectionDepth = projectionDepth <= 0f || planeDistance <= projectionDepth;

                if (!sameFacingSurface || !withinProjectionDepth)
                {
                    builder.AddIndexedTriangle(submesh, i0, i1, i2);
                    continue;
                }

                // Entirely inside the opaque part of the mask: removed.
                if (state == 1)
                    continue;

                VertexData a = new VertexData(vertices[i0], GetUV(uv, i0), ua,
                    hasNormals ? sourceNormals[i0] : default);
                VertexData b = new VertexData(vertices[i1], GetUV(uv, i1), ub,
                    hasNormals ? sourceNormals[i1] : default);
                VertexData c = new VertexData(vertices[i2], GetUV(uv, i2), uc,
                    hasNormals ? sourceNormals[i2] : default);

                ProcessMaskTriangle(builder, mask, submesh, a, b, c, 0, depthLimit);
            }
        }

        return builder.Build(source.name + "_ExplosiveMaskRemoved");
    }

    private static void ProcessMaskTriangle(
        MeshBuilder builder, AlphaMaskData mask, int submesh,
        VertexData a, VertexData b, VertexData c, int depth, int maxDepth)
    {
        int state = mask.GetRectState(a.maskUV, b.maskUV, c.maskUV);
        if (state == 0)
        {
            builder.AddTriangle(submesh, a, b, c);
            return;
        }
        if (state == 1)
            return;

        if (depth >= maxDepth)
        {
            Vector2 center = (a.maskUV + b.maskUV + c.maskUV) / 3f;
            if (!mask.IsSolid(center))
                builder.AddTriangle(submesh, a, b, c);
            return;
        }

        VertexData ab = VertexData.Lerp(a, b, 0.5f);
        VertexData bc = VertexData.Lerp(b, c, 0.5f);
        VertexData ca = VertexData.Lerp(c, a, 0.5f);

        ProcessMaskTriangle(builder, mask, submesh, a, ab, ca, depth + 1, maxDepth);
        ProcessMaskTriangle(builder, mask, submesh, ab, b, bc, depth + 1, maxDepth);
        ProcessMaskTriangle(builder, mask, submesh, ca, bc, c, depth + 1, maxDepth);
        ProcessMaskTriangle(builder, mask, submesh, ab, bc, ca, depth + 1, maxDepth);
    }

    public static Mesh[] Slice(
        Mesh source,
        Vector3 planePoint,
        Vector3 planeNormal)
    {
        if (source == null ||
            !source.isReadable ||
            planeNormal.sqrMagnitude < Epsilon * Epsilon)
        {
            return null;
        }

        planeNormal.Normalize();

        Vector3[] vertices = source.vertices;
        Vector2[] uv = source.uv;

        MeshBuilder positive =
            new MeshBuilder(source.subMeshCount);

        MeshBuilder negative =
            new MeshBuilder(source.subMeshCount);

        for (int submesh = 0;
             submesh < source.subMeshCount;
             submesh++)
        {
            if (source.GetTopology(submesh) != MeshTopology.Triangles)
                continue;

            int[] triangles = source.GetTriangles(submesh);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                VertexData a = GetVertex(vertices, uv, triangles[i]);
                VertexData b = GetVertex(vertices, uv, triangles[i + 1]);
                VertexData c = GetVertex(vertices, uv, triangles[i + 2]);

                List<VertexData> polygon = new()
                {
                    a, b, c
                };

                AddClippedPolygon(
                    positive,
                    ClipPolygon(polygon, planePoint, planeNormal, true),
                    submesh
                );

                AddClippedPolygon(
                    negative,
                    ClipPolygon(polygon, planePoint, planeNormal, false),
                    submesh
                );
            }
        }

        return new Mesh[]
        {
            positive.Build(source.name + "_Positive"),
            negative.Build(source.name + "_Negative")
        };
    }

    private static List<VertexData> ClipPolygon(
        List<VertexData> polygon,
        Vector3 planePoint,
        Vector3 planeNormal,
        bool keepPositive)
    {
        List<VertexData> result = new();

        for (int i = 0; i < polygon.Count; i++)
        {
            VertexData current = polygon[i];
            VertexData next = polygon[(i + 1) % polygon.Count];

            float d1 = Vector3.Dot(
                planeNormal,
                current.position - planePoint
            );

            float d2 = Vector3.Dot(
                planeNormal,
                next.position - planePoint
            );

            bool inside1 = keepPositive
                ? d1 >= -Epsilon
                : d1 <= Epsilon;

            bool inside2 = keepPositive
                ? d2 >= -Epsilon
                : d2 <= Epsilon;

            if (inside1 && inside2)
            {
                result.Add(next);
            }
            else if (inside1 && !inside2)
            {
                result.Add(GetIntersection(current, next, d1, d2));
            }
            else if (!inside1 && inside2)
            {
                result.Add(GetIntersection(current, next, d1, d2));
                result.Add(next);
            }
        }

        return result;
    }

    private static VertexData GetIntersection(
        VertexData a,
        VertexData b,
        float da,
        float db)
    {
        float t = da / (da - db);

        return VertexData.Lerp(a, b, t);
    }

    private static void AddClippedPolygon(
        MeshBuilder builder,
        List<VertexData> polygon,
        int submeshIndex)
    {
        if (polygon.Count < 3)
            return;

        for (int i = 1; i < polygon.Count - 1; i++)
        {
            builder.AddTriangle(
                submeshIndex,
                polygon[0],
                polygon[i],
                polygon[i + 1]
            );
        }
    }
}
