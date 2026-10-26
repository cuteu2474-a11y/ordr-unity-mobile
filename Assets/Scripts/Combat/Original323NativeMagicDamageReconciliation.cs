using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Original Warcraft defense ability A04Q (Def5): verified level 1..41.
    // These values are not permission to assume a native spell actually
    // uses Def5 or that all OTHER native modifiers can be ignored.
    public sealed class Original323ObservedNativeMagicHit
    {
        public string TargetId { get; private set; }
        // Observed native damage at the EXACT pipeline stage just BEFORE
        // the single A04Q factor. All non-A04Q mechanics are already handled.
        public float NativeDamageBeforeA04Q { get; private set; }
        public int OriginalA04QLevelAtImpact { get; private set; }
        public bool OriginalNativeDefendApplied { get; private set; }
        // Observed change to Warcraft native HP, already capped at current HP.
        public long ObservedFinalHpLoss { get; private set; }
        public string OriginalStageEvidence { get; private set; }

        public Original323ObservedNativeMagicHit(string targetId,
            float damageBeforeA04Q, int originalA04QLevel,
            bool nativeDefendApplied, long observedFinalHpLoss,
            string originalStageEvidence)
        {
            if (string.IsNullOrWhiteSpace(targetId) ||
                float.IsNaN(damageBeforeA04Q) ||
                float.IsInfinity(damageBeforeA04Q) ||
                damageBeforeA04Q < 0 ||
                observedFinalHpLoss < 0 ||
                string.IsNullOrWhiteSpace(originalStageEvidence))
                throw new ArgumentException(
                    "원본 마법 주문의 타격 대상·방어 전 피해·실제 체력 감소 증빙이 필요합니다.");
            TargetId = targetId;
            NativeDamageBeforeA04Q = damageBeforeA04Q;
            OriginalA04QLevelAtImpact = originalA04QLevel;
            OriginalNativeDefendApplied = nativeDefendApplied;
            ObservedFinalHpLoss = observedFinalHpLoss;
            OriginalStageEvidence = originalStageEvidence;
        }
    }

    public sealed class Original323ObservedNativeMagicImpact
    {
        public string OriginalCastId { get; private set; }
        public string OriginalImpactId { get; private set; }
        public double OriginalTimeSeconds { get; private set; }
        public bool SpellCompleted { get; private set; }
        public IReadOnlyList<Original323NativeSelectedSpellTarget>
            SelectedTargets { get; private set; }
        public IReadOnlyList<Original323ObservedNativeMagicHit> MagicHits
        {
            get; private set;
        }
        public string OriginalImpactEvidence { get; private set; }

        public Original323ObservedNativeMagicImpact(string castId,
            string impactId, double nativeTimeSeconds, bool completed,
            IReadOnlyList<Original323NativeSelectedSpellTarget> nativeSelected,
            IReadOnlyList<Original323ObservedNativeMagicHit> nativeHits,
            string originalImpactEvidence)
        {
            if (string.IsNullOrWhiteSpace(castId) ||
                string.IsNullOrWhiteSpace(impactId) ||
                double.IsNaN(nativeTimeSeconds) ||
                double.IsInfinity(nativeTimeSeconds) ||
                nativeTimeSeconds < 0 ||
                nativeSelected == null || nativeHits == null ||
                nativeSelected.Count > 512 || nativeHits.Count > 512 ||
                string.IsNullOrWhiteSpace(originalImpactEvidence))
                throw new ArgumentException(
                    "원본 마법 타격의 시점·선정 대상·처리 근거가 필요합니다.");
            var targets = new List<Original323NativeSelectedSpellTarget>();
            foreach (var target in nativeSelected)
            {
                if (target == null) throw new ArgumentException("원본 선정 대상 누락");
                targets.Add(target);
            }
            var damage = new List<Original323ObservedNativeMagicHit>();
            foreach (var hit in nativeHits)
            {
                if (hit == null) throw new ArgumentException("원본 마법 피해 기록 누락");
                damage.Add(hit);
            }
            OriginalCastId = castId;
            OriginalImpactId = impactId;
            OriginalTimeSeconds = nativeTimeSeconds;
            SpellCompleted = completed;
            SelectedTargets = targets.AsReadOnly();
            MagicHits = damage.AsReadOnly();
            OriginalImpactEvidence = originalImpactEvidence;
        }
    }

    // Native source must observe original target order, magic immunity,
    // non-A04Q modifiers, real pre-A04Q damage, whether Defend affected
    // the SPELL, and the actual post-impact HP loss independently.
    public interface IOriginal323NativeMagicImpactWitness
    {
        bool TryConfirmOriginalNativeMagicImpact(
            Original323ObservedDummyCast cast, CharacterCombatRuntime combat,
            out Original323ObservedNativeMagicImpact observedImpact);
    }

    // During the original Warcraft native impact, this observer must
    // independently verify that the complete CURRENT set of relevant
    // magic-reduction / amplification effects has already been resolved
    // into NativeDamageBeforeA04Q. It is not a guessed stacking formula.
    // An earlier cast-time observation cannot certify impact-time effects.
    public interface IOriginal323NativeMagicModifierWitness
    {
        bool TryConfirmNativeModifiersAtImpact(
            CombatEnemy target, string abilityRawcode,
            Original323ObservedNativeMagicHit currentHit,
            IReadOnlyList<CombatEffect> activeRelevantEffects,
            out string observedNativeModifierEvidence);
    }

    // Warcraft native real-to-HP rounding is not yet source-confirmed.
    // A real engine-backed policy must be supplied; the adapter cannot
    // silently choose floor, nearest or ceiling.
    public interface IOriginal323NativeMagicHpConversion
    {
        bool TryConvertNativeSpellDamage(float damageAfterDefend,
            string originalAbilityRawcode, out long nativeQuantizedDamage);
    }

    // Reconciles ORIGINAL observed values; never derives raw damage from
    // AbilityData.slk's DataA1 or rewrites a native impact report.
    public static class Original323NativeMagicDamageReconciler
    {
        public static bool TryReconcile(
            CombatEnemy liveMonster, string originalAbilityRawcode,
            Original323ObservedNativeMagicHit observed,
            IOriginal323NativeMagicHpConversion verifiedNativeHp,
            out Original323NativeDummySpellHit verifiedHit)
        {
            return TryReconcile(liveMonster, originalAbilityRawcode,
                observed, verifiedNativeHp, null, out verifiedHit);
        }

        // This overload permits source-witnessed stacks without inventing
        // whether their contributions add, multiply, clamp, or override.
        public static bool TryReconcile(
            CombatEnemy liveMonster, string originalAbilityRawcode,
            Original323ObservedNativeMagicHit observed,
            IOriginal323NativeMagicHpConversion verifiedNativeHp,
            IOriginal323NativeMagicModifierWitness nativeModifierWitness,
            out Original323NativeDummySpellHit verifiedHit)
        {
            verifiedHit = null;
            if (liveMonster == null || !liveMonster.IsAlive ||
                observed == null || verifiedNativeHp == null ||
                liveMonster.Id != observed.TargetId ||
                (originalAbilityRawcode != "A0VS" &&
                 originalAbilityRawcode != "A086") ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(
                    observed.OriginalStageEvidence))
                return false;

            var defense = liveMonster.Defenses;
            if (defense == null || !defense.MagicAbilityLevel.HasValue ||
                !defense.HasVerifiedMagicResistance ||
                defense.MagicAbilityLevel.Value !=
                    observed.OriginalA04QLevelAtImpact)
                return false;

            double originalFactor;
            if (!Ordr323VerifiedDefenseAbilities.TryGetMagicDamageTakenFactor(
                observed.OriginalA04QLevelAtImpact, out originalFactor) ||
                Math.Abs(defense.MagicResistancePercent.Value -
                    (1.0 - originalFactor) * 100.0) > 0.0001)
                return false;

            // Snapshot impact-time effects, not cast-time ones. Any source
            // bonus that can influence magical HP damage requires a fully
            // witnessed native stage; never assume generic effect stacking.
            var relevant = new List<CombatEffect>();
            foreach (var effect in liveMonster.Status.Snapshot())
                if (effect.Type == CombatEffectType.MagicResistanceReduction ||
                    effect.Type == CombatEffectType.OriginalA04QLevelIncrease ||
                    effect.Type == CombatEffectType.MagicDamageAmplification ||
                    effect.Type == CombatEffectType.AllDamageAmplification ||
                    effect.Type == CombatEffectType.ExplosiveDamageAmplification)
                    relevant.Add(effect);
            string stackingEvidence = null;
            if (relevant.Count > 0)
            {
                if (nativeModifierWitness == null ||
                    !nativeModifierWitness.TryConfirmNativeModifiersAtImpact(
                        liveMonster, originalAbilityRawcode, observed,
                        relevant.AsReadOnly(), out stackingEvidence) ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(
                        stackingEvidence))
                    return false;
                // Crucially, do not multiply raw damage AGAIN here.
                // Its independently observed pre-A04Q native stage already
                // includes the confirmed effects and engine stacking.
            }

            float damageAfterDefend = observed.NativeDamageBeforeA04Q;
            if (observed.OriginalNativeDefendApplied)
                damageAfterDefend *= (float)originalFactor;
            if (float.IsNaN(damageAfterDefend) ||
                float.IsInfinity(damageAfterDefend) || damageAfterDefend < 0)
                return false;

            long converted;
            if (!verifiedNativeHp.TryConvertNativeSpellDamage(
                    damageAfterDefend, originalAbilityRawcode,
                    out converted) || converted < 0)
                return false;

            // The independent original HP observation is decisive. If
            // even one target disagrees, the whole native impact fails.
            if (Math.Min(converted, liveMonster.Health) !=
                observed.ObservedFinalHpLoss)
                return false;
            verifiedHit = new Original323NativeDummySpellHit(
                observed.TargetId, observed.ObservedFinalHpLoss,
                observed.OriginalStageEvidence +
                " / original A04Q Lv." +
                observed.OriginalA04QLevelAtImpact +
                (observed.OriginalNativeDefendApplied ?
                    " applied" : " source-confirmed bypass") +
                (stackingEvidence == null ? "" :
                    " / original native modifier stage: " +
                    stackingEvidence));
            return true;
        }
    }

    // Connects a real Warcraft observation to the EXISTING deferred spell
    // impact ledger. No damage is committed until every selected target
    // and every final HP amount agree with original defense data.
    public sealed class Original323VerifiedNativeMagicImpactAdapter :
        IOriginal323DummySpellImpactWitness
    {
        private readonly IOriginal323NativeMagicImpactWitness original;
        private readonly IOriginal323NativeMagicHpConversion quantization;
        private readonly IOriginal323NativeMagicModifierWitness modifiers;

        public Original323VerifiedNativeMagicImpactAdapter(
            IOriginal323NativeMagicImpactWitness source,
            IOriginal323NativeMagicHpConversion nativeHp)
            : this(source, nativeHp, null)
        { }

        public Original323VerifiedNativeMagicImpactAdapter(
            IOriginal323NativeMagicImpactWitness source,
            IOriginal323NativeMagicHpConversion nativeHp,
            IOriginal323NativeMagicModifierWitness nativeModifiers)
        {
            original = source ?? throw new ArgumentNullException(nameof(source));
            quantization = nativeHp ??
                throw new ArgumentNullException(nameof(nativeHp));
            modifiers = nativeModifiers;
        }

        public bool TryConfirmOriginalDummyImpact(
            Original323ObservedDummyCast cast, CharacterCombatRuntime combat,
            out Original323NativeDummyImpact verifiedImpact)
        {
            verifiedImpact = null;
            if (cast == null || combat == null) return false;
            Original323NativeDummyAbilityFacts ability;
            if (!Original323NativeDummyAbilityCatalog.TryGet(
                cast.OriginalOrder, out ability))
                return false;
            Original323ObservedNativeMagicImpact observed;
            if (!original.TryConfirmOriginalNativeMagicImpact(
                    cast, combat, out observed) ||
                observed == null ||
                observed.OriginalCastId != cast.CastId ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(
                    observed.OriginalImpactEvidence))
                return false;

            var selected = new HashSet<string>(StringComparer.Ordinal);
            var immune = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in observed.SelectedTargets)
            {
                if (!Original323BossRoundRules.TrustedRuntimeEvidence(
                        target.OriginalSelectionEvidence) ||
                    !selected.Add(target.TargetId) ||
                    combat.EnemyOf(target.TargetId) == null)
                    return false;
                if (target.OriginalMagicImmune)
                    immune.Add(target.TargetId);
            }
            if (ability.MaxNativeTargets.HasValue &&
                selected.Count > ability.MaxNativeTargets.Value)
                return false;

            var confirmed = new List<Original323NativeDummySpellHit>();
            var hitIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var nativeHit in observed.MagicHits)
            {
                if (!hitIds.Add(nativeHit.TargetId) ||
                    !selected.Contains(nativeHit.TargetId) ||
                    immune.Contains(nativeHit.TargetId))
                    return false;
                Original323NativeDummySpellHit checkedHit;
                if (!Original323NativeMagicDamageReconciler.TryReconcile(
                        combat.EnemyOf(nativeHit.TargetId),
                        ability.AbilityRawcode, nativeHit,
                        quantization, modifiers, out checkedHit))
                    return false;
                confirmed.Add(checkedHit);
            }

            verifiedImpact = new Original323NativeDummyImpact(
                observed.OriginalImpactId, observed.OriginalTimeSeconds,
                observed.SpellCompleted, confirmed,
                observed.SelectedTargets, observed.OriginalImpactEvidence);
            return true;
        }
    }
}
