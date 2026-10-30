using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class CharacterActiveSkillLaneTests
    {
        private static CharacterCombatRuntime World(bool withVerifiedCooldown = false,
            string components = null, bool area = false)
        {
            var units = CharacterUnitCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[" +
                "{\"id\":\"TEST-01\",\"grade\":\"흔함\",\"name\":\"테스트\",\"roleTags\":[]}]}");
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[" +
                "{\"unitId\":\"TEST-01\",\"sourceUrl\":\"TEST_FIXTURE\"," +
                "\"skillSpecCompleteness\":\"partial\",\"basicAttack\":{}," +
                "\"passives\":[],\"skills\":[{" +
                "\"name\":\"테스트 스턴\",\"trigger\":\"activeMana\"," +
                "\"manaCost\":10," +
                (withVerifiedCooldown ? "\"cooldownSeconds\":2," : "") +
                (area ? "\"target\":\"targetCircle\",\"radius\":20," : "\"target\":\"single\",") +
                "\"components\":[" + (components ?? "{\"type\":\"stun\",\"durationSeconds\":1}") + "]}]}]}");
            var missing = new RejectUnverifiedDamageFormula();
            var world = new CharacterCombatRuntime(units, skills, missing, missing,
                new SeededNonOriginalProcRandom(1));
            world.Start();
            world.BeginWave(1);
            return world;
        }

        [Test]
        public void AutomaticUnlimitedSkillCastsAtDistantSameLaneEnemyOnly()
        {
            var world = World(true);
            long caster = world.SpawnCharacter("TEST-01", 0, 0);
            world.ConfigureCharacterResources(caster,
                new CharacterResources(100, 100, 100, 50));
            world.SpawnEnemy("story", 1000, CharacterEnemyKind.Story, 10, 0, 0);
            world.SpawnEnemy("distant", 1000, CharacterEnemyKind.Line, 100000, 0, 0);
            world.MobileAutomation.Configure(null, null,
                new MobileUnlimitedAutoSkillTargetRules());
            world.Step(0);
            Assert.AreEqual(40, world.ResourcesOf(caster).Mana);
            Assert.AreEqual(1, world.EnemyOf("distant").Status.ActiveCount);
            Assert.AreEqual(0, world.EnemyOf("story").Status.ActiveCount);
            world.Step(0);
            Assert.AreEqual(40, world.ResourcesOf(caster).Mana,
                "Same-tick auto cast cannot bypass the verified 2-second cooldown.");
        }

        [Test]
        public void AutomaticSkillWithUnverifiedCooldownCannotAutoConsumeMana()
        {
            var world = World();
            long caster = world.SpawnCharacter("TEST-01", 0, 0);
            world.ConfigureCharacterResources(caster,
                new CharacterResources(100, 100, 100, 50));
            world.SpawnEnemy("distant", 1000, CharacterEnemyKind.Line, 100000, 0, 0);
            world.MobileAutomation.Configure(null, null,
                new MobileUnlimitedAutoSkillTargetRules());
            world.Step(0);
            Assert.AreEqual(50, world.ResourcesOf(caster).Mana);
            Assert.AreEqual(0, world.EnemyOf("distant").Status.ActiveCount);
        }

        [Test]
        public void AutomaticSkillWithNoVerifiedResourcesCannotCast()
        {
            var world = World(true);
            world.SpawnCharacter("TEST-01", 0, 0);
            world.SpawnEnemy("distant", 1000, CharacterEnemyKind.Line, 100000, 0, 0);
            world.MobileAutomation.Configure(null, null,
                new MobileUnlimitedAutoSkillTargetRules());
            world.Step(0);
            Assert.AreEqual(0, world.EnemyOf("distant").Status.ActiveCount);
        }

        [Test]
        public void ActiveSkillDoesNotSpendManaOnEnemyInDifferentBattlefield()
        {
            var world = World();
            long caster = world.SpawnCharacter("TEST-01",0,0);
            world.ConfigureCharacterResources(caster,
                new CharacterResources(100,100,100,50));
            world.SpawnEnemy("story",1000,CharacterEnemyKind.Story,0,0,0);
            world.SpawnEnemy("line",1000,CharacterEnemyKind.Line,0,0,0);

            Assert.AreEqual(AbilityCastResult.MissingTarget,
                world.TryCastActiveSkill(caster,"TEST-01:skill:0","story"));
            Assert.AreEqual(50,world.ResourcesOf(caster).Mana);
            Assert.AreEqual(0,world.EnemyOf("story").Status.ActiveCount);
            Assert.AreEqual(0,world.EnemyOf("line").Status.ActiveCount);

            Assert.AreEqual(AbilityCastResult.Cast,
                world.TryCastActiveSkill(caster,"TEST-01:skill:0","line"));
            Assert.AreEqual(40,world.ResourcesOf(caster).Mana);
            Assert.AreEqual(1,world.EnemyOf("line").Status.ActiveCount);
            Assert.AreEqual(0,world.EnemyOf("story").Status.ActiveCount);
        }

        [TestCase("stun", "", CombatEffectType.Stun, 1.0)]
        [TestCase("armorReduction", ",\"amount\":12", CombatEffectType.ArmorReduction, 12.0)]
        [TestCase("movementSlow", ",\"percent\":20", CombatEffectType.MovementSlowPercent, 20.0)]
        [TestCase("movementSlowPercent", ",\"percent\":20", CombatEffectType.MovementSlowPercent, 20.0)]
        [TestCase("magicResistanceReduction", ",\"percent\":10", CombatEffectType.MagicResistanceReduction, 10.0)]
        [TestCase("magicDamageAmplification", ",\"percent\":10", CombatEffectType.MagicDamageAmplification, 10.0)]
        [TestCase("explosiveDamageAmplification", ",\"percent\":10", CombatEffectType.ExplosiveDamageAmplification, 10.0)]
        [TestCase("allDamageAmplification", ",\"percent\":10", CombatEffectType.AllDamageAmplification, 10.0)]
        public void CommonTimedStatusUsesTheSameCastAndExpiryPath(string type,
            string value, CombatEffectType expected, double amount)
        {
            var world = World(true, "{\"type\":\"" + type + "\"" + value +
                ",\"durationSeconds\":1}", true);
            long caster = world.SpawnCharacter("TEST-01", 0, 0);
            world.ConfigureCharacterResources(caster, new CharacterResources(100, 100, 100, 50));
            world.SpawnEnemy("primary", 1000, CharacterEnemyKind.Line, 1000, 0, 0);
            world.SpawnEnemy("near", 1000, CharacterEnemyKind.Line, 1010, 0, 0);
            world.SpawnEnemy("outside", 1000, CharacterEnemyKind.Line, 1021, 0, 0);
            world.SpawnEnemy("story", 1000, CharacterEnemyKind.Story, 1000, 0, 0);
            Assert.AreEqual(AbilityCastResult.Cast, world.TryCastActiveSkill(caster, "TEST-01:skill:0", "primary"));
            foreach (string id in new[] { "primary", "near" })
            {
                var status = world.StatusOf(id).Snapshot();
                Assert.AreEqual(1, status.Count);
                Assert.AreEqual(expected, status[0].Type);
                Assert.AreEqual(amount, status[0].Amount);
            }
            Assert.AreEqual(0, world.StatusOf("outside").ActiveCount);
            Assert.AreEqual(0, world.StatusOf("story").ActiveCount);
            world.Step(1);
            Assert.AreEqual(0, world.StatusOf("primary").ActiveCount);
            Assert.AreEqual(0, world.StatusOf("near").ActiveCount);
        }

        [TestCase("{\"type\":\"armorReduction\",\"durationSeconds\":1}")]
        [TestCase("{\"type\":\"movementSlow\",\"percent\":10,\"durationSeconds\":0}")]
        [TestCase("{\"type\":\"allDamageAmplification\",\"percent\":10,\"durationSeconds\":1,\"maxPercent\":25}")]
        [TestCase("{\"type\":\"armorBreak\",\"stacks\":1.5}")]
        [TestCase("{\"type\":\"armorBreak\",\"stacks\":1,\"maxArmorReduction\":75}")]
        public void InvalidOrUnsupportedCommonEffectCannotSpendManaOrApplyPartialSkill(string invalid)
        {
            var world = World(true, "{\"type\":\"stun\",\"durationSeconds\":1}," + invalid);
            long caster = world.SpawnCharacter("TEST-01", 0, 0);
            world.ConfigureCharacterResources(caster, new CharacterResources(100, 100, 100, 50));
            world.SpawnEnemy("target", 1000, CharacterEnemyKind.Line, 0, 0, 0);
            Assert.AreEqual(AbilityCastResult.UnsupportedEffect,
                world.TryCastActiveSkill(caster, "TEST-01:skill:0", "target"));
            Assert.AreEqual(50, world.ResourcesOf(caster).Mana);
            Assert.AreEqual(0, world.StatusOf("target").ActiveCount);
        }

        [Test]
        public void CommonArmorBreakUsesIntegerStacksAndItsExplicitCap()
        {
            var world = World(true, "{\"type\":\"armorBreak\",\"stacks\":3,\"maxStacks\":4}");
            long caster = world.SpawnCharacter("TEST-01", 0, 0);
            world.ConfigureCharacterResources(caster, new CharacterResources(100, 100, 100, 50));
            world.SpawnEnemy("target", 1000, CharacterEnemyKind.Line, 0, 0, 0);
            Assert.AreEqual(AbilityCastResult.Cast, world.TryCastActiveSkill(caster, "TEST-01:skill:0", "target"));
            Assert.AreEqual(3, world.ArmorBreakStacksOf("target"));
            world.Step(2);
            Assert.AreEqual(AbilityCastResult.Cast, world.TryCastActiveSkill(caster, "TEST-01:skill:0", "target"));
            Assert.AreEqual(4, world.ArmorBreakStacksOf("target"));
        }
    }
}
