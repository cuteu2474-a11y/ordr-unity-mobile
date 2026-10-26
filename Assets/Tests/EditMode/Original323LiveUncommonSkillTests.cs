using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323LiveUncommonSkillTests
    {
        private sealed class LowestRoll : ICombatProcRandom
        {
            public int Calls;
            public int Next(int upperExclusive)
            {
                Calls++;
                return 0;
            }
        }

        // All implementations in this fixture are deliberately TEST_ONLY.
        // They do not authorize combat rules in the shipped game.
        private sealed class TestAttack : IOriginalCommonNativeAttackVerification
        {
            private readonly OriginalMapBaseStatCatalog map;
            private readonly string unit;
            public TestAttack(OriginalMapBaseStatCatalog source, string allowed)
            { map = source; unit = allowed; }

            public bool TryVerify(string unitId, string rawcode,
                out double delay, out DamageSchool school, out string evidence)
            {
                var row = map.Find(unitId);
                delay = row == null || row.Attack1 == null ?
                    0 : row.Attack1.AttackPointSeconds.Value;
                school = DamageSchool.Physical;
                bool valid = unitId == unit && row != null &&
                    row.OriginalRawcode == rawcode;
                evidence = valid ? "TEST_ONLY: map native basic attack" : null;
                return valid;
            }
        }

        private sealed class TruncateTestDamage : IOrdr323VerifiedDamageQuantization
        {
            public long Quantize(double original, Ordr323VerifiedDamageHit hit)
            { return (long)Math.Truncate(original); }
        }

        private sealed class LiveFlags : IOriginal323LiveBashTargetWitness
        {
            public string ObjectRawcode = "o00I";
            public string BoundTarget = "target";
            public string GameSource = Original323VerifiedMonsterWaveCatalog.SourceW3xSha256;
            public bool Hero;
            public bool Immune;
            public bool SourceAvailable = true;
            public int Inspections;

            public bool TryObserveCurrent(CombatEnemy liveEnemy,
                out Original323ObservedBashTarget record)
            {
                Inspections++;
                record = SourceAvailable ? new Original323ObservedBashTarget(
                    BoundTarget, ObjectRawcode, GameSource, Hero, Immune,
                    "TEST_ONLY live native hero/immune and instance rawcode") : null;
                return record != null;
            }
        }

        private sealed class Bonus : IOriginal323UncommonBonusDamageRules
        {
            public long FinalHitHp = 47;
            public int Calls;
            public bool TryResolveFinalBonus(OriginalUncommonProc ability,
                string attackerId, CombatEnemy target, out long damage,
                out string evidence)
            {
                Calls++;
                damage = FinalHitHp;
                evidence = "TEST_ONLY verified native AHbh final physical HP";
                return target != null && target.IsAlive;
            }
        }

        private sealed class Rankyaku : IOriginal323RankyakuVerifiedRules
        {
            public bool CanAttempt(OriginalUncommonProc ability,
                string attackerId, CombatEnemy primary, out string proof)
            {
                proof = null;
                return false;
            }

            public bool TryResolveCompleteLine(OriginalUncommonProc ability,
                CharacterCombatRuntime runtime, string attackerId,
                string primaryId, out IReadOnlyList<Original323RankyakuLineHit> hits,
                out string proof)
            {
                hits = null;
                proof = null;
                return false;
            }
        }

        private sealed class Robin : IOriginal323RobinVerifiedVolleyRules
        {
            public bool CanAttempt(OriginalUncommonProc ability, string attackerId,
                CombatEnemy primary, out string proof)
            {
                proof = null;
                return false;
            }

            public bool TryResolveAdditionalHits(OriginalUncommonProc ability,
                CharacterCombatRuntime runtime, string attackerId,
                string primaryId, out IReadOnlyList<Original323RobinSecondaryHit> hits,
                out string proof)
            {
                hits = null;
                proof = null;
                return false;
            }
        }

        private static string Json(string name)
        {
            return File.ReadAllText(Path.Combine(
                Application.dataPath, "..", "data", name));
        }

        private static Original323MonsterObjectAbilityCatalog OriginalObjects()
        {
            return Original323MonsterObjectAbilityCatalog.FromJson(
                Json("original-2323C-monster-object-abilities.json"));
        }

        private static Original323UncommonAbilityCatalog OriginalUncommons()
        {
            var units = CharacterUnitCatalog.FromJson(Json("units.json"));
            return new Original323UncommonAbilityCatalog(
                OriginalMapBaseStatCatalog.FromJson(
                    Json("original-2323C-map-base-stats.json"), units));
        }

        private static CharacterCombatRuntime World(string catalogId,
            LowestRoll rng, IOriginalUncommonVerifiedProcWorld skills)
        {
            var units = CharacterUnitCatalog.FromJson(Json("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Json("original-2323C-map-base-stats.json"), units);
            var native = new OriginalMapCommonNativeAttackBinding(
                map, new TestAttack(map, catalogId), rng);
            var profiles = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var world = Ordr323DamageWorldBinding.Create(
                units, profiles, native, null, new TruncateTestDamage(), rng);
            world.ConfigureOriginalUncommonAbilities(
                new Original323UncommonAbilityCatalog(map), skills);
            world.ConfigureVerifiedUnitAttackProvider(native);
            world.Start();
            world.BeginWave(1);
            world.SpawnCharacter(catalogId, 0, 0);
            world.SpawnEnemyWithDefenses("target", 1000,
                CharacterEnemyKind.Line, 10, 0, 0,
                new EnemyDefenseProfile(CharacterEnemyKind.Line, 0, null,
                    "normal", "TEST_ONLY target physical armor"));
            return world;
        }

        [Test]
        public void LiveObjectWitnessRunsRealBrookStunInCombat()
        {
            var flags = new LiveFlags();
            var targets = new Original323LiveBashTargetRules(
                OriginalObjects(), flags);
            var rng = new LowestRoll();
            var world = World("U-03", rng,
                Original323VerifiedUncommonSkillAssembly.Create(targets));
            world.Step(.28);
            Assert.AreEqual(3, rng.Calls,
                "2 native attack dice + one 10% Brook Bash roll");
            Assert.GreaterOrEqual(flags.Inspections, 2,
                "Native current immunity checked before roll and at impact");
            Assert.IsTrue(world.StatusOf("target").IsStunned());
            Assert.IsFalse(world.TryMoveEnemy("target", 15, 0, 0));
            world.RemoveCharacter(world.MobileUnitSnapshot()[0].InstanceId);
            world.Step(.31);
            Assert.IsFalse(world.StatusOf("target").IsStunned());
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void UnobservedImmunityOrWrongObjectNeverConsumesBashRng(
            bool immune, bool wrongObject)
        {
            var flags = new LiveFlags { Immune = immune };
            if (wrongObject) flags.ObjectRawcode = "xxxx";
            var rng = new LowestRoll();
            var world = World("U-03", rng,
                Original323VerifiedUncommonSkillAssembly.Create(
                    new Original323LiveBashTargetRules(OriginalObjects(), flags)));
            world.Step(.28);
            Assert.AreEqual(2, rng.Calls,
                "Native proof must reject before Bash chance RNG");
            Assert.IsFalse(world.StatusOf("target").IsStunned());
            Assert.IsTrue(world.TryMoveEnemy("target", 15, 0, 0));
        }

        [Test]
        public void HeroFlagComesFromObservedNativeUnitNotMobileLane()
        {
            var flags = new LiveFlags { Hero = true };
            var world = World("U-03", new LowestRoll(),
                Original323VerifiedUncommonSkillAssembly.Create(
                    new Original323LiveBashTargetRules(OriginalObjects(), flags)));
            world.Step(.28);
            Assert.IsTrue(world.StatusOf("target").IsStunned());
            world.Step(.14);
            Assert.IsTrue(world.StatusOf("target").IsStunned());
            world.Step(.02);
            Assert.IsFalse(world.StatusOf("target").IsStunned());
        }

        [Test]
        public void MixedSourceVerifiedSkillSetSupportsSevenNativeAttackEffects()
        {
            var source = OriginalUncommons();
            var targets = new Original323LiveBashTargetRules(
                OriginalObjects(), new LiveFlags());
            var both = Original323VerifiedUncommonSkillAssembly.Create(
                targets, new Bonus(), new Rankyaku(), new Robin());

            foreach (var row in new[] {
                new[] { "U-01", "A07M:multi3" },
                new[] { "U-03", "A07P:bash" },
                new[] { "U-04", "A07I:rankyaku" },
                new[] { "U-05", "A07R:flame" },
                new[] { "U-09", "A07Q:shigure" },
                new[] { "U-12", "A07S:fishman" },
                new[] { "U-13", "A07I:rankyaku" }
            })
                Assert.IsTrue(both.SupportsCompleteEffect(
                    source.FindProc(row[0], row[1])), row[0] + "/" + row[1]);

            // Critical is a separate pre-armor source, not an OnHit proc.
            Assert.IsFalse(both.SupportsCompleteEffect(
                source.FindProc("U-04", "A07I:critical")));
            Assert.IsFalse(both.SupportsCompleteEffect(
                source.FindProc("U-13", "A07I:critical")));
        }

        [Test]
        public void AceBonusDamageAndBrookCanCoexistWithoutAuthorizingRankyaku()
        {
            var targets = new Original323LiveBashTargetRules(
                OriginalObjects(), new LiveFlags());
            var bonus = new Bonus();
            var effects = Original323VerifiedUncommonSkillAssembly.Create(
                targets, bonus);
            var baseline = World("U-05", new LowestRoll(), null);
            baseline.Step(.25);
            long afterOriginalBasic = baseline.EnemyOf("target").Health;
            var world = World("U-05", new LowestRoll(), effects);
            world.Step(.25);
            Assert.AreEqual(afterOriginalBasic - 47,
                world.EnemyOf("target").Health,
                "The TEST_ONLY bonus must be added exactly once to the same native basic.");
            Assert.AreEqual(2, bonus.Calls);
            var source = OriginalUncommons();
            Assert.IsFalse(effects.SupportsCompleteEffect(
                source.FindProc("U-04", "A07I:rankyaku")));
        }

        [TestCase("U-09", 0.7, 0.35)]
        [TestCase("U-12", 0.1, 0.05)]
        public void LiveTargetEvidenceRunsTashigiAndHachiBonusPlusStun(
            string attacker, double normalStun, double heroStun)
        {
            var baseline = World(attacker, new LowestRoll(), null);
            baseline.Step(.25);
            long afterOriginalBasic = baseline.EnemyOf("target").Health;
            foreach (bool hero in new[] { false, true })
            {
                var flags = new LiveFlags { Hero = hero };
                var bonus = new Bonus();
                var world = World(attacker, new LowestRoll(),
                    Original323VerifiedUncommonSkillAssembly.Create(
                        new Original323LiveBashTargetRules(
                            OriginalObjects(), flags), bonus));
                world.Step(.25);
                Assert.AreEqual(afterOriginalBasic - 47,
                    world.EnemyOf("target").Health);
                Assert.IsTrue(world.StatusOf("target").IsStunned());
                world.RemoveCharacter(world.MobileUnitSnapshot()[0].InstanceId);
                double duration = hero ? heroStun : normalStun;
                world.Step(duration - 0.01);
                Assert.IsTrue(world.StatusOf("target").IsStunned());
                world.Step(0.02);
                Assert.IsFalse(world.StatusOf("target").IsStunned());
                Assert.AreEqual(2, bonus.Calls);
                Assert.GreaterOrEqual(flags.Inspections, 2);
            }
        }

        [Test]
        public void ForgedGameVersionOrUnmappedLiveMonsterBlocksBashWithoutRng()
        {
            foreach (bool wrongSourceHash in new[] { false, true })
            {
                var flags = new LiveFlags();
                if (wrongSourceHash)
                    flags.GameSource = new string('0', 64);
                else
                    flags.SourceAvailable = false;
                var rng = new LowestRoll();
                var world = World("U-03", rng,
                    Original323VerifiedUncommonSkillAssembly.Create(
                        new Original323LiveBashTargetRules(
                            OriginalObjects(), flags)));
                world.Step(.28);
                Assert.AreEqual(2, rng.Calls);
                Assert.IsFalse(world.StatusOf("target").IsStunned());
            }
        }

        [Test]
        public void InvalidCurrentWitnessAndUnsupportedModuleCombinationFailClosed()
        {
            var targets = new Original323LiveBashTargetRules(
                OriginalObjects(), new LiveFlags {
                    BoundTarget = "other-monster",
                    SourceAvailable = true
                });
            bool hero, immune;
            string evidence;
            var world = World("U-03", new LowestRoll(),
                Original323VerifiedUncommonSkillAssembly.Create(targets));
            Assert.IsFalse(targets.TryInspect(
                world.EnemyOf("target"), out hero, out immune, out evidence));
            Assert.IsNull(evidence);

            Assert.Throws<ArgumentException>(() =>
                Original323VerifiedUncommonSkillAssembly.Create(
                    null, new Bonus()));
            Assert.Throws<ArgumentException>(() =>
                Original323VerifiedUncommonSkillAssembly.Create(null));
            var onlyRobin = Original323VerifiedUncommonSkillAssembly.Create(
                null, null, null, new Robin());
            Assert.IsFalse(onlyRobin.SupportsCompleteEffect(
                OriginalUncommons().FindProc("U-03", "A07P:bash")));
        }
    }
}
