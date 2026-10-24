using System;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Native AHbh/Bash: a Warcraft "hero" target is not necessarily the same
    // thing as a mobile Boss/Story enemy. Never derive the duration from lane.
    // This port supplies independently verified current target flags, including
    // immunity from the original enemy object and any live modifiers.
    public interface IOriginal323UncommonBashTargetRules
    {
        bool TryInspect(CombatEnemy enemy, out bool isWarcraftHero,
            out bool isBashImmune, out string originalEvidence);
    }

    // First source-checked complete native effect: Brook's A07P stun.
    // No guessed armor conversion or physical damage is added (AHbh DataC1=0).
    // Other uncommon skills are deliberately left for separate damage/target
    // implementations; do not downgrade them to this stun-only handler.
    public sealed class Original323UncommonBashWorld : IOriginalUncommonVerifiedProcWorld
    {
        public const string AbilityRawcode = "A07P";
        public const string OwnerCatalogId = "U-03";
        private readonly IOriginal323UncommonBashTargetRules targetRules;

        public Original323UncommonBashWorld(
            IOriginal323UncommonBashTargetRules verifiedTargetRules)
        {
            targetRules = verifiedTargetRules ??
                throw new ArgumentNullException(nameof(verifiedTargetRules));
        }

        private static bool IsSupported(OriginalUncommonProc proc)
        {
            return proc != null && proc.UnitId == OwnerCatalogId &&
                proc.OriginalUnitRawcode == "h00D" &&
                proc.AbilityRawcode == AbilityRawcode &&
                proc.ProcId == "A07P:bash" &&
                proc.Kind == OriginalUncommonProcKind.BashStun &&
                proc.ChancePerMillion == 100000 &&
                proc.BonusDamage == 0 &&
                proc.NormalStunSeconds == 0.3 &&
                proc.HeroStunSeconds == 0.15;
        }

        public bool SupportsCompleteEffect(OriginalUncommonProc proc)
        {
            return IsSupported(proc);
        }

        private bool TryEligible(OriginalUncommonProc proc, CombatEnemy enemy,
            out double seconds, out string evidence)
        {
            seconds = 0;
            evidence = null;
            if (!IsSupported(proc) || enemy == null || !enemy.IsAlive)
                return false;
            bool isHero, immune;
            string proof;
            if (!targetRules.TryInspect(enemy, out isHero, out immune, out proof) ||
                string.IsNullOrWhiteSpace(proof) || immune)
                return false;
            // Native A07P: Dur1=.3, HeroDur1=.15, DataA1=10%,
            // DataB1=0, DataC1=0, BuffID1=BPSE; source W3X AbilityData.slk.
            seconds = isHero ? proc.HeroStunSeconds : proc.NormalStunSeconds;
            evidence = proof;
            return seconds > 0;
        }

        public bool CanAffectTarget(OriginalUncommonProc proc, string attackerId,
            CombatEnemy target)
        {
            double duration;
            string proof;
            return !string.IsNullOrWhiteSpace(attackerId) &&
                TryEligible(proc, target, out duration, out proof);
        }

        public bool TryApplyCompleteEffect(OriginalUncommonProc proc,
            CharacterCombatRuntime world, string attackerId,
            string targetId, out string originalProof)
        {
            originalProof = null;
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (string.IsNullOrWhiteSpace(attackerId) ||
                string.IsNullOrWhiteSpace(targetId))
                return false;
            CombatEnemy enemy = world.EnemyOf(targetId);
            CombatStatusEngine status = world.StatusOf(targetId);
            double duration;
            string proof;
            if (status == null || !TryEligible(proc, enemy, out duration, out proof))
                return false;
            status.Apply(new CombatEffect(
                "original:A07P:" + attackerId, CombatEffectType.Stun,
                CombatEffectOrigin.TriggeredSkill, 1, duration));
            originalProof = "W3X AHbh A07P BPSE / " + proof +
                " / duration=" + duration.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
    }
}
