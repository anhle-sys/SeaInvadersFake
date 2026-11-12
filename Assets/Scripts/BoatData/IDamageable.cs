using UnityEngine;

public interface IDamageable
{
    BoatTeam Team { get; }
    void TakeDamage(float amount);

    /// <summary>Nhận sát thương từ một vị trí (tâm vụ nổ): tàu bị đẩy ra xa vị trí đó.</summary>
    void TakeDamage(float amount, Vector3 sourcePosition);

    /// <summary>
    /// Nhận sát thương tại hitPoint, lực tác động theo hitDirection (vd hướng bay của đạn A->B):
    /// tàu giật / bị đẩy đúng theo hướng này.
    /// </summary>
    void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitDirection);
}
