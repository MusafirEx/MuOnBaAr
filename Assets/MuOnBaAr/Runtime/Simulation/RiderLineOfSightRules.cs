using UnityEngine;

namespace MuOnBaAr.Runtime
{
    /// <summary>
    /// Collision-based LOS shared by the authoritative melee check and
    /// local target selection. Reuses MOBA's RaycastAll + ignore-owner concept,
    /// without requiring a camera for melee or changing Fusion network state.
    /// </summary>
    public static class RiderLineOfSightRules
    {
        public static bool HasClearPath(
            Vector3 origin, Vector3 destination,
            Transform attackerRoot, Transform targetRoot, LayerMask layers)
        {
            // Never claim visibility if the player roots are unavailable.
            if (attackerRoot == null || targetRoot == null)
                return false;

            Vector3 displacement = destination - origin;
            float distance = displacement.magnitude;
            if (distance <= 0.0001f)
                return true;

            // RaycastAll is intentional: a first-hit Raycast can hit the
            // attacking character's own forward-marker BoxCollider.
            // No fixed result buffer can silently discard a real wall.
            RaycastHit[] hits = Physics.RaycastAll(
                origin, displacement / distance, distance,
                layers, QueryTriggerInteraction.Ignore);

            float nearestDistance = float.PositiveInfinity;
            Transform firstExternalHit = null;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null)
                    continue;

                Transform hitTransform = collider.transform;
                if (hitTransform == attackerRoot ||
                    hitTransform.IsChildOf(attackerRoot))
                    continue;

                // RaycastAll does not guarantee sorted hits; choose the
                // closest hit AFTER excluding attacker-owned colliders.
                if (hits[i].distance < nearestDistance)
                {
                    nearestDistance = hits[i].distance;
                    firstExternalHit = hitTransform;
                }
            }

            // No external solid collider means the path is clear. Hitting
            // any child of the intended target first also counts as clear.
            return firstExternalHit == null ||
                firstExternalHit == targetRoot ||
                firstExternalHit.IsChildOf(targetRoot);
        }
    }
}
