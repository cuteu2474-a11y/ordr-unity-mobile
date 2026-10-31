using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private long sourceBatchDelayedHitSequence;
        private readonly HashSet<string> sourceBatchPendingReported =
            new HashSet<string>(StringComparer.Ordinal);

        // Separate BFi calls are independent, in source order. Delayed
        // callbacks pin the original attacked position, not a live target ID.
        // All damage uses the existing MODEL_ONLY common resolution path.
        private void ExecuteSourceModelBatchAttack(CharacterInstance actor,
            SpatialCombatEnemy primary, SourceModelBatchAttackSpec spec)
        {
            string attackerId = InstanceKey(actor.Id);
            string skillName = actor.Catalog.Name + " 공격 기술";
            // Bsk: <=15% target life enters a guaranteed Bsj ONLY when Bdl
            // is true (none of A902/A911/A903/A904). If Bdl is false it
            // enters a 10% fallback instead. The practice enemy does not
            // carry that complete ability-level witness; guessing would
            // fabricate a guaranteed 900000/150000 hit.
            if (spec.CatalogId == "L-27" &&
                primary.Enemy.Health <= primary.Enemy.MaxHealth * .15)
            {
                if (sourceBatchPendingReported.Add(attackerId + ":Bsk:health-gate"))
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EffectDeferred, attackerId,
                        primary.Id, skillName,
                        "MODEL_ONLY Bsk: 대상 체력 15% 이하의 Bdl/A902/A911/A903/A904 판정 미검증; 공격 추가 피해 보류",
                        0));
                return;
            }
            foreach (var stage in spec.Stages)
            {
                int roll = stage.RollSides == 0
                    ? 0 : sourceModelRules.NextScriptRoll(stage.RollSides) + 1;
                if (stage.RollSides > 0 && roll > stage.RollSuccess)
                    continue;

                if (stage.Hits.Count > 0)
                {
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillActivated, attackerId,
                        primary.Id, skillName,
                        "MODEL_ONLY " + stage.JassPath +
                        (stage.RollSides == 0
                            ? " 무조건"
                            : " 확률 " + roll + "/" + stage.RollSides),
                        0));

                    if (stage.DelaySeconds == 0)
                        ApplySourceScriptHits(actor, primary,
                            spec.DamageLabel, stage.Hits);
                    else
                    {
                        double centerX = primary.X, centerY = primary.Y;
                        var captured = stage;
                        long sequence = checked(++sourceBatchDelayedHitSequence);
                        effectExecutor.SchedulePracticeSourceAttack(attackerId,
                            "practice-source-batch:" + attackerId + ":" + sequence,
                            captured.DelaySeconds, () => {
                                if (!characters.ContainsKey(actor.Id)) return;
                                ApplySourceScriptHits(actor, primary,
                                    spec.DamageLabel, captured.Hits,
                                    centerX, centerY);
                            });
                    }
                }

                if (!string.IsNullOrWhiteSpace(stage.Deferred))
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EffectDeferred,
                        attackerId, primary.Id, skillName,
                        "MODEL_ONLY: " + stage.JassPath +
                        " 원본 추가 동작 보류: " + stage.Deferred, 0));
            }

            // A single explicit notice per source actor, rather than a
            // misleading "skill activated" event for unexecuted branches.
            if (!string.IsNullOrWhiteSpace(spec.PendingNative) &&
                sourceBatchPendingReported.Add(attackerId + ":" + spec.Rawcode))
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.EffectDeferred,
                    attackerId, primary.Id, skillName,
                    "MODEL_ONLY 원본 " + spec.JassFunction +
                    " 미구현 경로: " + spec.PendingNative, 0));
        }
    }
}
