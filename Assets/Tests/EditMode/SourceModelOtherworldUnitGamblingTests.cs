using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceModelOtherworldUnitGamblingTests
    {
        private sealed class Rolls : IOriginalWispRandom
        {
            private readonly Queue<int> values;
            public readonly List<string> Ranges = new List<string>();
            public Rolls(params int[] input) { values = new Queue<int>(input); }
            public int NextInclusive(int min, int max)
            { Ranges.Add(min + ".." + max); return values.Dequeue(); }
        }
        private sealed class World : IOriginal323OtherworldGamblingWorld
        {
            public bool Reject, Throw;
            public int Alive, Committed;
            public Action BeforeSpawn;
            public Original323OtherworldGamblingOutcome Last;
            public IOriginal323OtherworldGamblingTransaction Begin(Original323OtherworldGamblingOutcome outcome)
            { Last = outcome; return new Spawn(this); }
            private sealed class Spawn : IOriginal323OtherworldGamblingTransaction
            {
                private readonly World world;
                private bool staged, committed;
                public Spawn(World owner) { world = owner; }
                public bool TrySpawn(out long id, out string name)
                {
                    world.BeforeSpawn?.Invoke();
                    id = 0; name = null;
                    if (world.Reject) return false;
                    world.Alive++; staged = true;
                    if (world.Throw) throw new InvalidOperationException("TEST_ONLY spawn failure");
                    id = 1; name = "TEST_ONLY " + world.Last.Rawcode; return true;
                }
                public void Commit() { committed = true; world.Committed++; }
                public void Dispose() { if (staged && !committed) world.Alive--; }
            }
        }
        private sealed class Shop
        {
            public readonly EconomyWallet Wallet = new EconomyWallet();
            public readonly GameSession Game = new GameSession();
            public readonly World World = new World();
            public readonly Rolls Random;
            public readonly SourceModelOtherworldUnitGambling Service;
            public bool Paused;
            public Shop(Original323GamblingNavigation navigation, params int[] rolls)
            {
                Random = new Rolls(rolls);
                Game.Start();
                Wallet.Grant(new ResourceBundle().Add("gold", 100000).Add("wood", 100)
                    .Add(Original323OtherworldGamblingRules.ChargesResource, 10));
                Service = new SourceModelOtherworldUnitGambling(Wallet, Game, Random,
                    () => !Paused, World, () => navigation);
            }
            public ManualActionResult Buy() => Service.Confirm(ManualEconomyCategory.Gambling,
                SourceModelOtherworldUnitGambling.OfferId);
        }

        [TestCase(1, 0, "h06U")]
        [TestCase(15, 13, "h06X")]
        public void SourceSuccessBoundaryUsesLiteralKeOrderAndAtomicCost(int first, int index, string rawcode)
        {
            var s = new Shop(Original323GamblingNavigation.None, first, index);
            Assert.AreEqual(ManualActionState.Succeeded, s.Buy().State);
            Assert.IsTrue(s.World.Last.Won);
            Assert.AreEqual(rawcode, s.World.Last.Rawcode);
            Assert.AreEqual("A0YZ", s.World.Last.BonusAbility);
            Assert.AreEqual(96500, s.Wallet.Balance("gold"));
            Assert.AreEqual(95, s.Wallet.Balance("wood"));
            Assert.AreEqual(9, s.Service.Charges);
            Assert.AreEqual(1, s.Service.Attempts);
            Assert.AreEqual(1, s.Service.Wins);
            Assert.AreEqual(1, s.World.Alive);
            CollectionAssert.AreEqual(new[] { "1..100", "0..13" }, s.Random.Ranges);
        }

        [TestCase(16)] [TestCase(100)]
        public void OrdinaryFailureConsumesOnlyBranchDrawAndCreatesRealToken(int first)
        {
            var s = new Shop(Original323GamblingNavigation.None, first);
            Assert.AreEqual(ManualActionState.Succeeded, s.Buy().State);
            Assert.IsFalse(s.World.Last.Won);
            Assert.AreEqual("h06G", s.World.Last.Rawcode);
            Assert.IsNull(s.World.Last.BonusAbility);
            Assert.AreEqual(1, s.Service.Attempts);
            Assert.AreEqual(0, s.Service.Wins);
            CollectionAssert.AreEqual(new[] { "1..100" }, s.Random.Ranges);
        }

        [Test]
        public void CasinoFailureUsesScriptKdZeroThroughFortyOneAndRemainsFailure()
        {
            var s = new Shop(Original323GamblingNavigation.Casino, 16, 1, 41);
            Assert.AreEqual(ManualActionState.Succeeded, s.Buy().State);
            Assert.IsFalse(s.World.Last.Won);
            Assert.AreEqual("h05K", s.World.Last.Rawcode);
            Assert.AreEqual("A0YE", s.World.Last.BonusAbility);
            Assert.AreEqual(0, s.Service.Wins);
            CollectionAssert.AreEqual(new[] { "1..100", "1..2", "0..41" }, s.Random.Ranges);
            Assert.IsFalse(Original323OtherworldGamblingRules.CasinoRareRawcodes.Contains("h05X"));
        }

        [Test]
        public void CasinoSecondBranchCreatesTokenWithoutAnIndexDraw()
        {
            var s = new Shop(Original323GamblingNavigation.Casino, 100, 2);
            Assert.AreEqual(ManualActionState.Succeeded, s.Buy().State);
            Assert.AreEqual("h06G", s.World.Last.Rawcode);
            CollectionAssert.AreEqual(new[] { "1..100", "1..2" }, s.Random.Ranges);
        }

        [Test]
        public void RiskHedgeAddsFifteenOnlyAfterPaidFailureAndResetsOnWin()
        {
            var s = new Shop(Original323GamblingNavigation.RiskHedge, 16, 30, 2);
            Assert.AreEqual(ManualActionState.Succeeded, s.Buy().State);
            Assert.AreEqual(15, s.Service.RiskBonus);
            Assert.AreEqual(30, s.Service.SuccessPercent);
            Assert.AreEqual(ManualActionState.Succeeded, s.Buy().State);
            Assert.IsTrue(s.World.Last.Won);
            Assert.AreEqual("h070", s.World.Last.Rawcode);
            Assert.AreEqual(0, s.Service.RiskBonus);
            Assert.AreEqual(15, s.Service.SuccessPercent);
            Assert.AreEqual(2, s.Service.Attempts);
            Assert.AreEqual(1, s.Service.Wins);
        }

        [Test]
        public void RiskHedgeSourceThresholdAboveOneHundredGuaranteesSuccess()
        {
            var s = new Shop(Original323GamblingNavigation.RiskHedge, 100, 0);
            s.Wallet.Grant(new ResourceBundle().Add(Original323OtherworldGamblingRules.RiskBonusResource, 90));
            Assert.AreEqual(100, s.Service.SuccessPercent);
            Assert.AreEqual(ManualActionState.Succeeded, s.Buy().State);
            Assert.IsTrue(s.World.Last.Won);
            Assert.AreEqual(0, s.Service.RiskBonus);
        }

        [Test]
        public void RejectedAndPausedRequestsNeverRollOrSpend()
        {
            var s = new Shop(Original323GamblingNavigation.None);
            for (int i = 0; i < 3; i++) s.Service.AvailableOffers(ManualEconomyCategory.Gambling);
            s.Paused = true;
            Assert.AreEqual(ManualActionState.Rejected, s.Buy().State);
            s.Paused = false;
            s.Wallet.TryExchange(new ResourceBundle().Add(Original323OtherworldGamblingRules.ChargesResource, 10), new ResourceBundle());
            Assert.AreEqual(ManualActionState.Rejected, s.Buy().State);
            Assert.AreEqual(0, s.Random.Ranges.Count);
            Assert.AreEqual(100000, s.Wallet.Balance("gold"));
            Assert.AreEqual(100, s.Wallet.Balance("wood"));
            Assert.AreEqual(0, s.Service.Attempts);
        }

        [TestCase("gold", 96501)]
        [TestCase("wood", 96)]
        public void EachInsufficientCurrencyRejectsBeforeAnyDrawOrSpawn(string resource, int spend)
        {
            var s = new Shop(Original323GamblingNavigation.None);
            s.Wallet.TryExchange(new ResourceBundle().Add(resource, spend), new ResourceBundle());
            Assert.AreEqual(ManualActionState.Rejected, s.Buy().State);
            Assert.AreEqual(0, s.Random.Ranges.Count);
            Assert.AreEqual(0, s.World.Alive);
            Assert.AreEqual(10, s.Service.Charges);
            Assert.AreEqual(0, s.Service.Attempts);
        }

        [Test]
        public void SpawnFailureRollsBackActorsAndRetainsExactDrawForRetry()
        {
            var s = new Shop(Original323GamblingNavigation.None, 15, 4);
            s.World.Throw = true;
            Assert.Throws<InvalidOperationException>(() => s.Buy());
            Assert.AreEqual(0, s.World.Alive);
            Assert.AreEqual(100000, s.Wallet.Balance("gold"));
            Assert.AreEqual(10, s.Service.Charges);
            Assert.AreEqual(0, s.Service.Attempts);
            s.World.Throw = false;
            Assert.AreEqual(ManualActionState.Succeeded, s.Buy().State);
            Assert.AreEqual("h09L", s.World.Last.Rawcode);
            Assert.AreEqual(2, s.Random.Ranges.Count);
            Assert.AreEqual(1, s.World.Alive);
            Assert.AreEqual(1, s.World.Committed);
        }

        [Test]
        public void DebitFailureAfterStagingDoesNotLeaveAFreeUnitOrReroll()
        {
            var s = new Shop(Original323GamblingNavigation.None, 15, 3);
            s.World.BeforeSpawn = () => s.Wallet.TryExchange(new ResourceBundle().Add("gold", 100000), new ResourceBundle());
            Assert.AreEqual(ManualActionState.Rejected, s.Buy().State);
            Assert.AreEqual(0, s.World.Alive);
            Assert.AreEqual(10, s.Service.Charges);
            Assert.AreEqual(100, s.Wallet.Balance("wood"));
            Assert.AreEqual(0, s.Service.Attempts);
            s.World.BeforeSpawn = null;
            s.Wallet.Grant(new ResourceBundle().Add("gold", 3500));
            Assert.AreEqual(ManualActionState.Succeeded, s.Buy().State);
            Assert.AreEqual("h06T", s.World.Last.Rawcode);
            Assert.AreEqual(2, s.Random.Ranges.Count);
        }

        [Test]
        public void PostCommitObserverFailureCannotUndoPaidResultOrReenterService()
        {
            var s = new Shop(Original323GamblingNavigation.None, 100);
            int notices = 0;
            s.Service.PostCommitObserverFailed += _ => notices++;
            s.Wallet.Granted += _ => {
                Assert.AreEqual(ManualActionState.Rejected, s.Buy().State);
                throw new InvalidOperationException("TEST_ONLY observer");
            };
            Assert.AreEqual(ManualActionState.Succeeded, s.Buy().State);
            Assert.AreEqual(1, notices);
            Assert.AreEqual(1, s.Service.Attempts);
            Assert.AreEqual(1, s.World.Committed);
            Assert.AreEqual(1, s.World.Alive);
            Assert.AreEqual(9, s.Service.Charges);
        }

        [Test]
        public void WalletSnapshotPreservesChargesPityCountersAndPaidReceipts()
        {
            var s = new Shop(Original323GamblingNavigation.RiskHedge, 100);
            Assert.AreEqual(ManualActionState.Succeeded, s.Buy().State);
            var restored = new EconomyWallet(); restored.RestoreState(s.Wallet.CaptureState());
            Assert.AreEqual(9, restored.Balance(Original323OtherworldGamblingRules.ChargesResource));
            Assert.AreEqual(15, restored.Balance(Original323OtherworldGamblingRules.RiskBonusResource));
            Assert.AreEqual(1, restored.Balance(Original323OtherworldGamblingRules.AttemptsResource));
            Assert.IsTrue(restored.HasCompletedTransaction("source:unit-gambling:CJM:purchase:1"));
        }

        [Test]
        public void GraduationPreservesTheSameOtherworldOfferAndCharges()
        {
            var s = new Shop(Original323GamblingNavigation.None, 100);
            var desk = new SourceModelMoneyGamblingDesk(s.Wallet, s.Game, s.Random, () => 0, () => true);
            desk.ConfigureOtherworld(s.Service);
            Assert.IsTrue(desk.AvailableOffers(ManualEconomyCategory.Gambling)
                .Any(x => x.Id == SourceModelOtherworldUnitGambling.OfferId));
            s.Wallet.Grant(new ResourceBundle().Add(SourceModelMoneyGamblingDesk.GraduatedResource, 1));
            Assert.IsTrue(desk.AvailableOffers(ManualEconomyCategory.Gambling)
                .Any(x => x.Id == SourceModelOtherworldUnitGambling.OfferId));
            Assert.AreEqual(ManualActionState.Succeeded, desk.Confirm(ManualEconomyCategory.Gambling,
                SourceModelOtherworldUnitGambling.OfferId).State);
            Assert.AreEqual(9, s.Service.Charges);
        }

        [TestCase(Original323GamblingNavigation.None, 1)]
        [TestCase(Original323GamblingNavigation.Casino, 2)]
        public void ThirdCreepGrantsOncePerGameWithoutBeingConfusedWithEscapeOrOtherStages(
            Original323GamblingNavigation navigation, int amount)
        {
            var s = new Shop(navigation);
            var signal = typeof(SourceModelOtherworldUnitGambling).GetMethod(
                "RecordThirdCreepDefeated", BindingFlags.Instance | BindingFlags.NonPublic);
            signal.Invoke(s.Service, new object[] { "n00F" });
            Assert.AreEqual(10, s.Service.Charges);
            signal.Invoke(s.Service, new object[] { "n00G" });
            signal.Invoke(s.Service, new object[] { "n00G" });
            Assert.AreEqual(10 + amount, s.Service.Charges);
            Assert.AreEqual(0, s.Random.Ranges.Count);
            var saved = s.Wallet.CaptureState();
            s.Wallet.RestoreState(saved);
            signal.Invoke(s.Service, new object[] { "n00G" });
            Assert.AreEqual(10 + amount, s.Service.Charges);
        }
    }
}
