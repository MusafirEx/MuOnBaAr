using MuOnBaAr.Runtime;
using NUnit.Framework;

namespace MuOnBaAr.Tests
{
    public sealed class MuOnBaArHudRulesTests
    {
        [Test]
        public void StatsIncludeFormHealthAndEnergy()
        {
            string actual = MuOnBaArHudRules.FormatStats("Balanced", 60, 95);
            StringAssert.Contains("Balanced", actual);
            StringAssert.Contains("HP 60", actual);
            StringAssert.Contains("ENERGY 95", actual);
        }

        [Test]
        public void NegativeResourceValuesAreClamped()
        {
            string actual = MuOnBaArHudRules.FormatStats("", -5, -3);
            StringAssert.Contains("HP 0", actual);
            StringAssert.Contains("ENERGY 0", actual);
        }

        [Test]
        public void NoLockedTargetDisplaysNone()
        {
            StringAssert.Contains("[None]", MuOnBaArHudRules.FormatLock(null));
        }

        [Test]
        public void LockedTargetDisplaysItsHealth()
        {
            StringAssert.Contains("Rider HP 25",
                MuOnBaArHudRules.FormatLock("Rider HP 25"));
        }

        [Test]
        public void KnockoutCountdownShowsThreeSeconds()
        {
            string actual = MuOnBaArHudRules.FormatStatus(0, 100, 292, 0, 1f / 64f);
            StringAssert.Contains("KO", actual);
            StringAssert.Contains("3.0s", actual);
        }

        [Test]
        public void RespawnShieldShowsOneAndHalfSeconds()
        {
            string actual = MuOnBaArHudRules.FormatStatus(100, 200, 0, 296, 1f / 64f);
            StringAssert.Contains("RESPAWN SHIELD", actual);
            StringAssert.Contains("1.5s", actual);
        }

        [Test]
        public void ShieldEndsAtExpiryTick()
        {
            StringAssert.Contains("WASD MOVE",
                MuOnBaArHudRules.FormatStatus(100, 296, 0, 296, 1f / 64f));
        }

        [Test]
        public void KnockoutStatusHasPriorityOverShield()
        {
            StringAssert.StartsWith("KO",
                MuOnBaArHudRules.FormatStatus(0, 100, 200, 300, 0.02f));
        }
    }
}
