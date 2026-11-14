using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    public sealed class CombatActorRestoreGraphTests
    {
        private static CharacterUnitCatalog Units() =>
            CharacterUnitCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[{\"id\":\"TEST-01\",\"grade\":\"흔함\",\"name\":\"테스트\",\"roleTags\":[]}]}");
        private static CharacterSkillCatalog Skills() =>
            CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[{\"unitId\":\"TEST-01\",\"sourceUrl\":\"TEST_FIXTURE\",\"skillSpecCompleteness\":\"partial\",\"basicAttack\":{},\"passives\":[],\"skills\":[]}]}");
        private static CharacterCombatRuntime World(
            UnitRoster roster = null, GameSession game = null)
        {
            var missing = new RejectUnverifiedDamageFormula();
            return game == null ?
                new CharacterCombatRuntime(Units(), Skills(), missing, missing,
                    new SeededNonOriginalProcRandom(234), roster ?? new UnitRoster()) :
                new CharacterCombatRuntime(Units(), Skills(), missing, missing,
                    new SeededNonOriginalProcRandom(234), roster, game);
        }

        [Test]
        public void ExistingActorIdentityAndTimedEffectsRestoreWithoutSpawn()
        {
            var original = World();
            original.Start();
            original.BeginWave(1);
            long id = original.SpawnCharacter("TEST-01", 8, 12);
            original.Roster.Select(id);
            original.SpawnEnemy("TEST_ONLY_ENEMY", 100,
                CharacterEnemyKind.Line, 10, 0, 0);
            original.Step(1.25);
            var snapshot = original.CaptureRuntimeDraft();
            Assert.AreEqual(4, snapshot.Version);
            Assert.AreEqual(0, snapshot.Summons.Count);
            Assert.AreEqual(snapshot.Actors.Count, snapshot.ManualOwners.Count);
            var restoredRoster = UnitRoster.Restore(original.Roster.CaptureState());
            var restoredGame = GameSession.Restore(original.Game.CaptureState());
            var recovered = World(restoredRoster, restoredGame);
            recovered.RestoreEnemyWorldDraft(snapshot);
            recovered.RestoreActorWorldDraft(snapshot);
            Assert.AreEqual(1, recovered.CharacterCount);
            Assert.AreEqual(id, recovered.Roster.SelectedId.Value);
            Assert.AreEqual(8, recovered.Roster.Get(id).X);
            Assert.AreEqual(original.Time, recovered.Time, 1e-8);
            Assert.AreEqual(1, recovered.Game.AliveEnemies);
            Assert.AreEqual(100L, recovered.EnemyOf("TEST_ONLY_ENEMY").Health);
            Assert.AreEqual(original.CaptureRuntimeDraft().Actors[0].Abilities.Time,
                recovered.CaptureRuntimeDraft().Actors[0].Abilities.Time);
            Assert.AreEqual(id + 1,
                recovered.SpawnCharacter("TEST-01", 0, 0));
        }

        [Test]
        public void ForgedManualFormCannotBeReplayedOnUnrelatedActor()
        {
            var original = World();
            original.Start();
            original.BeginWave(1);
            original.SpawnCharacter("TEST-01", 0, 0);
            var state = original.CaptureRuntimeDraft();
            state.ManualOwners[0].MarcoForm = "phoenix";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateManualOwnerState(state));
            var recovered = World(
                UnitRoster.Restore(original.Roster.CaptureState()),
                GameSession.Restore(original.Game.CaptureState()));
            Assert.Throws<FormatException>(() =>
                recovered.RestoreActorWorldDraft(state));
            Assert.AreEqual(0, recovered.CharacterCount);
        }

        [Test]
        public void ForgedSummonAndFormAreRejectedBeforeWorldCreation()
        {
            var live = World();
            live.Start();
            live.BeginWave(1);
            long id = live.SpawnCharacter("TEST-01", 0, 0);
            var snapshot = live.CaptureRuntimeDraft();
            snapshot.NextSummonId = 1;
            snapshot.Summons.Add(new SourceCombatSummonCheckpoint {
                Id = 1, OwnerId = id, SkillName = "clone", Name = "clone",
                ExpiresAt = snapshot.Time + 10, HasIndependentCombat = true,
                SourceUnitRawcode = "h085"
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(snapshot));
            snapshot.Summons.Clear();
            snapshot.Forms.Add(new SourceCombatFormCheckpoint {
                OwnerId = id, Form = "forest",
                ExpiresAt = snapshot.Time + 5
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(snapshot));
            snapshot.Forms.Clear();
            snapshot.Forest.Add(new SourceCombatForestCheckpoint {
                OwnerId = id, Nutrients = 2
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(snapshot));
        }

        [Test]
        public void MissingOrDuplicatedManualOwnersFailClosed()
        {
            var original = World();
            original.Start();
            original.BeginWave(1);
            original.SpawnCharacter("TEST-01", 0, 0);
            var state = original.CaptureRuntimeDraft();
            state.ManualOwners.Clear();
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateManualOwnerState(state));
        }

        [Test]
        public void DeployedActorIsRejectedBeforeRestoringRuntime()
        {
            var original = World();
            original.Start();
            original.BeginWave(1);
            original.SpawnCharacter("TEST-01", 0, 0);
            var state = original.CaptureRuntimeDraft();
            state.Actors[0].StoryDeployed = true;
            var recovered = World(
                UnitRoster.Restore(original.Roster.CaptureState()),
                GameSession.Restore(original.Game.CaptureState()));
            Assert.Throws<NotSupportedException>(() =>
                recovered.RestoreActorWorldDraft(state));
            Assert.AreEqual(0, recovered.CharacterCount);
        }
    }
}
