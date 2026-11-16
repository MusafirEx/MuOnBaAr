using MuOnBaAr.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace MuOnBaAr.Tests
{
    public sealed class RiderMeleeFacingTests
    {
        private static Vector2 Facing(float yawDegrees)
        {
            return RiderCombatRules.PlanarForward(
                Quaternion.Euler(0f, yawDegrees, 0f));
        }

        private static bool CanHit(float yawDegrees, Vector3 target)
        {
            return RiderCombatRules.IsInMeleeArc(
                Vector3.zero, target, Facing(yawDegrees),
                2.75f, 75f);
        }

        [Test]
        public void FacingZeroDegreesHitsPositiveZ()
        {
            Assert.That(Facing(0f).y, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(CanHit(0f, Vector3.forward * 2f), Is.True);
        }

        [Test]
        public void FacingNinetyDegreesHitsPositiveXButNotPositiveZ()
        {
            Assert.That(Facing(90f).x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(CanHit(90f, Vector3.right * 2f), Is.True);
            Assert.That(CanHit(90f, Vector3.forward * 2f), Is.False);
        }

        [Test]
        public void FacingOneEightyDegreesHitsNegativeZButNotPositiveZ()
        {
            Assert.That(Facing(180f).y, Is.EqualTo(-1f).Within(0.0001f));
            Assert.That(CanHit(180f, Vector3.back * 2f), Is.True);
            Assert.That(CanHit(180f, Vector3.forward * 2f), Is.False);
        }

        [Test]
        public void FacingTwoSeventyDegreesHitsNegativeX()
        {
            Assert.That(Facing(270f).x, Is.EqualTo(-1f).Within(0.0001f));
            Assert.That(CanHit(270f, Vector3.left * 2f), Is.True);
        }

        [Test]
        public void BehindCharacterIsNotHittableEvenIfCameraFacesBehind()
        {
            // Camera yaw is not an input to the authoritative facing rule.
            Assert.That(CanHit(0f, Vector3.back * 2f), Is.False);
        }

        [Test]
        public void PitchDoesNotChangeHorizontalAttackDirection()
        {
            Vector2 facing = RiderCombatRules.PlanarForward(
                Quaternion.Euler(30f, 45f, 0f));
            Assert.That(facing.magnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(facing.x, Is.EqualTo(0.7071068f).Within(0.0001f));
            Assert.That(facing.y, Is.EqualTo(0.7071068f).Within(0.0001f));
        }

        [Test]
        public void StraightUpFacingHasNoHorizontalAttackArc()
        {
            Vector2 facing = RiderCombatRules.PlanarForward(
                Quaternion.Euler(-90f, 0f, 0f));
            Assert.That(facing.sqrMagnitude, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(RiderCombatRules.IsInMeleeArc(
                Vector3.zero, Vector3.forward, facing, 2.75f, 75f),
                Is.False);
        }
    }
}
