using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalRandomWispResourceExchangeTests
    {
        private sealed class FixedRoll : IOriginalWispRandom
        {
            private readonly Queue<int> rolls;
            public int Calls;
            public FixedRoll(params int[] values)
            {
                rolls = new Queue<int>(values);
            }
            public int NextInclusive(int minimum, int maximum)
            {
                Assert.AreEqual(1, minimum);
                Assert.AreEqual(100, maximum);
                Calls++;
                return rolls.Dequeue();
            }
        }

        private static GameSession RunningAtWave(int wave)
        {
            var session = new GameSession();
            session.Start();
            for (int i = 1; i <= wave; i++) session.BeginWave(i);
            return session;
        }

        [TestCase(1)]
        [TestCase(67)]
        public void RollOneThroughSixtySevenGrantsOneWoodAndConsumesOneWisp(int roll)
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add(WispResourceIds.Random, 3));
            var random = new FixedRoll(roll);
            var exchange = new OriginalRandomWispResourceExchange(wallet,
                RunningAtWave(2), random);
            var outcome = exchange.SpendOne();
            Assert.AreEqual("wood", outcome.RewardResourceId);
            Assert.AreEqual(1, outcome.RewardAmount);
            Assert.AreEqual(1, wallet.Balance("wood"));
            Assert.AreEqual(0, wallet.Balance("gold"));
            Assert.AreEqual(2, wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(1, random.Calls);
        }

        [TestCase(68)]
        [TestCase(100)]
        public void RollSixtyEightThroughHundredGrantsSeventyFivePerWave(int roll)
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add(WispResourceIds.Random, 2));
            var exchange = new OriginalRandomWispResourceExchange(wallet,
                RunningAtWave(4), new FixedRoll(roll));
            var outcome = exchange.SpendOne();
            Assert.AreEqual("gold", outcome.RewardResourceId);
            Assert.AreEqual(300L, outcome.RewardAmount);
            Assert.AreEqual(300L, wallet.Balance("gold"));
            Assert.AreEqual(1L, wallet.Balance(WispResourceIds.Random));
        }

        [Test]
        public void BlockUnstartedAndRoundZeroWithoutConsumingOrRolling()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add(WispResourceIds.Random, 2));
            var game = new GameSession();
            var random = new FixedRoll();
            var exchange = new OriginalRandomWispResourceExchange(wallet, game, random);
            Assert.IsNull(exchange.SpendOne());
            game.Start();
            Assert.IsNull(exchange.SpendOne());
            Assert.AreEqual(2L, wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(0, random.Calls);
        }

        [Test]
        public void InsufficientWispAndFinishedSessionRejectWithoutRolling()
        {
            var wallet = new EconomyWallet();
            var game = RunningAtWave(1);
            var random = new FixedRoll();
            var exchange = new OriginalRandomWispResourceExchange(wallet, game, random);
            Assert.IsNull(exchange.SpendOne());
            wallet.Grant(new ResourceBundle().Add(WispResourceIds.Random, 1));
            game.Finish(false);
            Assert.IsNull(exchange.SpendOne());
            Assert.AreEqual(0, random.Calls);
            Assert.AreEqual(1, wallet.Balance(WispResourceIds.Random));
        }

        [Test]
        public void InvalidRandomRangeAndOverflowLeaveBalancesUnchanged()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add(WispResourceIds.Random, 2));
            var game = RunningAtWave(1);
            var invalid = new OriginalRandomWispResourceExchange(wallet,
                game, new FixedRoll(101));
            Assert.Throws<InvalidOperationException>(() => invalid.SpendOne());
            wallet.Grant(new ResourceBundle().Add("gold", long.MaxValue));
            var overflow = new OriginalRandomWispResourceExchange(wallet,
                game, new FixedRoll(68));
            Assert.Throws<OverflowException>(() => overflow.SpendOne());
            Assert.AreEqual(long.MaxValue, wallet.Balance("gold"));
            Assert.AreEqual(2, wallet.Balance(WispResourceIds.Random));
        }

        [Test]
        public void ExistingEconomyTabsRemainAvailableAndRandomExchangeIsTapOnly()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add(WispResourceIds.Random, 2));
            var random = new FixedRoll(67);
            var bridge = new MobileManualInteraction();
            bridge.ConfigureRandomWispResourceExchange(
                new OriginalRandomWispResourceExchange(wallet, RunningAtWave(1), random));

            Assert.IsTrue(bridge.HasEconomyService);
            var offers = bridge.ListEconomyOffers(ManualEconomyCategory.Wisp);
            Assert.AreEqual(1, offers.Count);
            Assert.AreEqual(OriginalRandomWispResourceExchange.OfferId, offers[0].Id);
            Assert.AreEqual(0, bridge.ListEconomyOffers(ManualEconomyCategory.Shop).Count);
            Assert.AreEqual(0, random.Calls);
            Assert.AreEqual(2L, wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(ManualActionState.Succeeded,
                bridge.RequestEconomyAction(ManualEconomyCategory.Wisp,
                    OriginalRandomWispResourceExchange.OfferId).State);
            Assert.AreEqual(1L, wallet.Balance("wood"));
            Assert.AreEqual(1L, wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(ManualActionState.NotImplemented,
                bridge.RequestEconomyAction(ManualEconomyCategory.Gambling, "invalid").State);
        }
    }
}
