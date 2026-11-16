using MuOnBaAr.Runtime;
using NUnit.Framework;

namespace MuOnBaAr.Tests
{
    public sealed class RiderWorldHealthVisibilityTests
    {
        private static bool Visible(
            bool local = false, bool allowLocal = false,
            bool inFront = true, bool inViewport = true,
            bool overlapsHud = false, float distance = 5f,
            float maxDistance = 35f)
        {
            return RiderCombatFeedbackRules.ShouldDisplay(
                local, allowLocal, inFront, inViewport, overlapsHud,
                distance, maxDistance);
        }

        [Test]
        public void HidesLocalPlayersOverheadByDefault()
        {
            Assert.That(Visible(local: true), Is.False);
        }

        [Test]
        public void AllowsLocalOverheadWhenExplicitlyEnabled()
        {
            Assert.That(Visible(local: true, allowLocal: true), Is.True);
        }

        [Test]
        public void ShowsRemoteRiderInView()
        {
            Assert.That(Visible(), Is.True);
        }

        [Test]
        public void HidesBarWhenScreenHudOverlaps()
        {
            Assert.That(Visible(overlapsHud: true), Is.False);
        }

        [Test]
        public void HidesBarOutsideViewport()
        {
            Assert.That(Visible(inViewport: false), Is.False);
        }

        [Test]
        public void HidesBarBehindCamera()
        {
            Assert.That(Visible(inFront: false), Is.False);
        }

        [Test]
        public void HidesRemoteBarOutsideMaximumDistance()
        {
            Assert.That(Visible(distance: 35.01f), Is.False);
        }

        [Test]
        public void ShowsRemoteBarAtMaximumDistance()
        {
            Assert.That(Visible(distance: 35f), Is.True);
        }
    }
}
