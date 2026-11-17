using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // State of original Warcraft e0KD orders only; physical missile geometry
    // and the actual native impact remain external, source-observed events.
    [Serializable]
    public sealed class SourceOriginalDummyAttackSequenceCheckpoint
    {
        public string AttackerId;
        public long Sequence;
    }

    [Serializable]
    public sealed class SourceOriginalDummyCastCheckpoint
    {
        public string CastId;
        public string AttackerId;
        public string OriginalTargetId;
        public string AttackerRawcode;
        public long AttackSequence;
        public double StartedAt;
        public double LastImpactAt;
        public bool StoryLane;
        public int ImmuneSelections;
        public string OriginalCastEvidence;
        public List<string> HitTargets = new List<string>();
        public List<string> SelectedTargets = new List<string>();
        public List<string> ImpactIds = new List<string>();
    }

    [Serializable]
    public sealed class SourceOriginalDummyLedgerCheckpoint
    {
        public int Version = 1;
        public bool OwnerConfigured;
        public List<SourceOriginalDummyAttackSequenceCheckpoint> AttackSequences =
            new List<SourceOriginalDummyAttackSequenceCheckpoint>();
        public List<SourceOriginalDummyCastCheckpoint> Pending =
            new List<SourceOriginalDummyCastCheckpoint>();
        public List<string> FinishedCastIds = new List<string>();
    }

    public interface IOriginal323DummyCastRestoreWitness
    {
        // Attest the original native order, its saved impact/selection history,
        // and that the SAME pending cast still belongs to this session/time.
        // This query must not reissue the order or mutate a live world.
        bool TryConfirmRestoredCast(string sessionId, double logicalTime,
            SourceOriginalDummyCastCheckpoint state, out string evidence);
    }

    public sealed partial class CharacterCombatRuntime
    {
        private static bool SafeNativeId(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && id.Length <= 256;
        }

        private static List<string> SortedNativeIds(HashSet<string> ids)
        {
            var result = new List<string>(ids);
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private SourceOriginalDummyLedgerCheckpoint CaptureOriginalDummyState()
        {
            if (committingOriginalDummyImpact ||
                executingOriginalDummyActor != null ||
                executingOriginalDummyTarget != null)
                throw new NotSupportedException(
                    "원본 네이티브 타격/시전 콜백 중 저장할 수 없습니다.");
            var state = new SourceOriginalDummyLedgerCheckpoint {
                OwnerConfigured = originalScriptAttackEffects != null
            };
            foreach (var row in originalScriptAttackSequences)
                state.AttackSequences.Add(
                    new SourceOriginalDummyAttackSequenceCheckpoint {
                        AttackerId = row.Key, Sequence = row.Value
                    });
            state.AttackSequences.Sort((a, b) =>
                string.CompareOrdinal(a.AttackerId, b.AttackerId));
            foreach (var row in pendingOriginalDummyCasts)
            {
                var cast = row.Value;
                state.Pending.Add(new SourceOriginalDummyCastCheckpoint {
                    CastId = row.Key,
                    AttackerId = cast.Snapshot.AttackerInstanceId,
                    OriginalTargetId = cast.Snapshot.OriginalTargetId,
                    AttackerRawcode = cast.Snapshot.OriginalOrder.AttackerRawcode,
                    AttackSequence = cast.AttackSequence,
                    StartedAt = cast.Snapshot.StartTimeSeconds,
                    LastImpactAt = cast.LastImpactAt,
                    StoryLane = cast.IsStoryLane,
                    ImmuneSelections = cast.ImmuneSelections,
                    OriginalCastEvidence = cast.Snapshot.OriginalCastEvidence,
                    HitTargets = SortedNativeIds(cast.AlreadyHit),
                    SelectedTargets = SortedNativeIds(cast.AlreadySelected),
                    ImpactIds = SortedNativeIds(cast.ImpactIds)
                });
            }
            state.Pending.Sort((a, b) =>
                string.CompareOrdinal(a.CastId, b.CastId));
            state.FinishedCastIds.AddRange(
                SortedNativeIds(finishedOriginalDummyCastIds));
            return state;
        }

        private static HashSet<string> CheckedNativeSet(
            List<string> items, string reason)
        {
            if (items == null || items.Count > 8192)
                throw new FormatException(reason);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            string previous = null;
            foreach (var item in items)
            {
                if (!SafeNativeId(item) ||
                    (previous != null &&
                     string.CompareOrdinal(previous, item) >= 0) ||
                    !unique.Add(item))
                    throw new FormatException(reason);
                previous = item;
            }
            return unique;
        }

        public static void ValidateOriginalDummyState(
            SourceCombatRuntimeCheckpoint state)
        {
            if (state == null || state.OriginalNativeDummy == null ||
                state.OriginalNativeDummy.Version != 1 ||
                state.OriginalNativeDummy.AttackSequences == null ||
                state.OriginalNativeDummy.Pending == null ||
                state.OriginalNativeDummy.FinishedCastIds == null ||
                state.OriginalNativeDummy.AttackSequences.Count > 1000000 ||
                state.OriginalNativeDummy.Pending.Count > 4096 ||
                state.OriginalNativeDummy.FinishedCastIds.Count > 1000000 ||
                double.IsNaN(state.Time) || double.IsInfinity(state.Time) ||
                state.Time < 0)
                throw new FormatException(
                    "원본 공격 사건/더미 시전 저장 장부가 없습니다.");
            var ledger = state.OriginalNativeDummy;
            var sequences = new Dictionary<string, long>(
                StringComparer.Ordinal);
            string lastActor = null;
            foreach (var row in ledger.AttackSequences)
            {
                long id;
                if (row == null || !SafeNativeId(row.AttackerId) ||
                    !long.TryParse(row.AttackerId,
                        NumberStyles.None, CultureInfo.InvariantCulture,
                        out id) || id <= 0 ||
                    row.AttackerId != id.ToString(CultureInfo.InvariantCulture) ||
                    row.Sequence <= 0 ||
                    (lastActor != null &&
                     string.CompareOrdinal(lastActor, row.AttackerId) >= 0) ||
                    sequences.ContainsKey(row.AttackerId))
                    throw new FormatException("원본 공격자별 사건 순번이 잘못됐습니다.");
                sequences.Add(row.AttackerId, row.Sequence);
                lastActor = row.AttackerId;
            }
            var finished = new HashSet<string>(StringComparer.Ordinal);
            string lastFinished = null;
            foreach (var id in ledger.FinishedCastIds)
            {
                if (!SafeNativeId(id) ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(id) ||
                    (lastFinished != null &&
                     string.CompareOrdinal(lastFinished, id) >= 0) ||
                    !finished.Add(id))
                    throw new FormatException("완료된 원본 주문 ID가 중복됐습니다.");
                lastFinished = id;
            }
            var pending = new HashSet<string>(StringComparer.Ordinal);
            string lastCast = null;
            foreach (var row in ledger.Pending)
            {
                long sequence;
                Original323AttackDummySpellCommand order;
                Original323NativeDummyAbilityFacts facts;
                if (row == null || !SafeNativeId(row.CastId) ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(row.CastId) ||
                    !SafeNativeId(row.AttackerId) ||
                    !SafeNativeId(row.OriginalTargetId) ||
                    !SafeNativeId(row.AttackerRawcode) ||
                    !Original323ScriptAttackEffectCoordinator.TryCreateOriginalSpell(
                        row.AttackerRawcode, out order) ||
                    !Original323NativeDummyAbilityCatalog.TryGet(order, out facts) ||
                    !sequences.TryGetValue(row.AttackerId, out sequence) ||
                    row.AttackSequence <= 0 || row.AttackSequence > sequence ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(
                        row.OriginalCastEvidence) ||
                    double.IsNaN(row.StartedAt) ||
                    double.IsInfinity(row.StartedAt) ||
                    double.IsNaN(row.LastImpactAt) ||
                    double.IsInfinity(row.LastImpactAt) ||
                    row.StartedAt < 0 || row.StartedAt > row.LastImpactAt ||
                    row.LastImpactAt > state.Time ||
                    (lastCast != null &&
                     string.CompareOrdinal(lastCast, row.CastId) >= 0) ||
                    !pending.Add(row.CastId) || finished.Contains(row.CastId) ||
                    row.ImmuneSelections < 0)
                    throw new FormatException("진행 중 원본 더미 시전이 잘못됐습니다.");
                lastCast = row.CastId;
                var hits = CheckedNativeSet(row.HitTargets,
                    "원본 주문의 HP 타격 대상 기록이 손상됐습니다.");
                var selected = CheckedNativeSet(row.SelectedTargets,
                    "원본 주문의 선택 대상 기록이 손상됐습니다.");
                var impacts = CheckedNativeSet(row.ImpactIds,
                    "원본 주문의 처리된 충돌 ID가 손상됐습니다.");
                if (row.ImmuneSelections > selected.Count ||
                    (facts.MaxNativeTargets.HasValue &&
                     (selected.Count > facts.MaxNativeTargets.Value ||
                      !hits.IsSubsetOf(selected))) ||
                    ((hits.Count > 0 || selected.Count > 0 ||
                      row.ImmuneSelections > 0) && impacts.Count == 0) ||
                    (impacts.Count == 0 && row.LastImpactAt != row.StartedAt))
                    throw new FormatException(
                        "원본 선택/면역/타격/충돌 이력 순서가 불일치합니다.");
            }
            if (!ledger.OwnerConfigured &&
                (ledger.Pending.Count != 0 ||
                 ledger.AttackSequences.Count != 0 ||
                 ledger.FinishedCastIds.Count != 0))
                throw new FormatException(
                    "원본 공격 공급자가 없는 상태에 보조 주문이 저장됐습니다.");
        }

        // Only the source attacked-event receipts authorize a previously
        // successful original native order; the global BFi cursor is NOT
        // advanced while validating any of these histories.
        public static void ValidateOriginalDummyAttackReceipts(
            SourceCombatRuntimeCheckpoint combat,
            Original323ScriptAttackProcState receipts)
        {
            ValidateOriginalDummyState(combat);
            var ledger = combat.OriginalNativeDummy;
            if (ledger.OwnerConfigured && receipts == null)
                throw new FormatException(
                    "원본 보조 주문 소유자의 BFi 공격 사건 장부가 없습니다.");
            if (receipts == null) return;
            if (receipts.Receipts == null)
                throw new FormatException("원본 공격 사건 영수증이 없습니다.");
            var sequences = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var row in ledger.AttackSequences)
                sequences.Add(row.AttackerId, row.Sequence);
            var triggered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in receipts.Receipts)
            {
                long last;
                if (row == null || !sequences.TryGetValue(row.AttackerId, out last) ||
                    row.EventSequence > last || row.EventSequence <= 0)
                    throw new FormatException(
                        "공격 사건 순번이 실제 원본 주문 소유자와 다릅니다.");
                if (row.Triggered)
                    triggered.Add(row.SourceFunction + ":" + row.AttackerId +
                        ":" + row.EventSequence + ":" + row.TargetId);
            }
            foreach (var pending in ledger.Pending)
            {
                Original323AttackDummySpellCommand order;
                Original323ScriptAttackEffectCoordinator.TryCreateOriginalSpell(
                    pending.AttackerRawcode, out order);
                string key = order.OriginalScriptFunction + ":" +
                    pending.AttackerId + ":" + pending.AttackSequence +
                    ":" + pending.OriginalTargetId;
                if (!triggered.Contains(key))
                    throw new FormatException(
                        "원본 BFi 발동 영수증이 없는 대기 중 주문입니다.");
            }
        }

        // Called on the NEW unpublished world after actor recovery and after
        // optional Bnm/Bnp/Bnr owner binding. Never creates native dummies,
        // rolls random, applies a hit or invokes a saved Action delegate.
        public void RestoreOriginalDummyState(
            SourceOriginalDummyLedgerCheckpoint saved, string sessionId,
            double logicalTime, IOriginal323DummyCastRestoreWitness witness)
        {
            if (saved == null)
                throw new FormatException("원본 주문 장부 누락");
            var draft = new SourceCombatRuntimeCheckpoint {
                Time = logicalTime, OriginalNativeDummy = saved
            };
            ValidateOriginalDummyState(draft);
            if (pendingOriginalDummyCasts.Count != 0 ||
                finishedOriginalDummyCastIds.Count != 0 ||
                originalScriptAttackSequences.Count != 0 ||
                originalScriptAttackEffects != null != saved.OwnerConfigured ||
                scheduler.Time != logicalTime ||
                string.IsNullOrWhiteSpace(sessionId) ||
                processingStep || committingOriginalDummyImpact ||
                pendingStepSeconds != 0)
                throw new InvalidOperationException(
                    "원본 더미 시전은 같은 시각의 새 전투에서만 복원합니다.");
            if (saved.Pending.Count > 0 && witness == null)
                throw new NotSupportedException(
                    "원본 보조 주문의 진행 중 소유권 관측자가 없습니다.");
            foreach (var row in saved.Pending)
            {
                string evidence;
                if (!witness.TryConfirmRestoredCast(
                        sessionId, logicalTime, row, out evidence) ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(evidence) ||
                    scheduler.Time != logicalTime)
                    throw new NotSupportedException(
                        "진행 중 원본 더미 시전의 실제 소유권이 확인되지 않았습니다.");
            }
            foreach (var row in saved.AttackSequences)
                originalScriptAttackSequences.Add(row.AttackerId, row.Sequence);
            foreach (var id in saved.FinishedCastIds)
                finishedOriginalDummyCastIds.Add(id);
            foreach (var row in saved.Pending)
            {
                Original323AttackDummySpellCommand order;
                Original323ScriptAttackEffectCoordinator.TryCreateOriginalSpell(
                    row.AttackerRawcode, out order);
                var cast = new Original323ObservedDummyCast(row.CastId,
                    order, row.AttackerId, row.OriginalTargetId,
                    row.StartedAt, row.OriginalCastEvidence);
                var pending = new PendingOriginalDummyCast {
                    Snapshot = cast,
                    IsStoryLane = row.StoryLane,
                    LastImpactAt = row.LastImpactAt,
                    AttackSequence = row.AttackSequence,
                    ImmuneSelections = row.ImmuneSelections
                };
                pending.AlreadyHit.UnionWith(row.HitTargets);
                pending.AlreadySelected.UnionWith(row.SelectedTargets);
                pending.ImpactIds.UnionWith(row.ImpactIds);
                pendingOriginalDummyCasts.Add(row.CastId, pending);
            }
        }
    }
}
