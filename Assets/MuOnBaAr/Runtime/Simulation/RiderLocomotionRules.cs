namespace MuOnBaAr.Runtime
{
    /// <summary>
    /// Pure animation level mapping, independent of camera and networking.
    /// Full-speed WASD selects Run; Sprint is reserved for higher speeds.
    /// </summary>
    public static class RiderLocomotionRules
    {
        public const float Idle = 0f;
        public const float Walk = 1f;
        public const float Run = 2f;
        public const float Sprint = 3f;

        // Physical movement multiplier: full input reaches Run normally,
        // and 150% of nominal speed reaches Sprint when Shift is held.
        public static float MovementSpeedMultiplier(bool sprintHeld)
        {
            return sprintHeld ? 1.5f : 1f;
        }

        // ACL00 authored 1D Blend Tree thresholds:
        // Idle 0.0, Walk 0.4, Run 1.4, Sprint 2.4.
        // Input is horizontal speed / the current form's nominal move speed.
        // Full-speed WASD (1.0) selects Run, not a Run/Sprint mix.
        public static float BlendValueForSpeed(float normalizedSpeed)
        {
            if (float.IsNaN(normalizedSpeed) ||
                float.IsInfinity(normalizedSpeed) ||
                normalizedSpeed <= 0.10f)
                return 0f;

            if (normalizedSpeed < 0.40f)
                return 0.4f * (normalizedSpeed - 0.10f) / 0.30f;

            if (normalizedSpeed < 1.0f)
                return 0.4f + (normalizedSpeed - 0.40f) / 0.60f;

            if (normalizedSpeed < 1.50f)
                return 1.4f + (normalizedSpeed - 1.0f) * 2f;

            return 2.4f;
        }

        // Keep the original discrete level selector for existing tests and
        // other callers; ACL00 now uses BlendValueForSpeed instead.
        public static float LevelForSpeed(float normalizedSpeed)
        {
            if (float.IsNaN(normalizedSpeed) ||
                float.IsInfinity(normalizedSpeed) || normalizedSpeed < 0.10f)
                return Idle;
            if (normalizedSpeed < 0.60f)
                return Walk;
            if (normalizedSpeed < 1.15f)
                return Run;
            return Sprint;
        }
    }
}
