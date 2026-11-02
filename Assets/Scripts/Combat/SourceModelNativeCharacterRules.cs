using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // Extracted from ORDR S2 2.323[C] war3map.j SHA
    // 96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d
    // Chopper Byb 24257, Vegapunk B4Z/B4a/B4l 32163-32238,
    // Kaido CBL/CBM 39925-39987.
    // Attack-event checks are NOT verification of native Warcraft metamorphosis.
    public static class SourceModelNativeCharacterRules
    {
        public const string JassSha256 =
            "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d";
        // Story BdL and personal missions credit this same shared resource.
        // The dedicated skill port must never use a second, invisible balance.
        public const string TraitPointsResourceId = OriginalMissionWalletRewards.TraitPointsId;
        public const string WoodResourceId = "wood";
        public const string Vegapunk = "TR-30";
        public const string Chopper = "TR-23";
        public const string Kaido = "I-11";
        public const int ChopperTransformMana = 150;
        public const int VegapunkMainMana = 200;
        public const double VegapunkMainDelaySeconds = 1.23;
        public const int KaidoTransformRollPercent = 14;
        public const int KaidoFirstSpecialRollPercent = 8;
        public const int KaidoSecondSpecialRollPercent = 15;
        public const int KaidoSourceCounterThreshold = 10;

        // CBL checks A0PS before rolling. The pinned object list for
        // original h07M has A0P0 but NO A0PS; transformed h0AD HAS A0PS.
        // B05F native buff status remains unobserved in offline practice.
        public static bool KaidoCanRollAutoMorph(string rawcode) => rawcode == "h0AD";

        // B_x/B_1/B_5 increment GetUnitUserData by 2; CBE/CBI by 1.
        public static int KaidoUserDataIncrease(int branch) =>
            branch == 8 || branch == 15 ? 2 : branch == 0 ? 1 : 0;

        public static bool TryGetNativeAttack(string rawcode, out string unitId)
        {
            switch (rawcode)
            {
                case "H091": unitId = Chopper; return true;
                case "H0AA": unitId = Vegapunk; return true;
                case "h07M": unitId = Kaido; return true;
                default: unitId = null; return false;
            }
        }

        public static string SatelliteName(string rawcode)
        {
            switch (rawcode)
            {
                case "H0AB": return "릴리스";
                case "H0AE": return "아틀라스";
                case "H0AM": return "요크";
                default: return null;
            }
        }

        public static string SatelliteActionId(string rawcode)
        {
            if (SatelliteName(rawcode) == null)
                throw new ArgumentException("원본 베가펑크 위성 코드 불일치", nameof(rawcode));
            return "vegapunk-satellite:" + rawcode;
        }

        // B4a: cr starts at 1. Lilith/Atlas consume 2*cr trait points
        // and 7*cr lumber, and increase cr; York spends 4 points, no lumber.
        // JASS c6 + 2 belongs to the original progression system and is
        // NOT an extra wallet payout.
        public static bool TryPlanSatelliteCost(string rawcode, int previousCoreSatellites,
            out long traitPoints, out long wood)
        {
            traitPoints = wood = 0;
            if (previousCoreSatellites < 0 || previousCoreSatellites > 2)
                return false;
            if (rawcode == "H0AM") { traitPoints = 4; return true; }
            if (rawcode != "H0AB" && rawcode != "H0AE") return false;
            int stage = checked(previousCoreSatellites + 1);
            traitPoints = checked(2L * stage);
            wood = checked(7L * stage);
            return true;
        }
    }

    public sealed class SourceModelVegapunkSatellite
    {
        public long Id { get; private set; }
        public long CreatorId { get; private set; }
        public string OriginalRawcode { get; private set; }
        public string Name { get; private set; }
        public double CreatedAt { get; private set; }
        // The offline SourceModel now schedules map-object basic attacks for
        // these actors. Warcraft-native splash/abilities and final parity are
        // still NOT independently witnessed, hence this flag remains false.
        public bool IndependentCombatVerified => false;

        internal SourceModelVegapunkSatellite(long id, long creator, string raw,
            double createdAt)
        {
            if (id <= 0 || creator <= 0 ||
                SourceModelNativeCharacterRules.SatelliteName(raw) == null ||
                double.IsNaN(createdAt) || double.IsInfinity(createdAt))
                throw new ArgumentException("베가펑크 위성 상태가 올바르지 않습니다.");
            Id = id; CreatorId = creator;
            OriginalRawcode = raw;
            Name = SourceModelNativeCharacterRules.SatelliteName(raw);
            CreatedAt = createdAt;
        }
    }
}
