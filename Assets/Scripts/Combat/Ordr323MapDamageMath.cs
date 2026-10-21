using System;

namespace Ordr.Mobile.Combat
{
    // 직접 추출한 ORDR_S2_2.323[C].w3x의 war3mapMisc.txt:
    // map sha256 0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c
    // misc sha256 131d8ca9c3b978d404a325a1f6f34ec79e97c8fc90cef491dab2608180df3223
    // 전투 계산용 값만 전사한다. 워크래프트 기본 0.06이 아니라 맵 고유 0.02다.
    public enum Ordr323AttackType
    {
        Spells, // JASS ATTACK_TYPE_NORMAL (0): DamageBonusSpells
        Melee,  // JASS ATTACK_TYPE_MELEE (1): DamageBonusNormal
        Pierce, Siege, Magic, Chaos, Hero
    }

    // war3mapMisc.txt DamageBonus* 8개 열의 실제 순서.
    public enum Ordr323ArmorType
    {
        Small, Medium, Large, Fortified, Normal, Hero, Divine, Unarmored
    }

    public static class Ordr323MapDamageMath
    {
        public const string GameVersion = "S2 2.323[C]";
        public const double ArmorCoefficient = 0.02;
        public const double NegativeArmorBase = 0.94;
        public const double NegativeArmorMultiplierCap = 1.71;

        // 원본 DamageBonus* 문자열 값을 변경하지 않고 표 행마다 순서대로 보존.
        private static readonly double[][] AttackTable = new double[][]
        {
            new double[] { 1.00, 1.00, 0.85, 0.90, 1.00, 0.80, 0.90, 0.40 }, // Spells
            new double[] { 1.00, 1.00, 1.00, 0.75, 1.25, 1.00, 0.90, 0.80 }, // Melee
            new double[] { 1.00, 1.00, 1.25, 1.00, 0.75, 0.90, 1.00, 0.60 }, // Pierce
            new double[] { 1.00, 1.00, 0.75, 1.25, 1.00, 0.80, 1.00, 0.60 }, // Siege
            new double[] { 1.00, 1.00, 1.00, 1.00, 1.00, 0.80, 0.90, 0.40 }, // Magic
            new double[] { 1.00, 1.00, 1.00, 1.00, 1.00, 1.00, 1.00, 0.50 }, // Chaos
            new double[] { 1.05, 1.05, 1.05, 1.05, 1.05, 1.05, 1.05, 1.05 }, // Hero
        };

        public static double AttackVersusArmor(Ordr323AttackType attack,
            Ordr323ArmorType armor)
        {
            if (!Enum.IsDefined(typeof(Ordr323AttackType), attack))
                throw new ArgumentOutOfRangeException(nameof(attack));
            if (!Enum.IsDefined(typeof(Ordr323ArmorType), armor))
                throw new ArgumentOutOfRangeException(nameof(armor));
            return AttackTable[(int)attack][(int)armor];
        }

        // 원본 JASS BdV(8434~8457)의 방어력 역산 분기와 일치하도록 작성.
        // 양수는 war3mapMisc.txt DefenseArmor=0.02 사용.
        // 음수 0.94/1.71은 BdV에서 직접 확인. 엔진 자체 결과는 실기 검증 전.
        public static double ArmorDamageMultiplier(double armor)
        {
            if (double.IsNaN(armor) || double.IsInfinity(armor))
                throw new ArgumentOutOfRangeException(nameof(armor));
            if (armor >= 0)
                return 1.0 / (1.0 + ArmorCoefficient * armor);
            double value = 2.0 - Math.Pow(NegativeArmorBase, -armor);
            return Math.Min(value, NegativeArmorMultiplierCap);
        }

        // 이것은 원본 최종 공식이 아니라 확인된 게임 상수의 *중간 단계*.
        // 마법 저항, 증폭/중첩/크리티컬, 피해 유형별 방어 무시는 외부 근거 필요.
        public static double ApplyVerifiedFactors(double rawDamage,
            Ordr323AttackType attack, Ordr323ArmorType armorType,
            bool useAttackTable, bool useArmorRating, double armorRating,
            bool useMagicResistance, double magicResistancePercent,
            double verifiedAmplificationFactor)
        {
            if (double.IsNaN(rawDamage) || double.IsInfinity(rawDamage) ||
                rawDamage < 0)
                throw new ArgumentOutOfRangeException(nameof(rawDamage));
            if (double.IsNaN(verifiedAmplificationFactor) ||
                double.IsInfinity(verifiedAmplificationFactor) ||
                verifiedAmplificationFactor < 0)
                throw new ArgumentOutOfRangeException(nameof(verifiedAmplificationFactor));
            if (useMagicResistance &&
                (double.IsNaN(magicResistancePercent) ||
                 double.IsInfinity(magicResistancePercent) ||
                 magicResistancePercent > 100))
                throw new ArgumentOutOfRangeException(nameof(magicResistancePercent));
            double total = rawDamage;
            if (useAttackTable) total *= AttackVersusArmor(attack, armorType);
            if (useArmorRating) total *= ArmorDamageMultiplier(armorRating);
            if (useMagicResistance) total *= 1.0 - magicResistancePercent / 100.0;
            total *= verifiedAmplificationFactor;
            if (double.IsNaN(total) || double.IsInfinity(total) || total < 0)
                throw new OverflowException("피해량 계산 결과가 유효 범위를 벗어났습니다.");
            return total;
        }

        // 2.323[C] 원본 일부 "적 현재/최대 HP 비례" 스킬의 조건부 보정:
        // JASS 15428, 17045, 17329 등에서 (.79 + .01 * abilityLevel(A11S)).
        // 이것은 개별 스킬에서 명시적으로 사용하는 보정이며
        // 일반 평타/전체 마법피해에 자동 적용하지 않는다.
        public static double ScriptHealthProportionalCorrection(int targetAbilityLevel)
        {
            if (targetAbilityLevel < 0)
                throw new ArgumentOutOfRangeException(nameof(targetAbilityLevel));
            return 0.79 + 0.01 * targetAbilityLevel;
        }

        // Bcb/Bcc: Hx * BFj(Hy,Hz). BFj는 최소값+절대차*BFh()로 계산.
        // BFh 분포/시드와 실제 타격별 난수 순서는 별도 구현 검증 대상.
        public static double ScriptDamageBeforeDefense(double baseDamage,
            double lowerMultiplier, double upperMultiplier, double uniformRoll)
        {
            if (double.IsNaN(baseDamage) || double.IsInfinity(baseDamage) ||
                baseDamage < 0 || double.IsNaN(lowerMultiplier) ||
                double.IsInfinity(lowerMultiplier) || lowerMultiplier < 0 ||
                double.IsNaN(upperMultiplier) || double.IsInfinity(upperMultiplier) ||
                upperMultiplier < 0 || double.IsNaN(uniformRoll) ||
                double.IsInfinity(uniformRoll) || uniformRoll < 0 ||
                uniformRoll >= 1)
                throw new ArgumentOutOfRangeException(nameof(uniformRoll));
            return baseDamage *
                (Math.Min(lowerMultiplier, upperMultiplier) +
                 Math.Abs(upperMultiplier - lowerMultiplier) * uniformRoll);
        }
    }
}
