using System;

namespace Ordr.Mobile.Combat
{
    // Source: ORDR S2 2.323[C] war3map.w3a, SHA-256
    // aa938f02c22884f38c5e31bcd6229a35e917a8fab0128331fdab88980cfd50e0
    // Original ability A04R: field isr2, level 5..26 inclusive:
    // level 5 -> float32(-0.04), level 26 -> float32(-0.25).
    // The map DOES NOT override levels 1..4. These field values must not
    // be silently turned into a global incoming-damage reduction formula:
    // Warcraft AIsr's actual attack pipeline is independently verified.
    public static class Ordr323OriginalA04R
    {
        public const string AbilityRawcode = "A04R";
        public const string ObjectField = "isr2";
        public const int FirstVerifiedFieldLevel = 5;
        public const int LastVerifiedFieldLevel = 26;

        public static bool TryReadIsr2(int abilityLevel, out double sourceField)
        {
            sourceField = 0.0;
            if (abilityLevel < FirstVerifiedFieldLevel ||
                abilityLevel > LastVerifiedFieldLevel)
                return false;
            // The Warcraft w3a field is IEEE-754 float32, not double.
            sourceField = (double)(float)(0.01 * (1 - abilityLevel));
            return true;
        }

        // war3map.j Bd8: if Hk > 0 and current level == 0, first
        // UnitAddAbility (sets level 1), then SetUnitAbilityLevel(current+Hk).
        // Level 26 is the highest known edited value, NOT a verified
        // Warcraft engine max. Fail closed above the source-backed range.
        public static bool TryPlanIncrease(int currentLevel, int delta, out int nextLevel)
        {
            nextLevel = 0;
            if (currentLevel < 0 || currentLevel > LastVerifiedFieldLevel ||
                delta <= 0 || delta > 26) return false;
            long candidate = (currentLevel == 0 ? 1L : currentLevel) + delta;
            if (candidate < FirstVerifiedFieldLevel ||
                candidate > LastVerifiedFieldLevel) return false;
            nextLevel = (int)candidate;
            double verified;
            return TryReadIsr2(nextLevel, out verified);
        }
    }
}
