using UnityEngine;

public readonly struct BoatFraming
{
    public readonly Vector3 Center;   // tâm thân tàu, ngang mặt boong
    public readonly Vector3 Bow;      // mũi tàu, ngang mặt boong
    public readonly Vector3 Forward;  // hướng mũi tàu (đã bỏ trục Y)
    public readonly Vector3 Right;
    public readonly float Length;

    private BoatFraming(Vector3 center, Vector3 bow, Vector3 forward, Vector3 right, float length)
    {
        Center = center;
        Bow = bow;
        Forward = forward;
        Right = right;
        Length = length;
    }

    public static BoatFraming From(Transform boat)
    {
        Vector3 forward = boat.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        if (boat.TryGetComponent(out BoxCollider box))
        {
            Vector3 c = box.center;
            Vector3 s = box.size;
            // Mặt boong ~ 3/4 chiều cao hộp va chạm
            Vector3 centerLocal = new Vector3(c.x, c.y + s.y * 0.25f, c.z);
            Vector3 bowLocal = centerLocal + new Vector3(0f, 0f, s.z * 0.5f);
            float length = s.z * Mathf.Abs(boat.lossyScale.z);
            return new BoatFraming(boat.TransformPoint(centerLocal), boat.TransformPoint(bowLocal), forward, right, length);
        }

        Bounds b = new Bounds(boat.position, Vector3.zero);
        bool has = false;
        foreach (Renderer r in boat.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }

        float len = has ? Mathf.Max(b.size.x, b.size.z) : 4f;
        Vector3 center = (has ? b.center : boat.position) + Vector3.up * (has ? b.extents.y * 0.5f : 0.5f);
        return new BoatFraming(center, center + forward * len * 0.5f, forward, right, len);
    }

    public Vector3 TowardBow(float t) => Vector3.Lerp(Center, Bow, t);
}
