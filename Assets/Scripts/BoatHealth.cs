using UnityEngine;

public class BoatHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private BoatTargetController targetController;
    private float health;

    public BoatTeam Team => targetController.Team;


    public void SetMaxHealth(float max)
    {
        health = max;
    }

    public void TakeDamage(float amount)
    {
        health -= amount;
        if (health <= 0f)
            Die();
    }

    private void Die()
    {
        Destroy(gameObject);
    }
}