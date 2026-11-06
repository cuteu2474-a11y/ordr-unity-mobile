using System;
using System.Globalization;
using System.Collections.Generic;
using Ordr.Mobile.Data;

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
                actor.Profile.Skills.Count == 0)
                return;

            // The BASIC hit must reduce the elbow timer even when it kills
            // its victim; its unrelated every-hit damage cannot hit a corpse.
            ReduceSourceModelBurgessElbowCooldown(actor, actual);
            if (!enemies.TryGetValue(targetId, out target) ||
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

        // This owner-bound periodic model reuses the shared additional-auto
        // clock. One cooldown starts at 12s and may be shortened by at most
        // 9s using actual successful BASIC damage (1s per 10,000).
        // Fractional reductions are an explicit mobile tooltip approximation:
        // original JASS integer rounding and hit timing are not verified.
        private readonly Dictionary<long, double> sourceModelBurgessElbowReduction =
            new Dictionary<long, double>();

        private static bool HasSourceModelBurgessElbow(CharacterInstance actor)
        {
            if (actor == null || actor.Catalog.Id != "D-06" ||
                actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != "h09V" ||
                actor.Profile == null || actor.Profile.UnitId != "D-06" ||
                actor.Profile.Skills.Count != 2) return false;
            var skill = actor.Profile.Skills[1];
            return skill.Id == "D-06:skill:1" && skill.Name == "파동 엘보" &&
                skill.Trigger == "periodic" && skill.Target == "area" &&
                skill.Radius == 425 && skill.OriginalFields.Count == 8 &&
                skill.Number("periodSeconds") == 12 &&
                skill.Number("reductionPer10000BasicAttackDamageSeconds") == 1 &&
                skill.Number("maxReductionSeconds") == 9 &&
                skill.Components.Count == 2 &&
                skill.Components[0].Type == "magical" &&
                skill.Components[0].Fields.Count == 4 &&
                skill.Components[0].Number("amount") == 360000 &&
                skill.Components[0].Number("extraLineCurrentHealthExplosivePercent") == 36 &&
                skill.Components[0].Number("extraSpecialMagicDamage") == 720000 &&
                skill.Components[1].Type == "stun" &&
                skill.Components[1].Fields.Count == 2 &&
                skill.Components[1].Number("durationSeconds") == 2.5;
        }

        private void ReduceSourceModelBurgessElbowCooldown(
            CharacterInstance actor, long actualBasicDamage)
        {
            if (actualBasicDamage <= 0 || !HasSourceModelBurgessElbow(actor))
                return;
            double? due = NextAdditionalAutoPeriodicAt(actor);
            if (!due.HasValue) return;
            double alreadyReduced;
            sourceModelBurgessElbowReduction.TryGetValue(actor.Id,
                out alreadyReduced);
            var skill = actor.Profile.Skills[1];
            double remainingReduction = Math.Max(0,
                skill.Number("maxReductionSeconds").Value - alreadyReduced);
            double damageReduction = actualBasicDamage /
                10000.0 * skill.Number(
                    "reductionPer10000BasicAttackDamageSeconds").Value;
            // A scheduled event must never move into the past. The next
            // ExecuteAdditionalAutoPeriodic invocation may fire it NOW.
            double applied = Math.Min(Math.Min(remainingReduction,
                damageReduction), Math.Max(0, due.Value - Time));
            if (applied <= 0) return;
            additionalAutoReadyAt[InstanceKey(actor.Id) +
                ":D-06:skill:1"] = due.Value - applied;
            sourceModelBurgessElbowReduction[actor.Id] =
                alreadyReduced + applied;
        }

        private void ResetSourceModelBurgessElbowReduction(long actorId)
        {
            sourceModelBurgessElbowReduction.Remove(actorId);
        }

        private void ForgetSourceModelBurgessElbow(long actorId)
        {
            ResetSourceModelBurgessElbowReduction(actorId);
            additionalAutoReadyAt.Remove(InstanceKey(actorId) +
                ":D-06:skill:1");
        }

        private void ApplySourceModelBurgessElbow(CharacterInstance actor,
            SpatialCombatEnemy primary, IReadOnlyList<SpatialCombatEnemy> targets)
        {
            if (!HasSourceModelBurgessElbow(actor) || targets.Count == 0 ||
                !CanAttackInThisLane(actor, primary)) return;
            var skill = actor.Profile.Skills[1];
            var damage = skill.Components[0];
            string attackerId = InstanceKey(actor.Id);
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated,
                attackerId, primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 12초 주기, 기본 피해 1만당 최대 9초 감소", 0));
            foreach (var target in targets)
            {
                if (!target.Enemy.IsAlive ||
                    !CanAttackInThisLane(actor, target)) continue;
                double healthAtHit = target.Enemy.Health;
                ApplySourceModelSeventhTimedHit(actor, target, skill.Name,
                    damage.Number("amount").Value, "magical");
                if (!target.Enemy.IsAlive) continue;
                if (target.Kind == CharacterEnemyKind.Line)
                    ApplySourceModelSeventhTimedHit(actor, target, skill.Name,
                        healthAtHit * damage.Number(
                            "extraLineCurrentHealthExplosivePercent").Value /
                        100, "explosive");
                else if (target.Kind == CharacterEnemyKind.Boss ||
                         target.Kind == CharacterEnemyKind.Enraged ||
                         target.Kind == CharacterEnemyKind.Story)
                    ApplySourceModelSeventhTimedHit(actor, target, skill.Name,
                        damage.Number("extraSpecialMagicDamage").Value, "magical");
                // Same practice immunity boundary as other line stuns.
                if (target.Enemy.IsAlive)
                    ApplySourceModelLineStun(attackerId, target, skill.Name,
                        skill.Components[1].Number("durationSeconds").Value);
            }
        }
    }
}
