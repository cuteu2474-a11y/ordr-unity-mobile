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
            if (originalLabel == null && string.IsNullOrWhiteSpace(unit.GameRawCode))
                issues.Add("원본 Warcraft III 유닛 ID 미대응");
            else if (originalLabel != null && string.IsNullOrWhiteSpace(unit.GameRawCode))
                issues.Add("JASS 이름-능력 연결만 확인, 원본 객체 테이블 대조 필요");
            if (!HasOriginalObjectStats)
                issues.Add("원본 평타 공격력·공격주기·사거리·피해 계열 최종 검증 필요");
            if (profile == null)
                issues.Add("도감 상세 스킬 데이터 없음 (원본 능력 트리거 확인 필요)");
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
