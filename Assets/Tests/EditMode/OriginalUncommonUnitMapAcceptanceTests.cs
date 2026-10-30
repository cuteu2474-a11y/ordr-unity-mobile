using System;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // S2 2.323[C] W3X object baselines, not permission to run native attacks
    // without independent Warcraft JASS/path, target and HP rounding proof.
    public sealed class OriginalUncommonUnitMapAcceptanceTests
    {
        private sealed class MinimumDie : ICombatProcRandom
        {
            public int Calls;
            public int Next(int upperExclusive) { Calls++; return 0; }
        }

        private sealed class TestOnlyNativePath : IOriginalCommonNativeAttackVerification
        {
            private readonly OriginalMapBaseStatCatalog map;
            private readonly string enabledUnitId;
            public TestOnlyNativePath(OriginalMapBaseStatCatalog map, string enabled)
            {
                this.map = map;
                enabledUnitId = enabled;
            }
            public bool TryVerify(string unitId, string raw, out double firstDelay,
                out DamageSchool school, out string evidence)
            {
                var entry = map.Find(unitId);
                firstDelay = entry == null || entry.Attack1 == null
                    ? 0 : entry.Attack1.AttackPointSeconds.GetValueOrDefault();
                school = DamageSchool.Physical;
                bool approved = unitId == enabledUnitId &&
                    entry != null && entry.OriginalRawcode == raw;
                evidence = approved ? "TEST_ONLY: synthetic uncommon approval" : null;
                return approved;
            }
        }

        private sealed class TestOnlyQuantization : IOrdr323VerifiedDamageQuantization
        {
            public Ordr323VerifiedDamageHit LastHit;
            public long Quantize(double value, Ordr323VerifiedDamageHit hit)
            {
                LastHit = hit;
                return (long)Math.Truncate(value); // Never proof of Warcraft HP conversion.
            }
        }

        private static string Data(string name)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", name));
        }

        private static CharacterUnitCatalog ReadUnits()
        {
            return CharacterUnitCatalog.FromJson(Data("units.json"));
        }

        private static OriginalMapBaseStatCatalog ReadOriginalMap(CharacterUnitCatalog units)
        {
            return OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
        }

        private static CharacterCombatRuntime World(string allowedId, MinimumDie dice,
            TestOnlyQuantization quantizer)
        {
            var units = ReadUnits();
            var original = ReadOriginalMap(units);
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var binding = new OriginalMapCommonNativeAttackBinding(original,
                new TestOnlyNativePath(original, allowedId), dice);
            var world = Ordr323DamageWorldBinding.Create(units, skills,
                binding, null, quantizer, dice);
            world.ConfigureVerifiedUnitAttackProvider(binding);
            world.ConfigureOriginalMapCommonAttackPreview(original);
            world.Start();
            world.BeginWave(1);
            return world;
        }

        private static void Enemy(CharacterCombatRuntime world)
        {
            world.SpawnEnemyWithDefenses("enemy", 1000, CharacterEnemyKind.Line,
                10, 0, 0,
                new EnemyDefenseProfile(CharacterEnemyKind.Line, 0, null,
                    "normal", "TEST_ONLY: synthetic target"));
        }

        [TestCase("U-01", "로빈", "h00O", 62, 66, 60, 2, 3, 1.2, 0.25, 600, "normal", "missile")]
        [TestCase("U-02", "베포", "h00C", 172, 178, 170, 2, 4, 1.07, 0.35, 500, "siege", "missile")]
        [TestCase("U-03", "브룩", "h00D", 112, 118, 110, 2, 4, 0.74, 0.28, 500, "normal", "missile")]
        [TestCase("U-04", "블루노", "h00M", 177, 183, 175, 2, 4, 1.32, 0.2, 600, "normal", "missile")]
        [TestCase("U-05", "에이스", "h00K", 127, 133, 125, 2, 4, 1.1, 0.25, 800, "normal", "missile")]
        [TestCase("U-06", "이나즈마", "h00I", 122, 128, 120, 2, 4, 0.85, 0.25, 500, "pierce", "normal")]
        [TestCase("U-07", "저격왕", "h00N", 222, 228, 220, 2, 4, 1.4, 0.25, 1320, "pierce", "missile")]
        [TestCase("U-08", "쵸파 럼블볼", "h00G", 172, 178, 170, 2, 4, 1, 0.25, 600, "siege", "missile")]
        [TestCase("U-09", "타시기", "h00E", 132, 138, 130, 2, 4, 0.96, 0.25, 500, "pierce", "missile")]
        [TestCase("U-10", "페로나", "h00J", 122, 128, 120, 2, 4, 0.9, 0.25, 1000, "normal", "missile")]
        [TestCase("U-11", "프랑키", "h00L", 142, 148, 140, 2, 4, 0.89, 0.25, 600, "siege", "missile")]
        [TestCase("U-12", "하찌", "h00F", 207, 213, 205, 2, 4, 1.6, 0.25, 550, "pierce", "missile")]
        [TestCase("U-13", "후쿠로", "h00A", 157, 163, 155, 2, 4, 1.3, 0.17, 600, "siege", "missile")]
        [TestCase("U-14", "좀비", "h00H", 122, 128, 120, 2, 4, 0.61, 0.25, 1050, "pierce", "normal")]
        public void AllFourteenUncommonsHavePinnedOriginalObjectIdentityAndAttack(
            string id, string name, string raw,
            int minimum, int maximum, int basePlus, int dice, int sides,
            double cooldown, double point, double range,
            string attackType, string weaponType)
        {
            var units = ReadUnits();
            var map = ReadOriginalMap(units);
            var catalog = units.Require(id);
            var entry = map.Find(id);
            Assert.AreEqual("안흔함", catalog.Grade);
            Assert.AreEqual(name, catalog.Name);
            Assert.IsNotNull(entry);
            Assert.AreEqual(raw, entry.OriginalRawcode);
            Assert.AreEqual(minimum, entry.Attack1.MinimumBaseDamage.Value);
            Assert.AreEqual(maximum, entry.Attack1.MaximumBaseDamage.Value);
            Assert.AreEqual(basePlus, entry.Attack1.BaseDamagePlus.Value);
            Assert.AreEqual(dice, entry.Attack1.DiceCount.Value);
            Assert.AreEqual(sides, entry.Attack1.DiceSides.Value);
            Assert.AreEqual(cooldown, entry.Attack1.AttackCooldownSeconds.Value, 1e-9);
            Assert.AreEqual(point, entry.Attack1.AttackPointSeconds.Value, 1e-9);
            Assert.AreEqual(range, entry.Attack1.OriginalRange.Value, 1e-9);
            Assert.AreEqual(attackType, entry.Attack1.OriginalAttackType);
            Assert.AreEqual(weaponType, entry.Attack1.OriginalWeaponType);
            Assert.IsFalse(entry.CombatParityVerified);
        }

        [Test]
        public void RuntimeRosterContainsZombieWithoutChangingTheOtherThirteen()
        {
            var units = ReadUnits();
            Assert.AreEqual(277, units.Count);
            var emptySkills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var registry = UnitImplementationRegistry.Combine(units, emptySkills, null);
            Assert.AreEqual(277, registry.Count);
            Assert.AreEqual(14, registry.OfGrade("안흔함").Count);
            var zombie = registry.Require("U-14");
            Assert.AreEqual("좀비", zombie.DisplayName);
            Assert.AreEqual("안흔함", zombie.Grade);
            Assert.IsFalse(zombie.HasOriginalObjectStats);
            Assert.IsFalse(zombie.HasCollectedDetailedSkills);
            for (int i = 1; i <= 13; i++)
                Assert.AreEqual("U-" + i.ToString("D2"),
                    registry.OfGrade("안흔함")[i - 1].UnitId);
            Assert.AreEqual("U-14", registry.OfGrade("안흔함")[13].UnitId);
        }

        [Test]
        public void ZombieHasSourceAbilityIdentifiersButNoInventedActiveCombatSkill()
        {
            var units = ReadUnits();
            var map = ReadOriginalMap(units);
            var zombie = map.Find("U-14");
            Assert.AreEqual("h00H", zombie.OriginalRawcode);
            Assert.AreEqual("좀비 - 안흔함", zombie.MapName);
            CollectionAssert.AreEqual(new[] { "A029", "Avul" },
                zombie.OriginalAbilityRawcodes);
            Assert.AreEqual("normal", zombie.Attack1.OriginalWeaponType);
            Assert.AreEqual(2, zombie.Attack1.DiceCount.Value);
            Assert.AreEqual(4, zombie.Attack1.DiceSides.Value);
            Assert.AreEqual(1050, zombie.Attack1.OriginalRange.Value);
            var mapped = new OriginalMapMappedNativeAttackEvidenceCatalog(map);
            Assert.AreEqual(273, mapped.Count);
            Assert.AreEqual("h00H", mapped.Find("U-14").Rawcode);
            Assert.AreEqual(Ordr323AttackType.Pierce,
                mapped.Find("U-14").NativeAttackType);
        }

        [TestCase("U-01")]
        [TestCase("U-02")]
        [TestCase("U-03")]
        [TestCase("U-04")]
        [TestCase("U-05")]
        [TestCase("U-06")]
        [TestCase("U-07")]
        [TestCase("U-08")]
        [TestCase("U-09")]
        [TestCase("U-10")]
        [TestCase("U-11")]
        [TestCase("U-12")]
        [TestCase("U-13")]
        [TestCase("U-14")]
        public void WithoutVerifiedWarcraftAttackPathUncommonCannotDamageHp(string id)
        {
            var dice = new MinimumDie();
            var quantizer = new TestOnlyQuantization();
            var world = World(null, dice, quantizer);
            long attacker = world.SpawnCharacter(id, 0, 0);
            Enemy(world);
            Assert.IsFalse(world.HasVerifiedAttack(attacker));
            world.Step(3.0);
            Assert.AreEqual(1000L, world.EnemyOf("enemy").Health);
            Assert.AreEqual(0, dice.Calls);
            Assert.IsNull(quantizer.LastHit);
        }

        [TestCase("U-01")]
        [TestCase("U-02")]
        [TestCase("U-03")]
        [TestCase("U-04")]
        [TestCase("U-05")]
        [TestCase("U-06")]
        [TestCase("U-07")]
        [TestCase("U-08")]
        [TestCase("U-09")]
        [TestCase("U-10")]
        [TestCase("U-11")]
        [TestCase("U-12")]
        [TestCase("U-13")]
        [TestCase("U-14")]
        public void SyntheticVerifiedUnitHasItsActualObjectDiceAndArmorFactor(string id)
        {
            var dice = new MinimumDie();
            var quantizer = new TestOnlyQuantization();
            var world = World(id, dice, quantizer);
            long attacker = world.SpawnCharacter(id, 0, 0);
            Enemy(world);
            Assert.IsTrue(world.HasVerifiedAttack(attacker));
            var map = ReadOriginalMap(ReadUnits()).Find(id).Attack1;
            world.Step(map.AttackPointSeconds.Value);
            Assert.IsNotNull(quantizer.LastHit);
            Assert.AreEqual(map.MinimumBaseDamage.Value, quantizer.LastHit.RawDamage);
            Assert.AreEqual(map.DiceCount.Value, dice.Calls);
            double factor = Ordr323MapDamageMath.AttackVersusArmor(
                quantizer.LastHit.AttackType, Ordr323ArmorType.Normal);
            long damage = (long)Math.Truncate(map.MinimumBaseDamage.Value * factor);
            Assert.AreEqual(1000L - damage, world.EnemyOf("enemy").Health);
        }

        [Test]
        public void ZombieUsesTwoD4PierceAndCannotHitWithoutKnownEnemyArmor()
        {
            var dice = new MinimumDie();
            var quantizer = new TestOnlyQuantization();
            var world = World("U-14", dice, quantizer);
            world.SpawnCharacter("U-14", 0, 0);
            world.SpawnEnemy("unknown", 1000, CharacterEnemyKind.Line, 1, 0, 0);
            world.Step(0.25);
            Assert.AreEqual(0, dice.Calls);
            Assert.AreEqual(1000L, world.EnemyOf("unknown").Health);
            Enemy(world);
            world.Step(0);
            Assert.AreEqual(2, dice.Calls);
            Assert.AreEqual(Ordr323AttackType.Pierce, quantizer.LastHit.AttackType);
            Assert.AreEqual(122, quantizer.LastHit.RawDamage);
            Assert.AreEqual(909L, world.EnemyOf("enemy").Health); // TEST_ONLY 122*0.75 -> 91
            Assert.AreEqual(1000L, world.EnemyOf("unknown").Health);
        }

        [Serializable]
        private sealed class FusionRegistry
        {
            public FusionRegistration[] registrations;
        }
        [Serializable]
        private sealed class FusionRegistration
        {
            public string abilityRawcode;
            public FusionCondition[] conditions;
            public FusionCondition[] results;
        }
        [Serializable]
        private sealed class FusionCondition
        {
            public string kind;
            public string rawcode;
            public int count;
        }

        [Test]
        public void ZombieA029UsesThreeZombiesAndNamiPlusUPUNNotAnAttackProc()
        {
            var registry = JsonUtility.FromJson<FusionRegistry>(
                Data("original-jass-fusion-registry.json"));
            Assert.IsNotNull(registry);
            FusionRegistration recipe = null;
            foreach (var reg in registry.registrations)
                if (reg.abilityRawcode == "A029") { recipe = reg; break; }
            Assert.IsNotNull(recipe);
            Assert.AreEqual(3, recipe.conditions.Length);
            Assert.AreEqual("UNIT", recipe.conditions[0].kind);
            Assert.AreEqual("h00H", recipe.conditions[0].rawcode);
            Assert.AreEqual(3, recipe.conditions[0].count);
            Assert.AreEqual("UNIT", recipe.conditions[1].kind);
            Assert.AreEqual("h001", recipe.conditions[1].rawcode);
            Assert.AreEqual(1, recipe.conditions[1].count);
            Assert.AreEqual("UPUN", recipe.conditions[2].kind);
            Assert.AreEqual("h05K", recipe.conditions[2].rawcode);
            Assert.AreEqual("UNIT", recipe.results[0].kind);
            Assert.AreEqual("h010", recipe.results[0].rawcode);
            Assert.AreEqual(1, recipe.results[0].count);
        }
    }
}
