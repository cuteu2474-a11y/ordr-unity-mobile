using System;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // 원본 오브젝트는 공격 후보의 근거일 뿐, JASS/네이티브 피해 경로
    // 및 워크래프트 최종 HP 반올림 검증은 별개입니다.
    // 이 테스트의 공격 승인·적 방어·HP 절사는 전부 TEST_ONLY입니다.
    public sealed class CommonUsoppSanjiChopperAcceptanceTests
    {
        private sealed class Case
        {
            public readonly string Id, Name, Raw, AttackName;
            public readonly int Minimum, Maximum, BasePlus, DiceCount, DiceSides;
            public readonly double Cooldown, AttackPoint;
            public readonly Ordr323AttackType AttackType;
            public readonly double NormalArmorFactor;
            public readonly string[] Abilities;

            public Case(string id, string name, string raw, string attackName,
                int minimum, int maximum, int basePlus, int diceCount, int diceSides,
                double cooldown, double attackPoint, Ordr323AttackType attackType,
                double normalArmorFactor, string[] abilities)
            {
                Id = id; Name = name; Raw = raw; AttackName = attackName;
                Minimum = minimum; Maximum = maximum;
                BasePlus = basePlus; DiceCount = diceCount; DiceSides = diceSides;
                Cooldown = cooldown; AttackPoint = attackPoint;
                AttackType = attackType; NormalArmorFactor = normalArmorFactor;
                Abilities = abilities;
            }
        }

        private static readonly Case[] Cases = {
            new Case("C-04", "우솝", "h007", "normal", 25, 27, 24, 1, 3,
                0.84, 0.20, Ordr323AttackType.Melee, 1.25,
                new[] { "A0JE", "A09G", "A00V", "A01H", "Avul" }),
            new Case("C-05", "상디", "h004", "siege", 23, 25, 22, 1, 3,
                0.77, 0.10, Ordr323AttackType.Siege, 1.00,
                new[] { "A0JE", "A09G", "A01B", "A00P", "Avul" }),
            new Case("C-06", "쵸파", "h008", "normal", 22, 24, 21, 1, 3,
                0.57, 0.20, Ordr323AttackType.Melee, 1.25,
                new[] { "A0JE", "A09G", "A02I", "A00E", "Avul" })
        };

        private static Case Find(string id)
        {
            foreach (var entry in Cases)
                if (entry.Id == id) return entry;
            throw new ArgumentException("TEST_ONLY 등록되지 않은 흔함 유닛 " + id);
        }

        private sealed class CountingDice : ICombatProcRandom
        {
            public int Calls;
            public bool Maximum;
            public int Next(int upperExclusive)
            {
                Calls++;
                return Maximum ? upperExclusive - 1 : 0;
            }
        }

        private sealed class TestOnlyNativeApproval : IOriginalCommonNativeAttackVerification
        {
            private readonly Case entry;
            private readonly bool enabled;
            public TestOnlyNativeApproval(Case source, bool allow)
            {
                entry = source;
                enabled = allow;
            }
            public bool TryVerify(string id, string rawcode, out double firstDelay,
                out DamageSchool school, out string evidence)
            {
                firstDelay = entry.AttackPoint;
                school = DamageSchool.Physical;
                bool matched = enabled && id == entry.Id && rawcode == entry.Raw;
                evidence = matched ? "TEST_ONLY: synthetic native path approval" : null;
                return matched;
            }
        }

        private sealed class TestOnlyTruncation : IOrdr323VerifiedDamageQuantization
        {
            public Ordr323VerifiedDamageHit LastHit;
            public long Quantize(double value, Ordr323VerifiedDamageHit hit)
            {
                LastHit = hit;
                return (long)Math.Truncate(value); // NOT verified Warcraft rounding
            }
        }

        private static string Data(string name)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", name));
        }

        private static OriginalMapBaseStatCatalog SourceMap()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            return OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
        }

        private static CharacterCombatRuntime World(Case entry, bool approved,
            CountingDice dice, TestOnlyTruncation quantizer = null)
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var bridge = new OriginalMapCommonNativeAttackBinding(
                map, new TestOnlyNativeApproval(entry, approved), dice);
            var world = Ordr323DamageWorldBinding.Create(units, skills,
                bridge, null, quantizer ?? new TestOnlyTruncation(), dice);
            world.ConfigureVerifiedUnitAttackProvider(bridge);
            world.ConfigureOriginalMapCommonAttackPreview(map);
            world.Start();
            world.BeginWave(1);
            return world;
        }

        private static void KnownEnemy(CharacterCombatRuntime world,
            string name, long hp = 1000,
            CharacterEnemyKind kind = CharacterEnemyKind.Line)
        {
            world.SpawnEnemyWithDefenses(name, hp, kind, 10, 0, 0,
                new EnemyDefenseProfile(kind, 0, null, "normal",
                    "TEST_ONLY: synthetic armor and hit points"));
        }

        private static long ExpectedDamage(Case entry, bool maximum = false)
        {
            // TEST_ONLY: map bonus table + provided truncation, NOT Warcraft HP parity.
            return (long)Math.Truncate(
                (maximum ? entry.Maximum : entry.Minimum) * entry.NormalArmorFactor);
        }

        [TestCase("C-04")]
        [TestCase("C-05")]
        [TestCase("C-06")]
        public void SourceObjectStatsIdentityAndAbilitiesAreExact(string id)
        {
            var entry = Find(id);
            var basic = SourceMap().Find(id);
            Assert.IsNotNull(basic);
            Assert.AreEqual(entry.Raw, basic.OriginalRawcode);
            Assert.AreEqual("verified-common-map-identity", basic.MappingStatus);
            Assert.AreEqual(entry.Minimum, basic.Attack1.MinimumBaseDamage.Value);
            Assert.AreEqual(entry.Maximum, basic.Attack1.MaximumBaseDamage.Value);
            Assert.AreEqual(entry.BasePlus, basic.Attack1.BaseDamagePlus.Value);
            Assert.AreEqual(entry.DiceCount, basic.Attack1.DiceCount.Value);
            Assert.AreEqual(entry.DiceSides, basic.Attack1.DiceSides.Value);
            Assert.AreEqual(entry.Cooldown, basic.Attack1.AttackCooldownSeconds.Value, 1e-9);
            Assert.AreEqual(entry.AttackPoint, basic.Attack1.AttackPointSeconds.Value, 1e-9);
            Assert.AreEqual(420, basic.Attack1.OriginalRange.Value);
            Assert.AreEqual(entry.AttackName, basic.Attack1.OriginalAttackType);
            Assert.AreEqual("missile", basic.Attack1.OriginalWeaponType);
            CollectionAssert.AreEqual(entry.Abilities, basic.OriginalAbilityRawcodes);
            Assert.IsFalse(basic.CombatParityVerified);
        }

        [TestCase("C-04")]
        [TestCase("C-05")]
        [TestCase("C-06")]
        public void ObjectDiceAndAttackArmorTableArePreserved(string id)
        {
            var entry = Find(id);
            var candidate = new OriginalMapCommonNativeAttackEvidenceCatalog(
                SourceMap()).Find(id);
            Assert.IsNotNull(candidate);
            Assert.AreEqual(entry.Raw, candidate.Rawcode);
            Assert.AreEqual(entry.AttackType, candidate.NativeAttackType);
            Assert.AreEqual(entry.NormalArmorFactor,
                candidate.AttackTableFactor(Ordr323ArmorType.Normal), 1e-9);
            var minimum = new CountingDice();
            var maximum = new CountingDice { Maximum = true };
            Assert.AreEqual(entry.Minimum, candidate.RollObjectBaseDamage(minimum));
            Assert.AreEqual(entry.Maximum, candidate.RollObjectBaseDamage(maximum));
            Assert.AreEqual(entry.DiceCount, minimum.Calls);
            Assert.AreEqual(entry.DiceCount, maximum.Calls);
        }

        [TestCase("C-04")]
        [TestCase("C-05")]
        [TestCase("C-06")]
        public void MissingIndependentApprovalNeverDealsDamageOrRollsDice(string id)
        {
            var entry = Find(id);
            var dice = new CountingDice();
            var world = World(entry, false, dice);
            long attacker = world.SpawnCharacter(entry.Id, 0, 0);
            KnownEnemy(world, "enemy");
            Assert.IsFalse(world.HasVerifiedAttack(attacker));
            Assert.AreEqual(BasicAttackReadinessKind.UnverifiedSource,
                world.InspectBasicAttack(attacker).Kind);
            world.Step(3.0);
            Assert.AreEqual(1000L, world.EnemyOf("enemy").Health);
            Assert.AreEqual(0, dice.Calls);
            Assert.AreEqual(0, world.Game.DefeatedEnemies);
            Assert.IsNull(world.ProfileOf(attacker),
                "전투 프로필 없음은 원작에 전투 스킬이 없다는 증거가 아닙니다.");
        }

        [TestCase("C-04")]
        [TestCase("C-05")]
        [TestCase("C-06")]
        public void SyntheticAttackUsesObjectDiceAndNativeArmorType(string id)
        {
            var entry = Find(id);
            var dice = new CountingDice();
            var quantizer = new TestOnlyTruncation();
            var world = World(entry, true, dice, quantizer);
            long attacker = world.SpawnCharacter(entry.Id, 0, 0);
            KnownEnemy(world, "enemy");
            Assert.IsTrue(world.HasVerifiedAttack(attacker));
            world.Step(entry.AttackPoint);
            Assert.AreEqual(1000L - ExpectedDamage(entry),
                world.EnemyOf("enemy").Health);
            Assert.AreEqual(entry.DiceCount, dice.Calls);
            Assert.IsNotNull(quantizer.LastHit);
            Assert.AreEqual(entry.Minimum, quantizer.LastHit.RawDamage);
            Assert.AreEqual(entry.AttackType, quantizer.LastHit.AttackType);
        }

        [TestCase("C-04")]
        [TestCase("C-05")]
        [TestCase("C-06")]
        public void MaximumDiceRollStillRespectsObjectAndArmorTable(string id)
        {
            var entry = Find(id);
            var dice = new CountingDice { Maximum = true };
            var quantizer = new TestOnlyTruncation();
            var world = World(entry, true, dice, quantizer);
            world.SpawnCharacter(entry.Id, 0, 0);
            KnownEnemy(world, "enemy");
            world.Step(entry.AttackPoint);
            Assert.AreEqual(entry.Maximum, quantizer.LastHit.RawDamage);
            Assert.AreEqual(1000L - ExpectedDamage(entry, true),
                world.EnemyOf("enemy").Health);
            Assert.AreEqual(entry.DiceCount, dice.Calls);
        }

        [TestCase("C-04")]
        [TestCase("C-05")]
        [TestCase("C-06")]
        public void SyntheticCooldownAndFirstAttackPointDoNotFireEarly(string id)
        {
            var entry = Find(id);
            var dice = new CountingDice();
            var world = World(entry, true, dice);
            world.SpawnCharacter(entry.Id, 0, 0);
            KnownEnemy(world, "enemy");
            world.Step(entry.AttackPoint - 0.01);
            Assert.AreEqual(1000L, world.EnemyOf("enemy").Health);
            Assert.AreEqual(0, dice.Calls);
            world.Step(0.01);
            Assert.AreEqual(1000L - ExpectedDamage(entry),
                world.EnemyOf("enemy").Health);
            world.Step(entry.Cooldown - 0.02);
            Assert.AreEqual(1000L - ExpectedDamage(entry),
                world.EnemyOf("enemy").Health);
            world.Step(0.03);
            Assert.AreEqual(1000L - 2 * ExpectedDamage(entry),
                world.EnemyOf("enemy").Health);
            Assert.AreEqual(2 * entry.DiceCount, dice.Calls);
        }

        [TestCase("C-04")]
        [TestCase("C-05")]
        [TestCase("C-06")]
        public void UnknownArmorCannotConsumeDiceOrObstructLaterKnownTarget(string id)
        {
            var entry = Find(id);
            var dice = new CountingDice();
            var world = World(entry, true, dice);
            world.SpawnCharacter(entry.Id, 0, 0);
            world.SpawnEnemy("unknown", 1000, CharacterEnemyKind.Line, 1, 0, 0);
            world.Step(3.0);
            Assert.AreEqual(0, dice.Calls);
            KnownEnemy(world, "known");
            world.Step(0);
            Assert.AreEqual(1000L, world.EnemyOf("unknown").Health);
            Assert.AreEqual(1000L - ExpectedDamage(entry),
                world.EnemyOf("known").Health);
            Assert.AreEqual(entry.DiceCount, dice.Calls);
        }

        [TestCase("C-04")]
        [TestCase("C-05")]
        [TestCase("C-06")]
        public void MainFieldUnitCannotHitStoryOrCreepWithoutDispatch(string id)
        {
            var entry = Find(id);
            var dice = new CountingDice();
            var world = World(entry, true, dice);
            world.SpawnCharacter(entry.Id, 0, 0);
            KnownEnemy(world, "story", 1000, CharacterEnemyKind.Story);
            KnownEnemy(world, "creep", 1000, CharacterEnemyKind.Creep);
            world.Step(3.0);
            Assert.AreEqual(0, dice.Calls);
            KnownEnemy(world, "main");
            world.Step(0);
            Assert.AreEqual(1000L - ExpectedDamage(entry),
                world.EnemyOf("main").Health);
            Assert.AreEqual(1000L, world.EnemyOf("story").Health);
            Assert.AreEqual(1000L, world.EnemyOf("creep").Health);
        }

        [TestCase("C-04")]
        [TestCase("C-05")]
        [TestCase("C-06")]
        public void SyntheticEnemyDeathAndCountersAreCommittedOnce(string id)
        {
            var entry = Find(id);
            var world = World(entry, true, new CountingDice());
            world.SpawnCharacter(entry.Id, 0, 0);
            KnownEnemy(world, "enemy", ExpectedDamage(entry));
            var output = world.Step(entry.AttackPoint);
            int attacks = 0, deaths = 0;
            foreach (var evt in output)
            {
                if (evt.Type == CharacterCombatEventType.Attack) attacks++;
                if (evt.Type == CharacterCombatEventType.EnemyDefeated) deaths++;
            }
            Assert.AreEqual(1, attacks);
            Assert.AreEqual(1, deaths);
            Assert.IsNull(world.EnemyOf("enemy"));
            Assert.AreEqual(1, world.Game.DefeatedEnemies);
            world.Step(2.0);
            Assert.AreEqual(1, world.Game.DefeatedEnemies);
        }

        [TestCase("C-04")]
        [TestCase("C-05")]
        [TestCase("C-06")]
        public void RemovedCharacterCannotDealGhostDamage(string id)
        {
            var entry = Find(id);
            var dice = new CountingDice();
            var world = World(entry, true, dice);
            long unit = world.SpawnCharacter(entry.Id, 0, 0);
            KnownEnemy(world, "enemy");
            world.Step(entry.AttackPoint / 2.0);
            world.RemoveCharacter(unit);
            world.Step(3.0);
            Assert.AreEqual(1000L, world.EnemyOf("enemy").Health);
            Assert.AreEqual(0, dice.Calls);
        }
    }
}
