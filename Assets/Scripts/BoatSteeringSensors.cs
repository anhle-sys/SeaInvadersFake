using UnityEngine;

public class BoatSteeringSensors
{
    private const float ObstacleLookAhead = 1.2f;   // nhìn trước bao nhiêu giây theo tốc độ hiện tại
    private const float BlockedTolerance = 0.3f;    // khoảng trống còn lại dưới mức này coi như đã bị chặn
    private const float AllyLaneMargin = 0.4f;      // đồng đội lệch khỏi thân tàu dưới mức này vẫn coi là nằm trên đường chạy
    private const float AllyGap = 0.4f;             // khoảng trống giữ lại trước mũi khi nhường đồng đội
    private const float AllyLookAhead = 0.8f;       // nhìn trước đồng đội trên đường chạy bao nhiêu giây theo tốc độ tiếp cận
    private const float SideBlockRadius = 0.6f;     // tàu đồng đội trong (avoidanceRadius × hệ số này) bên mạn thì chắn hướng quay

    private readonly Rigidbody rb;
    private readonly Transform transform;
    private readonly BoatTargetController teamSource;
    private readonly BoxCollider hull;
    private readonly int boatMask;

    private const float PassHoldSteer = 0.6f;       // độ bẻ lái giữ lại khi đã chốt mạn vượt nhưng đường tạm trống
    private const float PassMemoryRange = 3f;
    private const float PassClearance = 1f;         // tách ngang thêm khoảng này khỏi đường chạy là coi như đã vượt xong
    private const float CrossingSpeed = 1f;         // đồng đội chạy ngang nhanh hơn mức này thì coi là đang băng qua       // đồng đội đã chốt mạn vượt còn trong (tầm nhìn + khoảng này) thì vẫn nhớ

    private readonly Collider[] avoidanceHits = new Collider[16];
    private Collider passCollider;                  // đồng đội đang vòng qua
    private float passSide;                         // +1: vượt sang phải, -1: sang trái
    private readonly RaycastHit[] obstacleHits = new RaycastHit[8];

    public Vector3 Avoidance { get; private set; } //Hướng né tàu đồng đội

    // Mức bị tàu đồng đội chắn bên mạn: > 0 = bên phải, < 0 = bên trái (0..1 mỗi bên)
    public float RightBlock { get; private set; }
    public float LeftBlock { get; private set; }

    // Khoảng trống từ mũi tới đồng đội nằm trên đường chạy phía trước (MaxValue = không có)
    public float AllyAheadFree { get; private set; } = float.MaxValue;

    // Hướng bẻ lái để vòng qua đồng đội chắn trước mũi (ngang thân tàu, mạnh dần khi càng gần)
    public Vector3 AllyAheadSteer { get; private set; }

    // Có đồng đội sát sau đuôi (không lùi được)
    public bool AllyBehind { get; private set; }

    public bool BlockedAhead { get; private set; } //Mũi tàu đã sát vật cản phía trước

    public float ObstacleDistance { get; private set; } = float.MaxValue; //Khoảng cách từ tâm thân tàu tới vật cản gần nhất phía trước

    public BoatSteeringSensors(Rigidbody rb)
    {
        this.rb = rb;
        transform = rb.transform;
        teamSource = rb.GetComponent<BoatTargetController>();
        hull = rb.GetComponent<BoxCollider>();
        boatMask = LayerMask.GetMask("Boat");
    }

    public void ResetPass() => passCollider = null;

