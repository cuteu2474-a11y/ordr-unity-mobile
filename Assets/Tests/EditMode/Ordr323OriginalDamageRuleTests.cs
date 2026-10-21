using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    // 허구의 입력을 사용한 미실행 NUnit 회귀 사양.
    // 2.323[C] 실제 캐릭터를 테스트했다는 뜻이 아니다.
    public sealed class Ordr323OriginalDamageRuleTests
    {
        private sealed class FixtureHits : IOrdr323VerifiedHitSource
        {
            public bool Supported = true;

            public bool TryResolveBasic(BasicDamageRequest request,
                CombatEnemy enemy, out Ordr323VerifiedDamageHit hit)
            {
                hit = Supported ? new Ordr323VerifiedDamageHit(1000,
                    Ordr323AttackType.Chaos, true, true, false,
                    DamageTraits.None, "TEST_FIXTURE") : null;
                return hit != null;
            }

            public bool TryResolveSkill(CharacterSkillDamageRequest request,
                out Ordr323VerifiedDamageHit hit)
            {
                hit = null;
                return false;
            }
        }

        private sealed class FixtureTruncation : IOrdr323VerifiedDamageQuantization
        {
            public long Quantize(double value, Ordr323VerifiedDamageHit hit)
            {
                return (long)Math.Truncate(value);
            }
        }

        private static BasicDamageRequest Attack()
        {
            var source = new UnitCombatSpec("TEST",
                new BasicAttackSpec(1000, 1, DamageSchool.Physical,
                    DamageTraits.None),
                new CombatSkillSpec[0], "TEST_FIXTURE");
            return new BasicDamageRequest("attacker", source, "enemy");
        }

        private static CombatEnemy Enemy(double? armor, string type)
        {
            return new CombatEnemy("enemy", 2000, new EnemyDefenseProfile(
                CharacterEnemyKind.Line, armor, null, type, "TEST_FIXTURE"));
        }

        [Test]
        public void OriginalMapDamageRuleAppliesActualMapArmorCoefficient()
        {
            var rules = new Ordr323OriginalDamageRules(
                new FixtureHits(), null, new FixtureTruncation());
            Assert.AreEqual(500L, rules.ResolveBasic(Attack(), Enemy(50, "normal"),
                new CombatEffect[0], 0));
        }

        [Test]
        public void MissingArmorEvidenceDoesNotBecomeZeroArmor()
        {
            var rules = new Ordr323OriginalDamageRules(
                new FixtureHits(), null, new FixtureTruncation());
            Assert.Throws<NotSupportedException>(() =>
                rules.ResolveBasic(Attack(), Enemy(null, "normal"),
                    new CombatEffect[0], 0));
        }

        [Test]
        public void ArmorBreakAndArmorReductionNeedVerifiedStacking()
        {
            var rules = new Ordr323OriginalDamageRules(
                new FixtureHits(), null, new FixtureTruncation());
            Assert.Throws<NotSupportedException>(() =>
                rules.ResolveBasic(Attack(), Enemy(50, "normal"),
                    new CombatEffect[0], 1));
            Assert.Throws<NotSupportedException>(() =>
                rules.ResolveBasic(Attack(), Enemy(50, "normal"),
                    new [] { new CombatEffect("buff", CombatEffectType.ArmorReduction,
                        CombatEffectOrigin.TriggeredSkill, 10, 3) }, 0));
        }

        [Test]
        public void MissingAttackTypeMappingFailsRatherThanInventingDamage()
        {
            var source = new FixtureHits { Supported = false };
            var rules = new Ordr323OriginalDamageRules(
                source, null, new FixtureTruncation());
            Assert.Throws<NotSupportedException>(() =>
                rules.ResolveBasic(Attack(), Enemy(50, "normal"),
                    new CombatEffect[0], 0));
        }

        [Test]
        public void ScriptHealthFactorMustNotBeMisusedAsUniversalMagicResistance()
        {
            Assert.AreEqual(0.79,
                Ordr323MapDamageMath.ScriptHealthProportionalCorrection(0), 1e-9);
            Assert.AreEqual(0.84,
                Ordr323MapDamageMath.ScriptHealthProportionalCorrection(5), 1e-9);
        }
    }
}
