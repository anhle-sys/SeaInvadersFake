using UnityEngine;

public interface IDamageable
{
    BoatTeam Team { get; }
    void TakeDamage(float amount);

    void TakeDamage(float amount, Vector3 sourcePosition);

    void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitDirection);
}
