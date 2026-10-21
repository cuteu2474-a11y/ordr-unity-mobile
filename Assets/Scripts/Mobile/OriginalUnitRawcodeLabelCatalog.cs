using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // Bi8 + Bi9 provide explicit rawcode/ability/name evidence in war3map.j.
    // Matching an alias to a catalog name is useful for preview, NOT authorization
    // to spend items or instantiate the referenced rawcode.
    public sealed class OriginalUnitRawcodeLabelLink
    {
        public string Rawcode { get; private set; }
        public string CatalogUnitId { get; private set; }
        public string AbilityRawcode { get; private set; }
        public string UnitDisplayName { get; private set; }

        internal OriginalUnitRawcodeLabelLink(Dictionary<string, object> obj,
            CharacterUnitCatalog catalog)
        {
            Rawcode = OrdrJson.RequiredString(obj, "rawcode");
            CatalogUnitId = OrdrJson.RequiredString(obj, "catalogUnitId");
            AbilityRawcode = OrdrJson.RequiredString(obj, "abilityRawcode");
            if (Rawcode.Length != 4 || AbilityRawcode.Length != 4)
                throw new FormatException("JASS rawcode must contain exactly four characters.");
            UnitDisplayName = catalog.Require(CatalogUnitId).Name;
        }
    }

    public sealed class OriginalUnitRawcodeLabelCatalog
    {
        private readonly Dictionary<string, OriginalUnitRawcodeLabelLink> byRawcode =
            new Dictionary<string, OriginalUnitRawcodeLabelLink>(StringComparer.Ordinal);
        private readonly Dictionary<string, OriginalUnitRawcodeLabelLink> byUnitId =
            new Dictionary<string, OriginalUnitRawcodeLabelLink>(StringComparer.Ordinal);
        private readonly Dictionary<string, OriginalUnitRawcodeLabelLink> byAbility =
            new Dictionary<string, OriginalUnitRawcodeLabelLink>(StringComparer.Ordinal);

        public int Count { get { return byRawcode.Count; } }
        public IEnumerable<OriginalUnitRawcodeLabelLink> All { get { return byRawcode.Values; } }

        public OriginalUnitRawcodeLabelLink FindRawcode(string rawcode)
        {
            OriginalUnitRawcodeLabelLink link;
            return rawcode != null && byRawcode.TryGetValue(rawcode, out link) ? link : null;
        }

        public OriginalUnitRawcodeLabelLink FindUnit(string id)
        {
            OriginalUnitRawcodeLabelLink link;
            return id != null && byUnitId.TryGetValue(id, out link) ? link : null;
        }

        public OriginalUnitRawcodeLabelLink FindAbility(string ability)
        {
            OriginalUnitRawcodeLabelLink link;
            return ability != null && byAbility.TryGetValue(ability, out link) ? link : null;
        }

        public static OriginalUnitRawcodeLabelCatalog FromJson(string json,
            CharacterUnitCatalog units, OriginalJassFusionCatalog originalFusions = null)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            var doc = OrdrJson.Object(OrdrJson.Parse(json), "rawcode evidence");
            if (OrdrJson.RequiredString(doc, "gameVersion") != "S2 2.323[C]")
                throw new FormatException("원본 유닛 매핑 버전 불일치.");
            object spendFlag = OrdrJson.Value(doc, "useForAutomaticUnitConsumption");
            if (!(spendFlag is bool) || (bool)spendFlag)
                throw new FormatException("이름 매칭 자료로 제작 소비를 승인할 수 없습니다.");
            var result = new OriginalUnitRawcodeLabelCatalog();
            foreach (object row in OrdrJson.Array(OrdrJson.Value(doc, "mappings"), "mappings"))
            {
                var link = new OriginalUnitRawcodeLabelLink(
                    OrdrJson.Object(row, "unit label link"), units);
                if (result.byRawcode.ContainsKey(link.Rawcode) ||
                    result.byUnitId.ContainsKey(link.CatalogUnitId) ||
                    result.byAbility.ContainsKey(link.AbilityRawcode))
                    throw new FormatException("중복 JASS 원시 코드 또는 캐릭터 ID: " + link.Rawcode);
                if (originalFusions != null)
                {
                    var recipe = originalFusions.FindByAbility(link.AbilityRawcode);
                    if (recipe == null || recipe.Results.Count != 1 ||
                        recipe.Results[0].Kind != "UNIT" ||
                        recipe.Results[0].Rawcode != link.Rawcode)
                        throw new FormatException("Bi8/ Bi9 원시 코드가 CWR 결과와 일치하지 않습니다: " +
                            link.AbilityRawcode);
                }
                result.byRawcode.Add(link.Rawcode, link);
                result.byUnitId.Add(link.CatalogUnitId, link);
                result.byAbility.Add(link.AbilityRawcode, link);
            }
            return result;
        }
    }
}
