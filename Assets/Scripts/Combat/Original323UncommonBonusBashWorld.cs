using System;

namespace Ordr.Mobile.Combat
{
    // May inspect original enemy immunity, ability target flags, hero status,
    // live armor, buffs, and 2.323[C] bonus-HP conversion. Must be side-effect
    // free: the pre-hit read must NOT roll RNG or alter units, buffs or gold.
    // BonusDamage on the original AHbh ability is OBJECT data, NOT final HP.
    public interface IOriginal323UncommonBonusDamageRules
    {
        bool TryResolveFinalBonus(OriginalUncommonProc proc, string attackerId,
            CombatEnemy enemy, out long finalHpDamage, out string originalEvidence);
    }

    // A07R (Ace), A07Q (Tashigi) and A07S (Hachi) use Warcraft's native
    // AHbh. They share the native mechanism, NOT the same target mask or stun.
    // The effect cannot start until BOTH the target's original class/immunity
    // and the 2.323[C] final-damage computation are independently verified.
    public sealed class Original323UncommonBonusBashWorld :
        IOriginalUncommonVerifiedProcWorld
    {
        private readonly IOriginal323UncommonBashTargetRules targets;
        private readonly IOriginal323UncommonBonusDamageRules damage;

        public Original323UncommonBonusBashWorld(
            IOriginal323UncommonBashTargetRules verifiedTargets,
            IOriginal323UncommonBonusDamageRules verifiedDamage)
        {
            targets = verifiedTargets ??
                throw new ArgumentNullException(nameof(verifiedTargets));
            damage = verifiedDamage ??
                throw new ArgumentNullException(nameof(verifiedDamage));
        }

        public static bool IsSupported(OriginalUncommonProc ability)
        {
            if (ability == null || ability.ChancePerMillion < 1)
                return false;
            if (ability.OriginalUnitRawcode == "h00K" &&
                ability.UnitId == "U-05" && ability.AbilityRawcode == "A07R" &&
                ability.ProcId == "A07R:flame" &&
                ability.Kind == OriginalUncommonProcKind.BonusPhysical)
                return ability.ChancePerMillion == 100000 &&
                    ability.BonusDamage == 200 &&
                    ability.NormalStunSeconds == 0 &&
                    ability.HeroStunSeconds == 0;
            if (ability.OriginalUnitRawcode == "h00E" &&
                ability.UnitId == "U-09" && ability.AbilityRawcode == "A07Q" &&
                ability.ProcId == "A07Q:shigure" &&
                ability.Kind == OriginalUncommonProcKind.BonusPhysicalStun)
                return ability.ChancePerMillion == 200000 &&
                    ability.BonusDamage == 200 &&
                    ability.NormalStunSeconds == 0.7 &&
                    ability.HeroStunSeconds == 0.35;
            if (ability.OriginalUnitRawcode == "h00F" &&
                ability.UnitId == "U-12" && ability.AbilityRawcode == "A07S" &&
                ability.ProcId == "A07S:fishman" &&
                ability.Kind == OriginalUncommonProcKind.BonusPhysicalStun)
                return ability.ChancePerMillion == 200000 &&
                    ability.BonusDamage == 150 &&
                    ability.NormalStunSeconds == 0.1 &&
                    ability.HeroStunSeconds == 0.05;
            return false;
        }

        public bool SupportsCompleteEffect(OriginalUncommonProc ability)
        {
            return IsSupported(ability);
        }

        private bool TryPrepare(OriginalUncommonProc ability, string attackerId,
            CombatEnemy enemy, out long finalHpDamage, out double stunSeconds,
            out string proof)
        {
            finalHpDamage = 0;
            stunSeconds = 0;
            proof = null;
            if (!IsSupported(ability) || string.IsNullOrWhiteSpace(attackerId) ||
                enemy == null || !enemy.IsAlive)
                return false;
            bool isWarcraftHero, isBashImmune;
            string targetProof;
            if (!targets.TryInspect(enemy, out isWarcraftHero, out isBashImmune,
                out targetProof) || string.IsNullOrWhiteSpace(targetProof) ||
                isBashImmune)
                return false;

            // The damage policy, not mobile lane kind, proves native AHbh's
            // original mask: A07R excludes neutral, A07Q/S include neutral.
            string damageProof;
            long verified;
            if (!damage.TryResolveFinalBonus(ability, attackerId, enemy,
                out verified, out damageProof) ||
                verified < 0 || string.IsNullOrWhiteSpace(damageProof))
                return false;
            finalHpDamage = verified;
            stunSeconds = isWarcraftHero
                ? ability.HeroStunSeconds : ability.NormalStunSeconds;
            proof = targetProof + " / " + damageProof;
            return true;
        }

        public bool CanAffectTarget(OriginalUncommonProc ability,
            string attackerId, CombatEnemy target)
        {
            long value;
            double duration;
            string proof;
            return TryPrepare(ability, attackerId, target,
                out value, out duration, out proof);
        }

        public bool TryApplyCompleteEffect(OriginalUncommonProc ability,
            CharacterCombatRuntime world, string attackerId, string targetId,
            out string originalProof)
        {
            originalProof = null;
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (string.IsNullOrWhiteSpace(targetId)) return false;
            var enemy = world.EnemyOf(targetId);
            long amount;
            double duration;
            string proof;
            if (!TryPrepare(ability, attackerId, enemy,
                out amount, out duration, out proof))
                return false;

            // Only a currently triggered original uncommon proc may commit a
            // final damage value through the same CombatResolution as basics.
            // This guarantees normal kill counters and participation hooks.
            long actualDamage;
            if (!world.TryCommitOriginalUncommonBonus(attackerId, targetId,
                ability.ProcId, amount, out actualDamage))
                return false;

            if (duration > 0)
            {
                // Bonus damage may have killed the enemy. Stun only surviving
                // targets (never a dead enemy or a newly spawned enemy ID).
                var status = world.StatusOf(targetId);
                var surviving = world.EnemyOf(targetId);
                if (status != null && surviving != null && surviving.IsAlive)
                    status.Apply(new CombatEffect("original:" +
                        ability.AbilityRawcode + ":" + attackerId,
                        CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill,
                        1, duration));
            }
            originalProof = "W3X native AHbh " + ability.AbilityRawcode +
                " / " + proof + " / actual bonus HP=" +
                actualDamage.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
    }

    // Composite is explicit; a verified Brook stun must not inadvertently
    // authorize unverified physical bonus, criticals, line magic, or multi-hit.
    public sealed class Original323UncommonBashBundleWorld :
        IOriginalUncommonVerifiedProcWorld
    {
        private readonly Original323UncommonBashWorld brook;
        private readonly Original323UncommonBonusBashWorld bonus;

        public Original323UncommonBashBundleWorld(
            IOriginal323UncommonBashTargetRules targets,
            IOriginal323UncommonBonusDamageRules damageRules)
        {
            brook = new Original323UncommonBashWorld(targets);
            bonus = new Original323UncommonBonusBashWorld(targets, damageRules);
        }

        private IOriginalUncommonVerifiedProcWorld Resolve(OriginalUncommonProc proc)
        {
            if (brook.SupportsCompleteEffect(proc)) return brook;
            if (bonus.SupportsCompleteEffect(proc)) return bonus;
            return null;
        }

        public bool SupportsCompleteEffect(OriginalUncommonProc proc)
        {
            return Resolve(proc) != null;
        }

        public bool CanAffectTarget(OriginalUncommonProc proc,
            string attackerId, CombatEnemy target)
        {
            var handler = Resolve(proc);
            return handler != null &&
                handler.CanAffectTarget(proc, attackerId, target);
        }

        public bool TryApplyCompleteEffect(OriginalUncommonProc proc,
            CharacterCombatRuntime runtime, string attackerId,
            string targetId, out string originalProof)
        {
            originalProof = null;
            var handler = Resolve(proc);
            return handler != null &&
                handler.TryApplyCompleteEffect(proc, runtime,
                    attackerId, targetId, out originalProof);
        }
    }
}
