using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    // Exact map AbilityData.slk levels 1..4 + war3map.w3a levels 5..41.
    public class Ordr323DefenseAbilityTests
    {
        [Test]
        public void NightmareUsesTenPercentMagicReductionAtA04QLevel16()
        {
            double factor;
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetMagicDamageTakenFactor(
                16, out factor));
            Assert.AreEqual(0.90, factor, 1e-6);

            double resistance;
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities
                .TryGetNightmareBaselineResistance(out resistance));
            Assert.AreEqual(10.0, resistance, 1e-4);
        }

        [Test]
        public void A04QLevelsFollowExtractedFloat32Def5()
        {
            double v;
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetMagicDamageTakenFactor(5, out v));
            Assert.AreEqual(0.79, v, 1e-6);
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetMagicDamageTakenFactor(21, out v));
            Assert.AreEqual(0.95, v, 1e-6);
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetMagicDamageTakenFactor(26, out v));
            Assert.AreEqual(1.00, v, 1e-6);
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetMagicDamageTakenFactor(41, out v));
            Assert.AreEqual(1.15, v, 1e-6);
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetMagicDamageTakenFactor(1, out v));
            Assert.AreEqual(0.75, v, 1e-6);
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetMagicDamageTakenFactor(2, out v));
            Assert.AreEqual(0.76, v, 1e-6);
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetMagicDamageTakenFactor(3, out v));
            Assert.AreEqual(0.77, v, 1e-6);
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetMagicDamageTakenFactor(4, out v));
            Assert.AreEqual(0.78, v, 1e-6);
            Assert.IsFalse(Ordr323VerifiedDefenseAbilities.TryGetMagicDamageTakenFactor(0, out v));
            Assert.IsFalse(Ordr323VerifiedDefenseAbilities.TryGetMagicDamageTakenFactor(42, out v));
        }

        [Test]
        public void A04MItemArmorBreakLevelAndConfirmedBounds()
        {
            double d;
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetArmorChange("A04M",5,out d));
            Assert.AreEqual(-4.0, d);
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetArmorChange("A04M",76,out d));
            Assert.AreEqual(-75.0, d);
            Assert.IsFalse(Ordr323VerifiedDefenseAbilities.TryGetArmorChange("A04M",4,out d));
            Assert.IsFalse(Ordr323VerifiedDefenseAbilities.TryGetArmorChange("A04M",77,out d));
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetArmorBreakFromStacks(0,out d));
            Assert.AreEqual(0, d);
            Assert.IsFalse(Ordr323VerifiedDefenseAbilities.TryGetArmorBreakFromStacks(1,out d));
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetArmorBreakFromStacks(10,out d));
            Assert.AreEqual(-10, d);
        }

        [Test]
        public void OtherItemArmorBonusFieldMatchesMap()
        {
            double d;
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetArmorChange("A04N",5,out d));
            Assert.AreEqual(-12,d);
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetArmorChange("A04O",11,out d));
            Assert.AreEqual(-115,d);
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetArmorChange("A04P",41,out d));
            Assert.AreEqual(-40,d);
        }

        [Test]
        public void PartialModifierResolverAppliesOnlyVerifiedArmorBreak()
        {
            var enemy = new CombatEnemy("test",1000,
                new EnemyDefenseProfile(CharacterEnemyKind.Line,50,10,
                    "normal","2.323 TEST_FIXTURE"));
            var resolver = new Ordr323VerifiedPartialModifierRules(
                (enemyId, stacks) => enemyId == "test" && stacks == 10);
            double armor;
            Assert.IsTrue(resolver.TryResolveArmor(enemy, new CombatEffect[0],10,out armor));
            Assert.AreEqual(40,armor);
            Assert.IsFalse(resolver.TryResolveArmor(enemy,new CombatEffect[0],2,out armor));
        }

        [Test]
        public void ArmorBreakTagWithoutJassProvenanceNeverSilentlyApplies()
        {
            var enemy = new CombatEnemy("test",1000,
                new EnemyDefenseProfile(CharacterEnemyKind.Line,50,10,
                    "normal","2.323 TEST_FIXTURE"));
            var partial = new Ordr323VerifiedPartialModifierRules();
            double armor;
            Assert.IsFalse(partial.TryResolveArmor(
                enemy,new CombatEffect[0],10,out armor));
        }

        [Test]
        public void BdVInverseUsesFirstMatchingBuffNotProduct()
        {
            double inverse=Ordr323JassDamageInversion.ReverseEventDamage(
                500, 50, true, true, true);
            Assert.AreEqual(500*2/1.1,inverse,1e-8);
            Assert.AreEqual(500*2/1.25,
                Ordr323JassDamageInversion.ReverseEventDamage(
                    500,50,false,true,true),1e-8);
            Assert.AreEqual(500*2/1.2,
                Ordr323JassDamageInversion.ReverseEventDamage(
                    500,50,false,false,true),1e-8);
        }

        [Test]
        public void MagicDefenseFactorySupportsNewlyVerifiedLowLevelsAndRejectsUnknown()
        {
            var p=Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                CharacterEnemyKind.Line, 50, "normal",16,"JASS CUM+W3A");
            Assert.AreEqual(10,p.MagicResistancePercent.Value,1e-4);
            var warcury=Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                CharacterEnemyKind.Line,50,"normal",1,"JASS BeR + SLK:A04Q:Def5");
            Assert.AreEqual(25,warcury.MagicResistancePercent.Value,1e-4);
            Assert.Throws<NotSupportedException>(() =>
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line,50,"normal",42,"out-of-range"));
        }
    }
}