    public void UpdateAvoidance(BoatHandlingProfile p, float speed)
    {
        AllyAheadFree = float.MaxValue;
        AllyAheadSteer = Vector3.zero;
        AllyBehind = false;
        if (p.avoidanceRadius <= 0f || teamSource == null)
        {
            Avoidance = Vector3.zero;
            RightBlock = LeftBlock = 0f;
            return;
        }

        // Chạy mỗi bước vật lý: khoảng trống trước mũi dùng để phanh, dữ liệu cũ sẽ phanh trễ
        Vector3 pos = rb.position;
        Vector3 forward = rb.rotation * Vector3.forward;
        Vector3 right = rb.rotation * Vector3.right;
        float sideRadius = p.avoidanceRadius * SideBlockRadius;
        float rightBlock = 0f, leftBlock = 0f;

        Vector3 scale = transform.lossyScale;
        float halfLength = hull != null ? hull.size.z * 0.5f * scale.z : 1f;
        float halfWidth = hull != null ? hull.size.x * 0.5f * scale.x : 0.5f;
        Vector3 bow = pos + forward * halfLength;
        Vector3 stern = pos - forward * halfLength;

        // Tầm nhìn đồng đội trước mũi tăng theo tốc độ để kịp vòng qua thay vì phải phanh chết
        float laneRange = Mathf.Max(speed, 0f) * AllyLookAhead + AllyGap + 2f;
        float queryRadius = Mathf.Max(p.avoidanceRadius, halfLength + laneRange + halfWidth);
        Vector3 velocity = rb.linearVelocity;

        int count = Physics.OverlapSphereNonAlloc(pos, queryRadius, avoidanceHits, boatMask,
                                                  QueryTriggerInteraction.Ignore);
        Vector3 sum = Vector3.zero;
        float aheadLateral = 0f;
        float aheadLateralSpeed = 0f;
        Collider aheadCollider = null;

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

            Vector3 toOther = -away / dist;

            // Tàu sát mạn (cả hơi chếch ra sau): mũi tàu quay về phía đó sẽ đâm vào nó
            if (dist < sideRadius && Vector3.Dot(forward, toOther) > -0.5f)
            {
                float side = Vector3.Dot(toOther, right);
                float w = 1f - dist / sideRadius;
                if (side > 0.2f) rightBlock = Mathf.Max(rightBlock, w);
                else if (side < -0.2f) leftBlock = Mathf.Max(leftBlock, w);
            }

            // Đồng đội nằm trên đường chạy trước mũi: ghi lại khoảng trống để phanh, không húc nó đi
            Vector3 front = col.ClosestPoint(bow) - pos;
            front.y = 0f;
            float along = Vector3.Dot(front, forward);
            Vector3 otherVelocity = col.attachedRigidbody != null ? col.attachedRigidbody.linearVelocity : Vector3.zero;
            float laneFree = Mathf.Max(along - halfLength - AllyGap, 0f);
            // Chỉ tính khi đang tiến lại gần (tàu phía trước chạy xa dần thì cứ bám theo, không cần vòng)
            bool approaching = laneFree <= 0.05f || Vector3.Dot(velocity - otherVelocity, forward) > 0.5f;
            if (along > 0f && laneFree < laneRange && approaching
                && Mathf.Abs(Vector3.Dot(front, right)) < halfWidth + AllyLaneMargin)
            {
                float free = laneFree;
                if (free < AllyAheadFree)
                {
                    AllyAheadFree = free;
                    aheadLateral = Vector3.Dot(col.bounds.center - pos, right);
                    aheadLateralSpeed = Vector3.Dot(otherVelocity, right);
                    aheadCollider = col;
                }
            }

            // Đồng đội sát đuôi: không lùi vào nó
            Vector3 back = col.ClosestPoint(stern) - pos;
            back.y = 0f;
            float behind = -Vector3.Dot(back, forward);
            if (behind > 0f && behind < halfLength + AllyGap + 0.5f
                && Mathf.Abs(Vector3.Dot(back, right)) < halfWidth + AllyLaneMargin)
                AllyBehind = true;

            // Bỏ qua tàu ở phía sau (tàu sau tự tránh tàu trước) và tàu ngoài bán kính né
            if (dist > p.avoidanceRadius || Vector3.Dot(forward, toOther) < -0.2f)
                continue;

            sum += away / dist * (1f - Mathf.Clamp01(dist / p.avoidanceRadius));
        }

        if (aheadCollider != null)
        {
            // Chốt mạn vượt một lần cho mỗi đồng đội: vòng sang phía không có nó.
            // Nó nằm ngay chính giữa (đối đầu) thì cả hai cùng bẻ phải
            if (aheadCollider != passCollider)
            {
                passCollider = aheadCollider;
                if (Mathf.Abs(aheadLateralSpeed) > CrossingSpeed)
                    passSide = -Mathf.Sign(aheadLateralSpeed);          // nó đang băng ngang: vượt sau đuôi nó
                else
                    passSide = aheadLateral > halfWidth * 0.25f ? -1f : 1f;
            }
            float proximity = 1f - Mathf.Clamp01(AllyAheadFree / laneRange);
            AllyAheadSteer = right * passSide * Mathf.Max(proximity, PassHoldSteer);
        }
        else if (passCollider != null)
        {
            // Đường tạm trống (vd: vừa lùi ra) nhưng chưa vượt qua: giữ mạn đã chọn, nếu không
            // cả hai lại bẻ về đích và lao vào nhau lần nữa
            Vector3 toPass = passCollider.ClosestPoint(pos) - pos;
            toPass.y = 0f;
            // Nhớ khi nó vẫn còn phía trước và: chưa tách hẳn sang ngang, hoặc vẫn đang lao về phía mình
            // (đối đầu: vừa lùi ra thì đường tạm trống nhưng lao tiếp là đâm). Tách ngang và không còn
            // tiến lại gần (vd: nó đang băng qua) thì bỏ, không đuổi theo nó
            Rigidbody passBody = passCollider.attachedRigidbody;
            Vector3 passVelocity = passBody != null ? passBody.linearVelocity : Vector3.zero;
            bool overlapping = Mathf.Abs(Vector3.Dot(toPass, right)) < halfWidth + AllyLaneMargin + PassClearance;
            // Đang lao về phía mình, hoặc đang quay mũi về phía mình (đối đầu, kể cả lúc cả hai đang lùi)
            bool closingIn = Vector3.Dot(passVelocity, forward) < -CrossingSpeed
                             || (passBody != null && Vector3.Dot(passBody.rotation * Vector3.forward, forward) < -0.5f);
            bool movingAway = Vector3.Dot(velocity - passVelocity, forward) < -0.5f;
            bool stillAhead = passCollider.enabled && passCollider.gameObject.activeInHierarchy
                              && toPass.magnitude < halfLength + laneRange + PassMemoryRange
                              && Vector3.Dot(toPass, forward) > 0f
                              && (closingIn || (overlapping && !movingAway));
            if (stillAhead)
                AllyAheadSteer = right * passSide * PassHoldSteer;
            else
                passCollider = null;
        }

        Avoidance = Vector3.ClampMagnitude(sum, 1f);
        RightBlock = rightBlock;
        LeftBlock = leftBlock;
    }

    public float SenseObstacle(BoatHandlingProfile p, float speed, float brakeDecel, out Vector3 steer)
    {
        steer = Vector3.zero;
        BlockedAhead = false;
        ObstacleDistance = float.MaxValue;
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
        ObstacleDistance = nearest;
        BlockedAhead = free <= BlockedTolerance;
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
