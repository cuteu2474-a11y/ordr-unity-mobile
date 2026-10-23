using System;

namespace Ordr.Mobile.Combat
{
    // S2 2.323[C] war3map.j CJZ, ability A0J2. This is only the proven
    // target/preflight contract. CJY's dynamic fusion recipe graph must be
    // verified before any unit is removed or ingredients are created.
    public static class Ordr323OriginalHelperAlchemy
    {
        public const string AbilityRawcode = "A0J2";
        public const string OwnerAbilityA = "A0BA";
        public const string OwnerAbilityB = "A0B9";
        public const string SpecialUnitRawcode = "h01C";
        public const string RareUnitRawcode = "h015";
        public const double ManaCost = 100;
        public const double CooldownSeconds = 5;
        public const double CastRange = 99999;

        // CJZ rejects a target that is not owned by the casting player, and
        // accepts it if ANY of the exact four rawcode/ability tests passes.
        // This is not a generic unit-grade check: some map units bypass the
        // ability markers via the h01C / h015 rawcode exceptions.
        public static bool IsOriginalTargetEligible(int casterPlayerId,
            int targetOwnerPlayerId, string targetUnitRawcode,
            int a0baLevel, int a0b9Level)
        {
            if (casterPlayerId < 0 || targetOwnerPlayerId < 0 ||
                a0baLevel < 0 || a0b9Level < 0)
                throw new ArgumentOutOfRangeException("원본 대상 소유자/능력 레벨이 올바르지 않습니다.");
            if (string.IsNullOrWhiteSpace(targetUnitRawcode)) return false;
            return casterPlayerId == targetOwnerPlayerId &&
                (a0baLevel > 0 || a0b9Level > 0 ||
                 targetUnitRawcode == SpecialUnitRawcode ||
                 targetUnitRawcode == RareUnitRawcode);
        }
    }

    // The source JASS consumes the target using BdD and reconstructs its
    // ingredients via R3/Wb/Wq/Wr/Ws -> CJY. Do not expose a cast button or
    // implement a guessed refund until all five original tables are ported.
    public interface IOrdr323VerifiedAlchemyRecipeWorld
    {
        bool CanRestoreEveryOriginalRecipeIngredient(string targetRawcode);
    }
}
