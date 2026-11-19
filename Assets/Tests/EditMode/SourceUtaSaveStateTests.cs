using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceUtaSaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint World()
        {
            var state = new SourceCombatRuntimeCheckpoint {
                Time = 10,
                HasSourceModelBasicAttacks = true,
                NextSummonId = 2,
                Uta = new SourceUtaOwnerCheckpoint {
                    SongElapsed = 10
                },
                Timed = new CombatTimedEffectQueueState {
                    Time = 10
                }
            };
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 9, CatalogId = "E-09",
                SourceManagedResources = true, SourceLaneRevision = 2
            });
            return state;
        }

        private static void AddStarted(SourceCombatRuntimeCheckpoint state)
        {
            state.Uta.IntroOwners.Add(9);
            state.Uta.CycleClocks.Add(new SourceUtaClockCheckpoint {
                OwnerId = 9, At = 10.02
            });
        }

        private static void AddSong(SourceCombatRuntimeCheckpoint state)
        {
            var status = new CombatStatusEngine();
            status.Advance(10);
            state.Summons.Add(new SourceCombatSummonCheckpoint {
                Id = 2, OwnerId = 9, LaneRevision = 2,
                Name = "우타 · 음표 병사",
                SkillName = "B6s · 음표 병사",
                SourceUnitRawcode = "h04O",
                SpawnX = 200, SpawnY = 400,
                ExpiresAt = 16, HasIndependentCombat = true,
                IsVirtualAttackEffect = true,
                OrderedAttackTargetId = "defeated-enemy",
                AttackStatus = status.CaptureState()
            });
            state.Uta.PendingSongIds.Add(2);
        }

        private static void AddPulse(SourceCombatRuntimeCheckpoint state,
            long serial, string kind, double castAt, double delay)
        {
            var pulse = new SourceUtaPulse {
                Kind = kind, Serial = serial, OwnerId = 9,
                LaneRevision = 2, CastAt = castAt,
                DelaySeconds = delay, CenterX = 200, CenterY = 400
            };
            if (kind != "source-uta-b6x-intro-v1")
            {
                pulse.OriginalPrimaryId = "removed-primary";
                pulse.SongCount = 1;
                if (kind == "source-uta-b6o-order-v1")
                    pulse.SongIds.Add(2);
            }
            state.Uta.NextPulseId = Math.Max(state.Uta.NextPulseId, serial);
            state.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = serial, CasterId = "9",
                TargetId = "uta-native:" + serial, Kind = kind,
                At = castAt + delay, Payload = JsonUtility.ToJson(pulse)
            });
        }

        [Test]
        public void NativeRotationIntroManaGatesAndH0AFCanRestoreTogether()
        {
            var state = World();
            AddStarted(state);
            state.Uta.TransformedOwners.Add(9);
            state.Uta.ManaGates.Add(new SourceUtaClockCheckpoint {
                OwnerId = 9, At = 10.72
            });
            CharacterCombatRuntime.ValidateSourceUtaOwners(state);
            state.Uta.CycleClocks[0].At = 13.01;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceUtaOwners(state));
            state.Uta.CycleClocks[0].At = 10.02;
            state.Uta.IntroOwners.Clear();
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceUtaOwners(state));
        }

        [Test]
        public void IndependentH04OIsNotAnOwnedUnitAndRetainsAttackonce()
        {
            var state = World();
            AddStarted(state);
            AddSong(state);
            CharacterCombatRuntime.ValidateSourceSidecars(state);
            CharacterCombatRuntime.ValidateSourceUtaOwners(state);
            state.Uta.ActiveSongIds.Add(2);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceUtaOwners(state));
            state.Uta.ActiveSongIds.Clear();
            state.Summons[0].IsVirtualAttackEffect = false;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.Summons[0].IsVirtualAttackEffect = true;
            state.Summons[0].OrderedAttackTargetId = null;
            CharacterCombatRuntime.ValidateSourceSidecars(state);
        }

        [Test]
        public void B6sProcSoldierCanExistBeforeManualB7WIntroduction()
        {
            var state = World();
            AddSong(state);
            CharacterCombatRuntime.ValidateSourceSidecars(state);
            CharacterCombatRuntime.ValidateSourceUtaOwners(state);
            state.Uta.PendingSongIds.Add(2);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceUtaOwners(state));
        }

        [Test]
        public void IntroWithPendingRandomOffsetIsBoundToOneOriginalCast()
        {
            var state = World();
            AddStarted(state);
            AddPulse(state, 1, "source-uta-b6x-intro-v1", 9.5, 1);
            CharacterCombatRuntime.ValidateSourceUtaOwners(state);
            var pulse = JsonUtility.FromJson<SourceUtaPulse>(
                state.Timed.Pending[0].Payload);
            pulse.CenterX = double.PositiveInfinity;
            state.Timed.Pending[0].Payload = JsonUtility.ToJson(pulse);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceUtaOwners(state));
            pulse.CenterX = 200;
            pulse.SongCount = 1;
            state.Timed.Pending[0].Payload = JsonUtility.ToJson(pulse);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceUtaOwners(state));
        }

        [Test]
        public void B6oOrderAndB6rImpactKeepHistoricalSummonsAndDeletedPrimary()
        {
            var state = World();
            AddStarted(state);
            AddSong(state);
            AddPulse(state, 1, "source-uta-b6o-order-v1", 9.99, .02);
            AddPulse(state, 2, "source-uta-b6r-impact-v1", 9.6,
                SourceModelUtaB6rRules.WindupSeconds);
            CharacterCombatRuntime.ValidateSourceUtaOwners(state);
            state.Summons.Clear();
            state.Uta.PendingSongIds.Clear();
            // B6o/B6r retain the count and old IDs after the original
            // soldier dies. No re-spawn or extra independent attack is owed.
            CharacterCombatRuntime.ValidateSourceUtaOwners(state);
            state.Timed.Pending[0].TargetId = "uta-native:2";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceUtaOwners(state));
        }

        [Test]
        public void MissingOwnerForeignLaneOrForgedRotationFailsClosed()
        {
            var state = World();
            AddStarted(state);
            state.Uta.TransformedOwners.Add(9);
            state.Uta.SongRotations = 1;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceUtaOwners(state));
            state.Uta.SongRotations = 0;
            AddPulse(state, 1, "source-uta-b6x-intro-v1", 9.5, 1);
            state.Timed.Pending[0].CasterId = "999";
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceUtaOwners(state));
            state.Timed.Pending[0].CasterId = "9";
            var pulse = JsonUtility.FromJson<SourceUtaPulse>(
                state.Timed.Pending[0].Payload);
            pulse.LaneRevision = 3;
            state.Timed.Pending[0].Payload = JsonUtility.ToJson(pulse);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceUtaOwners(state));
        }
    }
}
