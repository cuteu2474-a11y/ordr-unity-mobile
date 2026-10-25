using System;

namespace Ordr.Mobile.Core
{
    // 2.323 악몽 도감의 오로성 저주 원자료를 수치로 보관한다.
    // 실제 적용 시점, 중첩, 체력 보정 대상은 추가 검증이 필요하다.
    public readonly struct ElderCurseModifiers
    {
        public readonly double EnemyMoveSpeedMultiplier;
        public readonly double AllyAttackSpeedMultiplier;
        public readonly double AllyAttackDamageMultiplier;
        public readonly double ExplosionDamageMultiplier;
        public readonly int EnemyArmorBonus;
        public readonly double EnemyMagicResistanceBonusPercent;
        public readonly long LaneEnemyBonusHealth;
        public readonly long BossBonusHealth;
        public readonly long EnemyHealthRegenerationPerSecond;

        public ElderCurseModifiers(double enemyMoveSpeedMultiplier,
            double allyAttackSpeedMultiplier, double allyAttackDamageMultiplier,
            double explosionDamageMultiplier, int enemyArmorBonus,
            double enemyMagicResistanceBonusPercent, long laneEnemyBonusHealth,
            long bossBonusHealth, long enemyHealthRegenerationPerSecond)
        {
            EnemyMoveSpeedMultiplier = enemyMoveSpeedMultiplier;
            AllyAttackSpeedMultiplier = allyAttackSpeedMultiplier;
            AllyAttackDamageMultiplier = allyAttackDamageMultiplier;
            ExplosionDamageMultiplier = explosionDamageMultiplier;
            EnemyArmorBonus = enemyArmorBonus;
            EnemyMagicResistanceBonusPercent = enemyMagicResistanceBonusPercent;
            LaneEnemyBonusHealth = laneEnemyBonusHealth;
            BossBonusHealth = bossBonusHealth;
            EnemyHealthRegenerationPerSecond = enemyHealthRegenerationPerSecond;
        }
    }

    public static class ElderCurseCatalog
    {
        public static ElderCurseModifiers Get(ElderCurse curse)
        {
            switch (curse)
            {
                case ElderCurse.None:
                    return new ElderCurseModifiers(1, 1, 1, 1, 0, 0, 0, 0, 0);
                case ElderCurse.Nusjuro:
                    return new ElderCurseModifiers(1.15, 0.85, 1, 1, 0, 0, 15000000, 0, 0);
                case ElderCurse.Warcury:
                    // Descriptive original effect. Do NOT add +15% on top of
                    // an A04Q magic resistance already using NJ/LK -15.
                    return new ElderCurseModifiers(1, 1, 1, 1, 15, 15, 0, 15000000, 0);
                case ElderCurse.Saturn:
                    return new ElderCurseModifiers(1, 1, 0.70, 0.90, 0, 0, 0, 0, 350000);
                default: throw new ArgumentOutOfRangeException(nameof(curse));
            }
        }
    }
}
