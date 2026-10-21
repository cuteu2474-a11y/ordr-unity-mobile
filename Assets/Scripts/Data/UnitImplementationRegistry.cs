using System;
using System.Collections.Generic;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Data
{
    // Every character from the 2.323[C] catalogue has one runtime definition.
    // This is NOT a claim that each character has verified combat statistics.
    public sealed class UnitImplementationDefinition
    {
        public string UnitId { get; private set; }
        public string DisplayName { get; private set; }
        public string Grade { get; private set; }
        public CatalogUnit Unit { get; private set; }
        public CharacterSkillProfile Profile { get; private set; }
        public string LabelLinkedOriginalRawcode { get; private set; }
        public string OriginalCraftingAbility { get; private set; }
        public bool HasOriginalObjectStats { get; private set; }
        public bool CanSpawnIntoRoster { get { return true; } }
        public bool HasCollectedDetailedSkills { get { return Profile != null; } }
        public bool HasConfirmedNoPublishedSkills { get { return Unit.HasNoPublishedSkills; } }
        public int SkillCount { get { return Profile == null ? 0 : Profile.Skills.Count; } }
        public int PassiveCount { get { return Profile == null ? 0 : Profile.Passives.Count; } }
        public IReadOnlyList<string> MissingImplementation { get; private set; }

        internal UnitImplementationDefinition(CatalogUnit unit, CharacterSkillProfile profile,
            OriginalUnitRawcodeLabelLink originalLabel)
        {
            Unit = unit ?? throw new ArgumentNullException(nameof(unit));
            if (profile != null && profile.UnitId != unit.Id)
                throw new FormatException("캐릭터 ID와 상세 스킬 데이터가 다릅니다: " + unit.Id);
            UnitId = unit.Id;
            DisplayName = unit.Name;
            Grade = unit.Grade;
            Profile = profile;
            if (originalLabel != null)
            {
                if (originalLabel.CatalogUnitId != unit.Id)
                    throw new FormatException("원본 rawcode 대응 유닛이 다릅니다: " + unit.Id);
                LabelLinkedOriginalRawcode = originalLabel.Rawcode;
                OriginalCraftingAbility = originalLabel.AbilityRawcode;
            }

            // A source-script label is not an authoritative Warcraft object
            // table, and cannot make unknown attack values "verified".
            HasOriginalObjectStats = profile != null && profile.HasVerifiedBasicAttack;
            var issues = new List<string>();
            // Unity 독립 리메이크는 원본 rawcode 대조를 실행 필수 조건으로
            // 사용하지 않는다. 이 필드는 과거 분석 자료로만 유지한다.
            if (!HasOriginalObjectStats)
                issues.Add(unit.AttackPowerMin.HasValue
                    ? "평타 공격력 범위 근거는 있으나 공격 주기·피해 계열 및 버전 확인이 필요"
                    : "도감 평타 공격력·공격 주기·피해 계열 추가 수집 필요");
            if (profile == null && !unit.HasNoPublishedSkills)
                issues.Add("도감 상세 스킬 설명 아직 수집되지 않음");
            else
            {
                if (profile.Completeness != "complete")
                    issues.Add("스킬·패시브 발동 조건 및 효과 공식 부분 수집");
                foreach (var skill in profile.Skills)
                    if (skill.Get("requiresOriginalVerification") is bool &&
                        (bool)skill.Get("requiresOriginalVerification"))
                    {
                        issues.Add("패치/도감 수치 충돌로 실행 보류한 스킬 존재");
                        break;
                    }
            }
            // Source-verification gates are kept explicit to avoid treating a
            // mobile catalogue entry as a finished playable combat unit.
            MissingImplementation = issues.AsReadOnly();
        }
    }

    public sealed class UnitImplementationRegistry
    {
        private readonly Dictionary<string, UnitImplementationDefinition> byId =
            new Dictionary<string, UnitImplementationDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<UnitImplementationDefinition>> byGrade =
            new Dictionary<string, List<UnitImplementationDefinition>>(StringComparer.Ordinal);
        private readonly List<UnitImplementationDefinition> ordered =
            new List<UnitImplementationDefinition>();
        private readonly List<string> gradeOrder = new List<string>();

        public int Count { get { return ordered.Count; } }
        public int DetailedProfiles { get; private set; }
        public int LinkedJassLabels { get; private set; }
        public int VerifiedBasicAttacks { get; private set; }
        public int CollectedSkills { get; private set; }
        public int CollectedPassives { get; private set; }
        public IReadOnlyList<UnitImplementationDefinition> All { get { return ordered.AsReadOnly(); } }

        public UnitImplementationDefinition Find(string unitId)
        {
            UnitImplementationDefinition value;
            return unitId != null && byId.TryGetValue(unitId, out value) ? value : null;
        }

        public UnitImplementationDefinition Require(string unitId)
        {
            var value = Find(unitId);
            if (value == null) throw new KeyNotFoundException("등록되지 않은 캐릭터: " + unitId);
            return value;
        }

        public IReadOnlyList<UnitImplementationDefinition> OfGrade(string grade)
        {
            List<UnitImplementationDefinition> units;
            if (grade != null && byGrade.TryGetValue(grade, out units))
                return units.AsReadOnly();
            return new List<UnitImplementationDefinition>().AsReadOnly();
        }

        public IReadOnlyList<string> Grades { get { return gradeOrder.AsReadOnly(); } }

        public static UnitImplementationRegistry Combine(
            CharacterUnitCatalog units, CharacterSkillCatalog skills,
            OriginalUnitRawcodeLabelCatalog originalLinks)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            if (skills == null) throw new ArgumentNullException(nameof(skills));
            var result = new UnitImplementationRegistry();

            foreach (var unit in units.All)
            {
                var profile = skills.Find(unit.Id);
                var original = originalLinks == null ? null : originalLinks.FindUnit(unit.Id);
                var definition = new UnitImplementationDefinition(unit, profile, original);
                if (result.byId.ContainsKey(unit.Id))
                    throw new FormatException("기본 카탈로그 ID 중복: " + unit.Id);
                result.byId.Add(unit.Id, definition);
                result.ordered.Add(definition);

                List<UnitImplementationDefinition> gradeUnits;
                if (!result.byGrade.TryGetValue(unit.Grade, out gradeUnits))
                {
                    gradeUnits = new List<UnitImplementationDefinition>();
                    result.byGrade.Add(unit.Grade, gradeUnits);
                    result.gradeOrder.Add(unit.Grade);
                }
                gradeUnits.Add(definition);
                if (profile != null)
                {
                    result.DetailedProfiles++;
                    checked
                    {
                        result.CollectedSkills += profile.Skills.Count;
                        result.CollectedPassives += profile.Passives.Count;
                    }
                }
                if (original != null) result.LinkedJassLabels++;
                if (definition.HasOriginalObjectStats) result.VerifiedBasicAttacks++;
            }

            foreach (var profile in skills.All)
                if (!result.byId.ContainsKey(profile.UnitId))
                    throw new FormatException("271종 카탈로그에 없는 상세 스킬 데이터: " +
                        profile.UnitId);
            if (originalLinks != null)
                foreach (var original in originalLinks.All)
                    if (!result.byId.ContainsKey(original.CatalogUnitId))
                        throw new FormatException("271종 카탈로그에 없는 JASS 매핑: " +
                            original.CatalogUnitId);
            return result;
        }
    }
}
