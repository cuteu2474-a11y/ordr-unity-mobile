using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalA04REvidenceTests
    {
        [TestCase(5, -0.04)]
        [TestCase(6, -0.05)]
        [TestCase(11, -0.10)]
        [TestCase(14, -0.13)]
        [TestCase(26, -0.25)]
        public void Isr2ValuesMatchOriginalW3aOverrides(int level, double expected)
        {
            double value;
            Assert.IsTrue(Ordr323OriginalA04R.TryReadIsr2(level, out value));
            Assert.AreEqual(expected, value, 0.0000001);
        }

        [Test]
        public void UnknownOriginalFirstFourLevelsAndBeyondTwentySixAreNotInvented()
        {
            double ignored;
            foreach (int level in new[] { -1, 0, 1, 2, 3, 4, 27, 100 })
                Assert.IsFalse(Ordr323OriginalA04R.TryReadIsr2(level, out ignored));
        }

        [Test]
        public void Bd8AddsAbilityAtLevelOneBeforeFiveOrEightLevelIncrease()
        {
            int result;
            Assert.IsTrue(Ordr323OriginalA04R.TryPlanIncrease(0, 5, out result));
            Assert.AreEqual(6, result);
            Assert.IsTrue(Ordr323OriginalA04R.TryPlanIncrease(0, 8, out result));
            Assert.AreEqual(9, result);
            Assert.IsTrue(Ordr323OriginalA04R.TryPlanIncrease(6, 5, out result));
            Assert.AreEqual(11, result);
            Assert.IsTrue(Ordr323OriginalA04R.TryPlanIncrease(6, 8, out result));
            Assert.AreEqual(14, result);
        }

        [Test]
        public void UnknownOrUnverifiedResultingAbilityLevelCannotAdvance()
        {
            int ignored;
            Assert.IsFalse(Ordr323OriginalA04R.TryPlanIncrease(-1, 5, out ignored));
            Assert.IsFalse(Ordr323OriginalA04R.TryPlanIncrease(22, 5, out ignored));
            Assert.IsFalse(Ordr323OriginalA04R.TryPlanIncrease(25, 8, out ignored));
            Assert.IsFalse(Ordr323OriginalA04R.TryPlanIncrease(0, 0, out ignored));
        }

        [Test]
        public void VerifiedBaselineMustBeExplicitOnceAndRemainsCanonical()
        {
            var enemy = new CombatEnemy("source-enemy", 1000);
            Assert.IsNull(enemy.OriginalA04RLevel);
            Assert.Throws<ArgumentException>(() =>
                enemy.SetVerifiedOriginalA04RBaseline(0, ""));
            Assert.IsNull(enemy.OriginalA04RLevel);

            enemy.SetVerifiedOriginalA04RBaseline(0, "TEST_ONLY source");
            Assert.AreEqual(0, enemy.OriginalA04RLevel);
            Assert.AreEqual("TEST_ONLY source", enemy.OriginalA04RProvenance);
            Assert.Throws<InvalidOperationException>(() =>
                enemy.SetVerifiedOriginalA04RBaseline(4, "TEST_ONLY duplicate"));
        }
    }
}
