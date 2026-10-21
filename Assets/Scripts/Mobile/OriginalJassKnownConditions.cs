using System;

namespace Ordr.Mobile.Mobile
{
    // Only values backed by the matching Warcraft III JASS state may be supplied.
    // Values must be for the same player whose fusion command is being evaluated.
    public interface IOriginalJassVerifiedConditionState
    {
        // xB(player, unitRawcode) from CHg: true means the unit is banned.
        bool TryIsUnitBanned(string unitRawcode, out bool banned);

        // RC[player], hr, Q7[player] from CHh.
        bool TryIsTopTierCombinationBlocked(out bool blocked);

        // X3 from CH3 (actual JASS game-round counter, not an arbitrary UI wave).
        bool TryGetOriginalRoundCounter(out int originalRound);
    }

    // Implements only the JASS semantics that can be grounded in CH3/CHg/CHh.
    // Every other specialised condition remains blocked until its check AND
    // corresponding payment/side-effect handler are implemented.
    public sealed class OriginalJassKnownConditions : IOriginalJassSpecialConditionEvaluator
    {
        private readonly IOriginalJassVerifiedConditionState state;

        public OriginalJassKnownConditions(IOriginalJassVerifiedConditionState verifiedState)
        {
            state = verifiedState ?? throw new ArgumentNullException(nameof(verifiedState));
        }

        public bool CanSatisfy(OriginalJassFusionInstruction instruction,
            string abilityRawcode, out string rejectionReason)
        {
            if (instruction == null) throw new ArgumentNullException(nameof(instruction));
            rejectionReason = null;
            switch (instruction.Kind)
            {
                case "UPUN":
                    // CHV/CHX display an additional preview/selection marker.
                    // No BGR/BGS handler for UPUN is registered in CH5/CHi.
                    // Transaction code must still preserve any other original
                    // effects of the selected ability; this marker alone costs zero.
                    return true;

                case "UBAN":
                    bool banned;
                    if (!state.TryIsUnitBanned(instruction.Rawcode, out banned))
                    {
                        rejectionReason = "유닛 밴 여부 미확인";
                        return false;
                    }
                    rejectionReason = banned ? "원본에서 밴된 유닛" : null;
                    return !banned;

                case "KING":
                    bool blocked;
                    if (!state.TryIsTopTierCombinationBlocked(out blocked))
                    {
                        rejectionReason = "원본 항법/최상위 유닛 제한 미확인";
                        return false;
                    }
                    rejectionReason = blocked ? "원본 항법이 최상위 조합을 제한" : null;
                    return !blocked;

                case "RMAX":
                    int originalRound;
                    if (!state.TryGetOriginalRoundCounter(out originalRound))
                    {
                        rejectionReason = "원본 라운드 카운터 미확인";
                        return false;
                    }
                    rejectionReason = originalRound <= instruction.Count
                        ? null : "이 라운드에서는 원본 조합 불가";
                    return originalRound <= instruction.Count;

                default:
                    // Includes ITEM, PICK, IFCT, SKPT, CHCT, RDUN, SPEC, GREN.
                    // Even where a check is obvious, the matching JASS effect
                    // (item removal, point payment, transformation mutation,
                    // additional wood tax) must be reproduced atomically first.
                    rejectionReason = "검증된 검사/소비 핸들러 없음: " + instruction.Kind;
                    return false;
            }
        }
    }
}
