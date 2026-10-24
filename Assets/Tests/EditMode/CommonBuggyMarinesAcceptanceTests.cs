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
    public sealed class CommonBuggyMarinesAcceptanceTests
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
            new Case("C-07", "버기", "h005", "pierce", 29, 31, 28, 1, 3,
                0.99, 0.10, Ordr323AttackType.Pierce, 0.75,
                new[] { "A0JE", "A09G", "A00L", "A018", "Avul" }),
            new Case("C-08", "총병", "h009", "siege", 30, 32, 29, 1, 3,
                0.96, 0.17, Ordr323AttackType.Siege, 1.00,
                new[] { "A0JE", "A09G", "A0N5", "A01L", "A19Z", "Avul" }),
            new Case("C-09", "칼병", "h006", "pierce", 28, 30, 27, 1, 3,
                0.94, 0.17, Ordr323AttackType.Pierce, 0.75,
                new[] { "A0JE", "A09G", "A0N7", "A01R", "Avul" })
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


        [Serializable]
        private sealed class OriginalAbilityAudit
        {
            public OriginalAuditedCommon[] commonUnits;
        }

        [Serializable]
        private sealed class OriginalAuditedCommon
        {
            public string catalogUnitId;
            public OriginalAuditedAbility[] abilityCodesInObjectOrder;
        }

        [Serializable]
        private sealed class OriginalAuditedAbility
        {
            public string rawcode;
            public string kind;
            public string jassResultRawcode;
            public int jassUnitIngredientCount;
            public bool nativeCombatSkillVerified;
        }

        // 이 스킬들은 전투 스킬이 아니라 원본 JASS CWR 조합 진입점.
        // 실제 조합 소비/지급을 검증했다는 뜻은 아니다.
        [TestCase("C-07", "A00L", "h00J", 2)]
        [TestCase("C-07", "A018", "h015", 3)]
        [TestCase("C-08", "A0N5", "h00M", 2)]
        [TestCase("C-08", "A01L", "h00F", 2)]
        [TestCase("C-08", "A19Z", "h01F", 3)]
        [TestCase("C-09", "A0N7", "h00A", 2)]
        [TestCase("C-09", "A01R", "h00E", 2)]
        public void OriginalCwrAbilitiesHaveExpectedResultIdentityAndAreNotCombatSkills(
            string id, string rawcode, string resultRawcode, int ingredients)
        {
            var audit = JsonUtility.FromJson<OriginalAbilityAudit>(
                Data("original-2323C-common-unit-ability-audit.json"));
            Assert.IsNotNull(audit);
            Assert.IsNotNull(audit.commonUnits);
            OriginalAuditedCommon unit = null;
            foreach (var row in audit.commonUnits)
                if (row.catalogUnitId == id) { unit = row; break; }
            Assert.IsNotNull(unit, id + " 원본 능력 감사 누락");
            Assert.IsNotNull(unit.abilityCodesInObjectOrder);
            OriginalAuditedAbility ability = null;
            foreach (var row in unit.abilityCodesInObjectOrder)
                if (row.rawcode == rawcode) { ability = row; break; }
            Assert.IsNotNull(ability, id + "/" + rawcode + " 능력 누락");
            Assert.AreEqual("original-cwr-fusion-ability", ability.kind);
            Assert.AreEqual(resultRawcode, ability.jassResultRawcode);
            Assert.AreEqual(ingredients, ability.jassUnitIngredientCount);
            Assert.IsFalse(ability.nativeCombatSkillVerified);
        }

        [Test]
        public void CommonNativeAttackCandidateCatalogContainsAllNineCommonUnits()
        {
            var map = SourceMap();
            var evidence = new OriginalMapCommonNativeAttackEvidenceCatalog(map);
            Assert.AreEqual(9, evidence.Count);
            for (int number = 1; number <= 9; number++)
            {
                string id = "C-" + number.ToString("D2");
                var entry = map.Find(id);
                var candidate = evidence.Find(id);
                Assert.IsNotNull(entry, id);
                Assert.IsNotNull(candidate, id);
                Assert.AreEqual(entry.OriginalRawcode, candidate.Rawcode);
                Assert.IsFalse(entry.CombatParityVerified);
            }
        }

        [TestCase("C-07")]
        [TestCase("C-08")]
        [TestCase("C-09")]
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

        [TestCase("C-07")]
        [TestCase("C-08")]
        [TestCase("C-09")]
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

        [TestCase("C-07")]
        [TestCase("C-08")]
        [TestCase("C-09")]
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

        [TestCase("C-07")]
        [TestCase("C-08")]
        [TestCase("C-09")]
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

        [TestCase("C-07")]
        [TestCase("C-08")]
        [TestCase("C-09")]
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

        [TestCase("C-07")]
        [TestCase("C-08")]
        [TestCase("C-09")]
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

        [TestCase("C-07")]
        [TestCase("C-08")]
        [TestCase("C-09")]
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

        [TestCase("C-07")]
        [TestCase("C-08")]
        [TestCase("C-09")]
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

        [TestCase("C-07")]
        [TestCase("C-08")]
        [TestCase("C-09")]
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

        [TestCase("C-07")]
        [TestCase("C-08")]
        [TestCase("C-09")]
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
