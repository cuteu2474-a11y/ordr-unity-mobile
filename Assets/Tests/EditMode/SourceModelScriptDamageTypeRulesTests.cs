using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceModelScriptDamageTypeRulesTests
    {
        private static CombatEnemy Enemy(double? armor, int? magicLevel,
            string armorClass = "normal")
        {
            var defense = magicLevel.HasValue
                ? Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line, armor, armorClass,
                    magicLevel.Value, "TEST_ONLY: pinned original A04Q table")
                : new EnemyDefenseProfile(CharacterEnemyKind.Line, armor,
                    null, armorClass, "TEST_ONLY: missing magic evidence");
            return new CombatEnemy("target", 1000000, defense);
        }

        private static SourceScriptHit Hit(SourceScriptDamageType damageType)
        {
            return new SourceScriptHit(SourceScriptHitShape.Direct,
                0, 0, 250, 1, 1, Ordr323AttackType.Spells,
                damageType: damageType);
        }

        private static double Resolve(SourceScriptHit hit, CombatEnemy enemy)
        {
            SourceModelScriptDamageFactors factors;
            string reason;
            Assert.IsTrue(SourceModelScriptDamageTypeRules.TryPrepare(
                hit, enemy, out factors, out reason), reason);
            return factors.Apply(hit.Damage, hit.Attack);
        }

        [Test]
        public void NativeMagicalSpellUsesArmorClassAndA04QButIgnoresNumericArmor()
        {
            Assert.AreEqual(225, Resolve(Hit(SourceScriptDamageType.Magical),
                Enemy(0, 16)), 1e-9);
            Assert.AreEqual(225, Resolve(Hit(SourceScriptDamageType.Magical),
                Enemy(500, 16)), 1e-9);
            // Spell attack-type row is still 0.85 against Large armor.
            Assert.AreEqual(191.25, Resolve(Hit(SourceScriptDamageType.Magical),
                Enemy(500, 16, "large")), 1e-9);
            Assert.AreEqual(287.5, Resolve(Hit(SourceScriptDamageType.Magical),
                Enemy(500, 41)), 1e-5);
        }

        [Test]
        public void NormalAndUniversalDamageDoNotInheritMagicResistance()
        {
            // Legacy unpinned hits remain on their previous physical model.
            Assert.AreEqual(125, Resolve(Hit(SourceScriptDamageType.ModelDefault),
                Enemy(50, 16)), 1e-9);
            Assert.AreEqual(125, Resolve(Hit(SourceScriptDamageType.Physical),
                Enemy(50, 16)), 1e-9);
            Assert.AreEqual(250, Resolve(Hit(SourceScriptDamageType.Universal),
                Enemy(50, 16)), 1e-9);
            Assert.IsTrue(Hit(SourceScriptDamageType.Magical).UsesMagicResistance);
            Assert.IsFalse(Hit(SourceScriptDamageType.Magical).UsesArmorRating);
            Assert.IsFalse(Hit(SourceScriptDamageType.Universal).UsesMagicResistance);
        }

        [Test]
        public void UnobservedMagicLevelNeverBecomesZeroPercentResistance()
        {
            SourceModelScriptDamageFactors factors;
            string reason;
            Assert.IsFalse(SourceModelScriptDamageTypeRules.TryPrepare(
                Hit(SourceScriptDamageType.Magical), Enemy(30, null),
                out factors, out reason));
            StringAssert.Contains("A04Q", reason);
            // Universal still works without MR evidence.
            Assert.AreEqual(250, Resolve(Hit(SourceScriptDamageType.Universal),
                Enemy(null, null)), 1e-9);
        }

        [Test]
        public void UnreconciledNativeMagicStacksFailClosed()
        {
            var enemy = Enemy(20, 16);
            enemy.Status.Apply(new CombatEffect("TEST_ONLY:mr-down",
                CombatEffectType.MagicResistanceReduction,
                CombatEffectOrigin.TriggeredSkill, 10, 5));
            SourceModelScriptDamageFactors factors;
            string reason;
            Assert.IsFalse(SourceModelScriptDamageTypeRules.TryPrepare(
                Hit(SourceScriptDamageType.Magical), enemy,
                out factors, out reason));
            StringAssert.Contains("중첩", reason);
            Assert.AreEqual(250.0 / 1.4,
                Resolve(Hit(SourceScriptDamageType.ModelDefault), enemy), 1e-9);
        }

        [Test]
        public void SourcePinnedNativeSpellsDeclareDamageTypeSeparately()
        {
            Assert.AreEqual(SourceScriptDamageType.Magical,
                SourceModelNextAttackCatalog.BonClayPhoenixFireTick().DamageType);
            Assert.AreEqual(250,
                SourceModelNextAttackCatalog.BonClayPhoenixFireTick().Damage);
            SourceScriptAttackSpec enel;
            Assert.IsTrue(SourceModelNextAttackCatalog.TryGet("h00Q", out enel));
            Assert.AreEqual(SourceScriptDamageType.Magical,
                enel.OnProc[0].DamageType);
            SourceScriptAttackSpec ace;
            Assert.IsTrue(SourceModelNextAttackCatalog.TryGet("h01J", out ace));
            Assert.AreEqual(SourceScriptDamageType.Magical,
                ace.OnProc[0].DamageType);
            SourceScriptAttackSpec rankyaku;
            Assert.IsTrue(SourceModelScriptAttackCatalog.TryGet("h00M", out rankyaku));
            Assert.AreEqual(SourceScriptDamageType.Magical,
                rankyaku.OnProc[0].DamageType);
            SourceScriptAttackSpec universal;
            Assert.IsTrue(SourceModelScriptAttackCatalog.TryGet("h00P", out universal));
            Assert.AreEqual(SourceScriptDamageType.Universal,
                universal.OnProc[0].DamageType);
        }
    }
}
