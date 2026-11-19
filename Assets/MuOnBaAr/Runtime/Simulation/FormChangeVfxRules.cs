using UnityEngine;

namespace MuOnBaAr.Runtime
{
    /// <summary>Cosmetic cue scheduling from synchronized CombatTime.</summary>
    public static class FormChangeVfxRules
    {
        public static bool IsCueDue(float elapsedSeconds, float startSeconds)
        {
            return elapsedSeconds >= Mathf.Max(0f, startSeconds);
        }

        public static float RemainingSeconds(
            float elapsedSeconds, float startSeconds, float durationSeconds)
        {
            if (!IsCueDue(elapsedSeconds, startSeconds)) return 0f;
            return Mathf.Max(0f, Mathf.Max(0f, durationSeconds) -
                (elapsedSeconds - Mathf.Max(0f, startSeconds)));
        }
    }
}
