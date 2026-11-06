using UnityEngine;

public interface IDamageable
{
    BoatTeam Team { get; }
    void TakeDamage(float amount);

    /// <summary>Nhận sát thương từ một vị trí (đạn, tâm vụ nổ) để hiệu ứng trúng đòn biết hướng.</summary>
    void TakeDamage(float amount, Vector3 sourcePosition);
}
