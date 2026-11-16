using UnityEngine;
namespace MuOnBaAr.Runtime
{
    /// <summary>Gravity integration and Y displacement share rider CombatTime.</summary>
    public static class RiderAirborneRules
    {
        public static float ScaledDelta(float tickSeconds, float timeScale)
            => Mathf.Max(0f, tickSeconds) * Mathf.Max(0f, timeScale);

        public static float IntegrateVertical(float speed, float gravity,
            float terminalFallSpeed, float tickSeconds, float timeScale)
            => Mathf.Max(terminalFallSpeed,
                speed + gravity * ScaledDelta(tickSeconds, timeScale));

        /// <summary>Clamp flight ascent at absolute Unity world Y ceiling.
        /// Takeoff Y is never added. Downward travel remains available.</summary>
        public static float LimitFlyingAscent(float worldSpeedY, float worldY,
            float maxFlyHeight, float tickSeconds)
        {
            if (worldSpeedY <= 0f) return worldSpeedY;
            if (tickSeconds <= 0f) return 0f;
            float headroom = Mathf.Max(0f,
                Mathf.Max(0f, maxFlyHeight) - worldY);
            return Mathf.Min(worldSpeedY, headroom / tickSeconds);
        }

        public static float WorldVerticalSpeed(float gameplaySpeed, float timeScale)
            => gameplaySpeed * Mathf.Max(0f, timeScale);
    }
}
