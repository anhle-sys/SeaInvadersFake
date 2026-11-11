using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class BoatAimController : MonoBehaviour
{
    private const int GizmoArcSegments = 8;

    [Header("References")]
    [SerializeField] private BoatTargetController targetController;
    [SerializeField] private Transform[] cannons;

    [Header("Aim Cone")]
    [Tooltip("Nửa góc của hình nón (độ), đo từ hướng nòng pháo. Được ghi đè bằng GunnerData.fireArcHalfAngle qua Configure.")]
    [FormerlySerializedAs("aimTolerance")]
    [SerializeField] private float coneHalfAngle = 10f;

    [Tooltip("Chiều dài hình nón. Được ghi đè bằng attackRange × 1.1 qua Configure.")]
    [FormerlySerializedAs("raycastDistance")]
    [SerializeField] private float coneRange = 20f;

    private readonly List<Transform> readyCannons = new List<Transform>();
    private Transform cachedTarget;
    private Collider cachedCollider;

    public IReadOnlyList<Transform> ReadyCannons => readyCannons;
    public bool IsAimingAtTarget => readyCannons.Count > 0;

    public void Configure(float attackRange, float halfAngle)
    {
        coneRange = attackRange * 1.1f;
        coneHalfAngle = halfAngle;
    }

    private void Update()
    {
        readyCannons.Clear();
        if (!GameController.BattleStarted)
            return;

        Transform target = targetController.CurrentBoatTarget;
        if (target == null)
            return;

        // Chỉ GetComponent khi đổi mục tiêu, không phải mỗi frame
        if (target != cachedTarget)
        {
            cachedTarget = target;
            cachedCollider = target.GetComponentInChildren<Collider>();
        }

        foreach (Transform cannon in cannons)
        {
            if (cannon != null && IsInCone(cannon, target))
                readyCannons.Add(cannon);
        }
    }

    // Hình nón nằm trên mặt phẳng ngang: đỉnh ở nòng pháo, trục theo hướng nòng.
    // Địch nằm trong nón khi lệch hướng <= coneHalfAngle và khoảng cách (tới mép collider) <= coneRange.
    private bool IsInCone(Transform cannon, Transform target)
    {
        Vector3 origin = cannon.position;

        Vector3 forward = cannon.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            return false;

        // Khoảng cách tính tới điểm gần nhất của collider để tàu to không bị tính là ở xa
        Vector3 nearestPoint = cachedCollider != null ? cachedCollider.ClosestPoint(origin) : target.position;
        Vector3 toNearest = nearestPoint - origin;
        toNearest.y = 0f;
        if (toNearest.sqrMagnitude > coneRange * coneRange)
            return false;

        // Góc tính theo tâm tàu địch, nới thêm nửa bề rộng góc của thân tàu địch:
        // chỉ cần một phần thân tàu nằm trong nón là bắn được (trước đây tâm phải lọt nón -> thấy địch mà không bắn)
        Vector3 toTarget = target.position - origin;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;
        if (distance < 0.03f)
            return true;

        float hullAngle = 0f;
        if (cachedCollider != null)
        {
            Vector3 ext = cachedCollider.bounds.extents;
            float hullRadius = Mathf.Min(ext.x, ext.z);   // lấy cạnh ngắn: không nới quá tay với tàu dài
            hullAngle = Mathf.Atan2(hullRadius, distance) * Mathf.Rad2Deg;
        }

        return Vector3.Angle(forward, toTarget) <= coneHalfAngle + hullAngle;
    }

    private void OnDrawGizmos()
    {
        if (cannons == null)
            return;

        foreach (Transform cannon in cannons)
        {
            if (cannon == null) continue;

            Vector3 origin = cannon.position;
            Vector3 forward = cannon.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) continue;
            forward.Normalize();

            Gizmos.color = readyCannons.Contains(cannon) ? Color.green : Color.red;

            // Hai cạnh của nón + cung tròn ở đầu nón
            Vector3 prev = origin + Quaternion.AngleAxis(-coneHalfAngle, Vector3.up) * forward * coneRange;
            Gizmos.DrawLine(origin, prev);
            for (int i = 1; i <= GizmoArcSegments; i++)
            {
                float a = Mathf.Lerp(-coneHalfAngle, coneHalfAngle, i / (float)GizmoArcSegments);
                Vector3 next = origin + Quaternion.AngleAxis(a, Vector3.up) * forward * coneRange;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
            Gizmos.DrawLine(origin, prev);
        }
    }
}