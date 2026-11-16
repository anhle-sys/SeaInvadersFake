using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
public class RuntimeMeshFillController : MonoBehaviour
{
    [Header("Source Mesh")]
    [SerializeField] private Mesh sourceMesh;

    [SerializeField] private float weldTolerance = 0.0001f;

    [Header("Hit Damage")]
    [Tooltip("Bán kính (m, world) lỗ thủng khi sát thương rất nhỏ.")]
    [SerializeField] private float minHitRadius = 0.5f;

    [Tooltip("Bán kính (m, world) lỗ thủng khi sát thương >= Damage Ratio For Max Radius.")]
    [SerializeField] private float maxHitRadius = 3f;

    [Tooltip("Tỉ lệ sát thương / máu tối đa của 1 phát để đạt bán kính lớn nhất.")]
    [SerializeField, Range(0.01f, 1f)] private float damageRatioForMaxRadius = 0.25f;

    [Tooltip("Không có vùng nào trong bán kính thì vẫn mở vùng gần nhất nếu cách điểm trúng không quá khoảng này (m). 0 = tắt.")]
    [SerializeField] private float snapDistance = 1.5f;

    [Header("Hit Direction")]
    [Tooltip("Chỉ mở vùng có mặt phẳng quay về hướng sát thương: |dot(pháp tuyến vùng, hướng đạn)| >= giá trị này. " +
             "Đạn bắn ngang không mở lỗ trên sàn, đạn rơi từ trên không mở lỗ ở mạn. 0 = không lọc.")]
    [SerializeField, Range(0f, 1f)] private float minNormalAlignment = 0.5f;

    [Tooltip("Bỏ qua vùng nằm sâu hơn khoảng này (m) phía sau điểm trúng theo hướng đạn " +
             "(vd. mạn bên kia, đáy tàu). Nên lớn hơn độ dày vỏ để lỗ xuyên được cả 2 lớp vỏ.")]
    [SerializeField] private float maxDepthBehindImpact = 0.3f;

    [Tooltip("Lùi điểm đầu tia đạn khoảng này (m) trước vị trí đạn lúc chạm collider: trong 1 bước physics " +
             "đạn có thể đã lọt qua vỏ. Thực tế dùng max(giá trị này, bán kính lỗ).")]
    [SerializeField] private float rayBacktrack = 0.5f;

    [Tooltip("Hệ số bán kính cho vụ nổ / đâm (Banger) so với đạn thường.")]
    [SerializeField] private float explosionRadiusMultiplier = 1.5f;

    [Tooltip("Vùng có diện tích (m², world) nhỏ hơn mức này (mảnh li ti do viền mask răng cưa) " +
             "luôn được lấp và không bị sát thương mở ra.")]
    [SerializeField] private float minDamageRegionArea = 0.001f;

    [Header("Deck Decals")]
    [Tooltip("Vùng quay lên trên (sàn tàu) không phải lỗ thật trên mesh hiển thị: MeshFilter giữ sàn nguyên vẹn " +
             "(tránh khe nứt khi cắt các tam giác sàn rất lớn), Source Mesh có lỗ sàn. Khi bị bắn, vùng này hiện " +
             "thành miếng 'lỗ cháy' màu tối nằm sát trên mặt sàn.")]
    [SerializeField] private bool upwardRegionsAsDecals;

    [Tooltip("Vật liệu của miếng lỗ trên sàn. Để trống sẽ dùng màu tối mặc định.")]
    [SerializeField] private Material decalMaterial;

    [Tooltip("Vùng có pháp tuyến (world) với y >= giá trị này được coi là sàn.")]
    [SerializeField, Range(0f, 1f)] private float decalMinUp = 0.7f;

    [Tooltip("Nâng miếng lỗ lên trên mặt sàn khoảng này (m) để không bị z-fighting.")]
    [SerializeField] private float decalOffset = 0.003f;

    [SerializeField] private bool drawHitGizmos;

    // ---- Dữ liệu vùng dùng chung --------------------------------------------
    // Mọi tàu cùng Source Mesh có cùng các vùng: tìm lỗ + lấp lỗ chỉ chạy 1 lần
    // cho mỗi mesh, các lần spawn sau chỉ đọc cache.

    private sealed class RegionData
    {
        public int id;
        public int submesh;
        public Vector3[] positions;
        public Vector2[] uv;
        public int[] triangles;
        public Vector3 normal;
        public Vector3 center;
        public float area;

