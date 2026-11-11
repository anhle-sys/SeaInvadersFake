using UnityEngine;

public class BattleBounds
{
    private readonly Vector3[] corners = new Vector3[4];
    private readonly Vector3[] normals = new Vector3[4]; // pháp tuyến hướng vào trong của cạnh i (corners[i] -> corners[i + 1])
    private readonly float padding;
    private readonly float softMargin;

    public Vector3[] Corners => corners;

    public BattleBounds(Vector3 camPos, Quaternion camRot, float fov, float aspect, float planeY,
                        float marginX, float marginY, float maxDistance, float padding, float softMargin)
    {
        this.padding = padding;
        this.softMargin = Mathf.Max(softMargin, 0.01f);

        float tanV = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        float tanH = tanV * aspect;
        float nx = 1f - 2f * marginX;
        float ny = 1f - 2f * marginY;

        corners[0] = Project(camPos, camRot, -nx * tanH, -ny * tanV, planeY, maxDistance);
        corners[1] = Project(camPos, camRot, nx * tanH, -ny * tanV, planeY, maxDistance);
        corners[2] = Project(camPos, camRot, nx * tanH, ny * tanV, planeY, maxDistance);
        corners[3] = Project(camPos, camRot, -nx * tanH, ny * tanV, planeY, maxDistance);

        Vector3 center = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
        for (int i = 0; i < 4; i++)
        {
            Vector3 edge = corners[(i + 1) % 4] - corners[i];
            Vector3 n = new Vector3(-edge.z, 0f, edge.x).normalized;
            if (Vector3.Dot(center - corners[i], n) < 0f)
                n = -n;
            normals[i] = n;
        }
    }

    // Điểm tia nhìn (x, y, 1) trong không gian camera chạm mặt nước; tia không chạm / quá xa thì cắt ở maxDistance
    private static Vector3 Project(Vector3 camPos, Quaternion camRot, float x, float y, float planeY, float maxDistance)
    {
        Vector3 dir = camRot * new Vector3(x, y, 1f);
        Vector3 flat = new Vector3(dir.x, 0f, dir.z);
        float flatLen = flat.magnitude;
        if (flatLen < 0.0001f)
            return new Vector3(camPos.x, planeY, camPos.z);

        float horizontal = maxDistance;
        if (dir.y < -0.0001f)
            horizontal = Mathf.Min((planeY - camPos.y) / dir.y * flatLen, maxDistance);

        Vector3 p = camPos + flat / flatLen * horizontal;
        p.y = planeY;
        return p;
    }

    private float EdgeDistance(int i, Vector3 position) =>
        Vector3.Dot(position - corners[i], normals[i]) - padding;

    // Khoảng cách từ tâm tàu tới mép gần nhất (đã trừ padding). Âm = đang ở ngoài vùng cho phép
    public float DistanceInside(Vector3 position)
    {
        float min = float.MaxValue;
        for (int i = 0; i < 4; i++)
            min = Mathf.Min(min, EdgeDistance(i, position));
        return min;
    }

    // Tới gần mép: bỏ dần phần hướng lái đang chĩa ra ngoài và thêm lực kéo vào trong (tăng dần, không bật tắt)
    public Vector3 SteerInside(Vector3 position, Vector3 direction)
    {
        Vector3 push = Vector3.zero;
        for (int i = 0; i < 4; i++)
        {
            float d = EdgeDistance(i, position);
            if (d >= softMargin)
                continue;

            float k = Mathf.Clamp01(1f - d / softMargin);
            float outward = Mathf.Max(0f, -Vector3.Dot(direction, normals[i]));
            push += normals[i] * (k * (outward + k));
        }

        if (push.sqrMagnitude < 0.000001f)
            return direction;

        Vector3 result = direction + push;
        result.y = 0f;
        return result.sqrMagnitude > 0.0001f ? result.normalized : direction;
    }

    // Chốt chặn cuối: tàu đã chạm mép thì bỏ phần vận tốc đi ra ngoài (trượt dọc mép, không dịch chuyển tức thời)
    public Vector3 ClampVelocity(Vector3 position, Vector3 velocity)
    {
        for (int i = 0; i < 4; i++)
        {
            if (EdgeDistance(i, position) >= 0f)
                continue;

            float outward = Vector3.Dot(velocity, normals[i]);
            if (outward < 0f)
                velocity -= normals[i] * outward;
        }
        return velocity;
    }
}
