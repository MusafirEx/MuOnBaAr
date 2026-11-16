using MuOnBaAr.Runtime;
using NUnit.Framework;

namespace MuOnBaAr.Tests
{
    public sealed class RiderRespawnRulesTests
    {
        [Test]
        public void ThreeSecondsAtFiftyTicksPerSecondIsOneHundredFiftyTicks()
        {
            Assert.That(RiderRespawnRules.DurationTicks(3f, 0.02f), Is.EqualTo(150));
        }

        [Test]
        public void PositiveFractionalDurationRoundsUpToOneTick()
        {
            Assert.That(RiderRespawnRules.DurationTicks(0.001f, 0.02f), Is.EqualTo(1));
        }

        [Test]
        public void ZeroProtectionDurationCreatesNoExtraTicks()
        {
            Assert.That(RiderRespawnRules.DurationTicks(0f, 0.02f), Is.Zero);
        }

        [Test]
        public void InvalidTickDeltaCannotScheduleTime()
        {
            Assert.That(RiderRespawnRules.DurationTicks(3f, 0f), Is.Zero);
        }

        [Test]
        public void KnockedOutActorWaitsUntilExactRespawnTick()
        {
            Assert.That(RiderRespawnRules.ShouldRespawn(0, 199, 200), Is.False);
            Assert.That(RiderRespawnRules.ShouldRespawn(0, 200, 200), Is.True);
            Assert.That(RiderRespawnRules.ShouldRespawn(0, 201, 200), Is.True);
        }

        [Test]
        public void LivingActorNeverRespawns()
        {
            Assert.That(RiderRespawnRules.ShouldRespawn(1, 200, 200), Is.False);
        }

        [Test]
        public void MissingRespawnScheduleDoesNotRespawn()
        {
            Assert.That(RiderRespawnRules.ShouldRespawn(0, 200, 0), Is.False);
        }

        [Test]
        public void DefeatedActorCannotTakeMoreDamage()
        {
            Assert.That(RiderRespawnRules.CanTakeDamage(0, 200, 0), Is.False);
        }

        [Test]
        public void SpawnProtectionRejectsDamageUntilExpiry()
        {
            Assert.That(RiderRespawnRules.CanTakeDamage(100, 200, 201), Is.False);
            Assert.That(RiderRespawnRules.CanTakeDamage(100, 201, 201), Is.True);
        }

        [Test]
        public void LivingUnprotectedActorCanTakeDamage()
        {
            Assert.That(RiderRespawnRules.CanTakeDamage(100, 200, 0), Is.True);
        }
    }
}
