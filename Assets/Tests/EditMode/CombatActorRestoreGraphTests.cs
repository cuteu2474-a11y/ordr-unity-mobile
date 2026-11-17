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
        public void EquipmentSaveMustBelongToAnActualLiveCombatActor()
        {
            var original = World();
            original.Start();
            original.BeginWave(1);
            long id = original.SpawnCharacter("TEST-01", 0, 0);
            var save = original.CaptureRuntimeDraft();
            save.HasSourceModelBasicAttacks = true;
            save.EquippedItems = new OriginalEquippedItemLedgerState();
            save.EquippedItems.ReceiptOwners.Add(id + 100);
            save.EquippedItems.Receipts.Add("witnessed-transaction");
            save.EquippedItems.Owners.Add(new OriginalEquippedOwnerState {
                OwnerId = id + 100,
                Stacks = new System.Collections.Generic.List<OriginalEquippedStackState> {
                    new OriginalEquippedStackState {
                        Rawcode = "Iatk", Count = 1,
                        Stat = OriginalEquippedStat.Attack,
                        Rarity = OriginalEquippedRarity.Unique
                    }
                }
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateEquipmentState(save));
            save.EquippedItems.Owners[0].OwnerId = id;
            save.EquippedItems.ReceiptOwners[0] = id;
            Assert.DoesNotThrow(() =>
                CharacterCombatRuntime.ValidateEquipmentState(save));
        }

        [Test]
        public void BulletStanceSaveRejectsForeignExpiredAndDuplicatedEntitlements()
        {
            var original = World();
            original.Start();
            original.BeginWave(1);
            long id = original.SpawnCharacter("TEST-01", 0, 0);
            var state = original.CaptureRuntimeDraft();
            state.HasSourceModelBasicAttacks = true;
            state.Actors[0].CatalogId = "I-05"; // validation-only fixture
            state.BulletStances.Add(new SourceCombatBulletStanceCheckpoint {
                OwnerId = id, LaneRevision = state.Actors[0].SourceLaneRevision,
                ExpiresAt = state.Time + 2
            });
            Assert.DoesNotThrow(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.BulletStances.Add(new SourceCombatBulletStanceCheckpoint {
                OwnerId = id, LaneRevision = state.Actors[0].SourceLaneRevision,
                ExpiresAt = state.Time + 2
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.BulletStances.RemoveAt(1);
            state.BulletStances[0].LaneRevision++;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.BulletStances[0].LaneRevision--;
            state.BulletStances[0].ExpiresAt = state.Time;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.BulletStances[0].ExpiresAt = state.Time + 2;
            state.Actors[0].CatalogId = "C-01";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.Actors[0].CatalogId = "I-05";
            state.BulletStances[0].ExpiresAt = state.Time + 4;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.BulletStances[0].ExpiresAt = state.Time + 2;
            state.HasSourceModelBasicAttacks = false;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
        }

        [Test]
        public void NamiZoneAndMoriaOwnerCannotBeForgedOrDuplicated()
        {
            var original = World();
            original.Start();
            original.BeginWave(1);
            long id = original.SpawnCharacter("TEST-01", 0, 0);
            original.SpawnEnemy("line", 100, CharacterEnemyKind.Line, 0, 0, 0);
            var state = original.CaptureRuntimeDraft();
            state.HasSourceModelBasicAttacks = true;
            state.Actors[0].CatalogId = "S-09";
            state.MoriaParticipation.Participated.Add("line");
            var row = new Ordr.Mobile.Mobile.MoriaParticipationOwnerState {
                EnemyId = "line"
            };
            row.OwnerIds.Add(id);
            state.MoriaParticipation.Owners.Add(row);
            Assert.DoesNotThrow(() =>
                CharacterCombatRuntime.ValidateSourceTransientState(state));
            row.OwnerIds[0] = id + 9;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceTransientState(state));
            row.OwnerIds[0] = id;
            state.NamiNextSequence = 1;
            state.NamiZones.Add(new SourceCombatNamiZoneCheckpoint {
                Source = "practice-nami:A089:1", X = 100, Y = 0,
                ExpiresAt = state.Time + 2
            });
            Assert.DoesNotThrow(() =>
                CharacterCombatRuntime.ValidateSourceTransientState(state));
            state.NamiZones.Add(new SourceCombatNamiZoneCheckpoint {
                Source = "practice-nami:A089:1",
                ExpiresAt = state.Time + 2
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceTransientState(state));
            state.NamiZones.RemoveAt(1);
            state.NamiZones[0].ExpiresAt = state.Time + 4;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceTransientState(state));
        }

        [Test]
        public void MariaActiveLoopRequiresExactlyOneTypedFuturePulse()
        {
            var original = World();
            original.Start();
            original.BeginWave(1);
            long id = original.SpawnCharacter("TEST-01", 0, 0);
            var state = original.CaptureRuntimeDraft();
            state.HasSourceModelBasicAttacks = true;
            state.Actors[0].CatalogId = "D-01";
            state.MariaNextToken = 1;
            state.ManualOwners[0].MariaStage = 0;
            state.ManualOwners[0].MariaToken = 1;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateMariaPulseState(state));
            state.Timed.NextSequence = 1;
            state.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = 1, CasterId = id.ToString(),
                TargetId = "practice-maria:" + id + ":1:0",
                At = state.Time + .06, Kind = "source-maria-cdh-v1",
                Payload = id + "|1|0|0|0"
            });
            Assert.DoesNotThrow(() =>
                CharacterCombatRuntime.ValidateMariaPulseState(state));
            state.Timed.Pending[0].TargetId = "practice-maria:999:1:0";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateMariaPulseState(state));
            state.Timed.Pending[0].TargetId =
                "practice-maria:" + id + ":1:0";
            state.Timed.Pending[0].At = state.Time + 6;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateMariaPulseState(state));
        }

        [Test]
        public void PoneglyphChannelKeepsTheSamePausedOwnerWithoutRestarting()
        {
            var original = World();
            original.Start();
            original.BeginWave(1);
            long owner = original.SpawnCharacter("TEST-01", 9, 11);
            original.SetOriginalPoneglyphChanneling(owner, true);
            var state = original.CaptureRuntimeDraft();
            var poneglyph = new Original323PoneglyphState {
                SharedCompletedCount = 8, HasActiveUnit = true,
                ActiveUnitId = owner, ActiveDurationSeconds = 150,
                AttemptCount = 1, StartedAt = 0, FinishesAt = 150,
                LastMessage = "source recorded decode"
            };
            for (int i = 0; i < 8; i++)
            {
                poneglyph.Missions[i] = true;
                poneglyph.AcceptedEvents.Add("source-event-" + i);
            }
            CharacterCombatRuntime.ValidateExternalLaneState(state,
                original.Roster.CaptureState(), poneglyph);
            var restored = World(
                UnitRoster.Restore(original.Roster.CaptureState()),
                GameSession.Restore(original.Game.CaptureState()));
            restored.RestoreEnemyWorldDraft(state);
            restored.RestoreActorWorldDraft(state, poneglyph);
            Assert.IsTrue(restored.IsOriginalPoneglyphChanneling(owner));
            Assert.IsTrue(restored.CaptureRuntimeDraft().Actors[0].PoneglyphChanneling);
            Assert.AreEqual(9, restored.Roster.Get(owner).X);
            Assert.AreEqual(11, restored.Roster.Get(owner).Y);
            poneglyph.ActiveUnitId = owner + 1;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateExternalLaneState(state,
                    original.Roster.CaptureState(), poneglyph));
        }

        [Test]
        public void SharedObjectiveOwnerRestoresWithoutMovementAndRejectsFakeHistory()
        {
            var original = World();
            original.Start();
            original.BeginWave(1);
            long owner = original.SpawnCharacter("TEST-01", 8, 9);
            const string enemyId = "shared:objective:SeaKing:1";
            original.SpawnEnemyWithDefenses(enemyId, 100,
                CharacterEnemyKind.SeaKing, 8, 9, 0,
                new EnemyDefenseProfile(CharacterEnemyKind.SeaKing,
                    0, null, "hero",
                    "original observed shared objective native-sea-king-1 / source-observed"));
            var state = original.CaptureRuntimeDraft();
            state.Actors[0].SharedObjectiveEnemyId = enemyId;
            state.SharedDeployments.Add(new SourceCombatSharedDeploymentCheckpoint {
                OwnerId = owner, EnemyId = enemyId,
                ReturnX = 2, ReturnY = 3,
                PositionX = 8, PositionY = 9
            });
            var observed = new Original323SharedObjectiveWorldState { Serial = 1 };
            observed.Encounters.Add(new Original323SharedObjectiveEncounterCheckpoint {
                Mission = Original323SharedMissionKind.SeaKing,
                SourceKey = "CNW", ObjectRawcode = "o02N",
                SourceEventId = "native-sea-king-1",
                MaximumHealth = 100, Armor = 0, ArmorType = "hero",
                X = 8, Y = 9,
                EnemyId = enemyId, State = Original323SharedObjectiveState.Alive,
                SpawnEvidence = "source-observed",
                GateSpawnDebtSettled = true
            });
            var poneglyph = new Original323PoneglyphState();
            CharacterCombatRuntime.ValidateExternalLaneState(state,
                original.Roster.CaptureState(), poneglyph, observed);
            var restored = World(
                UnitRoster.Restore(original.Roster.CaptureState()),
                GameSession.Restore(original.Game.CaptureState()));
            restored.RestoreEnemyWorldDraft(state);
            restored.RestoreActorWorldDraft(state, poneglyph, observed);
            Assert.AreEqual(enemyId, restored.SharedObjectiveTargetOf(owner));
            Assert.AreEqual(8, restored.Roster.Get(owner).X);
            Assert.AreEqual(9, restored.Roster.Get(owner).Y);
            Assert.IsTrue(restored.TryRecallFromSharedObjective(owner));
            Assert.AreEqual(2, restored.Roster.Get(owner).X);
            Assert.AreEqual(3, restored.Roster.Get(owner).Y);
            observed.Encounters[0].SourceEventId = "forged";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateExternalLaneState(state,
                    original.Roster.CaptureState(), poneglyph, observed));
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
