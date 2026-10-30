using System;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323MonsterNativeAbilityRuntimeTests
    {
        private sealed class CurrentNative : IOriginal323MonsterNativeAbilitiesWitness
        {
            public Original323ObservedMonsterNativeAbilities Current;
            public bool Mutate;
            public bool TryObserveCurrent(CombatEnemy target,
                out Original323ObservedMonsterNativeAbilities observed)
            {
                if (Mutate) target.Status.Apply(new CombatEffect("unexpected",
                    CombatEffectType.ArmorReduction, CombatEffectOrigin.Aura, 1, 0));
                observed = Current;
                return Current != null;
            }
        }

        private sealed class StunImpact : IOriginal323MonsterStunImpactWitness
        {
            public double Duration;
            public string Proof = "captured native stun impact";
            public bool Mutate;

            public bool TryObserveFinalStun(string impactId, string abilityCode,
                CombatEnemy target, out double actualSeconds, out string evidence)
            {
                if (Mutate) target.Status.Advance(0.01);
                actualSeconds = Duration;
                evidence = Proof;
                return true;
            }
        }

        private static Original323MonsterObjectAbilityCatalog Catalog() =>
            Original323MonsterObjectAbilityCatalog.FromJson(File.ReadAllText(
                Path.Combine(Application.dataPath, "..", "data",
                    "original-2323C-monster-object-abilities.json")));

        private static Original323ObservedMonsterNativeAbilities Flags(
            string targetId, string originalCode, bool visibleGhost = false,
            bool magicImmune = false, bool invulnerable = false,
            bool resistantSkin = false, bool hero = false, bool bashImmune = false,
            string evidence = "captured live native unit flags")
        {
            return new Original323ObservedMonsterNativeAbilities(targetId,
                originalCode, Original323VerifiedMonsterWaveCatalog.SourceW3xSha256,
                visibleGhost, magicImmune, invulnerable, resistantSkin,
                hero, bashImmune, evidence);
        }

        [Test]
        public void AethIsVisibleGhostNotMagicOrStunImmunity()
        {
            var witness = new CurrentNative { Current = Flags("line", "o00I", true) };
            var rules = new Original323MonsterNativeAbilityRuntime(Catalog(), witness);
            Assert.IsTrue(rules.HasStartingAbility("o00I", "Aeth"));
            Assert.IsFalse(rules.HasStartingAbility("o00I", "Amim"));
            Assert.IsFalse(rules.HasStartingAbility("o00I", "A08Q"));
            bool eligible, hero, immune;
            string proof;
            Assert.IsTrue(rules.TryCanReceiveNativeMagic(new CombatEnemy("line", 100), out eligible));
            Assert.IsTrue(eligible, "Ghost Visible must not grant immunity.");
            Assert.IsTrue(rules.TryInspectBash(new CombatEnemy("line", 100),
                out hero, out immune, out proof));
            Assert.IsFalse(hero);
            Assert.IsFalse(immune);
        }

        [Test]
        public void CreepsMagicImmuneAndStoryResistantSkinAreDistinctNativeAbilities()
        {
            var catalog = Catalog();
            foreach (string code in new[] { "n00E", "n00F", "n00G" })
                Assert.IsTrue(catalog.HasObjectBaseAbility(code, "Amim"));
            Assert.IsTrue(catalog.HasObjectBaseAbility("n000", "A08Q"));
            Assert.IsFalse(catalog.HasObjectBaseAbility("n000", "Amim"));
            var witness = new CurrentNative { Current = Flags("story", "n000",
                resistantSkin: true, bashImmune: false) };
            var rules = new Original323MonsterNativeAbilityRuntime(catalog, witness);
            bool eligible, hero, immune;
            string proof;
            Assert.IsTrue(rules.TryCanReceiveNativeMagic(new CombatEnemy("story", 50), out eligible));
            Assert.IsTrue(eligible, "Resistant Skin is not automatically Amim.");
            Assert.IsTrue(rules.TryInspectBash(new CombatEnemy("story", 50),
                out hero, out immune, out proof));
            Assert.IsFalse(immune, "A08Q does not prove current Bash immunity.");
            var existingBash = new Original323LiveBashTargetRules(catalog, rules);
            Assert.IsTrue(existingBash.TryInspect(new CombatEnemy("story", 50),
                out hero, out immune, out proof));
            Assert.IsFalse(immune, "The existing native Bash path uses shared flags.");
            witness.Current = Flags("creep", "n00E", magicImmune: true);
            Assert.IsTrue(rules.TryCanReceiveNativeMagic(new CombatEnemy("creep", 100), out eligible));
            Assert.IsFalse(eligible);
            witness.Current = Flags("invulnerable", "o02E", invulnerable: true);
            Assert.IsTrue(rules.TryCanReceiveNativeMagic(
                new CombatEnemy("invulnerable", 100), out eligible));
            Assert.IsFalse(eligible);
        }

        [Test]
        public void MissingStaleOrUntrustedCurrentNativeProofFailsClosed()
        {
            var witness = new CurrentNative();
            var rules = new Original323MonsterNativeAbilityRuntime(Catalog(), witness);
            bool eligible;
            var target = new CombatEnemy("creep", 100);
            Assert.IsFalse(rules.TryCanReceiveNativeMagic(target, out eligible));
            Assert.IsFalse(eligible);
            witness.Current = Flags("different-instance", "n00E", magicImmune: true);
            Assert.IsFalse(rules.TryCanReceiveNativeMagic(target, out eligible));
            witness.Current = Flags("creep", "n00E", magicImmune: true,
                evidence: "TEST_ONLY fabricated runtime");
            Assert.IsFalse(rules.TryCanReceiveNativeMagic(target, out eligible));
            witness.Current = Flags("creep", "zzzz", magicImmune: true);
            Assert.IsFalse(rules.TryCanReceiveNativeMagic(target, out eligible));
            witness.Current = Flags("creep", "n00E", magicImmune: true);
            witness.Mutate = true;
            Assert.IsFalse(rules.TryCanReceiveNativeMagic(target, out eligible));
        }

        [Test]
        public void StunAppliesObservedNativeDurationOnlyAndDeduplicatesImpacts()
        {
            var witness = new CurrentNative { Current = Flags("story", "n000",
                resistantSkin: true, bashImmune: false) };
            var rules = new Original323MonsterNativeAbilityRuntime(Catalog(), witness);
            var target = new CombatEnemy("story", 100);
            var impact = new StunImpact { Duration = 0.27 };
            bool applied;
            Assert.IsTrue(rules.TryApplyObservedStun(target, "impact-1", "A07P", impact, out applied));
            Assert.IsTrue(applied);
            Assert.AreEqual(0.27, target.Status.RemainingStunSeconds, 1e-9);
            Assert.IsFalse(rules.TryApplyObservedStun(target, "impact-1", "A07P", impact, out applied));
            Assert.IsFalse(applied);
            impact.Duration = 0;
            Assert.IsTrue(rules.TryApplyObservedStun(target, "impact-2", "A07P", impact, out applied));
            Assert.IsFalse(applied);
            Assert.AreEqual(0.27, target.Status.RemainingStunSeconds, 1e-9);
        }

        [Test]
        public void StunWitnessCannotInventDurationForBashImmuneOrMutateDuringInspection()
        {
            var witness = new CurrentNative { Current = Flags("creep", "n00E",
                magicImmune: true, bashImmune: true) };
            var rules = new Original323MonsterNativeAbilityRuntime(Catalog(), witness);
            var target = new CombatEnemy("creep", 100);
            var impact = new StunImpact { Duration = 1 };
            bool applied;
            Assert.IsFalse(rules.TryApplyObservedStun(target, "impact-a", "A07P", impact, out applied));
            Assert.IsFalse(target.Status.IsStunned());
            impact.Duration = 0;
            Assert.IsTrue(rules.TryApplyObservedStun(target, "impact-a", "A07P", impact, out applied));
            Assert.IsFalse(applied);
            witness.Current = Flags("creep", "n00E", bashImmune: false);
            impact.Duration = 0.5;
            impact.Mutate = true;
            Assert.IsFalse(rules.TryApplyObservedStun(target, "impact-b", "A07P", impact, out applied));
            Assert.IsFalse(target.Status.IsStunned());
            impact.Mutate = false;
            impact.Proof = "MOCK_ONLY forged duration";
            Assert.IsFalse(rules.TryApplyObservedStun(target, "impact-b", "A07P", impact, out applied));
        }
    }
}