        // AABB (local) của mảnh lấp, nới nhẹ để mảnh phẳng không có bề dày 0:
        // dùng loại nhanh khi bắn tia / đo bán kính trước khi xét từng tam giác.
        public Bounds bounds;
    }

    private sealed class SharedRegions
    {
        public readonly List<RegionData> regions = new List<RegionData>();
        public bool hasUV;
    }

    private static readonly Dictionary<(Mesh, float), SharedRegions> cache =
        new Dictionary<(Mesh, float), SharedRegions>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cache.Clear();
    }

    public static void ClearSharedCache()
    {
        cache.Clear();
    }

    // ---- Trạng thái của tàu này -----------------------------------------------

    private SharedRegions shared;

    // Vùng chưa bị phá: vùng thường thì hiện lớp lấp, miếng lỗ trên sàn thì ẩn.
    private bool[] intact;
    private bool[] isDecal;
    private bool[] damageable;
    private int[] vertexOffset;
    private int[] patchSlot;
    private readonly Dictionary<int, int> indexById = new Dictionary<int, int>();

    // Toàn bộ mảnh lấp của tàu gộp thành 1 mesh (mỗi vật liệu 1 submesh), miếng lỗ
    // trên sàn gộp thành 1 mesh khác: 1-3 draw call thay vì mỗi vùng 1 renderer.
    private Mesh patchMesh;
    private MeshRenderer patchRenderer;
    private int patchSlotCount;

    private Mesh decalMesh;
    private MeshRenderer decalRenderer;

    private bool dirty;
    private Material runtimeDecalMaterial;
    private readonly List<int> indexBuffer = new List<int>(4096);

    private Vector3 lastHitPoint;
    private float lastHitRadius;

    private void Awake()
    {
        MeshFilter sourceFilter = GetComponent<MeshFilter>();
        MeshRenderer sourceRenderer = GetComponent<MeshRenderer>();

        if (sourceMesh == null)
            sourceMesh = sourceFilter.sharedMesh;

        if (sourceMesh == null)
        {
            Debug.LogError("Source Mesh is missing!", this);
            return;
        }

        if (sourceRenderer == null)
        {
            Debug.LogError("MeshRenderer is missing!", this);
            return;
        }

        shared = GetSharedRegions(sourceMesh, weldTolerance);

        if (shared.regions.Count == 0)
        {
            Debug.LogWarning("No Fill Regions found.", this);
            return;
        }

        BuildInstance(sourceRenderer);
    }

    private void LateUpdate()
    {
        if (dirty)
            RebuildIndices();
    }

    // ---- Cache ------------------------------------------------------------------

    private static SharedRegions GetSharedRegions(Mesh mesh, float tolerance)
    {
        var key = (mesh, tolerance);
        if (cache.TryGetValue(key, out SharedRegions data))
            return data;

        data = new SharedRegions();
        cache[key] = data;

        Mesh temporary = MeshSurfacePatcher.FillClosedHoles(mesh, out List<FillRegion> regions, tolerance);
        SafeDestroy(temporary);

        if (regions == null || regions.Count == 0)
            return data;

        Vector3[] vertices = mesh.vertices;
        var sourceUV = new List<Vector2>();
        mesh.GetUVs(0, sourceUV);
        data.hasUV = sourceUV.Count == vertices.Length;

        foreach (FillRegion region in regions)
        {
            RegionData r = BuildRegionData(region, vertices, data.hasUV ? sourceUV : null);
            if (r != null)
                data.regions.Add(r);
        }

        Debug.Log($"Built {data.regions.Count} fill regions for '{mesh.name}' (shared).");
        return data;
    }

    private static RegionData BuildRegionData(FillRegion region, Vector3[] sourceVertices, List<Vector2> sourceUV)
    {
        if (region == null || region.triangleIndices == null || region.triangleIndices.Count < 3)
            return null;

        var map = new Dictionary<int, int>();
        var positions = new List<Vector3>();
        var uvs = new List<Vector4>();
        var triangles = new List<int>(region.triangleIndices.Count);

        foreach (int original in region.triangleIndices)
        {
            if (original < 0 || original >= sourceVertices.Length)
                return null;

            if (!map.TryGetValue(original, out int index))
            {
                index = positions.Count;
                map.Add(original, index);
                positions.Add(sourceVertices[original]);
                if (sourceUV != null)
                    uvs.Add(sourceUV[original]);
            }

            triangles.Add(index);
        }

        // Mảnh lấp là mặt phẳng: mọi đỉnh dùng pháp tuyến của mặt phẳng đó. Copy pháp
        // tuyến từ đỉnh viền thì một đỉnh trùng vị trí với mặt đứng (pháp tuyến nằm ngang)
        // làm cả chùm tam giác ear-clipping dùng chung đỉnh đó bị tô tối thành vệt.
        Vector3 areaVector = Vector3.zero;
        for (int i = 0; i + 2 < triangles.Count; i += 3)
            areaVector += Vector3.Cross(
                positions[triangles[i + 1]] - positions[triangles[i]],
                positions[triangles[i + 2]] - positions[triangles[i]]);

        if (sourceUV != null)
            FlattenInconsistentUVs(positions, triangles, uvs);

        Bounds bounds = new Bounds(positions[0], Vector3.zero);
        foreach (Vector3 p in positions)
            bounds.Encapsulate(p);

        var r = new RegionData
        {
            id = region.regionId,
            submesh = region.submeshIndex,
            positions = positions.ToArray(),
            triangles = triangles.ToArray(),
            normal = areaVector.sqrMagnitude > 1e-20f ? areaVector.normalized : Vector3.zero,
            center = bounds.center,
            area = areaVector.magnitude * 0.5f
        };

        Bounds padded = bounds;
        padded.Expand(Mathf.Max(bounds.size.magnitude * 0.01f, 1e-5f));
        r.bounds = padded;

        if (sourceUV != null)
        {
            r.uv = new Vector2[uvs.Count];
            for (int i = 0; i < uvs.Count; i++)
                r.uv[i] = uvs[i];
        }

        return r;
    }

    // ---- Dựng mesh gộp cho tàu này ---------------------------------------------

    private void BuildInstance(MeshRenderer sourceRenderer)
    {
        List<RegionData> regions = shared.regions;
        int count = regions.Count;

        intact = new bool[count];
        isDecal = new bool[count];
        damageable = new bool[count];
        vertexOffset = new int[count];
        patchSlot = new int[count];

        Matrix4x4 localToWorld = transform.localToWorldMatrix;
        Matrix4x4 normalMatrix = transform.worldToLocalMatrix.transpose;
        float det = Mathf.Abs(localToWorld.determinant);

        // Vật liệu của vỏ tàu -> slot submesh trong mesh gộp.
        Material[] sourceMaterials = sourceRenderer.sharedMaterials;
        var slotBySubmesh = new Dictionary<int, int>();
        var slotMaterials = new List<Material>();

        var patchPositions = new List<Vector3>();
        var patchNormals = new List<Vector3>();
        var patchUV = new List<Vector2>();
        var decalPositions = new List<Vector3>();
        var decalNormals = new List<Vector3>();

        for (int i = 0; i < count; i++)
        {
            RegionData r = regions[i];
            indexById[r.id] = i;
            intact[i] = true;

            Vector3 worldNormal = r.normal != Vector3.zero
                ? normalMatrix.MultiplyVector(r.normal).normalized
                : Vector3.zero;

            isDecal[i] = upwardRegionsAsDecals && worldNormal.y >= decalMinUp;

            // Diện tích world của mảnh phẳng qua biến đổi tuyến tính (kể cả scale không đều).
            float worldArea = r.normal != Vector3.zero
                ? det * normalMatrix.MultiplyVector(r.normal * r.area).magnitude
                : 0f;
            damageable[i] = worldArea >= minDamageRegionArea;

            if (isDecal[i])
            {
                vertexOffset[i] = decalPositions.Count;
                Vector3 lift = transform.InverseTransformVector(worldNormal * decalOffset);
                foreach (Vector3 p in r.positions)
                {
                    decalPositions.Add(p + lift);
                    decalNormals.Add(r.normal);
                }
            }
            else
            {
                if (!slotBySubmesh.TryGetValue(r.submesh, out int slot))
                {
                    slot = slotMaterials.Count;
                    slotBySubmesh.Add(r.submesh, slot);
                    slotMaterials.Add(r.submesh >= 0 && r.submesh < sourceMaterials.Length
                        ? sourceMaterials[r.submesh]
                        : null);
                }

                patchSlot[i] = slot;
                vertexOffset[i] = patchPositions.Count;
                patchPositions.AddRange(r.positions);
                for (int v = 0; v < r.positions.Length; v++)
                    patchNormals.Add(r.normal);
                if (shared.hasUV)
                    patchUV.AddRange(r.uv);
            }
        }

        patchSlotCount = slotMaterials.Count;

        if (patchPositions.Count > 0)
        {
            patchMesh = CreateMesh("FillPatches", patchPositions, patchNormals, shared.hasUV ? patchUV : null, patchSlotCount);
            patchRenderer = CreateChildRenderer("Runtime_Fill_Patches", patchMesh, slotMaterials.ToArray());
            patchRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            patchRenderer.receiveShadows = sourceRenderer.receiveShadows;
        }

        if (decalPositions.Count > 0)
        {
            decalMesh = CreateMesh("HoleDecals", decalPositions, decalNormals, null, 1);
            decalRenderer = CreateChildRenderer("Runtime_Hole_Decals", decalMesh, new[] { GetDecalMaterial() });
            decalRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        RebuildIndices();
    }

    private static Mesh CreateMesh(string name, List<Vector3> positions, List<Vector3> normals, List<Vector2> uv, int submeshCount)
    {
        var mesh = new Mesh { name = name };
        if (positions.Count > 65535)
            mesh.indexFormat = IndexFormat.UInt32;

        mesh.MarkDynamic();
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        if (uv != null)
            mesh.SetUVs(0, uv);
        mesh.subMeshCount = Mathf.Max(1, submeshCount);
        mesh.RecalculateBounds();
        return mesh;
    }

    private MeshRenderer CreateChildRenderer(string name, Mesh mesh, Material[] materials)
    {
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);
        child.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = child.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        return renderer;
    }

    // Dựng lại index buffer theo trạng thái các vùng (vertex buffer giữ nguyên).
    private void RebuildIndices()
    {
        dirty = false;

        if (shared == null)
            return;

        List<RegionData> regions = shared.regions;

        if (patchMesh != null)
        {
            bool any = false;

            for (int slot = 0; slot < patchSlotCount; slot++)
            {
                indexBuffer.Clear();

                for (int i = 0; i < regions.Count; i++)
                {
                    if (isDecal[i] || !intact[i] || patchSlot[i] != slot)
                        continue;

                    AppendTriangles(regions[i], vertexOffset[i]);
                }

                any |= indexBuffer.Count > 0;
                patchMesh.SetTriangles(indexBuffer, slot, false);
            }

            patchRenderer.enabled = any;
        }

        if (decalMesh != null)
        {
            indexBuffer.Clear();

            for (int i = 0; i < regions.Count; i++)
            {
                if (isDecal[i] && !intact[i])
                    AppendTriangles(regions[i], vertexOffset[i]);
            }

            decalMesh.SetTriangles(indexBuffer, 0, false);
            decalRenderer.enabled = indexBuffer.Count > 0;
        }
    }

    private void AppendTriangles(RegionData r, int offset)
    {
        int[] tris = r.triangles;
        for (int t = 0; t < tris.Length; t++)
            indexBuffer.Add(tris[t] + offset);
    }

    private Material GetDecalMaterial()
    {
        if (decalMaterial != null)
            return decalMaterial;

        if (runtimeDecalMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            runtimeDecalMaterial = new Material(shader) { name = "RuntimeHoleDecal" };
            Color dark = new Color(0.06f, 0.06f, 0.07f, 1f);
            if (runtimeDecalMaterial.HasProperty("_BaseColor"))
                runtimeDecalMaterial.SetColor("_BaseColor", dark);
            if (runtimeDecalMaterial.HasProperty("_Color"))
                runtimeDecalMaterial.SetColor("_Color", dark);
        }

        return runtimeDecalMaterial;
    }

    private const float UVFitTolerance = 0.01f;

    private static void FlattenInconsistentUVs(List<Vector3> positions, List<int> triangles, List<Vector4> uvs)
    {
        int n = positions.Count;
        if (n < 4)
            return;

        Vector3 normal = Vector3.zero;
        for (int i = 0; i + 2 < triangles.Count; i += 3)
            normal += Vector3.Cross(
                positions[triangles[i + 1]] - positions[triangles[i]],
                positions[triangles[i + 2]] - positions[triangles[i]]);

        if (normal.sqrMagnitude < 1e-20f)
            return;

        normal.Normalize();
        Vector3 axisX = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 axisY = Vector3.Cross(normal, axisX);
        Vector3 origin = positions[0];

        // Bình phương tối thiểu: uv = a*x + b*y + c (x, y trên mặt phẳng mảnh lấp).
        double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0;
        double sxu = 0, syu = 0, su = 0, sxv = 0, syv = 0, sv = 0;
        var px = new double[n];
        var py = new double[n];

        for (int i = 0; i < n; i++)
        {
            Vector3 d = positions[i] - origin;
            double x = Vector3.Dot(d, axisX), y = Vector3.Dot(d, axisY);
            double u = uvs[i].x, v = uvs[i].y;
            px[i] = x; py[i] = y;
            sxx += x * x; sxy += x * y; syy += y * y; sx += x; sy += y;
            sxu += x * u; syu += y * u; su += u;
            sxv += x * v; syv += y * v; sv += v;
        }

        if (!Solve3(sxx, sxy, sx, sxy, syy, sy, sx, sy, n, sxu, syu, su, out double au, out double bu, out double cu) ||
            !Solve3(sxx, sxy, sx, sxy, syy, sy, sx, sy, n, sxv, syv, sv, out double av, out double bv, out double cv))
            return;

        double maxResidual = 0;
        for (int i = 0; i < n; i++)
        {
            double ru = au * px[i] + bu * py[i] + cu - uvs[i].x;
            double rv = av * px[i] + bv * py[i] + cv - uvs[i].y;
            maxResidual = System.Math.Max(maxResidual, System.Math.Max(System.Math.Abs(ru), System.Math.Abs(rv)));
        }

        if (maxResidual <= UVFitTolerance)
            return;

        var us = new List<float>(n);
        var vs = new List<float>(n);
        for (int i = 0; i < n; i++) { us.Add(uvs[i].x); vs.Add(uvs[i].y); }
        us.Sort();
        vs.Sort();

        Vector4 flat = uvs[0];
        flat.x = us[n / 2];
        flat.y = vs[n / 2];

        for (int i = 0; i < n; i++)
            uvs[i] = flat;
    }

    // Giải hệ 3x3 (Cramer).
    private static bool Solve3(
        double a11, double a12, double a13,
        double a21, double a22, double a23,
        double a31, double a32, double a33,
        double b1, double b2, double b3,
        out double x, out double y, out double z)
    {
        double det = a11 * (a22 * a33 - a23 * a32) - a12 * (a21 * a33 - a23 * a31) + a13 * (a21 * a32 - a22 * a31);
        x = y = z = 0;

        if (System.Math.Abs(det) < 1e-12)
            return false;

        x = (b1 * (a22 * a33 - a23 * a32) - a12 * (b2 * a33 - a23 * b3) + a13 * (b2 * a32 - a22 * b3)) / det;
        y = (a11 * (b2 * a33 - a23 * b3) - b1 * (a21 * a33 - a23 * a31) + a13 * (a21 * b3 - b2 * a31)) / det;
        z = (a11 * (a22 * b3 - b2 * a32) - a12 * (a21 * b3 - b2 * a31) + b1 * (a21 * a32 - a22 * a31)) / det;
        return true;
    }

    // ---- API bật / tắt vùng ---------------------------------------------------------

    // visible = thân tàu nguyên vẹn ở vùng này.
    public void SetFillVisible(int regionId, bool visible)
    {
        if (!indexById.TryGetValue(regionId, out int i))
        {
            Debug.LogWarning($"Fill Region {regionId} not found!", this);
            return;
        }

        if (intact[i] == visible)
            return;

        intact[i] = visible;
        dirty = true;
    }

    public void EnableFill(int regionId)
    {
        SetFillVisible(regionId, true);
    }

    public void DisableFill(int regionId)
    {
        SetFillVisible(regionId, false);
    }

    public void EnableAllFill()
    {
        SetAll(true);
    }

    public void DisableAllFill()
    {
        SetAll(false);
    }

    private void SetAll(bool visible)
    {
        if (intact == null)
            return;

        for (int i = 0; i < intact.Length; i++)
            intact[i] = visible;

        RebuildIndices();
    }

    public int GetFillRegionCount()
    {
        return shared != null ? shared.regions.Count : 0;
    }

    // ---- Sát thương ---------------------------------------------------------------

    public float GetHitRadius(float damageRatio)
    {
        float t = Mathf.Clamp01(damageRatio / Mathf.Max(damageRatioForMaxRadius, 0.0001f));
        return Mathf.Lerp(minHitRadius, maxHitRadius, t);
    }

    public int ApplyHit(Vector3 worldPoint, float damageRatio)
    {
        return ApplyHit(worldPoint, Vector3.zero, damageRatio, false);
    }

    public int ApplyHit(Vector3 sourcePoint, Vector3 hitDirection, float damageRatio, bool isExplosion)
    {
        if (shared == null || intact == null)
            return 0;

        float radius = GetHitRadius(damageRatio);
        if (isExplosion)
            radius *= explosionRadiusMultiplier;

        bool hasDirection = hitDirection.sqrMagnitude > 0.000001f;
        Vector3 impact = hasDirection ? GetImpactPoint(sourcePoint, hitDirection) : sourcePoint;
        int hitRegion = -1;

        if (hasDirection)
        {
            Vector3 dir = hitDirection.normalized;

            // 1) Bắn tia thật vào các tam giác của mảnh lấp: điểm trúng nằm đúng trên bề mặt.
            // 2) Tia trượt hết (trúng phần vỏ không có vùng, hoặc chỉ sượt hộp collider):
            //    lấy điểm trên đường bay gần vùng đầu tiên nó đi qua (cách cũ).
            if (TryRaycastRegions(sourcePoint, dir, Mathf.Max(rayBacktrack, radius),
                    out Vector3 surfacePoint, out hitRegion))
                impact = surfacePoint;
            else if (TryFindRayImpact(sourcePoint, dir, radius, out Vector3 rayImpact))
                impact = rayImpact;
        }

        return OpenRegions(impact, radius, hasDirection ? hitDirection : Vector3.zero, hitRegion);
    }

    // Tia đạn (world) giao với tam giác của các vùng còn nguyên. Làm trong không gian
    // local của mesh: chỉ đổi tia 1 lần thay vì nhân ma trận cho từng đỉnh. Hướng tia
    // local = toLocal * dir (không chuẩn hóa) nên tham số t vẫn là khoảng cách world.
    private bool TryRaycastRegions(Vector3 origin, Vector3 dir, float backtrack,
        out Vector3 hitPoint, out int hitRegion)
    {
        hitPoint = origin;
        hitRegion = -1;

        Matrix4x4 toLocal = transform.worldToLocalMatrix;
        Matrix4x4 normalMatrix = toLocal.transpose;
        Vector3 worldStart = origin - dir * backtrack;
        Vector3 localStart = toLocal.MultiplyPoint3x4(worldStart);
        Vector3 localDir = toLocal.MultiplyVector(dir);

        float bestT = float.MaxValue;
        List<RegionData> regions = shared.regions;

        for (int i = 0; i < regions.Count; i++)
        {
            if (!damageable[i] || !intact[i])
                continue;

            RegionData r = regions[i];

            // Loại nhanh: tia không đi qua AABB của vùng (hoặc đi qua xa hơn điểm trúng đã có).
            if (!RayHitsBounds(localStart, localDir, r.bounds, bestT))
                continue;

            if (!FacesHit(r, dir, normalMatrix))
                continue;

            Vector3[] p = r.positions;
            int[] tris = r.triangles;

            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                if (RayTriangle(localStart, localDir, p[tris[t]], p[tris[t + 1]], p[tris[t + 2]], out float dist) &&
                    dist < bestT)
                {
                    bestT = dist;
                    hitRegion = i;
                }
            }
        }

        if (hitRegion < 0)
            return false;

        hitPoint = worldStart + dir * bestT;
        return true;
    }

    // Slab test: tia o + d*t (0 <= t <= maxT) có đi qua hộp không.
    private static bool RayHitsBounds(Vector3 o, Vector3 d, Bounds b, float maxT)
    {
        Vector3 min = b.min;
        Vector3 max = b.max;
        float tMin = 0f;
        float tMax = maxT;

        for (int a = 0; a < 3; a++)
        {
            if (Mathf.Abs(d[a]) < 1e-12f)
            {
                if (o[a] < min[a] || o[a] > max[a])
                    return false;
                continue;
            }

            float inv = 1f / d[a];
            float t1 = (min[a] - o[a]) * inv;
            float t2 = (max[a] - o[a]) * inv;
            if (t1 > t2)
            {
                float tmp = t1;
                t1 = t2;
                t2 = tmp;
            }

            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax)
                return false;
        }

        return true;
    }

    // Möller–Trumbore, 2 mặt (đạn có thể đi vào từ mặt trong của vỏ).
    private static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0f;
        Vector3 e1 = b - a;
        Vector3 e2 = c - a;
        Vector3 p = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, p);

        if (det > -1e-20f && det < 1e-20f)
            return false;

        float inv = 1f / det;
        Vector3 s = o - a;
        float u = Vector3.Dot(s, p) * inv;
        if (u < 0f || u > 1f)
            return false;

        Vector3 q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(d, q) * inv;
        if (v < 0f || u + v > 1f)
            return false;

        t = Vector3.Dot(e2, q) * inv;
        return t >= 0f;
    }

    // Nguồn nằm ngoài tàu (vd. Banger nổ cách thân tàu vài mét): lấy điểm tia từ nguồn
    // theo hướng sát thương đi vào bounding box của mesh, để lỗ mở đúng mặt bị đâm.
    private Vector3 GetImpactPoint(Vector3 sourcePoint, Vector3 hitDirection)
    {
        if (sourceMesh == null)
            return sourcePoint;

        Bounds b = sourceMesh.bounds;
        Vector3 localSource = transform.InverseTransformPoint(sourcePoint);

        if (b.Contains(localSource))
            return sourcePoint;

        Vector3 localDir = transform.InverseTransformDirection(hitDirection).normalized;

        if (b.IntersectRay(new Ray(localSource, localDir), out float distance))
            return transform.TransformPoint(localSource + localDir * distance);

        // Tia trượt bounding box: lấy điểm gần nhất trên box.
        return transform.TransformPoint(b.ClosestPoint(localSource));
    }

    // Điểm trên đường bay (origin + dir * t) gần vùng đầu tiên mà đường bay đi qua
    // trong phạm vi radius. Cho phép t âm tới -radius vì vị trí đạn lúc chạm
    // collider có thể đã nằm sau bề mặt.
    private bool TryFindRayImpact(Vector3 origin, Vector3 dir, float radius, out Vector3 impact)
    {
        impact = origin;

        Matrix4x4 toWorld = transform.localToWorldMatrix;
        Matrix4x4 normalMatrix = transform.worldToLocalMatrix.transpose;
        float radiusSqr = radius * radius;
        float bestT = float.MaxValue;
        List<RegionData> regions = shared.regions;

        for (int i = 0; i < regions.Count; i++)
        {
            if (!damageable[i] || !intact[i] || !FacesHit(regions[i], dir, normalMatrix))
                continue;

            foreach (Vector3 p in regions[i].positions)
            {
                Vector3 w = toWorld.MultiplyPoint3x4(p);
                float along = Vector3.Dot(w - origin, dir);

                if (along < -radius || along >= bestT)
                    continue;

                if ((w - (origin + dir * along)).sqrMagnitude <= radiusSqr)
                    bestT = along;
            }
        }

        if (bestT == float.MaxValue)
            return false;

        impact = origin + dir * bestT;
        return true;
    }

    // Mặt phẳng của vùng có quay về hướng đạn không (cả 2 phía: mặt ngoài lẫn mặt trong vỏ).
    private bool FacesHit(RegionData r, Vector3 worldDir, Matrix4x4 normalMatrix)
    {
        if (minNormalAlignment <= 0f || r.normal == Vector3.zero)
            return true;

        // Pháp tuyến qua scale không đều: nhân với ma trận nghịch đảo chuyển vị.
        Vector3 n = normalMatrix.MultiplyVector(r.normal).normalized;
        return Mathf.Abs(Vector3.Dot(n, worldDir)) >= minNormalAlignment;
    }

    public int OpenRegionsInRadius(Vector3 worldPoint, float worldRadius)
    {
        return OpenRegionsInRadius(worldPoint, worldRadius, Vector3.zero);
    }

    public int OpenRegionsInRadius(Vector3 worldPoint, float worldRadius, Vector3 hitDirection)
    {
        return OpenRegions(worldPoint, worldRadius, hitDirection, -1);
    }

    // hitRegion: vùng mà tia đạn trúng trực tiếp (-1 = không có) - luôn được mở.
    private int OpenRegions(Vector3 worldPoint, float worldRadius, Vector3 hitDirection, int hitRegion)
    {
        if (shared == null || intact == null)
            return 0;

        lastHitPoint = worldPoint;
        lastHitRadius = worldRadius;

        bool filter = hitDirection.sqrMagnitude > 0.000001f;
        Vector3 dir = filter ? hitDirection.normalized : Vector3.zero;
        Matrix4x4 toWorld = transform.localToWorldMatrix;
        Matrix4x4 normalMatrix = transform.worldToLocalMatrix.transpose;

        int opened = 0;
        float radiusSqr = worldRadius * worldRadius;
        float snapSqr = snapDistance > 0f ? snapDistance * snapDistance : 0f;

        if (hitRegion >= 0 && intact[hitRegion])
        {
            intact[hitRegion] = false;
            opened++;
        }

        int nearest = -1;
        float nearestSqr = float.MaxValue;
        List<RegionData> regions = shared.regions;

        for (int i = 0; i < regions.Count; i++)
        {
            if (!damageable[i] || !intact[i])
                continue;

            RegionData r = regions[i];

            // Loại nhanh bằng AABB world (cận dưới của khoảng cách): xa hơn cả bán kính lẫn
            // khoảng snap / vùng gần nhất đã có thì không cần xét từng tam giác.
            float lowerBound = WorldBounds(r.bounds, toWorld).SqrDistance(worldPoint);
            float limit = opened > 0 ? radiusSqr : Mathf.Max(radiusSqr, Mathf.Min(snapSqr, nearestSqr));
            if (lowerBound > limit)
                continue;

            if (filter)
            {
                if (!FacesHit(r, dir, normalMatrix))
                    continue;

                // Nằm quá sâu sau điểm trúng theo hướng đạn: mạn bên kia, đáy tàu...
                float depth = Vector3.Dot(toWorld.MultiplyPoint3x4(r.center) - worldPoint, dir);
                if (depth > maxDepthBehindImpact)
                    continue;
            }

            float dSqr = ClosestDistanceSqr(r, worldPoint, toWorld);

            if (dSqr <= radiusSqr)
            {
                intact[i] = false;
                opened++;
            }
            else if (dSqr < nearestSqr)
            {
                nearestSqr = dSqr;
                nearest = i;
            }
        }

        if (opened == 0 &&
            nearest >= 0 &&
            snapDistance > 0f &&
            nearestSqr <= snapDistance * snapDistance)
        {
            intact[nearest] = false;
            opened = 1;
        }

        if (opened > 0)
            dirty = true;

        return opened;
    }

    // AABB world bao AABB local qua ma trận (kể cả xoay / scale không đều).
    private static Bounds WorldBounds(Bounds local, Matrix4x4 m)
    {
        Vector3 e = local.extents;
        Vector3 worldExtents = new Vector3(
            Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
            Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
            Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);
        return new Bounds(m.MultiplyPoint3x4(local.center), worldExtents * 2f);
    }

    // Khoảng cách (bình phương, world) từ điểm tới BỀ MẶT của vùng (điểm gần nhất trên
    // các tam giác), không chỉ tới đỉnh: điểm trúng giữa 1 mảnh lấp lớn có khoảng cách 0.
    // Tính trong world vì scale không đều làm méo khoảng cách local.
    private static float ClosestDistanceSqr(RegionData r, Vector3 worldPoint, Matrix4x4 toWorld)
    {
        float best = float.MaxValue;
        Vector3[] p = r.positions;
        int[] tris = r.triangles;

        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            Vector3 a = toWorld.MultiplyPoint3x4(p[tris[t]]);
            Vector3 b = toWorld.MultiplyPoint3x4(p[tris[t + 1]]);
            Vector3 c = toWorld.MultiplyPoint3x4(p[tris[t + 2]]);

            float d = (ClosestPointOnTriangle(worldPoint, a, b, c) - worldPoint).sqrMagnitude;
            if (d < best)
            {
                best = d;
                if (best <= 0f)
                    break;
            }
        }

        return best;
    }

    // Ericson, Real-Time Collision Detection 5.1.5.
    private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            return a + ab * (d1 / (d1 - d3));

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            return a + ac * (d2 / (d2 - d6));

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        float denom = 1f / (va + vb + vc);
        float v = vb * denom, w = vc * denom;
        return a + ab * v + ac * w;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawHitGizmos || lastHitRadius <= 0f)
            return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(lastHitPoint, lastHitRadius);
    }

    private void OnDestroy()
    {
        SafeDestroy(patchMesh);
        SafeDestroy(decalMesh);
        SafeDestroy(runtimeDecalMaterial);

        if (patchRenderer != null)
            SafeDestroy(patchRenderer.gameObject);
        if (decalRenderer != null)
            SafeDestroy(decalRenderer.gameObject);
    }

    private static void SafeDestroy(Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }
}
