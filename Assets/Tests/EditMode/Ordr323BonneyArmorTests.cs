using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    // Synthetic units/defense input: checks extracted A04P formula and routing.
    // Unity/NUnit has NOT been executed in the authoring environment.
    public class Ordr323BonneyArmorTests
    {
        private sealed class Fixture
        {
            public readonly Dictionary<string, CombatStatusEngine> Statuses =
                new Dictionary<string, CombatStatusEngine>();
            public readonly Dictionary<string, int> Totals =
                new Dictionary<string, int>();
            public readonly Ordr323BonneyDefenseController Controller;
            public readonly CombatEnemy Target;
            public readonly Ordr323VerifiedPartialModifierRules Modifiers;

            public Fixture()
            {
                var status = new CombatStatusEngine();
                Statuses.Add("target", status);
                Totals.Add("target", 0);
                Controller = new Ordr323BonneyDefenseController(Statuses, Totals);
                Target = new CombatEnemy("target", 100000,
                    new EnemyDefenseProfile(CharacterEnemyKind.Line,
                        50, 10, "normal", "TEST_FIXTURE"));
                Modifiers = new Ordr323VerifiedPartialModifierRules(
                    (enemyId, amount) => false,
                    (enemyId, amount) => enemyId == "target" &&
                        Totals[enemyId] == amount);
            }

            public bool Apply(Ordr323BonneyForm form,
                Ordr323BonneyArmorEvent eventKind, out string reason)
            {
                return Controller.TryApply(37, form, eventKind, "target", out reason);
            }

            public bool TryReadArmor(int a04MStacks, out double result)
            {
                return Modifiers.TryResolveArmor(Target,
                    Statuses["target"].Snapshot(), a04MStacks, out result);
            }
        }

        private sealed class BonneyFormFixture : IOriginalBonneyFormEvidence
        {
            public bool TryGetVerifiedForm(long instanceId, out Ordr323BonneyForm form)
            {
                form = Ordr323BonneyForm.BaseH0BX;
                return true;
            }
        }

        private sealed class FixtureRandom : ICombatProcRandom
        {
            public int Next(int exclusiveUpperBound) { return 0; }
        }

        private sealed class FixtureBasicDamage : IOrdr323VerifiedHitSource
        {
            public bool TryResolveBasic(BasicDamageRequest request,
                CombatEnemy target, out Ordr323VerifiedDamageHit hit)
            {
                hit = new Ordr323VerifiedDamageHit(1000,
                    Ordr323AttackType.Melee, false, true, false,
                    DamageTraits.None,"TEST_ONLY_FIGHT",DamageSchool.Physical);
                return true;
            }
            public bool TryResolveSkill(CharacterSkillDamageRequest request,
                out Ordr323VerifiedDamageHit hit)
            {
                hit = null;return false;
            }
        }

        private sealed class FixtureTruncator : IOrdr323VerifiedDamageQuantization
        {
            public long Quantize(double amount, Ordr323VerifiedDamageHit hit)
            {
                return (long)Math.Truncate(amount);
            }
        }

        private static CharacterCombatRuntime MinimalVerifiedBonneyBattle(bool withForm)
        {
            string units = "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[{" +
                "\"id\":\"TR-21\",\"name\":\"보니\",\"grade\":\"초월함\"," +
                "\"roleTags\":[],\"skillSpecsStatus\":\"partial\"}]}";
            string spells = "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[{" +
                "\"unitId\":\"TR-21\",\"sourceUrl\":\"TEST_FIXTURE\"," +
                "\"skillSpecCompleteness\":\"partial\",\"skills\":[]," +
                "\"passives\":[],\"basicAttack\":{}}]}";
            var runtime = Ordr323DamageWorldBinding.Create(
                CharacterUnitCatalog.FromJson(units),
                CharacterSkillCatalog.FromJson(spells),
                new FixtureBasicDamage(),null,new FixtureTruncator(),
                new FixtureRandom());
            if (withForm)
                runtime.ConfigureVerifiedBonneyFormEvidence(new BonneyFormFixture());
            runtime.Start();
            runtime.BeginWave(1);
            long bonney = runtime.SpawnCharacter("TR-21",0,0);
            runtime.BindVerifiedBasicAttack(bonney,
                new BasicAttackSpec(1000,1,DamageSchool.Physical,
                    DamageTraits.None),0,"H0BX");
            runtime.SpawnEnemyWithDefenses("enemy",10000,
                CharacterEnemyKind.Line,10,0,0,
                new EnemyDefenseProfile(CharacterEnemyKind.Line,50,10,
                    "normal","TEST_FIXTURE"));
            return runtime;
        }

        [Test]
        public void EndToEndAttackDeclaredA04PChangesActualArmorBasedDamage()
        {
            var world=MinimalVerifiedBonneyBattle(true);
            world.Step(0);
            // ARMOR 50 -> A04P+10 -> effective 40.
            // In this TEST fixture (not original DPS), 1000/(1+.02*40)=555.55..
            Assert.AreEqual(9445L,world.EnemyOf("enemy").Health);
            Assert.IsTrue(world.IsVerified323BonneyArmorTotal("enemy",10));
            world.Step(1);
            // Next declared attack: A04P+20 overall, armor30, damage625.
            Assert.AreEqual(8820L,world.EnemyOf("enemy").Health);
            Assert.IsTrue(world.IsVerified323BonneyArmorTotal("enemy",20));
        }

        [Test]
        public void EndToEndWithoutVerifiedFormDoesNotGuessBonneyDebuff()
        {
            var world=MinimalVerifiedBonneyBattle(false);
            world.Step(0);
            // No source can attest H0BX/H0BY, so 50 armor remains unchanged.
            Assert.AreEqual(9500L,world.EnemyOf("enemy").Health);
            Assert.IsFalse(world.IsVerified323BonneyArmorTotal("enemy",10));
        }

        [Test]
        public void BaseBonneyB4CAttackStartsAtLevelElevenAndReducesArmorByTen()
        {
            var f = new Fixture(); string reason;
            Assert.IsTrue(f.Apply(Ordr323BonneyForm.BaseH0BX,
                Ordr323BonneyArmorEvent.AttackDeclared,out reason),reason);
            Assert.AreEqual(10,f.Totals["target"]);
            int currentLevel;
            Assert.IsTrue(f.Controller.TryGetVerifiedAbilityLevel("target",out currentLevel));
            Assert.AreEqual(11,currentLevel);
            double armor;
            Assert.IsTrue(f.TryReadArmor(0,out armor));
            Assert.AreEqual(40,armor,1e-9);
            Assert.AreEqual(1,f.Statuses["target"].ActiveCount);
            Assert.AreEqual(CombatEffectType.OriginalA04PLevelIncrease,
                f.Statuses["target"].Snapshot()[0].Type);
        }

        [Test]
        public void NormalAttackPlusSingleProcPlusAttackReachFortyWithoutExtrapolation()
        {
            var f = new Fixture();string reason;double armor;
            Assert.IsTrue(f.Apply(Ordr323BonneyForm.BaseH0BX,
                Ordr323BonneyArmorEvent.AttackDeclared,out reason),reason);
            Assert.IsTrue(f.Apply(Ordr323BonneyForm.BaseH0BX,
                Ordr323BonneyArmorEvent.SingleProc,out reason),reason);
            Assert.AreEqual(30,f.Totals["target"]);
            Assert.IsTrue(f.TryReadArmor(0,out armor));
            Assert.AreEqual(20,armor,1e-9);
            Assert.IsTrue(f.Apply(Ordr323BonneyForm.TransformedH0BY,
                Ordr323BonneyArmorEvent.AttackDeclared,out reason),reason);
            Assert.AreEqual(40,f.Totals["target"]);
            Assert.IsTrue(f.TryReadArmor(0,out armor));
            Assert.AreEqual(10,armor,1e-9);
            Assert.IsFalse(f.Apply(Ordr323BonneyForm.BaseH0BX,
                Ordr323BonneyArmorEvent.AttackDeclared,out reason));
            Assert.AreEqual(40,f.Totals["target"]);
            Assert.AreEqual(3,f.Statuses["target"].ActiveCount);
        }

        [Test]
        public void InitialPlusTwoCannotInventMissingLowerW3ALevels()
        {
            var f = new Fixture();string reason;double armor;
            Assert.IsTrue(f.Apply(Ordr323BonneyForm.BaseH0BX,
                Ordr323BonneyArmorEvent.AreaProc,out reason),reason);
            Assert.AreEqual(2,f.Totals["target"]);
            Assert.IsFalse(f.TryReadArmor(0,out armor));
            Assert.IsTrue(f.Apply(Ordr323BonneyForm.BaseH0BX,
                Ordr323BonneyArmorEvent.AreaProc,out reason),reason);
            Assert.AreEqual(4,f.Totals["target"]);
            Assert.IsTrue(f.TryReadArmor(0,out armor));
            Assert.AreEqual(46,armor,1e-9);
        }

        [Test]
        public void MixedA04PAndA04MAndUnknownAurasMustNotBeBlindlySummed()
        {
            var f = new Fixture();string reason;double armor;
            Assert.IsTrue(f.Apply(Ordr323BonneyForm.BaseH0BX,
                Ordr323BonneyArmorEvent.AttackDeclared,out reason),reason);
            Assert.IsFalse(f.TryReadArmor(5,out armor)); // even if 5 A04M proven
            f.Statuses["target"].Apply(new CombatEffect(
                "TEST_unknown_armor_aura",CombatEffectType.ArmorReduction,
                CombatEffectOrigin.Aura,15,0));
            Assert.IsFalse(f.TryReadArmor(0,out armor));
        }

        [Test]
        public void ForgedOrOutOfSyncLevelEvidenceIsRejected()
        {
            var f = new Fixture();string reason;double armor;
            f.Statuses["target"].Apply(new CombatEffect("spoofed",
                CombatEffectType.OriginalA04PLevelIncrease,
                CombatEffectOrigin.Aura,10,0));
            Assert.IsFalse(f.Apply(Ordr323BonneyForm.BaseH0BX,
                Ordr323BonneyArmorEvent.AttackDeclared,out reason));
            Assert.IsFalse(f.TryReadArmor(0,out armor));
            Assert.AreEqual(0,f.Totals["target"]);
        }

        [Test]
        public void UnknownEnemyAndInvalidFormAreDeniedWithoutMutation()
        {
            var f = new Fixture();string reason;
            Assert.IsFalse(f.Controller.TryApply(37,Ordr323BonneyForm.BaseH0BX,
                Ordr323BonneyArmorEvent.AttackDeclared,"missing",out reason));
            Assert.IsFalse(f.Controller.TryApply(37,(Ordr323BonneyForm)999,
                Ordr323BonneyArmorEvent.AttackDeclared,"target",out reason));
            Assert.AreEqual(0,f.Totals["target"]);
            Assert.AreEqual(0,f.Statuses["target"].ActiveCount);
        }

        [Test]
        public void BonneyOriginalProcChancesAndRangeAreDifferentForBothForms()
        {
            Assert.AreEqual(100,Ordr323BonneyDefenseController.VerifiedRollThresholdPercent(
                Ordr323BonneyForm.BaseH0BX,Ordr323BonneyArmorEvent.AttackDeclared));
            Assert.AreEqual(30,Ordr323BonneyDefenseController.VerifiedRollThresholdPercent(
                Ordr323BonneyForm.BaseH0BX,Ordr323BonneyArmorEvent.SingleProc));
            Assert.AreEqual(15,Ordr323BonneyDefenseController.VerifiedRollThresholdPercent(
                Ordr323BonneyForm.BaseH0BX,Ordr323BonneyArmorEvent.AreaProc));
            Assert.AreEqual(50,Ordr323BonneyDefenseController.VerifiedRollThresholdPercent(
                Ordr323BonneyForm.TransformedH0BY,Ordr323BonneyArmorEvent.SingleProc));
            Assert.AreEqual(30,Ordr323BonneyDefenseController.VerifiedRollThresholdPercent(
                Ordr323BonneyForm.TransformedH0BY,Ordr323BonneyArmorEvent.AreaProc));
            Assert.IsTrue(Ordr323BonneyDefenseController.IsWithinVerifiedAreaProc(
                0,0,320,0));
            Assert.IsFalse(Ordr323BonneyDefenseController.IsWithinVerifiedAreaProc(
                0,0,320.1,0));
            Assert.IsFalse(Ordr323BonneyDefenseController.IsWithinVerifiedAreaProc(
                0,0,double.NaN,0));
        }

        [Test]
        public void OriginalDefensiveAbilitiesStaySeparateFromOriginalA04Q()
        {
            var f = new Fixture();string reason;
            Assert.IsTrue(f.Apply(Ordr323BonneyForm.BaseH0BX,
                Ordr323BonneyArmorEvent.AttackDeclared,out reason),reason);
            Assert.IsFalse(f.Statuses["target"].HasActiveEffect(
                CombatEffectType.OriginalA04QLevelIncrease,CombatEffectOrigin.Aura));
            Assert.IsFalse(f.Statuses["target"].HasActiveEffect(
                CombatEffectType.ArmorBreakStack,CombatEffectOrigin.Aura));
            Assert.IsTrue(f.Statuses["target"].HasActiveEffect(
                CombatEffectType.OriginalA04PLevelIncrease,CombatEffectOrigin.Aura));
        }
    }
}
