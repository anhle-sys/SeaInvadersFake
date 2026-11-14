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

        // Index of the source vertex this data came from, or -1 for a generated vertex.
        public int index;

        public VertexData(Vector3 position, Vector2 uv, Vector2 maskUV = default, Vector3 normal = default, int index = -1)
        {
            this.position = position;
            this.uv = uv;
            this.maskUV = maskUV;
            this.normal = normal;
            this.index = index;
        }

        public static VertexData Lerp(
            in VertexData a,
            in VertexData b,
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

    // Quantized vertex identity used to weld generated vertices.
    private struct WeldKey : System.IEquatable<WeldKey>
    {
        private readonly int px, py, pz, u, v, nx, ny, nz;

        public WeldKey(Vector3 p, Vector2 uv, Vector3 n, float invPosStep)
        {
            px = Mathf.RoundToInt(p.x * invPosStep);
            py = Mathf.RoundToInt(p.y * invPosStep);
            pz = Mathf.RoundToInt(p.z * invPosStep);
            u = Mathf.RoundToInt(uv.x * 100000f);
            v = Mathf.RoundToInt(uv.y * 100000f);
            nx = Mathf.RoundToInt(n.x * 1000f);
            ny = Mathf.RoundToInt(n.y * 1000f);
            nz = Mathf.RoundToInt(n.z * 1000f);
        }

        public bool Equals(WeldKey o) =>
            px == o.px && py == o.py && pz == o.pz && u == o.u && v == o.v &&
            nx == o.nx && ny == o.ny && nz == o.nz;

        public override bool Equals(object obj) => obj is WeldKey o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = px;
                h = h * 73856093 ^ py;
                h = h * 19349663 ^ pz;
                h = h * 83492791 ^ u;
                h = h * 31 ^ v;
                h = h * 31 ^ nx;
                h = h * 31 ^ ny;
                return h * 31 ^ nz;
            }
        }
    }

    private struct PosKey : System.IEquatable<PosKey>
    {
        private readonly int x, y, z;

        public PosKey(Vector3 p, float invStep)
        {
            x = Mathf.RoundToInt(p.x * invStep);
            y = Mathf.RoundToInt(p.y * invStep);
            z = Mathf.RoundToInt(p.z * invStep);
        }

        public bool Equals(PosKey o) => x == o.x && y == o.y && z == o.z;
        public override bool Equals(object obj) => obj is PosKey o && Equals(o);
        public override int GetHashCode() { unchecked { return (x * 73856093) ^ (y * 19349663) ^ (z * 83492791); } }
    }

    public sealed class CutContext
    {
        public HashSet<long> SourceBoundary;

        public int[] SourceRemap;

        public bool[] SourceTouched;

        public bool[] ResultTouched;

        public List<MeshBoundaryEdge> RegionBoundary;

        public static long Key(int from, int to) => ((long)from << 32) | (uint)to;
    }

    private class MeshBuilder
    {
        private readonly List<Vector3> vertices;
        public CutContext context;
        private readonly List<Vector2> uvs;

        private readonly List<Vector3> normals;

        // Indexed mode: mask-space position of every vertex (used to find debris).
        private readonly List<Vector2> maskUVs;

        // Source vertices of triangles this cut removed or split.
        private bool[] touchedSource;

        public void MarkTouched(int i0, int i1, int i2)
        {
            touchedSource ??= new bool[sourceVertexCount];
            touchedSource[i0] = touchedSource[i1] = touchedSource[i2] = true;
        }

        private readonly List<List<int>> submeshTriangles = new();

        private readonly bool indexed;
        private readonly int sourceVertexCount;

        // Indexed mode: generated vertices are welded so neighbouring subdivided
        // triangles share vertices instead of each adding 3 unique ones.
        private readonly float positionStep;
        private readonly float invPositionStep;
        private readonly Dictionary<WeldKey, int> weldMap;

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
            Vector3[] sourceNormals,
            Vector2[] sourceMaskUV,
            float positionStep)
        {
            indexed = true;
            sourceVertexCount = sourceVertices.Length;
            maskUVs = new List<Vector2>(sourceVertexCount + sourceVertexCount / 8 + 64);
            maskUVs.AddRange(sourceMaskUV);
            this.positionStep = positionStep;
            invPositionStep = 1f / positionStep;
            weldMap = new Dictionary<WeldKey, int>(1024);

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
            in VertexData a,
            in VertexData b,
            in VertexData c)
        {
            if (indexed)
            {
                int i0 = GetOrAddVertex(a);
                int i1 = GetOrAddVertex(b);
                int i2 = GetOrAddVertex(c);

                if (i0 == i1 || i1 == i2 || i2 == i0)
                    return;

                List<int> list = submeshTriangles[submeshIndex];
                list.Add(i0);
                list.Add(i1);
                list.Add(i2);
                return;
            }

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

            List<MeshBoundaryEdge> regionBoundary = null;

            if (indexed && (vertices.Count > sourceVertexCount || touchedSource != null))
            {
                // Without a context the debris pass needs ids for every vertex.
                bool fullIds = context == null || context.SourceBoundary == null;
                ComputePositionIds(fullIds);
                ExtractRegionTriangles();
                RemoveDegenerateTriangles();
                RepairTJunctions();
                RemoveDegenerateTriangles();
                RemoveCutDebris();
                if (context != null)
                    regionBoundary = CollectRegionBoundary();
                MergeRegionTriangles();
            }

            if (indexed)
            {
                NormalizeAppendedNormals();
                CompactUnusedVertices();

                if (context != null)
                    FillContextOutputs(regionBoundary);
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

        // Zero-area triangles (collinear vertices) are invisible but their edges
        // overlap real edges, which confuses edge matching and creates
        // non-manifold edges. Dropping them leaves plain T-junctions, which
        // RepairTJunctions then closes properly.
        // ---- Cut region ---------------------------------------------------
        // Only triangles touching the cut can change, so the cleanup passes
        // below work on those instead of the whole mesh (70k+ triangles).
        // Position ids weld UV/normal seams; a position is "touched" when any
        // vertex there is new or belonged to a triangle this cut removed/split.
        private readonly List<int> posId = new List<int>();
        private readonly List<int> posRep = new List<int>();
        private readonly List<bool> posTouched = new List<bool>();
        private Dictionary<PosKey, int> posMap;

        // Triangles of the cut region, moved out of submeshTriangles so the
        // cleanup passes never iterate the (possibly huge) untouched rest.
        private readonly List<List<int>> regionTriangles = new List<List<int>>();

        private int GetOrAddPosId(int i, bool touched)
        {
            PosKey key = new PosKey(vertices[i], invPositionStep);
            if (!posMap.TryGetValue(key, out int id))
            {
                id = posRep.Count;
                posMap.Add(key, id);
                posRep.Add(i);
                posTouched.Add(false);
            }
            if (touched)
                posTouched[id] = true;
            return id;
        }

        // Ids only for what the cut can affect: touched vertices (new, or of a
        // removed/split triangle) and untouched vertices at the same position
        // (UV/normal seams). Everything else stays -1 unless fullIds.
        private void ComputePositionIds(bool fullIds)
        {
            int vertexCount = vertices.Count;
            posId.Clear(); posRep.Clear(); posTouched.Clear();
            posMap = new Dictionary<PosKey, int>(fullIds ? vertexCount : 4096);

            for (int i = 0; i < vertexCount; i++)
                posId.Add(-1);

            for (int i = 0; i < vertexCount; i++)
            {
                if (i >= sourceVertexCount || (touchedSource != null && touchedSource[i]))
                    posId[i] = GetOrAddPosId(i, true);
            }

            for (int i = 0; i < sourceVertexCount; i++)
            {
                if (posId[i] >= 0)
                    continue;

                if (fullIds)
                {
                    posId[i] = GetOrAddPosId(i, false);
                }
                else if (posMap.TryGetValue(new PosKey(vertices[i], invPositionStep), out int id))
                {
                    posId[i] = id;
                }
            }
        }

        private bool IsTouched(int i)
        {
            int p = posId[i];
            return p >= 0 && posTouched[p];
        }

        private bool IsRegionTriangle(List<int> list, int t)
        {
            return IsTouched(list[t]) || IsTouched(list[t + 1]) || IsTouched(list[t + 2]);
        }

        // Edge on the border of the cut region: shared with untouched geometry
        // (or an old hole edge).
        private bool IsRegionBorderEdge(int ia, int ib)
        {
            return !IsTouched(ia) && !IsTouched(ib);
        }

        private void ExtractRegionTriangles()
        {
            regionTriangles.Clear();

            foreach (List<int> list in submeshTriangles)
            {
                var region = new List<int>();
                int write = 0;

                for (int t = 0; t < list.Count; t += 3)
                {
                    int i0 = list[t], i1 = list[t + 1], i2 = list[t + 2];

                    if (IsRegionTriangle(list, t))
                    {
                        region.Add(i0); region.Add(i1); region.Add(i2);
                        continue;
                    }

                    list[write++] = i0;
                    list[write++] = i1;
                    list[write++] = i2;
                }

                if (write < list.Count)
                    list.RemoveRange(write, list.Count - write);

                regionTriangles.Add(region);
            }

            // Every vertex of a region triangle needs an id for edge matching.
            foreach (List<int> region in regionTriangles)
            {
                for (int k = 0; k < region.Count; k++)
                {
                    int i = region[k];
                    if (posId[i] < 0)
                        posId[i] = GetOrAddPosId(i, false);
                }
            }
        }

        private void MergeRegionTriangles()
        {
            for (int s = 0; s < submeshTriangles.Count; s++)
                submeshTriangles[s].AddRange(regionTriangles[s]);
            regionTriangles.Clear();
        }

        // Directed boundary edges of the region (hole outlines touched by this
        // cut). A region-border edge (both ends untouched) is a boundary edge
        // only if it already was one in the source mesh (an old hole edge).
        private List<MeshBoundaryEdge> CollectRegionBoundary()
        {
            var count = new Dictionary<long, int>(4096, MixedLongComparer.Instance);

            foreach (List<int> list in regionTriangles)
            {
                for (int t = 0; t < list.Count; t += 3)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int a = posId[list[t + e]], b = posId[list[t + (e + 1) % 3]];
                        if (a == b) continue;
                        long key = UndirectedEdgeKey(a, b);
                        count.TryGetValue(key, out int c);
                        count[key] = c + 1;
                    }
                }
            }

            var result = new List<MeshBoundaryEdge>();

            for (int s = 0; s < regionTriangles.Count; s++)
            {
                List<int> list = regionTriangles[s];

                for (int t = 0; t < list.Count; t += 3)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int ia = list[t + e], ib = list[t + (e + 1) % 3];
                        int a = posId[ia], b = posId[ib];
                        if (a == b || count[UndirectedEdgeKey(a, b)] != 1) continue;

                        if (IsRegionBorderEdge(ia, ib) &&
                            !context.SourceBoundary.Contains(CutContext.Key(ia, ib)))
                            continue;

                        result.Add(new MeshBoundaryEdge
                        {
                            from = ia,
                            to = ib,
                            third = list[t + (e + 2) % 3],
                            submesh = s
                        });
                    }
                }
            }

            return result;
        }

        private void FillContextOutputs(List<MeshBoundaryEdge> regionBoundary)
        {
            int[] remap = compactRemap;
            context.SourceRemap = new int[sourceVertexCount];
            context.SourceTouched = new bool[sourceVertexCount];
            context.ResultTouched = new bool[vertices.Count];

            bool haveIds = posId.Count > 0;

            for (int i = 0; i < remap.Length; i++)
            {
                bool touched = i >= sourceVertexCount || (haveIds && IsTouched(i));

                if (i < sourceVertexCount)
                {
                    context.SourceRemap[i] = remap[i];
                    context.SourceTouched[i] = touched;
                }

                if (remap[i] >= 0 && touched)
                    context.ResultTouched[remap[i]] = true;
            }

            context.RegionBoundary = new List<MeshBoundaryEdge>();

            if (regionBoundary != null)
            {
                foreach (MeshBoundaryEdge e in regionBoundary)
                {
                    int f = remap[e.from], to = remap[e.to], th = remap[e.third];
                    if (f < 0 || to < 0 || th < 0) continue;
                    context.RegionBoundary.Add(new MeshBoundaryEdge { from = f, to = to, third = th, submesh = e.submesh });
                }
            }
        }

        private void RemoveDegenerateTriangles()
        {
            float minAbs = positionStep * positionStep;

            foreach (List<int> list in regionTriangles)
            {
                int write = 0;

                for (int t = 0; t < list.Count; t += 3)
                {
                    int i0 = list[t], i1 = list[t + 1], i2 = list[t + 2];

                    if (!IsRegionTriangle(list, t))
                    {
                        list[write++] = i0;
                        list[write++] = i1;
                        list[write++] = i2;
                        continue;
                    }

                    Vector3 a = vertices[i0];
                    Vector3 e1 = vertices[i1] - a;
                    Vector3 e2 = vertices[i2] - a;
                    float crossMag = Vector3.Cross(e1, e2).magnitude;
                    float maxEdgeSq = Mathf.Max(e1.sqrMagnitude, Mathf.Max(e2.sqrMagnitude, (e2 - e1).sqrMagnitude));

                    if (crossMag <= minAbs || crossMag <= maxEdgeSq * 1e-5f)
                        continue;

                    list[write++] = i0;
                    list[write++] = i1;
                    list[write++] = i2;
                }

                if (write < list.Count)
                    list.RemoveRange(write, list.Count - write);
            }
        }

        private int GetOrAddVertex(in VertexData d)
        {
            if (d.index >= 0)
                return d.index;

            WeldKey key = new WeldKey(
                d.position, d.uv, normals != null ? d.normal : Vector3.zero, invPositionStep);

            if (weldMap.TryGetValue(key, out int existing))
                return existing;

            int index = vertices.Count;
            vertices.Add(d.position);
            uvs.Add(d.uv);
            normals?.Add(d.normal);
            maskUVs.Add(d.maskUV);
            weldMap.Add(key, index);
            return index;
        }

        private struct EdgeSplit
        {
            public int slot;
            public float t;
            public int pid;
        }

        private static long UndirectedEdgeKey(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        private static long CellKey(int x, int y, int z)
        {
            return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
        }

        // Adaptive subdivision leaves T-junctions where a large kept triangle
        // borders smaller ones. Those show up as open edges, which breaks hole
        // detection in MeshSurfacePatcher and makes later cuts slower. This pass
        // finds boundary vertices lying on boundary edges and splits the owning
        // triangles so the result is watertight again (except the real cut holes).
        private void RepairTJunctions()
        {
            int vertexCount = vertices.Count;
            if (vertexCount == sourceVertexCount)
                return;

            // 1. Position ids (computed once in Build).
            List<int> pid = posId;
            List<int> rep = posRep;

            // 2. Undirected edge use count, cut region only.
            var edgeCount = new Dictionary<long, int>(4096, MixedLongComparer.Instance);

            foreach (List<int> list in regionTriangles)
            {
                for (int t = 0; t < list.Count; t += 3)
                {
                    if (!IsRegionTriangle(list, t))
                        continue;

                    for (int e = 0; e < 3; e++)
                    {
                        int a = pid[list[t + e]];
                        int b = pid[list[t + (e + 1) % 3]];
                        if (a == b)
                            continue;

                        long key = UndirectedEdgeKey(a, b);
                        edgeCount.TryGetValue(key, out int c);
                        edgeCount[key] = c + 1;
                    }
                }
            }

            // 3. Boundary edges and boundary vertices.
            var boundarySub = new List<int>();
            var boundaryTri = new List<int>();
            var boundarySlot = new List<int>();
            bool[] boundaryPid = new bool[rep.Count];
            float totalLength = 0f;

            for (int s = 0; s < regionTriangles.Count; s++)
            {
                List<int> list = regionTriangles[s];

                for (int t = 0; t < list.Count; t += 3)
                {
                    if (!IsRegionTriangle(list, t))
                        continue;

                    for (int e = 0; e < 3; e++)
                    {
                        int ia = list[t + e];
                        int ib = list[t + (e + 1) % 3];
                        int a = pid[ia];
                        int b = pid[ib];
                        if (a == b || edgeCount[UndirectedEdgeKey(a, b)] != 1)
                            continue;

                        // Shared with untouched geometry outside the region: not open.
                        if (IsRegionBorderEdge(ia, ib))
                            continue;

                        boundarySub.Add(s);
                        boundaryTri.Add(t);
                        boundarySlot.Add(e);
                        boundaryPid[a] = true;
                        boundaryPid[b] = true;
                        totalLength += Vector3.Distance(vertices[ia], vertices[ib]);
                    }
                }
            }

            if (boundaryTri.Count == 0)
                return;

            // 4. Spatial hash of boundary vertices.
            // Cell size: average boundary edge, but never so small that the
            // longest edge has to walk more than ~4096 cells.
            float longestEdge = 0f;
            for (int r = 0; r < boundaryTri.Count; r++)
            {
                List<int> l = regionTriangles[boundarySub[r]];
                int t0 = boundaryTri[r], sl = boundarySlot[r];
                longestEdge = Mathf.Max(longestEdge,
                    Vector3.Distance(vertices[l[t0 + sl]], vertices[l[t0 + (sl + 1) % 3]]));
            }

            float cellSize = Mathf.Max(totalLength / boundaryTri.Count, positionStep * 16f, longestEdge / 4096f);
            float invCell = 1f / cellSize;
            var grid = new Dictionary<long, List<int>>(MixedLongComparer.Instance);

            for (int p = 0; p < rep.Count; p++)
            {
                if (!boundaryPid[p])
                    continue;

                Vector3 v = vertices[rep[p]] * invCell;
                long key = CellKey(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y), Mathf.FloorToInt(v.z));

                if (!grid.TryGetValue(key, out List<int> cell))
                {
                    cell = new List<int>(4);
                    grid.Add(key, cell);
                }
                cell.Add(p);
            }

            // 5. Find T-vertices lying on each boundary edge (3D DDA through the grid).
            var splitsByTriangle = new Dictionary<long, List<EdgeSplit>>(MixedLongComparer.Instance);

            for (int r = 0; r < boundaryTri.Count; r++)
            {
                int s = boundarySub[r];
                int t = boundaryTri[r];
                int slot = boundarySlot[r];
                List<int> list = regionTriangles[s];

                int ia = list[t + slot];
                int ib = list[t + (slot + 1) % 3];
                int pa = pid[ia];
                int pb = pid[ib];

                Vector3 A = vertices[ia];
                Vector3 AB = vertices[ib] - A;
                float len2 = AB.sqrMagnitude;
                if (len2 < positionStep * positionStep)
                    continue;

                float len = Mathf.Sqrt(len2);
                float tol = Mathf.Max(positionStep, len * 1e-6f);
                float tol2 = tol * tol;
                float invLen2 = 1f / len2;
                // Parameter margin so a split point is never (almost) on an end point.
                float tMargin = Mathf.Max(1e-5f, tol * 2f / len);

                Vector3 ga = A * invCell;
                Vector3 gd = AB * invCell;

                int x = Mathf.FloorToInt(ga.x), y = Mathf.FloorToInt(ga.y), z = Mathf.FloorToInt(ga.z);
                Vector3 gb = ga + gd;
                int ex = Mathf.FloorToInt(gb.x), ey = Mathf.FloorToInt(gb.y), ez = Mathf.FloorToInt(gb.z);

                int sx = gd.x > 0f ? 1 : (gd.x < 0f ? -1 : 0);
                int sy = gd.y > 0f ? 1 : (gd.y < 0f ? -1 : 0);
                int sz = gd.z > 0f ? 1 : (gd.z < 0f ? -1 : 0);

                float tdx = sx != 0 ? 1f / Mathf.Abs(gd.x) : float.PositiveInfinity;
                float tdy = sy != 0 ? 1f / Mathf.Abs(gd.y) : float.PositiveInfinity;
                float tdz = sz != 0 ? 1f / Mathf.Abs(gd.z) : float.PositiveInfinity;

                float tmx = sx > 0 ? (x + 1 - ga.x) * tdx : (sx < 0 ? (ga.x - x) * tdx : float.PositiveInfinity);
                float tmy = sy > 0 ? (y + 1 - ga.y) * tdy : (sy < 0 ? (ga.y - y) * tdy : float.PositiveInfinity);
                float tmz = sz > 0 ? (z + 1 - ga.z) * tdz : (sz < 0 ? (ga.z - z) * tdz : float.PositiveInfinity);

                int maxSteps = Mathf.Abs(ex - x) + Mathf.Abs(ey - y) + Mathf.Abs(ez - z) + 1;
                List<EdgeSplit> found = null;

                for (int step = 0; step <= maxSteps; step++)
                {
                    if (grid.TryGetValue(CellKey(x, y, z), out List<int> cell))
                    {
                        for (int c = 0; c < cell.Count; c++)
                        {
                            int q = cell[c];
                            if (q == pa || q == pb)
                                continue;

                            Vector3 AP = vertices[rep[q]] - A;
                            float tp = Vector3.Dot(AP, AB) * invLen2;
                            if (tp <= tMargin || tp >= 1f - tMargin)
                                continue;

                            if ((AP - AB * tp).sqrMagnitude > tol2)
                                continue;

                            found ??= new List<EdgeSplit>();
                            found.Add(new EdgeSplit { slot = slot, t = tp, pid = q });
                        }
                    }

                    if (x == ex && y == ey && z == ez)
                        break;

                    if (tmx <= tmy && tmx <= tmz)
                    {
                        if (tmx > 1f) break;
                        x += sx; tmx += tdx;
                    }
                    else if (tmy <= tmz)
                    {
                        if (tmy > 1f) break;
                        y += sy; tmy += tdy;
                    }
                    else
                    {
                        if (tmz > 1f) break;
                        z += sz; tmz += tdz;
                    }
                }

                if (found == null)
                    continue;

                long triKey = ((long)s << 40) | (long)t;
                if (!splitsByTriangle.TryGetValue(triKey, out List<EdgeSplit> splits))
                {
                    splits = new List<EdgeSplit>();
                    splitsByTriangle.Add(triKey, splits);
                }
                splits.AddRange(found);
            }

            if (splitsByTriangle.Count == 0)
                return;

            // 6. Re-triangulate every triangle that owns T-vertices.
            var polygon = new List<int>(16);
            var edgePoints = new List<EdgeSplit>(8);

            foreach (KeyValuePair<long, List<EdgeSplit>> pair in splitsByTriangle)
            {
                int s = (int)(pair.Key >> 40);
                int t = (int)(pair.Key & 0xFFFFFFFFFFL);
                List<int> list = regionTriangles[s];
                List<EdgeSplit> splits = pair.Value;

                int v0 = list[t], v1 = list[t + 1], v2 = list[t + 2];
                int[] corners = { v0, v1, v2 };

                splits.Sort((p, q) => p.slot != q.slot ? p.slot.CompareTo(q.slot) : p.t.CompareTo(q.t));

                polygon.Clear();
                int usedSlots = 0;
                int lastSlot = -1;

                for (int slot = 0; slot < 3; slot++)
                {
                    polygon.Add(corners[slot]);
                    edgePoints.Clear();

                    foreach (EdgeSplit sp in splits)
                    {
                        if (sp.slot == slot)
                            edgePoints.Add(sp);
                    }

                    if (edgePoints.Count == 0)
                        continue;

                    usedSlots++;
                    lastSlot = slot;

                    int from = corners[slot];
                    int to = corners[(slot + 1) % 3];
                    float lastT = -1f;

                    foreach (EdgeSplit sp in edgePoints)
                    {
                        if (sp.t - lastT < 1e-6f)
                            continue;
                        lastT = sp.t;
                        polygon.Add(MakeEdgeVertex(from, to, sp.t, rep[sp.pid]));
                    }
                }

                var newTriangles = new List<int>(polygon.Count * 3);

                if (usedSlots == 1)
                {
                    // Fan from the vertex opposite the split edge.
                    int opposite = corners[(lastSlot + 2) % 3];
                    int startIdx = polygon.IndexOf(corners[lastSlot]);
                    int endIdx = polygon.IndexOf(corners[(lastSlot + 1) % 3]);
                    if (endIdx == 0) endIdx = polygon.Count;

                    for (int j = startIdx; j < endIdx; j++)
                    {
                        newTriangles.Add(polygon[j]);
                        newTriangles.Add(polygon[(j + 1) % polygon.Count]);
                        newTriangles.Add(opposite);
                    }
                }
                else
                {
                    int centroid = AddCentroidVertex(v0, v1, v2);
                    for (int j = 0; j < polygon.Count; j++)
                    {
                        newTriangles.Add(polygon[j]);
                        newTriangles.Add(polygon[(j + 1) % polygon.Count]);
                        newTriangles.Add(centroid);
                    }
                }

                // Never replace a triangle with a fan that contains a flipped or
                // zero-area piece; leaving one T-junction is far less harmful.
                Vector3 originalNormal = Vector3.Cross(vertices[v1] - vertices[v0], vertices[v2] - vertices[v0]);
                bool valid = true;

                for (int j = 0; j < newTriangles.Count && valid; j += 3)
                {
                    Vector3 p0 = vertices[newTriangles[j]];
                    Vector3 n = Vector3.Cross(vertices[newTriangles[j + 1]] - p0, vertices[newTriangles[j + 2]] - p0);
                    valid = Vector3.Dot(n, originalNormal) > 0f && n.magnitude > positionStep * positionStep;
                }

                if (!valid)
                    continue;

                list[t] = newTriangles[0];
                list[t + 1] = newTriangles[1];
                list[t + 2] = newTriangles[2];

                for (int j = 3; j < newTriangles.Count; j++)
                    list.Add(newTriangles[j]);
            }
        }

        private int MakeEdgeVertex(int from, int to, float t, int existing)
        {
            Vector2 uv = Vector2.LerpUnclamped(uvs[from], uvs[to], t);
            Vector3 n = Vector3.zero;

            if (normals != null)
                n = Vector3.LerpUnclamped(normals[from].normalized, normals[to].normalized, t).normalized;

            bool sameUV = (uvs[existing] - uv).sqrMagnitude < 1e-10f;
            bool sameNormal = normals == null || Vector3.Dot(normals[existing].normalized, n) > 0.999f;

            if (sameUV && sameNormal)
                return existing;

            int index = vertices.Count;
            vertices.Add(vertices[existing]);
            uvs.Add(uv);
            normals?.Add(n);
            maskUVs.Add(maskUVs[existing]);
            posId.Add(posId[existing]);
            return index;
        }

        private int AddCentroidVertex(int a, int b, int c)
        {
            int index = vertices.Count;
            vertices.Add((vertices[a] + vertices[b] + vertices[c]) / 3f);
            uvs.Add((uvs[a] + uvs[b] + uvs[c]) / 3f);
            normals?.Add((normals[a].normalized + normals[b].normalized + normals[c].normalized) / 3f);
            maskUVs.Add((maskUVs[a] + maskUVs[b] + maskUVs[c]) / 3f);
            posId.Add(posRep.Count);
            posRep.Add(index);
            posTouched.Add(true);
            return index;
        }

        // Pieces left between the spikes of overlapping cuts end up as small
        // floating fragments inside the hole (and as hundreds of "islands" the
        // fill has to work around). A connected piece that contains geometry
        // generated by this cut and lies completely inside the mask area is not
        // attached to anything else, so it is removed. The main surface always
        // reaches outside the mask area and is never affected.
        private void RemoveCutDebris()
        {
            List<int> pid = posId;

            // Components are built over cut-region triangles connected through
            // a shared EDGE. Pieces touching the surface at a single vertex (very
            // common where the outline pinches) count as detached. A region
            // triangle sharing an edge with untouched geometry is attached.
            int triangleCount = 0;
            foreach (List<int> list in regionTriangles)
                triangleCount += list.Count / 3;

            int[] parent = new int[triangleCount];
            for (int i = 0; i < parent.Length; i++)
                parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            // A region-border edge (both ends untouched) attaches its triangle to
            // the rest of the mesh unless it is an old hole edge (nothing on the
            // other side). With a context the source boundary is known directly;
            // otherwise look for an outside triangle sharing the edge.
            HashSet<long> sharedBorder = null;
            bool useSourceBoundary = context != null && context.SourceBoundary != null;

            if (!useSourceBoundary)
            {
                var borderEdges = new HashSet<long>(MixedLongComparer.Instance);
                bool[] borderPid = new bool[posRep.Count];

                foreach (List<int> list in regionTriangles)
                {
                    for (int t = 0; t < list.Count; t += 3)
                    {
                        for (int e = 0; e < 3; e++)
                        {
                            int ia = list[t + e], ib = list[t + (e + 1) % 3];
                            if (pid[ia] == pid[ib] || !IsRegionBorderEdge(ia, ib)) continue;
                            borderEdges.Add(UndirectedEdgeKey(pid[ia], pid[ib]));
                            borderPid[pid[ia]] = borderPid[pid[ib]] = true;
                        }
                    }
                }

                sharedBorder = new HashSet<long>(MixedLongComparer.Instance);

                foreach (List<int> list in submeshTriangles)
                {
                    for (int t = 0; t < list.Count; t += 3)
                    {
                        int p0 = pid[list[t]], p1 = pid[list[t + 1]], p2 = pid[list[t + 2]];
                        if (p0 < 0 || p1 < 0 || p2 < 0) continue;
                        if (!borderPid[p0] && !borderPid[p1] && !borderPid[p2]) continue;

                        long k0 = UndirectedEdgeKey(p0, p1), k1 = UndirectedEdgeKey(p1, p2), k2 = UndirectedEdgeKey(p2, p0);
                        if (borderEdges.Contains(k0)) sharedBorder.Add(k0);
                        if (borderEdges.Contains(k1)) sharedBorder.Add(k1);
                        if (borderEdges.Contains(k2)) sharedBorder.Add(k2);
                    }
                }
            }

            var edgeOwner = new Dictionary<long, int>(4096, MixedLongComparer.Instance);
            bool[] attached = new bool[triangleCount];
            int tri = 0;

            foreach (List<int> list in regionTriangles)
            {
                for (int t = 0; t < list.Count; t += 3, tri++)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int ia = list[t + e], ib = list[t + (e + 1) % 3];
                        int a = pid[ia], b = pid[ib];
                        if (a == b) continue;

                        long key = UndirectedEdgeKey(a, b);

                        if (IsRegionBorderEdge(ia, ib))
                        {
                            bool shared = useSourceBoundary
                                ? !context.SourceBoundary.Contains(CutContext.Key(ia, ib))
                                : sharedBorder.Contains(key);
                            if (shared)
                                attached[tri] = true;
                        }
                        if (edgeOwner.TryGetValue(key, out int other))
                        {
                            int ra = Find(tri), rb = Find(other);
                            if (ra != rb) parent[ra] = rb;
                        }
                        else
                        {
                            edgeOwner.Add(key, tri);
                        }
                    }
                }
            }

            // Per component: attached to the rest, or reaching outside the mask?
            bool[] keep = new bool[triangleCount];
            tri = 0;

            foreach (List<int> list in regionTriangles)
            {
                for (int t = 0; t < list.Count; t += 3, tri++)
                {
                    int root = Find(tri);
                    if (attached[tri])
                        keep[root] = true;

                    for (int k = 0; k < 3; k++)
                    {
                        Vector2 m = maskUVs[list[t + k]];
                        if (m.x < 0f || m.x > 1f || m.y < 0f || m.y > 1f)
                            keep[root] = true;
                    }
                }
            }

            tri = 0;

            foreach (List<int> list in regionTriangles)
            {
                int write = 0;

                for (int t = 0; t < list.Count; t += 3, tri++)
                {
                    if (!keep[Find(tri)])
                        continue;

                    list[write++] = list[t];
                    list[write++] = list[t + 1];
                    list[write++] = list[t + 2];
                }

                if (write < list.Count)
                    list.RemoveRange(write, list.Count - write);
            }
        }

        // Old index -> new index (-1 = removed), valid after CompactUnusedVertices.
        private int[] compactRemap;

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
            compactRemap = remap;
            int next = 0;

            for (int i = 0; i < count; i++)
            {
                remap[i] = used[i] ? next++ : -1;
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
        private readonly byte[] alpha;
        private readonly int[] summedArea;
        public readonly float threshold;

        public AlphaMaskData(Texture2D texture, float threshold)
        {
            width = texture.width;
            height = texture.height;
            this.threshold = threshold;
            Color32[] pixels = texture.GetPixels32();
            solid = new byte[pixels.Length];
            alpha = new byte[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
                alpha[i] = pixels[i].a;
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

        // Signed field for contour cutting: > 0 inside the opaque (removed) area,
        // < 0 outside. Bilinear alpha, so the iso-line is smooth (sub-texel).
        public float Field(Vector2 uv)
        {
            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
                return -threshold - 1e-3f;

            float fx = uv.x * width - 0.5f;
            float fy = uv.y * height - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;

            float a00 = Alpha(x0, y0), a10 = Alpha(x0 + 1, y0);
            float a01 = Alpha(x0, y0 + 1), a11 = Alpha(x0 + 1, y0 + 1);

            float a = Mathf.Lerp(Mathf.Lerp(a00, a10, tx), Mathf.Lerp(a01, a11, tx), ty);
            return a - threshold;
        }

        private float Alpha(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
                return 0f;
            return alpha[y * width + x] * (1f / 255f);
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

            // Outside the mask is transparent: a rectangle crossing the mask border
            // is transparent if its inside part is (was always "mixed" before,
            // which recursed along the whole mask border down to leaf size).
            // It can only be fully opaque if it does not cross the border.
            bool crossesBoundary = minU < 0f || maxU > 1f || minV < 0f || maxV > 1f;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(minU * width), 0, width - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt(maxU * width), 0, width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(minV * height), 0, height - 1);
            int y1 = Mathf.Clamp(Mathf.FloorToInt(maxV * height), 0, height - 1);

            int rectWidth = x1 - x0 + 1;
            int rectHeight = y1 - y0 + 1;
            int sum = RectangleSum(x0, y0, x1, y1);
            int count = rectWidth * rectHeight;

            if (sum == 0) return 0;
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
        float rotationDegrees = 0f,
        CutContext context = null)
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

        // Weld step scaled to the mesh, capped to stay consistent with
        // MeshSurfacePatcher's default weld tolerance (1e-4).
        Vector3 boundsSize = source.bounds.size;
        float maxExtent = Mathf.Max(boundsSize.x, Mathf.Max(boundsSize.y, boundsSize.z));
        float weldStep = Mathf.Clamp(maxExtent * 1e-6f, 1e-6f, 1e-4f);

        MeshBuilder builder = new MeshBuilder(
            source.subMeshCount, vertices, uv, hasNormals ? sourceNormals : null, maskUVs, weldStep);
        builder.context = context;

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

                // Both sides of a plate are cut (the back face of a thin deck is
                // otherwise left in place and, with double-sided materials, still
                // covers the hole). Walls perpendicular to the hit are untouched.
                bool sameFacingSurface = Mathf.Abs(Vector3.Dot(triangleNormal, hitNormalWorld)) >= minNormalDot;
                bool withinProjectionDepth = projectionDepth <= 0f || planeDistance <= projectionDepth;

                if (!sameFacingSurface || !withinProjectionDepth)
                {
                    builder.AddIndexedTriangle(submesh, i0, i1, i2);
                    continue;
                }

                builder.MarkTouched(i0, i1, i2);

                // Entirely inside the opaque part of the mask: removed.
                if (state == 1)
                    continue;

                VertexData a = new VertexData(vertices[i0], GetUV(uv, i0), ua,
                    hasNormals ? sourceNormals[i0] : default, i0);
                VertexData b = new VertexData(vertices[i1], GetUV(uv, i1), ub,
                    hasNormals ? sourceNormals[i1] : default, i1);
                VertexData c = new VertexData(vertices[i2], GetUV(uv, i2), uc,
                    hasNormals ? sourceNormals[i2] : default, i2);

                ProcessMaskTriangle(builder, mask, submesh, a, b, c, 0, depthLimit);
            }
        }

        return builder.Build(source.name + "_ExplosiveMaskRemoved");
    }

    private const int TriKept = 0;
    private const int TriRemoved = 1;
    private const int TriMixed = 2;

    // "Edge Detail" = resolution of the cut outline on the mask. The outline is
    // interpolated along the alpha contour inside each leaf (no staircase), so
    // leaves can be 4x larger than a staircase cut for the same look:
    // detail 8 -> smallest piece 1/64 of the mask, detail 10 -> 1/256.
    // Cost (cut, T-junction repair, fill) scales with 1/leafSize.
    private static void ProcessMaskTriangle(
        MeshBuilder builder, AlphaMaskData mask, int submesh,
        VertexData a, VertexData b, VertexData c, int depth, int maxDepth)
    {
        float leafSize = 1f / (1 << Mathf.Max(2, maxDepth - 2));
        int recursionLimit = maxDepth * 2 + 24;

        int result = ClassifyMaskTriangle(builder, mask, submesh, a, b, c, 0, recursionLimit, leafSize);

        if (result == TriKept)
            builder.AddTriangle(submesh, a, b, c);
    }

    // Returns TriKept / TriRemoved without emitting anything, so a parent whose
    // children are all kept (or all removed) collapses back into one triangle.
    // Only TriMixed results have already emitted their kept pieces.
    //
    // Longest-edge bisection (instead of 4-way midpoint split) matters for long
    // thin triangles such as fill patches: a 4-way split keeps them thin, so their
    // bounding rect stays "mixed" all the way down and each one exploded into tens
    // of thousands of pieces. Bisection shortens them, so work is proportional to
    // the outline length / leafSize.
    // VertexData (48 bytes) is passed with `in` everywhere in the recursion: it
    // runs ~100k times per cut and by-value copies were a large part of its cost.
    private static int ClassifyMaskTriangle(
        MeshBuilder builder, AlphaMaskData mask, int submesh,
        in VertexData a, in VertexData b, in VertexData c, int depth, int recursionLimit, float leafSize)
    {
        Vector2 ua = a.maskUV, ub = b.maskUV, uc = c.maskUV;

        int state = mask.GetRectState(ua, ub, uc);
        if (state == 0)
            return TriKept;
        if (state == 1)
            return TriRemoved;

        float extentU = Mathf.Max(ua.x, Mathf.Max(ub.x, uc.x)) - Mathf.Min(ua.x, Mathf.Min(ub.x, uc.x));
        float extentV = Mathf.Max(ua.y, Mathf.Max(ub.y, uc.y)) - Mathf.Min(ua.y, Mathf.Min(ub.y, uc.y));

        if (depth >= recursionLimit || (extentU <= leafSize && extentV <= leafSize))
            return ClipLeafAlongContour(builder, mask, submesh, a, b, c);

        // Bisect the longest edge (in mask space), preserving winding.
        float lab = (ub - ua).sqrMagnitude;
        float lbc = (uc - ub).sqrMagnitude;
        float lca = (ua - uc).sqrMagnitude;

        if (lbc >= lab && lbc >= lca)
            return Bisect(builder, mask, submesh, b, c, a, depth, recursionLimit, leafSize);
        if (lca >= lab && lca >= lbc)
            return Bisect(builder, mask, submesh, c, a, b, depth, recursionLimit, leafSize);
        return Bisect(builder, mask, submesh, a, b, c, depth, recursionLimit, leafSize);
    }

    // Splits edge a-b at its midpoint into (a, m, c) and (m, b, c).
    private static int Bisect(
        MeshBuilder builder, AlphaMaskData mask, int submesh,
        in VertexData a, in VertexData b, in VertexData c, int depth, int recursionLimit, float leafSize)
    {
        VertexData m = VertexData.Lerp(a, b, 0.5f);

        int r0 = ClassifyMaskTriangle(builder, mask, submesh, a, m, c, depth + 1, recursionLimit, leafSize);
        int r1 = ClassifyMaskTriangle(builder, mask, submesh, m, b, c, depth + 1, recursionLimit, leafSize);

        if (r0 == r1 && r0 != TriMixed)
            return r0;

        if (r0 == TriKept) builder.AddTriangle(submesh, a, m, c);
        if (r1 == TriKept) builder.AddTriangle(submesh, m, b, c);

        return TriMixed;
    }

    // Marching-triangles cut of a leaf along the mask iso-line (alpha = threshold).
    // Instead of keeping/removing whole leaves (a staircase outline that differs
    // per layer/triangulation), the kept part is clipped exactly at the contour.
    // Edge crossings depend only on the edge (canonical endpoint order), so
    // neighbouring leaves share the same cut vertex and every stacked layer gets
    // the same outline. No allocations: this runs for every leaf on the outline.
    private static int ClipLeafAlongContour(
        MeshBuilder builder, AlphaMaskData mask, int submesh,
        in VertexData a, in VertexData b, in VertexData c)
    {
        float fa = mask.Field(a.maskUV);
        float fb = mask.Field(b.maskUV);
        float fc = mask.Field(c.maskUV);

        bool ra = fa >= 0f, rb = fb >= 0f, rc = fc >= 0f;
        int removedCount = (ra ? 1 : 0) + (rb ? 1 : 0) + (rc ? 1 : 0);

        if (removedCount == 0)
            return TriKept;
        if (removedCount == 3)
            return TriRemoved;

        // Rotate (keeping winding) so p is the vertex on its own side:
        // the removed one when 1 is removed, the kept one when 2 are removed.
        bool oddIsRemoved = removedCount == 1;

        if (ra == oddIsRemoved)
            EmitLeafCut(builder, mask, submesh, a, fa, b, fb, c, fc, oddIsRemoved);
        else if (rb == oddIsRemoved)
            EmitLeafCut(builder, mask, submesh, b, fb, c, fc, a, fa, oddIsRemoved);
        else
            EmitLeafCut(builder, mask, submesh, c, fc, a, fa, b, fb, oddIsRemoved);

        return TriMixed;
    }

    private static void EmitLeafCut(
        MeshBuilder builder, AlphaMaskData mask, int submesh,
        in VertexData p, float fp, in VertexData q, float fq, in VertexData r, float fr,
        bool pRemoved)
    {
        VertexData xpq = ContourCrossing(mask, p, fp, q, fq);
        VertexData xrp = ContourCrossing(mask, r, fr, p, fp);

        if (pRemoved)
        {
            // Kept quad: xpq, q, r, xrp
            builder.AddTriangle(submesh, xpq, q, r);
            builder.AddTriangle(submesh, xpq, r, xrp);
        }
        else
        {
            // Kept corner triangle: p, xpq, xrp
            builder.AddTriangle(submesh, p, xpq, xrp);
        }
    }

    private static VertexData ContourCrossing(
        AlphaMaskData mask, in VertexData p0, float fp0, in VertexData q0, float fq0)
    {
        // Canonical order so both triangles sharing this edge compute the
        // bit-identical vertex.
        bool swap = q0.maskUV.x < p0.maskUV.x || (q0.maskUV.x == p0.maskUV.x && q0.maskUV.y < p0.maskUV.y);
        VertexData p = swap ? q0 : p0;
        VertexData q = swap ? p0 : q0;
        float fp = swap ? fq0 : fp0;
        float fq = swap ? fp0 : fq0;

        float lo = 0f, hi = 1f;
        float flo = fp, fhi = fq;
        Vector2 pu = p.maskUV, qu = q.maskUV;

        // 8 halvings of a leaf edge (<= 1/2^detail of the mask) is far below a
        // texel; regula falsi on the last bracket refines it further.
        for (int i = 0; i < 8; i++)
        {
            float mid = (lo + hi) * 0.5f;
            float fm = mask.Field(Vector2.LerpUnclamped(pu, qu, mid));

            if ((fm >= 0f) == (flo >= 0f))
            {
                lo = mid;
                flo = fm;
            }
            else
            {
                hi = mid;
                fhi = fm;
            }
        }

        float denom = flo - fhi;
        float t = Mathf.Abs(denom) > 1e-12f ? lo + (hi - lo) * (flo / denom) : (lo + hi) * 0.5f;

        // Snap crossings that land on an end point; avoids sliver triangles.
        if (t < 0.02f) return p;
        if (t > 0.98f) return q;

        return VertexData.Lerp(p, q, t);
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
