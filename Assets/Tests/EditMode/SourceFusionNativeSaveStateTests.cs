using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceFusionNativeSaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint World(double time = 10)
        {
            var result = new SourceCombatRuntimeCheckpoint {
                Time = time, HasSourceModelBasicAttacks = true,
                Timed = new CombatTimedEffectQueueState { Time = time },
                FusionNative = new SourceFusionNativeWorldCheckpoint {
                    NextPulseId = 4
                }
            };
            result.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 9, CatalogId = "I-04", SourceLaneRevision = 1
            });
            result.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 12, CatalogId = "E-11", SourceLaneRevision = 1
            });
            result.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 16, CatalogId = "SR-03", SourceLaneRevision = 1
            });
            result.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 17, CatalogId = "TR-03", SourceLaneRevision = 1
            });
            result.Enemies.Add(new SourceCombatEnemyCheckpoint {
                EnemyId = "line-2", Kind = CharacterEnemyKind.Line
            });
            result.Enemies.Add(new SourceCombatEnemyCheckpoint {
                EnemyId = "boss-2", Kind = CharacterEnemyKind.Boss
            });
            return result;
        }

        private static void AddPulse(SourceCombatRuntimeCheckpoint world,
            SourceFusionNativePulse pulse)
        {
            world.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = pulse.Serial,
                CasterId = pulse.OwnerId.ToString(),
                TargetId = "fusion-native:" + pulse.Serial,
                Kind = pulse.Kind, At = pulse.CastAt + pulse.DelaySeconds,
                Payload = JsonUtility.ToJson(pulse)
            });
        }

        [Test]
        public void Roger150SecondCooldownAndPendingImpactMustKeepCastIdentity()
        {
            var w = World();
            w.FusionNative.RogerCooldowns.Add(new SourceFusionClockReceipt {
                OwnerId = 9, Until = 159
            });
            AddPulse(w, new SourceFusionNativePulse {
                Kind = "fusion-roger-impact-v1", Serial = 1,
                OwnerId = 9, TargetId = "boss-2",
                LaneRevision = 1, CastAt = 9, DelaySeconds = 2.2
            });
            CharacterCombatRuntime.ValidateSourceFusionNativeOwners(w);
            w.FusionNative.RogerCooldowns[0].Until = 158;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceFusionNativeOwners(w));
            w.FusionNative.RogerCooldowns[0].Until = 159;
            w.Enemies.RemoveAt(1);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceFusionNativeOwners(w));
        }

        [Test]
        public void TesoroGoldIsDeclarationTimeCappedAndSpawnLevelsAreFixed()
        {
            var w = World();
            w.FusionNative.TesoroCounts.Add(new SourceFusionCountReceipt {
                OwnerId = 12, Count = 2
            });
            w.FusionNative.EnemyLevels.Add(new SourceFusionEnemyLevelReceipt {
                EnemyId = "line-2", Level = 21
            });
            w.FusionNative.EnemyMagicFactors.Add(new SourceFusionEnemyFactorReceipt {
                EnemyId = "line-2", Factor = .9
            });
            AddPulse(w, new SourceFusionNativePulse {
                Kind = "fusion-tesoro-impact-v1", Serial = 2,
                OwnerId = 12, TargetId = "line-2",
                LaneRevision = 1, CastAt = 9.75,
                DelaySeconds = .45, GoldCap = 250000
            });
            CharacterCombatRuntime.ValidateSourceFusionNativeOwners(w);
            var bad = JsonUtility.FromJson<SourceFusionNativePulse>(
                w.Timed.Pending[0].Payload);
            bad.GoldCap = 250001;
            w.Timed.Pending[0].Payload = JsonUtility.ToJson(bad);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceFusionNativeOwners(w));
            w.Timed.Pending[0].Payload = JsonUtility.ToJson(
                new SourceFusionNativePulse {
                    Kind = "fusion-tesoro-impact-v1", Serial = 2,
                    OwnerId = 12, TargetId = "line-2",
                    LaneRevision = 1, CastAt = 9.75,
                    DelaySeconds = .45, GoldCap = 250000
                });
            w.FusionNative.EnemyLevels[0].Level = -1;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceFusionNativeOwners(w));
        }

        [Test]
        public void SharkPeriodicOwnershipAndDelayedAllyMustHaveOneMaster()
        {
            var w = World();
            w.FusionNative.ActiveSharkOwners.Add(16);
            AddPulse(w, new SourceFusionNativePulse {
                Kind = "fusion-shark-cycle-v1", Serial = 1,
                OwnerId = 16, LaneRevision = 1,
                CastAt = 9.8, DelaySeconds = 19.9
            });
            AddPulse(w, new SourceFusionNativePulse {
                Kind = "fusion-shark-ally-v1", Serial = 2,
                OwnerId = 16, AllyId = 17, LaneRevision = 1,
                CastAt = 9.95, DelaySeconds = .1
            });
            CharacterCombatRuntime.ValidateSourceFusionNativeOwners(w);
            w.Timed.Pending.RemoveAt(0);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceFusionNativeOwners(w));
            w = World();
            w.FusionNative.ActiveSharkOwners.Add(16);
            AddPulse(w, new SourceFusionNativePulse {
                Kind = "fusion-shark-cycle-v1", Serial = 1,
                OwnerId = 16, LaneRevision = 1,
                CastAt = 9.8, DelaySeconds = 19.9
            });
            w.FusionNative.ActiveSharkOwners.Add(16);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceFusionNativeOwners(w));
        }

        [Test]
        public void DuplicatePulseIdsForeignActorsAndOldLaneRevisionsAreRejected()
        {
            var w = World();
            AddPulse(w, new SourceFusionNativePulse {
                Kind = "fusion-tesoro-impact-v1", Serial = 2,
                OwnerId = 12, TargetId = "line-2",
                LaneRevision = 1, CastAt = 9.75,
                DelaySeconds = .45, GoldCap = 1
            });
            AddPulse(w, new SourceFusionNativePulse {
                Kind = "fusion-tesoro-impact-v1", Serial = 2,
                OwnerId = 12, TargetId = "line-2",
                LaneRevision = 1, CastAt = 9.8,
                DelaySeconds = .45, GoldCap = 1
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceFusionNativeOwners(w));
            w.Timed.Pending.RemoveAt(1);
            var p = JsonUtility.FromJson<SourceFusionNativePulse>(
                w.Timed.Pending[0].Payload);
            p.OwnerId = 17;
            w.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceFusionNativeOwners(w));
            p.OwnerId = 12;
            p.LaneRevision = 2;
            w.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceFusionNativeOwners(w));
        }

        [Test]
        public void MissingSourceModelCannotCarryOriginalFusionReceipts()
        {
            var w = World();
            w.HasSourceModelBasicAttacks = false;
            w.FusionNative.TesoroCounts.Add(new SourceFusionCountReceipt {
                OwnerId = 12, Count = 1
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceFusionNativeOwners(w));
        }
    }
}
