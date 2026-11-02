using System;

namespace Ordr.Mobile.Combat
{
    // Directly transcribed from pinned war3map.j:
    // SHA256 96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d
    // Sugar Btq(18942), Blackbeard Boa/BoZ(15260), Ryuma Bos(15414),
    // Zoro Bsk/Bsj(18058), Kuma BtL(18500), original Bdi/Bdl(8508).
    // These are SOURCE branch facts, not permission to infer native armor,
    // ability immunity, loot payout or standalone summon attack cadence.
    public static class SourceModelOriginalSpecialCallbackRules
    {
        public const string JassSha256 =
            "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d";
        public const int SugarToyRollSides = 1000;
        public const int SugarToyRollSuccess = 8;
        public const int SugarLootAbilityRollSuccess = 33;
        public const int SugarSuicideRollSuccess = 10;
        public const double SugarToyLife = 4.5;
        public const double SugarSuicideLife = 3.0;
        public const int BlackbeardProcPercent = 12;
        public const double BlackbeardPullAnchor = 82.0;
        public const double BlackbeardDamage = 15000.0;
        public const double RyumaAlwaysDamage = 2000.0;
        public const double RyumaAreaRadius = 375.0;
        public const double RyumaAreaDamage = 4500.0;
        public const int RyumaAreaChancePercent = 10;
        public const int ZoroBaseProcPercent = 10;
        public const double ZoroExecuteThreshold = .15;
        public const double ZoroDirectDamage = 900000.0;
        public const double ZoroAreaDamage = 150000.0;
        public const double ZoroAreaRadius = 385.0;
        public const int KumaFullMana = 115;
        public const int KumaManaGainPerAttack = 1;
        public const double KumaAreaRadius = 600.0;
        public const int KumaAdditionalProcPercent = 10;
        public const double KumaAdditionalAreaRadius = 500.0;
        public const double KumaAdditionalAreaDamage = 200000.0;

        public static bool TryGetUnit(string originalRawcode, out string catalogId)
        {
            switch (originalRawcode)
            {
                case "h037": catalogId = "L-19"; return true;
                case "h021": catalogId = "R-03"; return true;
                case "h025": catalogId = "R-07"; return true;
                case "h02S": catalogId = "L-27"; return true;
                case "h030": catalogId = "L-32"; return true;
                default: catalogId = null; return false;
            }
        }

        public static bool SugarToyBranch(int source1Through1000) =>
            source1Through1000 >= 1 && source1Through1000 <= SugarToyRollSuccess;

        public static bool SugarSuicideBranch(int source1Through100) =>
            source1Through100 >= 1 && source1Through100 <= SugarSuicideRollSuccess;

        public static bool ZoroSourceExecuteCondition(
            bool sourceBdlEligible, long currentHealth, long maximumHealth)
        {
            if (maximumHealth <= 0 || currentHealth <= 0 ||
                currentHealth > maximumHealth) return false;
            return sourceBdlEligible &&
                currentHealth <= maximumHealth * ZoroExecuteThreshold;
        }

        public static double RyumaHealthBonus(int sourceAbilityLevel,
            double currentHealth)
        {
            if (sourceAbilityLevel < 0 || currentHealth < 0 ||
                double.IsNaN(currentHealth) || double.IsInfinity(currentHealth))
                throw new ArgumentOutOfRangeException(nameof(sourceAbilityLevel));
            return currentHealth * .1 *
                Ordr323MapDamageMath.ScriptHealthProportionalCorrection(sourceAbilityLevel);
        }
    }
