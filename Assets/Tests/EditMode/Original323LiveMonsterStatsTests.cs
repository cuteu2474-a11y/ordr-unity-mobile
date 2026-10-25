using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    // Simulated observation fixtures; these are not measurements from a retail game.
    public sealed class Original323LiveMonsterStatsTests
    {
        private sealed class Witness : IOriginal323LiveMonsterStatWitness
        {
            public bool Approved = true;
            public string Evidence = "original Warcraft existing-unit stat observation";
            public bool TryConfirmMaxHealthChange(string monsterId,
                long previousHealth, long previousMax,
                long currentHealth, long currentMax, out string source)
            {
                source = Evidence;
                return Approved && monsterId == "monster-one";
            }
            public bool TryConfirmPhysicalArmorChange(string monsterId,
                double previousArmor, string previousType,
                double currentArmor, string currentType, out string source)
            {
                source = Evidence;
                return Approved && monsterId == "monster-one";
            }
        }

        private sealed class PhysicalHit : IOrdr323VerifiedHitSource
        {
            public bool TryResolveBasic(BasicDamageRequest request,
                CombatEnemy enemy, out Ordr323VerifiedDamageHit hit)
            {
                hit = new Ordr323VerifiedDamageHit(1000, Ordr323AttackType.Chaos,
                    false, true, false, DamageTraits.None, "TEST_FIXTURE");
                return true;
            }
            public bool TryResolveSkill(CharacterSkillDamageRequest request,
                out Ordr323VerifiedDamageHit hit)
            {
                hit = null; return false;
            }
        }
        private sealed class FixtureQuantizer : IOrdr323VerifiedDamageQuantization
        {
            public long Quantize(double damage, Ordr323VerifiedDamageHit hit)
            {
                return (long)Math.Truncate(damage);
            }
        }

        private static CombatEnemy Spawn(string id = "monster-one")
        {
            return new CombatEnemy(id, 2000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line, 50, "normal", 16,
                    "source-backed test observation fixture"));
        }

        [Test]
        public void MaximumHealthChangeRequiresExactObservedCurrentAndMaxValues()
        {
            var enemy = Spawn();
            Assert.AreEqual(600L, enemy.ReceiveDamage(600));
            Assert.IsTrue(enemy.TryApplyObservedMaximumHealthChange(
                1400, 2000, 1800, 3000, new Witness()));
            Assert.AreEqual(3000L, enemy.MaxHealth);
            Assert.AreEqual(1800L, enemy.Health);
            Assert.AreEqual(50, enemy.Defenses.BaseArmor.Value);
            Assert.AreEqual(10, enemy.Defenses.MagicResistancePercent.Value, 1e-4);
            Assert.IsNotEmpty(enemy.ObservedHealthSource);
        }

        [Test]
        public void StaleCurrentHealthAndDuplicateEventAreRejected()
        {
            var enemy = Spawn();
            Assert.IsFalse(enemy.TryApplyObservedMaximumHealthChange(
                1999, 2000, 2500, 3000, new Witness()));
            Assert.IsTrue(enemy.TryApplyObservedMaximumHealthChange(
                2000, 2000, 2600, 3000, new Witness()));
            Assert.IsFalse(enemy.TryApplyObservedMaximumHealthChange(
                2000, 2000, 2600, 3000, new Witness()));
            Assert.AreEqual(2600L, enemy.Health);
        }

        [Test]
        public void MaxHpChangeRejectsUnsupportedZeroOverflowLikeAndImplicitHealing()
        {
            var enemy = Spawn();
            Assert.IsFalse(enemy.TryApplyObservedMaximumHealthChange(
                2000, 2000, 3001, 3000, new Witness()));
            Assert.IsFalse(enemy.TryApplyObservedMaximumHealthChange(
                2000, 2000, 0, 3000, new Witness()));
            Assert.IsFalse(enemy.TryApplyObservedMaximumHealthChange(
                2000, 2000, 2000, 0, new Witness()));
            Assert.IsFalse(enemy.TryApplyObservedMaximumHealthChange(
                2000, 2000, 2000, 2000, new Witness()));
            Assert.AreEqual(2000L, enemy.MaxHealth);
        }

        [Test]
        public void UnapprovedOrSyntheticWitnessDoesNotMutateHealthOrArmor()
        {
            var enemy = Spawn();
            Assert.IsFalse(enemy.TryApplyObservedMaximumHealthChange(
                2000, 2000, 2500, 3000, null));
            var witness = new Witness { Approved = false };
            Assert.IsFalse(enemy.TryApplyObservedMaximumHealthChange(
                2000, 2000, 2500, 3000, witness));
            witness.Approved = true;
            witness.Evidence = "MODEL_ONLY: upgrade HP result";
            Assert.IsFalse(enemy.TryApplyObservedMaximumHealthChange(
                2000, 2000, 2500, 3000, witness));
            Assert.IsFalse(enemy.TryApplyObservedPhysicalArmorChange(
                50, "normal", 70, "normal", witness));
            Assert.AreEqual(2000L, enemy.Health);
            Assert.AreEqual(50d, enemy.Defenses.BaseArmor.Value);
        }

        [Test]
        public void ObservedPhysicalArmorChangesSubsequentHitDamageButNotMagicResistance()
        {
            var enemy = Spawn();
            var source = new UnitCombatSpec("TEST",
                new BasicAttackSpec(1000, 1, DamageSchool.Physical,
                    DamageTraits.None), new CombatSkillSpec[0], "TEST_FIXTURE");
            var request = new BasicDamageRequest("player", source, enemy.Id);
            var rules = new Ordr323OriginalDamageRules(
                new PhysicalHit(), null, new FixtureQuantizer());
            Assert.AreEqual(500L, rules.ResolveBasic(
                request, enemy, new CombatEffect[0], 0));
            Assert.IsTrue(enemy.TryApplyObservedPhysicalArmorChange(
                50, "normal", 70, "normal", new Witness()));
            Assert.AreEqual(416L, rules.ResolveBasic(
                request, enemy, new CombatEffect[0], 0));
            Assert.AreEqual(16, enemy.Defenses.MagicAbilityLevel.Value);
            Assert.AreEqual(10d, enemy.Defenses.MagicResistancePercent.Value, 1e-4);
            Assert.AreEqual(2000L, enemy.Health);
        }

        [Test]
        public void UnverifiedOrStaleArmorAndUnknownWarcraftArmorTypeFailClosed()
        {
            var enemy = Spawn();
            var witness = new Witness();
            Assert.IsFalse(enemy.TryApplyObservedPhysicalArmorChange(
                51, "normal", 70, "normal", witness));
            Assert.IsFalse(enemy.TryApplyObservedPhysicalArmorChange(
                50, "normal", 70, "imaginary", witness));
            Assert.IsTrue(enemy.TryApplyObservedPhysicalArmorChange(
                50, "normal", 70, "normal", witness));
            Assert.IsFalse(enemy.TryApplyObservedPhysicalArmorChange(
                50, "normal", 90, "normal", witness));
            Assert.AreEqual(70d, enemy.Defenses.BaseArmor.Value);
        }

        [Test]
        public void UnknownOriginalArmorCannotBePromotedByAnArbitraryEvent()
        {
            var enemy = new CombatEnemy("monster-one", 2000,
                new EnemyDefenseProfile(CharacterEnemyKind.Line, null, 10,
                    null, "unverified armor"));
            Assert.IsFalse(enemy.TryApplyObservedPhysicalArmorChange(
                0, "normal", 10, "normal", new Witness()));
            Assert.IsNull(enemy.Defenses.BaseArmor);
        }

        [Test]
        public void DefeatedMonsterRejectsAllStatMutationAndKeepsKillAccounting()
        {
            var game = new GameSession();
            game.Start(); game.BeginWave(1);
            var resolution = new CombatResolution(game, new CombatScheduler());
            var enemy = Spawn();
            resolution.Spawn(enemy);
            Assert.IsTrue(resolution.TryApplySkillDamage(
                "player", enemy.Id, 2000, out long actual));
            Assert.AreEqual(2000L, actual);
            Assert.AreEqual(1, game.DefeatedEnemies);
            Assert.AreEqual(0, game.AliveEnemies);
            Assert.IsFalse(enemy.TryApplyObservedMaximumHealthChange(
                0, 2000, 2000, 3000, new Witness()));
            Assert.IsFalse(enemy.TryApplyObservedPhysicalArmorChange(
                50, "normal", 70, "normal", new Witness()));
        }

        [Test]
        public void LiveRuntimeUsesTheSameMonsterAndReportsUpdatedHealthToMobileHud()
        {
            var units = CharacterUnitCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[]}");
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var unavailable = new RejectUnverifiedDamageFormula();
            var world = new CharacterCombatRuntime(units, skills,
                unavailable, unavailable, new SeededNonOriginalProcRandom(111));
            world.Start();
            world.BeginWave(1);
            world.SpawnEnemyWithDefenses("monster-one", 2000, CharacterEnemyKind.Line,
                0, 0, 0, Spawn().Defenses);
            Assert.IsTrue(world.TryApplyObservedEnemyMaximumHealthChange(
                "monster-one", 2000, 2000, 2600, 3000, new Witness()));
            Assert.IsTrue(world.TryApplyObservedEnemyPhysicalArmorChange(
                "monster-one", 50, "normal", 70, "normal", new Witness()));
            Assert.AreEqual(3000L, world.EnemyOf("monster-one").MaxHealth);
            Assert.AreEqual(2600L, world.MobileEnemySnapshot()[0].Health);
            Assert.AreEqual(3000L, world.MobileEnemySnapshot()[0].MaxHealth);
            Assert.AreEqual(70d, world.EnemyOf("monster-one").Defenses.BaseArmor.Value);
        }
    }
}
