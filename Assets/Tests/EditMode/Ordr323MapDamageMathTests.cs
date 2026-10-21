using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    // 테스트 스펙 숫자는 방어계수·행렬 검증용, 원작 특정 캐릭터 DPS가 아니다.
    // 작성만 했으며 Unity NUnit 실행은 별도로 검증해야 한다.
    public class Ordr323MapDamageMathTests
    {
        [Test]
        public void MapArmorCoefficientIsTwoPercentNotSixPercent()
        {
            Assert.AreEqual(0.02, Ordr323MapDamageMath.ArmorCoefficient);
            Assert.AreEqual(0.5, Ordr323MapDamageMath.ArmorDamageMultiplier(50), 1e-12);
            Assert.AreEqual(1.0, Ordr323MapDamageMath.ArmorDamageMultiplier(0), 1e-12);
        }

        [Test]
        public void NegativeArmorUsesRecoveredJassCap()
        {
            Assert.AreEqual(2.0 - Math.Pow(0.94, 10),
                Ordr323MapDamageMath.ArmorDamageMultiplier(-10), 1e-12);
            Assert.AreEqual(1.71,
                Ordr323MapDamageMath.ArmorDamageMultiplier(-100), 1e-12);
        }

        [Test]
        public void MapAttackRowsAreNotStockWarcraftTables()
        {
            Assert.AreEqual(0.85, Ordr323MapDamageMath.AttackVersusArmor(
                Ordr323AttackType.Spells, Ordr323ArmorType.Large));
            Assert.AreEqual(1.25, Ordr323MapDamageMath.AttackVersusArmor(
                Ordr323AttackType.Melee, Ordr323ArmorType.Normal));
            Assert.AreEqual(1.25, Ordr323MapDamageMath.AttackVersusArmor(
                Ordr323AttackType.Pierce, Ordr323ArmorType.Large));
            Assert.AreEqual(0.50, Ordr323MapDamageMath.AttackVersusArmor(
                Ordr323AttackType.Chaos, Ordr323ArmorType.Unarmored));
            Assert.AreEqual(1.05, Ordr323MapDamageMath.AttackVersusArmor(
                Ordr323AttackType.Hero, Ordr323ArmorType.Divine));
        }

        [Test]
        public void AllVerifiedMathStagesComposeWithoutHiddenDefaultResistance()
        {
            Assert.AreEqual(500, Ordr323MapDamageMath.ApplyVerifiedFactors(
                1000, Ordr323AttackType.Chaos, Ordr323ArmorType.Normal,
                true, true, 50, false, 0, 1), 1e-9);
            Assert.AreEqual(300, Ordr323MapDamageMath.ApplyVerifiedFactors(
                1000, Ordr323AttackType.Chaos, Ordr323ArmorType.Normal,
                true, true, 50, true, 40, 1), 1e-9);
        }

        [Test]
        public void BcbDamageUsesMappedRandomInterval()
        {
            Assert.AreEqual(1200, Ordr323MapDamageMath.ScriptDamageBeforeDefense(
                1000, 1, 1.5, 0.4), 1e-9);
            Assert.AreEqual(1200, Ordr323MapDamageMath.ScriptDamageBeforeDefense(
                1000, 1.5, 1, 0.4), 1e-9);
        }
    }
}
