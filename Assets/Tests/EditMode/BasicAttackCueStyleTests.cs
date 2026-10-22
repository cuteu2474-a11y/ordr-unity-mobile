using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class BasicAttackCueStyleTests
    {
        [Test]
        public void MapMissilesGetProjectileCueWithoutInventingMagicalDamage()
        {
            Assert.AreEqual(BasicAttackCueKind.Projectile,
                BasicAttackCueStyle.Choose("missile", null));
            Assert.AreEqual(BasicAttackCueKind.Projectile,
                BasicAttackCueStyle.Choose("msplash", DamageSchool.Physical));
        }

        [Test]
        public void VerifiedMagicDamageUsesMagicCueEvenWithInstantWeapon()
        {
            Assert.AreEqual(BasicAttackCueKind.Magic,
                BasicAttackCueStyle.Choose("instant", DamageSchool.Magical));
            Assert.AreEqual(BasicAttackCueKind.Magic,
                BasicAttackCueStyle.Choose("missile", DamageSchool.Magical));
        }

        [Test]
        public void UnknownOrInstantAttackGetsNeutralSlashNotAssumedMagic()
        {
            Assert.AreEqual(BasicAttackCueKind.Slash,
                BasicAttackCueStyle.Choose("instant", null));
            Assert.AreEqual(BasicAttackCueKind.Slash,
                BasicAttackCueStyle.Choose("normal", DamageSchool.ScriptDefined));
            Assert.AreEqual(BasicAttackCueKind.Slash,
                BasicAttackCueStyle.Choose(null, null));
        }
    }
}
