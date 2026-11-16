using MuOnBaAr.Runtime;
using NUnit.Framework;

namespace MuOnBaAr.Tests
{
    public sealed class RiderLocomotionRulesTests
    {
        [TestCase(0f, RiderLocomotionRules.Idle)]
        [TestCase(0.08f, RiderLocomotionRules.Idle)]
        [TestCase(0.10f, RiderLocomotionRules.Walk)]
        [TestCase(0.35f, RiderLocomotionRules.Walk)]
        [TestCase(0.60f, RiderLocomotionRules.Run)]
        [TestCase(1.00f, RiderLocomotionRules.Run)]
        [TestCase(1.15f, RiderLocomotionRules.Sprint)]
        [TestCase(1.50f, RiderLocomotionRules.Sprint)]
        public void SpeedSelectsExpectedLocomotion(float speed, float expected)
        {
            Assert.That(RiderLocomotionRules.LevelForSpeed(speed), Is.EqualTo(expected));
        }

        [Test]
        public void InvalidOrNegativeSpeedIsIdle()
        {
            Assert.That(RiderLocomotionRules.LevelForSpeed(-1f),
                Is.EqualTo(RiderLocomotionRules.Idle));
            Assert.That(RiderLocomotionRules.LevelForSpeed(float.NaN),
                Is.EqualTo(RiderLocomotionRules.Idle));
            Assert.That(RiderLocomotionRules.LevelForSpeed(float.PositiveInfinity),
                Is.EqualTo(RiderLocomotionRules.Idle));
        }
    }
}
