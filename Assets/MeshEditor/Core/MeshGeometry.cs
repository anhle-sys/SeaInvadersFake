
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

        public VertexData(Vector3 position, Vector2 uv)
        {
            this.position = position;
            this.uv = uv;
        }

        public static VertexData Lerp(
            VertexData a,
            VertexData b,
            float t)
        {
            return new VertexData(
                Vector3.LerpUnclamped(a.position, b.position, t),
                Vector2.LerpUnclamped(a.uv, b.uv, t)
            );
        }
    }

    private class MeshBuilder
    {
        private readonly List<Vector3> vertices = new();
        private readonly List<Vector2> uvs = new();

        private readonly List<List<int>> submeshTriangles = new();

        public MeshBuilder(int submeshCount)
        {
            for (int i = 0; i < submeshCount; i++)
            {
                submeshTriangles.Add(new List<int>());
            }
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

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
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

    public static Mesh RemoveBrush(
        Mesh source,
        Transform meshTransform,
        Vector3 centerWorld,
        float radiusWorld,
        int hitSubmesh,
        int hitTriangleIndex,
        Vector3 hitNormalWorld)
    {
        if (source == null ||
            !source.isReadable ||
            radiusWorld <= 0f)
        {
            return null;
        }

        Vector3[] vertices = source.vertices;
        Vector2[] uv = source.uv;

        MeshBuilder builder =
            new MeshBuilder(source.subMeshCount);

        float radiusSqr = radiusWorld * radiusWorld;

        for (int submesh = 0;
             submesh < source.subMeshCount;
             submesh++)
        {
            if (source.GetTopology(submesh) != MeshTopology.Triangles)
                continue;

            int[] triangles = source.GetTriangles(submesh);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int ia = triangles[i];
                int ib = triangles[i + 1];
                int ic = triangles[i + 2];

                Vector3 a = vertices[ia];
                Vector3 b = vertices[ib];
                Vector3 c = vertices[ic];

                Vector3 worldA = meshTransform.TransformPoint(a);
                Vector3 worldB = meshTransform.TransformPoint(b);
                Vector3 worldC = meshTransform.TransformPoint(c);

                Vector3 centroid =
                    (worldA + worldB + worldC) / 3f;

                Vector3 triangleNormal = Vector3.Cross(
                    worldB - worldA,
                    worldC - worldA
                ).normalized;

                bool isClickedTriangle =
                    submesh == hitSubmesh &&
                    i / 3 == hitTriangleIndex;

                bool isInsideBrush =
                    (centroid - centerWorld).sqrMagnitude <= radiusSqr;

                bool sameSurface =
                    Vector3.Dot(
                        triangleNormal,
                        hitNormalWorld
                    ) > 0.1f;

                bool shouldRemove =
                    isClickedTriangle ||
                    (isInsideBrush && sameSurface);

                if (shouldRemove)
                    continue;

                builder.AddTriangle(
                    submesh,
                    GetVertex(vertices, uv, ia),
                    GetVertex(vertices, uv, ib),
                    GetVertex(vertices, uv, ic)
                );
            }
        }

        return builder.Build(source.name + "_BrushRemoved");
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
