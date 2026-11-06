using System;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // I-07's 75-HP ability is explicitly a one-second 450-radius damage
        // field with a separate 300-radius explosion. The source tooltip does
        // not define native tick cadence; ten model slices preserve the total
        // amount without misrepresenting an instantaneous two-million hit.
        private void ScheduleSourceModelSengokuExplosion(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill)
        {
            if (actor.Catalog.Id != "I-07" || actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != "h04E" ||
                !SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill))
                throw new InvalidOperationException("센고쿠 체력 폭발 승인 패킷 불일치");

            double centerX = primary.X, centerY = primary.Y;
            bool storyLane = IsStoryDeployed(actor.Id);
            long laneRevision = SourceModelLaneRevision(actor.Id);
            const int modelTicks = 10;
            double total = skill.Components[0].Number("totalAmount").Value;
            double duration = skill.Components[0].Number("durationSeconds").Value;
            double explosion = skill.Components[1].Number("amount").Value;
            string attacker = InstanceKey(actor.Id);

            for (int tick = 1; tick <= modelTicks; tick++)
            {
                int completedTick = tick;
                effectExecutor.SchedulePracticeSourceAttack(attacker,
                    "practice-sengoku-explosion:" + attacker + ":" + completedTick,
                    duration * completedTick / modelTicks, () => {
                        if (!CanFinishSourceModelDelayedAttack(actor.Id,
                                storyLane, laneRevision)) return;
                        // Pin the spell's cast point. Newly entered enemies
                        // receive remaining ticks; departed enemies do not.
                        var dotTargets = SourceScriptTargets(actor, primary,
                            new SourceScriptHit(SourceScriptHitShape.Circle,
                                skill.Radius.Value, 0, 0, 1, 1,
                                Ordr323AttackType.Spells), centerX, centerY);
                        foreach (var victim in dotTargets)
                            ApplySourceModelSeventhTimedHit(actor, victim,
                                skill.Name, total / modelTicks, "magical");

                        if (completedTick == modelTicks)
                        {
                            var explosionTargets = SourceScriptTargets(actor, primary,
                                new SourceScriptHit(SourceScriptHitShape.Circle,
                                    skill.Number("explosionRadius").Value,
                                    0, 0, 1, 1, Ordr323AttackType.Spells),
                                centerX, centerY);
                            foreach (var victim in explosionTargets)
                                ApplySourceModelSeventhTimedHit(actor, victim,
                                    skill.Name + " · 폭발", explosion, "magical");
                        }
                        RemoveDefeatedEnemies();
                    });
            }
            // The shared cast path already emits SkillActivated. Reporting
            // EffectDeferred here would wrongly label a scheduled skill as
            // unavailable in the player-facing combat event stream.
        }
    }
}
