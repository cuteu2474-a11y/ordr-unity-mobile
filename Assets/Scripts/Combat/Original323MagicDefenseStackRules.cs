using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Parsed directly from ORDR S2 2.323[C] war3map.j, SHA-256
    // 96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d.
    // JASS Bd5 adds a fixed integer to the CURRENT monster A04Q ability
    // LEVEL, not a flat percentage to an arbitrary "magic reduction" status.
    // A04Q Def5's actual magic-damage factor comes from AbilityData + W3A.
    public sealed class Original323ObservedMagicDefenseStackCall
    {
        public string OriginalFunction { get; private set; }
        public int OriginalJassLine { get; private set; }
        public int ObservedLevelIncrease { get; private set; }
        public string OriginalEventId { get; private set; }
        public string OriginalEventEvidence { get; private set; }

        public Original323ObservedMagicDefenseStackCall(string function,
            int originalJassLine, int levelIncrease, string eventId,
            string evidence)
        {
            OriginalFunction = function;
            OriginalJassLine = originalJassLine;
            ObservedLevelIncrease = levelIncrease;
            OriginalEventId = eventId;
            OriginalEventEvidence = evidence;
        }
    }

    // Required separately from the already implemented per-monster
    // IOriginal323LiveMonsterA04QWitness final native-level observer.
    // Proves these ordered JASS callbacks ACTUALLY ran for this monster,
    // including optional caller conditions. The source-line catalog alone
    // cannot prove that a live callback executed.
    public interface IOriginal323MagicDefenseStackSequenceWitness
    {
        bool TryConfirmOriginalOrderedStackCalls(string enemyId,
            int previousLevel, int observedFinalLevel,
            IReadOnlyList<Original323ObservedMagicDefenseStackCall> calls,
            out string observedNativeCallEvidence);
    }

    public static class Original323MagicDefenseStackRules
    {
        // Every direct Bd5(...) JASS call site in this pinned source version.
        // Never infer unknown callbacks or extrapolate ability data > level41.
        public static bool TryGetSourceIncrease(string originalFunction,
            out int originalJassLine, out int levelIncrease)
        {
            originalJassLine = 0;
            levelIncrease = 0;
            switch (originalFunction)
            {
                case "BoM": originalJassLine=15139; levelIncrease=10; return true;
                case "Br6": originalJassLine=17604; levelIncrease=10; return true;
                case "Bur": originalJassLine=19761; levelIncrease=1; return true;
                case "BvG": originalJassLine=20134; levelIncrease=18; return true;
                case "B46": originalJassLine=32897; levelIncrease=5; return true;
                case "B7I": originalJassLine=35379; levelIncrease=10; return true;
                case "CEa": originalJassLine=43508; levelIncrease=1; return true;
                case "CFI": originalJassLine=44588; levelIncrease=3; return true;
                case "CGm": originalJassLine=46939; levelIncrease=10; return true;
                default: return false;
            }
        }

        public static bool TryPlanObservedStack(int originalLiveLevel,
            IReadOnlyList<Original323ObservedMagicDefenseStackCall> orderedCalls,
            out int finalLiveLevel)
        {
            finalLiveLevel = 0;
            double ignored;
            if (!Ordr323VerifiedDefenseAbilities.TryGetMagicResistancePercent(
                    originalLiveLevel, out ignored) ||
                orderedCalls == null || orderedCalls.Count == 0 ||
                orderedCalls.Count > 64)
                return false;
            var eventIds = new HashSet<string>(StringComparer.Ordinal);
            long level = originalLiveLevel;
            foreach (var call in orderedCalls)
            {
                int line, delta;
                if (call == null ||
                    !TryGetSourceIncrease(call.OriginalFunction,
                        out line, out delta) ||
                    call.OriginalJassLine != line ||
                    call.ObservedLevelIncrease != delta ||
                    string.IsNullOrWhiteSpace(call.OriginalEventId) ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(
                        call.OriginalEventId) ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(
                        call.OriginalEventEvidence) ||
                    !eventIds.Add(call.OriginalEventId))
                    return false;
                level += delta;
                // Original 2.323[C] A04Q W3A is source-confirmed only
                // through level 41. Native beyond-max handling is unknown.
                if (level > 41)
                    return false;
            }
            finalLiveLevel = (int)level;
            return true;
        }

        public static bool TryApplyObservedStack(
            CombatEnemy liveMonster, int expectedPreviousLevel,
            int independentlyObservedFinalLevel,
            IReadOnlyList<Original323ObservedMagicDefenseStackCall> orderedCalls,
            IOriginal323MagicDefenseStackSequenceWitness callWitness,
            IOriginal323LiveMonsterA04QWitness actualLevelWitness)
        {
            if (liveMonster == null || !liveMonster.IsAlive ||
                callWitness == null || actualLevelWitness == null ||
                liveMonster.Defenses == null ||
                !liveMonster.Defenses.HasVerifiedMagicResistance ||
                !liveMonster.Defenses.MagicAbilityLevel.HasValue ||
                liveMonster.Defenses.MagicAbilityLevel.Value !=
                    expectedPreviousLevel)
                return false;

            // Some imported status profiles independently store an A04Q
            // level-increase tag. Do not count that native event twice.
            foreach (var active in liveMonster.Status.Snapshot())
                if (active.Type ==
                    CombatEffectType.OriginalA04QLevelIncrease)
                    return false;

            int planned;
            if (!TryPlanObservedStack(expectedPreviousLevel,
                    orderedCalls, out planned) ||
                planned != independentlyObservedFinalLevel)
                return false;

            string proof;
            if (!callWitness.TryConfirmOriginalOrderedStackCalls(
                    liveMonster.Id, expectedPreviousLevel,
                    independentlyObservedFinalLevel, orderedCalls,
                    out proof) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(proof))
                return false;

            // ONE authoritative native-level replacement, after ALL
            // source callbacks and their combined result have been checked.
            // If native witness rejects, current HP/armor/level stay intact.
            return liveMonster.TryApplyObservedOriginalA04QLevel(
                expectedPreviousLevel, independentlyObservedFinalLevel,
                actualLevelWitness);
        }
    }
}
