using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // A single additional original A07M attack, with independently proven
    // targeting and FINAL HP damage for this specific enemy. Raw Warcraft
    // object dice or tooltip text must not be substituted for final HP.
    public sealed class Original323RobinSecondaryHit
    {
        public string TargetId { get; private set; }
        public long FinalHpDamage { get; private set; }
        public string Evidence { get; private set; }

        public Original323RobinSecondaryHit(string targetId, long finalHpDamage,
            string originalEvidence)
        {
            if (string.IsNullOrWhiteSpace(targetId) || finalHpDamage < 0 ||
                string.IsNullOrWhiteSpace(originalEvidence))
                throw new ArgumentException(
                    "A07M 추가 타격: 대상·최종 피해·원본 검증 근거 누락");
            TargetId = targetId;
            FinalHpDamage = finalHpDamage;
            Evidence = originalEvidence;
        }
    }

    // Multi-shot is one normal native basic hit plus at most TWO additional
    // eligible targets. Selection, per-target armor/dice, Warcraft target
    // masks, original missile impact timing and quantization are NOT inferred
    // from mobile proximity: this independent provider must establish them.
    //
    // Both functions must be read-only: no damage, RNG, mana or roster
    // mutations, and no synthetic damage when source verification is absent.
    public interface IOriginal323RobinVerifiedVolleyRules
    {
        bool CanAttempt(OriginalUncommonProc proc, string attackerId,
            CombatEnemy primary, out string originalEvidence);

        bool TryResolveAdditionalHits(OriginalUncommonProc proc,
            CharacterCombatRuntime world, string attackerId,
            string originalPrimaryTargetId,
            out IReadOnlyList<Original323RobinSecondaryHit> additionalHits,
            out string originalEvidence);
    }

    public sealed class Original323RobinMultiTargetWorld :
        IOriginalUncommonVerifiedProcWorld
    {
        private readonly IOriginal323RobinVerifiedVolleyRules rules;
        public Original323RobinMultiTargetWorld(
            IOriginal323RobinVerifiedVolleyRules originalRules)
        {
            rules = originalRules ??
                throw new ArgumentNullException(nameof(originalRules));
        }

        public static bool IsSupported(OriginalUncommonProc proc)
        {
            return proc != null && proc.UnitId == "U-01" &&
                proc.OriginalUnitRawcode == "h00O" &&
                proc.AbilityRawcode == "A07M" &&
                proc.ProcId == "A07M:multi3" &&
                proc.Kind == OriginalUncommonProcKind.MultiTarget &&
                proc.ChancePerMillion == 1000000 &&
                proc.NativeTargetCount == 3 &&
                proc.BonusDamage == 0 && proc.CriticalMultiplier == 0 &&
                proc.NormalStunSeconds == 0 && proc.HeroStunSeconds == 0;
        }

        public bool SupportsCompleteEffect(OriginalUncommonProc proc)
        {
            return IsSupported(proc);
        }

        public bool CanAffectTarget(OriginalUncommonProc proc, string attackerId,
            CombatEnemy target)
        {
            string evidence;
            return IsSupported(proc) && target != null && target.IsAlive &&
                !string.IsNullOrWhiteSpace(attackerId) &&
                rules.CanAttempt(proc, attackerId, target, out evidence) &&
                !string.IsNullOrWhiteSpace(evidence);
        }

        public bool TryApplyCompleteEffect(OriginalUncommonProc proc,
            CharacterCombatRuntime world, string attackerId,
            string primaryTargetId, out string originalProof)
        {
            originalProof = null;
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!IsSupported(proc) || string.IsNullOrWhiteSpace(attackerId) ||
                string.IsNullOrWhiteSpace(primaryTargetId)) return false;
            IReadOnlyList<Original323RobinSecondaryHit> additional;
            string evidence;
            if (!rules.TryResolveAdditionalHits(proc, world, attackerId,
                    primaryTargetId, out additional, out evidence) ||
                additional == null || additional.Count > 2 ||
                string.IsNullOrWhiteSpace(evidence))
                return false;
            long actual;
            if (!world.TryCommitOriginalRobinMultiTarget(
                    attackerId, primaryTargetId, proc.ProcId,
                    additional, out actual))
                return false;
            originalProof = "W3X A07M Robin additional targets=" +
                additional.Count.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) +
                " / actual secondary HP=" +
                actual.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                " / " + evidence;
            return true;
        }
    }
}
