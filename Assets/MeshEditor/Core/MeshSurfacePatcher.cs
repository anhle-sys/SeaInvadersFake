using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class MeshSurfacePatcher
{
    private struct PointKey : IEquatable<PointKey>
    {
        public long x, y, z;
        public PointKey(Vector3 p, float invTolerance)
        {
            x = (long)Math.Round(p.x * invTolerance);
            y = (long)Math.Round(p.y * invTolerance);
            z = (long)Math.Round(p.z * invTolerance);
        }
        public bool Equals(PointKey other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object obj) => obj is PointKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return (int)(x * 73856093L) ^ (int)(y * 19349663L) ^ (int)(z * 83492791L); }
        }
    }

    private static long UndirectedKey(int a, int b)
    {
        return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
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
        int vertexCount = vertices.Length;
        int submeshCount = source.subMeshCount;

        // 1. Weld once per vertex (instead of hashing 3 longs per half-edge).
        float invTolerance = 1f / weldTolerance;
        int[] pid = new int[vertexCount];
        var pointMap = new Dictionary<PointKey, int>(vertexCount);

        for (int i = 0; i < vertexCount; i++)
        {
            PointKey key = new PointKey(vertices[i], invTolerance);
            if (!pointMap.TryGetValue(key, out int id))
            {
                id = pointMap.Count;
                pointMap.Add(key, id);
            }
            pid[i] = id;
        }

        // 2. Edge table: undirected key -> slot in flat arrays (no per-edge allocations).
        var triangleLists = new int[submeshCount][];
        int totalIndices = 0;

        for (int s = 0; s < submeshCount; s++)
        {
            if (source.GetTopology(s) != MeshTopology.Triangles)
                continue;

            triangleLists[s] = source.GetTriangles(s);
            totalIndices += triangleLists[s].Length;
        }

        var edgeSlot = new Dictionary<long, int>(totalIndices, MixedLongComparer.Instance);
        int[] edgeFromIndex = new int[totalIndices];
        int[] edgeToIndex = new int[totalIndices];
        int[] edgeSubmesh = new int[totalIndices];
        int[] edgeThirdIndex = new int[totalIndices];
        int[] edgeCount = new int[totalIndices];
        int edgeTotal = 0;

        for (int s = 0; s < submeshCount; s++)
        {
            int[] t = triangleLists[s];
            if (t == null)
                continue;

            for (int i = 0; i < t.Length; i += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int ia = t[i + e];
                    int ib = t[i + (e + 1) % 3];
                    int a = pid[ia];
                    int b = pid[ib];

                    if (a == b)
                        continue;

                    long key = UndirectedKey(a, b);

                    if (!edgeSlot.TryGetValue(key, out int slot))
                    {
                        slot = edgeTotal++;
                        edgeSlot.Add(key, slot);
                        edgeFromIndex[slot] = ia;
                        edgeToIndex[slot] = ib;
                        edgeSubmesh[slot] = s;
                        edgeThirdIndex[slot] = t[i + (e + 2) % 3];
                    }

                    edgeCount[slot]++;
                }
            }
        }

        // 3. Boundary edges, directed by their triangle's winding, linked per start vertex.
        var outgoingHead = new Dictionary<int, int>();
        var boundary = new List<int>();
        var nextOutgoing = new List<int>();
        var used = new List<bool>();

        for (int e = 0; e < edgeTotal; e++)
        {
            if (edgeCount[e] != 1)
                continue;

            int local = boundary.Count;
            int from = pid[edgeFromIndex[e]];

            boundary.Add(e);
            used.Add(false);
            nextOutgoing.Add(outgoingHead.TryGetValue(from, out int head) ? head : -1);
            outgoingHead[from] = local;
        }

        if (boundary.Count < 3)
            return null;

        // Cuts through large low-poly triangles can leave sub-millimetre cracks in
        // a hole outline (sliver triangles removed as degenerate), so the walk hits
        // a dead end. Chains that start at a vertex with no incoming boundary edge
        // are the other side of such a crack; the walk may jump to one of them when
        // it is within gapBridgeDistance.
        float gapBridgeDistance = weldTolerance * GapBridgeMultiplier;
        float bridgeSqr = gapBridgeDistance * gapBridgeDistance;

        var hasIncoming = new HashSet<int>();
        for (int c = 0; c < boundary.Count; c++)
            hasIncoming.Add(pid[edgeToIndex[boundary[c]]]);

        var danglingStarts = new List<int>();
        for (int c = 0; c < boundary.Count; c++)
        {
            if (!hasIncoming.Contains(pid[edgeFromIndex[boundary[c]]]))
                danglingStarts.Add(c);
        }

        // 4. Walk closed loops and sort them into holes and islands.
        var holes = new List<List<int>>();
        var holeSubmesh = new List<int>();
        var islands = new List<List<int>>();
        int[] votes = new int[submeshCount];

        for (int first = 0; first < boundary.Count; first++)
        {
            if (used[first])
                continue;

            var loopIndices = new List<int>();
            Array.Clear(votes, 0, votes.Length);
            Vector3 surfaceNormal = Vector3.zero;

            int startPid = pid[edgeFromIndex[boundary[first]]];
            int current = first;
            bool closed = false;
            int guard = boundary.Count + 1;

            while (guard-- > 0)
            {
                used[current] = true;

                int e = boundary[current];
                loopIndices.Add(edgeFromIndex[e]);
                votes[edgeSubmesh[e]]++;

                Vector3 ea = vertices[edgeFromIndex[e]];
                surfaceNormal += Vector3.Cross(
                    vertices[edgeToIndex[e]] - ea, vertices[edgeThirdIndex[e]] - ea);

                int nextPid = pid[edgeToIndex[e]];

                if (nextPid == startPid)
                {
                    closed = true;
                    break;
                }

                int candidate = PickNextBoundaryEdge(
                    e, outgoingHead.TryGetValue(nextPid, out int h) ? h : -1,
                    boundary, nextOutgoing, used, vertices,
                    edgeFromIndex, edgeToIndex, edgeThirdIndex);

                if (candidate < 0 && danglingStarts.Count > 0)
                {
                    Vector3 end = vertices[edgeToIndex[e]];
                    Vector3 start = vertices[edgeFromIndex[boundary[first]]];

                    if ((end - start).sqrMagnitude <= bridgeSqr)
                    {
                        closed = true;
                        break;
                    }

                    candidate = FindBridgeEdge(
                        end, bridgeSqr, danglingStarts, boundary, used, vertices, edgeFromIndex);
                }

                if (candidate < 0)
                    break;

                current = candidate;
            }

            if (!closed || loopIndices.Count < 3)
                continue;

            // The walk follows the surrounding triangles' winding; the patch
            // must face the opposite way to close the surface.
            loopIndices.Reverse();

            // A hole patch faces the same way as the surface around it. A loop
            // whose patch would face the opposite way is the outline of an island
            // left inside a hole (or an outer border). Filling it would put a
            // back-facing copy on top of the island, so it is never filled on its
            // own; instead it is cut out of the surrounding hole's patch.
            // Zero-area slit loops (leftover T-junction cracks) are neither holes
            // nor islands; bridging them into a patch makes ear clipping fail.
            Vector3 loopNormal = LoopNormal(loopIndices, vertices);
            float perimeter = 0f;
            for (int i = 0; i < loopIndices.Count; i++)
                perimeter += Vector3.Distance(vertices[loopIndices[i]], vertices[loopIndices[(i + 1) % loopIndices.Count]]);

            if (loopNormal.magnitude * 0.5f < perimeter * perimeter * 1e-3f)
                continue;

            if (Vector3.Dot(loopNormal, surfaceNormal) <= 0f)
            {
                islands.Add(loopIndices);
                continue;
            }

            int targetSubmesh = 0;
            for (int s = 1; s < submeshCount; s++)
            {
                if (votes[s] > votes[targetSubmesh])
                    targetSubmesh = s;
            }

            holes.Add(loopIndices);
            holeSubmesh.Add(targetSubmesh);
        }

        // 5. Triangulate each hole, leaving out the islands lying inside it so the
        // patch never overlaps kept geometry (overlap = non-manifold after the next cut).
        var islandBounds = new Bounds[islands.Count];
        var islandUsed = new bool[islands.Count];

        for (int i = 0; i < islands.Count; i++)
            islandBounds[i] = LoopBounds(islands[i], vertices);

        var candidates = new List<List<int>>();
        var candidateIds = new List<int>();

        for (int h = 0; h < holes.Count; h++)
        {
            Bounds holeBounds = LoopBounds(holes[h], vertices);
            candidates.Clear();
            candidateIds.Clear();

            for (int i = 0; i < islands.Count; i++)
            {
                if (islandUsed[i] || !holeBounds.Intersects(islandBounds[i]))
                    continue;

                candidates.Add(islands[i]);
                candidateIds.Add(i);
            }

            var patchTriangles = new List<int>(holes[h].Count * 3);
            var consumed = new List<int>();

            if (!Triangulate(holes[h], candidates, vertices, patchTriangles, consumed))
                continue;

            foreach (int c in consumed)
                islandUsed[candidateIds[c]] = true;

            regions.Add(new FillRegion
            {
                regionId = regions.Count,
                submeshIndex = holeSubmesh[h],
                triangleIndices = patchTriangles,
                isVisible = true
            });
        }

        if (regions.Count == 0)
            return null;

        return BuildMeshWithRegions(source, regions);
    }

    private static Vector3 LoopNormal(List<int> loop, Vector3[] vertices)
    {
        Vector3 n = Vector3.zero;
        int count = loop.Count;

        for (int i = 0; i < count; i++)
        {
            Vector3 a = vertices[loop[i]], b = vertices[loop[(i + 1) % count]];
            n.x += (a.y - b.y) * (a.z + b.z);
            n.y += (a.z - b.z) * (a.x + b.x);
            n.z += (a.x - b.x) * (a.y + b.y);
        }

        return n;
    }

    // Max crack width bridged by the loop walk, as a multiple of the weld tolerance
    // (default tolerance 1e-4 -> 5 mm in mesh units).
    private const float GapBridgeMultiplier = 50f;

    // Nearest unused chain start (vertex without an incoming boundary edge)
    // within the bridge distance of a dead end, or -1.
    private static int FindBridgeEdge(
        Vector3 deadEnd, float bridgeSqr, List<int> danglingStarts,
        List<int> boundary, List<bool> used, Vector3[] vertices, int[] edgeFrom)
    {
        int best = -1;
        float bestSqr = bridgeSqr;

        foreach (int c in danglingStarts)
        {
            if (used[c])
                continue;

            float d = (vertices[edgeFrom[boundary[c]]] - deadEnd).sqrMagnitude;
            if (d <= bestSqr)
            {
                bestSqr = d;
                best = c;
            }
        }

        return best;
    }

    // At a vertex where two holes touch there are several unused outgoing
    // boundary edges. Taking an arbitrary one merges the holes into a
    // figure-8 loop that ear clipping cannot triangulate. Instead take the
    // first edge counter-clockwise (around the face normal) from the incoming
    // edge, which keeps following the same hole.
    private static int PickNextBoundaryEdge(
        int incomingEdge, int head,
        List<int> boundary, List<int> nextOutgoing, List<bool> used,
        Vector3[] vertices, int[] edgeFrom, int[] edgeTo, int[] edgeThird)
    {
        int first = -1;
        int count = 0;

        for (int c = head; c >= 0; c = nextOutgoing[c])
        {
            if (used[c]) continue;
            if (first < 0) first = c;
            count++;
        }

        if (count <= 1)
            return first;

        Vector3 a = vertices[edgeFrom[incomingEdge]];
        Vector3 b = vertices[edgeTo[incomingEdge]];
        Vector3 normal = Vector3.Cross(b - a, vertices[edgeThird[incomingEdge]] - a);
        Vector3 back = a - b;

        int best = first;
        float bestAngle = float.MaxValue;

        for (int c = head; c >= 0; c = nextOutgoing[c])
        {
            if (used[c]) continue;

            Vector3 d = vertices[edgeTo[boundary[c]]] - b;
            float angle = Mathf.Atan2(
                Vector3.Dot(normal, Vector3.Cross(back, d)),
                Vector3.Dot(back, d) * normal.magnitude);

            if (angle <= 0f)
                angle += Mathf.PI * 2f;

            if (angle < bestAngle)
            {
                bestAngle = angle;
                best = c;
            }
        }

        return best;
    }

    public static Mesh BuildMeshWithRegions(
        Mesh baseMesh,
        List<FillRegion> regions)
    {
        if (baseMesh == null || regions == null)
            return null;

        // Native copy keeps every vertex attribute (normals, tangents, uv2, colors)
        // and is much faster than reading/writing managed arrays. Fill patches only
        // reference existing vertices, so vertices/bounds never change.
        Mesh result = UnityEngine.Object.Instantiate(baseMesh);
        result.name = baseMesh.name + "_SurfacePatched";

        var combined = new List<int>();

        for (int s = 0; s < baseMesh.subMeshCount; s++)
        {
            bool hasVisible = false;

            foreach (FillRegion region in regions)
            {
                if (region.isVisible && region.submeshIndex == s)
                {
                    hasVisible = true;
                    break;
                }
            }

            if (!hasVisible || baseMesh.GetTopology(s) != MeshTopology.Triangles)
                continue;

            combined.Clear();
            baseMesh.GetTriangles(combined, s);

            foreach (FillRegion region in regions)
            {
                if (region.isVisible && region.submeshIndex == s)
                    combined.AddRange(region.triangleIndices);
            }

            result.SetTriangles(combined, s, false);
        }

        if (!baseMesh.HasVertexAttribute(VertexAttribute.Normal))
            result.RecalculateNormals();

        return result;
    }

    private static Bounds LoopBounds(List<int> loop, Vector3[] vertices)
    {
        Bounds b = new Bounds(vertices[loop[0]], Vector3.zero);
        for (int i = 1; i < loop.Count; i++)
            b.Encapsulate(vertices[loop[i]]);
        b.Expand(1e-4f);
        return b;
    }

    // Ear clipping with a doubly linked list and reflex-only containment tests.
    // Amortised O(n * r) instead of the previous O(n^3) restart-from-zero loop.
    // Islands that lie inside the outer loop (same plane) are joined to it with
    // bridge edges first, so the patch has holes where the islands are.
    private static bool Triangulate(
        List<int> outer, List<List<int>> islandCandidates, Vector3[] vertices,
        List<int> triangles, List<int> consumedIslands)
    {
        triangles.Clear();
        int outerCount = outer.Count;
        if (outerCount < 3) return false;

        Vector3 normal = Vector3.zero;
        Vector3 center = Vector3.zero;

        for (int i = 0; i < outerCount; i++)
        {
            Vector3 a = vertices[outer[i]], b = vertices[outer[(i + 1) % outerCount]];
            normal.x += (a.y - b.y) * (a.z + b.z);
            normal.y += (a.z - b.z) * (a.x + b.x);
            normal.z += (a.x - b.x) * (a.y + b.y);
            center += a;
        }

        if (normal.sqrMagnitude < 1e-12f) return false;
        normal.Normalize();
        center /= outerCount;

        // A strongly folded boundary cannot be safely filled by one projected polygon.
        float scale = 0f, maxDeviation = 0f;
        for (int i = 0; i < outerCount; i++)
        {
            Vector3 d = vertices[outer[i]] - center;
            scale = Mathf.Max(scale, d.magnitude);
            maxDeviation = Mathf.Max(maxDeviation, Mathf.Abs(Vector3.Dot(d, normal)));
        }
        if (scale > 0f && maxDeviation > scale * 0.35f) return false;

        Vector3 axis = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
        Vector3 right = Vector3.Cross(axis, normal).normalized;
        Vector3 up = Vector3.Cross(normal, right).normalized;

        Vector2 Project(int vertexIndex)
        {
            Vector3 d = vertices[vertexIndex] - center;
            return new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up));
        }

        var loop = new List<int>(outer);
        var pts = new List<Vector2>(outerCount);
        for (int i = 0; i < outerCount; i++)
            pts.Add(Project(outer[i]));

        float outerArea = SignedArea(pts);
        if (Mathf.Abs(outerArea) < 1e-10f) return false;
        float sign = Mathf.Sign(outerArea);

        if (islandCandidates != null && islandCandidates.Count > 0)
        {
            MergeIslands(loop, pts, islandCandidates, consumedIslands, vertices,
                center, normal, Mathf.Max(scale * 0.02f, 1e-5f), sign, Project);
        }

        return EarClip(loop, pts.ToArray(), sign, triangles);
    }

    private static float SignedArea(List<Vector2> pts)
    {
        float area = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % pts.Count];
            area += a.x * b.y - b.x * a.y;
        }
        return area;
    }

    private static bool PointInPolygon(List<Vector2> poly, int count, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            Vector2 a = poly[i], b = poly[j];
            if ((a.y > p.y) != (b.y > p.y) &&
                p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    // Hole bridging (Eberly, "Triangulation by Ear Clipping"): connect each
    // island's right-most vertex to a visible vertex of the polygon.
    private static void MergeIslands(
        List<int> loop, List<Vector2> pts, List<List<int>> candidates, List<int> consumed,
        Vector3[] vertices, Vector3 center, Vector3 normal, float planeTolerance, float sign,
        Func<int, Vector2> project)
    {
        int outerCount = pts.Count;
        var accepted = new List<(int id, List<int> idx, List<Vector2> p, int maxIdx)>();

        for (int c = 0; c < candidates.Count; c++)
        {
            List<int> island = candidates[c];
            if (island.Count < 3) continue;

            bool planar = true;
            for (int i = 0; i < island.Count; i++)
            {
                if (Mathf.Abs(Vector3.Dot(vertices[island[i]] - center, normal)) > planeTolerance)
                {
                    planar = false;
                    break;
                }
            }
            if (!planar) continue;

            var ip = new List<Vector2>(island.Count);
            for (int i = 0; i < island.Count; i++)
                ip.Add(project(island[i]));

            // Island loops never cross the hole outline (both are closed boundary
            // loops of the same mesh), so one vertex decides inside/outside.
            if (!PointInPolygon(pts, outerCount, ip[0]))
                continue;

            var idx = new List<int>(island);
            if (Mathf.Sign(SignedArea(ip)) == sign)
            {
                idx.Reverse();
                ip.Reverse();
            }

            int maxIdx = 0;
            for (int i = 1; i < ip.Count; i++)
                if (ip[i].x > ip[maxIdx].x) maxIdx = i;

            accepted.Add((c, idx, ip, maxIdx));
        }

        accepted.Sort((x, y) => y.p[y.maxIdx].x.CompareTo(x.p[x.maxIdx].x));

        foreach (var island in accepted)
        {
            Vector2 m = island.p[island.maxIdx];
            int bridge = FindBridgeVertex(pts, m, sign);
            if (bridge < 0) continue;

            // Splice: ..., P, M, island..., M, P, ...
            var insertIdx = new List<int>(island.idx.Count + 2);
            var insertPts = new List<Vector2>(island.idx.Count + 2);
            int count = island.idx.Count;

            for (int k = 0; k <= count; k++)
            {
                int i = (island.maxIdx + k) % count;
                insertIdx.Add(island.idx[i]);
                insertPts.Add(island.p[i]);
            }

            insertIdx.Add(loop[bridge]);
            insertPts.Add(pts[bridge]);

            loop.InsertRange(bridge + 1, insertIdx);
            pts.InsertRange(bridge + 1, insertPts);
            consumed.Add(island.id);
        }
    }

    private static int FindBridgeVertex(List<Vector2> pts, Vector2 m, float sign)
    {
        int n = pts.Count;
        float bestX = float.MaxValue;
        int edge = -1;
        Vector2 hit = default;

        for (int i = 0; i < n; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % n];
            if ((a.y > m.y) == (b.y > m.y)) continue;

            float x = a.x + (m.y - a.y) * (b.x - a.x) / (b.y - a.y);
            if (x < m.x || x >= bestX) continue;

            bestX = x;
            edge = i;
            hit = new Vector2(x, m.y);
        }

        if (edge < 0) return -1;

        int p = pts[edge].x > pts[(edge + 1) % n].x ? edge : (edge + 1) % n;
        Vector2 pp = pts[p];

        // A reflex vertex inside triangle (M, hit, P) would block the bridge:
        // take the one with the smallest angle to the ray instead.
        float bestAngle = float.MaxValue;
        int best = p;

        for (int i = 0; i < n; i++)
        {
            if (i == p) continue;
            Vector2 q = pts[i];
            if (q.x < m.x) continue;

            Vector2 prev = pts[(i - 1 + n) % n], next = pts[(i + 1) % n];
            if (Cross(q - prev, next - q) * sign > 0f) continue;

            if (!InsideTriangle(q, m, hit, pp, Mathf.Sign(Cross(hit - m, pp - m)))) continue;

            float angle = Mathf.Abs(q.y - m.y) / Mathf.Max(q.x - m.x, 1e-12f);
            if (angle < bestAngle)
            {
                bestAngle = angle;
                best = i;
            }
        }

        return best;
    }

    private static bool EarClip(List<int> loop, Vector2[] p2, float sign, List<int> triangles)
    {
        int n = loop.Count;
        if (n < 3) return false;

        int[] prev = new int[n];
        int[] next = new int[n];
        bool[] convex = new bool[n];
        bool[] removed = new bool[n];

        for (int i = 0; i < n; i++)
        {
            prev[i] = (i - 1 + n) % n;
            next[i] = (i + 1) % n;
        }

        // Non-convex (reflex or collinear) vertices are the only ones that can
        // block an ear, so containment tests only iterate this list.
        var reflex = new List<int>();
        for (int i = 0; i < n; i++)
        {
            convex[i] = IsConvex(p2, prev[i], i, next[i], sign);
            if (!convex[i]) reflex.Add(i);
        }

        // Uniform grid over the reflex vertices: an ear test only looks at the
        // cells under the ear's bounding box instead of every reflex vertex
        // (long jagged outlines have thousands of them -> was O(n^3)).
        var grid = new ReflexGrid(p2, reflex);

        int remaining = n;
        int current = 0;
        int stall = 0;

        while (remaining > 3)
        {
            if (stall > remaining)
            {
                triangles.Clear();
                return false;
            }

            int pi = prev[current], ni = next[current];

            if (convex[current] && !grid.AnyInside(p2, removed, convex, pi, current, ni, sign))
            {
                triangles.Add(loop[pi]);
                triangles.Add(loop[current]);
                triangles.Add(loop[ni]);

                removed[current] = true;
                next[pi] = ni;
                prev[ni] = pi;
                remaining--;

                UpdateConvexity(p2, prev, next, convex, pi, sign);
                UpdateConvexity(p2, prev, next, convex, ni, sign);

                current = pi;
                stall = 0;
                continue;
            }

            current = ni;
            stall++;
        }

        int last = current;
        while (removed[last]) last = next[last];

        if (IsConvex(p2, prev[last], last, next[last], sign))
        {
            triangles.Add(loop[prev[last]]);
            triangles.Add(loop[last]);
            triangles.Add(loop[next[last]]);
        }

        return triangles.Count >= 3;
    }

    // Relative threshold: nearly collinear vertices (very common on a jagged cut
    // outline) are not ears, otherwise zero-area triangles are emitted and they
    // corrupt the topology for the next cut.
    private static bool IsConvex(Vector2[] p, int a, int b, int c, float sign)
    {
        Vector2 e1 = p[b] - p[a];
        Vector2 e2 = p[c] - p[b];
        float cross = Cross(e1, e2) * sign;
        return cross > 1e-5f * Mathf.Sqrt(e1.sqrMagnitude * e2.sqrMagnitude) && cross > 1e-12f;
    }

    private static void UpdateConvexity(Vector2[] p, int[] prev, int[] next, bool[] convex, int i, float sign)
    {
        // Vertices only ever become convex after clipping; they stay in the reflex
        // list but are skipped via the convex flag.
        convex[i] = IsConvex(p, prev[i], i, next[i], sign);
    }

    private sealed class ReflexGrid
    {
        private readonly float minX, minY, invCell;
        private readonly int cols, rows;
        private readonly List<int>[] cells;

        public ReflexGrid(Vector2[] p, List<int> reflex)
        {
            float maxX = float.MinValue, maxY = float.MinValue;
            minX = float.MaxValue; minY = float.MaxValue;

            for (int i = 0; i < p.Length; i++)
            {
                minX = Mathf.Min(minX, p[i].x); maxX = Mathf.Max(maxX, p[i].x);
                minY = Mathf.Min(minY, p[i].y); maxY = Mathf.Max(maxY, p[i].y);
            }

            float w = Mathf.Max(maxX - minX, 1e-9f);
            float h = Mathf.Max(maxY - minY, 1e-9f);
            int target = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(reflex.Count, 1))), 1, 256);
            float cell = Mathf.Max(w, h) / target;
            invCell = 1f / cell;
            cols = Mathf.Clamp(Mathf.CeilToInt(w * invCell) + 1, 1, 512);
            rows = Mathf.Clamp(Mathf.CeilToInt(h * invCell) + 1, 1, 512);
            cells = new List<int>[cols * rows];

            foreach (int r in reflex)
            {
                int ci = CellIndex(p[r]);
                (cells[ci] ??= new List<int>(4)).Add(r);
            }
        }

        private int CellX(float x) => Mathf.Clamp((int)((x - minX) * invCell), 0, cols - 1);
        private int CellY(float y) => Mathf.Clamp((int)((y - minY) * invCell), 0, rows - 1);
        private int CellIndex(Vector2 q) => CellY(q.y) * cols + CellX(q.x);

        public bool AnyInside(Vector2[] p, bool[] removed, bool[] convex, int a, int b, int c, float sign)
        {
            Vector2 pa = p[a], pb = p[b], pc = p[c];

            float bx0 = Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x));
            float bx1 = Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x));
            float by0 = Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y));
            float by1 = Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y));

            int x0 = CellX(bx0), x1 = CellX(bx1);
            int y0 = CellY(by0), y1 = CellY(by1);

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    List<int> list = cells[y * cols + x];
                    if (list == null) continue;

                    for (int k = 0; k < list.Count; k++)
                    {
                        int r = list[k];
                        if (removed[r] || convex[r] || r == a || r == b || r == c)
                            continue;

                        Vector2 q = p[r];
                        if (q.x < bx0 || q.x > bx1 || q.y < by0 || q.y > by1)
                            continue;

                        // Bridge edges duplicate vertices; a copy of an ear corner is not "inside".
                        if (q == pa || q == pb || q == pc)
                            continue;

                        if (InsideTriangle(q, pa, pb, pc, sign))
                            return true;
                    }
                }
            }

            return false;
        }
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

// long.GetHashCode() is (hi ^ lo), which collides massively for packed
// (vertexA << 32 | vertexB) edge keys with nearby ids. This mixes all bits.
internal sealed class MixedLongComparer : IEqualityComparer<long>
{
    public static readonly MixedLongComparer Instance = new MixedLongComparer();

    public bool Equals(long a, long b) => a == b;

    public int GetHashCode(long key)
    {
        unchecked
        {
            ulong z = (ulong)key + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (int)(z ^ (z >> 31));
        }
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
