using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // 원본 JASS와 웹 도감의 조합 후보를 조회하기 위한 읽기 전용 진단 자료.
    // 원본 객체를 검증한 실행용 매핑으로 사용하면 안 된다.
    public sealed class OriginalMysticJassCandidate
    {
        public string AbilityRawcode { get; private set; }
        public string ResultRawcodeCandidate { get; private set; }
        public int CorrelatedIngredientCount { get; private set; }

        internal OriginalMysticJassCandidate(Dictionary<string, object> row)
        {
            AbilityRawcode = OrdrJson.RequiredString(row, "abilityRawcode");
            ResultRawcodeCandidate = OrdrJson.RequiredString(row, "resultRawcode");
            if (AbilityRawcode.Length != 4 || ResultRawcodeCandidate.Length != 4)
                throw new FormatException("신비 조합 원본 후보 rawcode의 길이가 올바르지 않습니다.");
            int labelCount = 0;
            foreach (object item in OrdrJson.Array(OrdrJson.Value(row,
                "labelMatchedIngredients"), "labelMatchedIngredients"))
            {
                var ingredient = OrdrJson.Object(item, "labelMatchedIngredient");
                OrdrJson.RequiredString(ingredient, "catalogUnitId");
                labelCount = checked(labelCount + OtherworldMysticFusionCatalog.NonNegativeInteger(
                    ingredient, "count", false));
            }
            CorrelatedIngredientCount = labelCount;
        }
    }

    public sealed class OriginalMysticJassCandidateRow
    {
        public string ResultUnitId { get; private set; }
        public string MatchStatus { get; private set; }
        public IReadOnlyList<string> StrongestEvidenceAbilities { get; private set; }
        public IReadOnlyList<OriginalMysticJassCandidate> CompatibleCandidates { get; private set; }
        public bool AuthorizedForUnitConsumption { get { return false; } }

        internal OriginalMysticJassCandidateRow(Dictionary<string, object> row)
        {
            ResultUnitId = OrdrJson.RequiredString(row, "resultUnitId");
            if (!ResultUnitId.StartsWith("MY-", StringComparison.Ordinal))
                throw new FormatException("신비 조합 결과 ID가 아닙니다.");
            MatchStatus = OrdrJson.RequiredString(row, "matchStatus");
            if (MatchStatus != "positive-label-correlation" &&
                MatchStatus != "structural-only-ambiguous")
                throw new FormatException("원본 후보 신뢰도 상태가 올바르지 않습니다.");
            object permission = OrdrJson.Value(row, "authorizedForUnitConsumption");
            if (!(permission is bool) || (bool)permission ||
                OrdrJson.Value(row, "verifiedResultRawcode") != null)
                throw new FormatException("검증 전에는 원본 조합 결과 매핑을 실행할 수 없습니다.");

            var candidates = new List<OriginalMysticJassCandidate>();
            var abilities = new HashSet<string>(StringComparer.Ordinal);
            foreach (object raw in OrdrJson.Array(OrdrJson.Value(row,
                "compatibleCandidatesUnderHypotheses"), "compatibleCandidatesUnderHypotheses"))
            {
                var candidate = new OriginalMysticJassCandidate(
                    OrdrJson.Object(raw, "candidate"));
                if (!abilities.Add(candidate.AbilityRawcode))
                    throw new FormatException("같은 JASS 조합 후보가 중복되었습니다.");
                candidates.Add(candidate);
            }
            if (candidates.Count == 0)
                throw new FormatException("신비 조합의 JASS 후보가 없습니다.");
            CompatibleCandidates = candidates.AsReadOnly();

            var strongest = new List<string>();
            foreach (object raw in OrdrJson.Array(OrdrJson.Value(row,
                "strongestLabelEvidenceCandidateAbilities"), "strongestLabelEvidenceCandidateAbilities"))
            {
                string ability = raw as string;
                if (ability == null || !abilities.Contains(ability) || strongest.Contains(ability))
                    throw new FormatException("근거가 없는 신비 조합 후보입니다.");
                strongest.Add(ability);
            }
            if (strongest.Count == 0)
                throw new FormatException("가장 유력한 JASS 후보가 없습니다.");
            StrongestEvidenceAbilities = strongest.AsReadOnly();
        }
    }

    public sealed class OriginalMysticJassCandidateCatalog
    {
        private readonly Dictionary<string, OriginalMysticJassCandidateRow> byResult =
            new Dictionary<string, OriginalMysticJassCandidateRow>(StringComparer.Ordinal);

        public int Count { get { return byResult.Count; } }
        public bool AuthorizedForUnitConsumption { get { return false; } }

        public OriginalMysticJassCandidateRow Find(string resultUnitId)
        {
            OriginalMysticJassCandidateRow row;
            return resultUnitId != null && byResult.TryGetValue(resultUnitId, out row)
                ? row : null;
        }

        public static OriginalMysticJassCandidateCatalog FromJson(string json)
        {
            var root = OrdrJson.Object(OrdrJson.Parse(json), "original mystic JASS candidates");
            if (OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "mode") != "악몽 난이도 + 이세계모드 고정" ||
                OrdrJson.RequiredString(root, "sourceJassSha256") !=
                    OriginalVerifiedUnitObjectMap.ExpectedJassSha256 ||
                OtherworldMysticFusionCatalog.NonNegativeInteger(root,
                    "verifiedObjectRawcodeMappings", true) != 0 ||
                !(OrdrJson.Value(root, "authorizedForUnitConsumption") is bool) ||
                (bool)OrdrJson.Value(root, "authorizedForUnitConsumption"))
                throw new FormatException("검증 전의 원본 신비 조합 후보는 읽기 전용이어야 합니다.");

            var catalog = new OriginalMysticJassCandidateCatalog();
            foreach (object raw in OrdrJson.Array(OrdrJson.Value(root, "rows"), "rows"))
            {
                var row = new OriginalMysticJassCandidateRow(
                    OrdrJson.Object(raw, "mystic candidate row"));
                if (catalog.byResult.ContainsKey(row.ResultUnitId))
                    throw new FormatException("신비 결과 조합 후보가 중복되었습니다.");
                catalog.byResult.Add(row.ResultUnitId, row);
            }
            if (catalog.Count != OtherworldMysticFusionCatalog.NonNegativeInteger(root,
                "webRecipeCount", true))
                throw new FormatException("원본 신비 JASS 후보 수가 불일치합니다.");
            return catalog;
        }
    }
}
