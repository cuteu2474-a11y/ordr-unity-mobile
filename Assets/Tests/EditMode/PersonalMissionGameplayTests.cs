using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public class PersonalMissionGameplayTests
    {
        private sealed class ChooseFirst : IOriginalMissionRandom
        {
            private readonly int index;
            private bool first = true;
            public ChooseFirst(int index) { this.index = index; }
            public int NextIndex(int limit)
            { int result = first ? index : 0; first = false; return result; }
        }

        private sealed class FailUntilEnabled : IOriginalMissionRewardSink
        {
            public bool Enabled;
            public readonly OriginalMissionRewardLedger Ledger = new OriginalMissionRewardLedger();
            public void Grant(string id, IReadOnlyList<OriginalMissionReward> rewards)
            {
                if (!Enabled) throw new InvalidOperationException("TEST_ONLY reward outage");
                Ledger.Grant(id, rewards);
            }
        }

        private static PersonalMissionGameplay Begin(int first, out GameSession game,
            out EconomyWallet wallet)
        {
            game = new GameSession(); game.Start();
            wallet = new EconomyWallet();
            return PersonalMissionGameplay.Start(game, wallet, new ChooseFirst(first), "TEST_ONLY-session");
        }
        private static void NextRound(GameSession game, PersonalMissionGameplay missions)
        { game.BeginWave(game.CurrentWave + 1); missions.OnRoundStarted(); }

        [Test]
        public void EveryCatalogRewardCanBePaidToSharedWalletExactlyOnce()
        {
            var wallet = new EconomyWallet();
            var rewards = new OriginalMissionWalletRewards(wallet);
            foreach (var rule in OriginalPersonalMissionCatalog.All)
            {
                rewards.Grant(rule.Id, rule.Rewards);
                rewards.Grant(rule.Id, rule.Rewards);
            }
            Assert.AreEqual(18, wallet.CaptureState().CompletedRewards.Count);
            Assert.AreEqual(8, wallet.Balance("wood"));
            Assert.AreEqual(1, wallet.Balance(OriginalMissionWalletRewards.TraitPointsId));
            Assert.AreEqual(1, wallet.Balance(OriginalMissionWalletRewards.AbilityId("A09C")));
            Assert.AreEqual(2, wallet.Balance(OriginalMissionWalletRewards.AbilityId("A0X0")));
            Assert.AreEqual(1, wallet.Balance(WispResourceIds.Random));
        }

        [Test]
        public void ResourceRewardAndSetLevelAreOneWalletTransaction()
        {
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("wood", long.MaxValue));
            Assert.Throws<OverflowException>(() => wallet.TryGrantOnce("TEST_ONLY-overflow",
                new ResourceBundle().Add("wood", 1), new Dictionary<string, long> { { "level", 2 } }));
            Assert.AreEqual(0, wallet.Balance("level"));
            Assert.AreEqual(0, wallet.CaptureState().CompletedRewards.Count);
            Assert.IsTrue(wallet.TryGrantOnce("TEST_ONLY-overflow", new ResourceBundle(),
                new Dictionary<string, long> { { "level", 2 } }));
            Assert.AreEqual(2, wallet.Balance("level"));
        }

        [Test]
        public void SetLevelRetainsItsOriginalSetRatherThanAddMeaning()
        {
            var wallet = new EconomyWallet();
            string id = OriginalMissionWalletRewards.AbilityId("A0X0");
            wallet.Grant(new ResourceBundle().Add(id, 7));
            new OriginalMissionWalletRewards(wallet).Grant("royal",
                OriginalPersonalMissionCatalog.Find("Q014").Rewards);
            Assert.AreEqual(2, wallet.Balance(id));
        }

        [Test]
        public void InvalidRewardNeverGrantsAPartialBundle()
        {
            var wallet = new EconomyWallet();
            var rewards = new OriginalMissionWalletRewards(wallet);
            Assert.Throws<ArgumentException>(() => rewards.Grant("TEST_ONLY-bad", new[] {
                new OriginalMissionReward(OriginalMissionRewardKind.Lumber, "", 5),
                new OriginalMissionReward(OriginalMissionRewardKind.Item, "????", 1)
            }));
            Assert.AreEqual(0, wallet.Balance("wood"));
            Assert.AreEqual(0, wallet.CaptureState().CompletedRewards.Count);
        }

        [Test]
        public void NineActualSalesPayOnlyAtNextVerifiedRoundAndNeverTwice()
        {
            GameSession game; EconomyWallet wallet;
            var gameplay = Begin(16, out game, out wallet);
            NextRound(game, gameplay);
            for (int i = 1; i <= 9; i++) Assert.IsTrue(gameplay.RecordCommonSale(i));
            Assert.IsFalse(gameplay.RecordCommonSale(9));
            Assert.AreEqual(9, gameplay.Missions.ProgressAt(0));
            Assert.AreEqual(0, wallet.Balance(WispResourceIds.Choice));
            NextRound(game, gameplay);
            Assert.IsTrue(gameplay.Missions.Slots[0].Completed);
            Assert.AreEqual(2, wallet.Balance(WispResourceIds.Choice));
            NextRound(game, gameplay);
            Assert.AreEqual(2, wallet.Balance(WispResourceIds.Choice));
        }

        [Test]
        public void ElevenCommittedChoiceUsesPayBeforeDeadline()
        {
            GameSession game; EconomyWallet wallet;
            var gameplay = Begin(1, out game, out wallet);
            for (int i = 0; i < 14; i++) NextRound(game, gameplay);
            for (int i = 0; i < 11; i++) Assert.IsTrue(gameplay.RecordChoiceWispUse());
            Assert.IsTrue(gameplay.Missions.Slots[0].Completed);
            Assert.AreEqual(3, wallet.Balance(WispResourceIds.Choice));
        }

        [Test]
        public void LateChoiceUsesCannotCompleteExpiredMission()
        {
            GameSession game; EconomyWallet wallet;
            var gameplay = Begin(1, out game, out wallet);
            for (int i = 0; i < 15; i++) NextRound(game, gameplay);
            for (int i = 0; i < 11; i++) gameplay.RecordChoiceWispUse();
            Assert.IsFalse(gameplay.Missions.Slots[0].Completed);
            Assert.AreEqual(0, wallet.Balance(WispResourceIds.Choice));
        }

        [Test]
        public void RerollIsOnceFreeAndNeverAvailableAtRoundTen()
        {
            GameSession game; EconomyWallet wallet;
            var gameplay = Begin(0, out game, out wallet);
            Assert.IsTrue(gameplay.TryReroll(0));
            Assert.IsFalse(gameplay.TryReroll(1));
            Assert.IsFalse(gameplay.TryReroll(-1));
            Assert.AreEqual(0, wallet.CaptureState().Balances.Count);
            var late = Begin(0, out game, out wallet);
            for (int i = 0; i < 10; i++) NextRound(game, late);
            Assert.IsFalse(late.TryReroll(0));
        }

        [Test]
        public void PendingRewardRetainsDeadlineEligibilityAndCannotBeRerolled()
        {
            var sink = new FailUntilEnabled();
            var missions = new OriginalPersonalMissionSession(true, new ChooseFirst(1), sink, "TEST_ONLY");
            missions.Start();
            for (int i = 1; i <= 14; i++) missions.BeginRound(i);
            Assert.Throws<InvalidOperationException>(() => missions.Record(OriginalMissionSignal.SelectionWispUsed, 11));
            Assert.IsTrue(missions.Slots[0].RewardPending);
            Assert.IsFalse(missions.TryReroll(0));
            missions.BeginRound(15);
            sink.Enabled = true; missions.RetryPendingRewards();
            Assert.IsTrue(missions.Slots[0].Completed);
            Assert.AreEqual(3, sink.Ledger.ItemCount("e018"));
        }

        [Test]
        public void GambleCountersAllCommitBeforeAnyRewardObserverFailure()
        {
            var sink = new FailUntilEnabled();
            var missions = new OriginalPersonalMissionSession(true, new ChooseFirst(3), sink, "TEST_ONLY");
            missions.Start();
            for (int i = 0; i < 4; i++)
            {
                missions.RecordGamble(OriginalGambleTier.Low, false);
                missions.RecordGamble(OriginalGambleTier.Middle, false);
                if (i == 3) Assert.Throws<InvalidOperationException>(() => missions.RecordGamble(OriginalGambleTier.High, false));
                else missions.RecordGamble(OriginalGambleTier.High, false);
            }
            Assert.AreEqual(4, missions.EventCount(OriginalMissionSignal.HighGambleAttempted));
            Assert.AreEqual(4, missions.EventCount(OriginalMissionSignal.HighGambleFailed));
            sink.Enabled = true; missions.RetryPendingRewards();
            Assert.IsTrue(missions.Slots[0].Completed);
        }

        [Test]
        public void FusionFactsAreNotInferredFromTheCatalogGrade()
        {
            GameSession game; EconomyWallet wallet;
            var gameplay = Begin(8, out game, out wallet);
            gameplay.RecordFusion("fusion:1", "H000", false, new[] { "A07O" }, false);
            Assert.AreEqual(0, gameplay.Missions.EventCount(OriginalMissionSignal.TranscendentFusion));
            Assert.AreEqual(0, gameplay.Missions.EventCount(OriginalMissionSignal.HighestTierFusion));
            gameplay.RecordFusion("fusion:2", "H000", true, new[] { "A07O" }, true);
            Assert.IsTrue(gameplay.Missions.Slots[0].Completed);
            Assert.IsFalse(gameplay.RecordFusion("fusion:2", "H000", true, new[] { "A07O" }, true));
            Assert.AreEqual(1, gameplay.Missions.EventCount(OriginalMissionSignal.HighestTierFusion));
        }

        [Test]
        public void FusionCountsSurviveRewardFailureAcrossMultipleCategories()
        {
            var sink = new FailUntilEnabled();
            var missions = new OriginalPersonalMissionSession(true, new ChooseFirst(9), sink, "TEST_ONLY");
            missions.Start();
            Assert.Throws<InvalidOperationException>(() => missions.RecordSuccessfulFusion("h067", false,
                new[] { "A905", "A909" }, true));
            Assert.AreEqual(1, missions.EventCount(OriginalMissionSignal.ImmortalFusion));
            Assert.AreEqual(1, missions.EventCount(OriginalMissionSignal.MysticFusion));
            Assert.AreEqual(1, missions.EventCount(OriginalMissionSignal.ItemRequiredFusion));
            Assert.AreEqual(1, missions.EventCount(OriginalMissionSignal.HighestTierFusion));
        }

        [Test]
        public void CommittedEventsStayDeduplicatedAcrossJsonSaveRestore()
        {
            GameSession game; EconomyWallet wallet;
            var gameplay = Begin(17, out game, out wallet);
            NextRound(game, gameplay);
            gameplay.RecordFusion("fusion:1", "h000", false, new[] { "A909" }, false);
            var state = JsonUtility.FromJson<PersonalMissionGameplayState>(
                JsonUtility.ToJson(gameplay.CaptureState()));
            var restored = PersonalMissionGameplay.Restore(game, wallet, state, new ChooseFirst(0));
            Assert.IsFalse(restored.RecordFusion("fusion:1", "h000", false, new[] { "A909" }, false));
            restored.RetryRewards();
            Assert.AreEqual(5, wallet.Balance("wood"));
            Assert.IsTrue(restored.Missions.Slots[0].Completed);
        }

        [Test]
        public void ReceiptWrittenBeforeObserverFailurePreventsDuplicateRewardsAfterReload()
        {
            GameSession game; EconomyWallet wallet;
            var gameplay = Begin(17, out game, out wallet);
            Action<ResourceBundle> failure = _ => { throw new InvalidOperationException("TEST_ONLY consumer"); };
            wallet.Granted += failure;
            Assert.IsTrue(gameplay.RecordFusion("fusion:1", "h000", false, new[] { "A909" }, false));
            Assert.IsTrue(gameplay.Missions.Slots[0].RewardPending);
            Assert.AreEqual(5, wallet.Balance("wood"));
            var state = gameplay.CaptureState();
            wallet.Granted -= failure;
            var restored = PersonalMissionGameplay.Restore(game, wallet, state, new ChooseFirst(0));
            restored.RetryRewards();
            Assert.IsTrue(restored.Missions.Slots[0].Completed);
            Assert.AreEqual(5, wallet.Balance("wood"));
            Assert.AreEqual(1, restored.Missions.EventCount(OriginalMissionSignal.MysticFusion));
        }

        [Test]
        public void BadSnapshotsNeverReplaceTheLiveWallet()
        {
            GameSession game; EconomyWallet wallet;
            var gameplay = Begin(0, out game, out wallet);
            wallet.Grant(new ResourceBundle().Add("gold", 123));
            var state = gameplay.CaptureState();
            state.Missions.Slots[1].MissionId = state.Missions.Slots[0].MissionId;
            state.Wallet.Balances.Clear();
            Assert.Throws<FormatException>(() => PersonalMissionGameplay.Restore(game, wallet, state, new ChooseFirst(0)));
            Assert.AreEqual(123, wallet.Balance("gold"));
        }

        [Test]
        public void CompletedMissionWithoutRewardReceiptIsRejected()
        {
            GameSession game; EconomyWallet wallet;
            var gameplay = Begin(17, out game, out wallet);
            gameplay.RecordFusion("fusion:1", "h000", false, new[] { "A909" }, false);
            var state = gameplay.CaptureState();
            state.Wallet.CompletedRewards.Clear();
            Assert.Throws<FormatException>(() => PersonalMissionGameplay.Restore(game, wallet, state, new ChooseFirst(0)));
            Assert.AreEqual(5, wallet.Balance("wood"));
        }

        [Test]
        public void DifferentGameRoundCannotRestoreMissionComponentAlone()
        {
            GameSession game; EconomyWallet wallet;
            var gameplay = Begin(0, out game, out wallet);
            var state = gameplay.CaptureState();
            game.BeginWave(1);
            Assert.Throws<FormatException>(() => PersonalMissionGameplay.Restore(game, wallet, state, new ChooseFirst(0)));
        }

        [Test]
        public void RerollSequenceIsStableAcrossJsonCheckpointForManySeeds()
        {
            for (uint seed = 1; seed <= 100; seed++)
            {
                var game = new GameSession(); game.Start();
                var wallet = new EconomyWallet();
                var original = PersonalMissionGameplay.Start(game, wallet, new MobileMissionRandom(seed), "TEST_ONLY");
                var state = JsonUtility.FromJson<PersonalMissionGameplayState>(JsonUtility.ToJson(original.CaptureState()));
                var restored = PersonalMissionGameplay.Restore(game, new EconomyWallet(), state);
                Assert.IsTrue(original.TryReroll(1));
                Assert.IsTrue(restored.TryReroll(1));
                Assert.AreEqual(original.Missions.Slots[1].Mission.Id, restored.Missions.Slots[1].Mission.Id);
            }
        }

        [Test]
        public void LossDoesNotSettleRoundEndMissionOrAcceptMoreEvents()
        {
            GameSession game; EconomyWallet wallet;
            var gameplay = Begin(2, out game, out wallet);
            NextRound(game, gameplay);
            gameplay.RecordOnce("wood-attempts", OriginalMissionSignal.WoodGambleAttempted, 9);
            game.Finish(false); gameplay.OnGameFinished();
            Assert.IsFalse(gameplay.Missions.Slots[0].Completed);
            Assert.AreEqual(0, wallet.Balance("wood"));
            Assert.IsFalse(gameplay.RecordCommonSale(1));
            Assert.IsFalse(gameplay.TryReroll(0));
        }

        [Test]
        public void VictoryFinishesLastRoundOnceWithoutInventingBerrySettlement()
        {
            GameSession game; EconomyWallet wallet;
            var gameplay = Begin(2, out game, out wallet);
            NextRound(game, gameplay);
            gameplay.RecordOnce("wood-attempts", OriginalMissionSignal.WoodGambleAttempted, 9);
            game.Finish(true); gameplay.OnGameFinished(); gameplay.OnGameFinished();
            Assert.IsTrue(gameplay.Missions.Slots[0].Completed);
            Assert.AreEqual(3, wallet.Balance("wood"));
            Assert.AreEqual(0, wallet.Balance("berry"));
        }

        [Test]
        public void InvalidSignalsDoNotConsumeACommittedEventId()
        {
            GameSession game; EconomyWallet wallet;
            var gameplay = Begin(17, out game, out wallet);
            Assert.Throws<ArgumentException>(() => gameplay.RecordOnce("event:1", (OriginalMissionSignal)999));
            Assert.IsTrue(gameplay.RecordOnce("event:1", OriginalMissionSignal.MysticFusion));
            Assert.AreEqual(5, wallet.Balance("wood"));
        }

        [Test]
        public void FreshSessionDoesNotShareOldMissionReceiptsOrRerollFlags()
        {
            var game = new GameSession(); game.Start();
            var wallet = new EconomyWallet();
            var first = PersonalMissionGameplay.Start(game, wallet, new ChooseFirst(17), "TEST_ONLY-first");
            first.RecordOnce("fusion:1", OriginalMissionSignal.MysticFusion);
            first.TryReroll(1);
            var nextGame = new GameSession(); nextGame.Start();
            var next = PersonalMissionGameplay.Start(nextGame, wallet, new ChooseFirst(17), "TEST_ONLY-second");
            Assert.IsFalse(next.Missions.HasRerolled);
            Assert.IsTrue(next.RecordOnce("fusion:1", OriginalMissionSignal.MysticFusion));
            Assert.AreEqual(10, wallet.Balance("wood"));
        }
    }
}
