using System;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceAdditionalTimedSaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint World(
            string kind, string catalog, SourceAdditionalTimedPayload p,
            double at)
        {
            var combat = new SourceCombatRuntimeCheckpoint {
                Time = 10, HasSourceModelBasicAttacks = true,
                Timed = new CombatTimedEffectQueueState { Time = 10 }
            };
            combat.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 11, CatalogId = catalog,
                SourceLaneRevision = 2
            });
            combat.Enemies.Add(new SourceCombatEnemyCheckpoint {
                EnemyId = "line-1", Kind = CharacterEnemyKind.Line
            });
            combat.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = 1, CasterId = "11", TargetId = "line-1",
                Kind = kind, Payload = JsonUtility.ToJson(p), At = at
            });
            combat.Timed.NextSequence = 1;
            return combat;
        }

        private static SourceAdditionalTimedPayload Source(
            string skill, double castAt, double delay, double amount) =>
            new SourceAdditionalTimedPayload {
                OwnerId = 11, EnemyId = "line-1", SkillId = skill,
                LaneRevision = 2, CastAt = castAt,
                DelaySeconds = delay, OriginalAmount = amount
            };

        private static void Rewrite(SourceCombatRuntimeCheckpoint combat,
            SourceAdditionalTimedPayload p) =>
            combat.Timed.Pending[0].Payload = JsonUtility.ToJson(p);

        [Test]
        public void WhitebeardSlowPreservesOriginalThreePointFiveSecondTransition()
        {
            var p = Source("I-12:skill:2", 7, 3.5, 10);
            p.DurationSeconds = 2;
            var combat = World("source-additional-whitebeard-slow-v1",
                "I-12", p, 10.5);
            CharacterCombatRuntime.ValidateSourceAdditionalTimedState(combat);
            p.DelaySeconds = 3;
            Rewrite(combat, p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceAdditionalTimedState(
                    combat));
        }

        [Test]
        public void NarutoEverySecondTickIsBoundToSameCastAndEnemy()
        {
            var p = Source("RN-02:skill:1", 9, 2, 35000);
            p.IntervalSeconds = 1;
            p.DurationSeconds = 4;
            p.Tick = 2;
            var combat = World("source-additional-naruto-dot-v1",
                "RN-02", p, 11);
            CharacterCombatRuntime.ValidateSourceAdditionalTimedState(combat);
            p.Tick = 5;
            Rewrite(combat, p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceAdditionalTimedState(
                    combat));
        }

        [Test]
        public void ZoroBaseDamageAndBuffSensitiveTickRemainSeparate()
        {
            var p = Source("TR-20:skill:0", 9, 1.5, 3000);
            p.IntervalSeconds = .5;
            p.DurationSeconds = 3;
            p.Tick = 3;
            var combat = World("source-additional-zoro-dot-v1",
                "TR-20", p, 10.5);
            CharacterCombatRuntime.ValidateSourceAdditionalTimedState(combat);
            p.IntervalSeconds = .4;
            Rewrite(combat, p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceAdditionalTimedState(
                    combat));
        }

        [Test]
        public void OwnerAndTargetTamperingAreRejected()
        {
            var p = Source("RN-02:skill:1", 9, 2, 500);
            p.IntervalSeconds = 1;
            p.DurationSeconds = 4;
            p.Tick = 2;
            var combat = World("source-additional-naruto-dot-v1",
                "RN-02", p, 11);
            combat.Actors[0].CatalogId = "TR-20";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceAdditionalTimedState(
                    combat));
            combat.Actors[0].CatalogId = "RN-02";
            p.EnemyId = "fake-enemy";
            Rewrite(combat, p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceAdditionalTimedState(
                    combat));
        }

        [Test]
        public void OldLanePulseMayRemainButFutureLaneCannot()
        {
            var p = Source("RN-02:skill:1", 9, 2, 500);
            p.IntervalSeconds = 1;
            p.DurationSeconds = 4;
            p.Tick = 2;
            p.LaneRevision = 1;
            var combat = World("source-additional-naruto-dot-v1",
                "RN-02", p, 11);
            CharacterCombatRuntime.ValidateSourceAdditionalTimedState(combat);
            p.LaneRevision = 3;
            Rewrite(combat, p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceAdditionalTimedState(
                    combat));
        }

        [Test]
        public void ScheduledPastAndMismatchedClockCannotReplay()
        {
            var p = Source("I-12:skill:2", 7, 3.5, 10);
            p.DurationSeconds = 2;
            var combat = World("source-additional-whitebeard-slow-v1",
                "I-12", p, 10.5);
            combat.Timed.Pending[0].At = 10.6;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceAdditionalTimedState(
                    combat));
            combat.Timed.Pending[0].At = 10.5;
            p.CastAt = 6;
            Rewrite(combat, p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceAdditionalTimedState(
                    combat));
        }
    }
}
