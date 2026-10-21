using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // Manually reviewed ORIGINAL W3X object evidence, never a web recipe guess
    // or an unreviewed .w3u name-candidate report. This is the only data-backed
    // implementation of IVerifiedUnitRawcodeMap in the mobile layer.
    public sealed class OriginalVerifiedUnitObjectMap : IVerifiedUnitRawcodeMap
    {
        // Fixed fingerprint of the user-provided S2 2.323[C] JASS extraction.
        public const string ExpectedJassSha256 =
            "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c";

        private readonly Dictionary<string, string> verified =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public int Count { get { return verified.Count; } }

        public bool TryResolveUnit(string gameRawcode, out string catalogUnitId)
        {
            return verified.TryGetValue(gameRawcode ?? "", out catalogUnitId);
        }

        // The reviewed JSON must be authored from inspected original object
        // records. It cannot be generated merely by matching web-dex names.
        // No default mappings are supplied or silently promoted.
        public static OriginalVerifiedUnitObjectMap FromReviewedJson(string json,
            CharacterUnitCatalog catalog, OriginalUnitRawcodeLabelCatalog labels)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (labels == null) throw new ArgumentNullException(nameof(labels));
            var doc = OrdrJson.Object(OrdrJson.Parse(json), "reviewed original object map");
            if (OrdrJson.RequiredString(doc, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(doc, "sourceJassSha256") != ExpectedJassSha256)
                throw new FormatException("원본 2.323[C] 객체 매핑 출처가 다릅니다.");
            if (OrdrJson.RequiredString(doc, "mappingEvidenceLevel") !=
                "manually-reviewed-original-object")
                throw new FormatException("이름 추정 자료를 검증된 객체 매핑으로 승격할 수 없습니다.");

            var result = new OriginalVerifiedUnitObjectMap();
            var usedCatalogIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (object raw in OrdrJson.Array(OrdrJson.Value(doc, "approvedMappings"),
                "approvedMappings"))
            {
                var row = OrdrJson.Object(raw, "reviewed object");
                string rawcode = OrdrJson.RequiredString(row, "rawcode");
                string unitId = OrdrJson.RequiredString(row, "catalogUnitId");
                string sourceFile = OrdrJson.RequiredString(row, "sourceObjectFile");
                string sourceSha256 = OrdrJson.RequiredString(row, "sourceObjectSha256");
                string originalName = OrdrJson.RequiredString(row, "resolvedOriginalObjectName");
                object reviewed = OrdrJson.Value(row, "manuallyReviewed");
                if (rawcode.Length != 4 || sourceFile != "war3map.w3u" &&
                    sourceFile != "Units/UnitData.slk" ||
                    !IsSha256(sourceSha256) || !(reviewed is bool) || !(bool)reviewed ||
                    string.IsNullOrWhiteSpace(originalName))
                    throw new FormatException("원본 객체 검수·증빙이 누락됐습니다: " + rawcode);
                // Ensure the target is a real 271-catalog entry, and do not allow
                // two unrelated original objects to consume the same catalog unit.
                catalog.Require(unitId);
                var jassNameLink = labels.FindRawcode(rawcode);
                if (jassNameLink != null && jassNameLink.CatalogUnitId != unitId)
                    throw new FormatException("원본 JASS 이름 근거와 객체 검수 결과가 충돌: " +
                        rawcode);
                if (result.verified.ContainsKey(rawcode) || !usedCatalogIds.Add(unitId))
                    throw new FormatException("중복 원본 rawcode 또는 카탈로그 ID: " + rawcode);
                result.verified.Add(rawcode, unitId);
            }
            return result;
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char digit in value)
                if (!Uri.IsHexDigit(digit)) return false;
            return true;
        }
    }
}
