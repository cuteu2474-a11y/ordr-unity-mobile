using System;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // End-to-end fixtures with a deliberately synthetic proof of the native
    // attack path and synthetic HP quantizer. These tests NEVER attest that
    // the production 2.323[C] JASS, defense or rounding is fully reproduced.
    public sealed class OriginalMapCommonNativeAttackBindingTests
    {
        private sealed class MaximumDieRoll : ICombatProcRandom
        {
            public int Next(int upperExclusive) { return upperExclusive - 1; }
        }

        private sealed class MinimumDieRoll : ICombatProcRandom
        {
            public int Next(int upperExclusive) { return 0; }
        }

        private sealed class MixedQuantizer : IOrdr323VerifiedDamageQuantization
        {
            public long Quantize(double value, Ordr323VerifiedDamageHit hit)
            {
                if (hit.Evidence.Contains("UnitWeapons.slk:h003"))
                    throw new NotSupportedException("TEST_ONLY: 루피 피해 정수화 미확정");
                return (long)Math.Truncate(value);
            }
        }

        private sealed class BothCommonsVerified : IOriginalCommonNativeAttackVerification
        {
            public bool TryVerify(string id, string raw, out double delay,
                out DamageSchool school, out string proof)
            {
                delay = 0.35; school = DamageSchool.Physical;
                bool allowed = (id == "C-01" && raw == "h003") ||
                    (id == "C-02" && raw == "h002");
                proof = allowed ? "TEST_ONLY: synthetic native approval" : null;
                return allowed;
            }
        }

        private sealed class FixtureQuantizer : IOrdr323VerifiedDamageQuantization
        {
            public long Quantize(double damage, Ordr323VerifiedDamageHit hit)
            {
                return (long)Math.Truncate(damage);
            }
        }

        private sealed class FixtureNativePath : IOriginalCommonNativeAttackVerification
        {
            private readonly bool approved;
            private readonly string approvedUnitId;

            public FixtureNativePath(bool approved, string approvedUnitId)
            {
                this.approved = approved;
                this.approvedUnitId = approvedUnitId;
            }

            public bool TryVerify(string unitId, string rawcode,
                out double firstDelay, out DamageSchool school, out string evidence)
            {
                firstDelay = unitId == "TR-08" ? 0.38 : 0.35;
                school = DamageSchool.Physical;
                evidence = approved && unitId == approvedUnitId &&
                    ((unitId == "C-01" && rawcode == "h003") ||
                     (unitId == "TR-08" && rawcode == "H099"))
                    ? "TEST_ONLY: synthetic native path/quantizer approval" : null;
                return evidence != null;
            }
        }

        private static string ProjectFile(string name)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", name));
        }

        private static CharacterCombatRuntime Create(bool approveNativePath,
            string approvedUnitId = "C-01", bool startWave = true)
        {
            var units = CharacterUnitCatalog.FromJson(ProjectFile("units.json"));
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var map = OriginalMapBaseStatCatalog.FromJson(
                ProjectFile("original-2323C-map-base-stats.json"), units);
            var dice = new MinimumDieRoll();
            var bridge = new OriginalMapCommonNativeAttackBinding(map,
                new FixtureNativePath(approveNativePath, approvedUnitId), dice);
            var world = Ordr323DamageWorldBinding.Create(units, skills,
                bridge, null, new FixtureQuantizer(), dice);
            world.ConfigureVerifiedUnitAttackProvider(bridge);
            world.ConfigureOriginalMapCommonAttackPreview(map);
            world.Start();
            if (startWave) world.BeginWave(1);
            return world;
        }

        [Test]
        public void VerifiedWaveSpawnArmorFlowsIntoActualHitInSyntheticNativeFixture()
        {
            // TEST-ONLY: independent path, armor and quantization are synthetic.
            var world = Create(true, "C-01", false);
            world.SpawnCharacter("C-01", 0, 0);
            var defense = new EnemyDefenseProfile(CharacterEnemyKind.Line,
                10, null, "normal", "TEST_ONLY: independently verified armor");
            var wave = new VerifiedWavePlan(1, new[] {
                new VerifiedWaveEnemy(0, "first", 1000,
                    CharacterEnemyKind.Line, 20, 0, 0, defense, "n001")
            }, "TEST_ONLY: schedule");
            var director = new CombatWaveDirector();
            director.Begin(world, wave);
            director.Step(world, 0.34);
            Assert.AreEqual(1000L, world.EnemyOf("first").Health);
            director.Step(world, 0.01);
            Assert.AreEqual(976L, world.EnemyOf("first").Health);
            Assert.AreEqual(1, world.Game.AliveEnemies);
        }

        [Test]
        public void CommonObjectStatsContainNineMapDiceAndNativeTypes()
        {
            var units = CharacterUnitCatalog.FromJson(ProjectFile("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                ProjectFile("original-2323C-map-base-stats.json"), units);
            var commons = new OriginalMapCommonNativeAttackEvidenceCatalog(map);
            Assert.AreEqual(9, commons.Count);
            var luffy = commons.Find("C-01");
            Assert.AreEqual("h003", luffy.Rawcode);
            Assert.AreEqual(28, luffy.BaseDamagePlus);
            Assert.AreEqual(1, luffy.DiceCount);
            Assert.AreEqual(3, luffy.DiceSides);
            Assert.AreEqual(1.02, luffy.CooldownSeconds, 1e-9);
            Assert.AreEqual(Ordr323AttackType.Siege, luffy.NativeAttackType);
            Assert.AreEqual(29L, luffy.RollObjectBaseDamage(new MinimumDieRoll()));
            Assert.AreEqual(31L, luffy.RollObjectBaseDamage(new MaximumDieRoll()));
            Assert.AreEqual(1.0, luffy.AttackTableFactor(Ordr323ArmorType.Normal), 1e-9);
            Assert.AreEqual(Ordr323AttackType.Pierce,
                commons.Find("C-02").NativeAttackType);
            Assert.AreEqual(Ordr323AttackType.Melee,
                commons.Find("C-03").NativeAttackType);
            var mapped = new OriginalMapMappedNativeAttackEvidenceCatalog(map);
            Assert.AreEqual(269, mapped.Count);
            var ryuma = mapped.Find("E-03");
            Assert.AreEqual("h0CJ", ryuma.Rawcode);
            Assert.AreEqual(140001L, ryuma.RollObjectBaseDamage(new MinimumDieRoll()));
            Assert.AreEqual(0.71, ryuma.CooldownSeconds, 1e-9);
            // Reviewed names are original W3X objects, not new guessed stats.
            var robin = mapped.Find("U-01");
            Assert.AreEqual("h00O", robin.Rawcode);
            Assert.AreEqual(62L, robin.RollObjectBaseDamage(new MinimumDieRoll()));
            Assert.AreEqual(1.2, robin.CooldownSeconds, 1e-9);
            var transcendent = mapped.Find("TR-08");
            Assert.AreEqual("H099", transcendent.Rawcode);
            Assert.AreEqual(30001L,
                transcendent.RollObjectBaseDamage(new MinimumDieRoll()));
            Assert.AreEqual(0.38, transcendent.CooldownSeconds, 1e-9);
            Assert.AreEqual(Ordr323AttackType.Siege, transcendent.NativeAttackType);
        }

        [Test]
        public void NewlyMappedTranscendentCanDamageHPOnlyWithIndependentFixtureProof()
        {
            var world = Create(true, "TR-08");
            long luffy = world.SpawnCharacter("TR-08", 0, 0);
            world.SpawnEnemyWithDefenses("target", 100000,
                CharacterEnemyKind.Line, 20, 0, 0,
                new EnemyDefenseProfile(CharacterEnemyKind.Line,
                    0, null, "normal", "TEST_ONLY: known enemy armor/type"));
            Assert.IsTrue(world.HasVerifiedAttack(luffy));
            Assert.AreEqual(0, world.PreviewAttackers);
            world.Step(0.37);
            Assert.AreEqual(100000L, world.EnemyOf("target").Health);
            world.Step(0.01);
            Assert.AreEqual(69999L, world.EnemyOf("target").Health);
            world.Step(0.38);
            Assert.AreEqual(39998L, world.EnemyOf("target").Health);
            Assert.IsTrue(world.AttackEvidenceOf(luffy).Contains("TEST_ONLY"));
        }

        [Test]
        public void UnapprovedTranscendentOnlyEmitsPreviewWithoutChangingHP()
        {
            var world = Create(false);
            long luffy = world.SpawnCharacter("TR-08", 0, 0);
            world.SpawnEnemy("target", 100000, CharacterEnemyKind.Line, 20, 0, 0);
            Assert.IsFalse(world.HasVerifiedAttack(luffy));
            Assert.AreEqual(1, world.PreviewAttackers);
            world.Step(0.38);
            Assert.AreEqual(100000L, world.EnemyOf("target").Health);
        }

        [Test]
        public void ApprovedNativeFixtureActuallyReducesHPOnOriginalTiming()
        {
            var world = Create(true);
            var luffy = world.SpawnCharacter("C-01", 0, 0);
            world.SpawnEnemyWithDefenses("normal", 1000,
                CharacterEnemyKind.Line, 20, 0, 0,
                new EnemyDefenseProfile(CharacterEnemyKind.Line,
                    10, null, "normal", "TEST_ONLY: enemy armor/type"));
            Assert.IsTrue(world.HasVerifiedAttack(luffy));
            Assert.AreEqual(0, world.PreviewAttackers);
            world.Step(0.34);
            Assert.AreEqual(1000L, world.EnemyOf("normal").Health);
            world.Step(0.01);
            // 28+1d3 = 29 (minimum roll), siege-v-normal = 1.0,
            // Armor coefficient is 0.02, fixture truncates 29/1.2 to 24.
            Assert.AreEqual(976L, world.EnemyOf("normal").Health);
            world.Step(1.02);
            Assert.AreEqual(952L, world.EnemyOf("normal").Health);
        }

        [Test]
        public void UnapprovedNativePathIsOnlyAnAttackCueAndDoesNotChangeHP()
        {
            var world = Create(false);
            var id = world.SpawnCharacter("C-01", 0, 0);
            world.SpawnEnemy("target", 1000, CharacterEnemyKind.Line, 20, 0, 0);
            Assert.IsFalse(world.HasVerifiedAttack(id));
            Assert.AreEqual(1, world.PreviewAttackers);
            world.Step(0.35);
            Assert.AreEqual(1000L, world.EnemyOf("target").Health);
        }

        [Test]
        public void UnverifiedEnemyDefenseCannotBecomeSyntheticZeroArmorDamage()
        {
            var world = Create(true);
            world.SpawnCharacter("C-01", 0, 0);
            world.SpawnEnemy("unknown-defense", 1000,
                CharacterEnemyKind.Line, 20, 0, 0);
            world.Step(0.35);
            Assert.AreEqual(1000L, world.EnemyOf("unknown-defense").Health);
        }

        [Test]
        public void NativeAttackSkipsUnverifiedClosestMonsterButHitsVerifiedDefender()
        {
            var world = Create(true);
            long attacker = world.SpawnCharacter("C-01", 0, 0);
            world.SpawnEnemy("closer-unverified", 1000,
                CharacterEnemyKind.Line, 1, 0, 0);
            world.SpawnEnemyWithDefenses("farther-verified", 1000,
                CharacterEnemyKind.Line, 20, 0, 0,
                new EnemyDefenseProfile(CharacterEnemyKind.Line,
                    10, null, "normal", "TEST_ONLY: known original armor"));

            world.Step(0.35);
            Assert.AreEqual(1000L, world.EnemyOf("closer-unverified").Health);
            Assert.AreEqual(976L, world.EnemyOf("farther-verified").Health);
            var enemySnapshot = world.MobileEnemySnapshot();
            foreach (var snapshot in enemySnapshot)
                if (snapshot.Id == "farther-verified")
                {
                    Assert.AreEqual(976L, snapshot.Health);
                    Assert.AreEqual(1000L, snapshot.MaxHealth);
                }
            Assert.IsTrue(world.HasVerifiedAttack(attacker));
            Assert.AreEqual(1, world.ReadyAttackers);
            world.Step(1.02);
            Assert.AreEqual(952L, world.EnemyOf("farther-verified").Health);
        }

        [Test]
        public void UnknownOnlyTargetDoesNotDisableVerifiedAttackWhenNewDefenderSpawns()
        {
            var world = Create(true);
            world.SpawnCharacter("C-01", 0, 0);
            world.SpawnEnemy("unknown", 1000,
                CharacterEnemyKind.Line, 1, 0, 0);
            world.Step(3.0);
            Assert.AreEqual(1000L, world.EnemyOf("unknown").Health);
            Assert.AreEqual(1, world.ReadyAttackers);

            world.SpawnEnemyWithDefenses("verified", 1000,
                CharacterEnemyKind.Line, 10, 0, 0,
                new EnemyDefenseProfile(CharacterEnemyKind.Line,
                    0, null, "normal", "TEST_ONLY: verified armor"));
            world.Step(0);
            Assert.AreEqual(971L, world.EnemyOf("verified").Health);
            Assert.AreEqual(1000L, world.EnemyOf("unknown").Health);
        }

        [Test]
        public void AttackReadinessSeparatesUnapprovedSourceAndUnverifiedDefender()
        {
            var blocked = Create(false);
            long unknown = blocked.SpawnCharacter("C-01", 0, 0);
            blocked.SpawnEnemy("enemy", 1000, CharacterEnemyKind.Line, 0, 0, 0);
            var missing = blocked.InspectBasicAttack(unknown);
            Assert.AreEqual(BasicAttackReadinessKind.UnverifiedSource, missing.Kind);
            StringAssert.Contains("독립 검증", missing.Detail);
            blocked.Step(0.35);
            Assert.AreEqual(1000L, blocked.EnemyOf("enemy").Health);

            var world = Create(true);
            long luffy = world.SpawnCharacter("C-01", 0, 0);
            world.SpawnEnemy("unknown", 1000, CharacterEnemyKind.Line, 0, 0, 0);
            Assert.AreEqual(BasicAttackReadinessKind.UnverifiedTarget,
                world.InspectBasicAttack(luffy).Kind);
            StringAssert.Contains("방어력", world.InspectBasicAttack(luffy).Detail);
            world.Step(1.0);
            Assert.AreEqual(1, world.ReadyAttackers);
            world.SpawnEnemyWithDefenses("verified", 1000, CharacterEnemyKind.Line,
                5, 0, 0, new EnemyDefenseProfile(CharacterEnemyKind.Line,
                    0, null, "normal", "TEST_ONLY: known armor"));
            Assert.AreEqual(BasicAttackReadinessKind.Ready,
                world.InspectBasicAttack(luffy).Kind);
            world.Step(0);
            Assert.AreEqual(971L, world.EnemyOf("verified").Health);
            Assert.AreEqual(1000L, world.EnemyOf("unknown").Health);
        }

        [Test]
        public void DeferredLuffyFormulaDoesNotBlockVerifiedZoro()
        {
            var units = CharacterUnitCatalog.FromJson(ProjectFile("units.json"));
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var map = OriginalMapBaseStatCatalog.FromJson(
                ProjectFile("original-2323C-map-base-stats.json"), units);
            var dice = new MinimumDieRoll();
            var bridge = new OriginalMapCommonNativeAttackBinding(map,
                new BothCommonsVerified(), dice);
            var world = Ordr323DamageWorldBinding.Create(units, skills,
                bridge, null, new MixedQuantizer(), dice);
            world.ConfigureVerifiedUnitAttackProvider(bridge);
            world.Start(); world.BeginWave(1);
            long luffy = world.SpawnCharacter("C-01", 0, 0);
            long zoro = world.SpawnCharacter("C-02", 0, 0);
            world.SpawnEnemyWithDefenses("target", 1000, CharacterEnemyKind.Line,
                3, 0, 0, new EnemyDefenseProfile(CharacterEnemyKind.Line,
                    0, null, "normal", "TEST_ONLY: known armor"));
            world.Step(0.35);
            var report = world.CaptureBasicAttackDiagnostics();
            Assert.AreEqual(2, report.Count);
            Assert.AreEqual(BasicAttackReadinessKind.FormulaDeferred, report[0].Kind);
            StringAssert.Contains("정수화", report[0].Detail);
            Assert.AreEqual(BasicAttackReadinessKind.Cooldown, report[1].Kind);
            Assert.AreEqual(1, world.ReadyAttackers);
            Assert.Less(world.EnemyOf("target").Health, 1000L);
            Assert.AreEqual(luffy, report[0].InstanceId);
            Assert.AreEqual(zoro, report[1].InstanceId);
        }

        [Test]
        public void PreflightRejectsUnrecognizedArmorTypeWithoutRollingDice()
        {
            var units = CharacterUnitCatalog.FromJson(ProjectFile("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                ProjectFile("original-2323C-map-base-stats.json"), units);
            var source = new OriginalMapCommonNativeAttackBinding(map,
                new FixtureNativePath(true, "C-01"), new MinimumDieRoll());
            var enemy = new CombatEnemy("enemy", 1000,
                new EnemyDefenseProfile(CharacterEnemyKind.Line,
                    0, null, "not-a-warcraft-armor-type", "TEST_ONLY"));
            Assert.IsFalse(source.CanAttemptBasicAttack(units.Require("C-01"), enemy));
        }
    }
}
