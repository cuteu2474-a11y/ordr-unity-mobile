using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Data
{
    public sealed class CatalogUnit
    {
        public string Id { get; private set; }
        public string Grade { get; private set; }
        public string Name { get; private set; }
        public string GameRawCode { get; private set; }
        public string VariantOfUnitId { get; private set; }
        public string OriginalSourceRawcode { get; private set; }
        public bool IsAlternateForm { get { return !string.IsNullOrEmpty(VariantOfUnitId); } }
        public string CatalogEntryKind { get; private set; }
        // Tooltip evidence is independent of Warcraft rawcodes and may be
        // incomplete or older than the target version. A range is not a
        // ready-to-run attack without timing and damage-school information.
        public long? AttackPowerMin { get; private set; }
        public long? AttackPowerMax { get; private set; }
        public string AttackPowerSourceUrl { get; private set; }
        public string AttackPowerVersionStatus { get; private set; }
        public string SkillSpecsStatus { get; private set; }
        public bool HasNoPublishedSkills { get { return SkillSpecsStatus == "no-published-skills"; } }
        public IReadOnlyList<string> RoleTags { get; private set; }
        public bool IsDetailAvailable { get; private set; }
        public IReadOnlyDictionary<string, object> OriginalFields { get; private set; }

        internal CatalogUnit(Dictionary<string, object> fields)
        {
            Id = OrdrJson.RequiredString(fields, "id");
            Grade = OrdrJson.RequiredString(fields, "grade");
            Name = OrdrJson.RequiredString(fields, "name");
            GameRawCode = OrdrJson.String(fields, "gameRawCode");
            VariantOfUnitId = OrdrJson.String(fields, "variantOfUnitId");
            OriginalSourceRawcode = OrdrJson.String(fields, "originalSourceRawcode");
            CatalogEntryKind = OrdrJson.String(fields, "catalogEntryKind");
            SkillSpecsStatus = OrdrJson.String(fields, "skillSpecsStatus");
            IsDetailAvailable = SkillSpecsStatus == "partial" || SkillSpecsStatus == "complete";
            var min = OrdrJson.Number(fields, "attackPowerMin");
            var max = OrdrJson.Number(fields, "attackPowerMax");
            if (min.HasValue != max.HasValue)
                throw new FormatException(Id + ": 공격력 범위는 최솟값과 최댓값이 모두 필요합니다.");
            if (min.HasValue)
            {
                if (min.Value < 0 || max.Value < min.Value ||
                    max.Value >= long.MaxValue ||
                    Math.Floor(min.Value) != min.Value ||
                    Math.Floor(max.Value) != max.Value)
                    throw new FormatException(Id + ": 잘못된 평타 공격력 범위입니다.");
                AttackPowerSourceUrl = OrdrJson.RequiredString(fields, "attackPowerSourceUrl");
                AttackPowerVersionStatus = OrdrJson.RequiredString(fields, "attackPowerVersionStatus");
                AttackPowerMin = (long)min.Value;
                AttackPowerMax = (long)max.Value;
            }
            var tags = new List<string>();
            foreach (var role in OrdrJson.Array(OrdrJson.Value(fields, "roleTags"), "roleTags"))
            {
                var tag = role as string;
                if (tag == null) throw new FormatException(Id + ": 역할 태그는 문자열이어야 합니다.");
                tags.Add(tag);
            }
            RoleTags = tags.AsReadOnly();
            OriginalFields = new Dictionary<string, object>(fields, StringComparer.Ordinal);
        }
    }

    // 캐릭터 데이터가 없는 하위 등급(71종)도 이름·등급·소환 ID를 보유할 수 있게 한다.
    public sealed class CharacterUnitCatalog
    {
        private readonly Dictionary<string, CatalogUnit> entries =
            new Dictionary<string, CatalogUnit>(StringComparer.Ordinal);

        public int Count { get { return entries.Count; } }
        public IEnumerable<CatalogUnit> All { get { return entries.Values; } }

        public CatalogUnit Find(string id)
        {
            CatalogUnit entry;
            return id != null && entries.TryGetValue(id, out entry) ? entry : null;
        }

        public CatalogUnit Require(string id)
        {
            CatalogUnit entry = Find(id);
            if (entry == null) throw new KeyNotFoundException("카탈로그에 없는 캐릭터: " + id);
            return entry;
        }

        // Merge source-reviewed wiki identities only after the pinned original
        // 277-object catalogue has been validated. No spawn/fusion authority.
        public void ApplyWikiExtension(string json)
        {
            var root = OrdrJson.Object(OrdrJson.Parse(json), "wiki-unit-catalog-extension.json");
            if (OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "format") != "ordr-wiki-unit-extension-v1" ||
                OrdrJson.Number(root, "baseCatalogEntries") != entries.Count ||
                OrdrJson.RequiredString(root, "sourceW3xSha256") !=
                    "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c")
                throw new FormatException("위키 확장 카탈로그의 원본 버전/등록 수 불일치");
            var next = new Dictionary<string, CatalogUnit>(entries, StringComparer.Ordinal);
            foreach (var raw in OrdrJson.Array(OrdrJson.Value(root, "reclassifications"),
                         "reclassifications"))
            {
                var change = OrdrJson.Object(raw, "reclassification");
                var id = OrdrJson.RequiredString(change, "id");
                CatalogUnit original;
                if (!next.TryGetValue(id, out original) ||
                    original.Grade != OrdrJson.RequiredString(change, "expectedGrade") ||
                    id != "PS-01" || OrdrJson.RequiredString(change, "grade") != "히든")
                    throw new FormatException("해적선 분류 변경 근거가 불일치합니다.");
                var copied = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var pair in original.OriginalFields) copied[pair.Key] = pair.Value;
                copied["grade"] = "히든";
                copied["gradeOrder"] = OrdrJson.Number(change, "gradeOrder");
                next[id] = new CatalogUnit(copied);
            }
            foreach (var raw in OrdrJson.Array(OrdrJson.Value(root, "entries"), "entries"))
            {
                var unit = new CatalogUnit(OrdrJson.Object(raw, "extended unit"));
                if (next.ContainsKey(unit.Id))
                    throw new FormatException("위키 확장 유닛 ID 중복: " + unit.Id);
                // A display identity cannot masquerade as an independently verified
                // Warcraft attacker. Only these original W3X rawcodes are known.
                if (unit.OriginalSourceRawcode != null &&
                    !((unit.Id == "MY-14" && unit.OriginalSourceRawcode == "h0C2" &&
                       unit.CatalogEntryKind == "fusion-result") ||
                      (unit.Id == "R-44" && unit.OriginalSourceRawcode == "h05Y" &&
                       unit.CatalogEntryKind == "support-object") ||
                      (unit.Id == "TR-32" && unit.OriginalSourceRawcode == "H0B6" &&
                       unit.CatalogEntryKind == "alternate-form")))
                    throw new FormatException("원본 객체 출처를 확인하지 않은 위키 ID: " + unit.Id);
                if (unit.AttackPowerMin.HasValue || unit.GameRawCode != null ||
                    (unit.CatalogEntryKind != "alternate-form" &&
                     unit.CatalogEntryKind != "support-object" &&
                     unit.CatalogEntryKind != "fusion-result"))
                    throw new FormatException("확장 유닛의 출처/공격 모델 검증이 필요: " + unit.Id);
                next.Add(unit.Id, unit);
            }
            if (OrdrJson.Number(root, "expectedMergedEntries") != next.Count)
                throw new FormatException("위키 확장 후 등록 수가 기대값과 다릅니다.");
            foreach (var unit in next.Values)
            {
                if (!unit.IsAlternateForm) continue;
                CatalogUnit parent;
                if (!next.TryGetValue(unit.VariantOfUnitId, out parent) ||
                    parent.IsAlternateForm || parent.Grade != unit.Grade ||
                    unit.CatalogEntryKind != "alternate-form")
                    throw new FormatException("유닛 형태의 원본 캐릭터가 유효하지 않습니다: " + unit.Id);
            }
            // All-or-nothing: corrupt extension never partially regrades units.
            entries.Clear();
            foreach (var pair in next) entries.Add(pair.Key, pair.Value);
        }

        public static CharacterUnitCatalog FromJson(string json)
        {
            var root = OrdrJson.Object(OrdrJson.Parse(json), "units.json");
            if (OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]")
                throw new FormatException("캐릭터 카탈로그 버전이 다릅니다.");
            var result = new CharacterUnitCatalog();
            foreach (var raw in OrdrJson.Array(OrdrJson.Value(root, "entries"), "entries"))
            {
                var entry = new CatalogUnit(OrdrJson.Object(raw, "unit"));
                if (result.entries.ContainsKey(entry.Id))
                    throw new FormatException("중복 유닛 ID: " + entry.Id);
                result.entries.Add(entry.Id, entry);
            }
            return result;
        }
    }
}
