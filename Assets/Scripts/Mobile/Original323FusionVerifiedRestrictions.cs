using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // CNn/CNm: four no-replacement ban draws at map initialization. These are
    // the exact uX/uY/uZ, ua/ub/uc, ud and uT rawcode pools from pinned JASS.
    // hn[-1, rawcode] is written only by CNm; hm has no SaveBoolean writer.
    // A new mobile session draws its own bans using the source probabilities;
    // it does not pretend to replay Warcraft's unknown original RNG seed.
    public sealed class Original323FusionVerifiedRestrictions : IOriginalJassVerifiedConditionState
    {
        private static readonly string[][] SourceBanPools = {
            new[] { "H099", "H09F", "H08V", "H0B2", "H098", "H09B", "H08Y", "H09I", "H09H", "H09A", "H091",
                "H094", "H08U", "H0B4", "H0BD", "H096", "H0BX", "H092", "H08Z", "H09E", "H090",
                "H095", "H097", "H0B5", "H08X", "H0BL", "H05N", "H08W", "H0CO", "H0AA" },
            new[] { "h04J", "h04B", "h04C", "h04D", "h04E", "h04F", "h049", "h04A", "h04G", "h081", "h04Q", "h07M" },
            new[] { "h05C", "h059", "h058", "h067", "h057", "h0CJ", "h08R", "h05A", "h05B", "H0BK", "h06A" },
            new[] { "h05E", "h08O", "h07I", "h084", "h040", "h08Q", "h05I", "h05F", "h0AI", "h05G" }
        };
        private static readonly int[] SourceBanDrawCounts = { 8, 3, 1, 2 };
        private readonly HashSet<string> banned = new HashSet<string>(StringComparer.Ordinal);
        private readonly GameSession game;
        private int confirmedOriginalRound;

        public Original323FusionVerifiedRestrictions(GameSession sourceRoundSession)
            : this(sourceRoundSession, new Random().Next) { }

        // Inject the integer draw for deterministic no-replacement regression tests.
        public Original323FusionVerifiedRestrictions(GameSession sourceRoundSession,
            Func<int, int> nextIndex)
        {
            game = sourceRoundSession ?? throw new ArgumentNullException(nameof(sourceRoundSession));
            if (nextIndex == null) throw new ArgumentNullException(nameof(nextIndex));
            for (int group = 0; group < SourceBanPools.Length; group++)
            {
                var available = new List<string>(SourceBanPools[group]);
                for (int i = 0; i < SourceBanDrawCounts[group]; i++)
                {
                    int index = nextIndex(available.Count);
                    if (index < 0 || index >= available.Count)
                        throw new ArgumentOutOfRangeException(nameof(nextIndex));
                    string rawcode = available[index];
                    available[index] = available[available.Count - 1];
                    available.RemoveAt(available.Count - 1);
                    if (!banned.Add(rawcode))
                        throw new FormatException("원본 유닛 밴 추첨 풀에 중복 객체가 있습니다.");
                }
            }
        }

        public int BannedUnitCount => banned.Count;
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
            isBanned = banned.Contains(unitRawcode);
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
