using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // 원본 war3map.j/CWR 등록식의 순서를 보존한 *읽기 전용* 데이터.
    // BGI는 모두 검증/실행 조건이며 UPUN/UBAN/KING도 자동 보상으로 취급하지 않는다.
    public sealed class OriginalJassFusionInstruction
    {
        public string Kind { get; private set; }
        public string Rawcode { get; private set; }
        public int Count { get; private set; }

        internal OriginalJassFusionInstruction(Dictionary<string, object> values)
        {
            Kind = OrdrJson.RequiredString(values, "kind");
            Rawcode = OrdrJson.RequiredString(values, "rawcode");
            double? count = OrdrJson.Number(values, "count");
            if (Kind.Length != 4 || Rawcode.Length != 4 ||
                !count.HasValue || count.Value < 1 ||
                count.Value > int.MaxValue || Math.Floor(count.Value) != count.Value)
                throw new FormatException("원본 CWR 4자 rawcode/정수 수량 형식이 잘못됐습니다.");
            Count = (int)count.Value;
        }
    }

    public sealed class OriginalJassFusionRegistration
    {
        public string AbilityRawcode { get; private set; }
        public int SourceLine { get; private set; }
        public IReadOnlyList<OriginalJassFusionInstruction> Results { get; private set; }
        public IReadOnlyList<OriginalJassFusionInstruction> OrderedConditions { get; private set; }

        internal OriginalJassFusionRegistration(Dictionary<string, object> values)
        {
            AbilityRawcode = OrdrJson.RequiredString(values, "abilityRawcode");
            if (AbilityRawcode.Length != 4)
                throw new FormatException("조합 능력 rawcode는 정확히 4자입니다.");

            var line = OrdrJson.Number(values, "jassLine");
            if (!line.HasValue || line.Value < 1 || line.Value > int.MaxValue ||
                Math.Floor(line.Value) != line.Value)
                throw new FormatException("원본 JASS 줄 번호가 잘못됐습니다.");
            SourceLine = (int)line.Value;

            var results = new List<OriginalJassFusionInstruction>();
            foreach (object item in OrdrJson.Array(OrdrJson.Value(values, "results"), "results"))
                results.Add(new OriginalJassFusionInstruction(
                    OrdrJson.Object(item, "result")));
            if (results.Count == 0)
                throw new FormatException(AbilityRawcode + "의 결과 BGH가 비어 있습니다.");
            Results = results.AsReadOnly();

            var conditions = new List<OriginalJassFusionInstruction>();
            foreach (object item in OrdrJson.Array(
                OrdrJson.Value(values, "conditions"), "conditions"))
                conditions.Add(new OriginalJassFusionInstruction(
                    OrdrJson.Object(item, "condition")));
            OrderedConditions = conditions.AsReadOnly();
        }

        public bool HasUnresolvedSpecialConditions
        {
            get
            {
                foreach (var condition in OrderedConditions)
                    if (condition.Kind != "UNIT")
                        return true;
                return false;
            }
        }

        public IReadOnlyDictionary<string, int> RequiredUnitRawcodes()
        {
            var required = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var condition in OrderedConditions)
            {
                if (condition.Kind != "UNIT") continue;
                int previous;
                required.TryGetValue(condition.Rawcode, out previous);
                required[condition.Rawcode] = checked(previous + condition.Count);
            }
            return required;
        }
    }

    // 게임이 사용하는 프로젝트 ID(예: L-21)와 Warcraft 원시 ID(h00K)를
    // 무리하게 동일시하지 않고, 검증된 객체 테이블만 제공한다.
    public interface IVerifiedUnitRawcodeMap
    {
        // mapping 미확인 시 false, 출력값은 null
        bool TryResolveUnit(string gameRawcode, out string catalogUnitId);
    }

    public sealed class OriginalJassFusionCatalog
    {
        private readonly Dictionary<string, OriginalJassFusionRegistration> registrations =
            new Dictionary<string, OriginalJassFusionRegistration>(StringComparer.Ordinal);
        public string SourceSha256 { get; private set; }
        public int Count { get { return registrations.Count; } }
        public IEnumerable<OriginalJassFusionRegistration> All
        {
            get { return registrations.Values; }
        }

        public OriginalJassFusionRegistration FindByAbility(string abilityRawcode)
        {
            OriginalJassFusionRegistration value;
            return !string.IsNullOrWhiteSpace(abilityRawcode) &&
                registrations.TryGetValue(abilityRawcode, out value) ? value : null;
        }

        public bool IsSafeToAutoExecute(string abilityRawcode)
        {
            // 조합 승인·재료 소비/환불·발동 방식은 원본 JASS 이벤트를 구현해야만 확정.
            // rawcode 조건과 결과만 아는 상태에서 절대 제작하지 않는다.
            return false;
        }

        public static OriginalJassFusionCatalog FromJson(string json)
        {
            var root = OrdrJson.Object(OrdrJson.Parse(json), "original-jass-fusion-registry");
            if (OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]")
                throw new FormatException("다른 버전의 조합 규칙을 불러올 수 없습니다.");
            var result = new OriginalJassFusionCatalog();
            result.SourceSha256 = OrdrJson.RequiredString(root, "sourceSha256");
            foreach (object row in OrdrJson.Array(
                OrdrJson.Value(root, "registrations"), "registrations"))
            {
                var item = new OriginalJassFusionRegistration(
                    OrdrJson.Object(row, "fusion registration"));
                if (item.AbilityRawcode == "TEST")
                    continue;
                if (result.registrations.ContainsKey(item.AbilityRawcode))
                    throw new FormatException("중복 능력 rawcode: " + item.AbilityRawcode);
                result.registrations.Add(item.AbilityRawcode, item);
            }
            return result;
        }
    }
}
