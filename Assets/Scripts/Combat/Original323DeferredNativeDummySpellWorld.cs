using System;
using System.Collections.Generic;

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
        public string OriginalImpactEvidence { get; private set; }

        public Original323NativeDummyImpact(string impactId,
            double originalTimeSeconds, bool finished,
            IReadOnlyList<Original323NativeDummySpellHit> hits, string evidence)
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
            ImpactId = impactId; ObservedTimeSeconds = originalTimeSeconds;
            SpellCompleted = finished;
            Hits = snapshot.AsReadOnly();
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

    // Two-phase native spell bridge: ATTACK DECLARED -> cast registration;
    // actual Warcraft NATIVE IMPACT -> canonical HP/death ledger mutation.
    // This does not alter the existing independently verified instant path.
    public sealed class Original323DeferredNativeDummySpellWorld :
        IOriginal323VerifiedAttackDummySpellWorld
    {
        private readonly IOriginal323DummySpellCastWitness castWitness;
        private readonly IOriginal323DummySpellImpactWitness impactWitness;

        public Original323DeferredNativeDummySpellWorld(
            IOriginal323DummySpellCastWitness verifiedCasts,
            IOriginal323DummySpellImpactWitness verifiedImpacts)
        {
            castWitness = verifiedCasts ??
                throw new ArgumentNullException(nameof(verifiedCasts));
            impactWitness = verifiedImpacts ??
                throw new ArgumentNullException(nameof(verifiedImpacts));
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

        public bool TryApplyObservedImpact(CharacterCombatRuntime liveCombat,
            string originalCastId, out long actualTotalHpDamage)
        {
            actualTotalHpDamage = 0;
            Original323ObservedDummyCast cast;
            if (liveCombat == null ||
                !liveCombat.TryGetPendingOriginalDummyCast(
                    originalCastId, out cast))
                return false;
            Original323NativeDummyImpact impact;
            if (!impactWitness.TryConfirmOriginalDummyImpact(
                    cast, liveCombat, out impact) ||
                impact == null)
                return false;
            return liveCombat.TryCommitObservedOriginalDummyImpact(
                originalCastId, impact, out actualTotalHpDamage);
        }
    }
}
