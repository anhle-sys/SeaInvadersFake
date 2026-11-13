using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cầu nối giữa sát thương đạn (BoatHealth) và hệ destruction.
/// Đạn chỉ biết vị trí/hướng bay (trigger), còn DestructionController cần điểm trúng
/// nằm trên bề mặt mesh + pháp tuyến. Script này bắn tia vào mesh hiện tại để lấy 2 giá trị đó
/// rồi gọi DamageReceiver. Đặt cùng GameObject với DestructionController/DamageReceiver.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DamageReceiver))]
[RequireComponent(typeof(DestructionController))]
public class ProjectileDestructionBridge : MonoBehaviour
{
    [SerializeField] private DamageReceiver damageReceiver;
    [SerializeField] private DestructionController destructionController;

    [Header("Raycast")]
    [Tooltip("Lùi điểm bắt đầu tia về phía sau đạn (m) để không bắt đầu bên trong mesh.")]
    [SerializeField] private float rayBackOffset = 1.5f;
    [SerializeField] private float rayMaxDistance = 25f;

    [Header("Performance")]
    [Tooltip("Khoảng cách tối thiểu (giây) giữa 2 lần khoét mesh. Mỗi lần khoét dựng lại toàn bộ mesh nên không nên chạy mỗi viên đạn.")]
    [SerializeField] private float minInterval = 0.2f;
    [Tooltip("Vượt số tam giác này thì ngừng khoét thêm (mỗi lần khoét làm mesh nhiều tam giác hơn).")]
    [SerializeField] private int maxTriangles = 80000;

    private float nextAllowedTime;

    private Mesh cachedMesh;
    private Vector3[] vertices;
    private int[] triangles;
    private int triCount;
    private readonly List<Vector3> vertBuf = new List<Vector3>();
    private readonly List<int> idxBuf = new List<int>();

    [Tooltip("Khoảng cách tối thiểu (giây) giữa 2 lần khoét của MỌI tàu cộng lại. Chặn việc nhiều tàu cùng khoét trong vài frame liên tiếp gây giật.")]
    [SerializeField] private float globalMinInterval = 0.04f;

    private static float s_nextGlobalTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_nextGlobalTime = 0f; }

    private void Awake()
    {
        if (damageReceiver == null) damageReceiver = GetComponent<DamageReceiver>();
        if (destructionController == null) destructionController = GetComponent<DestructionController>();
    }

    /// <summary>Trả về true nếu mesh thực sự bị phá.</summary>
    public bool Apply(float damage, Vector3 sourcePosition, Vector3 travelDirection)
    {
        if (damageReceiver == null || destructionController == null)
            return false;

        if (Time.time < nextAllowedTime)
            return false;

        if (Time.time < s_nextGlobalTime)
            return false;
        s_nextGlobalTime = Time.time + globalMinInterval;

        Mesh mesh = destructionController.CurrentMesh;
        if (mesh == null)
            return false;

        if (CountTriangles(mesh) >= maxTriangles)
            return false;

        EnsureCache(mesh);

        Vector3 dir = travelDirection.sqrMagnitude > 0.0001f
            ? travelDirection.normalized
            : (transform.position - sourcePosition).normalized;

        if (!TryRaycast(sourcePosition - dir * rayBackOffset, dir, out Vector3 point, out Vector3 normal))
        {
            // Dự phòng: ngắm vào tâm mesh (vd. vụ nổ / đạn trúng góc hộp va chạm nhưng trượt thân tàu)
            Vector3 center = transform.TransformPoint(mesh.bounds.center);
            Vector3 toCenter = center - sourcePosition;
            if (toCenter.sqrMagnitude < 0.0001f)
                return false;

            dir = toCenter.normalized;
            if (!TryRaycast(sourcePosition - dir * rayBackOffset, dir, out point, out normal))
                return false;
        }

        nextAllowedTime = Time.time + minInterval;
        return damageReceiver.TakeDamageAtHit(damage, point, normal);
    }

    private static int CountTriangles(Mesh mesh)
    {
        int count = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
            if (mesh.GetTopology(i) == MeshTopology.Triangles)
                count += (int)mesh.GetIndexCount(i) / 3;
        return count;
    }

private void EnsureCache(Mesh mesh)
    {
        if (mesh == cachedMesh)
            return;

        cachedMesh = mesh;

        // Dùng buffer tái sử dụng thay cho mesh.vertices / GetTriangles (mỗi lần cấp phát mảng mới -> GC).
        mesh.GetVertices(vertBuf);
        int vCount = vertBuf.Count;
        if (vertices == null || vertices.Length < vCount)
            vertices = new Vector3[Mathf.NextPowerOfTwo(Mathf.Max(1, vCount))];
        vertBuf.CopyTo(vertices, 0);

        int total = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
            if (mesh.GetTopology(s) == MeshTopology.Triangles)
                total += (int)mesh.GetIndexCount(s);

        if (triangles == null || triangles.Length < total)
            triangles = new int[Mathf.NextPowerOfTwo(Mathf.Max(1, total))];

        int offset = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            if (mesh.GetTopology(s) != MeshTopology.Triangles)
                continue;

            mesh.GetTriangles(idxBuf, s);
            idxBuf.CopyTo(triangles, offset);
            offset += idxBuf.Count;
        }
        triCount = offset;
    }

    // Möller–Trumbore trong không gian local (hướng không chuẩn hóa nên t vẫn tính theo mét thế giới).
    private bool TryRaycast(Vector3 worldOrigin, Vector3 worldDir, out Vector3 point, out Vector3 normal)
    {
        point = default;
        normal = default;

        if (vertices == null || triangles == null)
            return false;

        Vector3 o = transform.InverseTransformPoint(worldOrigin);
        Vector3 d = transform.InverseTransformVector(worldDir);

        // Loại nhanh: tia không chạm bounding box của mesh thì khỏi duyệt toàn bộ tam giác.
        if (!cachedMesh.bounds.IntersectRay(new Ray(o, d)))
            return false;

        float bestT = rayMaxDistance;
        int bestTri = -1;

        for (int i = 0; i + 2 < triCount; i += 3)
        {
            Vector3 v0 = vertices[triangles[i]];
            Vector3 e1 = vertices[triangles[i + 1]] - v0;
            Vector3 e2 = vertices[triangles[i + 2]] - v0;

            Vector3 p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (det > -1e-9f && det < 1e-9f)
                continue;

            float inv = 1f / det;
            Vector3 tv = o - v0;
            float u = Vector3.Dot(tv, p) * inv;
            if (u < 0f || u > 1f)
                continue;

            Vector3 q = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f)
                continue;

            float t = Vector3.Dot(e2, q) * inv;
            if (t < 0f || t >= bestT)
                continue;

            // Chỉ lấy mặt hướng về phía đạn (mặt ngoài)
            Vector3 n = Vector3.Cross(e1, e2);
            if (Vector3.Dot(n, d) >= 0f)
                continue;

            bestT = t;
            bestTri = i;
        }

        if (bestTri < 0)
            return false;

        Vector3 a = vertices[triangles[bestTri]];
        Vector3 b = vertices[triangles[bestTri + 1]];
        Vector3 c = vertices[triangles[bestTri + 2]];
        Vector3 localNormal = Vector3.Cross(b - a, c - a);

        point = transform.TransformPoint(o + d * bestT);
        normal = transform.worldToLocalMatrix.transpose.MultiplyVector(localNormal).normalized;
        return true;
    }
}
