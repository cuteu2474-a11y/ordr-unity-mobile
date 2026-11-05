using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323StoryNativeSourceRulesTests
    {
        private sealed class DoubleTrait : IOriginal323StoryTraitSelection
        {
            public bool HasDoublePointStoryTrait() { return true; }
        }

        [Test]
        public void SourceUniqueItemsRememberOwnershipEvenAfterFusionSpend()
        {
            var wallet = new EconomyWallet();
            Assert.IsFalse(Ordr.Mobile.Mobile.Original323FusionSourceItemLedger
                .WasEverObtained(wallet, "AI00"));
            wallet.Grant(new ResourceBundle()
                .Add(Ordr.Mobile.Mobile.Original323FusionSourceItemLedger.WalletId("AI00"), 1)
                .Add(Ordr.Mobile.Mobile.Original323FusionSourceItemLedger.EverObtainedWalletId("AI00"), 1));
            Assert.IsTrue(wallet.TryExchangeOnce("story-test-unique-spend",
                new ResourceBundle().Add(
                    Ordr.Mobile.Mobile.Original323FusionSourceItemLedger.WalletId("AI00"), 1),
                new ResourceBundle()));
            Assert.IsTrue(Ordr.Mobile.Mobile.Original323FusionSourceItemLedger
                .WasEverObtained(wallet, "AI00"));
            Assert.AreEqual("AI03", Original323SoloStoryScenario.SourceUniqueItemForStage(5));
            Assert.AreEqual("AI00", Original323SoloStoryScenario.SourceUniqueItemForStage(6));
            Assert.AreEqual("AI02", Original323SoloStoryScenario.SourceUniqueItemForStage(8));
            Assert.IsNull(Original323SoloStoryScenario.SourceUniqueItemForStage(7));
        }

        [Test]
        public void OriginalDoubleGamblingChargeBranchUsesExplicitCasinoSelection()
        {
            var queue = new Original323StoryNativeRewardQueue();
            queue.RecordCommittedClear(5); // earlier research does not lock trait selection
            var model = new Original323StoryMobileProgression();
            Assert.AreEqual(1, queue.ApplyPending(model));
            model.ConfigureOriginalStoryTraitSelection(new DoubleTrait());
            queue.RecordCommittedClear(8);
            Assert.AreEqual(2, queue.ApplyPending(model));
            Assert.AreEqual(2, model.GamblingCharges);
            Assert.AreEqual(0, model.TraitPoints);
            Assert.Throws<InvalidOperationException>(() =>
                model.ConfigureOriginalStoryTraitSelection(new DoubleTrait()));
            queue.RecordCommittedClear(9);
            Assert.AreEqual(2, queue.ApplyPending(model));
            Assert.AreEqual(4, model.GamblingCharges);
            Assert.AreEqual(1, queue.PendingCount); // Ancient ship is NOT created.
        }

        [Test]
        public void StoryGamblingChargesNeverFundTraitPurchases()
        {
            var wallet = new EconomyWallet();
            var queue = new Original323StoryNativeRewardQueue();
            var story = new Original323StoryMobileProgression(wallet);
            queue.RecordCommittedClear(8);
            Assert.AreEqual(2, queue.ApplyPending(story));
            Assert.AreEqual(1, story.GamblingCharges);
            Assert.AreEqual(0, wallet.Balance(OriginalMissionWalletRewards.TraitPointsId));
            new OriginalMissionWalletRewards(wallet).Grant("trait-mission", new[] {
                new OriginalMissionReward(OriginalMissionRewardKind.TraitPoint, "", 2)
            });
            Assert.AreEqual(2, wallet.Balance(
                SourceModelNativeCharacterRules.TraitPointsResourceId));
            Assert.AreEqual(0, queue.ApplyPending(story));
            Assert.AreEqual(2, wallet.Balance(OriginalMissionWalletRewards.TraitPointsId));
            Assert.IsTrue(wallet.TryExchangeOnce("satellite-cost",
                new ResourceBundle().Add(
                    SourceModelNativeCharacterRules.TraitPointsResourceId, 2),
                new ResourceBundle()));
            Assert.AreEqual(0, wallet.Balance(OriginalMissionWalletRewards.TraitPointsId));
            Assert.IsFalse(wallet.TryExchangeOnce("satellite-cost",
                new ResourceBundle().Add(
                    SourceModelNativeCharacterRules.TraitPointsResourceId, 2),
                new ResourceBundle()));
            Assert.AreEqual(0, wallet.Balance(OriginalMissionWalletRewards.TraitPointsId));
            Assert.AreEqual(1, story.GamblingCharges);
        }

        [Test]
        public void StoryChargeReceiptSurvivesPostCommitObserverFailureAndRetry()
        {
            var wallet = new EconomyWallet();
            wallet.Granted += _ => throw new InvalidOperationException("TEST_ONLY observer");
            var queue = new Original323StoryNativeRewardQueue();
            var story = new Original323StoryMobileProgression(wallet);
            queue.RecordCommittedClear(8);
            Assert.AreEqual(2, queue.ApplyPending(story));
            Assert.AreEqual(1, story.GamblingCharges);
            Assert.AreEqual(0, story.TraitPoints);
            Assert.AreEqual(0, wallet.Balance(OriginalMissionWalletRewards.TraitPointsId));
            Assert.IsTrue(wallet.HasCompletedTransaction(
                "story:native:8:GamblingCharges:BdL"));
            Assert.AreEqual(0, queue.ApplyPending(story));
            var restoredQueue = new Original323StoryNativeRewardQueue();
            restoredQueue.RecordCommittedClear(8);
            Assert.AreEqual(2, restoredQueue.ApplyPending(
                new Original323StoryMobileProgression(wallet)));
            Assert.AreEqual(1, wallet.Balance(Original323OtherworldGamblingRules.ChargesResource));
        }

        [Test]
        public void FailedStoryChargeWalletCreditLeavesTheNativeAwardPending()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add(
                Original323OtherworldGamblingRules.ChargesResource, long.MaxValue));
            var queue = new Original323StoryNativeRewardQueue();
            var story = new Original323StoryMobileProgression(wallet);
            queue.RecordCommittedClear(8);
            Assert.Throws<OverflowException>(() => queue.ApplyPending(story));
            Assert.AreEqual(1, queue.PendingCount);
            Assert.AreEqual(0, story.TraitPoints);
            Assert.AreEqual(long.MaxValue, wallet.Balance(
                Original323OtherworldGamblingRules.ChargesResource));
            Assert.IsFalse(wallet.HasCompletedTransaction(
                "story:native:8:GamblingCharges:BdL"));
        }

        [TestCase("h032", "h09Y", false)]
        [TestCase("h043", "h0A3", false)]
        [TestCase("h030", "h0A1", false)]
        [TestCase("h03G", "h0A0", true)]
        public void GreenBloodSourceBranchesAreFourExactTransformations(
            string source, string replacement, bool needsCen)
        {
            Original323GreenBloodPlan plan;
            Assert.IsTrue(Original323StoryGreenBloodRules.TryPlan(
                source, true, false, false, out plan));
            Assert.AreEqual(Original323GreenBloodActionKind.Transform, plan.Kind);
            Assert.AreEqual(replacement, plan.ReplacementRawcode);
            Assert.AreEqual(needsCen, plan.ExecuteOriginalCENAfterTransformation);
            Assert.IsEmpty(plan.AddedAbilityRawcodes);
            Assert.IsFalse(Original323StoryGreenBloodRules.TryPlan(
                source, false, false, false, out plan));
        }

        [Test]
        public void GreenBloodOtherLegendaryOnlyRequiresActualA912OrA07N()
        {
            Original323GreenBloodPlan plan;
            Assert.IsFalse(Original323StoryGreenBloodRules.TryPlan(
                "h0AA", true, false, false, out plan));
            Assert.IsTrue(Original323StoryGreenBloodRules.TryPlan(
                "h0AA", true, true, false, out plan));
            Assert.AreEqual(Original323GreenBloodActionKind.Enhance, plan.Kind);
            CollectionAssert.AreEqual(new[] { "A134", "A13C" },
                plan.AddedAbilityRawcodes);
            Assert.IsNull(plan.ReplacementRawcode);
        }

        [TestCase("h032", "SPH1")]
        [TestCase("h043", "SPH2")]
        [TestCase("h03G", "SPH3")]
        [TestCase("h030", "SPH4")]
        public void GreenBloodDirectCastMapsExactSourceActorToNativeRecipe(
            string originalRawcode, string recipe)
        {
            Assert.AreEqual(recipe,
                Original323StoryGreenBloodRules.SeraphimRecipe(originalRawcode));
            Assert.IsNull(Original323StoryGreenBloodRules.SeraphimRecipe("h0AA"));
        }

        [Test]
        public void GreenBloodNativeBurstUsesEachEnemyMaximumHealth()
        {
            Assert.AreEqual(825,
                Original323StoryGreenBloodRules.EnhancementBurstRadius);
            Assert.AreEqual(400000,
                Original323StoryGreenBloodRules.EnhancementBurstDamage(0));
            Assert.AreEqual(408000,
                Original323StoryGreenBloodRules.EnhancementBurstDamage(2000000));
            Assert.AreEqual(30,
                Original323StoryGreenBloodRules.EnhancementAttackSpeedPercent);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Original323StoryGreenBloodRules.EnhancementBurstDamage(-1));
        }

        [Test]
        public void ModelRegenerationNeverResurrectsOrExceedsMaxHitPoints()
        {
            var enemy = new CombatEnemy("story", 1000);
            Assert.AreEqual(200L, enemy.ReceiveDamage(200));
            Assert.AreEqual(100L, enemy.RecoverHitPoints(100));
            Assert.AreEqual(900L, enemy.Health);
            Assert.AreEqual(100L, enemy.RecoverHitPoints(5000));
            Assert.AreEqual(1000L, enemy.Health);
            Assert.AreEqual(1000L, enemy.ReceiveDamage(1000));
            Assert.IsFalse(enemy.IsAlive);
            Assert.AreEqual(0L, enemy.RecoverHitPoints(1000));
            Assert.AreEqual(0L, enemy.Health);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                enemy.RecoverHitPoints(-1));
        }
    }
}
