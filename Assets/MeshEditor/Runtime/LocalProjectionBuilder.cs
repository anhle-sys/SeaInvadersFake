using UnityEngine;

public struct LocalProjectionBuilder
{
    public Vector3 Origin { get; private set; }
    public Vector3 Right { get; private set; }
    public Vector3 Up { get; private set; }
    public Vector3 Normal { get; private set; }

    public LocalProjectionBuilder(
        Vector3 origin,
        Vector3 normal,
        Vector3 preferredRight)
    {
        Origin = origin;
        Normal = normal.sqrMagnitude > 0.000001f
            ? normal.normalized
            : Vector3.up;

        Vector3 tangent = Vector3.ProjectOnPlane(preferredRight, Normal);

        if (tangent.sqrMagnitude < 0.000001f)
        {
            Vector3 fallback = Mathf.Abs(Vector3.Dot(Normal, Vector3.up)) < 0.95f
                ? Vector3.up
                : Vector3.right;

            tangent = Vector3.Cross(fallback, Normal);
        }

        Right = tangent.normalized;
        Up = Vector3.Cross(Normal, Right).normalized;
    }

    public Vector2 WorldToPlane(Vector3 worldPosition)
    {
        Vector3 delta = worldPosition - Origin;
        return new Vector2(
            Vector3.Dot(delta, Right),
            Vector3.Dot(delta, Up));
    }

    public float SignedDistanceToPlane(Vector3 worldPosition)
    {
        return Vector3.Dot(worldPosition - Origin, Normal);
    }
}
