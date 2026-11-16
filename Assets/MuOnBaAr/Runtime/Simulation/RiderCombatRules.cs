using UnityEngine;

namespace MuOnBaAr.Runtime
{
    /// <summary>Deterministic combat checks shared by server logic and EditMode tests.</summary>
    public static class RiderCombatRules
    {
        public static bool CanAttack(int health, int currentTick, int nextAttackTick)
        {
            return health > 0 && currentTick >= nextAttackTick;
        }

        /// <summary>
        /// Returns rider-local +Z projected to ground space using the
        /// authoritative actor rotation, never camera or client input.
        /// </summary>
        public static Vector2 PlanarForward(Quaternion riderRotation)
        {
            Vector3 forward = riderRotation * Vector3.forward;
            Vector2 planar = new Vector2(forward.x, forward.z);
            return planar.sqrMagnitude > 0.0001f
                ? planar.normalized : Vector2.zero;
        }

        public static bool IsInMeleeArc(
            Vector3 attackerPosition, Vector3 targetPosition,
            Vector2 aim, float range, float halfAngleDegrees)
        {
            if (range <= 0f || aim.sqrMagnitude < 0.0001f)
                return false;

            Vector3 separation = targetPosition - attackerPosition;
            Vector2 planar = new Vector2(separation.x, separation.z);
            float distanceSquared = planar.sqrMagnitude;
            if (distanceSquared < 0.0001f || distanceSquared > range * range)
                return false;

            return Vector2.Angle(aim, planar) <= Mathf.Clamp(halfAngleDegrees, 0f, 180f);
        }

        public static int CalculateDamage(int baseDamage, float formMultiplier)
        {
            if (baseDamage <= 0 || formMultiplier <= 0f)
                return 0;

            return Mathf.Max(1, Mathf.RoundToInt(baseDamage * formMultiplier));
        }
    }
}
