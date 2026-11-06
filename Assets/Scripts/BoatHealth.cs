using System;
using UnityEngine;

public class BoatHealth : MonoBehaviour, IDamageable, IHealthBarTarget
{
    private const float MinDamageRatio = 0.1f;

    [SerializeField] private BoatTargetController targetController;
    [SerializeField] private Vector3 barOffset = new Vector3(0, 2f, 0);

    private BoatData data;
    private BoatStats stats;
    private float health;
    private bool isDead;

    public event Action<float> OnHealthChanged;
    public event Action OnDied;
    public bool IsAlly => Team == BoatTeam.Ally;
    public BoatTeam Team => targetController.Team;
    public bool IsDead => isDead;
    public Vector3 BarWorldPosition => transform.position + barOffset;

    public void Setup(BoatData boatData, BoatStats boatStats)
    {
        data = boatData;
        stats = boatStats;
        health = boatStats.MaxHealth;
        isDead = false;

        GameController.Instance.HealthBarManager.Register(this);
    }

    public void TakeDamage(float amount)
    {
        if (isDead)
            return;

        if (stats != null)
        {
            if (UnityEngine.Random.value * 100f < stats.Evasion)
                return;

            amount = Mathf.Max(amount - stats.Armor, amount * MinDamageRatio);
        }

        health -= amount;
        OnHealthChanged?.Invoke(Mathf.Clamp01(health / stats.MaxHealth));

        if (health <= 0f)
            Die();
    }

    private void Die()
    {
        isDead = true;
        OnDied?.Invoke();

        if (Team == BoatTeam.Enemy && data != null && data.killReward > 0)
        {
            int reward = Mathf.RoundToInt(data.killReward * GoldBuff.Multiplier);
            GameData.AddGold(reward);
            GameData.Save();
        }

        Destroy(gameObject);
    }
}