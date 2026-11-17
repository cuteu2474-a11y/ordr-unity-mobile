using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceRemainingDelayedSaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint World()
        {
            var state = new SourceCombatRuntimeCheckpoint {
                Time = 4, HasSourceModelBasicAttacks = true,
                RemainingDelayed = new SourceRemainingDelayedOwnerCheckpoint {
                    NextSerial = 2
                },
                Timed = new CombatTimedEffectQueueState { Time = 4 }
            };
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 9, CatalogId = "MY-01",
                SourceLaneRevision = 2
            });
            state.Enemies.Add(new SourceCombatEnemyCheckpoint {
                EnemyId = "line-1", Kind = CharacterEnemyKind.Line
            });
            var pulse = new SourceRemainingDelayedPayload {
                OwnerId = 9, EnemyId = "line-1", CatalogId = "MY-01",
                UnitRawcode = "h09M", JassFunction = "CGd",
                LaneRevision = 2, Serial = 2,
                CastAt = 3, DelaySeconds = 2,
                CastX = 123, CastY = 456
            };
            pulse.Hits.Add(new SourceRemainingHitSnapshot {
                Shape = SourceScriptHitShape.AttackerCircle,
                Radius = 1600, Damage = 450000,
                Minimum = 1, Maximum = 1,
                Attack = Ordr323AttackType.Chaos,
                TargetMaxHealthMultiplier = .0045
            });
            state.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = 1, CasterId = "9",
                TargetId = "remaining-jass:9:2",
                At = 5, Kind = "source-remaining-jass-hit-v1",
                Payload = JsonUtility.ToJson(pulse)
            });
            return state;
        }

        [Test]
        public void MultiHitKeepsOriginalCastLocationAndNextSerial()
        {
            var state = World();
            CharacterCombatRuntime.ValidateRemainingDelayedState(state);
            state.RemainingDelayed.NextSerial = 1;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateRemainingDelayedState(state));
        }

        [Test]
        public void DuplicateSequenceAndDifferentCasterAreRejected()
        {
            var state = World();
            state.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = 2, CasterId = "9",
                TargetId = state.Timed.Pending[0].TargetId,
                At = 5, Kind = state.Timed.Pending[0].Kind,
                Payload = state.Timed.Pending[0].Payload
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateRemainingDelayedState(state));
            state.Timed.Pending.RemoveAt(1);
            state.Timed.Pending[0].CasterId = "10";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateRemainingDelayedState(state));
        }

        [Test]
        public void ForgedRawcodeTimingAndDamagePayloadAreRejected()
        {
            var state = World();
            var p = JsonUtility.FromJson<SourceRemainingDelayedPayload>(
                state.Timed.Pending[0].Payload);
            p.UnitRawcode = "h00A";
            state.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateRemainingDelayedState(state));
            state = World();
            state.Timed.Pending[0].At = 5.1;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateRemainingDelayedState(state));
            state = World();
            p = JsonUtility.FromJson<SourceRemainingDelayedPayload>(
                state.Timed.Pending[0].Payload);
            p.Hits[0].Damage = -1;
            state.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateRemainingDelayedState(state));
        }

        [Test]
        public void TargetOrLaneChangeCannotRebindOldPendingHit()
        {
            var state = World();
            state.Enemies.Clear();
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateRemainingDelayedState(state));
            state = World();
            var p = JsonUtility.FromJson<SourceRemainingDelayedPayload>(
                state.Timed.Pending[0].Payload);
            p.LaneRevision = 3;
            state.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateRemainingDelayedState(state));
        }

        [Test]
        public void BurgessTwoHitComboRetainsArmorAndHpModifiers()
        {
            var state = World();
            state.Actors[0].CatalogId = "D-06";
            var p = JsonUtility.FromJson<SourceRemainingDelayedPayload>(
                state.Timed.Pending[0].Payload);
            p.CatalogId = "D-06";
            p.UnitRawcode = "h09V";
            p.JassFunction = "CD0/CDz";
            p.Hits.Clear();
            p.Hits.Add(new SourceRemainingHitSnapshot {
                Shape = SourceScriptHitShape.Circle,
                Radius = 425, Damage = 100000,
                Minimum = 1, Maximum = 1,
                Attack = Ordr323AttackType.Spells
            });
            p.Hits.Add(new SourceRemainingHitSnapshot {
                Shape = SourceScriptHitShape.Direct, Radius = 0,
                Damage = 200000, Minimum = 1, Maximum = 1,
                Attack = Ordr323AttackType.Spells,
                LineCurrentHealthMultiplier = .1,
                HasLineAttackOverride = true,
                LineAttackOverride = Ordr323AttackType.Siege
            });
            state.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            CharacterCombatRuntime.ValidateRemainingDelayedState(state);
            p.Hits[1].Damage = 999999;
            state.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateRemainingDelayedState(state));
        }

        [Test]
        public void StructurallyValidButForeignJassHitIsRejected()
        {
            var state = World();
            var p = JsonUtility.FromJson<SourceRemainingDelayedPayload>(
                state.Timed.Pending[0].Payload);
            p.Hits[0].Damage = 999999;
            state.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateRemainingDelayedState(state));
        }
    }
}
