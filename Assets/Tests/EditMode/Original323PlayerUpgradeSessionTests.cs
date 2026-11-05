using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323PlayerUpgradeSessionTests
    {
        private sealed class LiveUpgradeWorld : IOriginal323VerifiedUpgradeWorld
        {
            public bool Ready = true;
            public bool Accept = true;
            public int LiveLevel;
            public int UndoCount;
            public bool CanApply(Original323VerifiedUpgradeStep step) => Ready;
            public bool TryApply(Original323VerifiedUpgradeStep step, int previousLevel)
            {
                if (!Accept) return false;
                Assert.AreEqual(LiveLevel, previousLevel);
                LiveLevel = step.Level;
                return true;
            }
            public void Undo(Original323VerifiedUpgradeStep step, int previousLevel)
            {
                UndoCount++;
                LiveLevel = previousLevel;
            }
        }

        private static Original323VerifiedUpgradeStep Step(string rawcode, int level,
            long cost, double cooldown = 0)
        {
            return new Original323VerifiedUpgradeStep(rawcode, level,
                new ResourceBundle().Add("gold", cost),
                "fixture: source-observed level cost",
                "fixture: source-observed damage effect",
                "fixture: exact observed effect", cooldown);
        }

        [Test]
        public void BothShopsExposeAllEighteenOriginalRawcodesAndCaps()
        {
            var a = Original323PlayerBuildingResearchCatalog.ForBuilding(
                Original323PlayerBuildingResearchCatalog.FirstUpgradeBuilding);
            var b = Original323PlayerBuildingResearchCatalog.ForBuilding(
                Original323PlayerBuildingResearchCatalog.SecondUpgradeBuilding);
            Assert.AreEqual(8, a.Count);
            Assert.AreEqual(10, b.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in a) Assert.IsTrue(ids.Add(item.Rawcode));
            foreach (var item in b) Assert.IsTrue(ids.Add(item.Rawcode));
            Assert.AreEqual(18, ids.Count);
            Assert.AreEqual(7, a[0].MaximumLevel);
            Assert.AreEqual(21, b[0].MaximumLevel);
        }

        [Test]
        public void NoSourceAndNoEffectNeverSpendGold()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 100));
            var game = new GameSession(); game.Start();
            var blank = new Original323PlayerUpgradeSession(wallet, game, () => 0);
            Assert.AreEqual(Original323UpgradePurchaseStatus.SourceMissing,
                blank.Purchase("R00F"));
            var missingWorld = new Original323PlayerUpgradeSession(wallet, game,
                () => 0, new[] { Step("R00F", 1, 20) });
            Assert.AreEqual(Original323UpgradePurchaseStatus.WorldMissing,
                missingWorld.Purchase("R00F"));
            Assert.AreEqual(100, wallet.Balance("gold"));
            Assert.AreEqual(0, blank.LevelOf("R00F"));
        }

        [Test]
        public void EachLevelChargesItsOwnVerifiedCostAndAppliesLiveEffect()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 100));
            var game = new GameSession(); game.Start();
            var live = new LiveUpgradeWorld();
            var service = new Original323PlayerUpgradeSession(wallet, game, () => 0,
                new[] { Step("R00F", 1, 10), Step("R00F", 2, 25) }, live);
            Assert.AreEqual(Original323UpgradePurchaseStatus.Succeeded,
                service.Purchase("R00F"));
            Assert.AreEqual(1, service.LevelOf("R00F"));
            Assert.AreEqual(90, wallet.Balance("gold"));
            Assert.AreEqual(Original323UpgradePurchaseStatus.Succeeded,
                service.Purchase("R00F"));
            Assert.AreEqual(2, live.LiveLevel);
            Assert.AreEqual(65, wallet.Balance("gold"));
            Assert.AreEqual(Original323UpgradePurchaseStatus.SourceMissing,
                service.Purchase("R00F"));
            Assert.AreEqual(65, wallet.Balance("gold"));
        }

        [Test]
        public void RejectedEffectRollsBackDebitAndDurableReceipt()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 25));
            var game = new GameSession(); game.Start();
            var live = new LiveUpgradeWorld { Accept = false };
            var service = new Original323PlayerUpgradeSession(wallet, game, () => 0,
                new[] { Step("R032", 1, 20) }, live);
            Assert.AreEqual(Original323UpgradePurchaseStatus.EffectRejected,
                service.Purchase("R032"));
            Assert.AreEqual(25, wallet.Balance("gold"));
            Assert.IsFalse(wallet.HasCompletedTransaction("original323-upgrade:R032:1"));
            Assert.AreEqual(0, service.LevelOf("R032"));
            Assert.AreEqual(1, live.UndoCount);
            live.Accept = true;
            Assert.AreEqual(Original323UpgradePurchaseStatus.Succeeded,
                service.Purchase("R032"));
            Assert.AreEqual(5, wallet.Balance("gold"));
        }

        [Test]
        public void RunningStateFundsAndSourceCooldownAreEnforced()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 40));
            var game = new GameSession();
            var live = new LiveUpgradeWorld();
            double now = 1;
            var service = new Original323PlayerUpgradeSession(wallet, game, () => now,
                new[] { Step("R00F", 1, 30, 5), Step("R00F", 2, 20) }, live);
            Assert.AreEqual(Original323UpgradePurchaseStatus.NotRunning,
                service.Purchase("R00F"));
            game.Start();
            Assert.AreEqual(Original323UpgradePurchaseStatus.Succeeded,
                service.Purchase("R00F"));
            Assert.AreEqual(Original323UpgradePurchaseStatus.Cooldown,
                service.Purchase("R00F"));
            now = 10;
            Assert.AreEqual(Original323UpgradePurchaseStatus.InsufficientResources,
                service.Purchase("R00F"));
            Assert.AreEqual(10, wallet.Balance("gold"));
        }

        [Test]
        public void RejectResearchBuildingForeignLevelsAndDuplicateSteps()
        {
            var wallet = new EconomyWallet();
            var game = new GameSession();
            var service = new Original323PlayerUpgradeSession(wallet, game, () => 0);
            Assert.Throws<ArgumentException>(() => service.RegisterSourceStep(
                Step("Rhfc", 1, 1)));
            Assert.Throws<ArgumentException>(() => service.RegisterSourceStep(
                Step("R00F", 8, 1)));
            service.RegisterSourceStep(Step("R00F", 1, 1));
            Assert.Throws<InvalidOperationException>(() => service.RegisterSourceStep(
                Step("R00F", 1, 1)));
        }
    }
}
