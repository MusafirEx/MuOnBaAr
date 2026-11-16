using UnityEngine;

namespace MuOnBaAr.Runtime
{
    /// <summary>
    /// Stateless client-side visual feedback decisions.
    /// These values never authorize damage, KO or respawn.
    /// </summary>
    public static class RiderCombatFeedbackRules
    {
        public static float HealthFraction(int health, int maximumHealth)
        {
            if (maximumHealth <= 0)
                return 0f;
            return Mathf.Clamp01((float)health / maximumHealth);
        }

        public static int DamageTaken(int previousHealth, int currentHealth)
        {
            return Mathf.Max(0, previousHealth - currentHealth);
        }

        public static bool IsNewAttack(int previousSequence, int currentSequence)
        {
            return previousSequence != currentSequence;
        }

        public static bool IsProtectionActive(
            int health, int currentTick, int invulnerableUntilTick)
        {
            return health > 0 && currentTick < invulnerableUntilTick;
        }

        // Presentation-only visibility rule, accessible to EditMode tests
        // through the existing MuOnBaAr.Runtime test assembly reference.
        public static bool ShouldDisplay(
            bool isLocalPlayer, bool showLocalPlayer,
            bool inFront, bool inViewport, bool overlapsHud,
            float distance, float maximumDistance)
        {
            return (!isLocalPlayer || showLocalPlayer) &&
                inFront && inViewport && !overlapsHud &&
                distance <= Mathf.Max(1f, maximumDistance);
        }
    }
}
