using MuOnBaAr.Runtime;
using NUnit.Framework;

namespace MuOnBaAr.Tests
{
    public sealed class RiderEnergyOnHitRulesTests
    {
        [Test]
        public void SuccessfulHitRestoresConfiguredAmount()
        {
            Assert.That(RiderCombatRules.RestoreEnergyOnSuccessfulHit(
                40, 100, 5, 25), Is.EqualTo(45));
        }

        [Test]
        public void EnergyGainIsCappedAtMax()
        {
            Assert.That(RiderCombatRules.RestoreEnergyOnSuccessfulHit(
                98, 100, 5, 1), Is.EqualTo(100));
        }

        [Test]
        public void MissingDamageDoesNotGrantEnergy()
        {
            Assert.That(RiderCombatRules.RestoreEnergyOnSuccessfulHit(
                40, 100, 5, 0), Is.EqualTo(40));
        }

        [Test]
        public void EnergyGainZeroDisablesFeature()
        {
            Assert.That(RiderCombatRules.RestoreEnergyOnSuccessfulHit(
                40, 100, 0, 25), Is.EqualTo(40));
        }

        [Test]
        public void LargeGainSaturatesWithoutOverflow()
        {
            Assert.That(RiderCombatRules.RestoreEnergyOnSuccessfulHit(
                40, 100, int.MaxValue, 10), Is.EqualTo(100));
        }

        [Test]
        public void FinishingBlowStillRestoresEnergy()
        {
            Assert.That(RiderCombatRules.RestoreEnergyOnSuccessfulHit(
                40, 100, 5, 1), Is.EqualTo(45));
        }
    }
}
