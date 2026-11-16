using UnityEngine;

namespace MuOnBaAr.Runtime
{
    /// <summary>
    /// Pure tick-based KO, respawn and spawn-protection checks.
    /// The network authority owns all calls that change combat state.
    /// </summary>
    public static class RiderRespawnRules
    {
        public static int DurationTicks(float durationSeconds, float tickDeltaTime)
        {
            if (durationSeconds <= 0f || tickDeltaTime <= 0f)
                return 0;

            return Mathf.Max(1, Mathf.CeilToInt(durationSeconds / tickDeltaTime));
        }

        public static bool ShouldRespawn(int health, int now, int respawnTick)
        {
            return health <= 0 && respawnTick > 0 && now >= respawnTick;
        }

        public static bool CanTakeDamage(int health, int now, int protectedUntilTick)
        {
            return health > 0 && now >= protectedUntilTick;
        }
    }
}
