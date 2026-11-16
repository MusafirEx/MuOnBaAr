using UnityEngine;

namespace MuOnBaAr.Runtime
{
    /// <summary>Presentation-only formatting for the native uGUI combat HUD.</summary>
    public static class MuOnBaArHudRules
    {
        public static string FormatStats(string form, int health, int energy)
        {
            return "RIDER  " + (string.IsNullOrEmpty(form) ? "Unknown" : form) +
                   "   |   HP " + Mathf.Max(0, health) +
                   "   |   ENERGY " + Mathf.Max(0, energy);
        }

        public static string FormatLock(string target)
        {
            return "Q: TARGET LOCK  [" +
                   (string.IsNullOrEmpty(target) ? "None" : target) +
                   "]   |   LMB: ATTACK";
        }

        public static string FormatStatus(
            int health, int currentTick, int respawnTick,
            int invulnerableUntilTick, float tickDelta)
        {
            if (health <= 0)
            {
                float seconds = Mathf.Max(0, respawnTick - currentTick) *
                                Mathf.Max(0f, tickDelta);
                return "KO  |  Respawning in " + seconds.ToString("0.0") + "s";
            }

            if (invulnerableUntilTick > currentTick)
            {
                float seconds = (invulnerableUntilTick - currentTick) *
                                Mathf.Max(0f, tickDelta);
                return "RESPAWN SHIELD  |  " + seconds.ToString("0.0") + "s";
            }

            return "WASD MOVE  |  TAB FORM  |  Q LOCK  |  RMB CAMERA";
        }
    }
}
