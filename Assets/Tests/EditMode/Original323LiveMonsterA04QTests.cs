using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323LiveMonsterA04QTests
    {
        private sealed class Witness : IOriginal323LiveMonsterA04QWitness
        {
            public bool Approved = true;
            public string Evidence = "observed JASS Bd5 existing-unit event";
            public bool TryConfirm(string enemyId, int previous, int current,
                out string evidence)
            {
                evidence = Evidence;
                return Approved && enemyId == "line-a" &&
                    previous == 16 && current == 1;
            }
        }

        private sealed class MagicHitSource : IOrdr323VerifiedHitSource
        {
            public bool TryResolveBasic(BasicDamageRequest request,
                CombatEnemy enemy, out Ordr323VerifiedDamageHit hit)
            {
                hit = new Ordr323VerifiedDamageHit(1000, Ordr323AttackType.Chaos,
                    false, false, true, DamageTraits.None, "TEST_FIXTURE",
                    DamageSchool.Magical);
                return true;
            }
            public bool TryResolveSkill(CharacterSkillDamageRequest request,
                out Ordr323VerifiedDamageHit hit)
            {
                hit = null;
                return false;
            }
        }

        private sealed class FixtureQuantization : IOrdr323VerifiedDamageQuantization
        {
            public long Quantize(double finalDamage, Ordr323VerifiedDamageHit hit)
            {
                return (long)Math.Round(finalDamage, MidpointRounding.AwayFromZero);
            }
        }

        private static CombatEnemy Spawn(string id = "line-a")
        {
            return new CombatEnemy(id, 2000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line, 50, "normal", 16,
                    "original JASS CUM + AbilityData.slk + war3map.w3a"));
        }

        [Test]
        public void ObservedIndividualLevelUpdatesOnlyMagicDefenseNotHealthOrArmor()
        {
            var enemy = Spawn();
            Assert.AreEqual(700, enemy.ReceiveDamage(700));
            Assert.IsTrue(enemy.TryApplyObservedOriginalA04QLevel(
                16, 1, new Witness()));
            Assert.AreEqual(1, enemy.Defenses.MagicAbilityLevel.Value);
            Assert.AreEqual(25, enemy.Defenses.MagicResistancePercent.Value, 1e-4);
            Assert.AreEqual(50, enemy.Defenses.BaseArmor.Value);
            Assert.AreEqual("normal", enemy.Defenses.WarcraftArmorType);
            Assert.AreEqual(CharacterEnemyKind.Line, enemy.Defenses.Kind.Value);
            Assert.AreEqual(2000, enemy.MaxHealth);
            Assert.AreEqual(1300, enemy.Health);
        }

        [Test]
        public void ExistingCombatDamageCalculationImmediatelyUsesUpdatedA04Q()
        {
            var enemy = Spawn();
            var source = new UnitCombatSpec("TEST",
                new BasicAttackSpec(1000, 1, DamageSchool.Magical,
                    DamageTraits.None), new CombatSkillSpec[0], "TEST_FIXTURE");
            var attack = new BasicDamageRequest("attacker", source, enemy.Id);
            var rules = new Ordr323OriginalDamageRules(
                new MagicHitSource(), null, new FixtureQuantization());
            Assert.AreEqual(900L,
                rules.ResolveBasic(attack, enemy, new CombatEffect[0], 0));
            Assert.IsTrue(enemy.TryApplyObservedOriginalA04QLevel(
                16, 1, new Witness()));
            Assert.AreEqual(750L,
                rules.ResolveBasic(attack, enemy, new CombatEffect[0], 0));
        }

        [Test]
        public void MissingWitnessOrUntrustedEvidenceCannotChangeExistingMonster()
        {
            var enemy = Spawn();
            Assert.IsFalse(enemy.TryApplyObservedOriginalA04QLevel(16, 1, null));
            var witness = new Witness { Approved = false };
            Assert.IsFalse(enemy.TryApplyObservedOriginalA04QLevel(16, 1, witness));
            witness.Approved = true;
            witness.Evidence = "TEST_ONLY";
            Assert.IsFalse(enemy.TryApplyObservedOriginalA04QLevel(16, 1, witness));
            witness.Evidence = "SYNTHETIC";
            Assert.IsFalse(enemy.TryApplyObservedOriginalA04QLevel(16, 1, witness));
            Assert.AreEqual(16, enemy.Defenses.MagicAbilityLevel.Value);
        }

        [Test]
        public void StaleDuplicateAndUnverifiedAbilityLevelsFailClosed()
        {
            var enemy = Spawn();
            var witness = new Witness();
            Assert.IsFalse(enemy.TryApplyObservedOriginalA04QLevel(15, 1, witness));
            Assert.IsFalse(enemy.TryApplyObservedOriginalA04QLevel(16, 42, witness));
            Assert.IsFalse(enemy.TryApplyObservedOriginalA04QLevel(16, 0, witness));
            Assert.IsTrue(enemy.TryApplyObservedOriginalA04QLevel(16, 1, witness));
            Assert.IsFalse(enemy.TryApplyObservedOriginalA04QLevel(16, 1, witness));
            Assert.AreEqual(1, enemy.Defenses.MagicAbilityLevel.Value);
        }

        [Test]
        public void UnknownBaselineAndOtherEnemyRemainUntouched()
        {
            var unknown = new CombatEnemy("line-a", 2000,
                new EnemyDefenseProfile(CharacterEnemyKind.Line, null, 10,
                    null, "external magic percentage only"));
            Assert.IsFalse(unknown.TryApplyObservedOriginalA04QLevel(
                16, 1, new Witness()));
            var other = Spawn("line-b");
            Assert.IsFalse(other.TryApplyObservedOriginalA04QLevel(
                16, 1, new Witness()));
            Assert.AreEqual(16, other.Defenses.MagicAbilityLevel.Value);
            Assert.IsFalse(other.Defenses.BaseArmor == null);
        }

        [Test]
        public void FutureSpawnStateDoesNotRetroactivelyUpdateExistingMonster()
        {
            var enemy = Spawn();
            var state = new Original323NightmareA04QSpawnState();
            var futureWitness = new FutureWitness();
            Assert.IsTrue(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.WarcuryActivated, -1, futureWitness));
            int futureLevel;
            Assert.IsTrue(state.TryGetNextSpawnLevel(
                CharacterEnemyKind.Line, 0, out futureLevel));
            Assert.AreEqual(1, futureLevel);
            Assert.AreEqual(16, enemy.Defenses.MagicAbilityLevel.Value);
        }

        private sealed class FutureWitness : IOriginal323NightmareA04QEventWitness
        {
            public bool TryConfirm(Original323NightmareA04QEvent sourceEvent,
                int playerSlot, out string evidence)
            {
                evidence = "observed JASS BeR future-spawn event";
                return sourceEvent == Original323NightmareA04QEvent.WarcuryActivated &&
                    playerSlot == -1;
            }
        }

        [Test]
        public void DefeatedMonsterCannotBeRewritten()
        {
            var enemy = Spawn();
            enemy.ReceiveDamage(enemy.MaxHealth);
            Assert.IsFalse(enemy.TryApplyObservedOriginalA04QLevel(
                16, 1, new Witness()));
        }
    }
}
