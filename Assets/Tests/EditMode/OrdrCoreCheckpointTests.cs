using System;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Unity;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrCoreCheckpointTests
    {
        private static OrdrCoreCheckpointState Example()
        {
            var game = new GameSession();
            game.Start();

            var wallet = new EconomyWallet();
            var roster = new UnitRoster();
            var economy = new SharedEconomySession(wallet, roster);
            var profile = new VerifiedEconomyStartProfile(
                OrdrCoreCheckpoint.SourceVersion, GameSession.ModeId,
                "TEST_ONLY_SAVE_CONTRACT", true,
                new ResourceBundle().Add("gold", 60),
                Array.Empty<VerifiedStartingUnit>());
            economy.ApplyVerifiedStart(profile, roster.Add, id => { roster.Remove(id); });
            Assert.IsTrue(wallet.TryGrantOnce("test:story-reward",
                new ResourceBundle().Add("wood", 7)));

            long removed = roster.Add("C-01", 1, 2);
            long original = roster.Add("R-32", 3, 4);
            roster.Remove(removed);
            roster.Select(original);
            var missions = PersonalMissionGameplay.Start(
                game, wallet, new MobileMissionRandom(17), "test-session-1");
            game.BeginWave(1);
            missions.OnRoundStarted();
            game.SpawnEnemy();
            game.DefeatEnemy();
            missions.RecordOnce("test:common-sale-event",
                OriginalMissionSignal.CommonUnitSold);
            return OrdrCoreCheckpoint.Capture(game, economy, missions, 8.125);
        }

        [Test]
        public void StableRosterIdentityAndNextIdSurviveRoundTrip()
        {
            var state = Example();
            var restored = OrdrCoreCheckpoint.Restore(state);
            Assert.IsTrue(restored.Economy.HasStarted);
            Assert.AreEqual(1, restored.Economy.Units.Count);
            Assert.AreEqual(2, restored.Economy.Units.SelectedId.Value);
            Assert.AreEqual("R-32", restored.Economy.Units.Get(2).UnitId);
            Assert.AreEqual(3d, restored.Economy.Units.Get(2).X);
            Assert.AreEqual(3, restored.Economy.Units.Add("C-02", 5, 6));
            Assert.AreEqual(1, restored.Game.CurrentWave);
            Assert.AreEqual(1, restored.Game.DefeatedEnemies);
            Assert.AreEqual(8.125d, restored.LogicalTime);
            Assert.AreEqual(60, restored.Economy.Wallet.Balance("gold"));
            Assert.AreEqual(7, restored.Economy.Wallet.Balance("wood"));
            Assert.IsTrue(restored.Economy.Wallet.HasCompletedTransaction(
                "test:story-reward"));
            Assert.IsFalse(restored.Economy.Wallet.TryGrantOnce(
                "test:story-reward", new ResourceBundle().Add("wood", 7)));
            Assert.AreEqual(7, restored.Economy.Wallet.Balance("wood"));
            Assert.AreEqual(1, restored.Missions.CurrentRound);
            Assert.AreEqual("test-session-1", restored.Missions.SessionId);
            Assert.IsFalse(restored.MissionGameplay.RecordOnce(
                "test:common-sale-event", OriginalMissionSignal.CommonUnitSold));
            Assert.AreEqual(state.MissionGameplay.MobileRandomState,
                restored.MissionGameplay.CaptureState().MobileRandomState);
        }

        [Test]
        public void TamperedIdentitySessionAndLedgerFailBeforePublishing()
        {
            var state = Example();
            state.Roster.Units.Add(new UnitRosterEntryState {
                InstanceId = 2, UnitId = "R-32", X = 9, Y = 9
            });
            Assert.Throws<FormatException>(() => OrdrCoreCheckpoint.Validate(state));

            state = Example();
            state.Roster.SelectedInstanceId = 900;
            Assert.Throws<FormatException>(() => OrdrCoreCheckpoint.Validate(state));

            state = Example();
            state.MissionGameplay.Wallet.Balances[0].Amount = -1;
            Assert.Throws<FormatException>(() => OrdrCoreCheckpoint.Validate(state));

            state = Example();
            state.Game.CurrentWave = 2;
            Assert.Throws<FormatException>(() => OrdrCoreCheckpoint.Validate(state));

            state = Example();
            state.SourceW3xSha256 = "wrong-version";
            Assert.Throws<FormatException>(() => OrdrCoreCheckpoint.Validate(state));

            state = Example();
            state.LogicalTime = double.PositiveInfinity;
            Assert.Throws<FormatException>(() => OrdrCoreCheckpoint.Validate(state));
        }

        [Test]
        public void SlotsRecoverLastValidCheckpointAfterTornWriteOrCorruption()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "ordr-core-save-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new OrdrCoreCheckpointStore(folder);
                Assert.IsFalse(store.HasCoreDraft());
                Assert.Throws<FileNotFoundException>(() => store.LoadCore());
                var first = Example();
                store.WriteCore(first);
                Assert.IsTrue(store.HasCoreDraft());
                var next = Example();
                next.LogicalTime = 9.5;
                store.WriteCore(next);
                Assert.AreEqual(9.5d, store.LoadCore().LogicalTime);

                // A crash may leave a temp file and an invalid newer slot.
                File.WriteAllText(Path.Combine(folder, "ordr-core-draft-orphan.tmp"),
                    "INTERRUPTED_TEST_ONLY");
                File.WriteAllText(Path.Combine(folder, "ordr-core-draft-v1-1.json"),
                    "CORRUPTED_TEST_ONLY");
                Assert.AreEqual(8.125d, store.LoadCore().LogicalTime);
                store.WriteCore(next);
                Assert.AreEqual(9.5d, store.LoadCore().LogicalTime);

                // Never silently overwrite two corrupt files or treat them
                // as an empty new game.
                File.WriteAllText(Path.Combine(folder, "ordr-core-draft-v1-0.json"),
                    "CORRUPTED_TEST_ONLY");
                File.WriteAllText(Path.Combine(folder, "ordr-core-draft-v1-1.json"),
                    "CORRUPTED_TEST_ONLY");
                Assert.IsFalse(store.HasCoreDraft());
                Assert.Throws<FormatException>(() => store.LoadCore());
                Assert.Throws<FormatException>(() => store.WriteCore(Example()));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        [Test]
        public void GameAndRosterRejectInvalidRestoreWithoutMutatingSource()
        {
            var game = new GameSession();
            game.Start();
            game.BeginWave(1);
            game.SpawnEnemy();
            var state = game.CaptureState();
            state.AliveEnemies = -1;
            Assert.Throws<FormatException>(() => GameSession.Restore(state));
            Assert.AreEqual(1, game.AliveEnemies);

            var roster = new UnitRoster();
            roster.Add("R-32", 1, 2);
            var snapshot = roster.CaptureState();
            snapshot.NextInstanceId = 1;
            Assert.Throws<FormatException>(() => UnitRoster.Restore(snapshot));
            Assert.AreEqual(1, roster.Count);
            Assert.AreEqual(2, roster.CaptureState().NextInstanceId);
        }

        [Test]
        public void InvalidStateNeverClobbersExistingValidSlot()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "ordr-invalid-save-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new OrdrCoreCheckpointStore(folder);
                var valid = Example();
                store.WriteCore(valid);
                valid.ModeId = "another-mode";
                Assert.Throws<FormatException>(() => store.WriteCore(valid));
                Assert.AreEqual(GameSession.ModeId, store.LoadCore().ModeId);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
