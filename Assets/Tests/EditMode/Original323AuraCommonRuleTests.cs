using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323AuraCommonRuleTests
    {
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(7)]
        [TestCase(25)]
        public void IdenticalAurasContributeOnceIndependentOfUnitNames(double amount)
        {
            double result;
            Assert.IsTrue(Original323AuraCommonRules.TryResolveIdenticalAura(new[] {
                new Original323AuraContribution("A08N", "B011", 1, amount),
                new Original323AuraContribution("A08N", "B011", 1, amount),
                new Original323AuraContribution("A08N", "B011", 1, amount)
            }, out result));
            Assert.AreEqual(amount, result);
        }

        [TestCase("A0EO", "B011", 1, 3)]
        [TestCase("A08N", "B02G", 1, 3)]
        [TestCase("A08N", "B011", 2, 3)]
        [TestCase("A08N", "B011", 1, 4)]
        public void UnprovenDifferentIdentitiesRanksOrValuesRequireAnotherRule(
            string ability, string buff, int level, double amount)
        {
            double result;
            Assert.IsFalse(Original323AuraCommonRules.TryResolveIdenticalAura(new[] {
                new Original323AuraContribution("A08N", "B011", 1, 3),
                new Original323AuraContribution(ability, buff, level, amount)
            }, out result));
            Assert.AreEqual(0, result);
        }

        [Test]
        public void MissingContributionsOrIdentityNeverAcquireImplicitDefaults()
        {
            double result;
            Assert.IsFalse(Original323AuraCommonRules.TryResolveIdenticalAura(null, out result));
            Assert.IsFalse(Original323AuraCommonRules.TryResolveIdenticalAura(
                new Original323AuraContribution[] { null }, out result));
            Original323AuraContribution parsed;
            Assert.IsFalse(Original323AuraCommonRules.TryReadEffectIdentity(new CombatEffect(
                "original-aura:1:A08N:B011", CombatEffectType.ArmorReduction,
                CombatEffectOrigin.Aura, 3, 0), out parsed), "Missing rank is not assumed to be 1.");
            Assert.IsTrue(Original323AuraCommonRules.TryReadEffectIdentity(new CombatEffect(
                "original-aura:42:A08N:B011:1", CombatEffectType.ArmorReduction,
                CombatEffectOrigin.Aura, 3, 0), out parsed));
            Assert.AreEqual(1, parsed.Level);
        }

        [TestCase(3)]
        [TestCase(12)]
        [TestCase(40)]
        public void ArmorConsumerUsesTheCommonIdentityRuleWithoutASixUnitOrThreePointWhitelist(double amount)
        {
            // Synthetic source-approved effects test the generic calculation
            // contract; A123/B123 are not claims about a real map ability.
            var first = new CombatEffect("original-aura:11:A123:B123:1",
                CombatEffectType.ArmorReduction, CombatEffectOrigin.Aura, amount, 0);
            var second = new CombatEffect("original-aura:12:A123:B123:1",
                CombatEffectType.ArmorReduction, CombatEffectOrigin.Aura, amount, 0);
            var enemy = new CombatEnemy("TEST_ONLY", 1000,
                new EnemyDefenseProfile(CharacterEnemyKind.Line, 100, null, "normal", "TEST_ONLY"));
            var rules = new Ordr323VerifiedPartialModifierRules(
                verifiedSpecialAuraProvenance: (id, effect) => id == enemy.Id &&
                    (ReferenceEquals(effect, first) || ReferenceEquals(effect, second)));
            double armor;
            Assert.IsTrue(rules.TryResolveArmor(enemy, new[] { first, second }, 0, out armor));
            Assert.AreEqual(100 - amount, armor);
        }

        [TestCase(true, false, false, false, true)]
        [TestCase(true, false, false, true, true)]
        [TestCase(false, false, false, true, false)]
        [TestCase(true, true, false, false, false)]
        [TestCase(true, false, true, false, false)]
        public void CommonTargetExclusionsApplyRegardlessOfUnitGrade(bool friendly,
            bool hidden, bool invisible, bool immune, bool expected)
        {
            var source = new Original323AuraUnitState(true, false, false, false, "TEST_ONLY state");
            var target = new Original323AuraUnitState(true, hidden, invisible, immune, "TEST_ONLY state");
            Assert.AreEqual(expected, Original323AuraCommonRules.PassesCommonEligibility(source, target, friendly));
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void HiddenOrInvisibleSourceDoesNotSupplyAura(bool hidden, bool invisible)
        {
            var source = new Original323AuraUnitState(true, hidden, invisible, false, "TEST_ONLY");
            var target = new Original323AuraUnitState(true, false, false, false, "TEST_ONLY");
            Assert.IsFalse(Original323AuraCommonRules.PassesCommonEligibility(source, target, true));
            Assert.IsFalse(Original323AuraCommonRules.PassesCommonEligibility(null, target, true));
            Assert.Throws<ArgumentException>(() => new Original323AuraUnitState(true, false, false, false, null));
        }
    }
}
