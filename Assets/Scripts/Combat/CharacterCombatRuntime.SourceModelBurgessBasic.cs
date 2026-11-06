using System;
using System.Globalization;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // D-06:skill:0 explicitly says ON EVERY HIT. This must observe the
        // positive basic-hit event, not an attack-declared callback that also
        // fires for misses and prevented hits. The 11% is the tooltip value;
        // older patch notes cite 10%, so original-version parity is pending.
        private void OnSourceModelBurgessSuccessfulBasicHit(string attackerId,
            string rawcode, string targetId, long actual)
        {
            if (actual <= 0 || rawcode != "h09V" ||
                !UsesSourceModelBasicAttacks || sourceModelRules == null ||
                !processingStep || game.Phase != Ordr.Mobile.Core.SessionPhase.Running)
                return;

            long actorId;
            CharacterInstance actor;
            SpatialCombatEnemy target;
            if (!long.TryParse(attackerId, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out actorId) ||
                !characters.TryGetValue(actorId, out actor) ||
                actor.Catalog.Id != "D-06" || actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != "h09V" ||
                actor.Profile == null || actor.Profile.UnitId != "D-06" ||
                actor.Profile.Skills.Count == 0 ||
                !enemies.TryGetValue(targetId, out target) ||
                !target.Enemy.IsAlive || !CanAttackInThisLane(actor, target))
                return;

            var skill = actor.Profile.Skills[0];
            if (skill.Id != "D-06:skill:0" ||
                skill.Name != "매 평타 추가 피해" ||
                skill.Trigger != "onEveryHit" || skill.Target != "single" ||
                skill.OriginalFields.Count != 4 ||
                skill.Components.Count != 1 ||
                skill.Components[0].Fields.Count != 4 ||
                skill.Components[0].Type != "magical" ||
                skill.Components[0].Number("amount") != 100000 ||
                skill.Components[0].Number("extraLineCurrentHealthExplosivePercent") != 11 ||
                skill.Components[0].Number("extraSpecialMagicDamage") != 220000)
                return;

            double baseAmount = skill.Components[0].Number("amount").Value;
            double originalCurrentHealth = target.Enemy.Health;
            // Separate packets keep the current-HP line explosion distinct
            // from the magical flat hit; both reuse the shared defense and
            // death ledger. No additional RNG is invented for an every-hit.
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, attackerId, target.Id,
                skill.Name, "TOOLTIP_MODEL_ONLY / 매 적중 100% 후속 타격", 0));
            ApplySourceModelSeventhTimedHit(actor, target, skill.Name,
                baseAmount, "magical");
            if (!target.Enemy.IsAlive) return;
            if (target.Kind == CharacterEnemyKind.Line)
                ApplySourceModelSeventhTimedHit(actor, target, skill.Name,
                    originalCurrentHealth *
                    skill.Components[0].Number("extraLineCurrentHealthExplosivePercent").Value / 100,
                    "explosive");
            else if (target.Kind == CharacterEnemyKind.Boss ||
                     target.Kind == CharacterEnemyKind.Enraged ||
                     target.Kind == CharacterEnemyKind.Story)
                ApplySourceModelSeventhTimedHit(actor, target, skill.Name,
                    skill.Components[0].Number("extraSpecialMagicDamage").Value,
                    "magical");
        }
    }
}
