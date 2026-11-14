using System;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrAuxiliaryResumeStateTests
    {
        [Test]
        public void EvolutionPreserves152DistinctKillReceiptsAndUnsealClock()
        {
            var original = new Original323EvolutionProgress(9);
            original.Activate(15, "S-01");
            for (int i = 0; i < 152; i++)
                Assert.IsTrue(original.RecordVerifiedLineDefeat(1, "line-" + i, 1 + i*.1));
            var snapshot = original.CaptureState();
            var restored = Original323EvolutionProgress.Restore(snapshot, 20);
            Assert.AreEqual(152, restored.LineDefeats);
            Assert.IsFalse(restored.RecordVerifiedLineDefeat(1, "line-0", 20));
            Assert.IsTrue(restored.CanRelease(20));
            snapshot.DefeatedEnemyKeys[0] = snapshot.DefeatedEnemyKeys[1];
            Assert.Throws<FormatException>(() =>
                Original323EvolutionProgress.Restore(snapshot, 20));
        }

        [Test]
        public void PoneglyphKeepsWitnessedMissionsAndExactDecodeTimer()
        {
            var session = new Original323PoneglyphSession();
            for (int i = 0; i < 8; i++)
                Assert.IsTrue(session.RecordVerifiedSharedMission(
                    (Original323SharedMissionKind)i, "mission-" + i, 1, "TEST_ONLY"));
            string message;
            Assert.IsTrue(session.TryStart(45, new string[0], 20, out message));
            session.RecordVerifiedAbilityState(46, "A001", true);
            var saved = session.CaptureState();
            var loaded = Original323PoneglyphSession.Restore(saved, 21);
            Assert.AreEqual(8, loaded.SharedCompletedCount);
            Assert.AreEqual(45, loaded.ActiveUnitId.Value);
            Assert.AreEqual(149, loaded.RemainingSeconds(21));
            Assert.IsFalse(loaded.RecordVerifiedSharedMission(
                Original323SharedMissionKind.RumbarSouls, "mission-0", 1, "TEST_ONLY"));
            saved.AcceptedEvents.Add("mission-0");
            Assert.Throws<FormatException>(() =>
                Original323PoneglyphSession.Restore(saved, 21));
        }

        [Test]
        public void CreepLedgerRejectsForgedRawcodeAndDuplicateStage()
        {
            var state = new Original323CreepPracticeState { NextSerial = 1 };
            state.Encounters.Add(new Original323CreepEncounterCheckpoint {
                EnemyId = "creep:object-base:n00E:1", Stage = 1, Rawcode = "n00E",
                X = 40, Y = -2, State = Original323CreepEncounterState.Alive
            });
            Original323CreepPracticeWorld.ValidateState(state);
            state.Encounters[0].Rawcode = "n00F";
            Assert.Throws<FormatException>(() =>
                Original323CreepPracticeWorld.ValidateState(state));
        }
    }
}
