using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Exact source object fields, NOT retail gameplay verification:
    // UnitBalance.slk HP/manaN/regenHP/regenMana for the pinned 2.323[C] W3X.
    public sealed class SourceModelUnitResourceSpec
    {
        public string CatalogUnitId { get; private set; }
        public string OriginalRawcode { get; private set; }
        public double MaximumHealth { get; private set; }
        public double MaximumMana { get; private set; }
        public double HealthRegeneration { get; private set; }
        public double ManaRegeneration { get; private set; }

        internal SourceModelUnitResourceSpec(string id, string rawcode,
            double health, double mana, double hpRegen, double mpRegen)
        {
            CatalogUnitId = id; OriginalRawcode = rawcode;
            MaximumHealth = health; MaximumMana = mana;
            HealthRegeneration = hpRegen; ManaRegeneration = mpRegen;
        }

        // The source model's HP acts as a spendable unit ability pool.
        // Skills like Robin's 40HP cast and Yamato's 95HP form use EXACTLY
        // their full object baseline HP. Zero balance is allowed only for this
        // explicit model, not for caller-verified runtime survival resources.
        public CharacterResources CreateStartingPool() =>
            new CharacterResources(MaximumHealth, MaximumHealth,
                MaximumMana, MaximumMana, true);
    }

    public sealed class SourceModelUnitResourceCatalog
    {
        public const string Evidence = "MODEL_ONLY: source W3X UnitBalance.slk baseline HP/mana/regen";
        private readonly Dictionary<string, SourceModelUnitResourceSpec> byId =
            new Dictionary<string, SourceModelUnitResourceSpec>(StringComparer.Ordinal);
        public int Count => byId.Count;

        public SourceModelUnitResourceSpec Find(string catalogUnitId)
        {
            SourceModelUnitResourceSpec result;
            return catalogUnitId != null && byId.TryGetValue(catalogUnitId, out result)
                ? result : null;
        }

        private static double ReadStat(Dictionary<string, object> fields,
            string name, string rawcode, bool optional = false)
        {
            object value = OrdrJson.Value(fields, name);
            if (value == null)
            {
                if (optional) return 0;
                throw new FormatException(rawcode + ": 원본 자원 필드 누락: " + name);
            }
            var text = value as string;
            double parsed;
            if (text == null ||
                !double.TryParse(text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out parsed) ||
                double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < 0)
                throw new FormatException(rawcode + ": 원본 자원 수치가 잘못됐습니다: " + name);
            return parsed;
        }

        public static SourceModelUnitResourceCatalog FromOriginalObjectStats(
            string originalSlkJson, OriginalMapBaseStatCatalog mapping)
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));
            var root = OrdrJson.Object(OrdrJson.Parse(originalSlkJson),
                "original-2323C-unit-object-stats");
            if (OrdrJson.RequiredString(root, "format") !=
                    "ordr-original-object-stat-index-v1" ||
                OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "sourceW3xSha256") !=
                    OriginalCommonSaleRules.OriginalMapSha256 ||
                mapping.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256)
                throw new InvalidOperationException("캐릭터 자원 데이터가 원본 W3X와 일치하지 않습니다.");

            var byRaw = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (var raw in OrdrJson.Array(OrdrJson.Value(root, "entries"), "entries"))
            {
                var entry = OrdrJson.Object(raw, "source unit");
                string code = OrdrJson.RequiredString(entry, "rawcode");
                if (code.Length != 4 || byRaw.ContainsKey(code))
                    throw new FormatException("중복되거나 잘못된 SLK 자원 rawcode: " + code);
                var fields = OrdrJson.Object(OrdrJson.Value(entry, "originalObjectFields"),
                    "unit fields");
                var balance = OrdrJson.Object(OrdrJson.Value(fields, "balance"), "balance");
                var stats = new[] {
                    ReadStat(balance, "HP", code),
                    ReadStat(balance, "manaN", code, true),
                    ReadStat(balance, "regenHP", code),
                    ReadStat(balance, "regenMana", code)
                };
                if (stats[0] <= 0 || stats[1] == 0 && stats[3] != 0)
                    throw new FormatException(code + ": 원본 자원 상한/재생이 잘못됐습니다.");
                byRaw.Add(code, stats);
            }

            if (byRaw.Count != 265)
                throw new FormatException("원본 265종 SLK 자원 자료가 누락됐습니다.");
            var output = new SourceModelUnitResourceCatalog();
            foreach (var unit in mapping.All)
            {
                if (unit.OriginalRawcode == null) continue;
                double[] stats;
                if (!byRaw.TryGetValue(unit.OriginalRawcode, out stats)) continue;
                output.byId.Add(unit.UnitId, new SourceModelUnitResourceSpec(
                    unit.UnitId, unit.OriginalRawcode,
                    stats[0], stats[1], stats[2], stats[3]));
            }
            return output;
        }
    }
}
