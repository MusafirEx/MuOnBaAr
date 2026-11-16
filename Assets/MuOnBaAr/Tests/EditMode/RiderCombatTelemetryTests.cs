using MuOnBaAr.Runtime;
using NUnit.Framework;

namespace MuOnBaAr.Tests
{
    public sealed class RiderCombatTelemetryTests
    {
        [Test]
        public void MessagesHaveMcpEventMarker()
        {
            string text = RiderCombatTelemetry.Format("HIT", 100, "Player:1", "42", "damage=10");
            StringAssert.StartsWith("[WDMCP_EVENT][MuOnBaAr][COMBAT]", text);
        }

        [Test]
        public void MessageIncludesPlayerTickAndObject()
        {
            string text = RiderCombatTelemetry.Format("KO", 250, "Player:2", "99", "hp=0");
            StringAssert.Contains("event=KO tick=250 player=Player:2 object=99 hp=0", text);
        }

        [Test]
        public void EmptyDetailsDoNotAddTrailingSpace()
        {
            string text = RiderCombatTelemetry.Format("JOIN", 1, "Player:1", "1", "");
            Assert.That(text.EndsWith(" "), Is.False);
        }

        [Test]
        public void ProtectionReasonRemainsSearchable()
        {
            string text = RiderCombatTelemetry.Format(
                "BLOCKED", 300, "Player:1", "42", "reason=SPAWN_PROTECTION");
            StringAssert.Contains("event=BLOCKED", text);
            StringAssert.Contains("reason=SPAWN_PROTECTION", text);
        }
    }
}
