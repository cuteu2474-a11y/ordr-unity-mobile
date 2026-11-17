using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceTemporaryOwnerSaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint Sample(
            string kind, SourceTemporaryDeferredPayload pulse,
            string target, double at, string catalog)
        {
            var state = new SourceCombatRuntimeCheckpoint {
                HasSourceModelBasicAttacks = true, Time = 5,
                TemporaryOwners = new SourceTemporaryOwnerCheckpoint(),
                Timed = new CombatTimedEffectQueueState { Time = 5 }
            };
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 9, CatalogId = catalog,
                SourceLaneRevision = 2
            });
            state.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = 1, CasterId = "9",
                TargetId = target, At = at, Kind = kind,
                Payload = JsonUtility.ToJson(pulse)
            });
            return state;
        }

        [Test]
        public void ValidFormWindupRetainsOnlyOneOwnerDeadline()
        {
            var pulse = new SourceTemporaryDeferredPayload {
                OwnerId = 9, LaneRevision = 2, CastAt = 4.5,
                DelaySeconds = 1, SkillId = "TR-14:skill:0"
            };
            var state = Sample("source-form-windup-v1",
                pulse, "practice-form-cast:9", 5.5, "TR-14");
            state.TemporaryOwners.FormWindups.Add(
                new SourceTemporaryOwnerDeadline {
                    OwnerId = 9, FinishesAt = 5.5
                });
            CharacterCombatRuntime.ValidateSourceTemporaryOwners(state);
            state.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = 2, CasterId = "9",
                TargetId = "practice-form-cast:9", At = 5.5,
                Kind = "source-form-windup-v1", Payload = JsonUtility.ToJson(pulse)
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceTemporaryOwners(state));
            state.Timed.Pending.RemoveAt(1);
            state.TemporaryOwners.FormWindups[0].FinishesAt = 6.5;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceTemporaryOwners(state));
        }

        [Test]
        public void AttackPauseOlderTokenIsInertButLatestMustHaveReceipt()
        {
            var pulse = new SourceTemporaryDeferredPayload {
                OwnerId = 9, LaneRevision = 2, CastAt = 4.5,
                DelaySeconds = 1.5, Token = 3,
                EnemyId = "line-1", SkillName = "박치기"
            };
            var state = Sample("source-attack-pause-v1",
                pulse, "practice-attack-pause:9:3", 6, "R-01");
            state.TemporaryOwners.AttackPauseSerial = 4;
            state.TemporaryOwners.AttackPauses.Add(
                new SourceTemporaryOwnerDeadline {
                    OwnerId = 9, FinishesAt = 6.5, Token = 4
                });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceTemporaryOwners(state));
            var newer = new SourceTemporaryDeferredPayload {
                OwnerId = 9, LaneRevision = 2, CastAt = 5,
                DelaySeconds = 1.5, Token = 4,
                EnemyId = "line-2", SkillName = "박치기"
            };
            state.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = 2, CasterId = "9",
                TargetId = "practice-attack-pause:9:4", At = 6.5,
                Kind = "source-attack-pause-v1",
                Payload = JsonUtility.ToJson(newer)
            });
            CharacterCombatRuntime.ValidateSourceTemporaryOwners(state);
            state.Timed.Pending.RemoveAt(1);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceTemporaryOwners(state));
        }

        [Test]
        public void FormIdentityAndYamatoOpeningMustBelongToOwnedCatalog()
        {
            var payload = new SourceTemporaryDeferredPayload {
                OwnerId = 9, LaneRevision = 2, CastAt = 4.5,
                DelaySeconds = 1, SkillId = "TR-14:skill:0"
            };
            var state = Sample("source-form-windup-v1",
                payload, "practice-form-cast:9", 5.5, "TR-14");
            state.TemporaryOwners.FormWindups.Add(
                new SourceTemporaryOwnerDeadline {
                    OwnerId = 9, FinishesAt = 5.5
                });
            state.TemporaryOwners.FormNames.Add(
                new SourceTemporaryFormName {
                    OwnerId = 9, Name = "black-mamba"
                });
            CharacterCombatRuntime.ValidateSourceTemporaryOwners(state);
            state.TemporaryOwners.YamatoOpeningReady.Add(9);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceTemporaryOwners(state));
            state.TemporaryOwners.YamatoOpeningReady.Clear();
            state.TemporaryOwners.FormNames[0].OwnerId = 99;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceTemporaryOwners(state));
        }

        [Test]
        public void SugarProjectileKeepsOriginalSummonIdAndLane()
        {
            var pulse = new SourceTemporaryDeferredPayload {
                OwnerId = 9, LaneRevision = 2, CastAt = 5,
                DelaySeconds = .35, SummonId = 7,
                EnemyId = "line-7", OriginalAbilityRawcode = "A03A"
            };
            var state = Sample("source-sugar-doll-v1",
                pulse, "remaining-doll:9:7", 5.35, "R-24");
            state.Summons.Add(new SourceCombatSummonCheckpoint {
                Id = 7, OwnerId = 9, LaneRevision = 2,
                SourceUnitRawcode = "h06O",
                IsVirtualAttackEffect = true, ExpiresAt = 8.5
            });
            CharacterCombatRuntime.ValidateSourceTemporaryOwners(state);
            state.Summons[0].IsVirtualAttackEffect = false;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceTemporaryOwners(state));
            state.Summons.Clear();
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceTemporaryOwners(state));
        }
    }
}
