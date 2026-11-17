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

    [Serializable]
    public sealed class Original323ScriptAttackProcReceipt
    {
        public string SourceFunction;
        public string AttackerId;
        public string TargetId;
        public long EventSequence;
        public long GlobalBfiOrdinal;
        public bool Triggered;
        public string SourceEvidence;
    }

    [Serializable]
    public sealed class Original323ScriptAttackProcState
    {
        public int Version = 1;
        public List<Original323ScriptAttackProcReceipt> Receipts =
            new List<Original323ScriptAttackProcReceipt>();
    }

    public sealed class Original323ScriptAttackProcRolls
    {
        private readonly Original323SharedBfiRandom scriptRandom;
        private readonly IOriginal323ScriptAttackProcWitness originalEvents;
        private readonly HashSet<string> processed =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Original323ScriptAttackProcReceipt> receipts =
            new List<Original323ScriptAttackProcReceipt>();
        public Original323SharedBfiRandom SharedRandom => scriptRandom;

        public Original323ScriptAttackProcState CaptureState()
        {
            var state = new Original323ScriptAttackProcState();
            foreach (var record in receipts)
                state.Receipts.Add(new Original323ScriptAttackProcReceipt {
                    SourceFunction = record.SourceFunction,
                    AttackerId = record.AttackerId, TargetId = record.TargetId,
                    EventSequence = record.EventSequence,
                    GlobalBfiOrdinal = record.GlobalBfiOrdinal,
                    Triggered = record.Triggered,
                    SourceEvidence = record.SourceEvidence
                });
            ValidateState(state, scriptRandom.CaptureCheckpoint());
            return state;
        }

        public static void ValidateState(Original323ScriptAttackProcState state,
            Original323SharedBfiCheckpoint bfi)
        {
            Original323SharedBfiRandom.ValidateCheckpoint(bfi);
            if (state == null || state.Version != 1 ||
                state.Receipts == null || state.Receipts.Count > 1000000 ||
                state.Receipts.Count > bfi.DrawCount)
                throw new FormatException("원본 공격 확률 스킬 영수증이 잘못됐습니다.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            long preceding = 0;
            foreach (var record in state.Receipts)
            {
                long actor;
                if (record == null ||
                    !SupportsScriptFunction(record.SourceFunction) ||
                    string.IsNullOrWhiteSpace(record.AttackerId) ||
                    !long.TryParse(record.AttackerId, out actor) || actor <= 0 ||
                    actor.ToString() != record.AttackerId ||
                    string.IsNullOrWhiteSpace(record.TargetId) ||
                    record.TargetId.Length > 256 ||
                    record.EventSequence <= 0 ||
                    record.GlobalBfiOrdinal <= preceding ||
                    record.GlobalBfiOrdinal > bfi.DrawCount ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(
                        record.SourceEvidence) ||
                    !ids.Add(record.SourceFunction + ":" +
                        record.AttackerId + ":" + record.EventSequence))
                    throw new FormatException(
                        "원본 공격 사건 ID·BFi 전역 순번이 불일치합니다.");
                preceding = record.GlobalBfiOrdinal;
            }
        }

        public void RestoreState(Original323ScriptAttackProcState state)
        {
            ValidateState(state, scriptRandom.CaptureCheckpoint());
            if (processed.Count != 0 || receipts.Count != 0)
                throw new InvalidOperationException(
                    "원본 공격 영수증은 새 비공개 전투에서 한 번만 복원합니다.");
            foreach (var record in state.Receipts)
            {
                processed.Add(record.SourceFunction + ":" +
                    record.AttackerId + ":" + record.EventSequence);
                receipts.Add(new Original323ScriptAttackProcReceipt {
                    SourceFunction = record.SourceFunction,
                    AttackerId = record.AttackerId, TargetId = record.TargetId,
                    EventSequence = record.EventSequence,
                    GlobalBfiOrdinal = record.GlobalBfiOrdinal,
                    Triggered = record.Triggered,
                    SourceEvidence = record.SourceEvidence
                });
            }
        }

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
            receipts.Add(new Original323ScriptAttackProcReceipt {
                SourceFunction = sourceFunction,
                AttackerId = attackerInstanceId,
                TargetId = targetInstanceId,
                EventSequence = eventSequence,
                GlobalBfiOrdinal = scriptRandom.DrawCount,
                Triggered = triggered,
                SourceEvidence = evidence
            });
            return true;
        }
    }
}
