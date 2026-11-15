using System;

namespace Ordr.Mobile.Combat
{
    // Derived from the original 2.323[C] W3X Units/UnitBalance.slk.
    // SHA-256 of UnitBalance.slk:
    // f7f26217d8097b9791723feb02d9181277d9afb1947dc0a3bf553ce590ab3af5
    // All 70 H-prefix rows were examined: exactly these four have three
    // EXPLICIT native STR, AGI and INT cells. The other 66 rows omit them;
    // never interpret those omissions as verified numeric zero.
    public static class Original323UnitBalanceHeroInitialAttributes
    {
        public const string SourceUnitBalanceSlkSha256 =
            "f7f26217d8097b9791723feb02d9181277d9afb1947dc0a3bf553ce590ab3af5";
        public const int ExaminedHeroRawcodes = 70;
        public const int VerifiedInitialStatTriples = 4;

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
    }
}
