using System;

namespace Ordr.Mobile.Combat
{
    // 공격 계열·방어 공식 계산기에 필요한 적의 원시 스탯.
    // 미확인 값은 반드시 null이며, 0방어/0저항으로 취급하면 안 된다.
    public sealed class EnemyDefenseProfile
    {
        public CharacterEnemyKind? Kind { get; private set; }
        public double? BaseArmor { get; private set; }
        public double? MagicResistancePercent { get; private set; }
        public string WarcraftArmorType { get; private set; }
        public string Source { get; private set; }

        public bool HasVerifiedArmor
        {
            get { return BaseArmor.HasValue && !string.IsNullOrWhiteSpace(WarcraftArmorType) &&
                         !string.IsNullOrWhiteSpace(Source); }
        }

        public bool HasVerifiedMagicResistance
        {
            get { return MagicResistancePercent.HasValue && !string.IsNullOrWhiteSpace(Source); }
        }

        public EnemyDefenseProfile(CharacterEnemyKind? kind, double? baseArmor,
            double? magicResistancePercent, string warcraftArmorType, string source)
        {
            if (baseArmor.HasValue && (double.IsNaN(baseArmor.Value) || double.IsInfinity(baseArmor.Value)))
                throw new ArgumentOutOfRangeException(nameof(baseArmor));
            if (magicResistancePercent.HasValue &&
                (double.IsNaN(magicResistancePercent.Value) ||
                 double.IsInfinity(magicResistancePercent.Value)))
                throw new ArgumentOutOfRangeException(nameof(magicResistancePercent));
            if (kind.HasValue && !Enum.IsDefined(typeof(CharacterEnemyKind), kind.Value))
                throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind;
            BaseArmor = baseArmor;
            MagicResistancePercent = magicResistancePercent;
            WarcraftArmorType = warcraftArmorType;
            Source = source;
        }

        public static EnemyDefenseProfile Unknown
        {
            get { return new EnemyDefenseProfile(null, null, null, null, null); }
        }

        public static EnemyDefenseProfile UnverifiedKind(CharacterEnemyKind kind)
        {
            return new EnemyDefenseProfile(kind, null, null, null, null);
        }
    }
}
