using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // ORDR S2 2.323[C] war3map.j SHA256:
    // 96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d
    // JASS Bd4 line 8554 SETS current A04Q level to Hk.
    // JASS Bd5 line 8560 ADDS Hk to current A04Q level.
    // They are different mutations and their source order matters.
    public enum Original323MagicDefenseLevelMutationKind
    {
        SetToOriginalRoundLevel,
        IncreaseFromOriginalSkill
    }

    public sealed class Original323ObservedMagicDefenseLevelMutation
    {
        public string OriginalCaller { get; private set; }
        public int SourceLine { get; private set; }
        public Original323MagicDefenseLevelMutationKind Kind { get; private set; }
        public int ObservedOperand { get; private set; }
        public string OriginalEventId { get; private set; }
        public string OriginalEventEvidence { get; private set; }

        public Original323ObservedMagicDefenseLevelMutation(
            string originalCaller, int sourceLine,
            Original323MagicDefenseLevelMutationKind kind,
            int observedOperand, string originalEventId, string originalProof)
        {
            OriginalCaller = originalCaller; SourceLine = sourceLine;
            Kind = kind; ObservedOperand = observedOperand;
            OriginalEventId = originalEventId;
            OriginalEventEvidence = originalProof;
        }
    }

    // The callbacks MUST be independently observed against the target live
    // monster. A source-line match or global LK/NJ level does not prove this
    // specific existing instance received the native ability change.
    public interface IOriginal323MagicDefenseLevelMutationWitness
    {
        bool TryConfirmOrderedOriginalLevelMutations(
            string liveEnemyId, int expectedPreviousLevel,
            int independentlyObservedFinalLevel,
            IReadOnlyList<Original323ObservedMagicDefenseLevelMutation>
                chronologicalEvents,
            out string sourceBackedEventEvidence);
    }

    public static class Original323MagicDefenseLevelMutationRules
    {
        // All eight direct source call sites for JASS Bd4, which sets the
        // monster's A04Q level from original LK[player] or NJ.
        public static bool TryGetVerifiedSetCaller(string function,
            out int originalLine, out string sourceOperand)
        {
            originalLine = 0; sourceOperand = null;
            switch (function)
            {
                case "Bvi": originalLine = 20698; break;
                case "CKz": originalLine = 50860; break;
                case "CNI":
                    originalLine = 52131;
                    sourceOperand = "NJ";
                    return true;
                case "CVX": originalLine = 59504; break;
                case "CVY": originalLine = 59518; break;
                case "CVZ": originalLine = 59552; break;
                case "CVo": originalLine = 59681; break;
                case "CVp": originalLine = 59694; break;
                default: return false;
            }
            sourceOperand = "LK[JG]";
            return true;
        }

        public static bool TryPlanOrderedLevelMutations(
            int currentLevel,
            IReadOnlyList<Original323ObservedMagicDefenseLevelMutation> events,
            out int expectedFinalLevel)
        {
            expectedFinalLevel = 0;
            double ignored;
            if (!Ordr323VerifiedDefenseAbilities.TryGetMagicResistancePercent(
                    currentLevel, out ignored) ||
                events == null || events.Count < 1 || events.Count > 64)
                return false;
            long level = currentLevel;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in events)
            {
                if (item == null ||
                    string.IsNullOrWhiteSpace(item.OriginalEventId) ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(
                        item.OriginalEventId) ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(
                        item.OriginalEventEvidence) ||
                    !ids.Add(item.OriginalEventId))
                    return false;

                if (item.Kind ==
                    Original323MagicDefenseLevelMutationKind.SetToOriginalRoundLevel)
                {
                    int verifiedLine;
                    string originalOperand;
                    if (!TryGetVerifiedSetCaller(item.OriginalCaller,
                            out verifiedLine, out originalOperand) ||
                        verifiedLine != item.SourceLine)
                        return false;
                    // LK[JG] and NJ are dynamic source values; no guessed
                    // default, inferred research offset or implicit clamping.
                    level = item.ObservedOperand;
                }
                else if (item.Kind ==
                    Original323MagicDefenseLevelMutationKind.IncreaseFromOriginalSkill)
                {
                    int verifiedLine, verifiedIncrease;
                    if (!Original323MagicDefenseStackRules.TryGetSourceIncrease(
                            item.OriginalCaller, out verifiedLine,
                            out verifiedIncrease) ||
                        verifiedLine != item.SourceLine ||
                        verifiedIncrease != item.ObservedOperand)
                        return false;
                    level += item.ObservedOperand;
                }
                else return false;

                if (level < 1 || level > 41)
                    return false; // W3A verified only for levels 1..41.
            }
            if (level == currentLevel) return false; // no level mutation.
            expectedFinalLevel = (int)level;
            return true;
        }

        public static bool TryApplyObservedLevelMutations(
            CombatEnemy actualMonster, int expectedPreviousLevel,
            int separatelyObservedNativeFinalLevel,
            IReadOnlyList<Original323ObservedMagicDefenseLevelMutation> events,
            IOriginal323MagicDefenseLevelMutationWitness sequenceWitness,
            IOriginal323LiveMonsterA04QWitness nativeLevelWitness)
        {
            if (actualMonster == null || !actualMonster.IsAlive ||
                sequenceWitness == null || nativeLevelWitness == null ||
                actualMonster.Defenses == null ||
                !actualMonster.Defenses.MagicAbilityLevel.HasValue ||
                !actualMonster.Defenses.HasVerifiedMagicResistance ||
                actualMonster.Defenses.MagicAbilityLevel.Value !=
                    expectedPreviousLevel)
                return false;

            // An already imported A04Q-level status must not be applied a
            // second time through a native JASS level-change callback.
            foreach (var active in actualMonster.Status.Snapshot())
                if (active.Type == CombatEffectType.OriginalA04QLevelIncrease)
                    return false;

            int planned;
            if (!TryPlanOrderedLevelMutations(
                    expectedPreviousLevel, events, out planned) ||
                planned != separatelyObservedNativeFinalLevel)
                return false;

            string proof;
            if (!sequenceWitness.TryConfirmOrderedOriginalLevelMutations(
                    actualMonster.Id, expectedPreviousLevel, planned,
                    events, out proof) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(proof))
                return false;

            // Update exactly ONCE after both independent native sequence and
            // actual live A04Q result have been confirmed. Pending native
            // projectiles continue using this enemy's IMPACT-time defenses.
            return actualMonster.TryApplyObservedOriginalA04QLevel(
                expectedPreviousLevel, planned, nativeLevelWitness);
        }
    }
}
