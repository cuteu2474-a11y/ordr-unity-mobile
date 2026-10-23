using System;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalHelperCowManaRechargeTests
    {
        private sealed class Cow : IOriginalHelperCowManaActor
        {
            public string OriginalRawcode { get; set; } = OriginalHelperCowRules.HelperRawcode;
            public int OwnerPlayerId { get; set; }
            public bool IsAlive { get; set; } = true;
            public double CurrentMana { get; private set; }
            public double MaximumMana { get; private set; }
            public bool FailNextSet;
            public Cow(double initial = 20, double maximum = 200)
            { CurrentMana = initial; MaximumMana = maximum; }
            public void SetMana(double mana)
            {
                CurrentMana = Math.Max(0, Math.Min(MaximumMana, mana));
                if (FailNextSet) { FailNextSet = false; throw new InvalidOperationException("TEST_ONLY"); }
            }
        }

        private sealed class FailCommitWorld : IOriginalHelperCowManaWorld
        {
            private readonly Cow cow;
            public FailCommitWorld(Cow actor) { cow = actor; }
            public bool TryRead(out OriginalHelperCowManaSnapshot snapshot)
            {
                snapshot = new OriginalHelperCowManaSnapshot(cow.CurrentMana, cow.MaximumMana);
                return true;
            }
            public IOriginalHelperCowManaTransaction BeginRecharge() => new Txn(cow);
            private sealed class Txn : IOriginalHelperCowManaTransaction
            {
                private readonly Cow cow;
                private readonly double before;
                public Txn(Cow actor) { cow = actor; before = cow.CurrentMana; }
                public double ApplyRecharge(int amount)
                { cow.SetMana(before + amount); return cow.CurrentMana; }
                public void Commit() { throw new InvalidOperationException("TEST_ONLY commit"); }
                public void Dispose() { cow.SetMana(before); }
            }
        }

        private static GameSession GameAt(int wave)
        {
            var g = new GameSession();
            g.Start();
            for (int i = 1; i <= wave; i++) g.BeginWave(i);
            return g;
        }

        private static EconomyWallet Wallet(long amount)
        {
            var wallet = new EconomyWallet();
            if (amount > 0) wallet.Grant(new ResourceBundle().Add(WispResourceIds.Random, amount));
            return wallet;
        }

        [TestCase(0, 20)]
        [TestCase(1, 21)]
        [TestCase(2, 23)]
        [TestCase(3, 24)]
        [TestCase(53, 99)]
        [TestCase(54, 100)]
        [TestCase(65, 100)]
        public void MatchesJassRoundFormula(int round, int expected)
        {
            Assert.AreEqual(expected, OriginalHelperCowRules.RechargeAmount(round));
        }

        [Test]
        public void RechargeUpdatesLiveCowAndSharedWallet()
        {
            var cow = new Cow(31, 210);
            var wallet = Wallet(2);
            var recharge = new OriginalHelperCowManaRecharge(wallet,
                GameAt(2), new OriginalHelperCowManaWorld(() => cow));
            var result = recharge.SpendOne();
            Assert.AreEqual(23, result.RequestedMana);
            Assert.AreEqual(54, result.CurrentMana);
            Assert.AreEqual(54, cow.CurrentMana);
            Assert.AreEqual(1, wallet.Balance(WispResourceIds.Random));
        }

        [Test]
        public void FullManaStillSpendsOneAsOriginalTriggerDoes()
        {
            var cow = new Cow(200, 200);
            var wallet = Wallet(1);
            var recharge = new OriginalHelperCowManaRecharge(wallet,
                GameAt(54), new OriginalHelperCowManaWorld(() => cow));
            Assert.AreEqual(200, recharge.SpendOne().CurrentMana);
            Assert.AreEqual(0, wallet.Balance(WispResourceIds.Random));
        }

        [Test]
        public void SourceCowAbsentDeadWrongTypeOrPlayerRejects()
        {
            var cow = new Cow();
            var wallet = Wallet(1);
            var game = GameAt(1);
            var recharge = new OriginalHelperCowManaRecharge(wallet, game,
                new OriginalHelperCowManaWorld(() => cow));
            cow.OriginalRawcode = "h003";
            Assert.IsNull(recharge.SpendOne());
            cow.OriginalRawcode = "h08A";
            cow.OwnerPlayerId = 1;
            Assert.IsNull(recharge.SpendOne());
            cow.OwnerPlayerId = 0;
            cow.IsAlive = false;
            Assert.IsNull(recharge.SpendOne());
            var absent = new OriginalHelperCowManaRecharge(wallet, game,
                new OriginalHelperCowManaWorld(() => null));
            Assert.IsNull(absent.SpendOne());
            Assert.AreEqual(1, wallet.Balance(WispResourceIds.Random));
        }

        [Test]
        public void FailingWorldSetterRollsBackManaAndWisp()
        {
            var cow = new Cow(40, 200) { FailNextSet = true };
            var wallet = Wallet(1);
            var recharge = new OriginalHelperCowManaRecharge(wallet, GameAt(2),
                new OriginalHelperCowManaWorld(() => cow));
            Assert.Throws<InvalidOperationException>(() => recharge.SpendOne());
            Assert.AreEqual(40, cow.CurrentMana);
            Assert.AreEqual(1, wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(63, recharge.SpendOne().CurrentMana);
        }

        [Test]
        public void FailedCommitRollsBackManaAndWisp()
        {
            var cow = new Cow(40, 200);
            var wallet = Wallet(1);
            var recharge = new OriginalHelperCowManaRecharge(wallet, GameAt(2),
                new FailCommitWorld(cow));
            Assert.Throws<InvalidOperationException>(() => recharge.SpendOne());
            Assert.AreEqual(40, cow.CurrentMana);
            Assert.AreEqual(1, wallet.Balance(WispResourceIds.Random));
        }

        [Test]
        public void BeforeSessionStartAndWithoutWispsNoCharge()
        {
            var cow = new Cow();
            var wallet = Wallet(1);
            var recharge = new OriginalHelperCowManaRecharge(wallet,
                new GameSession(), new OriginalHelperCowManaWorld(() => cow));
            Assert.IsNull(recharge.SpendOne());
            Assert.AreEqual(20, cow.CurrentMana);
            Assert.AreEqual(1, wallet.Balance(WispResourceIds.Random));
            Assert.IsFalse(new OriginalHelperCowManaRecharge(Wallet(0), GameAt(1),
                new OriginalHelperCowManaWorld(() => cow)).CanRecharge);
        }

        [Test]
        public void WispMenuUsesExplicitTapBeforeDebiting()
        {
            var cow = new Cow(10, 200);
            var wallet = Wallet(2);
            var bridge = new MobileManualInteraction();
            bridge.ConfigureHelperCowRecharge(new OriginalHelperCowManaRecharge(
                wallet, GameAt(1), new OriginalHelperCowManaWorld(() => cow)));
            var offers = bridge.ListEconomyOffers(ManualEconomyCategory.Wisp);
            Assert.AreEqual(1, offers.Count);
            Assert.AreEqual(OriginalHelperCowRules.OfferId, offers[0].Id);
            Assert.AreEqual(10, cow.CurrentMana);
            Assert.AreEqual(2, wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(ManualActionState.Succeeded,
                bridge.RequestEconomyAction(ManualEconomyCategory.Wisp,
                    OriginalHelperCowRules.OfferId).State);
            Assert.AreEqual(31, cow.CurrentMana);
            Assert.AreEqual(1, wallet.Balance(WispResourceIds.Random));
        }
    }
}
