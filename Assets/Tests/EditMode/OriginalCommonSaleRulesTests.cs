using System;
using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    // No player economy, owned unit, RNG stream or game session is mutated.
    public sealed class OriginalCommonSaleRulesTests
    {
        [Test]
        public void FirstTwoSalesDoNotConsumeRandomOrGrantWisp()
        {
            var first = OriginalCommonSaleRules.PlanAfterConfirmedSale(0, null);
            Assert.AreEqual(1, first.TotalSold);
            Assert.AreEqual(1, first.PointsTowardNextWisp);
            Assert.AreEqual(0, first.RandomWispsAwarded);
            Assert.AreEqual(0, first.BonusWoodAwarded);

            var second = OriginalCommonSaleRules.PlanAfterConfirmedSale(1, null);
            Assert.AreEqual(2, second.PointsTowardNextWisp);
            Assert.AreEqual(0, second.RandomWispsAwarded);
        }

        [Test]
        public void EveryThirdSaleGrantsWispAndBonusWoodUpToRoll35()
        {
            var pass = OriginalCommonSaleRules.PlanAfterConfirmedSale(2, 35);
            Assert.AreEqual(3, pass.TotalSold);
            Assert.AreEqual(0, pass.PointsTowardNextWisp);
            Assert.AreEqual(1, pass.RandomWispsAwarded);
            Assert.AreEqual(1, pass.BonusWoodAwarded);
            var fail = OriginalCommonSaleRules.PlanAfterConfirmedSale(5, 36);
            Assert.AreEqual(6, fail.TotalSold);
            Assert.AreEqual(1, fail.RandomWispsAwarded);
            Assert.AreEqual(0, fail.BonusWoodAwarded);
            Assert.AreEqual(1,
                OriginalCommonSaleRules.PlanAfterConfirmedSale(8, 1).BonusWoodAwarded);
            Assert.AreEqual(0,
                OriginalCommonSaleRules.PlanAfterConfirmedSale(8, 100).BonusWoodAwarded);
        }

        [Test]
        public void InvalidAndPrematureRandomUseIsRejected()
        {
            Assert.IsFalse(OriginalCommonSaleRules.RequiresBonusWoodRoll(0));
            Assert.IsTrue(OriginalCommonSaleRules.RequiresBonusWoodRoll(2));
            Assert.Throws<ArgumentException>(() =>
                OriginalCommonSaleRules.PlanAfterConfirmedSale(0, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                OriginalCommonSaleRules.PlanAfterConfirmedSale(2, null));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                OriginalCommonSaleRules.PlanAfterConfirmedSale(2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                OriginalCommonSaleRules.PlanAfterConfirmedSale(2, 101));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                OriginalCommonSaleRules.PlanAfterConfirmedSale(int.MaxValue, null));
        }
    }
}
