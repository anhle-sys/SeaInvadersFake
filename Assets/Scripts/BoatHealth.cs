using UnityEngine;

public class BoatHealth : MonoBehaviour, IDamageable
{
    // Giáp không bao giờ giảm sát thương xuống dưới tỉ lệ này
    private const float MinDamageRatio = 0.1f;

    [SerializeField] private BoatTargetController targetController;

    private BoatData data;
    private BoatStats stats;
    private float health;
    private bool isDead;

    public BoatTeam Team => targetController.Team;
    public bool IsDead => isDead;

    public void Setup(BoatData boatData, BoatStats boatStats)
    {
        data = boatData;
        stats = boatStats;
        health = boatStats.MaxHealth;
        isDead = false;
    }

    public void TakeDamage(float amount)
    {
        if (isDead)
            return;

        if (stats != null)
        {
            if (Random.value * 100f < stats.Evasion)
                return;

            amount = Mathf.Max(amount - stats.Armor, amount * MinDamageRatio);
        }

        health -= amount;
        if (health <= 0f)
            Die();
    }

    private void Die()
    {
        isDead = true;

        if (Team == BoatTeam.Enemy && data != null && data.killReward > 0)
        {
            GameData.AddGold(data.killReward);
            GameData.Save();
        }

        Destroy(gameObject);
    }
}
