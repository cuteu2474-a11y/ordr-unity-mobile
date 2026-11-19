using System;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceKingKobySaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint Sample(
            string ownerCatalog, long ownerId = 4)
        {
            var state = new SourceCombatRuntimeCheckpoint {
                Time = 10,
                HasSourceModelBasicAttacks = true,
                KingKoby = new SourceKingKobyOwnerCheckpoint(),
                Timed = new CombatTimedEffectQueueState { Time = 10 }
            };
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = ownerId, CatalogId = ownerCatalog
            });
            state.Enemies.Add(new SourceCombatEnemyCheckpoint {
                EnemyId = "line-5", Kind = CharacterEnemyKind.Line
            });
            return state;
        }

        private static void AddKing(SourceCombatRuntimeCheckpoint state)
        {
            state.KingKoby.KingBasic.Add(new SourceKingBasicOwnerRow {
                OwnerId = 4, EnemyId = "line-5", ExpiresAt = 12.5
            });
            state.Timed.NextSequence = 1;
            state.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = 1, CasterId = "4", TargetId = "line-5",
                Kind = "source-king-basic-tick-v1",
                Payload = JsonUtility.ToJson(new SourceKingKobyTimedPayload {
                    OwnerId = 4, EnemyId = "line-5",
                    CastAt = 9.75, DelaySeconds = 1
                }),
                At = 10.75
            });
        }

        [Test]
        public void KingOneSecondTickMustMatchLiveOwnerLease()
        {
            var state = Sample("L-34");
            AddKing(state);
            CharacterCombatRuntime.ValidateKingKobyState(state);
            state.KingKoby.KingBasic[0].ExpiresAt = 30;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateKingKobyState(state));
        }

        [Test]
        public void KingLeaseCannotSurviveWithoutPendingNextTick()
        {
            var state = Sample("L-34");
            AddKing(state);
            state.Timed.Pending.Clear();
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateKingKobyState(state));
        }

        [Test]
        public void KingCannotDuplicateSameEnemyNextTick()
        {
            var state = Sample("L-34");
            AddKing(state);
            state.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = 2, CasterId = "4", TargetId = "line-5",
                Kind = state.Timed.Pending[0].Kind,
                Payload = state.Timed.Pending[0].Payload, At = 10.75
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateKingKobyState(state));
        }

        [Test]
        public void KobyCloneUsesVisualSummonAndTypedHitsNotNativeAttack()
        {
            var state = Sample("TR-24");
            state.Summons.Add(new SourceCombatSummonCheckpoint {
                Id = 8, OwnerId = 4,
                SkillName = "콤비네이션 어택",
                Name = "코비 분신",
                ExpiresAt = 14.5
            });
            state.Timed.NextSequence = 1;
            state.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = 1, CasterId = "4",
                TargetId = "practice-koby-clone:8",
                Kind = "source-koby-clone-hit-v1",
                Payload = JsonUtility.ToJson(new SourceKingKobyTimedPayload {
                    OwnerId = 4, SummonId = 8,
                    SkillId = "TR-24:skill:2", CastAt = 9.5,
                    DelaySeconds = 1, Hit = 1,
                    IntervalSeconds = 1, DurationSeconds = 5
                }),
                At = 10.5
            });
            CharacterCombatRuntime.ValidateKingKobyState(state);
            state.Summons[0].HasIndependentCombat = true;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateKingKobyState(state));
            state.Summons[0].HasIndependentCombat = false;
            state.Timed.Pending[0].TargetId = "practice-koby-clone:9";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateKingKobyState(state));
        }

        [Test]
        public void KatakuriTemporarySpeedMustHaveValidOwnerAndDeadline()
        {
            var state = Sample("LI-08");
            state.KingKoby.KatakuriSpeed.Add(new SourceKatakuriOwnerRow {
                OwnerId = 4, ExpiresAt = 11.2
            });
            CharacterCombatRuntime.ValidateKingKobyState(state);
            state.KingKoby.KatakuriSpeed[0].ExpiresAt = 19;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateKingKobyState(state));
        }

        [Test]
        public void OtherCombatModeCannotImportKingOwnerState()
        {
            var state = Sample("L-34");
            AddKing(state);
            state.HasSourceModelBasicAttacks = false;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateKingKobyState(state));
        }
    }
}
