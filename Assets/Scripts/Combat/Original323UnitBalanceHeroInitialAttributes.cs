using System;

namespace Ordr.Mobile.Combat
{
    // Derived from the original 2.323[C] W3X Units/UnitBalance.slk.
    // SHA-256 of UnitBalance.slk:
    // f7f26217d8097b9791723feb02d9181277d9afb1947dc0a3bf553ce590ab3af5
    // All 70 H-prefix rows were examined: 4 explicitly state STR/AGI/INT.
    // The other 66 omit those fields. OpenRealm's actual typed sheet parser
    // (sheet/sheet.c FS_LoadSheetTyped: calloc + missing field no write)
    // leaves those specific numeric properties zero, with no inherited
    // previous-row value. This is a source-engine interpretation, not a
    // runtime comparison against the original Blizzard executable.
    public static class Original323UnitBalanceHeroInitialAttributes
    {
        public const string SourceUnitBalanceSlkSha256 =
            "f7f26217d8097b9791723feb02d9181277d9afb1947dc0a3bf553ce590ab3af5";
        public const int ExaminedHeroRawcodes = 70;
        public const int VerifiedInitialStatTriples = 4;
        public const int SparseNumericDefaultRows = 66;
        // Original SLK H rows with all three cells absent (not explicit
        // STR=0, INT=0, AGI=0 text). Keep source provenance separate.
        private static readonly string[] SparseDefaultRawcodes = (
            "H05M H08B H08E H08J H08T H08U H08V H08W H08X H08Y H08Z H090 H091 H092 H093 H094 H095 H096 H097 H098 H099 H09A H09B H09C H09D H09E H09F H09G H09H H09I H0AA H0AB H0AE H0AM H0AO H0AP H0AQ H0AR H0AS H0AT H0AU H0AW H0AY H0AZ H0B0 H0B1 H0B2 H0B3 H0B4 H0B5 H0B6 H0BD H0BE H0BK H0BL H0BN H0BO H0BT H0BV H0BZ H0C0 H0C1 H0C4 H0C9 H0CL H0CO").Split(' ');

        public static void RegisterVerifiedRows(Original323HeroAttributeWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            const string map = Original323HeroAttributeWorld.OriginalW3xSha256;
            // Original SLK headers: STR (X41), INT (X42), AGI (X43).
            world.RegisterOriginalUnitBalanceRow(map, "H05N", 10, 11, 1);
            world.RegisterOriginalUnitBalanceRow(map, "H0A5", 10, 11, 1);
            world.RegisterOriginalUnitBalanceRow(map, "H0BX", 1, 1, 1);
            world.RegisterOriginalUnitBalanceRow(map, "H0BY", 1, 1, 1);
        }

        public static void RegisterSparseNumericDefaults(
            Original323HeroAttributeWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (SparseDefaultRawcodes.Length != SparseNumericDefaultRows)
                throw new InvalidOperationException(
                    "원본 영웅의 누락 필드 수가 원본 SLK 조사 범위와 다릅니다.");
            var seen = new System.Collections.Generic.HashSet<string>(
                StringComparer.Ordinal);
            foreach (var raw in SparseDefaultRawcodes)
            {
                if (!seen.Add(raw))
                    throw new InvalidOperationException(
                        "원본 영웅 누락 필드 목록에 중복 rawcode가 있습니다.");
                world.RegisterOriginalUnitBalanceSparseNumericDefault(
                    Original323HeroAttributeWorld.OriginalW3xSha256,
                    SourceUnitBalanceSlkSha256, raw);
            }
        }
    }
}
