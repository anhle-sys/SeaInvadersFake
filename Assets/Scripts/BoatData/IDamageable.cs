public interface IDamageable
{
    BoatTeam Team { get; }
    void TakeDamage(float amount);
}