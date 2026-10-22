using System;

namespace Ordr.Mobile.Combat
{
    // Presentation only. This never selects damage type, damage amount, range,
    // attack timing or whether an original JASS effect may be executed.
    public enum BasicAttackCueKind { Slash, Projectile, Magic }

    public static class BasicAttackCueStyle
    {
        public static BasicAttackCueKind Choose(string originalWeaponType,
            DamageSchool? independentlyVerifiedSchool)
        {
            // A Warcraft map 'hero', 'siege' or 'pierce' attack type does not
            // prove a magical damage formula. Only a verified spec may do so.
            if (independentlyVerifiedSchool == DamageSchool.Magical)
                return BasicAttackCueKind.Magic;
            if (string.Equals(originalWeaponType, "missile", StringComparison.Ordinal) ||
                string.Equals(originalWeaponType, "msplash", StringComparison.Ordinal))
                return BasicAttackCueKind.Projectile;
            return BasicAttackCueKind.Slash;
        }
    }
}
