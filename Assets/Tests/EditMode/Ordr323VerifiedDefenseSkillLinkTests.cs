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
            int ordinal = 0, int? chanceOverride = null,
            int? manaThresholdOverride = null)
        {
            string prefix = "";
            for (int i = 0; i < ordinal; i++)
                prefix += "{\"name\":\"dummy\",\"trigger\":\"onAttack\"," +
                          "\"target\":\"single\",\"components\":[]},";
            string field = componentType == "armorBreak" ? "stacks" : "percent";
            int? chance = chanceOverride;
            int? mana = manaThresholdOverride;
            foreach (var link in Ordr323VerifiedDefenseSkillLinks.All)
                if (link.SkillId == unitId + ":skill:" + ordinal &&
                    link.ComponentType == componentType)
                {
                    if (!chance.HasValue) chance = link.VerifiedProcChancePercent;
                    if (!mana.HasValue) mana = link.VerifiedManaThreshold;
                    break;
                }
            string triggerFields =
                (chance.HasValue ? ",\"chancePercent\":" + chance.Value : "") +
                (mana.HasValue ? ",\"manaCost\":" + mana.Value : "");
            string json = "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[" +
              "{\"unitId\":\"" + unitId + "\",\"sourceUrl\":\"TEST_FIXTURE\"," +
              "\"skillSpecCompleteness\":\"partial\",\"passives\":[]," +
              "\"basicAttack\":{}," +
              "\"skills\":[" + prefix +
              "{\"name\":\"" + skillName + "\",\"trigger\":\"" +
              trigger + "\"" + triggerFields +
              ",\"target\":\"single\",\"components\":[{" +
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

            public Fixture(Func<string,string> verifiedCasterRawcode = null)
            {
                Router = new Ordr323VerifiedDefenseEffectRouter(
                    Stacks,Proof,Status,verifiedCasterRawcode);
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
        public void VerifiedAttackChancesAndManaThresholdsStayPinnedToJass()
        {
            var chance = new Dictionary<string,int> {
                {"E-10:skill:1",11}, {"I-09:skill:3",10},
                {"L-23:skill:0",5}, {"L-23:skill:1",10},
                {"L-37:skill:0",14}
            };
            var mana = new Dictionary<string,int> {
                {"H-10:skill:0",100}, {"H-09:skill:0",150},
                {"I-03:skill:0",115}, {"L-20:skill:0",50},
                {"L-07:skill:0",115}
            };
            int known = 0;
            foreach (var link in Ordr323VerifiedDefenseSkillLinks.All)
            {
                int verified;
                if (chance.TryGetValue(link.SkillId,out verified))
                {
                    Assert.AreEqual(verified,link.VerifiedProcChancePercent);
                    Assert.IsNull(link.VerifiedManaThreshold);
                    known++;
                }
                else if (mana.TryGetValue(link.SkillId,out verified))
                {
                    Assert.AreEqual(verified,link.VerifiedManaThreshold);
                    Assert.IsNull(link.VerifiedProcChancePercent);
                    known++;
                }
                else
                {
                    // H-10 skill 1: chance from this JASS evidence is still unpinned.
                    Assert.AreEqual("H-10:skill:1",link.SkillId);
                    Assert.IsNull(link.VerifiedProcChancePercent);
                    Assert.IsNull(link.VerifiedManaThreshold);
                }
            }
            Assert.AreEqual(10,known);
        }

        [Test]
        public void TooltipChanceOrManaDriftCannotApplyOriginalJassDebuff()
        {
            var f = new Fixture();
            var enemy = Enemy(); f.Track(enemy);
            string reason;
            var wrongChance = ProfileFixture("E-10","미검","onAttack",
                "armorBreak",5,1,chanceOverride:12);
            Assert.IsFalse(f.Apply(wrongChance,enemy,true,out reason,1));
            Assert.AreEqual(0,f.Stacks[enemy.Id]);

            var wrongMana = ProfileFixture("H-09","뇌영","activeMana",
                "magicResistanceReduction",10,0,manaThresholdOverride:149);
            Assert.IsFalse(f.Apply(wrongMana,enemy,true,out reason));
            Assert.AreEqual(0,f.Status[enemy.Id].ActiveCount);

            var matching = ProfileFixture("E-10","미검","onAttack", "armorBreak",5,1);
            Assert.IsTrue(f.Apply(matching,enemy,true,out reason,1),reason);
            Assert.AreEqual(5,f.Stacks[enemy.Id]);
        }

        [Test]
        public void VerifiedJassAttackProcIsRegisteredOnlyWithMatchingRawcodeAndChance()
        {
            var basic = new BasicAttackSpec(10,1,DamageSchool.Physical,
                DamageTraits.None);
            var correct = ProfileFixture("E-10","미검","onAttack",
                "armorBreak",5,1);
            var accepted = CharacterCombatMapping.Build("E-10",correct,basic,"h05B");
            Assert.IsTrue(accepted.ActivatedSkills.ContainsKey("E-10:skill:1"));

            var wrongUnit = CharacterCombatMapping.Build("E-10",correct,basic,"h000");
            Assert.IsFalse(wrongUnit.ActivatedSkills.ContainsKey("E-10:skill:1"));
            StringAssert.Contains("rawcode",string.Join(" ",wrongUnit.DeferredReasons));

            var missingUnit = CharacterCombatMapping.Build("E-10",correct,basic);
            Assert.IsFalse(missingUnit.ActivatedSkills.ContainsKey("E-10:skill:1"));
            StringAssert.Contains("rawcode",string.Join(" ",missingUnit.DeferredReasons));

            var changed = ProfileFixture("E-10","미검","onAttack",
                "armorBreak",5,1,chanceOverride:12);
            var wrongChance = CharacterCombatMapping.Build("E-10",changed,basic,"h05B");
            Assert.IsFalse(wrongChance.ActivatedSkills.ContainsKey("E-10:skill:1"));
            StringAssert.Contains("불일치",string.Join(" ",wrongChance.DeferredReasons));
        }

        [Test]
        public void UnverifiedJassProcChanceIsNeverRegisteredFromTooltipAlone()
        {
            var profile = ProfileFixture("H-10","기본 공격 발동형 타격",
                "onAttack","armorBreak",1,1,chanceOverride:20);
            var mapping = CharacterCombatMapping.Build("H-10",profile,
                new BasicAttackSpec(10,1,DamageSchool.Physical,DamageTraits.None),
                "h03W");
            Assert.IsFalse(mapping.ActivatedSkills.ContainsKey("H-10:skill:1"));
            StringAssert.Contains("발동 확률 미확인",string.Join(" ",mapping.DeferredReasons));
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
        public void OriginalSkillLinkRejectsWrongOrMissingBoundUnitRawcode()
        {
            // The profile and tooltip alone cannot authorize a W3X debuff.
            // A similarly named transformed/duplicate unit needs its own JASS registration.
            string resolved = "h000";
            var f = new Fixture(_ => resolved);
            var enemy = Enemy();
            f.Track(enemy);
            var profile = ProfileFixture("E-10", "미검", "onAttack",
                "armorBreak", 5, 1);
            var skill = profile.Skills[1];
            var component = skill.Components[0];
            string reason;
            Assert.IsFalse(f.Router.CanRoute("unit",profile,skill,component));
            Assert.IsFalse(f.Apply(profile,enemy,true,out reason,1));
            StringAssert.Contains("rawcode",reason);
            Assert.AreEqual(0,f.Stacks[enemy.Id]);
            Assert.AreEqual(0,f.Proof[enemy.Id]);

            resolved = null;
            Assert.IsFalse(f.Router.CanRoute("unit",profile,skill,component));
            Assert.IsFalse(f.Apply(profile,enemy,true,out reason,1));
            Assert.AreEqual(0,f.Stacks[enemy.Id]);

            resolved = "h05B"; // JASS w5(h05B,B5Z)
            Assert.IsTrue(f.Router.CanRoute("unit",profile,skill,component));
            Assert.IsTrue(f.Apply(profile,enemy,true,out reason,1),reason);
            Assert.AreEqual(5,f.Stacks[enemy.Id]);
        }

        [Test]
        public void VerifiedPreDamageRouterRequiresCorrectCasterRawcode()
        {
            var f = new Fixture(_ => "h000");
            var enemy = Enemy(); f.Track(enemy);
            var profile = ProfileFixture("H-10", "기본 공격 발동형 타격",
                "onAttack", "armorBreak", 1, 1);
            var skill = profile.Skills[1];
            var component = skill.Components[0];
            Assert.IsFalse(f.Router.HasPreDamagePart("unit",profile,skill,component));
            string reason;
            Assert.IsFalse(f.Router.TryApplyAtPhase("unit",profile,skill,component,
                enemy,true,Ordr323DebuffTiming.BeforeDamage,out reason));
            Assert.AreEqual(0,f.Stacks[enemy.Id]);
            StringAssert.Contains("rawcode",reason);
        }

        [Test]
        public void ActiveManaCannotConsumeResourcesWithWrongJassCasterRawcode()
        {
            var catalog = CharacterUnitCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[" +
                "{\"id\":\"H-10\",\"grade\":\"히든\",\"name\":\"베르고\"," +
                "\"roleTags\":[]}]}");
            var profiles = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[" +
                "{\"unitId\":\"H-10\",\"sourceUrl\":\"TEST_FIXTURE\"," +
                "\"skillSpecCompleteness\":\"partial\",\"basicAttack\":{}," +
                "\"passives\":[],\"skills\":[{" +
                "\"name\":\"맥시멈 스윙\",\"trigger\":\"activeMana\"," +
                "\"manaCost\":100,\"target\":\"single\",\"components\":[{" +
                "\"type\":\"armorBreak\",\"stacks\":4}]}]}]}");
            foreach (string rawcode in new[] { "h000", "h03W" })
            {
                var missing = new RejectUnverifiedDamageFormula();
                var world = new CharacterCombatRuntime(catalog,profiles,
                    missing,missing,new SeededNonOriginalProcRandom(7));
                world.ConfigureOriginal323DefenseSkillLinks();
                world.Start(); world.BeginWave(1);
                long caster = world.SpawnCharacter("H-10",0,0);
                world.BindVerifiedBasicAttack(caster,
                    new BasicAttackSpec(10,1,DamageSchool.Physical,
                        DamageTraits.None),0,rawcode);
                world.ConfigureCharacterResources(caster,
                    new CharacterResources(100,100,100,100));
                world.SpawnEnemy("target",1000,CharacterEnemyKind.Line,0,0,0);

                var result = world.TryCastActiveSkill(caster,"H-10:skill:0","target");
                if (rawcode == "h000")
                {
                    Assert.AreEqual(AbilityCastResult.UnsupportedEffect,result);
                    Assert.AreEqual(100,world.ResourcesOf(caster).Mana);
                    Assert.AreEqual(0,world.ArmorBreakStacksOf("target"));
                }
                else
                {
                    Assert.AreEqual(AbilityCastResult.Cast,result);
                    Assert.AreEqual(0,world.ResourcesOf(caster).Mana);
                    Assert.AreEqual(4,world.ArmorBreakStacksOf("target"));
                }
            }
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
