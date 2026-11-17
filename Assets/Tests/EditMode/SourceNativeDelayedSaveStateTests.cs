using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceNativeDelayedSaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint Scenario(
            string kind, string payload, string caster, string target,
            double scheduledAt, string catalog = "S-20")
        {
            var saved = new SourceCombatRuntimeCheckpoint {
                Time = 8,
                HasSourceModelBasicAttacks = true,
                NativeDelayed = new SourceNativeDelayedOwnerCheckpoint(),
                Timed = new CombatTimedEffectQueueState { Time = 8 }
            };
            saved.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 8, CatalogId = catalog,
                StoryDeployed = false, SourceLaneRevision = 2
            });
            saved.Enemies.Add(new SourceCombatEnemyCheckpoint { EnemyId = "enemy-1" });
            saved.NativeDelayed.LiquidFire.Add(new SourceNativeLiquidFireRevision {
                EnemyId = "enemy-1", Revision = 3
            });
            saved.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = 1, CasterId = caster, TargetId = target,
                At = scheduledAt, Kind = kind, Payload = payload
            });
            return saved;
        }

        [Test]
        public void ValidNativeDelaysKeepOriginalTimestampAndOwner()
        {
            var burn = new SourceNativeDelayedEffectPayload {
                OwnerId = 8, EnemyId = "enemy-1",
                LaneRevision = 2, CastAt = 7.5,
                Tick = 1, Revision = 3
            };
            var state = Scenario("source-liquid-fire-v1",
                JsonUtility.ToJson(burn), "8", "enemy-1", 8.5);
            CharacterCombatRuntime.ValidateSourceNativeDelayedState(state);
            // A lane move makes an older point order inert, not an
            // unauthorized resumption of its previous-lane damage.
            burn.LaneRevision = 1;
            state.Timed.Pending[0].Payload = JsonUtility.ToJson(burn);
            CharacterCombatRuntime.ValidateSourceNativeDelayedState(state);
            burn.LaneRevision = 3; // forged future owner epoch
            state.Timed.Pending[0].Payload = JsonUtility.ToJson(burn);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceNativeDelayedState(state));

            var inferno = new SourceNativeDelayedEffectPayload {
                OwnerId = 8, LaneRevision = 2,
                CastAt = 8, CenterX = 3, CenterY = 6,
                OriginalAbilityRawcode = "A14X"
            };
            state = Scenario("source-inferno-v1",
                JsonUtility.ToJson(inferno), "8",
                "practice-inferno:8:A14X", 8.01, "R-13");
            CharacterCombatRuntime.ValidateSourceNativeDelayedState(state);

            var faerie = new SourceNativeDelayedEffectPayload {
                OwnerId = 8, EnemyId = "enemy-1", LaneRevision = 2,
                CastAt = 8, OriginalAbilityRawcode = "A10I"
            };
            state = Scenario("source-sentinel-faerie-v1",
                JsonUtility.ToJson(faerie), "8", "enemy-1", 8.01, "RN-04");
            CharacterCombatRuntime.ValidateSourceNativeDelayedState(state);
        }

        [Test]
        public void RejectsUntrustedTimedPayloadAndMissingOwner()
        {
            var bad = Scenario("source-liquid-fire-v1", "{}", "8", "enemy-1", 9);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceNativeDelayedState(bad));
            bad.Timed.Pending.Clear();
            CharacterCombatRuntime.ValidateSourceNativeDelayedState(bad);
            bad.NativeDelayed.LiquidFire[0].Revision = 0;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceNativeDelayedState(bad));
        }

        [Test]
        public void RejectsWrongOriginalAttackTypeAndDuplicatedLiquidFireRevision()
        {
            var bad = Scenario("source-liquid-fire-v1", "{}", "8", "enemy-1", 9);
            bad.Actors[0].CatalogId = "R-13";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceNativeDelayedState(bad));
            bad.Timed.Pending.Clear();
            bad.NativeDelayed.LiquidFire.Add(new SourceNativeLiquidFireRevision {
                EnemyId = "enemy-1", Revision = 4
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceNativeDelayedState(bad));
        }

        [Test]
        public void RejectsUnknownOriginalFaerieAndInfernoDescriptors()
        {
            var faerie = Scenario("source-sentinel-faerie-v1", "{}", "8",
                "enemy-1", 8.01, "RN-04");
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceNativeDelayedState(faerie));
            var inferno = Scenario("source-inferno-v1", "{}", "8",
                "practice-inferno:8:A14X", 8.01, "R-13");
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceNativeDelayedState(inferno));
        }
    }
}
