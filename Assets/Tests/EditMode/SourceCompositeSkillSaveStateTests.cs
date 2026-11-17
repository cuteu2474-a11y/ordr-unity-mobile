using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceCompositeSkillSaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint World()
        {
            var state = new SourceCombatRuntimeCheckpoint {
                Time = 7, HasSourceModelBasicAttacks = true,
                CompositeSkillOwners = new SourceCompositeSkillOwnerCheckpoint(),
                Timed = new CombatTimedEffectQueueState { Time = 7 }
            };
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 9, CatalogId = "LI-10",
                SourceLaneRevision = 2
            });
            state.Enemies.Add(new SourceCombatEnemyCheckpoint { EnemyId = "line-9" });
            return state;
        }

        [Test]
        public void KingSlowRefreshUsesLatestReceiptAndRejectsFutureEpoch()
        {
            var state = World();
            state.CompositeSkillOwners.KingSlowRevisions.Add(
                new SourceCompositeKingSlowRevision {
                    OwnerId = 9, EnemyId = "line-9", Revision = 4
                });
            var pulse = new SourceKingDelayedPulse {
                OwnerId = 9, EnemyId = "line-9", LaneRevision = 2,
                CastAt = 6.5, Tick = 1, Revision = 3
            };
            state.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = 1, CasterId = "9", TargetId = "line-9",
                Kind = "source-king-slow-v1", At = 7.5,
                Payload = JsonUtility.ToJson(pulse)
            });
            CharacterCombatRuntime.ValidateCompositeSkillOwners(state);
            pulse.Revision = 5;
            state.Timed.Pending[0].Payload = JsonUtility.ToJson(pulse);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateCompositeSkillOwners(state));
            pulse.Revision = 3;
            pulse.LaneRevision = 3;
            state.Timed.Pending[0].Payload = JsonUtility.ToJson(pulse);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateCompositeSkillOwners(state));
        }

        [Test]
        public void KingFireKeepsTenTickOffsetsAndCannotBorrowAnotherOwner()
        {
            var state = World();
            var pulse = new SourceKingDelayedPulse {
                OwnerId = 9, EnemyId = "line-9",
                LaneRevision = 2, CastAt = 6.9, Tick = 1
            };
            state.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = 1, CasterId = "9", TargetId = "line-9",
                Kind = "source-king-fire-v1", At = 7.17,
                Payload = JsonUtility.ToJson(pulse)
            });
            CharacterCombatRuntime.ValidateCompositeSkillOwners(state);
            state.Timed.Pending[0].At = 7.18;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateCompositeSkillOwners(state));
            state.Timed.Pending[0].At = 7.17;
            state.Timed.Pending[0].CasterId = "8";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateCompositeSkillOwners(state));
        }

        [Test]
        public void MariaKobyApproachClockAndKingUseBelongToOriginalCatalog()
        {
            var state = World();
            state.CompositeSkillOwners.KingExecutionUsed.Add(9);
            CharacterCombatRuntime.ValidateCompositeSkillOwners(state);
            state.CompositeSkillOwners.MariaAttackCount.Add(
                new SourceCompositeOwnerInt { OwnerId = 9, Value = 7 });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateCompositeSkillOwners(state));
            state.CompositeSkillOwners.MariaAttackCount.Clear();
            state.CompositeSkillOwners.PeriodicDue.Add(
                new SourceCompositePeriodicClock {
                    OwnerId = 9, SkillId = "TR-13:skill:0", DueAt = 21.25
                });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateCompositeSkillOwners(state));
        }

        [Test]
        public void PersistentPeriodicAndApproachReceiptsValidateKnownOwners()
        {
            var state = World();
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 12, CatalogId = "TR-13",
                SourceLaneRevision = 1
            });
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 13, CatalogId = "D-01",
                SourceLaneRevision = 1
            });
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 14, CatalogId = "TR-24",
                SourceLaneRevision = 1
            });
            state.CompositeSkillOwners.PeriodicDue.Add(
                new SourceCompositePeriodicClock {
                    OwnerId = 12, SkillId = "TR-13:skill:0",
                    DueAt = 14.25
                });
            state.CompositeSkillOwners.ApproachInside.Add(
                "12:TR-13:skill:1:line-9");
            state.CompositeSkillOwners.ApproachRolled.Add(
                "12:TR-13:skill:1:line-9");
            state.CompositeSkillOwners.MariaAttackCount.Add(
                new SourceCompositeOwnerInt { OwnerId = 13, Value = 8 });
            state.CompositeSkillOwners.KobyLastUpgradeWave.Add(
                new SourceCompositeOwnerInt { OwnerId = 14, Value = 15 });
            state.CompositeSkillOwners.KobyRoundStacks.Add(
                new SourceCompositeOwnerInt { OwnerId = 14, Value = 2 });
            CharacterCombatRuntime.ValidateCompositeSkillOwners(state);
            state.CompositeSkillOwners.ApproachInside.Add(
                "12:TR-13:skill:1:line-9");
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateCompositeSkillOwners(state));
            state.CompositeSkillOwners.ApproachInside.RemoveAt(1);
            state.CompositeSkillOwners.KobyLastUpgradeWave.Clear();
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateCompositeSkillOwners(state));
        }

        [Test]
        public void HakiClaimRetainsHistoricalOwnerUntilEnemyRetires()
        {
            var state = World();
            state.CompositeSkillOwners.HakiClaims.Add(
                new SourceCompositeTargetClaim {
                    EnemyId = "line-9",
                    Claimant = "123:TR-13:skill:1"
                });
            CharacterCombatRuntime.ValidateCompositeSkillOwners(state);
            state.CompositeSkillOwners.HakiClaims[0].Claimant =
                "123:LI-07:skill:2";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateCompositeSkillOwners(state));
        }
    }
}
