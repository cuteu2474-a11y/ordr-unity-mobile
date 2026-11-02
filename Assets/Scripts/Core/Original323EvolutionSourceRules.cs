using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Core
{
    public sealed class Original323EvolutionPoolUnit
    {
        public string Rawcode { get; private set; }
        public string CatalogId { get; private set; }
        internal Original323EvolutionPoolUnit(string rawcode, string catalogId)
        { Rawcode = rawcode; CatalogId = catalogId; }
    }

    // Pinned war3map.j SHA256 96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d
    // CR0-CR4 (56113-56230): one e0HP -> one jZ[BFi(0,12)] uncommon,
    // independent native PlaceRandomUnit(sg) dormant special, A901 death x152,
    // 101 timer ticks of .02s, then release of the PRESELECTED special.
    // jZ[0..12] originals are initialized at JASS lines 58606-58618.
    public static class Original323EvolutionSourceRules
    {
        public const string SourceJassSha256 =
            "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d";
        public const int RequiredLineKills = 152;
        public const double UnsealSeconds = 2.02;
        public const double UncommonX = -3760.0;
        public const double UncommonY = 3904.0;
        public const double DormantX = -3776.0;
        public const double DormantY = 4480.0;

        private static readonly Original323EvolutionPoolUnit[] Uncommons = {
            new Original323EvolutionPoolUnit("h00M", "U-04"),
            new Original323EvolutionPoolUnit("h00A", "U-13"),
            new Original323EvolutionPoolUnit("h00O", "U-01"),
            new Original323EvolutionPoolUnit("h00C", "U-02"),
            new Original323EvolutionPoolUnit("h00D", "U-03"),
            new Original323EvolutionPoolUnit("h00I", "U-06"),
            new Original323EvolutionPoolUnit("h00N", "U-07"),
            new Original323EvolutionPoolUnit("h00E", "U-09"),
            new Original323EvolutionPoolUnit("h00G", "U-08"),
            new Original323EvolutionPoolUnit("h00J", "U-10"),
            new Original323EvolutionPoolUnit("h00K", "U-05"),
            new Original323EvolutionPoolUnit("h00L", "U-11"),
            new Original323EvolutionPoolUnit("h00F", "U-12")
        };
        private static readonly Original323EvolutionPoolUnit[] Specials = {
            new Original323EvolutionPoolUnit("h01K", "S-01"),
            new Original323EvolutionPoolUnit("h00Q", "S-18"),
            new Original323EvolutionPoolUnit("h00B", "S-09"),
            new Original323EvolutionPoolUnit("h00P", "S-03"),
            new Original323EvolutionPoolUnit("h00S", "S-04"),
            new Original323EvolutionPoolUnit("h00R", "S-06"),
            new Original323EvolutionPoolUnit("h00W", "S-22"),
            new Original323EvolutionPoolUnit("h00T", "S-08"),
            new Original323EvolutionPoolUnit("h00U", "S-07"),
            new Original323EvolutionPoolUnit("h017", "S-28"),
            new Original323EvolutionPoolUnit("h016", "S-10"),
            new Original323EvolutionPoolUnit("h015", "S-11"),
            new Original323EvolutionPoolUnit("h014", "S-12"),
            new Original323EvolutionPoolUnit("h013", "S-13"),
            new Original323EvolutionPoolUnit("h01F", "S-14"),
            new Original323EvolutionPoolUnit("h012", "S-15"),
            new Original323EvolutionPoolUnit("h011", "S-16"),
            new Original323EvolutionPoolUnit("h00Z", "S-20"),
            new Original323EvolutionPoolUnit("h00Y", "S-30"),
            new Original323EvolutionPoolUnit("h00X", "S-21"),
            new Original323EvolutionPoolUnit("h018", "S-23"),
            new Original323EvolutionPoolUnit("h019", "S-24"),
            new Original323EvolutionPoolUnit("h01C", "S-02"),
            new Original323EvolutionPoolUnit("h00V", "S-27"),
            new Original323EvolutionPoolUnit("h01A", "S-29"),
            new Original323EvolutionPoolUnit("h01B", "S-31"),
            new Original323EvolutionPoolUnit("h01D", "S-25"),
            new Original323EvolutionPoolUnit("h01E", "S-26"),
            new Original323EvolutionPoolUnit("h01H", "S-05"),
            new Original323EvolutionPoolUnit("h01G", "S-32"),
            new Original323EvolutionPoolUnit("h01J", "S-19"),
            new Original323EvolutionPoolUnit("h01I", "S-33")
        };

        public static IReadOnlyList<Original323EvolutionPoolUnit> UncommonPool =>
            Array.AsReadOnly(Uncommons);
        public static IReadOnlyList<Original323EvolutionPoolUnit> SpecialPool =>
            Array.AsReadOnly(Specials);

        public static void ValidateCatalog(CharacterUnitCatalog units,
            OriginalMapBaseStatCatalog original)
        {
            if (units == null || original == null)
                throw new ArgumentNullException("원본 유닛 자료가 필요합니다.");
            if (SourceJassSha256 != OriginalWispRewardCatalog.ExpectedJassSha256 ||
                original.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256)
                throw new InvalidOperationException("진화체 원본 지문이 다릅니다.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            Validate(Uncommons, "안흔함", units, original, ids);
            Validate(Specials, "특별함", units, original, ids);
        }

        private static void Validate(Original323EvolutionPoolUnit[] pool,
            string grade, CharacterUnitCatalog units, OriginalMapBaseStatCatalog original,
            HashSet<string> ids)
        {
            foreach (var entry in pool)
            {
                var unit = units.Find(entry.CatalogId);
                var mapped = original.Find(entry.CatalogId);
                if (!ids.Add(entry.Rawcode) || unit == null || mapped == null ||
                    unit.Grade != grade || mapped.OriginalRawcode != entry.Rawcode)
                    throw new InvalidOperationException(
                        "진화체 원본 풀 식별이 불일치합니다: " + entry.Rawcode);
            }
        }
    }
}
