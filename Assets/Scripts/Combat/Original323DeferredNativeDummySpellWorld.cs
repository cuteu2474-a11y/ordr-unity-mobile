using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Represents a real original W3X e0KD dummy CAST. It does not imply
    // that the Warcraft ability has hit, resolved immunity, or dealt damage.
    public sealed class Original323ObservedDummyCast
    {
        public string CastId { get; private set; }
        public Original323AttackDummySpellCommand OriginalOrder { get; private set; }
        public string AttackerInstanceId { get; private set; }
        public string OriginalTargetId { get; private set; }
        public double StartTimeSeconds { get; private set; }
        public string OriginalCastEvidence { get; private set; }

        internal Original323ObservedDummyCast(string castId,
            Original323AttackDummySpellCommand command, string attackerId,
            string primaryTarget, double atSeconds, string evidence)
        {
            CastId = castId; OriginalOrder = command;
            AttackerInstanceId = attackerId; OriginalTargetId = primaryTarget;
            StartTimeSeconds = atSeconds; OriginalCastEvidence = evidence;
        }
    }

    // Observed Warcraft native target choice is independent of HP damage.
    // Immunity can prevent a selected enemy from receiving any HP change.
    public sealed class Original323NativeSelectedSpellTarget
    {
        public string TargetId { get; private set; }
        public bool OriginalMagicImmune { get; private set; }
        public string OriginalSelectionEvidence { get; private set; }

        public Original323NativeSelectedSpellTarget(string targetId,
            bool originalMagicImmune, string originalSelectionEvidence)
        {
            if (string.IsNullOrWhiteSpace(targetId) ||
                string.IsNullOrWhiteSpace(originalSelectionEvidence))
                throw new ArgumentException(
                    "원본 주문 선정 대상과 마법 면역 판정 증빙이 필요합니다.");
            TargetId = targetId;
            OriginalMagicImmune = originalMagicImmune;
            OriginalSelectionEvidence = originalSelectionEvidence;
        }
    }

    // A single Warcraft native projectile impact / lightning resolution
    // observed at an exact simulation timestamp. Swarm can produce multiple
    // separate ordered impact receipts; forked lightning cannot exceed four
    // distinct damaged targets across the whole cast.
    public sealed class Original323NativeDummyImpact
    {
        public string ImpactId { get; private set; }
        public double ObservedTimeSeconds { get; private set; }
        public bool SpellCompleted { get; private set; }
        public IReadOnlyList<Original323NativeDummySpellHit> Hits { get; private set; }
        public IReadOnlyList<Original323NativeSelectedSpellTarget>
            SelectedTargets { get; private set; }
        // Legacy impact reports only final HP loss, so these must NOT be
        // silently treated as exhaustive native target selections.
        public bool HasOriginalTargetSelection { get; private set; }
        public string OriginalImpactEvidence { get; private set; }

        public Original323NativeDummyImpact(string impactId,
            double originalTimeSeconds, bool finished,
            IReadOnlyList<Original323NativeDummySpellHit> hits, string evidence)
            : this(impactId, originalTimeSeconds, finished, hits,
                null, evidence)
        { }

        public Original323NativeDummyImpact(string impactId,
            double originalTimeSeconds, bool finished,
            IReadOnlyList<Original323NativeDummySpellHit> hits,
            IReadOnlyList<Original323NativeSelectedSpellTarget> selected,
            string evidence)
        {
            if (string.IsNullOrWhiteSpace(impactId) ||
                impactId.Length > 256 ||
                double.IsNaN(originalTimeSeconds) ||
                double.IsInfinity(originalTimeSeconds) ||
                originalTimeSeconds < 0 ||
                hits == null || hits.Count > 512 ||
                string.IsNullOrWhiteSpace(evidence))
                throw new ArgumentException("원본 타격의 시점·대상·증빙이 필요합니다.");
            var snapshot = new List<Original323NativeDummySpellHit>();
            foreach (var hit in hits)
            {
                if (hit == null) throw new ArgumentException("주문 타격 항목 누락");
                snapshot.Add(hit);
            }
            var selectedSnapshot = new List<Original323NativeSelectedSpellTarget>();
            if (selected != null)
            {
                if (selected.Count > 512)
                    throw new ArgumentException("원본 주문 선정 인원 제한 초과");
                foreach (var item in selected)
                {
                    if (item == null)
                        throw new ArgumentException("원본 주문 선정 기록 누락");
                    selectedSnapshot.Add(item);
                }
            }
            ImpactId = impactId; ObservedTimeSeconds = originalTimeSeconds;
            SpellCompleted = finished;
            Hits = snapshot.AsReadOnly();
            HasOriginalTargetSelection = selected != null;
            SelectedTargets = selectedSnapshot.AsReadOnly();
            OriginalImpactEvidence = evidence;
        }
    }

    public interface IOriginal323DummySpellCastWitness
    {
        // Only a source-observed Warcraft IssuePointOrder/IssueTargetOrder
        // can authorize registration. Return a unique native cast reference.
        bool TryConfirmOriginalDummyCast(
            Original323AttackDummySpellCommand command,
            Original323NativeDummyAbilityFacts facts,
            CharacterCombatRuntime world, string attackerInstanceId,
            string attackedEnemyId, double observedCastAtSeconds,
            out string originalCastId, out string originalOrderEvidence);
    }

    public interface IOriginal323DummySpellImpactWitness
    {
        // The original native engine supplies real projectile intersection/
        // lightning target selection, immunity, resistance and final HP.
        // No Unity-estimated travel speed or AoE timing is accepted.
        bool TryConfirmOriginalDummyImpact(
            Original323ObservedDummyCast originalCast,
            CharacterCombatRuntime world,
            out Original323NativeDummyImpact nativeImpact);
    }

    // Original e0KD may expire or its native order can fail AFTER a verified
    // attack-declared cast but BEFORE any observed HP impact. It must be
    // explicitly CLOSED by the original Warcraft dummy lifecycle, not by
    // an invented cooldown/timeout or the target disappearing on mobile.
    public enum Original323DummyNativeCastEndReason
    {
        OriginalOrderRejected,
        OriginalDummyDespawned,
        OriginalCompletedWithoutDamage
    }

    public sealed class Original323ObservedDummyCastEnd
    {
        public string CastId { get; private set; }
        public string DummyRawcode { get; private set; }
        public string AbilityRawcode { get; private set; }
        public string OriginalW3xSha256 { get; private set; }
        public double NativeEndTimeSeconds { get; private set; }
        public Original323DummyNativeCastEndReason Reason { get; private set; }
        public string OriginalLifecycleEvidence { get; private set; }

        public Original323ObservedDummyCastEnd(string originalCastId,
            string sourceDummyRawcode, string sourceAbilityRawcode,
            string originalW3xSha256, double sourceEndTime,
            Original323DummyNativeCastEndReason verifiedReason,
            string sourceNativeEvidence)
        {
            if (string.IsNullOrWhiteSpace(originalCastId) ||
                originalCastId.Length > 256 ||
                sourceDummyRawcode == null || sourceDummyRawcode.Length != 4 ||
                sourceAbilityRawcode == null || sourceAbilityRawcode.Length != 4 ||
                string.IsNullOrWhiteSpace(originalW3xSha256) ||
                double.IsNaN(sourceEndTime) ||
                double.IsInfinity(sourceEndTime) || sourceEndTime < 0 ||
                !Enum.IsDefined(typeof(Original323DummyNativeCastEndReason),
                    verifiedReason) ||
                string.IsNullOrWhiteSpace(sourceNativeEvidence))
                throw new ArgumentException(
                    "실제 원본 더미 시전 종료 시각·원인·능력·증빙이 필요합니다.");
            CastId = originalCastId;
            DummyRawcode = sourceDummyRawcode;
            AbilityRawcode = sourceAbilityRawcode;
            OriginalW3xSha256 = originalW3xSha256;
            NativeEndTimeSeconds = sourceEndTime;
            Reason = verifiedReason;
            OriginalLifecycleEvidence = sourceNativeEvidence;
        }
    }

    // Only a witness to the ACTUAL original Warcraft e0KD native lifetime
    // can close a pending cast without native HP impact.
    public interface IOriginal323DummySpellTerminationWitness
    {
        bool TryConfirmOriginalNativeTermination(
            Original323ObservedDummyCast pendingNativeCast,
            CharacterCombatRuntime liveCombat,
            out Original323ObservedDummyCastEnd originalTerminal);
    }

    // Two-phase native spell bridge: ATTACK DECLARED -> cast registration;
    // actual Warcraft NATIVE IMPACT -> canonical HP/death ledger mutation.
    // This does not alter the existing independently verified instant path.
    public sealed class Original323DeferredNativeDummySpellWorld :
        IOriginal323VerifiedAttackDummySpellWorld
    {
        private readonly IOriginal323DummySpellCastWitness castWitness;
        private readonly IOriginal323DummySpellImpactWitness impactWitness;
        private readonly IOriginal323DummySpellTerminationWitness terminationWitness;
        // A source callback must not recursively resolve the same (or
        // another) native cast before the outer observation has finished.
        private bool observingNativeImpact;

        public Original323DeferredNativeDummySpellWorld(
            IOriginal323DummySpellCastWitness verifiedCasts,
            IOriginal323DummySpellImpactWitness verifiedImpacts)
            : this(verifiedCasts, verifiedImpacts, null)
        { }

        public Original323DeferredNativeDummySpellWorld(
            IOriginal323DummySpellCastWitness verifiedCasts,
            IOriginal323DummySpellImpactWitness verifiedImpacts,
            IOriginal323DummySpellTerminationWitness verifiedTerminations)
        {
            castWitness = verifiedCasts ??
                throw new ArgumentNullException(nameof(verifiedCasts));
            impactWitness = verifiedImpacts ??
                throw new ArgumentNullException(nameof(verifiedImpacts));
            terminationWitness = verifiedTerminations;
        }

        // Return "applied" for a VERIFIED cast command registration, not for
        // early or fabricated damage. The actual impact comes separately.
        public bool TryApplyOriginalDummySpell(
            Original323AttackDummySpellCommand sourceOrder,
            CharacterCombatRuntime liveCombat, string attackerInstanceId,
            string attackedEnemyId, out string originalApplicationEvidence)
        {
            originalApplicationEvidence = null;
            Original323NativeDummyAbilityFacts facts;
            if (liveCombat == null ||
                !Original323NativeDummyAbilityCatalog.TryGet(
                    sourceOrder, out facts))
                return false;
            string castId, proof;
            if (!castWitness.TryConfirmOriginalDummyCast(
                    sourceOrder, facts, liveCombat, attackerInstanceId,
                    attackedEnemyId, liveCombat.Time, out castId, out proof))
                return false;
            if (!liveCombat.TryRegisterObservedOriginalDummyCast(sourceOrder,
                    attackerInstanceId, attackedEnemyId, castId, proof))
                return false;
            originalApplicationEvidence = "원본 주문 시전 확인 (피해 전): " +
                proof + " / " + castId;
            return true;
        }

        // The target dying does NOT itself prove the original spell
        // disappeared: Warcraft can still hit other targets. Terminal
        // settlement requires an independent e0KD completion/despawn
        // witness and spends no damage or additional BFi dice.
        public bool TryConfirmObservedNativeCastEnd(
            CharacterCombatRuntime liveCombat, string sourceCastId)
        {
            Original323ObservedDummyCast pending;
            if (liveCombat == null || terminationWitness == null ||
                observingNativeImpact ||
                !liveCombat.TryGetPendingOriginalDummyCast(
                    sourceCastId, out pending))
                return false;
            observingNativeImpact = true;
            double before = liveCombat.Time;
            int actors = liveCombat.CharacterCount;
            int monsters = liveCombat.EnemyCount;
            int defeated = liveCombat.Game.DefeatedEnemies;
            try
            {
                Original323ObservedDummyCastEnd sourceEnd;
                if (!terminationWitness.TryConfirmOriginalNativeTermination(
                        pending, liveCombat, out sourceEnd) ||
                    sourceEnd == null ||
                    liveCombat.Time != before ||
                    liveCombat.CharacterCount != actors ||
                    liveCombat.EnemyCount != monsters ||
                    liveCombat.Game.DefeatedEnemies != defeated ||
                    sourceEnd.CastId != sourceCastId ||
                    sourceEnd.DummyRawcode != "e0KD" ||
                    sourceEnd.OriginalW3xSha256 !=
                        Original323VerifiedMonsterWaveCatalog.SourceW3xSha256 ||
                    sourceEnd.AbilityRawcode !=
                        pending.OriginalOrder.OriginalAbilityRawcode ||
                    Math.Abs(sourceEnd.NativeEndTimeSeconds - before) > 1e-6 ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(
                        sourceEnd.OriginalLifecycleEvidence))
                    return false;

                Original323ObservedDummyCast stillPending;
                if (!liveCombat.TryGetPendingOriginalDummyCast(
                        sourceCastId, out stillPending) ||
                    !Object.ReferenceEquals(stillPending, pending))
                    return false;

                return liveCombat.TryFinalizeObservedOriginalDummyCastWithoutHit(
                    pending, sourceEnd);
            }
            finally { observingNativeImpact = false; }
        }

        public bool TryApplyObservedImpact(CharacterCombatRuntime liveCombat,
            string originalCastId, out long actualTotalHpDamage)
        {
            actualTotalHpDamage = 0;
            Original323ObservedDummyCast cast;
            if (liveCombat == null || observingNativeImpact ||
                !liveCombat.TryGetPendingOriginalDummyCast(
                    originalCastId, out cast))
                return false;
            // Verification callbacks may be supplied by external adapters.
            // They must not step the simulation or consume the pending cast
            // while its original native impact is being certified.
            double timeBeforeWitness = liveCombat.Time;
            observingNativeImpact = true;
            try
            {
                Original323NativeDummyImpact impact;
                if (!impactWitness.TryConfirmOriginalDummyImpact(
                        cast, liveCombat, out impact) ||
                    impact == null ||
                    liveCombat.Time != timeBeforeWitness)
                    return false;
                Original323ObservedDummyCast stillPending;
                if (!liveCombat.TryGetPendingOriginalDummyCast(
                        originalCastId, out stillPending) ||
                    !Object.ReferenceEquals(stillPending, cast))
                    return false;
                return liveCombat.TryCommitObservedOriginalDummyImpact(
                    originalCastId, impact, out actualTotalHpDamage);
            }
            finally
            {
                observingNativeImpact = false;
            }
        }
    }
}
