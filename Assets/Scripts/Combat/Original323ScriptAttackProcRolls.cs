using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // Original script Bnm/Bnp/Bnr: attack-event BFi(1,100)<=10.
    // Not the same as Warcraft native Critical Strike/attack dice!
    // Only records verified proc outcomes; the source effect must be
    // applied separately by the matching original damage/skill world.
    public interface IOriginal323ScriptAttackProcWitness
    {
        bool TryConfirmOriginalAttackEvent(string sourceFunction,
            string attackerInstanceId, string targetInstanceId,
            long eventSequence, out string observedSource);
    }

    public sealed class Original323ScriptAttackProcRolls
    {
        private readonly Original323SharedBfiRandom scriptRandom;
        private readonly IOriginal323ScriptAttackProcWitness originalEvents;
        private readonly HashSet<string> processed =
            new HashSet<string>(StringComparer.Ordinal);

        public Original323ScriptAttackProcRolls(
            Original323SharedBfiRandom verifiedScriptState,
            IOriginal323ScriptAttackProcWitness source)
        {
            scriptRandom = verifiedScriptState ??
                throw new ArgumentNullException(nameof(verifiedScriptState));
            originalEvents = source ??
                throw new ArgumentNullException(nameof(source));
        }

        public static bool SupportsScriptFunction(string sourceFunction)
        {
            return sourceFunction == "Bnm" || sourceFunction == "Bnp" ||
                sourceFunction == "Bnr";
        }

        public bool TryObserveAndRoll(string sourceFunction,
            string attackerInstanceId, string targetInstanceId,
            long eventSequence, out bool triggered)
        {
            triggered = false;
            if (!SupportsScriptFunction(sourceFunction) ||
                string.IsNullOrWhiteSpace(attackerInstanceId) ||
                string.IsNullOrWhiteSpace(targetInstanceId) ||
                eventSequence <= 0) return false;

            string key = sourceFunction + ":" + attackerInstanceId +
                ":" + eventSequence;
            if (processed.Contains(key)) return false;
            string evidence;
            if (!originalEvents.TryConfirmOriginalAttackEvent(sourceFunction,
                attackerInstanceId, targetInstanceId, eventSequence,
                out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;

            int predicted, nextState;
            if (!scriptRandom.TryPreviewNext(1, 100,
                out predicted, out nextState) ||
                predicted < 1 || predicted > 100)
                return false;
            int roll = scriptRandom.NextForPurpose(
                Original323ScriptRandomPurpose.OtherOriginalScriptCall, 1, 100);
            if (roll != predicted)
                throw new InvalidOperationException(
                    "원본 스킬 확률 판정 중 공통 난수 순서가 달라졌습니다.");
            processed.Add(key); // Failed proc attempts ALSO consume a draw.
            triggered = roll <= 10;
            return true;
        }
    }
}
