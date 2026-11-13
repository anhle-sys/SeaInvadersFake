using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class RuntimeMeshBuilder
{
    private struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Tangent;
        public Color Color;
        public Vector2 UV0;
        public Vector2 UV1;
    }

    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector3> normals = new List<Vector3>();
    private readonly List<Vector4> tangents = new List<Vector4>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<Vector2> uv0 = new List<Vector2>();
    private readonly List<Vector2> uv1 = new List<Vector2>();
    private readonly List<List<int>> submeshTriangles = new List<List<int>>();

    private readonly bool hasNormals;
    private readonly bool hasTangents;
    private readonly bool hasColors;
    private readonly bool hasUV0;
    private readonly bool hasUV1;

    public RuntimeMeshBuilder(Mesh source)
    {
        // HasVertexAttribute tránh cấp phát lại cả mảng normals/tangents/... chỉ để kiểm tra độ dài
        hasNormals = source != null && source.HasVertexAttribute(VertexAttribute.Normal);
        hasTangents = source != null && source.HasVertexAttribute(VertexAttribute.Tangent);
        hasColors = source != null && source.HasVertexAttribute(VertexAttribute.Color);
        hasUV0 = source != null && source.HasVertexAttribute(VertexAttribute.TexCoord0);
        hasUV1 = source != null && source.HasVertexAttribute(VertexAttribute.TexCoord1);

        int count = source != null ? source.subMeshCount : 1;
        for (int i = 0; i < count; i++)
            submeshTriangles.Add(new List<int>());
    }

    // Nạp sẵn toàn bộ đỉnh gốc: các tam giác không bị ảnh hưởng dùng lại đúng chỉ số cũ,
    // không nhân bản đỉnh. Phải gọi trước mọi AddTriangle.
    public void PreloadSourceVertices(
        Vector3[] v, Vector3[] n, Vector4[] t, Color[] c, Vector2[] u0, Vector2[] u1)
    {
        vertices.AddRange(v);
        if (hasNormals && n != null && n.Length == v.Length) normals.AddRange(n);
        else if (hasNormals) for (int i = 0; i < v.Length; i++) normals.Add(Vector3.zero);
        if (hasTangents && t != null && t.Length == v.Length) tangents.AddRange(t);
        else if (hasTangents) for (int i = 0; i < v.Length; i++) tangents.Add(Vector4.zero);
        if (hasColors && c != null && c.Length == v.Length) colors.AddRange(c);
        else if (hasColors) for (int i = 0; i < v.Length; i++) colors.Add(Color.white);
        if (hasUV0 && u0 != null && u0.Length == v.Length) uv0.AddRange(u0);
        else if (hasUV0) for (int i = 0; i < v.Length; i++) uv0.Add(Vector2.zero);
        if (hasUV1 && u1 != null && u1.Length == v.Length) uv1.AddRange(u1);
        else if (hasUV1) for (int i = 0; i < v.Length; i++) uv1.Add(Vector2.zero);
    }

    public void AddSharedTriangle(int submesh, int ia, int ib, int ic)
    {
        if (submesh < 0 || submesh >= submeshTriangles.Count)
            return;

        List<int> indices = submeshTriangles[submesh];
        indices.Add(ia);
        indices.Add(ib);
        indices.Add(ic);
    }

    public void AddTriangle(int submesh, VertexData a, VertexData b, VertexData c)
    {
        AddTriangleInternal(submesh, Convert(a), Convert(b), Convert(c));
    }

    public void AddOriginalTriangle(
        int submesh,
        Vector3[] sourceVertices,
        Vector3[] sourceNormals,
        Vector4[] sourceTangents,
        Color[] sourceColors,
        Vector2[] sourceUV0,
        Vector2[] sourceUV1,
        int ia,
        int ib,
        int ic)
    {
        VertexData a = MakeVertex(sourceVertices, sourceNormals, sourceTangents, sourceColors, sourceUV0, sourceUV1, ia);
        VertexData b = MakeVertex(sourceVertices, sourceNormals, sourceTangents, sourceColors, sourceUV0, sourceUV1, ib);
        VertexData c = MakeVertex(sourceVertices, sourceNormals, sourceTangents, sourceColors, sourceUV0, sourceUV1, ic);
        AddTriangle(submesh, a, b, c);
    }

    private VertexData MakeVertex(
        Vector3[] v,
        Vector3[] n,
        Vector4[] t,
        Color[] c,
        Vector2[] u0,
        Vector2[] u1,
        int index)
    {
        return new VertexData(
            v[index],
            hasNormals ? n[index] : Vector3.zero,
            hasTangents ? t[index] : Vector4.zero,
            hasColors ? c[index] : Color.white,
            hasUV0 ? u0[index] : Vector2.zero,
            hasUV1 ? u1[index] : Vector2.zero,
            Vector2.zero // MaskPoint
        );
    }

    private Vertex Convert(VertexData data)
    {
        return new Vertex
        {
            Position = data.Position,
            Normal = data.Normal,
            Tangent = data.Tangent,
            Color = data.Color,
            UV0 = data.UV0,
            UV1 = data.UV1
        };
    }

    private void AddTriangleInternal(int submesh, Vertex a, Vertex b, Vertex c)
    {
        if (submesh < 0 || submesh >= submeshTriangles.Count)
            return;

        int start = vertices.Count;
        Append(a);
        Append(b);
        Append(c);

        List<int> indices = submeshTriangles[submesh];
        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 2);
    }

    private void Append(Vertex v)
    {
        vertices.Add(v.Position);
        if (hasNormals) normals.Add(v.Normal);
        if (hasTangents) tangents.Add(v.Tangent);
        if (hasColors) colors.Add(v.Color);
        if (hasUV0) uv0.Add(v.UV0);
        if (hasUV1) uv1.Add(v.UV1);
    }

    public Mesh Build(string meshName)
    {
        int indexCount = 0;
        for (int i = 0; i < submeshTriangles.Count; i++)
            indexCount += submeshTriangles[i].Count;

        if (indexCount == 0)
            return null;

        Mesh mesh = new Mesh();
        mesh.name = meshName;

        if (vertices.Count > 65535)
            mesh.indexFormat = IndexFormat.UInt32;

        mesh.SetVertices(vertices);

        if (hasNormals) mesh.SetNormals(normals);
        if (hasTangents) mesh.SetTangents(tangents);
        if (hasColors) mesh.SetColors(colors);
        if (hasUV0) mesh.SetUVs(0, uv0);
        if (hasUV1) mesh.SetUVs(1, uv1);

        mesh.subMeshCount = submeshTriangles.Count;
        for (int i = 0; i < submeshTriangles.Count; i++)
            mesh.SetTriangles(submeshTriangles[i], i, false);

        if (!hasNormals)
            mesh.RecalculateNormals();

        mesh.RecalculateBounds();
        mesh.MarkDynamic();
        return mesh;
    }

    public struct VertexData
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Tangent;
        public Color Color;
        public Vector2 UV0;
        public Vector2 UV1;
        public Vector2 MaskPoint;

        public VertexData(
            Vector3 position,
            Vector3 normal,
            Vector4 tangent,
            Color color,
            Vector2 uv0,
            Vector2 uv1,
            Vector2 maskPoint)
        {
            Position = position;
            Normal = normal;
            Tangent = tangent;
            Color = color;
            UV0 = uv0;
            UV1 = uv1;
            MaskPoint = maskPoint;
        }

        public static VertexData Lerp(
            VertexData a,
            VertexData b,
            float t)
        {
            Vector3 normal = Vector3.LerpUnclamped(
                a.Normal,
                b.Normal,
                t
            );

            if (normal.sqrMagnitude > 0.000001f)
                normal.Normalize();

            Vector4 tangent = Vector4.LerpUnclamped(
                a.Tangent,
                b.Tangent,
                t
            );

            Vector3 tangentDirection = new Vector3(
                tangent.x,
                tangent.y,
                tangent.z
            );

            if (tangentDirection.sqrMagnitude > 0.000001f)
            {
                tangentDirection.Normalize();

                tangent.x = tangentDirection.x;
                tangent.y = tangentDirection.y;
                tangent.z = tangentDirection.z;
            }

            tangent.w = tangent.w >= 0f ? 1f : -1f;

            return new VertexData(
                Vector3.LerpUnclamped(a.Position, b.Position, t),
                normal,
                tangent,
                Color.LerpUnclamped(a.Color, b.Color, t),
                Vector2.LerpUnclamped(a.UV0, b.UV0, t),
                Vector2.LerpUnclamped(a.UV1, b.UV1, t),
                Vector2.LerpUnclamped(a.MaskPoint, b.MaskPoint, t)
            );
        }
    }

}
