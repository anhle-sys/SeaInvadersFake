using UnityEngine;

/// <summary>
/// Cảm biến lái của tàu (class thường, do BoatMovementController sở hữu):
/// - Né tàu cùng phe ở gần (quét mỗi AvoidanceInterval giây).
/// - Quét phía trước mũi tàu để phanh / né đảo, đá.
/// </summary>
public class BoatSteeringSensors
{
    private const float AvoidanceInterval = 0.1f;
    private const float ObstacleLookAhead = 1.2f;   // nhìn trước bao nhiêu giây theo tốc độ hiện tại

    private readonly Rigidbody rb;
    private readonly Transform transform;
    private readonly BoatTargetController teamSource;
    private readonly BoxCollider hull;
    private readonly int boatMask;

    private readonly Collider[] avoidanceHits = new Collider[16];
    private readonly RaycastHit[] obstacleHits = new RaycastHit[8];
    private float avoidanceTimer;

    /// <summary>Hướng né tàu đồng đội (độ dài 0..1), cập nhật định kỳ.</summary>
    public Vector3 Avoidance { get; private set; }

    public BoatSteeringSensors(Rigidbody rb)
    {
        this.rb = rb;
        transform = rb.transform;
        teamSource = rb.GetComponent<BoatTargetController>();
        hull = rb.GetComponent<BoxCollider>();
        boatMask = LayerMask.GetMask("Boat");
    }

    public void UpdateAvoidance(BoatHandlingProfile p, float dt)
    {
        if (p.avoidanceRadius <= 0f || teamSource == null)
        {
            Avoidance = Vector3.zero;
            return;
        }

        avoidanceTimer -= dt;
        if (avoidanceTimer > 0f)
            return;
        avoidanceTimer = AvoidanceInterval;

        Vector3 pos = rb.position;
        Vector3 forward = rb.rotation * Vector3.forward;
        int count = Physics.OverlapSphereNonAlloc(pos, p.avoidanceRadius, avoidanceHits, boatMask,
                                                  QueryTriggerInteraction.Ignore);
        Vector3 sum = Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            Collider col = avoidanceHits[i];
            if (col.attachedRigidbody == rb)
                continue;

            // Chỉ tránh tàu cùng phe: tàu địch là mục tiêu (Gunner lượn quanh, Banger lao vào)
            BoatTargetController other = col.GetComponentInParent<BoatTargetController>();
            if (other == null || other.Team != teamSource.Team)
                continue;

            Vector3 away = pos - col.ClosestPoint(pos);
            away.y = 0f;
            float dist = away.magnitude;
            if (dist < 0.01f)
            {
                away = pos - col.transform.position;
                away.y = 0f;
                dist = away.magnitude;
                if (dist < 0.01f) continue;
            }

            // Bỏ qua tàu ở phía sau: tàu sau tự tránh tàu trước
            if (Vector3.Dot(forward, -away / dist) < -0.2f)
                continue;

            sum += away / dist * (1f - Mathf.Clamp01(dist / p.avoidanceRadius));
        }

        Avoidance = Vector3.ClampMagnitude(sum, 1f);
    }

    /// <summary>
    /// Quét hộp bằng chiều ngang thân tàu về phía trước. Trả về tốc độ tối đa để kịp dừng trước
    /// vật cản (phanh đều theo brakeDecel), và hướng lái né khi vật cản ở chéo phía trước.
    /// Vật cản chính diện thì chỉ phanh, không lái, để tàu không bị lật qua lật lại.
    /// </summary>
    public float SenseObstacle(BoatHandlingProfile p, float speed, float brakeDecel, out Vector3 steer)
    {
        steer = Vector3.zero;
        if (p.obstacleMask.value == 0 || hull == null)
            return float.MaxValue;

        Vector3 forward = rb.rotation * Vector3.forward;
        Vector3 right = rb.rotation * Vector3.right;
        Vector3 scale = transform.lossyScale;
        float halfLength = hull.size.z * 0.5f * scale.z;
        Vector3 halfExtents = new Vector3(hull.size.x * 0.5f * scale.x, hull.size.y * 0.25f * scale.y, 0.1f);
        Vector3 origin = transform.TransformPoint(hull.center);
        float range = halfLength + p.obstacleMargin + Mathf.Max(speed, 2f) * ObstacleLookAhead;

        int count = Physics.BoxCastNonAlloc(origin, halfExtents, forward, obstacleHits, rb.rotation,
                                            range, p.obstacleMask, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        Vector3 normal = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = obstacleHits[i];
            if (h.distance <= 0f) continue;                 // bắt đầu bên trong vật thể: bỏ qua
            if (h.rigidbody == rb) continue;                // chính mình
            if (h.collider.GetComponentInParent<BoatTargetController>() != null) continue; // tàu khác: đã có avoidance
            if (h.distance < nearest) { nearest = h.distance; normal = h.normal; }
        }

        if (nearest == float.MaxValue)
            return float.MaxValue;

        float free = Mathf.Max(nearest - halfLength - p.obstacleMargin, 0f);
        float limit = Mathf.Sqrt(2f * brakeDecel * 0.8f * free);

        normal.y = 0f;
        float side = Vector3.Dot(normal.normalized, right);
        if (normal.sqrMagnitude > 0.0001f && Mathf.Abs(side) > 0.15f)
        {
            float proximity = 1f - Mathf.Clamp01(nearest / range);
            steer = right * Mathf.Sign(side) * proximity * p.obstacleSteerWeight;
        }

        return limit;
    }
}
