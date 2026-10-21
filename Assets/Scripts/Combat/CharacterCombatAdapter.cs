using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // 변신·반격·스택·특수 이벤트는 해당 캐릭터의 원작 조건이 확인되면
    // 공통 엔진에 검증된 트리거 스펙을 연결한다.
    public interface IOriginalSpecialSkillTriggerMapping
    {
        bool TryBuildVerifiedSpec(CharacterSkillProfile profile,
            ImportedCombatSkill skill, out CombatSkillSpec specification);
    }

    // 스킬의 발동 여부는 기존 CombatSkillTriggerEngine이 관리한다.
    // CombatSkillSpec의 Effects는 실제 피해량 데이터가 아니라 발동 메타데이터 표식이다.
    // 실제 효과 적용은 원본 JSON component를 읽는 CharacterSkillEffectExecutor가 담당한다.
    public sealed class CharacterCombatMapping
    {
        public UnitCombatSpec AttackSpec { get; private set; }
        public IReadOnlyDictionary<string, ImportedCombatSkill> ActivatedSkills { get; private set; }
        public IReadOnlyList<string> DeferredReasons { get; private set; }

        internal CharacterCombatMapping(UnitCombatSpec attackSpec,
            Dictionary<string, ImportedCombatSkill> skills, List<string> deferred)
        {
            AttackSpec = attackSpec;
            ActivatedSkills = new Dictionary<string, ImportedCombatSkill>(skills, StringComparer.Ordinal);
            DeferredReasons = deferred.AsReadOnly();
        }

        public static CharacterCombatMapping Build(string catalogUnitId,
            CharacterSkillProfile profile, BasicAttackSpec verifiedBasic,
            string originalRawcode = null,
            IOriginalSpecialSkillTriggerMapping specialTriggers = null)
        {
            if (string.IsNullOrWhiteSpace(catalogUnitId))
                throw new ArgumentException("캐릭터 ID가 필요합니다.");
            if (verifiedBasic == null) throw new ArgumentNullException(nameof(verifiedBasic));
            var converted = new List<CombatSkillSpec>();
            var lookup = new Dictionary<string, ImportedCombatSkill>(StringComparer.Ordinal);
            var deferred = new List<string>();
            if (profile != null)
            {
                if (profile.UnitId != catalogUnitId)
                    throw new InvalidOperationException("캐릭터와 상세 프로필 ID가 다릅니다.");
                foreach (var skill in profile.Skills)
                {
                    string reason;
                    SkillTriggerKind trigger;
                    SkillTargetArea area;
                    double radius;
                    int chance;
                    if (!TryConvertTrigger(skill, out trigger, out area, out radius, out chance, out reason))
                    {
                        CombatSkillSpec verified;
                        if (specialTriggers != null &&
                            specialTriggers.TryBuildVerifiedSpec(profile, skill, out verified))
                        {
                            if (verified == null || verified.RawCode != skill.Id ||
                                verified.Trigger != SkillTriggerKind.VerifiedCondition)
                                throw new InvalidOperationException(
                                    "검증된 특수 트리거의 ID/종류가 맞지 않습니다.");
                            converted.Add(verified);
                            lookup.Add(skill.Id, skill);
                        }
                        else
                            deferred.Add(skill.Id + " (" + skill.Name + "): " + reason);
                        continue;
                    }

                    // 이 표식의 피해 0은 실제 피해량을 뜻하지 않는다.
                    // 스킬의 단일/광역 피해, 디버프, 다중 타격은 원본 components에서 실행한다.
                    var marker = new SkillEffectSpec(SkillEffectKind.Damage, 0, 0,
                        DamageSchool.ScriptDefined, DamageTraits.None);
                    var normalized = new CombatSkillSpec(skill.Id, trigger, chance,
                        skill.CooldownSeconds ??
                        (skill.Number("absoluteCooldownSeconds") ?? 0),
                        trigger == SkillTriggerKind.Periodic
                            ? skill.PeriodSeconds.Value : 0,
                        area, radius, new[] { marker }, profile.SourceUrl);
                    converted.Add(normalized);
                    lookup.Add(skill.Id, skill);
                }
            }

            // 원본 rawcode 미확보 시 catalog: 접두어로 가상 ID임을 명시한다.
            // 이 ID를 원작 Warcraft III 오브젝트 ID로 사용하면 안 된다.
            string sourceCode = !string.IsNullOrWhiteSpace(originalRawcode)
                ? originalRawcode : profile != null &&
                    !string.IsNullOrWhiteSpace(profile.GameRawCode)
                    ? profile.GameRawCode : "catalog:" + catalogUnitId;
            return new CharacterCombatMapping(
                new UnitCombatSpec(sourceCode, verifiedBasic, converted,
                    profile == null ? "데이터 없음: 기본 공격 수치 외부 확인 필요" : profile.SourceUrl),
                lookup, deferred);
        }

        private static bool TryConvertTrigger(ImportedCombatSkill skill,
            out SkillTriggerKind trigger, out SkillTargetArea area, out double radius,
            out int chance, out string reason)
        {
            trigger = SkillTriggerKind.OnAttack;
            area = SkillTargetArea.Single;
            radius = 0;
            chance = 0;
            reason = null;
            if (skill.Get("requiresOriginalVerification") is bool &&
                (bool)skill.Get("requiresOriginalVerification"))
            {
                reason = "툴팁/패치 버전 충돌: 원본 W3X 대조 전 실행 금지";
                return false;
            }
            if (skill.Trigger == "onAttack")
            {
                if (!skill.ChancePercent.HasValue)
                { reason = "발동 확률 미확인"; return false; }
            }
            else if (skill.Trigger == "onEveryAttack")
            {
                // 매 타격 발동은 원본 데이터가 명시한 경우만 확정 발동 처리한다.
            }
            else if (skill.Trigger == "periodic")
            {
                if (!skill.PeriodSeconds.HasValue || skill.PeriodSeconds.Value <= 0)
                { reason = "정확한 주기 간격 미확인"; return false; }
                trigger = SkillTriggerKind.Periodic;
            }
            else if (skill.Trigger == "onHit")
            {
                if (!skill.ChancePercent.HasValue)
                { reason = "적중 발동 확률 미확인"; return false; }
                trigger = SkillTriggerKind.OnHit;
            }
            else
            {
                reason = "조건·마나·주기·변신 등 전용 트리거는 후속 연결 필요: " + skill.Trigger;
                return false;
            }

            // 조건을 구현하지 않고 '일반 onAttack'으로 강제 실행하면 원작과 달라지므로 보류한다.
            foreach (var key in new[] {
                "requires", "requiresForm", "requiresHealthAtLeast", "onlyTargetKinds",
                "exceptTargetKinds", "conditions", "overrides", "triggerOverride",
                "exclusiveWith", "requiredAttacks", "guaranteedEveryNthHit",
                "summonedCloneProcChanceMultiplier", "chancePercentRange",
                "manaAtLeast", "manaCost", "requiredMana", "healthCost",
                "requiredHealth", "requiresNearbyUnit", "requiredTargetArmorBreak",
                "requiredTargetKind", "requiredForm", "onlyAffectsGradesAtLeast",
                "triggerOnlyWhen", "requiresState", "requiresNearbyLivingEnemyWithin"
            })
                if (skill.OriginalFields.ContainsKey(key))
                { reason = "별도 발동 조건 미구현: " + key; return false; }

            string target = skill.Target;
            if (target == null)
            {
                reason = "스킬 대상 방식 미확인";
                return false;
            }
            if (target == "single")
                area = SkillTargetArea.Single;
            else if (target == "area" || target == "targetCircle")
                area = SkillTargetArea.CircleAroundTarget;
            else if (target == "casterArea")
            {
                // 기존 스킬 메타데이터는 표적 중심 원형으로 등록하되 실행기는
                // 원본 target 필드를 사용해 시전자 중심으로 판정한다.
                area = SkillTargetArea.CircleAroundTarget;
            }
            else
            {
                reason = "대상 형태 미구현: " + target;
                return false;
            }
            if (area == SkillTargetArea.CircleAroundTarget)
            {
                if (!skill.Radius.HasValue || skill.Radius.Value <= 0)
                { reason = "광역 반경 미확인"; return false; }
                radius = skill.Radius.Value;
            }

            double prob = skill.Trigger == "onEveryAttack" ||
                (skill.Trigger == "periodic" && !skill.ChancePercent.HasValue)
                ? 100 : skill.ChancePercent.Value;
            if (prob < 0 || prob > 100)
            { reason = "발동 확률 범위 오류"; return false; }
            chance = (int)Math.Round(prob * 10000d, MidpointRounding.AwayFromZero);
            return true;
        }
    }
}
