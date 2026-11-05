using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    // Literal original CJO/CJR/CJJ pools; native selection uses a separate RNG lane.
    public static class Original323UnitGamblingRules
    {
        public static IReadOnlyList<string> Uncommon { get; } = Array.AsReadOnly(new[] { "h00M", "h00A", "h00O", "h00C", "h00D", "h00I", "h00N", "h00E", "h00G", "h00J", "h00K", "h00L", "h00F" });
        public static IReadOnlyList<string> Special { get; } = Array.AsReadOnly(new[] { "h01K", "h00Q", "h00B", "h00P", "h00S", "h00R", "h00W", "h00T", "h00U", "h017", "h016", "h015", "h014", "h013", "h01F", "h012", "h011", "h00Z", "h00Y", "h00X", "h018", "h019", "h01C", "h00V", "h01A", "h01B", "h01D", "h01E", "h01H", "h01G", "h01J", "h01I" });
        public static IReadOnlyList<string> HighSpecial { get; } = Array.AsReadOnly(new[] { "h01K", "h00Q", "h00B", "h00P", "h00S", "h00R", "h00W", "h00T", "h00U", "h017", "h016", "h015", "h014", "h013", "h01F", "h012", "h011", "h00Z", "h00Y", "h00X", "h018", "h019", "h01C", "h00V", "h01A", "h01B", "h01D", "h01E", "h01H", "h01G", "h01J", "h01I" });
        public static IReadOnlyList<string> HighRare { get; } = Array.AsReadOnly(new[] { "h09X", "h01Q", "h025", "h01L", "h022", "h021", "h020", "h01Z", "h01Y", "h01X", "h01W", "h01V", "h01U", "h01T", "h02M", "h01S", "h01R", "h01M", "h01N", "h01O", "h05L", "h01P", "h02I", "h029", "h028", "h027", "h026", "h02A", "h024", "h02C", "h02D", "h02G", "h02F", "h02E", "h04H", "h023", "h02B", "h02H", "h02J", "h02K", "h02L", "h05K" });
        public static IReadOnlyList<string> Common { get; } = Array.AsReadOnly(new[] { "h003", "h002", "h001", "h007", "h004", "h008", "h005", "h009", "h006" });
        public static int GoldCost(OriginalGambleTier tier) => tier == OriginalGambleTier.Low ? 200 : tier == OriginalGambleTier.Middle ? 1000 : 2000;
        public static int WoodCost(OriginalGambleTier tier) => tier == OriginalGambleTier.Low ? 1 : tier == OriginalGambleTier.Middle ? 2 : 4;
        public static string OfferId(OriginalGambleTier tier) => "original:unit-gambling:" + tier;
        public static string Label(OriginalGambleTier tier) => tier == OriginalGambleTier.Low ? "하급" : tier == OriginalGambleTier.Middle ? "중급" : "고급";
    }
}
