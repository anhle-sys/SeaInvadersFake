using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GunnerCombat : BoatCombatBase
{
    [SerializeField] private BoatAimController aimController;

    private GunnerData data;
    private float attackTimer;

    // Orbit state
    private float orbitDirection;   // +1: địch ở mạn phải, -1: mạn trái, 0: chưa chốt
    private bool isPassing;
    private float passTimer;

    public override float CombatSpeedMultiplier =>
        data != null ? data.combatSpeedMultiplier : 0.4f;

    public override void Setup(BoatData boatData)
    {
        data = boatData as GunnerData;
        if (data == null)
            Debug.LogError($"{name}: GunnerCombat cần GunnerData!");
    }

    public override void OnEnterCombat(Transform enemy)
    {
        isPassing = true;
        passTimer = 0f;

        switch (data.cannonSide)
        {
            case CannonSide.Right: orbitDirection = 1f; break;
            case CannonSide.Left: orbitDirection = -1f; break;
            default: orbitDirection = 0f; break;
        }
    }

    public override void Tick(Transform enemy)
    {
        if (data == null)
            return;

        if (attackTimer > 0f)
            attackTimer -= Time.deltaTime;

        if (aimController.IsAimingAtTarget)
            TryAttack(enemy);

        movement.Steer(GetCombatDirection(enemy), data.turnRadius);
    }
    private Vector3 GetCombatDirection(Transform enemy)
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 to = enemy.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;

        if (dist < 0.01f)
            return forward;

        Vector3 toDir = to / dist;

        if (isPassing)
        {
            passTimer += Time.deltaTime;

            if (Vector3.Dot(forward, to) <= -data.passDistance || passTimer >= data.passMaxTime)
            {
                isPassing = false;

                if (orbitDirection == 0f)
                {
                    float side = Vector3.Dot(Vector3.Cross(Vector3.up, forward), toDir);
                    orbitDirection = side >= 0f ? 1f : -1f;
                }
            }
            else
            {
                return forward;
            }
        }

        float sign = orbitDirection == 0f ? 1f : orbitDirection;

        float orbitDistance = data.attackRange * data.combatDistanceRatio;
        float correction = Mathf.Clamp((dist - orbitDistance) * data.orbitCorrectionGain,
                                       -data.maxOrbitCorrection, data.maxOrbitCorrection);

        return Quaternion.AngleAxis(-sign * (90f - correction), Vector3.up) * toDir;
    }

    private void TryAttack(Transform enemy)
    {
        if (attackTimer > 0f)
            return;

        if (Vector3.Distance(transform.position, enemy.position) > data.attackRange)
            return;

        attackTimer = data.attackCooldown;
        StartCoroutine(FireVolley(enemy, new List<Transform>(aimController.ReadyCannons)));
    }

    private IEnumerator FireVolley(Transform enemy, List<Transform> cannons)
    {
        foreach (Transform cannon in cannons)
        {
            if (enemy == null || cannon == null)
                yield break;

            GameObject bullet = Instantiate(data.bulletPrefab, cannon.position, cannon.rotation);
            Bullet b = bullet.GetComponent<Bullet>();
            if (b != null)
                b.Init(targetController.Team, enemy.position);

            if (data.volleyDelay > 0f)
                yield return new WaitForSeconds(data.volleyDelay);
        }
    }
}