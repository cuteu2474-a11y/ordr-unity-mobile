using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // CWR does not contain 266 ordinary recipes: these 24 registrations are
    // notifications, random rewards, or navigation hints. Their result units
    // must arrive through their own source event, never ConfirmFusion().
    public sealed class Original323CwrAcquisitionRoutes
    {
        private static readonly string[,] RandomOnly = {
            { "IS01", "h06U", "RN-01" }, { "IS02", "h06V", "RN-02" },
            { "IS03", "h065", "RN-03" }, { "IS04", "h06X", "RN-04" },
            { "IS05", "h073", "RN-05" }, { "IS06", "h06Z", "RN-06" },
            { "IS07", "h09K", "RN-07" }, { "IS08", "h06W", "RN-08" },
            { "IS09", "h070", "RN-09" }, { "IS10", "h09L", "RN-10" },
            { "IS11", "h06Y", "RN-11" }, { "IS12", "h071", "RN-12" },
            { "IS13", "h06T", "RN-13" }, { "IS14", "h072", "RN-14" }
        };
        private static readonly string[,] RareWispSpecial = {
            { "SP01", "h05H", "X-01" }, { "SP02", "h08N", "X-02" },
            { "SP03", "h05Q", "X-03" }, { "SP04", "h05O", "X-04" },
            { "SP05", "h05P", "X-05" }, { "SP06", "h05R", "X-06" },
            { "SP07", "h09T", "X-07" }, { "SP08", "h0BG", "X-08" }
        };
        private readonly Dictionary<string, string> randomCatalogIds =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public int RandomOnlyRegistrationCount => RandomOnly.GetLength(0);
        public int RareWispSpecialRegistrationCount => RareWispSpecial.GetLength(0);
        public int GuidanceOnlyRegistrationCount => 2;
        public int NonCraftRegistrationCount => RandomOnlyRegistrationCount +
            RareWispSpecialRegistrationCount + GuidanceOnlyRegistrationCount;

        public Original323CwrAcquisitionRoutes(OriginalJassFusionCatalog source,
            CharacterUnitCatalog units, OriginalMapBaseStatCatalog objects)
        {
            if (source == null || units == null || objects == null ||
                source.SourceSha256 != OriginalVerifiedUnitObjectMap.ExpectedJassSha256 ||
                objects.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256)
                throw new ArgumentException("원본 CWR 등록표와 유닛 객체 버전 불일치");
            for (int i = 0; i < RandomOnly.GetLength(0); i++)
            {
                string ability = RandomOnly[i, 0], raw = RandomOnly[i, 1],
                    catalogId = RandomOnly[i, 2];
                RequireSingleRoute(source, ability, raw, "RDUN", "A800", 1);
                var unit = units.Require(catalogId);
                var obj = objects.Find(catalogId);
                bool hasA800 = false;
                if (obj != null)
                    foreach (string native in obj.OriginalAbilityRawcodes)
                        if (native == "A800") { hasA800 = true; break; }
                if (unit.Grade != "랜덤" || obj == null ||
                    obj.OriginalRawcode != raw || !hasA800)
                    throw new FormatException("이세계 원본 랜덤 전용 객체 변경: " + ability);
                randomCatalogIds.Add(raw, catalogId);
            }
            for (int i = 0; i < RareWispSpecial.GetLength(0); i++)
            {
                string ability = RareWispSpecial[i, 0], raw = RareWispSpecial[i, 1],
                    catalogId = RareWispSpecial[i, 2];
                RequireSingleRoute(source, ability, raw, "SPEC", "A801", 1);
                var unit = units.Require(catalogId);
                var obj = objects.Find(catalogId);
                if (unit.Grade != "특수함" || obj == null || obj.OriginalRawcode != raw)
                    throw new FormatException("희귀 위습 1% 특수 보상 객체 변경: " + ability);
            }
            RequireGuidance(source, "UU01", "h05X", 4);
            RequireGuidance(source, "YY26", "h060", 6);
            if (units.Require("R-43").Grade != "희귀함" ||
                objects.Find("R-43")?.OriginalRawcode != "h05X" ||
                units.Require(Original323SourceCommonWispWorld.PirateShipCatalogId).Grade != "히든" ||
                objects.Find(Original323SourceCommonWispWorld.PirateShipCatalogId)?.OriginalRawcode != "h060")
                throw new FormatException("레일리/해적선 표기 원본 객체 불일치");
        }

        public bool TryResolveRandomOnlyAward(string rawcode, out string catalogId)
        {
            return randomCatalogIds.TryGetValue(rawcode ?? "", out catalogId);
        }

        private static void RequireSingleRoute(OriginalJassFusionCatalog catalog,
            string ability, string result, string kind, string conditionRaw, int count)
        {
            var row = catalog.FindByAbility(ability);
            if (row == null || row.Results.Count != 1 ||
                row.Results[0].Kind != "UNIT" || row.Results[0].Count != 1 ||
                row.Results[0].Rawcode != result || row.OrderedConditions.Count != 1 ||
                row.OrderedConditions[0].Kind != kind ||
                row.OrderedConditions[0].Rawcode != conditionRaw ||
                row.OrderedConditions[0].Count != count)
                throw new FormatException("원본 비조합 획득 규칙 변경: " + ability);
        }

        private static void RequireGuidance(OriginalJassFusionCatalog catalog,
            string ability, string result, int markers)
        {
            var row = catalog.FindByAbility(ability);
            if (row == null || row.Results.Count != 1 ||
                row.Results[0].Kind != "UNIT" || row.Results[0].Count != 1 ||
                row.Results[0].Rawcode != result ||
                row.OrderedConditions.Count != markers)
                throw new FormatException("원본 위습 안내 등록 변경: " + ability);
            foreach (var condition in row.OrderedConditions)
                if (condition.Kind != "UPUN" || condition.Count != 1)
                    throw new FormatException("안내용 UPUN을 제작 재료로 사용할 수 없습니다: " + ability);
        }
    }
}
