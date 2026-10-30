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
        public void OriginalDoubleTraitPointBranchUsesExplicitSelection()
        {
            var queue = new Original323StoryNativeRewardQueue();
            queue.RecordCommittedClear(5); // earlier research does not lock trait selection
            var model = new Original323StoryMobileProgression();
            Assert.AreEqual(1, queue.ApplyPending(model));
            model.ConfigureOriginalStoryTraitSelection(new DoubleTrait());
            queue.RecordCommittedClear(8);
            Assert.AreEqual(2, queue.ApplyPending(model));
            Assert.AreEqual(2, model.TraitPoints);
            Assert.Throws<InvalidOperationException>(() =>
                model.ConfigureOriginalStoryTraitSelection(new DoubleTrait()));
            queue.RecordCommittedClear(9);
            Assert.AreEqual(2, queue.ApplyPending(model));
            Assert.AreEqual(4, model.TraitPoints);
            Assert.AreEqual(1, queue.PendingCount); // Ancient ship is NOT created.
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
