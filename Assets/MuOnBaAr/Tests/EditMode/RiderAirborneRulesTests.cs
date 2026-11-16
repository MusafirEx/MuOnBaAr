using MuOnBaAr.Runtime;
using NUnit.Framework;
namespace MuOnBaAr.Tests
{
    public sealed class RiderAirborneRulesTests
    {
        [Test]
        public void NormalTimeUsesFullGravity() =>
            Assert.That(RiderAirborneRules.IntegrateVertical(8f, -20f, -30f, 0.02f, 1f),
                Is.EqualTo(7.6f).Within(0.0001f));

        [Test]
        public void ClockUpTenthSpeedScalesAccelerationAndDisplacement()
        {
            Assert.That(RiderAirborneRules.IntegrateVertical(8f, -20f, -30f, 0.02f, 0.1f),
                Is.EqualTo(7.96f).Within(0.0001f));
            Assert.That(RiderAirborneRules.WorldVerticalSpeed(8f, 0.1f),
                Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void FrozenClockPreservesVelocityButStopsMovement()
        {
            Assert.That(RiderAirborneRules.IntegrateVertical(8f, -20f, -30f, 0.02f, 0f),
                Is.EqualTo(8f));
            Assert.That(RiderAirborneRules.WorldVerticalSpeed(8f, 0f), Is.Zero);
        }

        [Test]
        public void GravityCapsAtTerminalFallSpeed() =>
            Assert.That(RiderAirborneRules.IntegrateVertical(-29.9f, -20f, -30f, 1f, 1f),
                Is.EqualTo(-30f));
    }
}
