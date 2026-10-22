using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    // 정확한 JASS unitId/trigger/name/effect 값만 매핑하는 정적 검증.
    // 사용자 테스트 도감 데이터는 캐릭터 스펙을 가상으로 복제한 것이며
    // 실제 스킬 발동의 원작 DPS/쿨타임을 검증하지 않는다.
    public class Ordr323VerifiedDefenseSkillLinkTests
    {
        private static CharacterSkillProfile ProfileFixture(string unitId, string skillName,
            string trigger, string componentType, int componentValue,
            int ordinal = 0)
        {
            string prefix = "";
            for (int i = 0; i < ordinal; i++)
                prefix += "{\"name\":\"dummy\",\"trigger\":\"onAttack\"," +
                          "\"target\":\"single\",\"components\":[]},";
            string field = componentType == "armorBreak" ? "stacks" : "percent";
            string json = "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[" +
              "{\"unitId\":\"" + unitId + "\",\"sourceUrl\":\"TEST_FIXTURE\"," +
              "\"skillSpecCompleteness\":\"partial\",\"passives\":[]," +
              "\"basicAttack\":{}," +
              "\"skills\":[" + prefix +
              "{\"name\":\"" + skillName + "\",\"trigger\":\"" +
              trigger + "\",\"target\":\"single\",\"components\":[{" +
              "\"type\":\"" + componentType + "\",\"" + field + "\":" +
              componentValue + "}]}]}]}";
            return CharacterSkillCatalog.FromJson(json).Require(unitId);
        }

        private static SpatialCombatEnemy Enemy(string enemyId = "enemy",
            int? a04qLevel = 16)
        {
            EnemyDefenseProfile defense = a04qLevel.HasValue
                ? Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line, 50, "normal", a04qLevel.Value, "TEST_FIXTURE")
                : new EnemyDefenseProfile(CharacterEnemyKind.Line,
                    50, 10, "normal", "TEST_FIXTURE");
            return new SpatialCombatEnemy(
                new CombatEnemy(enemyId, 100000, defense),
                CharacterEnemyKind.Line, 0, 0, 0);
        }

        // 원작 데이터가 아닌 "마방이 적용되는지"만 확인하는 인공 테스트 입력.
        private sealed class AlwaysMagicHit : IOrdr323VerifiedHitSource
        {
            public bool TryResolveBasic(BasicDamageRequest request,
                CombatEnemy enemy, out Ordr323VerifiedDamageHit hit)
            {
                hit = new Ordr323VerifiedDamageHit(1000,
                    Ordr323AttackType.Spells, false, false, true,
                    DamageTraits.None, "TEST_FIXTURE", DamageSchool.Magical);
                return true;
            }

            public bool TryResolveSkill(CharacterSkillDamageRequest request,
                out Ordr323VerifiedDamageHit hit)
            {
                hit = null;
                return false;
            }
        }

        private sealed class TruncateOnlyForTest : IOrdr323VerifiedDamageQuantization
        {
            public long Quantize(double damage, Ordr323VerifiedDamageHit hit)
            {
                return (long)Math.Truncate(damage);
            }
        }

        private static BasicDamageRequest FakeMagicAttack(string enemyId)
        {
            return new BasicDamageRequest("caster","TEST_RAWCODE",
                new BasicAttackSpec(1000, 1, DamageSchool.Magical, DamageTraits.None),
                enemyId);
        }

        private sealed class Fixture
        {
            public readonly Dictionary<string,int> Stacks = new Dictionary<string,int>();
            public readonly Dictionary<string,int> Proof = new Dictionary<string,int>();
            public readonly Dictionary<string,CombatStatusEngine> Status =
                new Dictionary<string,CombatStatusEngine>();
            public readonly Ordr323VerifiedDefenseEffectRouter Router;

            public Fixture()
            {
                Router = new Ordr323VerifiedDefenseEffectRouter(Stacks,Proof,Status);
            }

            public void Track(SpatialCombatEnemy enemy)
            {
                Stacks.Add(enemy.Id, 0);
                Proof.Add(enemy.Id, 0);
                Status.Add(enemy.Id, enemy.Enemy.Status);
            }

            public bool Apply(CharacterSkillProfile profile, SpatialCombatEnemy enemy,
                bool primary, out string reason, int ordinal = 0)
            {
                return Router.TryApply("unit", profile, profile.Skills[ordinal],
                    profile.Skills[ordinal].Components[0], enemy, primary, out reason);
            }
        }

        [Test]
        public void ElevenOriginalJassLinksCoverNineUnits()
        {
            Assert.AreEqual(11, Ordr323VerifiedDefenseSkillLinks.All.Count);
            var ids = new HashSet<string>();
            foreach (var item in Ordr323VerifiedDefenseSkillLinks.All)
            {
                ids.Add(item.UnitId);
                Assert.IsTrue(item.AbilityRawcode == "A04M" ||
                              item.AbilityRawcode == "A04Q");
                Assert.IsFalse(string.IsNullOrWhiteSpace(item.JassEvidence));
            }
            Assert.AreEqual(9, ids.Count);
        }

        [Test]
        public void UnknownOrChangedTooltipDoesNotGainJassEffect()
        {
            var f = new Fixture();
            var enemy = Enemy();
            f.Track(enemy);
            string reason;
            var incorrect = ProfileFixture("E-10", "다른 기술", "onAttack", "armorBreak", 5);
            Assert.IsFalse(f.Apply(incorrect, enemy, true, out reason));
            Assert.AreEqual(0, f.Stacks[enemy.Id]);
            Assert.AreEqual(0, f.Status[enemy.Id].ActiveCount);
            var matchingNameWrongTrigger = ProfileFixture(
                "E-10","미검","activeMana","armorBreak",5);
            Assert.IsFalse(f.Apply(matchingNameWrongTrigger, enemy,true,out reason));
            var wrongValue = ProfileFixture("E-10","미검","onAttack","armorBreak",4);
            Assert.IsFalse(f.Apply(wrongValue,enemy,true,out reason));
        }

        [Test]
        public void HiddenVergoPrimaryTargetReceivesBothJassBdqApplications()
        {
            var f = new Fixture();
            var primary = Enemy("primary");
            var adjacent = Enemy("adjacent");
            f.Track(primary);
            f.Track(adjacent);
            var p = ProfileFixture("H-10","기본 공격 발동형 타격",
                "onAttack","armorBreak",1,1);
            string reason;
            Assert.IsTrue(f.Apply(p,primary,true,out reason,1),reason);
            Assert.AreEqual(2,f.Stacks[primary.Id]); // 직접+광역 둘 다 맞음
            Assert.IsTrue(f.Apply(p,adjacent,false,out reason,1),reason);
            Assert.AreEqual(1,f.Stacks[adjacent.Id]);
            // 기존 W3A에 없는 1-3스택은 피해 계산기가 추정하지 않는다.
            var formula = new Ordr323VerifiedPartialModifierRules(
                (enemyId,stacks) =>
                    f.Proof.ContainsKey(enemyId) && f.Proof[enemyId] == stacks);
            double armor;
            Assert.IsFalse(formula.TryResolveArmor(primary.Enemy,
                new CombatEffect[0], f.Stacks[primary.Id], out armor));
            Assert.IsTrue(f.Apply(p,primary,true,out reason,1),reason);
            Assert.IsTrue(formula.TryResolveArmor(primary.Enemy,
                new CombatEffect[0], f.Stacks[primary.Id], out armor));
            Assert.AreEqual(46,armor);
        }

        [Test]
        public void EthernalCavendishA04MLevelFiveDirectlyChangesArmor()
        {
            var f = new Fixture();
            var enemy = Enemy();
            f.Track(enemy);
            var profile = ProfileFixture("E-10","미검","onAttack","armorBreak",5,1);
            string reason;
            Assert.IsTrue(f.Apply(profile,enemy,true,out reason,1),reason);
            Assert.AreEqual(5,f.Stacks[enemy.Id]);
            var formula = new Ordr323VerifiedPartialModifierRules(
                (enemyId,stacks) =>
                    f.Proof.ContainsKey(enemyId) && f.Proof[enemyId] == stacks);
            double actualArmor;
            Assert.IsTrue(formula.TryResolveArmor(
                enemy.Enemy, new CombatEffect[0], f.Stacks[enemy.Id], out actualArmor));
            Assert.AreEqual(45,actualArmor);
        }

        [Test]
        public void HiddenVergoActiveManaAddsFourA04MStacksToEachTarget()
        {
            var f = new Fixture();
            var enemy = Enemy(); f.Track(enemy);
            var p = ProfileFixture("H-10","맥시멈 스윙","activeMana","armorBreak",4);
            string reason;
            Assert.IsTrue(f.Apply(p,enemy,true,out reason),reason);
            Assert.AreEqual(4,f.Stacks[enemy.Id]);
        }

        [Test]
        public void ImmortalRayleighAndShikiMapToTheirOwnJassHandlers()
        {
            var f = new Fixture();
            var r = Enemy("rayleigh"); var shiki = Enemy("shiki");
            f.Track(r);f.Track(shiki);
            var rayleigh = ProfileFixture("I-03","패기발산","activeMana","armorBreak",5);
            var shikiProfile = ProfileFixture("I-09","범위 마법 스턴",
                "onAttack","armorBreak",1,3);
            string reason;
            Assert.IsTrue(f.Apply(rayleigh,r,true,out reason),reason);
            Assert.AreEqual(5,f.Stacks[r.Id]);
            Assert.IsTrue(f.Apply(shikiProfile,shiki,true,out reason,3),reason);
            Assert.AreEqual(1,f.Stacks[shiki.Id]);
        }

        [Test]
        public void JassVergoBrPAndBrNSplitDebuffsBeforeAndAfterDamage()
        {
            var f = new Fixture();
            var target = Enemy("primary");
            var nearby = Enemy("nearby");
            f.Track(target); f.Track(nearby);
            var profile = ProfileFixture("H-10","기본 공격 발동형 타격",
                "onAttack","armorBreak",1,1);
            var skill = profile.Skills[1];
            var effect = skill.Components[0];
            Assert.IsTrue(f.Router.HasPreDamagePart(profile,skill,effect));
            string why;
            Assert.IsTrue(f.Router.TryApplyAtPhase("unit",profile,skill,effect,
                target,true,Ordr323DebuffTiming.BeforeDamage,out why),why);
            Assert.AreEqual(1,f.Stacks[target.Id]);
            Assert.AreEqual(0,f.Stacks[nearby.Id]);
            // 아래는 BrN 광역 Bcb가 처리된 뒤 호출해야 하는 후행 Bdq.
            Assert.IsTrue(f.Router.TryApplyAtPhase("unit",profile,skill,effect,
                target,true,Ordr323DebuffTiming.AfterDamage,out why),why);
            Assert.IsTrue(f.Router.TryApplyAtPhase("unit",profile,skill,effect,
                nearby,false,Ordr323DebuffTiming.AfterDamage,out why),why);
            Assert.AreEqual(2,f.Stacks[target.Id]);
            Assert.AreEqual(1,f.Stacks[nearby.Id]);
        }

        [Test]
        public void JassEnelBr6AppliesResistanceBeforeDamageOnlyOnce()
        {
            var f = new Fixture();
            var target = Enemy();
            f.Track(target);
            var profile=ProfileFixture("H-09","뇌영",
                "activeMana","magicResistanceReduction",10);
            var skill=profile.Skills[0];var component=skill.Components[0];
            Assert.IsTrue(f.Router.HasPreDamagePart(profile,skill,component));
            string why;
            Assert.IsTrue(f.Router.TryApplyAtPhase("unit",profile,skill,component,
                target,true,Ordr323DebuffTiming.BeforeDamage,out why),why);
            Assert.AreEqual(1,f.Status[target.Id].ActiveCount);
            Assert.IsTrue(f.Router.TryApplyAtPhase("unit",profile,skill,component,
                target,true,Ordr323DebuffTiming.AfterDamage,out why),why);
            Assert.AreEqual("skipped",why);
            Assert.AreEqual(1,f.Status[target.Id].ActiveCount);
            var formula=new Ordr323VerifiedPartialModifierRules();
            double percent;
            Assert.IsTrue(formula.TryResolveMagicResistancePercent(
                target.Enemy,f.Status[target.Id].Snapshot(),out percent));
            Assert.AreEqual(0,percent,1e-4);
        }

        [Test]
        public void JassCavendishB5ZAppliesFiveArmorBreakAfterDamage()
        {
            var f=new Fixture();
            var target=Enemy();f.Track(target);
            var profile=ProfileFixture("E-10","미검","onAttack","armorBreak",5,1);
            var skill=profile.Skills[1];var effect=skill.Components[0];
            Assert.IsFalse(f.Router.HasPreDamagePart(profile,skill,effect));
            string why;
            Assert.IsTrue(f.Router.TryApplyAtPhase("unit",profile,skill,effect,
                target,true,Ordr323DebuffTiming.BeforeDamage,out why),why);
            Assert.AreEqual("skipped",why);
            Assert.AreEqual(0,f.Stacks[target.Id]);
            Assert.IsTrue(f.Router.TryApplyAtPhase("unit",profile,skill,effect,
                target,true,Ordr323DebuffTiming.AfterDamage,out why),why);
            Assert.AreEqual(5,f.Stacks[target.Id]);
        }

        [Test]
        public void LegendRayleighAppliesA04MBeforeHisAttackDamage()
        {
            var f=new Fixture();var enemy=Enemy();
            f.Track(enemy);
            var profile=ProfileFixture("L-07","패기발산","activeMana","armorBreak",3);
            var skill=profile.Skills[0];var effect=skill.Components[0];
            Assert.IsTrue(f.Router.HasPreDamagePart(profile,skill,effect));
            string reason;
            Assert.IsTrue(f.Router.TryApplyAtPhase("rayleigh",profile,skill,effect,
                enemy,true,Ordr323DebuffTiming.BeforeDamage,out reason),reason);
            Assert.AreEqual(3,f.Stacks[enemy.Id]);
            Assert.IsTrue(f.Router.TryApplyAtPhase("rayleigh",profile,skill,effect,
                enemy,true,Ordr323DebuffTiming.AfterDamage,out reason),reason);
            Assert.AreEqual("skipped",reason);
            Assert.AreEqual(3,f.Stacks[enemy.Id]);
        }

        [Test]
        public void LegendShikiSeparateFiveAndTenPercentDebuffCallbacks()
        {
            var f=new Fixture();
            var beforeTarget=Enemy("rocks");var afterTarget=Enemy("sea");
            f.Track(beforeTarget);f.Track(afterTarget);
            var rocks=ProfileFixture("L-23","바위낙하",
                "onAttack","armorBreak",2);
            var sea=ProfileFixture("L-23","바닷물 가두기",
                "onAttack","armorBreak",1,1);
            string why;
            Assert.IsTrue(f.Router.HasPreDamagePart(rocks,rocks.Skills[0],
                rocks.Skills[0].Components[0]));
            Assert.IsFalse(f.Router.HasPreDamagePart(sea,sea.Skills[1],
                sea.Skills[1].Components[0]));
            Assert.IsTrue(f.Router.TryApplyAtPhase("shiki",rocks,rocks.Skills[0],
                rocks.Skills[0].Components[0],beforeTarget,true,
                Ordr323DebuffTiming.BeforeDamage,out why),why);
            Assert.AreEqual(2,f.Stacks[beforeTarget.Id]);
            Assert.IsTrue(f.Router.TryApplyAtPhase("shiki",sea,sea.Skills[1],
                sea.Skills[1].Components[0],afterTarget,true,
                Ordr323DebuffTiming.BeforeDamage,out why),why);
            Assert.AreEqual(0,f.Stacks[afterTarget.Id]);
            Assert.IsTrue(f.Router.TryApplyAtPhase("shiki",sea,sea.Skills[1],
                sea.Skills[1].Components[0],afterTarget,true,
                Ordr323DebuffTiming.AfterDamage,out why),why);
            Assert.AreEqual(1,f.Stacks[afterTarget.Id]);
        }

        [Test]
        public void LegendSmokerAndIsshoWaitForDamageBeforeApplyingDebuffs()
        {
            var f=new Fixture();
            var smokerTarget=Enemy("smoker");
            var isshoTarget=Enemy("issho");
            f.Track(smokerTarget);f.Track(isshoTarget);
            var smoker=ProfileFixture("L-20","화이트 아웃",
                "activeMana","armorBreak",3);
            var issho=ProfileFixture("L-37","중력검",
                "onAttack","magicResistanceReduction",1);
            string why;
            Assert.IsTrue(f.Router.TryApplyAtPhase("smoker",smoker,smoker.Skills[0],
                smoker.Skills[0].Components[0],smokerTarget,true,
                Ordr323DebuffTiming.BeforeDamage,out why),why);
            Assert.AreEqual(0,f.Stacks[smokerTarget.Id]);
            Assert.IsTrue(f.Router.TryApplyAtPhase("smoker",smoker,smoker.Skills[0],
                smoker.Skills[0].Components[0],smokerTarget,true,
                Ordr323DebuffTiming.AfterDamage,out why),why);
            Assert.AreEqual(3,f.Stacks[smokerTarget.Id]);
            Assert.IsTrue(f.Router.TryApplyAtPhase("issho",issho,issho.Skills[0],
                issho.Skills[0].Components[0],isshoTarget,true,
                Ordr323DebuffTiming.BeforeDamage,out why),why);
            Assert.AreEqual(0,f.Status[isshoTarget.Id].ActiveCount);
            Assert.IsTrue(f.Router.TryApplyAtPhase("issho",issho,issho.Skills[0],
                issho.Skills[0].Components[0],isshoTarget,true,
                Ordr323DebuffTiming.AfterDamage,out why),why);
            Assert.AreEqual(1,f.Status[isshoTarget.Id].ActiveCount);
            double percent;
            Assert.IsTrue(new Ordr323VerifiedPartialModifierRules()
                .TryResolveMagicResistancePercent(isshoTarget.Enemy,
                    f.Status[isshoTarget.Id].Snapshot(),out percent));
            Assert.AreEqual(9,percent,1e-4);
        }

        [Test]
        public void EnelA04QIncreasesVerifiedMonsterAbilityLevelInsteadOfPercentAura()
        {
            var f = new Fixture();
            var enemy = Enemy();
            f.Track(enemy);
            var profile = ProfileFixture("H-09","뇌영",
                "activeMana","magicResistanceReduction",10);
            string reason;
            Assert.IsTrue(f.Apply(profile,enemy,true,out reason),reason);
            var original = new Ordr323VerifiedPartialModifierRules();
            double percent;
            Assert.IsTrue(original.TryResolveMagicResistancePercent(
                enemy.Enemy,f.Status[enemy.Id].Snapshot(),out percent));
            Assert.AreEqual(0,percent,1e-4); // A04Q 16→26
            Assert.AreEqual(1,f.Status[enemy.Id].ActiveCount);
            Assert.IsTrue(f.Apply(profile,enemy,true,out reason),reason);
            Assert.IsTrue(original.TryResolveMagicResistancePercent(
                enemy.Enemy,f.Status[enemy.Id].Snapshot(),out percent));
            Assert.AreEqual(-10,percent,1e-4); // A04Q 36 -> factor 1.10
        }

        [Test]
        public void UnverifiedMonsterA04QLevelDoesNotChange()
        {
            var f = new Fixture();
            var enemy = Enemy("unknown",null);
            f.Track(enemy);
            var profile = ProfileFixture("H-09","뇌영",
                "activeMana","magicResistanceReduction",10);
            string reason;
            Assert.IsFalse(f.Apply(profile,enemy,true,out reason));
            Assert.AreEqual(0,f.Status[enemy.Id].ActiveCount);
        }

        [Test]
        public void EnelFloat32DefenseWithFixtureTruncationGives899Then1000()
        {
            var f = new Fixture();
            var enemy = Enemy();
            f.Track(enemy);
            var rules = new Ordr323OriginalDamageRules(new AlwaysMagicHit(),
                new Ordr323VerifiedPartialModifierRules(),
                new TruncateOnlyForTest());
            var attack = FakeMagicAttack(enemy.Id);
            // W3A Def5 level 16 is stored as float32 (slightly below 0.9).
            // The synthetic truncation fixture therefore yields 899, not 900.
            // This does NOT establish Warcraft's real final HP quantization.
            Assert.AreEqual(899L,rules.ResolveBasic(attack,enemy.Enemy,
                f.Status[enemy.Id].Snapshot(),0));
            var profile = ProfileFixture("H-09","뇌영","activeMana",
                "magicResistanceReduction",10);
            string reason;
            Assert.IsTrue(f.Apply(profile,enemy,true,out reason),reason);
            Assert.AreEqual(1000L,rules.ResolveBasic(attack,enemy.Enemy,
                f.Status[enemy.Id].Snapshot(),0));
        }

        [Test]
        public void OriginalA04QLevelAboveSourceTableCannotBeGuessed()
        {
            var f = new Fixture();
            var enemy = Enemy("cap",36);
            f.Track(enemy);
            var profile = ProfileFixture("H-09","뇌영",
                "activeMana","magicResistanceReduction",10);
            string reason;
            Assert.IsFalse(f.Apply(profile,enemy,true,out reason));
            Assert.AreEqual(0,f.Status[enemy.Id].ActiveCount);
        }
    }
}
