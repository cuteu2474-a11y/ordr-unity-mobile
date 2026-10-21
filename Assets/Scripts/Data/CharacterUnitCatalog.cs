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
