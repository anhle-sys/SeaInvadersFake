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
    private const float CrossingSpeed = 1f;
    private const float PassClearance = 1f;         // tách ngang thêm khoảng này khỏi đường chạy là coi như đã vượt xong
    private const float MutualFacing = 0.8f;
    private const float HeadOnDriftThreshold = 0.05f; // vận tốc tương đối ngang dưới mức này coi như đối đầu thẳng hàng        // đồng đội hướng mũi về phía mình trong góc này (cos) thì coi là chắn nhau trực diện
    private const float AheadMinRatio = 0.6f;       // điểm của đồng đội phải ở trước tâm tàu quá (nửa chiều dài × hệ số này) mới coi là chắn mũi    // điểm gần nhất của đồng đội lệch khỏi trục tàu quá (nửa bề ngang × hệ số này) thì trườn qua được         // đồng đội chạy ngang nhanh hơn mức này thì coi là đang băng qua       // đồng đội đã chốt mạn vượt còn trong (tầm nhìn + khoảng này) thì vẫn nhớ

    private readonly Collider[] avoidanceHits = new Collider[16];
    private Collider passCollider;                  // đồng đội đang vòng qua
    private float passSide;                         // +1: vượt sang phải, -1: sang trái
    private bool passHasPriority;                   // mình là tàu được đi trước khi vượt đồng đội này
    private readonly RaycastHit[] obstacleHits = new RaycastHit[8];

    public Vector3 Avoidance { get; private set; } //Hướng né tàu đồng đội

    // Mức bị tàu đồng đội chắn bên mạn: > 0 = bên phải, < 0 = bên trái (0..1 mỗi bên)
    public float RightBlock { get; private set; }
    public float LeftBlock { get; private set; }

    // Khoảng trống từ mũi tới đồng đội nằm trên đường chạy phía trước (MaxValue = không có)
    public float AllyAheadFree { get; private set; } = float.MaxValue;

    // Tốc độ đồng đội chắn trước mũi đang lao về phía mình (> 0: đang tới gần)
    public float AllyAheadClosing { get; private set; }

    // Phải nhường đồng đội chắn trước mũi. Hai tàu chắn nhau trực diện thì chỉ một tàu nhường
    // (theo thứ tự cố định), tàu kia vòng qua -> không bao giờ cả hai cùng đứng chờ nhau
    public bool MustYield { get; private set; }

    // Hướng bẻ lái để vòng qua đồng đội chắn trước mũi (ngang thân tàu, mạnh dần khi càng gần)
    public Vector3 AllyAheadSteer { get; private set; }

    public bool BlockedAhead { get; private set; } //Mũi tàu đã sát vật cản phía trước

    public float ObstacleDistance { get; private set; } = float.MaxValue; //Khoảng cách từ tâm thân tàu tới vật cản gần nhất phía trước

    private const float EnemyHoldSteer = 0.3f;      // độ bẻ lái tối thiểu khi enemy chắn trước mũi

    // Bật né tàu địch (Gunner bật; Banger để tắt vì nó lao vào địch)
    public bool AvoidEnemies { get; set; }
    // Mục tiêu đang combat: không né, không phanh (Gunner lượn quanh nó)
    public Transform IgnoreEnemy { get; set; }

    public float EnemyAheadFree { get; private set; } = float.MaxValue;
    public float EnemyAheadClosing { get; private set; }
    public Vector3 EnemyAheadSteer { get; private set; }

    private Collider enemyPassCollider;
    private float enemyPassSide;
    private Collider enemyAheadCollider;
    private float enemyAheadLateral;
    public BoatSteeringSensors(Rigidbody rb)
    {
        this.rb = rb;
        transform = rb.transform;
        teamSource = rb.GetComponent<BoatTargetController>();
        hull = rb.GetComponent<BoxCollider>();
        boatMask = LayerMask.GetMask("Boat");
    }

    public void ResetPass() => passCollider = null;

    // Được đi trước: đồng đội cũng đang thấy mình chắn mũi nó (trực diện) và mình có thứ tự ưu tiên cao hơn
    private bool HasRightOfWay(Rigidbody other) =>
        other != null && IsFacingMe(other) && rb.GetInstanceID() > other.GetInstanceID();

    // Đồng đội đang hướng mũi về phía mình (chắn nhau trực diện)
    private bool IsFacingMe(Rigidbody other)
    {
        if (other == null)
            return false;

        Vector3 toMe = rb.position - other.position;
        toMe.y = 0f;
        return toMe.sqrMagnitude > 0.0001f
               && Vector3.Dot(other.rotation * Vector3.forward, toMe.normalized) > MutualFacing;
    }

    public void UpdateAvoidance(BoatHandlingProfile p, float speed)
    {
        AllyAheadFree = float.MaxValue;
        AllyAheadSteer = Vector3.zero;
        AllyAheadClosing = 0f;
        MustYield = false;
        EnemyAheadFree = float.MaxValue;
        EnemyAheadClosing = 0f;
        EnemyAheadSteer = Vector3.zero;
        enemyAheadCollider = null;
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

        // Tầm nhìn đồng đội trước mũi tăng theo tốc độ để kịp vòng qua thay vì phải phanh chết
        float laneRange = Mathf.Max(speed, 0f) * AllyLookAhead + AllyGap + 2f;
        float queryRadius = Mathf.Max(p.avoidanceRadius, halfLength + laneRange + halfWidth);
        Vector3 velocity = rb.linearVelocity;

        int count = Physics.OverlapSphereNonAlloc(pos, queryRadius, avoidanceHits, boatMask,
                                                  QueryTriggerInteraction.Ignore);
        Vector3 sum = Vector3.zero;
        float aheadLateral = 0f;
        float aheadLateralSpeed = 0f;
        Vector3 aheadOtherVelocity = Vector3.zero;
        Collider aheadCollider = null;

        for (int i = 0; i < count; i++)
        {
            Collider col = avoidanceHits[i];
            if (col.attachedRigidbody == rb)
                continue;

            // Chỉ tránh tàu cùng phe: tàu địch là mục tiêu (Gunner lượn quanh, Banger lao vào)
            BoatTargetController other = col.GetComponentInParent<BoatTargetController>();
            if (other == null)
                continue;

            if (other.Team != teamSource.Team)
            {
                if (!AvoidEnemies || other.transform == IgnoreEnemy)
                    continue;
                SenseEnemy(col, pos, forward, right, velocity, halfLength, halfWidth,
                           laneRange, sideRadius, ref rightBlock, ref leftBlock);
                continue;   // enemy không đi vào Avoidance (đẩy ra) của ally
            }

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
            // Chỉ tính phần nằm hẳn phía trước (nửa trước thân trở ra); tàu nằm ngang hông là "sát mạn", không phải chắn mũi
            if (along > halfLength * AheadMinRatio && laneFree < laneRange && approaching
                && Mathf.Abs(Vector3.Dot(front, right)) < halfWidth + AllyLaneMargin)
            {
                float free = laneFree;
                if (free < AllyAheadFree)
                {
                    AllyAheadFree = free;
                    aheadLateral = Vector3.Dot(col.bounds.center - pos, right);
                    aheadLateralSpeed = Vector3.Dot(otherVelocity, right);
                    aheadOtherVelocity = otherVelocity;
                    aheadCollider = col;
                    AllyAheadClosing = -Vector3.Dot(otherVelocity, forward);
                    MustYield = !HasRightOfWay(col.attachedRigidbody);
                    passHasPriority = !MustYield;
                }
            }

            // Bỏ qua tàu ở phía sau (tàu sau tự tránh tàu trước) và tàu ngoài bán kính né
            if (dist > p.avoidanceRadius || Vector3.Dot(forward, toOther) < -0.2f)
                continue;

            sum += away / dist * (1f - Mathf.Clamp01(dist / p.avoidanceRadius));
        }

        if (aheadCollider != null)
        {
            if (aheadCollider != passCollider)
            {
                passCollider = aheadCollider;
                if (IsFacingMe(aheadCollider.attachedRigidbody))
                {
                    Vector3 los = aheadCollider.bounds.center - pos;
                    los.y = 0f;
                    Vector3 tangent = los.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, los.normalized) : right;
                    float drift = Vector3.Dot(aheadOtherVelocity - velocity, tangent);
                    passSide = Mathf.Abs(drift) > HeadOnDriftThreshold ? -Mathf.Sign(drift) : 1f;
                }
                else if (Mathf.Abs(aheadLateralSpeed) > CrossingSpeed)
                    passSide = -Mathf.Sign(aheadLateralSpeed);          // nó đang băng ngang: vượt sau đuôi nó
                else
                    passSide = aheadLateral > halfWidth * 0.25f ? -1f : 1f;
            }
            float proximity = 1f - Mathf.Clamp01(AllyAheadFree / laneRange);
            AllyAheadSteer = PassTangent(aheadCollider, pos, right) * passSide * Mathf.Max(proximity, PassHoldSteer);
        }
        else if (passCollider != null)
        {
            Vector3 toPass = passCollider.ClosestPoint(pos) - pos;
            toPass.y = 0f;
            Rigidbody passBody = passCollider.attachedRigidbody;
            Vector3 passVelocity = passBody != null ? passBody.linearVelocity : Vector3.zero;
            bool closingIn = Vector3.Dot(passVelocity, forward) < -CrossingSpeed;
            bool overlapping = Mathf.Abs(Vector3.Dot(toPass, right)) < halfWidth + AllyLaneMargin + PassClearance;
            bool movingAway = Vector3.Dot(velocity - passVelocity, forward) < -0.5f;
            bool stillAhead = passCollider.enabled && passCollider.gameObject.activeInHierarchy
                              && toPass.magnitude < halfLength + laneRange + PassMemoryRange
                              && Vector3.Dot(toPass, forward) > 0f
                              && (closingIn || (!passHasPriority && overlapping && !movingAway));
            if (stillAhead)
                AllyAheadSteer = PassTangent(passCollider, pos, right) * passSide * PassHoldSteer;
            else
                passCollider = null;
        }
        if (enemyAheadCollider != null)
        {
            // Chốt mạn vượt một lần cho mỗi enemy: vòng sang phía ngược với nó, chính giữa thì bẻ phải
            if (enemyAheadCollider != enemyPassCollider)
            {
                enemyPassCollider = enemyAheadCollider;
                enemyPassSide = enemyAheadLateral > halfWidth * 0.25f ? -1f : 1f;
            }
            float proximity = 1f - Mathf.Clamp01(EnemyAheadFree / laneRange);
            EnemyAheadSteer = PassTangent(enemyAheadCollider, pos, right) * enemyPassSide
                              * Mathf.Max(proximity, EnemyHoldSteer);
        }
        else
        {
            enemyPassCollider = null;
        }
        Avoidance = Vector3.ClampMagnitude(sum, 1f);
        RightBlock = rightBlock;
        LeftBlock = leftBlock;
    }

    // Hướng vòng quanh đồng đội: vuông góc với đường nhìn sang nó (+ = vòng qua bên phải nó).
    // Tính theo đường nhìn chứ không theo mạn tàu, nếu không tàu xoay tới đâu "bên phải" xoay theo tới đó
    private static Vector3 PassTangent(Collider other, Vector3 pos, Vector3 fallbackRight)
    {
        Vector3 to = other.ClosestPoint(pos) - pos;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f)
        {
            to = other.bounds.center - pos;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f)
                return fallbackRight;
        }
        return Vector3.Cross(Vector3.up, to.normalized);
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
    private void SenseEnemy(Collider col, Vector3 pos, Vector3 forward, Vector3 right, Vector3 velocity,
                        float halfLength, float halfWidth, float laneRange, float sideRadius,
                        ref float rightBlock, ref float leftBlock)
    {
        Vector3 closest = col.ClosestPoint(pos);
        Vector3 away = pos - closest;
        away.y = 0f;
        float dist = away.magnitude;
        if (dist < 0.01f)
        {
            away = pos - col.transform.position;
            away.y = 0f;
            dist = away.magnitude;
            if (dist < 0.01f) return;
        }
        Vector3 toOther = -away / dist;

        if (dist < sideRadius && Vector3.Dot(forward, toOther) > -0.5f)
        {
            float side = Vector3.Dot(toOther, right);
            float w = 1f - dist / sideRadius;
            if (side > 0.2f) rightBlock = Mathf.Max(rightBlock, w);
            else if (side < -0.2f) leftBlock = Mathf.Max(leftBlock, w);
        }

        // Enemy nằm trên đường chạy trước mũi
        Vector3 bow = pos + forward * halfLength;
        Vector3 front = col.ClosestPoint(bow) - pos;
        front.y = 0f;
        float along = Vector3.Dot(front, forward);
        Vector3 otherVelocity = col.attachedRigidbody != null ? col.attachedRigidbody.linearVelocity : Vector3.zero;
        float laneFree = Mathf.Max(along - halfLength - AllyGap, 0f);
        bool approaching = laneFree <= 0.05f || Vector3.Dot(velocity - otherVelocity, forward) > 0.5f;

        if (along > halfLength * AheadMinRatio && laneFree < laneRange && approaching
            && Mathf.Abs(Vector3.Dot(front, right)) < halfWidth + AllyLaneMargin
            && laneFree < EnemyAheadFree)
        {
            EnemyAheadFree = laneFree;
            EnemyAheadClosing = -Vector3.Dot(otherVelocity, forward);
            enemyAheadCollider = col;
            enemyAheadLateral = Vector3.Dot(col.bounds.center - pos, right);
        }
    }
}
