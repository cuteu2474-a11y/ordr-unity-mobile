using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Data
{
    // 패치 기록에 나오는 '변경 후 공격 스펙' 증거를 수집.
    // 이 데이터는 원본 Warcraft III 오브젝트 파일을 검증한 공격 스펙이 아니다.
    public sealed class PatchStatObservation
    {
        public string Field { get; private set; }
        public double Value { get; private set; }
        public string Patch { get; private set; }
        public string SourceUrl { get; private set; }
        public string Evidence { get; private set; }

        internal PatchStatObservation(Dictionary<string, object> fields)
        {
            Field = OrdrJson.RequiredString(fields, "stat");
            double number;
            if (!OrdrJson.TryNumber(fields, "value", out number))
                throw new FormatException("패치 기록의 공격 수치가 없습니다.");
            Value = number;
            Patch = OrdrJson.RequiredString(fields, "fromPatch");
            SourceUrl = OrdrJson.RequiredString(fields, "sourceUrl");
            Evidence = OrdrJson.RequiredString(fields, "evidence");
        }
    }

    public sealed class PatchUnitStatEvidence
    {
        public string UnitId { get; private set; }
        public string Name { get; private set; }
        public bool UsableForCombat { get; private set; }
        public IReadOnlyDictionary<string, PatchStatObservation> Observations { get; private set; }

        internal PatchUnitStatEvidence(Dictionary<string, object> fields)
        {
            UnitId = OrdrJson.RequiredString(fields, "unitId");
            Name = OrdrJson.RequiredString(fields, "name");
            object usable = OrdrJson.Value(fields, "usableForCombat");
            if (!(usable is bool))
                throw new FormatException(UnitId + "의 usableForCombat 플래그가 필요합니다.");
            UsableForCombat = (bool)usable;
            var stats = new Dictionary<string, PatchStatObservation>(StringComparer.Ordinal);
            foreach (var raw in OrdrJson.Array(OrdrJson.Value(fields, "observations"), "observations"))
            {
                var observation = new PatchStatObservation(
                    OrdrJson.Object(raw, "observation"));
                if (stats.ContainsKey(observation.Field))
                    throw new FormatException(UnitId + "의 패치 스탯이 중복됩니다: " + observation.Field);
                stats.Add(observation.Field, observation);
            }
            Observations = stats;
        }

        public double? Get(string field)
        {
            PatchStatObservation observation;
            return Observations.TryGetValue(field, out observation)
                ? (double?)observation.Value : null;
        }
    }

    public sealed class OriginalUnitStatEvidenceCatalog
    {
        private readonly Dictionary<string, PatchUnitStatEvidence> entries =
            new Dictionary<string, PatchUnitStatEvidence>(StringComparer.Ordinal);
        public int Count { get { return entries.Count; } }
        public IEnumerable<PatchUnitStatEvidence> All { get { return entries.Values; } }

        public PatchUnitStatEvidence Find(string unitId)
        {
            PatchUnitStatEvidence value;
            return unitId != null && entries.TryGetValue(unitId, out value) ? value : null;
        }

        public static OriginalUnitStatEvidenceCatalog FromJson(string json,
            CharacterUnitCatalog units)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            var root = OrdrJson.Object(OrdrJson.Parse(json), "original-stat-evidence.json");
            if (OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]")
                throw new FormatException("패치 근거 데이터가 게임 버전과 일치하지 않습니다.");
            var result = new OriginalUnitStatEvidenceCatalog();
            var observations = OrdrJson.Array(
                OrdrJson.Value(root, "verifiedFromPages"), "verifiedFromPages");
            foreach (var raw in observations)
            {
                var entry = new PatchUnitStatEvidence(OrdrJson.Object(raw, "evidence"));
                if (units.Find(entry.UnitId) == null)
                    throw new FormatException("271종 카탈로그에 없는 공격 스펙: " + entry.UnitId);
                if (entry.UsableForCombat)
                    throw new FormatException("패치만 확인한 수치는 완전한 전투 스펙으로 취급할 수 없습니다.");
                if (result.entries.ContainsKey(entry.UnitId))
                    throw new FormatException("중복 패치 유닛: " + entry.UnitId);
                result.entries.Add(entry.UnitId, entry);
            }
            return result;
        }
    }
}
