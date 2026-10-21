using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // 직접 추출한 2.323[C] war3map.w3a 원본 오브젝트 덮어쓰기만 반영한다.
    // 원본 w3a에는 레벨 1~4의 값이 없다: 표를 외삽해 '확정'으로 표시하지 않는다.
    // Idef: 아이템 방어력 증감 / Def5: Defend의 마법 피해 수령 배율.
    public static class Ordr323VerifiedDefenseAbilities
    {
        public const string ArmorBreakAbility = "A04M";
        public const string MagicDefenseAbility = "A04Q";

        public static bool TryGetArmorChange(string abilityRawcode, int level,
            out double armorChange)
        {
            armorChange = 0;
            if (level < 5 || string.IsNullOrWhiteSpace(abilityRawcode)) return false;

            switch (abilityRawcode)
            {
                case "A04M": // w3a Idef[5..76] = -4..-75
                    if (level > 76) return false;
                    armorChange = 1 - level;
                    return true;
                case "A04P": // w3a Idef[5..41] = -4..-40
                    if (level > 41) return false;
                    armorChange = 1 - level;
                    return true;
                case "A04N": // w3a Idef[5..11] = -12..-30
                    if (level > 11) return false;
                    armorChange = 3 * (1 - level);
                    return true;
                case "A04O": // w3a Idef[5..11] = -85..-115
                    if (level > 11) return false;
                    armorChange = -5 * (level + 12);
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryGetMagicDamageTakenFactor(int abilityLevel,
            out double damageTakenFactor)
        {
            damageTakenFactor = 0;
            // w3a A04Q Def5[5..41] : 0.79, 0.80, ..., 1.15
            if (abilityLevel < 5 || abilityLevel > 41) return false;
            // w3a 필드는 float32이므로 같은 자료형으로 반올림한 값을 반환.
            damageTakenFactor = (double)(float)(0.74 + 0.01 * abilityLevel);
            return true;
        }

        public static bool TryGetMagicResistancePercent(int abilityLevel,
            out double resistance)
        {
            resistance = 0;
            double taken;
            if (!TryGetMagicDamageTakenFactor(abilityLevel, out taken)) return false;
            resistance = (1 - taken) * 100;
            return true;
        }

        // 레벨은 war3map.j CUM 난이도 분기에서 가져온 것만 허용한다.
        // Nightmare/Otherworld: LK[JG]=16, NJ=16 (2026-09-24 원본).
        public static bool TryGetNightmareBaselineResistance(out double resistance)
        {
            return TryGetMagicResistancePercent(16, out resistance);
        }

        // W3X의 모드/아이템/스토리 조건에 따라 A04Q 능력 레벨이 달라진다.
        // 이 팩토리는 적에게 적용되는 현재 *확인된* 레벨을 받은 경우에만 사용.
        public static EnemyDefenseProfile FromVerifiedMagicAbilityLevel(
            CharacterEnemyKind kind, double? preAbilityArmor,
            string armorType, int currentA04QLevel, string evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence))
                throw new ArgumentException("원본 몬스터 방어/능력 레벨 출처 필요");
            double magicResistance;
            if (!TryGetMagicResistancePercent(currentA04QLevel, out magicResistance))
                throw new NotSupportedException(
                    "A04Q 레벨에 대한 원본 Def5 데이터 미확인: " + currentA04QLevel);
            return new EnemyDefenseProfile(kind, preAbilityArmor,
                magicResistance, armorType,
                evidence + " / W3X:A04Q:Def5:level=" + currentA04QLevel);
        }

        // A04M은 JASS Bdq로 능력 레벨을 증가시키고
        // 스킬 피해식에서는 'GetUnitAbilityLevel(target,Hl)-1'을 스택으로 사용.
        // 스택 4..75는 w3a 레벨 5..76이므로 방어 감소 값이 직접 확인됨.
        // 1..3 스택은 원본 오브젝트 *덮어쓰기*에 미포함: 임의 보정 금지.
        public static bool TryGetArmorBreakFromStacks(int stacks, out double adjustment)
        {
            adjustment = 0;
            if (stacks == 0) return true;
            if (stacks < 0 || stacks >= int.MaxValue) return false;
            return TryGetArmorChange(ArmorBreakAbility, stacks + 1, out adjustment);
        }

    }

    // 실제 적용되는 원본 방어 감소가 *아머브레이크 A04M 하나일 때만*
    // 레벨별 Idef 값을 계산한다. 오라/발동형 방깎과 마방깎 중첩은
    // 현재 원본 자료에서 확인되지 않아 false로 차단한다.
    public sealed class Ordr323VerifiedPartialModifierRules : IOrdr323VerifiedModifierRules
    {
        private readonly Func<string, int, bool> originalA04MStackProvenance;

        // 전투 런타임의 'armorBreak' 도감 태그만으로는 A04M 사용이
        // 증명되지 않는다. 실제 JASS Bdq(A04M) 매핑된 이벤트만 승인한다.
        public Ordr323VerifiedPartialModifierRules(
            Func<string, int, bool> verifiedA04MStackProvenance = null)
        {
            originalA04MStackProvenance = verifiedA04MStackProvenance;
        }

        public bool TryResolveArmor(CombatEnemy target,
            IReadOnlyList<CombatEffect> effects, int armorBreakStacks,
            out double effectiveArmor)
        {
            effectiveArmor = 0;
            if (target == null || effects == null ||
                !target.Defenses.HasVerifiedArmor)
                return false;
            foreach (var effect in effects)
                if (effect.Type == CombatEffectType.ArmorReduction ||
                    effect.Type == CombatEffectType.ArmorBreakStack)
                    return false;

            // 수치 표 자체는 확인했지만 도감 스택의 출처가 다르면
            // 그 수치를 현재 적에게 적용하면 안 된다.
            if (armorBreakStacks > 0 &&
                (originalA04MStackProvenance == null ||
                 !originalA04MStackProvenance(target.Id, armorBreakStacks)))
                return false;
            double change;
            if (!Ordr323VerifiedDefenseAbilities.TryGetArmorBreakFromStacks(
                armorBreakStacks, out change))
                return false;
            effectiveArmor = target.Defenses.BaseArmor.Value + change;
            return !double.IsNaN(effectiveArmor) && !double.IsInfinity(effectiveArmor);
        }

        public bool TryResolveMagicResistancePercent(CombatEnemy target,
            IReadOnlyList<CombatEffect> effects, out double effectivePercent)
        {
            effectivePercent = 0;
            // A04Q 능력 레벨 직접 주입은 EnemyDefenseProfile에서 이미 반영.
            // 스킬의 MagicResistanceReduction 필드만으로 실제 레벨 변경을
            // 예측할 수 없으므로 추가 버프가 있는 경우 무조건 보류.
            return false;
        }

        public bool TryResolveAmplification(CombatEnemy target,
            IReadOnlyList<CombatEffect> effects,
            Ordr323VerifiedDamageHit hit, out double multiplier)
        {
            multiplier = 0;
            // JASS BdV의 /1.1 /1.25 /1.2는 역산용 보정이다.
            // 실제 스킬 증폭의 최종 순서/대상에 그대로 곱하지 않는다.
            return false;
        }
    }
}
