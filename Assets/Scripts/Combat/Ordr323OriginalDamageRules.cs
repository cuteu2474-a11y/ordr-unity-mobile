using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // 2.323[C] JASS에는 ATTACK_TYPE_CHAOS/DAMAGE_TYPE_NORMAL,
    // ATTACK_TYPE_NORMAL/DAMAGE_TYPE_UNIVERSAL 등 조합이 다양하다.
    // 물리/마법/고정 이름만으로 armor/저항 적용 여부를 추정하지 않는다.
    // 각 공격의 실제 원본 JASS 또는 오브젝트 근거로 결정할 것.
    public sealed class Ordr323VerifiedDamageHit
    {
        public double RawDamage { get; private set; }
        public Ordr323AttackType AttackType { get; private set; }
        public bool UseAttackTable { get; private set; }
        public bool UseArmorRating { get; private set; }
        public bool UseMagicResistance { get; private set; }
        public DamageTraits Traits { get; private set; }
        public DamageSchool School { get; private set; }
        public string Evidence { get; private set; }

        public Ordr323VerifiedDamageHit(double rawDamage,
            Ordr323AttackType attackType, bool useAttackTable,
            bool useArmorRating, bool useMagicResistance,
            DamageTraits traits, string evidence,
            DamageSchool school = DamageSchool.ScriptDefined)
        {
            if (double.IsNaN(rawDamage) || double.IsInfinity(rawDamage) ||
                rawDamage < 0)
                throw new ArgumentOutOfRangeException(nameof(rawDamage));
            if (!Enum.IsDefined(typeof(Ordr323AttackType), attackType))
                throw new ArgumentOutOfRangeException(nameof(attackType));
            if ((traits & ~DamageTraits.Explosive) != DamageTraits.None)
                throw new ArgumentOutOfRangeException(nameof(traits));
            if (!Enum.IsDefined(typeof(DamageSchool), school))
                throw new ArgumentOutOfRangeException(nameof(school));
            if (string.IsNullOrWhiteSpace(evidence))
                throw new ArgumentException("원작 공격 타입/피해 공식의 출처가 필요합니다.");
            RawDamage = rawDamage;
            AttackType = attackType;
            UseAttackTable = useAttackTable;
            UseArmorRating = useArmorRating;
            UseMagicResistance = useMagicResistance;
            Traits = traits;
            School = school;
            Evidence = evidence;
        }
    }

    // 개별 공격/스킬의 '방어력 적용 대상 여부'는 원본 호출 위치에서 결정.
    // 원본 BFj 난수와 공격력 주사위 값, HP비례/속성/특성 스케일링은 이 공급자가 계산한다.
    public interface IOrdr323VerifiedHitSource
    {
        bool TryResolveBasic(BasicDamageRequest request,
            CombatEnemy enemy, out Ordr323VerifiedDamageHit hit);

        bool TryResolveSkill(CharacterSkillDamageRequest request,
            out Ordr323VerifiedDamageHit hit);
    }

    // 사전 확인은 RNG/체력/마나를 소비하면 안 된다.
    // 실제 타깃에 대한 세부 유효성은 최종 ResolveSkill에서도 다시 검사한다.
    // 가상 스킬 데이터를 '검증 완료'로 임의 승격하지 않는다.
    public interface IOrdr323VerifiedSkillLookup
    {
        bool CanResolveSkillComponent(CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect effect);
    }

    // 방깎, 아머브레이크, 마방깎, 증폭 등의 효과가 하나라도 영향이 있으면
    // 2.323[C]에서 확인된 중첩/제한/적용 순서 규칙이 반드시 처리해야 한다.
    public interface IOrdr323VerifiedModifierRules
    {
        bool TryResolveArmor(CombatEnemy enemy,
            IReadOnlyList<CombatEffect> effects, int armorBreakStacks,
            out double effectiveArmor);
        bool TryResolveMagicResistancePercent(CombatEnemy enemy,
            IReadOnlyList<CombatEffect> effects, out double effectivePercent);
        bool TryResolveAmplification(CombatEnemy enemy,
            IReadOnlyList<CombatEffect> effects,
            Ordr323VerifiedDamageHit hit, out double multiplier);
    }

    // Warcraft III 실수 피해를 현재 프로젝트의 long HP에 반영하는 최종 변환.
    // 워크3 엔진과 일치하는 반올림/절사 규칙이 검증되기 전에는 임의로 정하지 않는다.
    public interface IOrdr323VerifiedDamageQuantization
    {
        long Quantize(double finalDamage, Ordr323VerifiedDamageHit hit);
    }

    // 현재 구현된 CombatDamageRulesAdapter에 직접 연결 가능한 정책.
    // 피해량 계산에는 2.323[C]의 명시적인 상수만 사용하며,
    // 스킬 종류별 방어 관여/버프 중첩/반올림은 미확인 시 공격을 거부한다.
    public sealed class Ordr323OriginalDamageRules : IOriginalCombatDamageRules,
        IOriginalSkillDamagePreflight
    {
        private readonly IOrdr323VerifiedHitSource hits;
        private readonly IOrdr323VerifiedModifierRules modifiers;
        private readonly IOrdr323VerifiedDamageQuantization quantizer;

        public Ordr323OriginalDamageRules(IOrdr323VerifiedHitSource verifiedHits,
            IOrdr323VerifiedModifierRules originalModifiers,
            IOrdr323VerifiedDamageQuantization verifiedQuantizer)
        {
            hits = verifiedHits ?? throw new ArgumentNullException(nameof(verifiedHits));
            modifiers = originalModifiers;
            quantizer = verifiedQuantizer ??
                throw new ArgumentNullException(nameof(verifiedQuantizer));
        }

        public bool CanResolveSkillDamage(CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect effect)
        {
            var lookup = hits as IOrdr323VerifiedSkillLookup;
            return lookup != null && profile != null && skill != null &&
                effect != null &&
                lookup.CanResolveSkillComponent(profile, skill, effect);
        }

        public long ResolveBasic(BasicDamageRequest request, CombatEnemy target,
            IReadOnlyList<CombatEffect> targetEffects, int armorBreakStacks)
        {
            if (request == null || target == null)
                throw new ArgumentNullException(request == null ? nameof(request) : nameof(target));
            Ordr323VerifiedDamageHit hit;
            if (!hits.TryResolveBasic(request, target, out hit) || hit == null)
                throw new NotSupportedException(
                    "2.323[C] 평타 공격 타입/주사위/피해 경로 미확인: " + request.UnitRawCode);
            return Calculate(target, hit, targetEffects, armorBreakStacks);
        }

        public long ResolveSkill(CharacterSkillDamageRequest request,
            IReadOnlyList<CombatEffect> targetEffects)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            Ordr323VerifiedDamageHit hit;
            if (!hits.TryResolveSkill(request, out hit) || hit == null)
                throw new NotSupportedException(
                    "2.323[C] 스킬 원본 피해 타입/공식 미확인: " +
                    request.CasterProfile.UnitId + "/" + request.Skill.Name);
            return Calculate(request.Target.Enemy, hit, targetEffects,
                request.TargetArmorBreakStacks);
        }

        private long Calculate(CombatEnemy target, Ordr323VerifiedDamageHit hit,
            IReadOnlyList<CombatEffect> effects, int armorBreakStacks)
        {
            if (effects == null) throw new ArgumentNullException(nameof(effects));
            var defense = target.Defenses;
            Ordr323ArmorType armorType = Ordr323ArmorType.Normal;
            // type이 필요한 요청이라면 방어 타입 미확인을 Normal로 간주하지 않는다.
            if (hit.UseAttackTable)
            {
                if (string.IsNullOrWhiteSpace(defense.WarcraftArmorType) ||
                    string.IsNullOrWhiteSpace(defense.Source) ||
                    !TryReadArmorType(defense.WarcraftArmorType, out armorType))
                    throw new NotSupportedException(
                        "2.323[C] 적 방어 타입/출처 미확인: " + target.Id);
            }

            double armor = 0;
            if (hit.UseArmorRating)
            {
                if (!defense.HasVerifiedArmor)
                    throw new NotSupportedException("적 방어력 미확인: " + target.Id);
                armor = defense.BaseArmor.Value;
                if (armorBreakStacks != 0 || Has(effects, CombatEffectType.ArmorReduction) ||
                    Has(effects, CombatEffectType.ArmorBreakStack))
                {
                    if (modifiers == null || !modifiers.TryResolveArmor(
                        target, effects, armorBreakStacks, out armor))
                        throw new NotSupportedException(
                            "2.323[C] 방깎·아머브레이크 중첩 수식 미연결");
                }
            }

            double resistance = 0;
            if (hit.UseMagicResistance)
            {
                if (!defense.HasVerifiedMagicResistance)
                    throw new NotSupportedException("적 마법저항률 미확인: " + target.Id);
                resistance = defense.MagicResistancePercent.Value;
                if (Has(effects, CombatEffectType.MagicResistanceReduction))
                {
                    if (modifiers == null ||
                        !modifiers.TryResolveMagicResistancePercent(target, effects,
                            out resistance))
                        throw new NotSupportedException(
                            "2.323[C] 마법저항 감소 중첩 수식 미연결");
                }
            }

            double amplification = 1.0;
            bool hasMagicAmp = Has(effects, CombatEffectType.MagicDamageAmplification);
            bool uncertainSchoolWithMagicAmp =
                hasMagicAmp && hit.School == DamageSchool.ScriptDefined;
            if (Has(effects, CombatEffectType.AllDamageAmplification) ||
                (hasMagicAmp && hit.School == DamageSchool.Magical) ||
                uncertainSchoolWithMagicAmp ||
                ((hit.Traits & DamageTraits.Explosive) != 0 &&
                 Has(effects, CombatEffectType.ExplosiveDamageAmplification)))
            {
                // 마법 피해 분류와 마법 저항 적용 여부는 서로 독립적이다.
                // 피해 계열 미확인일 때 마법 증폭을 임의로 무시하지 않는다.
                if (modifiers == null ||
                    !modifiers.TryResolveAmplification(target, effects,
                        hit, out amplification))
                    throw new NotSupportedException(
                        "2.323[C] 피해 증폭/스킬 계열·중첩 계산식 미연결");
            }

            double amount = Ordr323MapDamageMath.ApplyVerifiedFactors(hit.RawDamage,
                hit.AttackType, armorType, hit.UseAttackTable,
                hit.UseArmorRating, armor, hit.UseMagicResistance,
                resistance, amplification);
            if (amount > long.MaxValue)
                throw new OverflowException("원작 피해량이 프로젝트 표현 범위를 넘습니다.");
            long result = quantizer.Quantize(amount, hit);
            if (result < 0)
                throw new InvalidOperationException("원작 소수점 변환 정책 결과가 음수입니다.");
            return result;
        }

        private static bool TryReadArmorType(string source, out Ordr323ArmorType armor)
        {
            armor = Ordr323ArmorType.Normal;
            if (string.IsNullOrWhiteSpace(source)) return false;
            // Warcraft Object Editor defType uses "fort" and "none".
            // Gameplay table headings use fortified/unarmored.
            string alias = source.Trim();
            if (string.Equals(alias, "fort", StringComparison.OrdinalIgnoreCase))
                alias = "Fortified";
            else if (string.Equals(alias, "none", StringComparison.OrdinalIgnoreCase))
                alias = "Unarmored";
            return Enum.TryParse(alias, true, out armor) &&
                Enum.IsDefined(typeof(Ordr323ArmorType), armor);
        }

        private static bool Has(IReadOnlyList<CombatEffect> effects, CombatEffectType type)
        {
            foreach (var effect in effects)
                if (effect.Type == type) return true;
            return false;
        }
    }
}
