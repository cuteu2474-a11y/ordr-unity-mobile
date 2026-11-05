using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    public enum Original323GreenBloodActionKind { Transform, Enhance }

    // Original war3map.j CKe 50636..50698 (2.323[C]).
    // A13A is awarded by CRD at Egghead clear. This is a single-use
    // target spell, not an immediate random transformation.
    public sealed class Original323GreenBloodPlan
    {
        public Original323GreenBloodActionKind Kind { get; }
        public string OriginalTargetRawcode { get; }
        public string ReplacementRawcode { get; }
        public string[] AddedAbilityRawcodes { get; }
        public bool ExecuteOriginalCENAfterTransformation { get; }
        internal Original323GreenBloodPlan(Original323GreenBloodActionKind kind,
            string target, string replacement, bool runCen, string[] abilities)
        {
            Kind = kind;
            OriginalTargetRawcode = target;
            ReplacementRawcode = replacement;
            ExecuteOriginalCENAfterTransformation = runCen;
            AddedAbilityRawcodes = abilities;
        }
    }

    public static class Original323StoryGreenBloodRules
    {
        // war3map.j CKb/CKc/CKd; native AbilityData.slk proves A13C/AIas\n        // DataA1=.30 and A134/Aimp is a zero-data marker (not a proc).
        public const double EnhancementBurstRadius = 825;
        public const double EnhancementBurstFlatDamage = 400000;
        public const double EnhancementBurstEnemyMaxHealthRatio = 0.004;
        public const double EnhancementAttackSpeedPercent = 30;

        public static double EnhancementBurstDamage(double enemyMaximumHealth)
        {
            if (double.IsNaN(enemyMaximumHealth) ||
                double.IsInfinity(enemyMaximumHealth) || enemyMaximumHealth < 0)
                throw new ArgumentOutOfRangeException(nameof(enemyMaximumHealth));
            return EnhancementBurstFlatDamage +
                enemyMaximumHealth * EnhancementBurstEnemyMaxHealthRatio;
        }

        public static string SeraphimRecipe(string originalUnitRawcode)
        {
            switch (originalUnitRawcode)
            {
                case "h032": return "SPH1";
                case "h043": return "SPH2";
                case "h03G": return "SPH3";
                case "h030": return "SPH4";
                default: return null;
            }
        }

        // Four EXACT special Seraphim replacements in CKe. Unknown targets
        // never receive a guessed Seraphim unit ID.
        private static readonly Dictionary<string, string> replacements =
            new Dictionary<string, string>(StringComparer.Ordinal) {
                {"h032","h09Y"}, {"h043","h0A3"},
                {"h030","h0A1"}, {"h03G","h0A0"}
            };

        public static bool TryPlan(string originalUnitRawcode,
            bool targetOwned, bool hasA912, bool hasA07N,
            out Original323GreenBloodPlan plan)
        {
            plan = null;
            if (string.IsNullOrWhiteSpace(originalUnitRawcode) ||
                originalUnitRawcode.Length != 4 || !targetOwned) return false;
            string result;
            if (replacements.TryGetValue(originalUnitRawcode, out result))
            {
                plan = new Original323GreenBloodPlan(
                    Original323GreenBloodActionKind.Transform,
                    originalUnitRawcode, result, originalUnitRawcode == "h03G",
                    Array.Empty<string>());
                return true;
            }
            if (!hasA912 && !hasA07N) return false;
            plan = new Original323GreenBloodPlan(
                Original323GreenBloodActionKind.Enhance,
                originalUnitRawcode, null, false,
                new[] {"A134", "A13C"});
            return true;
        }
    }
}
