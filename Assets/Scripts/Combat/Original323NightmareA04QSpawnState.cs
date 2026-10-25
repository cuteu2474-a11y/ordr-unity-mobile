using System;

namespace Ordr.Mobile.Combat
{
    // These event names denote *observed JASS operations*, not UI wishes
    // or inferred skill effects. No event is automatically authorized here.
    public enum Original323NightmareA04QEvent
    {
        WarcuryActivated,
        NikaCanceledWarcury,
        LineItemAI19Used,
        BrookTraitTR12Applied,
        OnePieceTreasureCompleted
    }

    public interface IOriginal323NightmareA04QEventWitness
    {
        bool TryConfirm(Original323NightmareA04QEvent originalEvent,
            int playerSlot, out string observedJassEvidence);
    }

    // Predicts the A04Q level assigned to the NEXT spawned monster by
    // Bd4(Cm,LK[JG]) on line spawns, or SetUnitAbilityLevel(Cm,A04Q,NJ)
    // on bosses. A unit's LATER Bd5 changes are deliberately excluded.
    // This cannot prove final HP, armor, current active immunity or timing.
    public sealed class Original323NightmareA04QSpawnState
    {
        public const int NightmareInitialLevel = 16;
        public const int GlobalEventPlayerSlot = -1;
        public const string SourceWar3mapJSha256 =
            "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d";

        private readonly int[] lineLevels = {
            NightmareInitialLevel, NightmareInitialLevel,
            NightmareInitialLevel, NightmareInitialLevel
        };
        private int bossLevel = NightmareInitialLevel;
        private bool warcuryActive;

        public bool IsWarcuryActive { get { return warcuryActive; } }

        private static bool ValidPlayerSlot(int slot)
        {
            return slot >= 0 && slot < 4;
        }

        public bool TryGetNextSpawnLevel(CharacterEnemyKind kind, int playerSlot,
            out int level)
        {
            level = 0;
            if (kind == CharacterEnemyKind.Boss &&
                playerSlot == GlobalEventPlayerSlot)
            {
                level = bossLevel;
                return true;
            }
            if ((kind == CharacterEnemyKind.Line ||
                 kind == CharacterEnemyKind.Enraged) && ValidPlayerSlot(playerSlot))
            {
                level = lineLevels[playerSlot];
                return true;
            }
            return false;
        }

        // Only the supported original AbilityData.slk+war3map.w3a levels
        // may be turned into a numeric magic-resistance percentage.
        public bool TryGetNextSpawnMagicResistance(CharacterEnemyKind kind,
            int playerSlot, out double percent)
        {
            percent = 0;
            int level;
            return TryGetNextSpawnLevel(kind, playerSlot, out level) &&
                Ordr323VerifiedDefenseAbilities.TryGetMagicResistancePercent(
                    level, out percent);
        }

        private static bool ValidEventEvidence(string evidence)
        {
            return !string.IsNullOrWhiteSpace(evidence) &&
                evidence.IndexOf("TEST_ONLY", StringComparison.OrdinalIgnoreCase) < 0 &&
                evidence.IndexOf("SYNTHETIC", StringComparison.OrdinalIgnoreCase) < 0 &&
                evidence.IndexOf("MOCK_ONLY", StringComparison.OrdinalIgnoreCase) < 0;
        }

        public bool TryApplyObservedEvent(Original323NightmareA04QEvent originalEvent,
            int playerSlot, IOriginal323NightmareA04QEventWitness verifiedEvent)
        {
            bool global = originalEvent == Original323NightmareA04QEvent.WarcuryActivated ||
                originalEvent == Original323NightmareA04QEvent.NikaCanceledWarcury;
            bool personal = originalEvent == Original323NightmareA04QEvent.LineItemAI19Used ||
                originalEvent == Original323NightmareA04QEvent.BrookTraitTR12Applied ||
                originalEvent == Original323NightmareA04QEvent.OnePieceTreasureCompleted;
            if ((!global && !personal) ||
                (global && playerSlot != GlobalEventPlayerSlot) ||
                (personal && !ValidPlayerSlot(playerSlot)) ||
                (originalEvent == Original323NightmareA04QEvent.WarcuryActivated &&
                    warcuryActive) ||
                (originalEvent == Original323NightmareA04QEvent.NikaCanceledWarcury &&
                    !warcuryActive) || verifiedEvent == null)
                return false;

            string evidence;
            if (!verifiedEvent.TryConfirm(originalEvent, playerSlot, out evidence) ||
                !ValidEventEvidence(evidence)) return false;

            int adjustment = 0;
            switch (originalEvent)
            {
                case Original323NightmareA04QEvent.WarcuryActivated:
                    // JASS BeR: NJ/LK -= 10, then Nightmare-only -= 5.
                    adjustment = -15;
                    bossLevel += adjustment;
                    for (int i = 0; i < lineLevels.Length; i++)
                        lineLevels[i] += adjustment;
                    warcuryActive = true;
                    break;
                case Original323NightmareA04QEvent.NikaCanceledWarcury:
                    // JASS BeQ when NI==1: NJ/LK += 10 and then += 5.
                    adjustment = 15;
                    bossLevel += adjustment;
                    for (int i = 0; i < lineLevels.Length; i++)
                        lineLevels[i] += adjustment;
                    warcuryActive = false;
                    break;
                case Original323NightmareA04QEvent.LineItemAI19Used:
                    lineLevels[playerSlot] += 5;  // BhL, item AI19.
                    break;
                case Original323NightmareA04QEvent.BrookTraitTR12Applied:
                    lineLevels[playerSlot] += 3;  // BzL, trait TR12.
                    break;
                case Original323NightmareA04QEvent.OnePieceTreasureCompleted:
                    lineLevels[playerSlot] += 10; // CRW, LV[JG] reaches 0.
                    break;
            }
            return true;
        }
    }
}
