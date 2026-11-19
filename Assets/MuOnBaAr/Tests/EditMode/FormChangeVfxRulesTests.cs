using MuOnBaAr.Runtime;
using NUnit.Framework;

namespace MuOnBaAr.Tests
{
    public sealed class FormChangeVfxRulesTests
    {
        [Test]
        public void CueDoesNotFireBeforeItsStart()
        {
            Assert.That(FormChangeVfxRules.IsCueDue(0.49f, 0.5f), Is.False);
            Assert.That(FormChangeVfxRules.IsCueDue(0.5f, 0.5f), Is.True);
        }

        [Test]
        public void CueBeginsAtFullDuration()
        {
            Assert.That(FormChangeVfxRules.RemainingSeconds(0.5f, 0.5f, 2f),
                Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void LateJoinShowsOnlyRemainingDuration()
        {
            Assert.That(FormChangeVfxRules.RemainingSeconds(1.25f, 0.5f, 2f),
                Is.EqualTo(1.25f).Within(0.0001f));
        }

        [Test]
        public void ExpiredCueNeverRestarts()
        {
            Assert.That(FormChangeVfxRules.RemainingSeconds(2.5f, 0.5f, 2f),
                Is.Zero);
            Assert.That(FormChangeVfxRules.RemainingSeconds(3f, 0.5f, 2f),
                Is.Zero);
        }

        [Test]
        public void NoLifetimeBeforeCue()
        {
            Assert.That(FormChangeVfxRules.RemainingSeconds(0.4f, 0.5f, 2f),
                Is.Zero);
        }
    }
}
