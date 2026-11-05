using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoatCombatController : MonoBehaviour
{
    [Header("Attack")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float attackRange = 8f;
    [SerializeField] private float attackCooldown = 2f;
    [SerializeField] private float volleyDelay = 0.12f;

    private float attackTimer;
    private BoatTargetController targetController;

    public float AttackRange => attackRange;

    private void Awake()
    {
        targetController = GetComponent<BoatTargetController>();
    }

    private void Update()
    {
        if (attackTimer > 0f)
            attackTimer -= Time.deltaTime;
    }

    public bool CanAttack(Transform target)
    {
        if (target == null)
            return false;

        return Vector3.Distance(transform.position, target.position) <= attackRange;
    }

    public void Attack(Transform target, IReadOnlyList<Transform> cannons)
    {
        if (target == null || attackTimer > 0f || cannons.Count == 0 || !CanAttack(target))
            return;

        attackTimer = attackCooldown;
        StartCoroutine(FireVolley(target, new List<Transform>(cannons)));
    }

    private IEnumerator FireVolley(Transform target, List<Transform> cannons)
    {
        foreach (Transform cannon in cannons)
        {
            if (target == null || cannon == null)
                yield break;

            Shoot(cannon, target);

            if (volleyDelay > 0f)
                yield return new WaitForSeconds(volleyDelay);
        }
    }

    private void Shoot(Transform cannon, Transform target)
    {
        GameObject bullet = Instantiate(bulletPrefab, cannon.position, cannon.rotation);

        Bullet bulletController = bullet.GetComponent<Bullet>();
        if (bulletController != null)
            bulletController.Init(targetController.Team, target.position);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}