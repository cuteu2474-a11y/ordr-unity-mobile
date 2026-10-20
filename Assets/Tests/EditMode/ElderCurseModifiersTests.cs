using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public class ElderCurseModifiersTests
    {
        [Test]
        public void NusjuroAffectsMovementAttackSpeedAndLaneHealth()
        {
            var effect = ElderCurseCatalog.Get(ElderCurse.Nusjuro);
            Assert.AreEqual(1.15, effect.EnemyMoveSpeedMultiplier, 1e-9);
            Assert.AreEqual(0.85, effect.AllyAttackSpeedMultiplier, 1e-9);
            Assert.AreEqual(15000000L, effect.LaneEnemyBonusHealth);
        }

        [Test]
        public void WarcuryAffectsDefenseAndBossHealth()
        {
            var effect = ElderCurseCatalog.Get(ElderCurse.Warcury);
            Assert.AreEqual(15, effect.EnemyArmorBonus);
            Assert.AreEqual(15, effect.EnemyMagicResistanceBonusPercent, 1e-9);
            Assert.AreEqual(15000000L, effect.BossBonusHealth);
        }

        [Test]
        public void SaturnAffectsDamageAndRegeneration()
        {
            var effect = ElderCurseCatalog.Get(ElderCurse.Saturn);
            Assert.AreEqual(0.70, effect.AllyAttackDamageMultiplier, 1e-9);
            Assert.AreEqual(0.90, effect.ExplosionDamageMultiplier, 1e-9);
            Assert.AreEqual(350000L, effect.EnemyHealthRegenerationPerSecond);
        }

        [Test]
        public void NoCurseDoesNotModifyStats()
        {
            var effect = ElderCurseCatalog.Get(ElderCurse.None);
            Assert.AreEqual(1, effect.AllyAttackDamageMultiplier);
            Assert.AreEqual(0L, effect.BossBonusHealth);
        }
    }
}
