using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class MeshSurfacePatcher
{
    private struct PointKey : IEquatable<PointKey>
    {
        public long x, y, z;
        public PointKey(Vector3 p, float tolerance)
        {
            x = (long)Math.Round(p.x / tolerance);
            y = (long)Math.Round(p.y / tolerance);
            z = (long)Math.Round(p.z / tolerance);
        }
        public bool Equals(PointKey other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object obj) => obj is PointKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { int h = 17; h = h * 31 + x.GetHashCode(); h = h * 31 + y.GetHashCode(); return h * 31 + z.GetHashCode(); }
        }
    }

    private struct EdgeKey : IEquatable<EdgeKey>
    {
        public PointKey a, b;
        public EdgeKey(PointKey p, PointKey q)
        {
            if (Compare(p, q) <= 0) { a = p; b = q; }
            else { a = q; b = p; }
        }
        private static int Compare(PointKey p, PointKey q)
        {
            int c = p.x.CompareTo(q.x); if (c != 0) return c;
            c = p.y.CompareTo(q.y); if (c != 0) return c;
            return p.z.CompareTo(q.z);
        }
        public bool Equals(EdgeKey other) => a.Equals(other.a) && b.Equals(other.b);
        public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
        public override int GetHashCode() { unchecked { return a.GetHashCode() * 397 ^ b.GetHashCode(); } }
    }

    private sealed class EdgeRecord
    {
        public PointKey from, to;
        public int fromIndex, toIndex, submesh;
        public int count;
        public bool used;
    }


    public static Mesh FillClosedHoles(
        Mesh source,
        out List<FillRegion> regions,
        float weldTolerance = 0.0001f)
    {
        regions = new List<FillRegion>();

        if (source == null || !source.isReadable || weldTolerance <= 0f)
            return null;

        Vector3[] vertices = source.vertices;
        Vector2[] uv = source.uv;

        var edgeMap = new Dictionary<EdgeKey, EdgeRecord>();

        for (int s = 0; s < source.subMeshCount; s++)
        {
            if (source.GetTopology(s) != MeshTopology.Triangles)
                continue;

            int[] t = source.GetTriangles(s);

            for (int i = 0; i < t.Length; i += 3)
            {
                AddEdge(t[i], t[i + 1], s);
                AddEdge(t[i + 1], t[i + 2], s);
                AddEdge(t[i + 2], t[i], s);
            }
        }

        void AddEdge(int ia, int ib, int submesh)
        {
            PointKey a = new PointKey(vertices[ia], weldTolerance);
            PointKey b = new PointKey(vertices[ib], weldTolerance);

            if (a.Equals(b))
                return;

            EdgeKey key = new EdgeKey(a, b);

            if (!edgeMap.TryGetValue(key, out EdgeRecord record))
            {
                record = new EdgeRecord
                {
                    from = a,
                    to = b,
                    fromIndex = ia,
                    toIndex = ib,
                    submesh = submesh
                };

                edgeMap.Add(key, record);
            }

            record.count++;
        }

        var boundary = new List<EdgeRecord>();

        foreach (EdgeRecord e in edgeMap.Values)
        {
            if (e.count == 1)
                boundary.Add(e);
        }

        if (boundary.Count < 3)
            return null;

        var adjacency = new Dictionary<PointKey, List<EdgeRecord>>();

        foreach (EdgeRecord e in boundary)
        {
            AddAdjacent(e.from, e);
            AddAdjacent(e.to, e);
        }

        void AddAdjacent(PointKey p, EdgeRecord e)
        {
            if (!adjacency.TryGetValue(p, out List<EdgeRecord> list))
            {
                list = new List<EdgeRecord>();
                adjacency[p] = list;
            }

            list.Add(e);
        }

        foreach (EdgeRecord firstEdge in boundary)
        {
            if (firstEdge.used)
                continue;

            PointKey start = firstEdge.from;
            PointKey current = start;

            EdgeRecord edge = firstEdge;

            var loopKeys = new List<PointKey>();
            var loopIndices = new List<int>();

            bool closed = false;
            int guard = boundary.Count + 1;

            while (guard-- > 0)
            {
                if (edge.used)
                    break;

                if (!adjacency.TryGetValue(current, out List<EdgeRecord> around)
                    || around.Count != 2)
                    break;

                edge.used = true;

                loopKeys.Add(current);

                bool forward = edge.from.Equals(current);

                loopIndices.Add(
                    forward ? edge.fromIndex : edge.toIndex
                );

                PointKey next = forward ? edge.to : edge.from;

                if (next.Equals(start))
                {
                    closed = true;
                    break;
                }

                EdgeRecord nextEdge = null;

                foreach (EdgeRecord candidate in adjacency[next])
                {
                    if (!candidate.used)
                    {
                        nextEdge = candidate;
                        break;
                    }
                }

                if (nextEdge == null)
                    break;

                current = next;
                edge = nextEdge;
            }

            if (!closed || loopIndices.Count < 3)
                continue;

            if (firstEdge.from.Equals(loopKeys[0]) &&
                firstEdge.to.Equals(loopKeys[1]))
            {
                loopKeys.Reverse();
                loopIndices.Reverse();
            }

            int[] votes = new int[source.subMeshCount];

            var loopSet = new HashSet<PointKey>(loopKeys);

            foreach (EdgeRecord e in boundary)
            {
                if (loopSet.Contains(e.from) &&
                    loopSet.Contains(e.to))
                {
                    votes[e.submesh]++;
                }
            }

            int targetSubmesh = 0;

            for (int s = 1; s < votes.Length; s++)
            {
                if (votes[s] > votes[targetSubmesh])
                    targetSubmesh = s;
            }

            if (!Triangulate(
                loopIndices,
                vertices,
                out List<int> patchTriangles))
            {
                continue;
            }

            FillRegion region = new FillRegion
            {
                regionId = regions.Count,
                submeshIndex = targetSubmesh,
                triangleIndices = patchTriangles,
                isVisible = true
            };

            regions.Add(region);
        }

        if (regions.Count == 0)
            return null;

        return BuildMeshWithRegions(source, regions);
    }

    public static Mesh BuildMeshWithRegions(
        Mesh baseMesh,
        List<FillRegion> regions)
    {
        if (baseMesh == null || regions == null)
            return null;

        Mesh result = new Mesh
        {
            name = baseMesh.name + "_SurfacePatched"
        };

        if (baseMesh.vertexCount > 65535)
            result.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        result.vertices = baseMesh.vertices;

        if (baseMesh.uv != null &&
            baseMesh.uv.Length == baseMesh.vertexCount)
        {
            result.uv = baseMesh.uv;
        }

        result.subMeshCount = baseMesh.subMeshCount;

        for (int s = 0; s < baseMesh.subMeshCount; s++)
        {
            var combined = new List<int>();

            if (baseMesh.GetTopology(s) == MeshTopology.Triangles)
                combined.AddRange(baseMesh.GetTriangles(s));

            foreach (FillRegion region in regions)
            {
                if (!region.isVisible ||
                    region.submeshIndex != s)
                    continue;

                combined.AddRange(region.triangleIndices);
            }

            result.SetTriangles(combined, s, false);
        }

        result.RecalculateNormals();
        result.RecalculateBounds();

        return result;
    }


    private static bool Triangulate(List<int> loop, Vector3[] vertices, out List<int> triangles)
    {
        triangles = new List<int>();
        if (loop.Count < 3) return false;

        Vector3 n = Vector3.zero;
        for (int i = 0; i < loop.Count; i++)
        {
            Vector3 a = vertices[loop[i]], b = vertices[loop[(i + 1) % loop.Count]];
            n.x += (a.y - b.y) * (a.z + b.z);
            n.y += (a.z - b.z) * (a.x + b.x);
            n.z += (a.x - b.x) * (a.y + b.y);
        }
        if (n.sqrMagnitude < 1e-12f) return false;
        n.Normalize();

        Vector3 center = Vector3.zero;
        foreach (int index in loop) center += vertices[index];
        center /= loop.Count;

        // A strongly folded boundary cannot be safely filled by one projected polygon.
        float scale = 0f, maxDeviation = 0f;
        foreach (int index in loop)
        {
            Vector3 p = vertices[index];
            scale = Mathf.Max(scale, Vector3.Distance(center, p));
            maxDeviation = Mathf.Max(maxDeviation, Mathf.Abs(Vector3.Dot(p - center, n)));
        }
        if (scale > 0f && maxDeviation > scale * 0.35f) return false;

        Vector3 axis = Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
        Vector3 right = Vector3.Cross(axis, n).normalized;
        Vector3 up = Vector3.Cross(n, right).normalized;
        var p2 = new List<Vector2>();
        foreach (int index in loop)
        {
            Vector3 d = vertices[index] - center;
            p2.Add(new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up)));
        }

        float area = 0f;
        for (int i = 0; i < p2.Count; i++)
        {
            Vector2 a = p2[i], b = p2[(i + 1) % p2.Count];
            area += a.x * b.y - b.x * a.y;
        }
        if (Mathf.Abs(area) < 1e-10f) return false;
        float sign = Mathf.Sign(area);

        var remaining = new List<int>();
        for (int i = 0; i < loop.Count; i++) remaining.Add(i);
        int guard = loop.Count * loop.Count;

        while (remaining.Count > 3 && guard-- > 0)
        {
            bool found = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int prev = remaining[(i - 1 + remaining.Count) % remaining.Count];
                int curr = remaining[i];
                int next = remaining[(i + 1) % remaining.Count];
                Vector2 a = p2[prev], b = p2[curr], c = p2[next];
                if (Cross(b - a, c - b) * sign <= 1e-8f) continue;

                bool contains = false;
                foreach (int test in remaining)
                {
                    if (test == prev || test == curr || test == next) continue;
                    if (InsideTriangle(p2[test], a, b, c, sign)) { contains = true; break; }
                }
                if (contains) continue;

                triangles.Add(loop[prev]); triangles.Add(loop[curr]); triangles.Add(loop[next]);
                remaining.RemoveAt(i);
                found = true;
                break;
            }
            if (!found) { triangles.Clear(); return false; }
        }

        if (remaining.Count == 3)
        {
            triangles.Add(loop[remaining[0]]);
            triangles.Add(loop[remaining[1]]);
            triangles.Add(loop[remaining[2]]);
        }
        return triangles.Count >= 3;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static bool InsideTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c, float sign)
    {
        const float e = 1e-7f;
        return Cross(b - a, p - a) * sign >= -e &&
               Cross(c - b, p - b) * sign >= -e &&
               Cross(a - c, p - c) * sign >= -e;
    }
    public static void SetRegionVisible(
    Mesh mesh,
    List<FillRegion> regions,
    int regionId,
    bool visible)
    {
        FillRegion region = regions.Find(
            r => r.regionId == regionId
        );

        if (region == null)
            return;

        region.isVisible = visible;
    }
}

[Serializable]
public class FillRegion
{
    public int regionId;

    public int submeshIndex;

    public List<int> triangleIndices = new List<int>();

    public bool isVisible = true;
}
