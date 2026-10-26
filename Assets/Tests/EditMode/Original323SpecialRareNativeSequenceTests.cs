using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323SpecialRareNativeSequenceTests
    {
        // Every observation and final HP number in this test fixture is
        // TEST_ONLY. No fake timing or damage is authorized in production.
        private sealed class NativeSource :
            IOriginal323SpecialRareNativeSequenceWitness,
            IOriginal323SpecialRareImpactWitness
        {
            public Original323ObservedSpecialRareNativeCast Cast;
            public Original323ObservedSpecialRareNativeSequenceImpact Next;
            public Original323SpecialRareObservedImpact RuntimeImpact;
            public bool IdentityApproved = true;
            public bool CastApproved = true;
            public int ObservedCasts;
            public int ObservedSequenceImpacts;
            public int ObservedRuntimeImpacts;

            public bool TryObserveNativeCast(string castId,
                CharacterCombatRuntime live,
                out Original323ObservedSpecialRareNativeCast sourceCast)
            {
                ObservedCasts++;
                sourceCast = Cast;
                return CastApproved && Cast != null;
            }

            public bool TryObserveNextNativeImpact(
                Original323ObservedSpecialRareNativeCast sourceCast,
                int nextOrdinal, CharacterCombatRuntime live,
                out Original323ObservedSpecialRareNativeSequenceImpact observed)
            {
                ObservedSequenceImpacts++;
                observed = Next;
                return observed != null;
            }

            public bool TryObserveNativeImpact(string sourceEventId,
                CharacterCombatRuntime live,
                out Original323SpecialRareObservedImpact impact)
            {
                ObservedRuntimeImpacts++;
                impact = RuntimeImpact;
                return impact != null;
            }

            public bool TryVerifyCurrentCasterIdentity(long id,
                string originalRawcode, out string sourceIdentity)
            {
                sourceIdentity = IdentityApproved ?
                    "TEST_ONLY active original Warcraft caster identity" : null;
                return IdentityApproved;
            }
        }

        private sealed class Fixture
        {
            public CharacterCombatRuntime World;
            public Original323SpecialRareNativeSequenceWorld Sequences;
            public NativeSource Source;
            public OriginalMapBaseStatCatalog Map;
            public long CasterId;
            public string UnitId;
            public string UnitRawcode;
            public string Ability;
            public string CastId = "native-source-cast-1";
        }

        private static string Json(string name)
        {
            return File.ReadAllText(Path.Combine(
                Application.dataPath, "..", "data", name));
        }

        private static Fixture Make(string id, string raw, string ability,
            int observedNativeImpactCount)
        {
            var units = CharacterUnitCatalog.FromJson(Json("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Json("original-2323C-map-base-stats.json"), units);
            var source = new NativeSource();
            var reject = new RejectUnverifiedDamageFormula();
            var combat = new CharacterCombatRuntime(
                units, CharacterSkillCatalog.FromJson(
                    "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}"),
                reject, reject, new SeededNonOriginalProcRandom(21));
            combat.ConfigureOriginal323SpecialRareSkillImpacts(map, source);
            combat.Start();
            combat.BeginWave(1);
            long instanceId = combat.SpawnCharacter(id, 0, 0);
            combat.SpawnEnemy("line", 1000,
                CharacterEnemyKind.Line, 20, 0, 0);
            combat.SpawnEnemy("other", 1000,
                CharacterEnemyKind.Line, 25, 0, 0);
            combat.SpawnEnemy("story", 1000,
                CharacterEnemyKind.Story, 20, 0, 0);
            var fixture = new Fixture {
                World = combat, Source = source, Map = map,
                UnitId = id, UnitRawcode = raw, Ability = ability,
                CasterId = instanceId,
                Sequences = new Original323SpecialRareNativeSequenceWorld(
                    combat, map, source, source)
            };
            source.Cast = MakeNativeCast(fixture, observedNativeImpactCount);
            return fixture;
        }

        private static Original323ObservedSpecialRareNativeCast MakeNativeCast(
            Fixture fixture, int count, string abilityOverride = null)
        {
            return new Original323ObservedSpecialRareNativeCast(
                fixture.CastId, fixture.CasterId, fixture.UnitId,
                fixture.UnitRawcode, abilityOverride ?? fixture.Ability,
                fixture.Map.SourceW3xSha256, fixture.World.Time, count,
                "TEST_ONLY actually observed Warcraft source cast and count");
        }

        private static Original323SpecialRareObservedHit Damage(
            string target, long hp, double? stun = null)
        {
            return new Original323SpecialRareObservedHit(target, hp,
                "TEST_ONLY source observed final HP loss", stun,
                stun.HasValue ? "TEST_ONLY current stun immunity and native duration" : null);
        }

        private static void Report(Fixture f, int ordinal, string eventId,
            params Original323SpecialRareObservedHit[] damage)
        {
            var impact = new Original323SpecialRareObservedImpact(
                eventId, f.CasterId, f.UnitId, f.UnitRawcode, f.Ability,
                "TEST_ONLY native ability name", f.Map.SourceW3xSha256,
                f.World.Time, "TEST_ONLY original native target/order/time",
                damage);
            f.Source.Next = new Original323ObservedSpecialRareNativeSequenceImpact(
                f.CastId, ordinal, impact,
                "TEST_ONLY actual native projectile or periodic impact");
            f.Source.RuntimeImpact = impact;
        }

        [Test]
        public void SpecialEnelDelayedThreeImpactsApplyAtSourceTimesNotCastTime()
        {
            // W3X h00Q/A084 ownership is sourced; THREE impacts here are
            // synthetic receipts, not a claim about Enel's actual skill.
            var f = Make("S-18", "h00Q", "A084", 3);
            Assert.IsTrue(f.Sequences.TryRegisterCast(f.CastId));
            Assert.AreEqual(1, f.Sequences.PendingCastCount);
            Assert.AreEqual(1000, f.World.EnemyOf("line").Health);
            Assert.AreEqual(1000, f.World.EnemyOf("other").Health);

            f.World.Step(0.15);
            Report(f, 1, "enel-native-hit-1", Damage("line", 13));
            Assert.IsTrue(f.Sequences.TryResolveNextImpact(f.CastId));
            Assert.AreEqual(987, f.World.EnemyOf("line").Health);
            Assert.AreEqual(1000, f.World.EnemyOf("other").Health);

            f.World.Step(0.1);
            Report(f, 2, "enel-native-hit-2", Damage("line", 17),
                Damage("other", 31));
            Assert.IsTrue(f.Sequences.TryResolveNextImpact(f.CastId));
            Assert.AreEqual(970, f.World.EnemyOf("line").Health);
            Assert.AreEqual(969, f.World.EnemyOf("other").Health);

            f.World.Step(0.1);
            Report(f, 3, "enel-native-hit-3", Damage("line", 23));
            Assert.IsTrue(f.Sequences.TryResolveNextImpact(f.CastId));
            Assert.AreEqual(947, f.World.EnemyOf("line").Health);
            Assert.AreEqual(0, f.Sequences.PendingCastCount);
            Assert.IsFalse(f.Sequences.TryResolveNextImpact(f.CastId));
            Assert.IsFalse(f.Sequences.TryRegisterCast(f.CastId));
            Assert.AreEqual(3, f.Source.ObservedRuntimeImpacts);
        }

        [Test]
        public void RareDoflamingoSequenceKeepsVerifiedStunAcrossLaterDamage()
        {
            var f = Make("R-04", "h01L", "A057", 2);
            Assert.IsTrue(f.Sequences.TryRegisterCast(f.CastId));
            Report(f, 1, "rare-contact-1", Damage("line", 0, 0.5));
            Assert.IsTrue(f.Sequences.TryResolveNextImpact(f.CastId));
            Assert.IsTrue(f.World.StatusOf("line").IsStunned());
            Assert.AreEqual(1000, f.World.EnemyOf("line").Health);
            Assert.IsFalse(f.World.TryMoveEnemy("line", 30, 0, 0));

            f.World.Step(0.2);
            Report(f, 2, "rare-contact-2", Damage("line", 37));
            Assert.IsTrue(f.Sequences.TryResolveNextImpact(f.CastId));
            Assert.AreEqual(963, f.World.EnemyOf("line").Health);
            f.World.RemoveCharacter(f.CasterId);
            f.World.Step(0.31);
            Assert.IsFalse(f.World.StatusOf("line").IsStunned());
            Assert.AreEqual(0, f.Sequences.PendingCastCount);
        }

        [Test]
        public void WrongImpactOrderDoesNotConsumeAnyHealthOrPendingSlot()
        {
            var f = Make("S-18", "h00Q", "A084", 2);
            Assert.IsTrue(f.Sequences.TryRegisterCast(f.CastId));
            Report(f, 2, "out-of-order", Damage("line", 99));
            Assert.IsFalse(f.Sequences.TryResolveNextImpact(f.CastId));
            Assert.AreEqual(1000, f.World.EnemyOf("line").Health);
            Assert.AreEqual(1, f.Sequences.PendingCastCount);
            Report(f, 1, "actual-first", Damage("line", 10));
            Assert.IsTrue(f.Sequences.TryResolveNextImpact(f.CastId));
            Assert.AreEqual(990, f.World.EnemyOf("line").Health);
            Report(f, 2, "actual-second", Damage("line", 14));
            Assert.IsTrue(f.Sequences.TryResolveNextImpact(f.CastId));
            Assert.AreEqual(976, f.World.EnemyOf("line").Health);
        }

        [Test]
        public void ForgedCastOwnerAbilityOrBadLiveIdentityCannotRegister()
        {
            var f = Make("R-04", "h01L", "A057", 1);
            f.Source.Cast = MakeNativeCast(f, 1, "ZZZZ");
            Assert.IsFalse(f.Sequences.TryRegisterCast(f.CastId));
            f.Source.Cast = MakeNativeCast(f, 1);
            f.Source.IdentityApproved = false;
            Assert.IsFalse(f.Sequences.TryRegisterCast(f.CastId));
            Assert.AreEqual(0, f.Sequences.PendingCastCount);
            f.Source.IdentityApproved = true;
            Assert.IsTrue(f.Sequences.TryRegisterCast(f.CastId));
        }

        [Test]
        public void NativeDummyAbilityIsOwnedByDifferentCombatBridge()
        {
            var f = Make("S-18", "h00Q", "A084", 1);
            f.Source.Cast = MakeNativeCast(f, 1, "A086");
            Assert.IsFalse(f.Sequences.TryRegisterCast(f.CastId));
            Assert.AreEqual(0, f.Sequences.PendingCastCount);
        }

        [Test]
        public void SequenceCannotSubstituteAnotherNativeReceiptAtCommit()
        {
            var f = Make("R-04", "h01L", "A057", 1);
            Assert.IsTrue(f.Sequences.TryRegisterCast(f.CastId));
            Report(f, 1, "verified-target", Damage("line", 40));
            f.Source.RuntimeImpact = new Original323SpecialRareObservedImpact(
                "verified-target", f.CasterId, f.UnitId, f.UnitRawcode,
                f.Ability, "TEST_ONLY native effect", f.Map.SourceW3xSha256,
                f.World.Time, "TEST_ONLY second unapproved receipt",
                new[] { Damage("other", 200) });
            Assert.IsFalse(f.Sequences.TryResolveNextImpact(f.CastId));
            Assert.AreEqual(1000, f.World.EnemyOf("line").Health);
            Assert.AreEqual(1000, f.World.EnemyOf("other").Health);
            Assert.AreEqual(1, f.Sequences.PendingCastCount);
            f.Source.RuntimeImpact = f.Source.Next.Impact;
            Assert.IsTrue(f.Sequences.TryResolveNextImpact(f.CastId));
            Assert.AreEqual(960, f.World.EnemyOf("line").Health);
            Assert.AreEqual(1000, f.World.EnemyOf("other").Health);
        }

        [Test]
        public void WholeCrossLaneBatchRejectsBeforeAnyNativeDamage()
        {
            var f = Make("S-18", "h00Q", "A084", 1);
            Assert.IsTrue(f.Sequences.TryRegisterCast(f.CastId));
            Report(f, 1, "not-a-real-collision",
                Damage("line", 15), Damage("story", 20));
            Assert.IsFalse(f.Sequences.TryResolveNextImpact(f.CastId));
            Assert.AreEqual(1000, f.World.EnemyOf("line").Health);
            Assert.AreEqual(1000, f.World.EnemyOf("story").Health);
            Report(f, 1, "actual-source-collision", Damage("line", 15));
            Assert.IsTrue(f.Sequences.TryResolveNextImpact(f.CastId));
            Assert.AreEqual(985, f.World.EnemyOf("line").Health);
        }

        [Test]
        public void RemovedCasterCannotInventAdditionalNativeContacts()
        {
            var f = Make("S-18", "h00Q", "A084", 2);
            Assert.IsTrue(f.Sequences.TryRegisterCast(f.CastId));
            Report(f, 1, "cast-1", Damage("line", 10));
            Assert.IsTrue(f.Sequences.TryResolveNextImpact(f.CastId));
            f.World.RemoveCharacter(f.CasterId);
            f.World.Step(0.15);
            Report(f, 2, "cast-2-unconfirmed", Damage("line", 50));
            Assert.IsFalse(f.Sequences.TryResolveNextImpact(f.CastId));
            Assert.AreEqual(990, f.World.EnemyOf("line").Health);
            Assert.AreEqual(1, f.Sequences.PendingCastCount);
        }

        [Test]
        public void SingleNativeReceiptReplayCannotAdvanceTwoSourceOrdinals()
        {
            var f = Make("S-18", "h00Q", "A084", 2);
            Assert.IsTrue(f.Sequences.TryRegisterCast(f.CastId));
            Report(f, 1, "only-impact", Damage("line", 12));
            Assert.IsTrue(f.Sequences.TryResolveNextImpact(f.CastId));
            Report(f, 2, "only-impact", Damage("line", 12));
            Assert.IsFalse(f.Sequences.TryResolveNextImpact(f.CastId));
            Assert.AreEqual(988, f.World.EnemyOf("line").Health);
        }
    }
}
