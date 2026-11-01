using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // This is an explicitly incomplete tooltip-derived OFFLINE model for
        // the next thirty units. It never enters the original-verified attack
        // path. Attribute scaling, native dummies, mana/health transformations,
        // boss resistance, original random ordering and Warcraft magic armor
        // remain unverified. Do not use these packets as original parity.
        private void ExecuteSourceModelNextThirtyProfileAttacks(
            CharacterInstance actor, SpatialCombatEnemy primary,
            SourceModelNextThirtyUnit row)
        {
            string attackerId = InstanceKey(actor.Id);
            if (actor.Profile == null || actor.Profile.UnitId != row.CatalogId)
            {
                ReportSourceModelNextThirtyDeferred(attackerId, primary.Id,
                    "스킬 프로필 또는 유닛 ID 불일치");
                return;
            }

            int completedModels = 0;
            foreach (string approvedName in row.PracticeAttackSkills)
            {
                ImportedCombatSkill skill = null;
                foreach (var candidate in actor.Profile.Skills)
                    if (candidate.Name == approvedName)
                    {
                        skill = candidate;
                        break;
                    }
                if (!SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill))
                {
                    ReportSourceModelNextThirtyDeferred(attackerId, primary.Id,
                        approvedName + " 원본 조건 또는 복합 효과 미해석");
                    continue;
                }
                completedModels++;
                if (skill.Trigger == "onAttack" &&
                    sourceModelRules.NextScriptRoll(1000000) >=
                    (int)Math.Round(skill.ChancePercent.Value * 10000,
                        MidpointRounding.AwayFromZero)) continue;
                var shape = skill.Target == "single" ?
                    SourceScriptHitShape.Direct : SourceScriptHitShape.Circle;
                var template = new SourceScriptHit(shape, skill.Radius ?? 0,
                    0, 0, 1, 1, Ordr323AttackType.Spells);
                var targets = SourceScriptTargets(actor, primary, template);
                if (targets.Count == 0) continue;
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated, attackerId,
                    primary.Id, skill.Name,
                    "TOOLTIP_MODEL_ONLY " + row.CatalogId +
                    " 공격 선언 / 결과·시점·난수 원본 미검증", 0));
                ApplySourceModelNextThirtySkill(actor, primary, skill, targets);
            }
            if (completedModels != actor.Profile.Skills.Count)
                ReportSourceModelNextThirtyDeferred(attackerId, primary.Id,
                    row.CatalogId + " 도감 " + actor.Profile.Skills.Count +
                    "개 중 안전한 연습 타격 " + completedModels +
                    "개만 연결; 나머지 스킬은 실행하지 않음");
        }

        private void ReportSourceModelNextThirtyDeferred(string actorId,
            string enemyId, string reason)
        {
            if (!sourceBatchPendingReported.Add(actorId + ":next-thirty:" + reason))
                return;
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.EffectDeferred, actorId, enemyId,
                "다음 30명 스킬", "MODEL_ONLY 보류: " + reason, 0));
        }

        private void ApplySourceModelNextThirtySkill(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            IReadOnlyList<SpatialCombatEnemy> targets)
        {
            string attackerId = InstanceKey(actor.Id);
            foreach (var component in skill.Components)
            {
                if (component.Type == "stun")
                {
                    // Native hero/boss resistance and immunity are not known.
                    // Only ordinary line enemies receive the practice stun.
                    foreach (var target in targets)
                    {
                        if (target.Kind != CharacterEnemyKind.Line ||
                            !target.Enemy.IsAlive ||
                            (component.Text("scope") == "singlePrimaryTarget" &&
                             !ReferenceEquals(primary, target))) continue;
                        string source = "practice-tooltip-stun:" + attackerId +
                            ":" + skill.Id;
                        target.Enemy.Status.RemoveSource(source);
                        target.Enemy.Status.Apply(new CombatEffect(source,
                            CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill,
                            1, component.Number("durationSeconds").Value));
                        events.Add(new CharacterCombatEvent(
                            CharacterCombatEventType.StatusApplied, attackerId,
                            target.Id, skill.Name,
                            "TOOLTIP_MODEL_ONLY 일반 몬스터 기절; 원본 저항 미검증", 0));
                    }
                    continue;
                }

                double multiplier = 1;
                var range = component.Get("multiplierRange") as List<object>;
                if (range != null)
                {
                    double minimum = (double)range[0];
                    double maximum = (double)range[1];
                    multiplier = Ordr323MapDamageMath.ScriptDamageBeforeDefense(1,
                        minimum, maximum,
                        sourceModelRules.NextScriptRoll(1000000) / 1000000.0);
                }
                double raw = component.Number("amount").Value * multiplier;
                foreach (var target in targets)
                {
                    SpatialCombatEnemy current;
                    if (!target.Enemy.IsAlive ||
                        !enemies.TryGetValue(target.Id, out current) ||
                        !ReferenceEquals(current, target) ||
                        !CanAttackInThisLane(actor, target)) continue;
                    Ordr323ArmorType armor;
                    if (!target.Enemy.Defenses.BaseArmor.HasValue ||
                        !Ordr323OriginalDamageRules.TryReadArmorType(
                            target.Enemy.Defenses.WarcraftArmorType, out armor))
                    {
                        ReportSourceModelNextThirtyDeferred(attackerId,
                            target.Id, "대상 방어력·유형 자료 부재");
                        continue;
                    }
                    // Follow the existing practice arithmetic; physical and
                    // magical native schools are intentionally NOT certified.
                    double modeled = Ordr323MapDamageMath.ApplyVerifiedFactors(
                        raw, Ordr323AttackType.Spells, armor, true, true,
                        target.Enemy.Defenses.BaseArmor.Value -
                        SourceModelUnitEffects.ResolvePracticeArmorReduction(
                            target.Enemy.Status.Snapshot()), false, 0, 1);
                    if (modeled >= long.MaxValue)
                        throw new OverflowException("도감 모델 피해량 범위 초과");
                    long actual;
                    if (!resolution.TryApplySkillDamage(attackerId, target.Id,
                            (long)Math.Floor(modeled), out actual)) continue;
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillDamage, attackerId,
                        target.Id, skill.Name,
                        "TOOLTIP_MODEL_ONLY " + component.Type +
                        " / 원작 최종 방어·면역·속성 보정 미검증", actual));
                    if (!target.Enemy.IsAlive)
                        events.Add(new CharacterCombatEvent(
                            CharacterCombatEventType.EnemyDefeated, attackerId,
                            target.Id, skill.Name, "연습 도감 스킬로 적 처치", actual));
                }
            }
        }
    }
}
