using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceModelUnitGamblingCompletionTests
    {
        private sealed class Rolls : IOriginalWispRandom
        {
            private readonly Queue<int> values;
            public int Calls;
            public bool FailNext;
            public Rolls(params int[] rolls) { values = new Queue<int>(rolls); }
            public int NextInclusive(int min, int max)
            {
                Calls++;
                if (FailNext) { FailNext = false; throw new InvalidOperationException("injected RNG failure"); }
                int value = values.Dequeue();
                Assert.That(value, Is.InRange(min, max)); return value;
            }
        }
        private sealed class World : IOriginal323UnitGamblingWorld
        {
            public bool FailStage;
            public int Commits, Rollbacks;
            public List<string> Codes;
            public string Ability;
            public IOriginal323UnitGamblingTransaction Begin(IReadOnlyList<string> codes, string bonus)
            { Codes = new List<string>(codes); Ability = bonus; return new Transaction(this); }
            private sealed class Transaction : IOriginal323UnitGamblingTransaction
            {
                private readonly World owner;
                private bool committed;
                public Transaction(World value) { owner = value; }
                public string Stage() { if (owner.FailStage) throw new InvalidOperationException("spawn"); return "result"; }
                public void Commit() { committed = true; owner.Commits++; }
                public void Dispose() { if (!committed) owner.Rollbacks++; }
            }
        }
        private static GameSession At(int wave)
        {
            var game = new GameSession(); game.Start();
            for (int i = 1; i <= wave; i++) game.BeginWave(i);
            return game;
        }
        private static EconomyWallet Wallet()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 10000).Add("wood", 20)); return wallet;
        }
        private static ManualActionResult Buy(SourceModelUnitGambling service, OriginalGambleTier tier) =>
            service.Confirm(ManualEconomyCategory.Gambling, Original323UnitGamblingRules.OfferId(tier));

        [Test]
        public void LowFailureIsPaidAndSignalsOneFailedAttemptWithoutAnyRewardUnit()
        {
            var wallet = Wallet(); var world = new World(); var script = new Rolls(15); var native = new Rolls();
            int signals = 0;
            var service = new SourceModelUnitGambling(wallet, At(1), script, native, world, () => true,
                () => Original323GamblingNavigation.None, (receipt, tier, won) =>
                { Assert.AreEqual(OriginalGambleTier.Low, tier); Assert.IsFalse(won); signals++; });
            Assert.AreEqual(ManualActionState.Succeeded, Buy(service, OriginalGambleTier.Low).State);
            Assert.AreEqual(9800, wallet.Balance("gold")); Assert.AreEqual(19, wallet.Balance("wood"));
            Assert.IsEmpty(world.Codes); Assert.AreEqual(1, signals); Assert.AreEqual(0, native.Calls);
        }
        [Test]
        public void CasinoHighSkipsRayleighRollButStillConsumesZeroThresholdSpecialRoll()
        {
            var wallet = Wallet(); var world = new World(); var script = new Rolls(31, 100); var native = new Rolls(0);
            var service = new SourceModelUnitGambling(wallet, At(21), script, native, world, () => true,
                () => Original323GamblingNavigation.Casino);
            Assert.AreEqual(ManualActionState.Rejected, Buy(service, OriginalGambleTier.Low).State);
            Assert.AreEqual(0, script.Calls);
            Assert.AreEqual(ManualActionState.Succeeded, Buy(service, OriginalGambleTier.High).State);
            CollectionAssert.AreEqual(new[] { "h09X" }, world.Codes);
            Assert.AreEqual("A0YE", world.Ability);
            Assert.AreEqual(2, script.Calls); Assert.AreEqual(1, native.Calls);
            Assert.AreEqual(8000, wallet.Balance("gold")); Assert.AreEqual(16, wallet.Balance("wood"));
        }
        [Test]
        public void RiskHighFailureSpawnsTwoRealTokensAndRefundsExactlyOneWood()
        {
            var wallet = Wallet(); var world = new World(); var script = new Rolls(30);
            var service = new SourceModelUnitGambling(wallet, At(21), script, new Rolls(), world, () => true,
                () => Original323GamblingNavigation.RiskHedge);
            Buy(service, OriginalGambleTier.High);
            CollectionAssert.AreEqual(new[] { "h06G", "h06G" }, world.Codes);
            Assert.AreEqual(17, wallet.Balance("wood")); Assert.IsNull(world.Ability);
        }
        [Test]
        public void PartialDrawAndSpawnFailuresReuseEveryCompletedRollAndDoNotChargeOrSignalTwice()
        {
            var wallet = Wallet(); var world = new World { FailStage = true };
            var script = new Rolls(31, 46); var native = new Rolls(0) { FailNext = true }; int signals = 0;
            var service = new SourceModelUnitGambling(wallet, At(1), script, native, world, () => true,
                () => Original323GamblingNavigation.None, (r, t, w) => signals++);
            Assert.Throws<InvalidOperationException>(() => Buy(service, OriginalGambleTier.Middle));
            Assert.Throws<InvalidOperationException>(() => Buy(service, OriginalGambleTier.Middle));
            Assert.AreEqual(10000, wallet.Balance("gold")); Assert.AreEqual(0, signals);
            Assert.AreEqual(ManualActionState.Rejected, Buy(service, OriginalGambleTier.High).State);
            world.FailStage = false;
            wallet.Granted += _ => throw new InvalidOperationException("post-commit UI failure");
            Assert.AreEqual(ManualActionState.Succeeded, Buy(service, OriginalGambleTier.Middle).State);
            Assert.AreEqual(2, script.Calls); Assert.AreEqual(2, native.Calls);
            Assert.AreEqual(9000, wallet.Balance("gold")); Assert.AreEqual(18, wallet.Balance("wood"));
            Assert.AreEqual(1, signals); Assert.AreEqual(1, world.Commits);
        }
        [Test]
        public void CasinoConversionUsesSpentAttemptsAndSharesTheHelperNavigationLock()
        {
            var game = At(20); var wallet = Wallet(); var shared = new Original323NavigationSelection(game);
            wallet.Grant(new ResourceBundle().Add(Original323OtherworldGamblingRules.ChargesResource, 2)
                .Add(Original323OtherworldGamblingRules.AttemptsResource, 3));
            var navigation = new SourceModelGamblingNavigation(wallet, shared, () => true, () => false);
            var helper = new Ordr323OriginalHelperNavigation(game, new Ordr323OriginalHelperCowActor(0), shared);
            Assert.IsFalse(navigation.TryChoose(Original323GamblingNavigation.Casino));
            game.BeginWave(21);
            wallet.Granted += _ => { Assert.IsFalse(helper.TryChooseMaximumOutput()); throw new Exception("observer"); };
            Assert.IsTrue(navigation.TryChoose(Original323GamblingNavigation.Casino));
            Assert.AreEqual(7, wallet.Balance(Original323OtherworldGamblingRules.ChargesResource));
            Assert.AreEqual(3, navigation.RareRerolls);
            Assert.IsFalse(helper.TryChooseAlchemy());
            Assert.IsFalse(navigation.TryChoose(Original323GamblingNavigation.RiskHedge));
        }
        [Test]
        public void HelperChoiceOrPendingPurchaseBlocksGamblingNavigationWithoutConversion()
        {
            var game = At(21); var wallet = Wallet(); var shared = new Original323NavigationSelection(game);
            bool pending = true;
            var navigation = new SourceModelGamblingNavigation(wallet, shared, () => true, () => pending);
            Assert.IsFalse(navigation.TryChoose(Original323GamblingNavigation.RiskHedge));
            pending = false;
            Assert.IsTrue(new Ordr323OriginalHelperNavigation(game, new Ordr323OriginalHelperCowActor(0), shared)
                .TryChooseAlchemy());
            Assert.IsFalse(navigation.TryChoose(Original323GamblingNavigation.Casino));
            Assert.AreEqual(2, navigation.RareRerolls);
        }
    }
}
