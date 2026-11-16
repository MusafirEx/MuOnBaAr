using MuOnBaAr.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace MuOnBaAr.Tests
{
    public sealed class RiderCombatRulesTests
    {
        [Test]
        public void LivingActorCanAttackAfterCooldown()
        {
            Assert.That(RiderCombatRules.CanAttack(100, 20, 20), Is.True);
        }

        [Test]
        public void AttackRespectsCooldown()
        {
            Assert.That(RiderCombatRules.CanAttack(100, 19, 20), Is.False);
        }

        [Test]
        public void DefeatedActorCannotAttack()
        {
            Assert.That(RiderCombatRules.CanAttack(0, 100, 0), Is.False);
        }

        [Test]
        public void TargetDirectlyAheadInsideRangeIsHit()
        {
            Assert.That(RiderCombatRules.IsInMeleeArc(
                Vector3.zero, new Vector3(0f, 0f, 2f), Vector2.up, 2.75f, 75f),
                Is.True);
        }

        [Test]
        public void TargetOutsideRangeIsNotHit()
        {
            Assert.That(RiderCombatRules.IsInMeleeArc(
                Vector3.zero, new Vector3(0f, 0f, 3f), Vector2.up, 2.75f, 75f),
                Is.False);
        }

        [Test]
        public void TargetBehindAimIsNotHit()
        {
            Assert.That(RiderCombatRules.IsInMeleeArc(
                Vector3.zero, new Vector3(0f, 0f, -1f), Vector2.up, 2.75f, 75f),
                Is.False);
        }

        [Test]
        public void ZeroAimIsNotAValidAttackDirection()
        {
            Assert.That(RiderCombatRules.IsInMeleeArc(
                Vector3.zero, new Vector3(0f, 0f, 1f), Vector2.zero, 2.75f, 75f),
                Is.False);
        }

        [Test]
        public void FormMultiplierChangesDamage()
        {
            Assert.That(RiderCombatRules.CalculateDamage(10, 1.5f), Is.EqualTo(15));
            Assert.That(RiderCombatRules.CalculateDamage(10, 1f), Is.EqualTo(10));
        }

        [Test]
        public void DamageNeverBecomesNegative()
        {
            Assert.That(RiderCombatRules.CalculateDamage(-5, 2f), Is.Zero);
            Assert.That(RiderCombatRules.CalculateDamage(10, -1f), Is.Zero);
        }
    }
}
