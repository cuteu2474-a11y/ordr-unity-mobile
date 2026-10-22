using System;

namespace Ordr.Mobile.Core
{
    // JASS CRO (55576..55592): exact A09G common sale milestone rule.
    // Pure, source-backed reward PLAN, not a world transaction. The owning
    // game session must remove the real unit, update LY and grant reward in
    // the same rollback-capable transaction; no standalone wallet here.
    public sealed class OriginalCommonSaleOutcome
    {
        public int TotalSold { get; private set; }
        public int PointsTowardNextWisp { get; private set; }
        public int RandomWispsAwarded { get; private set; }
        public int BonusWoodAwarded { get; private set; }

        internal OriginalCommonSaleOutcome(int total, int progress, int wisps, int wood)
        {
            TotalSold = total;
            PointsTowardNextWisp = progress;
            RandomWispsAwarded = wisps;
            BonusWoodAwarded = wood;
        }
    }

    public static class OriginalCommonSaleRules
    {
        public const string OriginalMapSha256 =
            "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c";
        public const string AbilityRawcode = "A09G";
        public const int MilestoneSaleCount = 3;
        public const int BonusWoodChancePercent = 35;

        public static bool RequiresBonusWoodRoll(int alreadySold)
        {
            if (alreadySold < 0 || alreadySold == int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(alreadySold));
            return (alreadySold + 1) % MilestoneSaleCount == 0;
        }

        // Random sample is an independent integer in inclusive [1,100].
        // It is requested only when the confirmed sale makes LY a multiple
        // of three. Reject RNG consumption on non-milestone sale.
        public static OriginalCommonSaleOutcome PlanAfterConfirmedSale(
            int alreadySold, int? bonusWoodRollOneToHundred)
        {
            bool milestone = RequiresBonusWoodRoll(alreadySold);
            if (milestone)
            {
                if (!bonusWoodRollOneToHundred.HasValue ||
                    bonusWoodRollOneToHundred.Value < 1 ||
                    bonusWoodRollOneToHundred.Value > 100)
                    throw new ArgumentOutOfRangeException(nameof(bonusWoodRollOneToHundred));
            }
            else if (bonusWoodRollOneToHundred.HasValue)
                throw new ArgumentException(
                    "Bonus wood RNG can be consumed only on every third common sale.",
                    nameof(bonusWoodRollOneToHundred));

            int total = checked(alreadySold + 1);
            return new OriginalCommonSaleOutcome(
                total,
                total % MilestoneSaleCount,
                milestone ? 1 : 0,
                milestone && bonusWoodRollOneToHundred.Value <= BonusWoodChancePercent
                    ? 1 : 0);
        }
    }
}
