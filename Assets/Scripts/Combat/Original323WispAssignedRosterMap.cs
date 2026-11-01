using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // Source Wisp reward IDs only. This map cannot authorize fusion recipes,
    // free spawns or Wisp debits without a committed source-backed Wisp draw.
    public sealed class Original323WispAssignedRosterMap : IVerifiedUnitRawcodeMap
    {
        private readonly Dictionary<string, string> ids =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public int Count { get { return ids.Count; } }

        public bool TryResolveUnit(string rawcode, out string catalogId)
        {
            return ids.TryGetValue(rawcode ?? "", out catalogId);
        }

        public static Original323WispAssignedRosterMap FromSourceJson(
            string identityJson, string poolJson,
            CharacterUnitCatalog units, OriginalMapBaseStatCatalog mapObjects)
        {
            if (identityJson == null || poolJson == null ||
                units == null || mapObjects == null)
                throw new ArgumentNullException(nameof(identityJson));
            if (mapObjects.SourceW3xSha256 != OriginalWispRewardCatalog.ExpectedMapSha256)
                throw new FormatException("위습 맵 객체 원본 해시 불일치");
            var doc = OrdrJson.Object(OrdrJson.Parse(identityJson), "wisp identities");
            var pool = OrdrJson.Object(OrdrJson.Parse(poolJson), "source Wisp pool");
            if (OrdrJson.RequiredString(doc, "format") !=
                    "ordr-wisp-character-identity-assignments-v2" ||
                OrdrJson.RequiredString(doc, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(pool, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(doc, "sourceMapSha256") !=
                    OriginalWispRewardCatalog.ExpectedMapSha256 ||
                OrdrJson.RequiredString(doc, "sourceJassSha256") !=
                    OriginalWispRewardCatalog.ExpectedJassSha256 ||
                OrdrJson.RequiredString(pool, "sourceMapSha256") !=
                    OriginalWispRewardCatalog.ExpectedMapSha256 ||
                OrdrJson.RequiredString(pool, "sourceJassSha256") !=
                    OriginalWispRewardCatalog.ExpectedJassSha256)
                throw new FormatException("위습 2.323[C] 출처 불일치");

            var rawWisps = OrdrJson.Array(OrdrJson.Value(pool, "wisps"), "source wisps");
            var assignedWisps = OrdrJson.Array(OrdrJson.Value(doc, "wisps"), "assigned wisps");
            if (rawWisps.Count != 4 || assignedWisps.Count != 4)
                throw new FormatException("자동 위습 네 종류가 모두 필요합니다.");
            var result = new Original323WispAssignedRosterMap();
            var catalogIds = new HashSet<string>(StringComparer.Ordinal);
            for (int w = 0; w < rawWisps.Count; w++)
            {
                var originalWisp = OrdrJson.Object(rawWisps[w], "original wisp");
                var mappedWisp = OrdrJson.Object(assignedWisps[w], "mapped wisp");
                string rawWisp = OrdrJson.RequiredString(originalWisp, "wispRawcode");
                if (rawWisp != OrdrJson.RequiredString(mappedWisp, "wispRawcode") ||
                    !WispResourceIds.IsAuto("wisp:" + rawWisp))
                    throw new FormatException("위습 원본 종류 또는 순서 불일치");
                var rawBranches = OrdrJson.Array(
                    OrdrJson.Value(originalWisp, "branches"), "source branches");
                var mapBranches = OrdrJson.Array(
                    OrdrJson.Value(mappedWisp, "branches"), "mapped branches");
                if (rawBranches.Count != mapBranches.Count)
                    throw new FormatException("위습 원본 추첨 분기 불일치");
                for (int b = 0; b < rawBranches.Count; b++)
                {
                    var originalBranch = OrdrJson.Object(rawBranches[b], "original branch");
                    var mappedBranch = OrdrJson.Object(mapBranches[b], "mapped branch");
                    if (OrdrJson.Number(originalBranch, "chancePercent") !=
                            OrdrJson.Number(mappedBranch, "chancePercent") ||
                        OrdrJson.String(originalBranch, "bonusAbilityRawcode") !=
                            OrdrJson.String(mappedBranch, "bonusAbilityRawcode"))
                        throw new FormatException("위습 원본 분기 확률/효과 변경");
                    var originalEntries = OrdrJson.Array(
                        OrdrJson.Value(originalBranch, "entries"), "source entries");
                    var mappedEntries = OrdrJson.Array(
                        OrdrJson.Value(mappedBranch, "entries"), "mapped entries");
                    if (originalEntries.Count != mappedEntries.Count)
                        throw new FormatException("위습 원본 보상 대상 누락");
                    for (int i = 0; i < originalEntries.Count; i++)
                    {
                        string rawcode = OrdrJson.RequiredString(
                            OrdrJson.Object(originalEntries[i], "original entry"), "rawcode");
                        var assigned = OrdrJson.Object(mappedEntries[i], "assigned entry");
                        string id = OrdrJson.RequiredString(assigned, "catalogUnitId");
                        CatalogUnit unit = units.Find(id);
                        OriginalMapCharacterBasics obj = mapObjects.Find(id);
                        if (rawcode.Length != 4 ||
                            rawcode != OrdrJson.RequiredString(assigned, "rawcode") ||
                            unit == null || obj == null ||
                            obj.OriginalRawcode != rawcode ||
                            unit.Name != OrdrJson.RequiredString(assigned, "catalogUnitName") ||
                            unit.Grade != OrdrJson.RequiredString(assigned, "catalogGrade") ||
                            obj.MapName != OrdrJson.RequiredString(assigned, "originalW3xObjectName") ||
                            result.ids.ContainsKey(rawcode) || !catalogIds.Add(id))
                            throw new FormatException("위습 캐릭터 객체 매핑 불일치: " + rawcode);
                        result.ids.Add(rawcode, id);
                    }
                }
            }
            if (result.Count != 156 || catalogIds.Count != 156)
                throw new FormatException("위습 156개 캐릭터 매핑 누락");
            return result;
        }
    }
}
