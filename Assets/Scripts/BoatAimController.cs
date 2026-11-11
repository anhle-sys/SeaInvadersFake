using System.Collections.Generic;
using UnityEngine;

public class BoatAimController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BoatTargetController targetController;
    [SerializeField] private Transform[] cannons;

    [Header("Aim Settings")]
    [SerializeField] private float aimTolerance = 10f;   // độ lệch cho phép (độ)
    [SerializeField] private float raycastDistance = 20f; // nên >= attackRange
    [SerializeField] private LayerMask targetLayer;

    private readonly List<Transform> readyCannons = new List<Transform>();
    private readonly RaycastHit[] hits = new RaycastHit[8];
    private Transform cachedTarget;
    private BoatTargetController cachedEnemy;

    public IReadOnlyList<Transform> ReadyCannons => readyCannons;
    public bool IsAimingAtTarget => readyCannons.Count > 0;

    public void Configure(float attackRange)
    {
        raycastDistance = attackRange * 1.1f;
    }

    private void Update()
    {
        readyCannons.Clear();
        if (!GameController.BattleStarted)
            return;

        Transform target = targetController.CurrentBoatTarget;
        if (target == null)
            return;

        // Chỉ GetComponent khi đổi mục tiêu, không phải mỗi frame
        if (target != cachedTarget)
        {
            cachedTarget = target;
            cachedEnemy = target.GetComponent<BoatTargetController>();
        }
        BoatTargetController enemy = cachedEnemy;

        foreach (Transform cannon in cannons)
        {
            if (cannon != null && IsCannonAimed(cannon, target, enemy))
                readyCannons.Add(cannon);
        }
    }

    private bool IsCannonAimed(Transform cannon, Transform target, BoatTargetController enemy)
    {
        Vector3 toTarget = target.position - cannon.position;
        toTarget.y = 0f;

        Vector3 forward = cannon.forward;
        forward.y = 0f;

        if (toTarget.sqrMagnitude < 0.001f || Vector3.Angle(forward, toTarget) > aimTolerance)
            return false;

        int count = Physics.RaycastNonAlloc(
            cannon.position,
            cannon.forward,
            hits,
            raycastDistance,
            targetLayer,
            QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        BoatTargetController nearestOwner = null;
        bool foundAny = false;

        for (int i = 0; i < count; i++)
        {
            BoatTargetController owner =
                hits[i].collider.GetComponentInParent<BoatTargetController>();

            if (owner == targetController)
                continue;

            if (hits[i].distance < nearest)
            {
                nearest = hits[i].distance;
                nearestOwner = owner;
                foundAny = true;
            }
        }

        return foundAny && nearestOwner == enemy;
    }

    private void OnDrawGizmos()
    {
        if (cannons == null)
            return;

        foreach (Transform cannon in cannons)
        {
            if (cannon == null) continue;

            Gizmos.color = readyCannons.Contains(cannon) ? Color.green : Color.red;
            Gizmos.DrawRay(cannon.position, cannon.forward * raycastDistance);
        }
    }
}