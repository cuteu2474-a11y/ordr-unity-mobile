using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // Additional source-verified wallet debits for CWR registrations.
    // A special condition is executable ONLY when its original ability,
    // kind, rawcode and exact count match this allowlist. This is not an
    // free item inventory or unknown transformation-count simulator.
    // Batch 43 adds only source-receipted ITEM AI01/AI02 wallet debits.
    public sealed class Original323FusionSpecialWalletCost
    {
        public string ResourceId { get; }
        public string DisplayName { get; }
        public int Count { get; }

        internal Original323FusionSpecialWalletCost(string resource, string label, int count)
        {
            ResourceId = resource;
            DisplayName = label;
            Count = count;
        }
    }

    public static class Original323FusionSpecialWalletCosts
    {
        private static readonly HashSet<string> GreenBloodMobileResults =
            new HashSet<string>(StringComparer.Ordinal) { "SPH1", "SPH2", "SPH4" };
        public static bool IsApproved(string ability, OriginalJassFusionInstruction condition)
        {
            string resource, label;
            return TryResolve(ability, condition, out resource, out label);
        }

        public static bool TryResolve(string ability, OriginalJassFusionInstruction condition,
            out string resourceId, out string displayName)
        {
            resourceId = null;
            displayName = null;
            if (condition == null) return false;
            // Original CWR A18P: BGI SKPT/SKPT x4. Story stages 8..13 and
            // personal-mission rewards already settle spendable trait points
            // on this exact shared EconomyWallet; no synthetic grant is made.
            if (ability == "A18P" && condition.Kind == "SKPT" &&
                condition.Rawcode == "SKPT" && condition.Count == 4)
            {
                resourceId = OriginalMissionWalletRewards.TraitPointsId;
                displayName = "특성 포인트";
                return true;
            }
            // Source CKe: Egghead stage 14 grants exactly one A13A cast.
            // A13A's h032/h043/h030 replacements have no CEN post-hook.
            // The h03G -> h0A0 branch (SPH3) remains locked pending CEN.
            if (GreenBloodMobileResults.Contains(ability) &&
                condition.Kind == "GREN" && condition.Rawcode == "GREN" &&
                condition.Count == 1)
            {
                resourceId = Original323StoryMobileProgression.GreenBloodUseResourceId;
                displayName = "그린블러드";
                return true;
            }
            return false;
        }

        // Return null for no extra payment, preserving existing free-currency
        // paths. The caller merges this with GOLD/WOOD in ONE TryExchange
        // inside the reversible original-unit swap.
        public static IReadOnlyList<Original323FusionSpecialWalletCost> BuildApprovedCosts(
            string ability, OriginalJassFusionRegistration recipe)
        {
            if (recipe == null || recipe.AbilityRawcode != ability)
                throw new FormatException("원본 특수 비용 조합 등록식이 없습니다: " + ability);
            var costs = new List<Original323FusionSpecialWalletCost>();
            foreach (var instruction in recipe.OrderedConditions)
            {
                string resource, label;
                if (TryResolve(ability, instruction, out resource, out label) ||
                    Original323FusionSourceItemLedger.TryResolve(
                        ability, instruction, out resource, out label))
                    costs.Add(new Original323FusionSpecialWalletCost(resource, label,
                        instruction.Count));
                else if (instruction.Kind == "SKPT" || instruction.Kind == "GREN" ||
                         instruction.Kind == "ITEM")
                    throw new FormatException("원본 특수·아이템 소비 비용 미승인: " + ability);
            }
            return costs.Count == 0 ? null : costs.AsReadOnly();
        }
    }
}
