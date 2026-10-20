using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public class OriginalPersonalMissionSessionTests
    {
        private sealed class ChooseFirstIndex : IOriginalMissionRandom
        {
            private readonly int first;
            private int called;
            public ChooseFirstIndex(int first) { this.first = first; }
            public int NextIndex(int exclusiveUpperBound)
            {
                int result = called++ == 0 ? first : 0;
                if (result >= exclusiveUpperBound) throw new InvalidOperationException("테스트 인덱스 초과");
                return result;
            }
        }

        private static OriginalPersonalMissionSession StartAt(int catalogIndex,
            OriginalMissionRewardLedger ledger = null, bool otherworld = true)
        {
            var session = new OriginalPersonalMissionSession(otherworld,
                new ChooseFirstIndex(catalogIndex), ledger ?? new OriginalMissionRewardLedger(),
                "test-session-" + catalogIndex);
            session.Start();
            return session;
        }

        [Test]
        public void ExtractedCatalogHasSeventeenStandardAndOneOtherworldMission()
        {
            Assert.AreEqual(18, OriginalPersonalMissionCatalog.All.Count);
            Assert.AreEqual(17, OriginalPersonalMissionCatalog.Available(false).Count);
            Assert.AreEqual(18, OriginalPersonalMissionCatalog.Available(true).Count);
            for (int i = 0; i < 18; i++)
                Assert.AreEqual("Q" + i.ToString("D3"), OriginalPersonalMissionCatalog.All[i].Id);
            Assert.AreEqual("유닛조합(신비)", OriginalPersonalMissionCatalog.Find("Q017").Name);
        }

        [Test]
        public void AssignsThreeDistinctMissionsAndRerollsOnlyOnce()
        {
            var session = StartAt(0);
            Assert.AreEqual("Q000", session.Slots[0].Mission.Id);
            Assert.AreEqual("Q001", session.Slots[1].Mission.Id);
            Assert.AreEqual("Q002", session.Slots[2].Mission.Id);
            Assert.IsTrue(session.TryReroll(0));
            Assert.AreEqual("Q003", session.Slots[0].Mission.Id);
            Assert.IsFalse(session.TryReroll(1));
            var ids = new HashSet<string>();
            foreach (var slot in session.Slots)
                Assert.IsTrue(ids.Add(slot.Mission.Id));
        }

        [Test]
        public void CannotRerollAtRoundTenOrAfterCompletingSlot()
        {
            var completed = StartAt(8);
            completed.RecordSuccessfulFusion("test", true, new [] { "A07O" }, false);
            Assert.IsTrue(completed.Slots[0].Completed);
            Assert.IsFalse(completed.TryReroll(0));

            var late = StartAt(0);
            for (int round = 1; round <= 10; round++) late.BeginRound(round);
            Assert.IsFalse(late.TryReroll(0));
            Assert.IsFalse(late.HasRerolled);
        }

        [Test]
        public void WoodGambleMissionChecksOnlyWhenRoundEnds()
        {
            var ledger = new OriginalMissionRewardLedger();
            var session = StartAt(2, ledger);
            session.BeginRound(1);
            session.Record(OriginalMissionSignal.WoodGambleAttempted, 9);
            Assert.IsFalse(session.Slots[0].Completed);
            session.EndRound();
            Assert.IsTrue(session.Slots[0].Completed);
            Assert.AreEqual(3, ledger.Lumber);
            session.EndRound();
            Assert.AreEqual(3, ledger.Lumber);
            Assert.AreEqual(1, ledger.TransactionCount);
        }

        [Test]
        public void SelectionWispMissionExpiresAtRoundFifteen()
        {
            var late = StartAt(1);
            for (int round = 1; round <= 15; round++) late.BeginRound(round);
            late.Record(OriginalMissionSignal.SelectionWispUsed, 11);
            Assert.IsFalse(late.Slots[0].Completed);

            var ledger = new OriginalMissionRewardLedger();
            var early = StartAt(1, ledger);
            for (int round = 1; round <= 14; round++) early.BeginRound(round);
            early.Record(OriginalMissionSignal.SelectionWispUsed, 11);
            Assert.IsTrue(early.Slots[0].Completed);
            Assert.AreEqual(3, ledger.ItemCount("e018"));
        }

        [Test]
        public void GamblingAddictionRequiresFourAttemptsAtEachTier()
        {
            var ledger = new OriginalMissionRewardLedger();
            var session = StartAt(3, ledger);
            for (int i = 0; i < 4; i++)
            {
                session.RecordGamble(OriginalGambleTier.Low, false);
                session.RecordGamble(OriginalGambleTier.Middle, true);
            }
            Assert.IsFalse(session.Slots[0].Completed);
            Assert.AreEqual(0, session.ProgressAt(0));
            for (int i = 0; i < 4; i++)
                session.RecordGamble(OriginalGambleTier.High, true);
            Assert.IsTrue(session.Slots[0].Completed);
            Assert.AreEqual(2, ledger.ItemCount("e018"));
        }

        [Test]
        public void MysticFusionRequiresOtherworldAndPaysFiveLumber()
        {
            var ledger = new OriginalMissionRewardLedger();
            var mystic = StartAt(17, ledger);
            Assert.AreEqual("Q017", mystic.Slots[0].Mission.Id);
            mystic.RecordSuccessfulFusion("test", false, new[] { "A909" }, false);
            Assert.IsTrue(mystic.Slots[0].Completed);
            Assert.AreEqual(5, ledger.Lumber);
            Assert.AreEqual(17, OriginalPersonalMissionCatalog.Available(false).Count);
        }

        [Test]
        public void ItemRequiredFusionUsesOriginalThreeRawcodes()
        {
            var ledger = new OriginalMissionRewardLedger();
            var session = StartAt(13, ledger);
            session.RecordSuccessfulFusion("h066", false, new string[0], false);
            Assert.IsFalse(session.Slots[0].Completed);
            session.RecordSuccessfulFusion("h067", false, new string[0], false);
            Assert.IsTrue(session.Slots[0].Completed);
            Assert.AreEqual(3, ledger.ItemCount("e018"));
        }

        [Test]
        public void ThreeCompletedMissionsGiveVictoryBerryEligibility()
        {
            var ledger = new OriginalMissionRewardLedger();
            var session = StartAt(0, ledger);
            session.BeginRound(1);
            session.Record(OriginalMissionSignal.UniqueItemAcquired, 2);
            session.Record(OriginalMissionSignal.SelectionWispUsed, 11);
            session.Record(OriginalMissionSignal.WoodGambleAttempted, 9);
            Assert.IsFalse(session.AllThreeCompleted);
            session.EndRound();
            Assert.IsTrue(session.AllThreeCompleted);
            Assert.AreEqual(1, session.BonusBerryOnVictory);
            Assert.AreEqual(5, ledger.ItemCount("e018"));
            Assert.AreEqual(1, ledger.ItemCount("e0IX"));
            Assert.AreEqual(3, ledger.Lumber);
        }

        [Test]
        public void SpecialRewardsUpdateAbilityAndTraitPoint()
        {
            var ledger = new OriginalMissionRewardLedger();
            var midFailure = StartAt(5, ledger);
            midFailure.Record(OriginalMissionSignal.MidGambleFailed, 4);
            Assert.AreEqual(1, ledger.AbilityLevel("A09C"));
            var quadruple = StartAt(14, ledger);
            quadruple.Record(OriginalMissionSignal.HighestTierFusion, 4);
            Assert.AreEqual(2, ledger.AbilityLevel("A0X0"));
            var triple = StartAt(15, ledger);
            triple.Record(OriginalMissionSignal.HighestTierFusion, 3);
            Assert.AreEqual(1, ledger.TraitPoints);
        }

        [Test]
        public void RewardTransactionsAreIdempotent()
        {
            var ledger = new OriginalMissionRewardLedger();
            var rule = OriginalPersonalMissionCatalog.Find("Q000");
            ledger.Grant("txn", rule.Rewards);
            ledger.Grant("txn", rule.Rewards);
            Assert.AreEqual(2, ledger.ItemCount("e018"));
            Assert.AreEqual(1, ledger.ItemCount("e0IX"));
            Assert.AreEqual(1, ledger.TransactionCount);
        }

        [Test]
        public void InvalidRandomDoesNotSilentlySelectAnUnknownMission()
        {
            var session = new OriginalPersonalMissionSession(true,
                new ChooseFirstIndex(999), new OriginalMissionRewardLedger(), "bad-random");
            Assert.Throws<InvalidOperationException>(() => session.Start());
        }
    }
}
