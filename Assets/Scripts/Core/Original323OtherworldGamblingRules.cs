using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    public enum Original323GamblingNavigation { None, Casino, RiskHedge, ExchangeHouse }

    // CJM uses BFi for BOTH the branch and array index. Unlike CJF, it
    // does not use PlaceRandomUnit's separate native pool random stream.
    public static class Original323OtherworldGamblingRules
    {
        public const int GoldCost = 3500;
        public const int WoodCost = 5;
        public const int BaseSuccessPercent = 15;
        public const string ChargesResource = "source:unit-gambling:CJM:charges";
        // Original BdL(JG, 1): casino navigation kW[JG]==ka adds one more.
        // Used by story rewards, the first n00G kill, and sea-king CNU.
        public static int ChargesPerBdLEvent(bool casinoNavigation) => casinoNavigation ? 2 : 1;
        public const string AttemptsResource = "source:unit-gambling:CJM:attempts";
        public const string WinsResource = "source:unit-gambling:CJM:wins";
        public const string RiskBonusResource = "source:unit-gambling:CJM:kc";
        public const string TokenRawcode = "h06G";
        public const string RandomBonusAbility = "A0YZ";
        public const string RareBonusAbility = "A0YE";

        // Literal ke[0..13] order, not the IS01..IS14 registration order.
        private static readonly string[] random = {
            "h06U", "h06V", "h070", "h06T", "h09L", "h06Z", "h073",
            "h09K", "h06Y", "h071", "h072", "h06W", "h065", "h06X"
        };
        // CJM's casino branch explicitly draws kd[0..41]. It excludes
        // kd[42]=h05X even though the graduated CJF shop includes it.
        private static readonly string[] rare = {
            "h09X", "h01Q", "h025", "h01L", "h022", "h021", "h020",
            "h01Z", "h01Y", "h01X", "h01W", "h01V", "h01U", "h01T",
            "h02M", "h01S", "h01R", "h01M", "h01N", "h01O", "h05L",
            "h01P", "h02I", "h029", "h028", "h027", "h026", "h02A",
            "h024", "h02C", "h02D", "h02G", "h02F", "h02E", "h04H",
            "h023", "h02B", "h02H", "h02J", "h02K", "h02L", "h05K"
        };
        public static IReadOnlyList<string> RandomRawcodes => Array.AsReadOnly(random);
        public static IReadOnlyList<string> CasinoRareRawcodes => Array.AsReadOnly(rare);

        public static int SuccessPercent(Original323GamblingNavigation navigation, long bonus)
        {
            if (!Enum.IsDefined(typeof(Original323GamblingNavigation), navigation) || bonus < 0)
                throw new ArgumentOutOfRangeException(nameof(navigation));
            // Source e2 can exceed 100; all valid rolls then succeed.
            return navigation == Original323GamblingNavigation.RiskHedge
                ? (int)Math.Min(100L, BaseSuccessPercent + Math.Min(bonus, 100L))
                : BaseSuccessPercent;
        }
    }

    public sealed class Original323OtherworldGamblingOutcome
    {
        public bool Won { get; }
        public string Rawcode { get; }
        public string BonusAbility { get; }
        public Original323GamblingNavigation Navigation { get; }
        public Original323OtherworldGamblingOutcome(bool won, string rawcode,
            Original323GamblingNavigation navigation)
        {
            if (!Enum.IsDefined(typeof(Original323GamblingNavigation), navigation))
                throw new ArgumentOutOfRangeException(nameof(navigation));
            bool random = Contains(Original323OtherworldGamblingRules.RandomRawcodes, rawcode);
            bool casinoRare = Contains(Original323OtherworldGamblingRules.CasinoRareRawcodes, rawcode);
            if ((won && !random) || (!won && rawcode != Original323OtherworldGamblingRules.TokenRawcode &&
                !(navigation == Original323GamblingNavigation.Casino && casinoRare)))
                throw new ArgumentException("CJM 원본 결과 분기와 객체가 일치하지 않습니다.");
            Won = won; Rawcode = rawcode; Navigation = navigation;
            BonusAbility = won ? Original323OtherworldGamblingRules.RandomBonusAbility :
                casinoRare ? Original323OtherworldGamblingRules.RareBonusAbility : null;
        }
        private static bool Contains(IReadOnlyList<string> values, string rawcode)
        {
            foreach (string value in values) if (value == rawcode) return true;
            return false;
        }
    }
}
