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
        private sealed class MinimumDieRoll : ICombatProcRandom
        {
            public int Next(int upperExclusive) { return 0; }
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

            public FixtureNativePath(bool approved) { this.approved = approved; }

            public bool TryVerify(string unitId, string rawcode,
                out double firstDelay, out DamageSchool school, out string evidence)
            {
                firstDelay = 0.35; // original h003 attack-point fixture
                school = DamageSchool.Physical;
                evidence = approved && unitId == "C-01" && rawcode == "h003"
                    ? "TEST_ONLY: synthetic native path/quantizer approval" : null;
                return evidence != null;
            }
        }

        private static string ProjectFile(string name)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", name));
        }

        private static CharacterCombatRuntime Create(bool approveNativePath)
        {
            var units = CharacterUnitCatalog.FromJson(ProjectFile("units.json"));
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var map = OriginalMapBaseStatCatalog.FromJson(
                ProjectFile("original-2323C-map-base-stats.json"), units);
            var dice = new MinimumDieRoll();
            var bridge = new OriginalMapCommonNativeAttackBinding(map,
                new FixtureNativePath(approveNativePath), dice);
            var world = Ordr323DamageWorldBinding.Create(units, skills,
                bridge, null, new FixtureQuantizer(), dice);
            world.ConfigureVerifiedUnitAttackProvider(bridge);
            world.ConfigureOriginalMapCommonAttackPreview(map);
            world.Start();
            world.BeginWave(1);
            return world;
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
            Assert.AreEqual(1.0, luffy.AttackTableFactor(Ordr323ArmorType.Normal), 1e-9);
            Assert.AreEqual(Ordr323AttackType.Pierce,
                commons.Find("C-02").NativeAttackType);
            Assert.AreEqual(Ordr323AttackType.Melee,
                commons.Find("C-03").NativeAttackType);
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
    }
}
