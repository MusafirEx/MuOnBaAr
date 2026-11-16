using MuOnBaAr.Runtime;
using NUnit.Framework;

namespace MuOnBaAr.Tests
{
    public sealed class RiderCombatFeedbackRulesTests
    {
        [Test]
        public void FullHealthShowsFullBar()
        {
            Assert.That(RiderCombatFeedbackRules.HealthFraction(100, 100),
                Is.EqualTo(1f));
        }

        [Test]
        public void HalfHealthShowsHalfBar()
        {
            Assert.That(RiderCombatFeedbackRules.HealthFraction(50, 100),
                Is.EqualTo(0.5f));
        }

        [Test]
        public void ZeroMaximumHealthProducesEmptyBar()
        {
            Assert.That(RiderCombatFeedbackRules.HealthFraction(10, 0), Is.Zero);
        }

        [Test]
        public void OverMaximumHealthIsClamped()
        {
            Assert.That(RiderCombatFeedbackRules.HealthFraction(150, 100),
                Is.EqualTo(1f));
        }

        [Test]
        public void NegativeHealthIsClamped()
        {
            Assert.That(RiderCombatFeedbackRules.HealthFraction(-5, 100), Is.Zero);
        }

        [Test]
        public void DamagePopupReportsActualDecrease()
        {
            Assert.That(RiderCombatFeedbackRules.DamageTaken(100, 65),
                Is.EqualTo(35));
        }

        [Test]
        public void HealingDoesNotCreateDamagePopup()
        {
            Assert.That(RiderCombatFeedbackRules.DamageTaken(0, 100), Is.Zero);
        }

        [Test]
        public void IdenticalAttackSequenceDoesNotFlash()
        {
            Assert.That(RiderCombatFeedbackRules.IsNewAttack(3, 3), Is.False);
        }

        [Test]
        public void ChangedAttackSequenceStartsFlash()
        {
            Assert.That(RiderCombatFeedbackRules.IsNewAttack(3, 4), Is.True);
        }

        [Test]
        public void ProtectionEndsExactlyAtExpiryTick()
        {
            Assert.That(RiderCombatFeedbackRules.IsProtectionActive(100, 49, 50),
                Is.True);
            Assert.That(RiderCombatFeedbackRules.IsProtectionActive(100, 50, 50),
                Is.False);
        }

        [Test]
        public void KnockedOutRiderCannotDisplayRespawnShield()
        {
            Assert.That(RiderCombatFeedbackRules.IsProtectionActive(0, 10, 50),
                Is.False);
        }
    }
}
