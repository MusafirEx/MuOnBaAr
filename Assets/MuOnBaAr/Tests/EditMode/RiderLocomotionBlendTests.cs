using MuOnBaAr.Runtime;
using NUnit.Framework;

namespace MuOnBaAr.Tests
{
    public sealed class RiderLocomotionBlendTests
    {
        // These values match the currently user-authored ACL00.controller:
        // Idle=0, Walk=0.4, Run=1.4, Sprint=2.4.
        [TestCase(0f, 0f)]
        [TestCase(0.05f, 0f)]
        [TestCase(0.10f, 0f)]
        [TestCase(0.25f, 0.2f)]
        [TestCase(0.40f, 0.4f)]
        [TestCase(0.70f, 0.9f)]
        [TestCase(1.00f, 1.4f)]
        [TestCase(1.25f, 1.9f)]
        [TestCase(1.50f, 2.4f)]
        [TestCase(2.0f, 2.4f)]
        public void SpeedMapsToAuthoredBlendTree(float normalizedSpeed, float expected)
        {
            Assert.That(RiderLocomotionRules.BlendValueForSpeed(normalizedSpeed),
                Is.EqualTo(expected).Within(0.0001f));
        }

        [TestCase(false, 1f, 1.4f)]
        [TestCase(true, 1.5f, 2.4f)]
        public void FullInputSprintMultiplierMatchesAnimatorTargets(
            bool sprintHeld, float expectedSpeed, float expectedBlend)
        {
            float speed = RiderLocomotionRules.MovementSpeedMultiplier(sprintHeld);
            Assert.That(speed, Is.EqualTo(expectedSpeed).Within(0.0001f));
            Assert.That(RiderLocomotionRules.BlendValueForSpeed(speed),
                Is.EqualTo(expectedBlend).Within(0.0001f));
        }

        [Test]
        public void InvalidSpeedStaysIdle()
        {
            Assert.That(RiderLocomotionRules.BlendValueForSpeed(-2f), Is.Zero);
            Assert.That(RiderLocomotionRules.BlendValueForSpeed(float.NaN), Is.Zero);
            Assert.That(RiderLocomotionRules.BlendValueForSpeed(float.PositiveInfinity), Is.Zero);
        }
    }
}
