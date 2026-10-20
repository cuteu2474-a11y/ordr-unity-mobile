using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // 도감의 물리/마법 분류와 원본 워크래프트 공격 타입은 동일한 개념이 아니다.
    // ScriptDefined는 원작 고유 피해 처리 함수를 확인해야 하는 경우에만 사용한다.
    public enum DamageSchool { Physical, Magical, ScriptDefined }

    // 폭발형은 물리/마법과 배타적인 피해 유형이 아니라 별도 증폭 대상 속성이다.
    [Flags]
    public enum DamageTraits { None = 0, Explosive = 1 }

    public enum SkillTriggerKind
    {
        OnAttack, OnHit, Periodic, VerifiedCondition, PassiveAura
    }

    public enum SkillTargetArea { Single, CircleAroundTarget, EntireEligibleZone }

    public enum SkillEffectKind
    {
        Damage, ArmorReduction, MagicResistanceReduction, ArmorBreakStack,
        MovementSlowPercent, Stun, AttackSpeedBuff, AttackPowerBuff,
        MagicDamageAmplification, ExplosiveDamageAmplification,
        AllDamageAmplification, HealthRegeneration
    }

    public sealed class SkillEffectSpec
    {
        public SkillEffectKind Kind { get; }
        public double Magnitude { get; }
        public double DurationSeconds { get; }
        public DamageSchool DamageSchool { get; }
        public DamageTraits Traits { get; }

        // Magnitude의 단위는 원본 스킬 데이터에 의해 결정된다.
        // 특수 피해 공식, 오라 중첩 공식, 반올림은 여기서 추측해 넣지 않는다.
        public SkillEffectSpec(SkillEffectKind kind, double magnitude, double durationSeconds,
            DamageSchool damageSchool, DamageTraits damageTraits)
        {
            if (!Enum.IsDefined(typeof(SkillEffectKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (!Enum.IsDefined(typeof(DamageSchool), damageSchool))
                throw new ArgumentOutOfRangeException(nameof(damageSchool));
            if ((damageTraits & ~DamageTraits.Explosive) != DamageTraits.None)
                throw new ArgumentOutOfRangeException(nameof(damageTraits));
            if (double.IsNaN(magnitude) || double.IsInfinity(magnitude) || magnitude < 0)
                throw new ArgumentOutOfRangeException(nameof(magnitude));
            if (double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds) || durationSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            Kind = kind;
            Magnitude = magnitude;
            DurationSeconds = durationSeconds;
            DamageSchool = damageSchool;
            Traits = damageTraits;
        }
    }

    public sealed class CombatSkillSpec
    {
        public string RawCode { get; }
        public SkillTriggerKind Trigger { get; }
        public int ChancePerMillion { get; }
        public double CooldownSeconds { get; }
        public double PeriodSeconds { get; }
        public SkillTargetArea Area { get; }
        public double Radius { get; }
        public IReadOnlyList<SkillEffectSpec> Effects { get; }
        public string Evidence { get; }

        // 1,000,000 = 확정 발동. 원작의 확률/쿨타임 값을 그대로 주입할 것.
        public CombatSkillSpec(string rawCode, SkillTriggerKind trigger, int chancePerMillion,
            double cooldownSeconds, double periodSeconds, SkillTargetArea area, double radius,
            IEnumerable<SkillEffectSpec> effects, string evidence)
        {
            if (string.IsNullOrWhiteSpace(rawCode)) throw new ArgumentException("스킬 ID가 필요합니다.");
            if (string.IsNullOrWhiteSpace(evidence)) throw new ArgumentException("스킬 근거가 필요합니다.");
            if (!Enum.IsDefined(typeof(SkillTriggerKind), trigger))
                throw new ArgumentOutOfRangeException(nameof(trigger));
            if (!Enum.IsDefined(typeof(SkillTargetArea), area))
                throw new ArgumentOutOfRangeException(nameof(area));
            if (chancePerMillion < 0 || chancePerMillion > 1000000)
                throw new ArgumentOutOfRangeException(nameof(chancePerMillion));
            if (double.IsNaN(cooldownSeconds) || double.IsInfinity(cooldownSeconds) || cooldownSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(cooldownSeconds));
            if (double.IsNaN(periodSeconds) || double.IsInfinity(periodSeconds) || periodSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(periodSeconds));
            if (double.IsNaN(radius) || double.IsInfinity(radius) || radius < 0)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (trigger == SkillTriggerKind.Periodic && periodSeconds <= 0)
                throw new ArgumentException("주기 발동에는 양수 주기가 필요합니다.");
            if (trigger != SkillTriggerKind.Periodic && periodSeconds != 0)
                throw new ArgumentException("주기 발동 외에는 주기를 지정하지 않습니다.");
            if (trigger == SkillTriggerKind.PassiveAura && chancePerMillion != 1000000)
                throw new ArgumentException("지속형 오라는 확률형 트리거가 아닙니다.");
            if (area == SkillTargetArea.CircleAroundTarget && radius <= 0)
                throw new ArgumentException("원형 범위 스킬은 유효 반경이 필요합니다.");
            if (area != SkillTargetArea.CircleAroundTarget && radius != 0)
                throw new ArgumentException("반경은 원형 범위 스킬에서만 사용합니다.");
            if (effects == null) throw new ArgumentNullException(nameof(effects));
            var list = new List<SkillEffectSpec>();
            foreach (var effect in effects)
            {
                if (effect == null) throw new ArgumentException("null 스킬 효과는 허용되지 않습니다.");
                list.Add(effect);
            }
            if (list.Count == 0) throw new ArgumentException("효과 없는 스킬은 등록하지 않습니다.");
            RawCode = rawCode;
            Trigger = trigger;
            ChancePerMillion = chancePerMillion;
            CooldownSeconds = cooldownSeconds;
            PeriodSeconds = periodSeconds;
            Area = area;
            Radius = radius;
            Effects = list.AsReadOnly();
            Evidence = evidence;
        }
    }

    public sealed class BasicAttackSpec
    {
        public long NominalAttackPower { get; }
        public double IntervalSeconds { get; }
        public double Range { get; }
        public DamageSchool School { get; }
        public DamageTraits Traits { get; }

        // 공격력 표시값은 최종 피해량이 아니다. 방어/저항 계산기는 외부 주입.
        public BasicAttackSpec(long nominalAttackPower, double intervalSeconds, double range,
            DamageSchool school, DamageTraits traits)
        {
            if (nominalAttackPower < 0) throw new ArgumentOutOfRangeException(nameof(nominalAttackPower));
            if (double.IsNaN(intervalSeconds) || double.IsInfinity(intervalSeconds) || intervalSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            if (double.IsNaN(range) || double.IsInfinity(range) || range < 0)
                throw new ArgumentOutOfRangeException(nameof(range));
            if (!Enum.IsDefined(typeof(DamageSchool), school))
                throw new ArgumentOutOfRangeException(nameof(school));
            if ((traits & ~DamageTraits.Explosive) != DamageTraits.None)
                throw new ArgumentOutOfRangeException(nameof(traits));
            NominalAttackPower = nominalAttackPower;
            IntervalSeconds = intervalSeconds;
            Range = range;
            School = school;
            Traits = traits;
        }
    }

    public sealed class UnitCombatSpec
    {
        public string UnitRawCode { get; }
        public BasicAttackSpec BasicAttack { get; }
        public IReadOnlyList<CombatSkillSpec> Skills { get; }
        public string Evidence { get; }

        public UnitCombatSpec(string unitRawCode, BasicAttackSpec basicAttack,
            IEnumerable<CombatSkillSpec> skills, string evidence)
        {
            if (string.IsNullOrWhiteSpace(unitRawCode)) throw new ArgumentException("유닛 rawcode가 필요합니다.");
            if (basicAttack == null) throw new ArgumentNullException(nameof(basicAttack));
            if (skills == null) throw new ArgumentNullException(nameof(skills));
            if (string.IsNullOrWhiteSpace(evidence)) throw new ArgumentException("유닛 정보 근거가 필요합니다.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<CombatSkillSpec>();
            foreach (var skill in skills)
            {
                if (skill == null) throw new ArgumentException("null 스킬은 등록할 수 없습니다.");
                if (!seen.Add(skill.RawCode))
                    throw new ArgumentException("동일 유닛에 같은 스킬 ID가 중복 등록되었습니다.");
                list.Add(skill);
            }
            UnitRawCode = unitRawCode;
            BasicAttack = basicAttack;
            Skills = list.AsReadOnly();
            Evidence = evidence;
        }
    }
}
