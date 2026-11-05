using UnityEngine;

public abstract class BoatCombatBase : MonoBehaviour
{
    [SerializeField] protected BoatTargetController targetController;
    [SerializeField] protected BoatMovementController movement;
    public abstract float CombatSpeedMultiplier { get; }

    public abstract void Setup(BoatData data, BoatStats stats);

    public virtual void OnEnterCombat(Transform enemy) { }

    public abstract void Tick(Transform enemy);

    protected Vector3 GetOrbitDirection(
        Transform enemy, float radius, float orbitDirection, float radiusCorrection)
    {
        Vector3 fromEnemy = transform.position - enemy.position;
        fromEnemy.y = 0f;

        float distance = fromEnemy.magnitude;
        if (distance < 0.01f)
            return transform.forward;

        Vector3 radial = fromEnemy / distance;
        Vector3 tangent = Vector3.Cross(Vector3.up, radial) * orbitDirection;

        float error = Mathf.Clamp((distance - radius) / radius, -1f, 1f);
        Vector3 correction = -radial * error * radiusCorrection;

        return (tangent + correction).normalized;
    }
}