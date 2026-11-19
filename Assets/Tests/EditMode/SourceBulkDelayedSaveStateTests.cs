using System;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceBulkDelayedSaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint Sample(
            string kind, SourceBulkDelayedPayload payload, double at)
        {
            var world = new SourceCombatRuntimeCheckpoint {
                Time = 10, HasSourceModelBasicAttacks = true,
                Timed = new CombatTimedEffectQueueState { Time = 10 }
            };
            world.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 7, CatalogId = "MY-01",
                SourceLaneRevision = 2
            });
            world.Enemies.Add(new SourceCombatEnemyCheckpoint {
                EnemyId = "line-1", Kind = CharacterEnemyKind.Line
            });
            world.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = 1, CasterId = "7", TargetId = "line-1",
                Kind = kind, Payload = JsonUtility.ToJson(payload), At = at
            });
            world.Timed.NextSequence = 1;
            return world;
        }

        private static SourceBulkDelayedPayload Damage() =>
            new SourceBulkDelayedPayload {
                OwnerId = 7, EnemyId = "line-1",
                SkillId = "MY-01:skill:3", LaneRevision = 2,
                CastAt = 10, DelaySeconds = .2, Tick = 1,
                IntervalSeconds = .2, DurationSeconds = 2,
                RawDamage = 2500, School = "magical"
            };

        [Test]
        public void NormalPendingDotRetainsItsAlreadyChosenDamageAndTick()
        {
            var p = Damage();
            var world = Sample("source-bulk-dot-v1", p, 10.2);
            CharacterCombatRuntime.ValidateSourceBulkDelayedState(world);
            p.RawDamage = -1;
            world.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkDelayedState(world));
        }

        [Test]
        public void ChangedCastClockTickOrOwnerCannotDoubleHit()
        {
            var p = Damage();
            var world = Sample("source-bulk-dot-v1", p, 10.2);
            p.Tick = 2;
            world.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkDelayedState(world));
            p.Tick = 1;
            p.LaneRevision = 3;
            world.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkDelayedState(world));
            p.LaneRevision = 2;
            world.Actors[0].CatalogId = "MY-07";
            world.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkDelayedState(world));
        }

        [Test]
        public void SourceI02AndByakuyaIntervalsCannotBeRewritten()
        {
            var p = Damage();
            p.SkillId = "I-02:skill:2";
            p.IntervalSeconds = .15;
            p.DelaySeconds = .15;
            var world = Sample("source-bulk-dot-v1", p, 10.15);
            world.Actors[0].CatalogId = "I-02";
            CharacterCombatRuntime.ValidateSourceBulkDelayedState(world);
            p.IntervalSeconds = .16;
            world.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkDelayedState(world));
            p.SkillId = "MY-05:skill:3";
            p.School = "fixed";
            p.DurationSeconds = 4;
            p.DelaySeconds = .16;
            world.Actors[0].CatalogId = "MY-05";
            world.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            world.Timed.Pending[0].At = 10.16;
            CharacterCombatRuntime.ValidateSourceBulkDelayedState(world);
        }

        [Test]
        public void OriginalThreeSecondLingeringSlowIsRestorable()
        {
            var p = new SourceBulkDelayedPayload {
                OwnerId = 7, EnemyId = "line-1",
                SkillId = "TR-01:skill:1", LaneRevision = 2,
                CastAt = 9, DelaySeconds = 3,
                SlowPercent = 20, SlowDurationSeconds = 2
            };
            var world = Sample("source-bulk-slow-v1", p, 12);
            world.Actors[0].CatalogId = "TR-01";
            CharacterCombatRuntime.ValidateSourceBulkDelayedState(world);
            p.DelaySeconds = 2;
            world.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkDelayedState(world));
        }

        [Test]
        public void OldLaneEffectCannotClaimARevisionFromTheFuture()
        {
            var p = Damage();
            p.LaneRevision = 1;
            var world = Sample("source-bulk-dot-v1", p, 10.2);
            CharacterCombatRuntime.ValidateSourceBulkDelayedState(world);
            p.LaneRevision = -1;
            world.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkDelayedState(world));
            p.LaneRevision = 1;
            world.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            world.Enemies.Clear();
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceBulkDelayedState(world));
        }
    }
}
