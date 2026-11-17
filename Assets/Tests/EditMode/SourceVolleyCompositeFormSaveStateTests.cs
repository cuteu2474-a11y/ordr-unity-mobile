using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceVolleyCompositeFormSaveStateTests
    {
        private static SourceCombatRuntimeCheckpoint Form(
            string catalog, string form, double finish)
        {
            var state = new SourceCombatRuntimeCheckpoint {
                Time = 10, HasSourceModelBasicAttacks = true
            };
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 9, CatalogId = catalog,
                SourceManagedResources = true,
                SourceLaneRevision = 2
            });
            state.Forms.Add(new SourceCombatFormCheckpoint {
                OwnerId = 9, Form = form,
                ExpiresAt = finish, LaneRevision = 2
            });
            return state;
        }

        [Test]
        public void FiveFiniteFormsRequireExactOriginalCatalogAndDuration()
        {
            foreach (var spec in new[] {
                Tuple.Create("TR-03", "awakening", 4.5),
                Tuple.Create("TR-06", "forest", 7.0),
                Tuple.Create("E-01", "giant", 4.25),
                Tuple.Create("LI-10", "executioner", 1.25),
                Tuple.Create("TR-28", "frankyGeneral", 6.0)
            })
            {
                var state = Form(spec.Item1, spec.Item2, 10 + spec.Item3);
                CharacterCombatRuntime.ValidateSourceSidecars(state);
                state.Forms[0].ExpiresAt += 1;
                Assert.Throws<FormatException>(() =>
                    CharacterCombatRuntime.ValidateSourceSidecars(state));
                state.Forms[0].ExpiresAt = 10 + spec.Item3;
                state.Actors[0].CatalogId = "TR-29";
                Assert.Throws<FormatException>(() =>
                    CharacterCombatRuntime.ValidateSourceSidecars(state));
            }
        }

        [Test]
        public void NikaRequiresManaLifetimeSentinelAndOriginalBonneyOwner()
        {
            var state = Form("TR-21", "nika", -1);
            CharacterCombatRuntime.ValidateSourceSidecars(state);
            state.Forms[0].ExpiresAt = double.PositiveInfinity;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
            state.Forms[0].ExpiresAt = -1;
            state.Actors[0].SourceManagedResources = false;
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceSidecars(state));
        }

        private static SourceCombatRuntimeCheckpoint Volley(bool vivi, int startHit)
        {
            var state = new SourceCombatRuntimeCheckpoint {
                Time = vivi ? 1.1 : 1.0,
                HasSourceModelBasicAttacks = true,
                Timed = new CombatTimedEffectQueueState(),
                VolleyOwners = new SourceVolleyWorldCheckpoint { NextCastId = 3 }
            };
            state.Actors.Add(new SourceCombatActorCheckpoint {
                InstanceId = 9, CatalogId = vivi ? "E-06" : "E-12",
                SourceLaneRevision = 2
            });
            int total = vivi ? 20 : 25;
            double start = vivi ? .12 : .23;
            double interval = vivi ? .055 : .05;
            for (int i = startHit; i < total; i++)
            {
                var p = new SourceVolleyPulseCheckpoint {
                    OwnerId = 9, SkillId = vivi ? "E-06:skill:0" :
                        "E-12:skill:0",
                    OriginalPrimaryId = "removed-first-target",
                    LaneRevision = 2, CastId = 3, CastAt = .9, HitIndex = i
                };
                state.Timed.Pending.Add(new CombatTimedEffectState {
                    Sequence = i + 1, CasterId = "9",
                    TargetId = "practice-volley:9:3:" + i,
                    At = .9 + (start + i * interval),
                    Kind = "source-original-volley-v1",
                    Payload = JsonUtility.ToJson(p)
                });
            }
            return state;
        }

        [Test]
        public void ViviResumeMustPreserveTheUnplayedTwentyHitSuffix()
        {
            var state = Volley(true, 2);
            CharacterCombatRuntime.ValidateSourceVolleyOwners(state);
            state.Timed.Pending.RemoveAt(4);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceVolleyOwners(state));
        }

        [Test]
        public void HancockCastCannotChangeOriginalPrimaryBetweenShots()
        {
            var state = Volley(false, 0);
            CharacterCombatRuntime.ValidateSourceVolleyOwners(state);
            var p = JsonUtility.FromJson<SourceVolleyPulseCheckpoint>(
                state.Timed.Pending[2].Payload);
            p.OriginalPrimaryId = "different-first-target";
            state.Timed.Pending[2].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceVolleyOwners(state));
        }

        [Test]
        public void DuplicateHitForeignCastAndLaneRevisionMustFailClosed()
        {
            var state = Volley(true, 2);
            var original = state.Timed.Pending[0];
            state.Timed.Pending.Add(new CombatTimedEffectState {
                Sequence = 100, CasterId = original.CasterId,
                TargetId = original.TargetId, Kind = original.Kind,
                At = original.At, Payload = original.Payload
            });
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceVolleyOwners(state));
            state.Timed.Pending.RemoveAt(state.Timed.Pending.Count - 1);
            var p = JsonUtility.FromJson<SourceVolleyPulseCheckpoint>(
                original.Payload);
            p.CastId = 4;
            state.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceVolleyOwners(state));
            state = Volley(true, 2);
            p = JsonUtility.FromJson<SourceVolleyPulseCheckpoint>(
                state.Timed.Pending[0].Payload);
            p.LaneRevision = 3;
            state.Timed.Pending[0].Payload = JsonUtility.ToJson(p);
            Assert.Throws<FormatException>(() =>
                CharacterCombatRuntime.ValidateSourceVolleyOwners(state));
        }
    }
}
