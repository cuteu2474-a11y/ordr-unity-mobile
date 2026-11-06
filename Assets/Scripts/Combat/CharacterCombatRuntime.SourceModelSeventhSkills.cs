using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private readonly Dictionary<long, double> sourceModelSeventhReadyAt =
            new Dictionary<long, double>();

        private double SourceModelSeventhRawDamageAdjustment(
            CharacterInstance actor, SpatialCombatEnemy primary,
            ImportedCombatSkill skill, ImportedCombatEffect component,
            SpatialCombatEnemy target, double raw)
        {
            if (skill.Id != "TR-11:skill:4" || component.Type != "physical")
                return raw;
            // Only the documented per-buff and per-strength additions enter
            // the physical packet; absent verified attributes contribute zero.
            raw += SourceModelActiveBuffCount(actor.Id) *
                component.Number("plusBuffCountMultiplier").Value;
            if (sourceModelStrengthProvider != null)
            {
                double? strength = sourceModelStrengthProvider(actor.Id);
                if (strength.HasValue && strength.Value >= 0 &&
                    !double.IsNaN(strength.Value) &&
                    !double.IsInfinity(strength.Value))
                    raw += strength.Value * skill.Number("strengthScalingPerPoint").Value;
            }
            return raw;
        }

        private void ScheduleSourceModelIceAge(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill)
        {
            bool lane = IsStoryDeployed(actor.Id);
            long revision = SourceModelLaneRevision(actor.Id);
            effectExecutor.SchedulePracticeSourceAttack(InstanceKey(actor.Id),
                primary.Id, skill.Number("castTimeSeconds").Value, () => {
                    if (!CanFinishSourceModelDelayedAttack(actor.Id, lane, revision)) return;
                    SpatialCombatEnemy current;
                    if (!enemies.TryGetValue(primary.Id, out current) ||
                        !ReferenceEquals(current, primary) ||
                        !primary.Enemy.IsAlive || !CanAttackInThisLane(actor, primary))
                        return;
                    // Cast-time position is checked at completion, so newly
                    // entered enemies can be hit while departed ones cannot.
                    var actualTargets = SourceScriptTargets(actor, primary,
                        new SourceScriptHit(SourceScriptHitShape.Circle,
                            skill.Radius.Value, 0, 0, 1, 1,
                            Ordr323AttackType.Spells));
                    ApplySourceModelNextThirtySkill(actor, primary, skill, actualTargets);
                });
        }

        // Skill-specific timed packets use the existing hit point, defense,
        // status and death ledger. Clones are never fusion inventory units.
        private void ApplySourceModelSeventhTimedHit(CharacterInstance actor,
            SpatialCombatEnemy target, string name, double raw, string school)
        {
            if (!target.Enemy.IsAlive || !CanAttackInThisLane(actor, target) ||
                raw <= 0 || double.IsNaN(raw) || double.IsInfinity(raw)) return;
            Ordr323ArmorType armor;
            if (!target.Enemy.Defenses.BaseArmor.HasValue ||
                !Ordr323OriginalDamageRules.TryReadArmorType(
                    target.Enemy.Defenses.WarcraftArmorType, out armor)) return;
            double modeled = Ordr323MapDamageMath.ApplyVerifiedFactors(raw,
                Ordr323AttackType.Spells, armor, true, true,
                target.Enemy.Defenses.BaseArmor.Value -
                SourceModelUnitEffects.ResolvePracticeArmorReduction(
                    target.Enemy.Status.Snapshot()) -
                ArmorBreakStacksOf(target.Id), false, 0, 1);
            modeled *= SourceModelCompositeMagicFactor(target, school);
            if (modeled >= long.MaxValue || double.IsNaN(modeled) ||
                double.IsInfinity(modeled))
                throw new OverflowException("7차 연습 기술 피해 범위 초과");
            long actual;
            if (!resolution.TryApplySkillDamage(InstanceKey(actor.Id), target.Id,
                (long)Math.Floor(modeled), out actual)) return;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillDamage,
                InstanceKey(actor.Id), target.Id, name,
                "TOOLTIP_MODEL_ONLY / " + school + " 지속·소환 타격", actual));
            if (!target.Enemy.IsAlive)
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.EnemyDefeated,
                    InstanceKey(actor.Id), target.Id, name,
                    "연습 기술 피해로 처치", actual));
        }

        private double SourceModelSeventhKnownAttackBuff(long actorId, double raw)
        {
            CombatStatusEngine status;
            if (!unitStatuses.TryGetValue(InstanceKey(actorId), out status)) return raw;
            // Both the Zoro DoT's tooltip and Koby's inherited base attack
            // refer to buffs. Reuse the normal strongest-known-buff policy;
            // Fixed spell/DOT amounts are not native basic attack damage.
            return raw * (1 + SourceModelUnitEffects.ResolvePracticeAttackPower(
                status.Snapshot()) / 100);
        }

        private void ScheduleSourceModelZoroDot(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            ImportedCombatEffect component,
            IReadOnlyList<SpatialCombatEnemy> targets)
        {
            double interval = component.Number("tickIntervalSeconds").Value;
            double duration = component.Number("totalDurationSeconds").Value;
            int count = (int)Math.Floor(duration / interval + .000001);
            bool lane = IsStoryDeployed(actor.Id);
            long revision = SourceModelLaneRevision(actor.Id);
            foreach (var target in targets)
                for (int tick = 1; tick <= count; tick++)
                {
                    var victim = target;
                    effectExecutor.SchedulePracticeSourceAttack(InstanceKey(actor.Id),
                        victim.Id, tick * interval, () => {
                            SpatialCombatEnemy current;
                            if (!CanFinishSourceModelDelayedAttack(actor.Id, lane, revision) ||
                                !enemies.TryGetValue(victim.Id, out current) ||
                                !ReferenceEquals(victim, current)) return;
                            ApplySourceModelSeventhTimedHit(actor, victim, skill.Name,
                                SourceModelSeventhKnownAttackBuff(actor.Id,
                                    component.Number("perTick").Value), "physical");
                        });
                }
        }

        private void SpawnSourceModelKobyClone(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            ImportedCombatEffect component)
        {
            double duration = component.Number("durationSeconds").Value;
            double interval = actor.Mapping.AttackSpec.BasicAttack.IntervalSeconds;
            double attack = actor.Mapping.AttackSpec.BasicAttack.NominalAttackPower;
            if (interval <= 0 || double.IsNaN(interval) ||
                double.IsInfinity(interval) || attack < 0 ||
                double.IsNaN(attack) || double.IsInfinity(attack))
                throw new InvalidOperationException("코비 분신 기본 공격 근거가 올바르지 않습니다.");
            var clone = SpawnSourceTemporaryActor(actor, skill.Name,
                component.Text("unit"), duration, null, false,
                primary.X, primary.Y, true);
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 5초 코비 분신, 조합 재료가 아닌 전투 임시 객체", 0));
            int hits = (int)Math.Floor(duration / interval + .000001);
            bool lane = IsStoryDeployed(actor.Id);
            long revision = SourceModelLaneRevision(actor.Id);
            for (int hit = 1; hit <= hits; hit++)
                effectExecutor.SchedulePracticeSourceAttack(InstanceKey(actor.Id),
                    "practice-koby-clone:" + clone.Id, hit * interval, () => {
                        if (!CanFinishSourceModelDelayedAttack(actor.Id, lane, revision))
                            return;
                        bool exists = false;
                        foreach (var live in PracticeSummonsOf(actor.Id))
                            if (live.Id == clone.Id) { exists = true; break; }
                        if (!exists) return;
                        var candidates = BuildTargetCandidates(actor);
                        if (candidates.Count == 0) return;
                        string chosen = ChoosePreviewTarget(actor, candidates);
                        SpatialCombatEnemy victim;
                        if (chosen == null || !enemies.TryGetValue(chosen, out victim) ||
                            !victim.Enemy.IsAlive) return;
                        var hitTargets = SourceScriptTargets(actor, victim,
                            new SourceScriptHit(SourceScriptHitShape.Circle,
                                component.Number("explosiveRadius").Value,
                                0, 0, 1, 1, Ordr323AttackType.Spells));
                        foreach (var target in hitTargets)
                            ApplySourceModelSeventhTimedHit(actor, target,
                                skill.Name, SourceModelSeventhKnownAttackBuff(actor.Id,
                                    (attack + SourceModelUpgradeAttackBonus(actor.Id)) *
                                    component.Number("basicAttackExplosivePercent").Value / 100),
                                "explosive");
                        ApplySourceModelSeventhTimedHit(actor, victim, skill.Name,
                            component.Number("singleMagicalDamagePerHit").Value,
                            "magical");
                    });
        }
    }
}

