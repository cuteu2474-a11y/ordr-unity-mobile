using System;

namespace Ordr.Mobile.Combat
{
    // Source-model calculation only: the original game can resolve additional
    // native immunity, Defend and damage-stage modifiers at the impact moment.
    // Attack type (DamageBonus*) is deliberately independent of damage type.
    public struct SourceModelScriptDamageFactors
    {
        public Ordr323ArmorType ArmorType { get; private set; }
        public bool UseNumericArmor { get; private set; }
        public double NumericArmor { get; private set; }
        public bool UseMagicResistance { get; private set; }
        public double MagicResistancePercent { get; private set; }

        internal SourceModelScriptDamageFactors(Ordr323ArmorType armor,
            bool useNumericArmor, double numericArmor,
            bool useMagicResistance, double magicResistancePercent)
        {
            ArmorType = armor;
            UseNumericArmor = useNumericArmor;
            NumericArmor = numericArmor;
            UseMagicResistance = useMagicResistance;
            MagicResistancePercent = magicResistancePercent;
        }

        public double Apply(double sourceDamage, Ordr323AttackType attack)
        {
            return Ordr323MapDamageMath.ApplyVerifiedFactors(
                sourceDamage, attack, ArmorType, true,
                UseNumericArmor, NumericArmor,
                UseMagicResistance, MagicResistancePercent, 1);
        }
    }

    public static class SourceModelScriptDamageTypeRules
    {
        // Run before the skill's damage RNG. Missing defenses cannot spend a
        // script roll, deal a physical hit instead or silently become zero MR.
        public static bool TryPrepare(SourceScriptHit hit, CombatEnemy enemy,
            out SourceModelScriptDamageFactors factors, out string reason)
        {
            factors = default(SourceModelScriptDamageFactors);
            reason = null;
            if (hit == null || enemy == null)
                throw new ArgumentNullException(hit == null ? nameof(hit) : nameof(enemy));

            var defense = enemy.Defenses;
            Ordr323ArmorType armor;
            if (defense == null ||
                !Ordr323OriginalDamageRules.TryReadArmorType(
                    defense.WarcraftArmorType, out armor))
            {
                reason = "공격 유형에 필요한 방어 유형 근거 없음";
                return false;
            }

            bool useArmor = hit.UsesArmorRating;
            double armorRating = 0;
            if (useArmor)
            {
                if (!defense.BaseArmor.HasValue)
                {
                    reason = "물리 피해용 수치 방어력 근거 없음";
                    return false;
                }
                armorRating = defense.BaseArmor.Value -
                    SourceModelUnitEffects.ResolvePracticeArmorReduction(
                        enemy.Status.Snapshot());
            }

            bool useMagic = hit.UsesMagicResistance;
            double resistance = 0;
            if (useMagic)
            {
                double pinned;
                if (!defense.HasVerifiedMagicResistance ||
                    !defense.MagicAbilityLevel.HasValue ||
                    !Ordr323VerifiedDefenseAbilities.TryGetMagicResistancePercent(
                        defense.MagicAbilityLevel.Value, out pinned) ||
                    Math.Abs(pinned - defense.MagicResistancePercent.Value) > 0.0001)
                {
                    reason = "원본 A04Q 마법 저항 레벨/피격시점 근거 없음";
                    return false;
                }
                // Dynamic spell-resistance reduction and amplification have
                // independently ordered native effects. Never guess that
                // their tooltip percentages simply add or multiply.
                foreach (var effect in enemy.Status.Snapshot())
                {
                    if (effect.Type == CombatEffectType.MagicResistanceReduction ||
                        effect.Type == CombatEffectType.OriginalA04QLevelIncrease ||
                        effect.Type == CombatEffectType.MagicDamageAmplification ||
                        effect.Type == CombatEffectType.AllDamageAmplification)
                    {
                        reason = "마법 저항/증폭 중첩의 원본 최종 피해식 미확인";
                        return false;
                    }
                }
                resistance = pinned;
            }
            factors = new SourceModelScriptDamageFactors(
                armor, useArmor, armorRating, useMagic, resistance);
            return true;
        }
    }
}
