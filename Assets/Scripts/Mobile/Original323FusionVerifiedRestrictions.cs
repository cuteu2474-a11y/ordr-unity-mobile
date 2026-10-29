using System;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // Original mode-selection branch: th[tl] (변질) evaluates oC/CNn and
    // writes hn[-1, rawcode] via CNm. th[tn] (이세계) NEVER evaluates oC.
    // This game is fixed to nightmare-otherworld: no bans are drawn, and
    // xB/UBAN is always false for a valid original unit rawcode.
    public sealed class Original323FusionVerifiedRestrictions : IOriginalJassVerifiedConditionState
    {
        private readonly GameSession game;
        private int confirmedOriginalRound;

        public Original323FusionVerifiedRestrictions(GameSession sourceRoundSession)
        {
            game = sourceRoundSession ?? throw new ArgumentNullException(nameof(sourceRoundSession));
            if (GameSession.ModeId != "nightmare-otherworld")
                throw new NotSupportedException("이세계 모드 전용 조합 조건입니다.");
        }

        public int BannedUnitCount => 0;
        public GameSession SourceSession => game;

        // Call only after a source-approved monster round actually begins.
        // The non-retail Editor preview intentionally does not call this.
        internal void ConfirmObservedOriginalRound(int round)
        {
            if (game.Phase != SessionPhase.Running ||
                round != game.CurrentWave || round != confirmedOriginalRound + 1)
                throw new InvalidOperationException("원본 라운드 진행 증거가 일치하지 않습니다.");
            confirmedOriginalRound = round;
        }

        public bool TryIsUnitBanned(string unitRawcode, out bool isBanned)
        {
            isBanned = false;
            if (string.IsNullOrEmpty(unitRawcode) || unitRawcode.Length != 4)
                return false;
            // CHg's xB = !w8 && w9. In the source's isekai branch CNn/CNm
            // never write hn, hence w9=false and every UBAN check passes.
            return true;
        }

        public bool TryIsTopTierCombinationBlocked(out bool blocked)
        {
            // Original RC[player] starts at 0. The separate 패왕의길 selection
            // (CKQ) is not offered in this mobile build: RC therefore stays 0.
            // Do not conflate it with the implemented 최고의도움 navigation.
            // CHh does not inspect Q7 until RC > 0.
            blocked = false;
            return true;
        }

        public bool TryGetOriginalRoundCounter(out int originalRound)
        {
            // Only the started session's source-advancing round is usable.
            // The UI's round display or elapsed time are not substitutes.
            originalRound = 0;
            if (game.Phase != SessionPhase.Running ||
                confirmedOriginalRound < 1 || confirmedOriginalRound != game.CurrentWave)
                return false;
            originalRound = confirmedOriginalRound;
            return true;
        }
    }
}
