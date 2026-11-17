using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrTreasureRestoreDraftTests
    {
        private static OrdrCoreCheckpointState EmptyMobileTreasure()
        {
            return new OrdrCoreCheckpointState {
                Combat = new SourceCombatRuntimeCheckpoint(),
                ScriptRandom = new MobileRollbackWispRandom(428).CaptureState(),
                Treasure = new Original323TreasureSessionState {
                    Hunt = new Original323TreasureHuntState(),
                    Mobile = new Original323MobileTreasureState {
                        SessionId = Guid.NewGuid().ToString("N")
                    },
                    Gacha = new Original323TreasureGachaState(),
                    Alarms = new Original323TreasureAlarmJournalState()
                }
            };
        }

        private static EconomyWallet InitialBountyWallet()
        {
            var wallet = new EconomyWallet();
            Assert.IsTrue(wallet.TryGrantOnce(
                "source:bounty:CRW:LV-initial-5",
                new ResourceBundle().Add(
                    Original323BountyTreasureWorld.MapsRemainingId,
                    Original323BountyTreasureWorld.InitialMaps)));
            return wallet;
        }

        [Test]
        public void EmptyPortableMobileTreasurePassesWithoutNewRewardOrDraw()
        {
            var wallet = InitialBountyWallet();
            var saved = EmptyMobileTreasure();
            int receipts = wallet.CaptureState().CompletedRewards.Count;
            long maps = wallet.Balance(
                Original323BountyTreasureWorld.MapsRemainingId);
            Assert.DoesNotThrow(() =>
                OrdrTreasureRestoreGraphBuilder.ValidatePrerequisites(saved, wallet));
            Assert.AreEqual(receipts,
                wallet.CaptureState().CompletedRewards.Count);
            Assert.AreEqual(maps, wallet.Balance(
                Original323BountyTreasureWorld.MapsRemainingId));
            Assert.AreEqual(0, saved.ScriptRandom.Draws.Count);
        }

        [Test]
        public void MissingInitialBountyReceiptOrUnfinishedNativeAbilityBlocksRestore()
        {
            var saved = EmptyMobileTreasure();
            var incomplete = new EconomyWallet();
            Assert.Throws<NotSupportedException>(() =>
                OrdrTreasureRestoreGraphBuilder.ValidatePrerequisites(
                    saved, incomplete));

            var wallet = InitialBountyWallet();
            wallet.Grant(new ResourceBundle().Add(
                Original323BountyTreasureWorld.NativeAbilitiesPendingId, 1));
            Assert.Throws<NotSupportedException>(() =>
                OrdrTreasureRestoreGraphBuilder.ValidatePrerequisites(
                    saved, wallet));
        }

        [Test]
        public void ForeignOrCorruptTreasureSessionAndOriginalBfiAreRejected()
        {
            var wallet = InitialBountyWallet();
            var saved = EmptyMobileTreasure();
            saved.Treasure.Mobile.SessionId = "another-session";
            Assert.Throws<FormatException>(() =>
                OrdrTreasureRestoreGraphBuilder.ValidatePrerequisites(
                    saved, wallet));
            saved.Treasure.Mobile.SessionId = Guid.NewGuid().ToString("N");
            saved.Treasure.NamiAtDream = true;
            Assert.Throws<FormatException>(() =>
                OrdrTreasureRestoreGraphBuilder.ValidatePrerequisites(
                    saved, wallet));
            saved.Treasure.NamiAtDream = false;
            saved.OriginalScriptRandom = new Original323SharedBfiCheckpoint {
                State = 1
            };
            Assert.Throws<NotSupportedException>(() =>
                OrdrTreasureRestoreGraphBuilder.ValidatePrerequisites(
                    saved, wallet));
        }
    }
}
