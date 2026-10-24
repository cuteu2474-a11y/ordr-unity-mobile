using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // TEST_ONLY complete-effect port. This is intentionally not a production
    // implementation of Warcraft's attack, native Bash or 2.323[C] JASS.
    public sealed class Original323UncommonSkillIntegrationTests
    {
        private sealed class CountingRandom : ICombatProcRandom
        {
            public int Calls;
            public int Next(int upperExclusive)
            {
                Calls++;
                return 0;
            }
        }

        private sealed class SyntheticNative : IOriginalCommonNativeAttackVerification
        {
            private readonly OriginalMapBaseStatCatalog map;
            private readonly string enabled;
            public SyntheticNative(OriginalMapBaseStatCatalog source, string id)
            {
                map = source; enabled = id;
            }
            public bool TryVerify(string id, string rawcode, out double delay,
                out DamageSchool school, out string evidence)
            {
                var entry = map.Find(id);
                delay = entry != null && entry.Attack1 != null
                    ? entry.Attack1.AttackPointSeconds.Value : 0;
                school = DamageSchool.Physical;
                bool allowed = id == enabled && entry != null &&
                    entry.OriginalRawcode == rawcode;
                evidence = allowed ? "TEST_ONLY synthetic native proof" : null;
                return allowed;
            }
        }

        private sealed class TestOnlyQuantizer : IOrdr323VerifiedDamageQuantization
        {
            public long Quantize(double value, Ordr323VerifiedDamageHit hit)
            {
                return (long)Math.Truncate(value);
            }
        }

        private sealed class TestOnlyEffects : IOriginalUncommonVerifiedProcWorld
        {
            private readonly HashSet<string> allowed =
                new HashSet<string>(StringComparer.Ordinal);
            public bool DenyTarget;
            public int PreflightCalls;
            public int Executed;
            public readonly List<string> Fired = new List<string>();
            public TestOnlyEffects(params string[] procIds)
            {
                foreach (var id in procIds) allowed.Add(id);
            }
            public bool SupportsCompleteEffect(OriginalUncommonProc proc)
            {
                return allowed.Contains(proc.ProcId);
            }
            public bool CanAffectTarget(OriginalUncommonProc proc, string attackerId,
                CombatEnemy target)
            {
                PreflightCalls++;
                return !DenyTarget && target != null && target.IsAlive &&
                    target.Defenses.HasVerifiedArmor;
            }
            public bool TryApplyCompleteEffect(OriginalUncommonProc proc,
                CharacterCombatRuntime world, string attackerId,
                string targetId, out string proof)
            {
                Executed++;
                Fired.Add(proc.ProcId);
                proof = "TEST_ONLY: effect-world applied full verified fixture";
                // This fixture implements its ONE completely identified native
                // status, not physical/magical bonus damage approximations.
                if (proc.ProcId == "A07P:bash")
                {
                    var target = world.StatusOf(targetId);
                    if (target == null) return false;
                    target.Apply(new CombatEffect("TEST_ONLY:A07P", CombatEffectType.Stun,
                        CombatEffectOrigin.TriggeredSkill, 1, proc.NormalStunSeconds));
                }
                return true;
            }
        }

        private sealed class SourceCheckedBashTargets :
            IOriginal323UncommonBashTargetRules
        {
            public bool Hero;
            public bool Immune;
            public bool HasEvidence = true;
            public int Calls;
            public bool TryInspect(CombatEnemy target, out bool hero,
                out bool immune, out string originalEvidence)
            {
                Calls++;
                hero = Hero;
                immune = Immune;
                originalEvidence = HasEvidence
                    ? "TEST_ONLY: verified rawcode + native hero/immunity flags"
                    : null;
                return HasEvidence;
            }
        }

        private static string Data(string name)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, "..",
                "data", name));
        }

        private static OriginalMapBaseStatCatalog Map()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            return OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
        }

        private static CharacterCombatRuntime World(string id, CountingRandom rng,
            IOriginalUncommonVerifiedProcWorld effects)
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var native = new OriginalMapCommonNativeAttackBinding(map,
                new SyntheticNative(map, id), rng);
            var world = Ordr323DamageWorldBinding.Create(units, skills, native,
                null, new TestOnlyQuantizer(), rng);
            world.ConfigureOriginalUncommonAbilities(
                new Original323UncommonAbilityCatalog(map), effects);
            world.ConfigureVerifiedUnitAttackProvider(native);
            world.Start();
            world.BeginWave(1);
            return world;
        }

        private static void Enemy(CharacterCombatRuntime world)
        {
            world.SpawnEnemyWithDefenses("target", 1000, CharacterEnemyKind.Line,
                10, 0, 0, new EnemyDefenseProfile(CharacterEnemyKind.Line,
                    0, null, "normal", "TEST_ONLY target armor/type"));
        }

        [Test]
        public void FullOriginalMapCatalogHasFourteenUncommonsAndNineDistinctNativeProcs()
        {
            var map = Map();
            var cat = new Original323UncommonAbilityCatalog(map);
            Assert.AreEqual(14, cat.UnitCount);
            Assert.AreEqual(9, cat.NativeProcCount);
            Assert.AreEqual(0, cat.ForUnit("U-14").Count);
            Assert.AreEqual(0, cat.ForUnit("U-02").Count);
            Assert.AreEqual(2, cat.ForUnit("U-04").Count);
            Assert.AreEqual(2, cat.ForUnit("U-13").Count);
            Assert.AreEqual("A029", cat.MapAbilitiesFor("U-14")[0]);
            Assert.AreEqual("Avul", cat.MapAbilitiesFor("U-14")[1]);
            Assert.AreEqual(3, cat.ForUnit("U-01")[0].NativeTargetCount);
            var brook = cat.FindProc("U-03", "A07P:bash");
            Assert.AreEqual(100000, brook.ChancePerMillion);
            Assert.AreEqual(.3, brook.NormalStunSeconds, 1e-9);
            Assert.AreEqual(.15, brook.HeroStunSeconds, 1e-9);
            var blueno = cat.FindProc("U-04", "A07I:critical");
            Assert.AreEqual(150000, blueno.ChancePerMillion);
            Assert.AreEqual(1.5, blueno.CriticalMultiplier, 1e-9);
            Assert.AreEqual(150, cat.FindProc("U-04", "A07I:rankyaku").BonusDamage);
            Assert.AreEqual(200, cat.ForUnit("U-05")[0].BonusDamage);
            Assert.AreEqual(.7, cat.ForUnit("U-09")[0].NormalStunSeconds, 1e-9);
            Assert.AreEqual(.1, cat.ForUnit("U-12")[0].NormalStunSeconds, 1e-9);
        }

        [Test]
        public void WithoutVerifiedNativeEffectsBrookMayAttackButNeverRollAFalseSkill()
        {
            var rng = new CountingRandom();
            var world = World("U-03", rng, null);
            long id = world.SpawnCharacter("U-03", 0, 0);
            Enemy(world);
            Assert.IsTrue(world.HasVerifiedAttack(id));
            Assert.AreEqual(1, world.OriginalUncommonSkillsOf(id).Count);
            Assert.IsFalse(world.IsOriginalUncommonSkillConnected(id, "A07P:bash"));
            world.Step(.28);
            Assert.AreEqual(2, rng.Calls, "Only the map's 2d4 basic hit rolls.");
            Assert.IsFalse(world.StatusOf("target").IsStunned());
            Assert.AreEqual(860L, world.EnemyOf("target").Health);
        }

        [Test]
        public void VerifiedSyntheticBashAutomaticallyRollsAndAppliesStunAfterHit()
        {
            var rng = new CountingRandom();
            var effects = new TestOnlyEffects("A07P:bash");
            var world = World("U-03", rng, effects);
            long brook = world.SpawnCharacter("U-03", 0, 0);
            Enemy(world);
            Assert.IsTrue(world.IsOriginalUncommonSkillConnected(brook, "A07P:bash"));
            world.Step(.27);
            Assert.AreEqual(0, rng.Calls);
            var events = world.Step(.01);
            Assert.AreEqual(3, rng.Calls, "two 2d4 rolls + one chance roll");
            Assert.AreEqual(1, effects.Executed);
            Assert.IsTrue(world.StatusOf("target").IsStunned());
            bool skillActivated = false;
            foreach (var evt in events)
                if (evt.Type == CharacterCombatEventType.SkillActivated &&
                    evt.SkillName == "잠의 노래 프람: 10% 기절") skillActivated = true;
            Assert.IsTrue(skillActivated);
            world.Step(.30);
            Assert.IsFalse(world.StatusOf("target").IsStunned(),
                "Native status expires through the common combat clock.");
        }

        [Test]
        public void TargetPreflightRejectionSkipsProcRngButKeepsVerifiedBasicHit()
        {
            var rng = new CountingRandom();
            var effects = new TestOnlyEffects("A07P:bash") { DenyTarget = true };
            var world = World("U-03", rng, effects);
            world.SpawnCharacter("U-03", 0, 0);
            Enemy(world);
            world.Step(.28);
            Assert.AreEqual(2, rng.Calls);
            Assert.Greater(effects.PreflightCalls, 0);
            Assert.AreEqual(0, effects.Executed);
            Assert.IsFalse(world.StatusOf("target").IsStunned());
            Assert.AreEqual(860L, world.EnemyOf("target").Health);
        }

        [Test]
        public void TwoIndependentBluenoProcsRouteToCompleteEffectPortsOnly()
        {
            var rng = new CountingRandom();
            var effects = new TestOnlyEffects("A07I:critical", "A07I:rankyaku");
            var world = World("U-04", rng, effects);
            long blueno = world.SpawnCharacter("U-04", 0, 0);
            Enemy(world);
            Assert.AreEqual(2, world.OriginalUncommonSkillsOf(blueno).Count);
            Assert.IsTrue(world.IsOriginalUncommonSkillConnected(blueno, "A07I:critical"));
            Assert.IsTrue(world.IsOriginalUncommonSkillConnected(blueno, "A07I:rankyaku"));
            world.Step(.2);
            Assert.AreEqual(4, rng.Calls, "2d4 object dice + two independent chance rolls");
            CollectionAssert.AreEquivalent(
                new[] { "A07I:critical", "A07I:rankyaku" }, effects.Fired);
        }

        [Test]
        public void ZombieCraftAbilityNeverBecomesAnAttackProc()
        {
            var rng = new CountingRandom();
            var effects = new TestOnlyEffects("A029");
            var world = World("U-14", rng, effects);
            long zombie = world.SpawnCharacter("U-14", 0, 0);
            Enemy(world);
            Assert.IsTrue(world.HasVerifiedAttack(zombie));
            Assert.IsEmpty(world.OriginalUncommonSkillsOf(zombie));
            world.Step(.25);
            Assert.AreEqual(2, rng.Calls, "A029 is CWR crafting, not a combat proc.");
            Assert.AreEqual(0, effects.Executed);
        }

        [Test]
        public void OriginalBashNormalTargetActuallyStopsMovementForPointThreeSeconds()
        {
            var rng = new CountingRandom();
            var targets = new SourceCheckedBashTargets();
            var bash = new Original323UncommonBashWorld(targets);
            var world = World("U-03", rng, bash);
            long unit = world.SpawnCharacter("U-03", 0, 0);
            Enemy(world);
            Assert.IsTrue(world.IsOriginalUncommonSkillConnected(unit, "A07P:bash"));
            var effects = world.Step(.28);
            Assert.AreEqual(3, rng.Calls, "2 original damage dice + 1 native Bash proc.");
            Assert.GreaterOrEqual(targets.Calls, 2,
                "Target proof is checked both before RNG and before applying stun.");
            Assert.IsTrue(world.StatusOf("target").IsStunned());
            Assert.IsFalse(world.TryMoveEnemy("target", 11, 0, 0));
            world.Step(.29);
            Assert.IsTrue(world.StatusOf("target").IsStunned());
            world.Step(.02);
            Assert.IsFalse(world.StatusOf("target").IsStunned());
            Assert.IsTrue(world.TryMoveEnemy("target", 11, 0, 0));
        }

        [Test]
        public void OriginalBashUsesWarcraftHeroFlagNotMobileEnemyKind()
        {
            var targets = new SourceCheckedBashTargets { Hero = true };
            var world = World("U-03", new CountingRandom(),
                new Original323UncommonBashWorld(targets));
            world.SpawnCharacter("U-03", 0, 0);
            Enemy(world); // Even a mobile Line target can be proven Warcraft hero.
            world.Step(.28);
            Assert.IsTrue(world.StatusOf("target").IsStunned());
            world.Step(.14);
            Assert.IsTrue(world.StatusOf("target").IsStunned());
            world.Step(.02);
            Assert.IsFalse(world.StatusOf("target").IsStunned());
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void NativeBashImmunityOrMissingProofPreventsRngAndStun(
            bool immune, bool noProof)
        {
            var rng = new CountingRandom();
            var targets = new SourceCheckedBashTargets {
                Immune = immune, HasEvidence = !noProof
            };
            var world = World("U-03", rng,
                new Original323UncommonBashWorld(targets));
            long unit = world.SpawnCharacter("U-03", 0, 0);
            Enemy(world);
            Assert.IsTrue(world.IsOriginalUncommonSkillConnected(unit, "A07P:bash"));
            world.Step(.28);
            Assert.AreEqual(2, rng.Calls, "Failed eligibility must not roll bash chance.");
            Assert.IsFalse(world.StatusOf("target").IsStunned());
            Assert.IsTrue(world.TryMoveEnemy("target", 11, 0, 0));
        }

        [Test]
        public void BrookOnlyBashAdapterCannotApproveOtherUncommonBonusDamage()
        {
            var targetRules = new SourceCheckedBashTargets();
            var world = new Original323UncommonBashWorld(targetRules);
            var catalog = new Original323UncommonAbilityCatalog(Map());
            foreach (string otherId in new[] { "U-01", "U-04", "U-05",
                "U-09", "U-12", "U-13" })
                foreach (var proc in catalog.ForUnit(otherId))
                    Assert.IsFalse(world.SupportsCompleteEffect(proc),
                        "Unverified physical, critical and line-AoE components remain blocked.");
            Assert.IsTrue(world.SupportsCompleteEffect(
                catalog.FindProc("U-03", "A07P:bash")));
        }

        [Test]
        public void CannotReplaceSourceAbilityCatalogAfterSpawning()
        {
            var world = World("U-03", new CountingRandom(), null);
            world.SpawnCharacter("U-03", 0, 0);
            Assert.Throws<InvalidOperationException>(() =>
                world.ConfigureOriginalUncommonAbilities(
                    new Original323UncommonAbilityCatalog(Map())));
        }
    }
}
