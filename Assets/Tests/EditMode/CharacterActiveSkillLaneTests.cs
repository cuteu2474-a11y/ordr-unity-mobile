using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class CharacterActiveSkillLaneTests
    {
        private static CharacterCombatRuntime World(bool withVerifiedCooldown = false)
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
                "\"target\":\"single\",\"components\":[{" +
                "\"type\":\"stun\",\"durationSeconds\":1}]}]}]}");
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
    }
}
