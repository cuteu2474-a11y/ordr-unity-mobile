using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323StoryNightmareProgressionTests
    {
        [Test]
        public void PinnedSoloUpgradeModelAppliesResearchWithoutInventedLateBuffs()
        {
            // Pin the source 2.323[C] UnitBalance/UpgradeData and CUM/BeP.
            // The 14 source HP fields are object-base; the model uses net +28%.
            Assert.AreEqual(128, Original323StorySoloStats.SoloHitPointPercent);
            var catalog = Original323StoryBuildingCatalog.FromJson(File.ReadAllText(
                Path.Combine(Application.dataPath, "..", "data",
                    "original-2323C-monster-object-stats.json")));
            var first = catalog.Find(1);
            var second = catalog.Find(2);
            var final = catalog.Find(14);
            Assert.AreEqual("n000", first.Rawcode);
            Assert.AreEqual("n002", second.Rawcode);
            Assert.AreEqual("n009", final.Rawcode);
            Assert.AreEqual(89856L, Original323StorySoloStats.InitialHitPoints(first));
            Assert.AreEqual(326592L, Original323StorySoloStats.InitialHitPoints(second));
            Assert.AreEqual(369664000L, Original323StorySoloStats.InitialHitPoints(final));
            Assert.AreEqual(0L, Original323StorySoloStats.RegenPerSecondFor(first));
            Assert.AreEqual(1000L, Original323StorySoloStats.RegenPerSecondFor(final));
            Assert.AreEqual(16, Original323StorySoloStats.InitialA04QLevel);
        }

        private sealed class OriginalRuntimeFixture : IOriginal323StoryNativeRewardPort
        {
            public int Applied;
            public bool TryApplySourceNativeReward(Original323StoryNativeReward award)
            {
                if (award.Kind == Original323StoryNativeRewardKind.GreenBloodSkill)
                {
                    Assert.AreEqual(14, award.Stage);
                    Assert.AreEqual("A13A", award.OriginalRawcode);
                    Applied++;
                    return true;
                }
                return false;
            }
        }

        [Test]
        public void ResearchTalentAndNativeEffectsAreNotConflatedWithFakeCurrencies()
        {
            var effects = new Original323StoryNativeRewardQueue();
            for (int i = 1; i <= 14; i++) effects.RecordCommittedClear(i);
            for (int i = 1; i <= 14; i++) effects.RecordCommittedClear(i);
            Assert.AreEqual(15, effects.Awards.Count,
                "5 tech, 6 trait, ancient ship, 2 hero XP and Green Blood");
            var model = new Original323StoryMobileProgression();
            Assert.AreEqual(11, effects.ApplyPending(model));
            Assert.AreEqual(6, model.TraitPoints);
            Assert.AreEqual(5, model.Researches.Count);
            Assert.IsTrue(model.HasResearch("R01N"));
            Assert.IsTrue(model.HasResearch("R01O"));
            Assert.IsTrue(model.HasResearch("R01P"));
            Assert.IsTrue(model.HasResearch("R01Q"));
            Assert.IsTrue(model.HasResearch("R01S"));
            Assert.IsFalse(model.HasResearch("R02I"));
            Assert.AreEqual(4, effects.PendingCount);
            Assert.AreEqual(0, effects.ApplyPending(model));
            var fake = new OriginalRuntimeFixture();
            model.ConnectAdditionalNativeEffects(fake);
            Assert.AreEqual(1, effects.ApplyPending(model));
            Assert.AreEqual(1, fake.Applied);
            Assert.AreEqual(3, effects.PendingCount,
                "Ancient ship + two hero XP remain unexecuted");
            Assert.AreEqual(0, effects.ApplyPending(model));
            Assert.AreEqual(1, fake.Applied);
            Assert.AreEqual(6, model.TraitPoints);
        }

        [Test]
        public void DuplicateNativeClaimCanNeverGrantAdditionalTraitPoints()
        {
            var queue = new Original323StoryNativeRewardQueue();
            var model = new Original323StoryMobileProgression();
            queue.RecordCommittedClear(8);
            queue.RecordCommittedClear(8);
            Assert.AreEqual(2, queue.Awards.Count);
            Assert.AreEqual(2, queue.ApplyPending(model));
            Assert.AreEqual(1, model.TraitPoints);
            Assert.AreEqual(0, queue.ApplyPending(model));
            Assert.AreEqual(1, model.TraitPoints);
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.RecordCommittedClear(15));
        }
    }
}
