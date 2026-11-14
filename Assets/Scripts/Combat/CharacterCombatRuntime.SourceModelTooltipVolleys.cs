using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // Use the existing source-model damage resolution, roster identity,
        // status lifecycle and combat clock. Neither damage nor a stun is
        // applied to a deleted/replaced enemy or an enemy on another lane.
        private void ApplySourceModelTooltipDamage(CharacterInstance actor,
            SpatialCombatEnemy target, string skill, string component,
            double raw, bool fixedDamage,
            Ordr323AttackType attackType = Ordr323AttackType.Spells)
        {
            SpatialCombatEnemy current;
            CharacterInstance livingCaster;
            if (raw <= 0 || double.IsNaN(raw) || double.IsInfinity(raw) ||
                raw >= long.MaxValue ||
                !characters.TryGetValue(actor.Id, out livingCaster) ||
                !Object.ReferenceEquals(actor, livingCaster) ||
                !enemies.TryGetValue(target.Id, out current) ||
                !Object.ReferenceEquals(target, current) ||
                !current.Enemy.IsAlive || !CanAttackInThisLane(actor, current))
                return;
            double finalDamage = raw;
            if (!fixedDamage)
            {
                Ordr323ArmorType armor;
                if (!current.Enemy.Defenses.BaseArmor.HasValue ||
                    !Ordr323OriginalDamageRules.TryReadArmorType(
                        current.Enemy.Defenses.WarcraftArmorType, out armor))
                {
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EffectDeferred,
                        InstanceKey(actor.Id), current.Id, skill,
                        "TOOLTIP_MODEL_ONLY 적 방어 자료가 없어 피해 보류", 0));
                    return;
                }
                finalDamage = Ordr323MapDamageMath.ApplyVerifiedFactors(
                    raw, attackType, armor, true, true,
                    current.Enemy.Defenses.BaseArmor.Value -
                        SourceModelUnitEffects.ResolvePracticeArmorReduction(
                            current.Enemy.Status.Snapshot()) -
                        ArmorBreakStacksOf(current.Id), false, 0, 1);
            }
            if (double.IsNaN(finalDamage) || double.IsInfinity(finalDamage) ||
                finalDamage >= long.MaxValue)
                throw new OverflowException("도감 스킬 피해량이 한도를 초과했습니다.");
            long actual;
            string owner = InstanceKey(actor.Id);
            if (!resolution.TryApplySkillDamage(owner, current.Id,
                (long)Math.Floor(finalDamage), out actual)) return;
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillDamage, owner, current.Id,
                skill, "TOOLTIP_MODEL_ONLY " + component +
                " / 원본 실제 타격 간격·방어 및 면역 비교 전", actual));
            if (!current.Enemy.IsAlive)
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.EnemyDefeated, owner, current.Id,
                    skill, "도감 기반 스킬로 처치", actual));
        }

        private bool TryCastSourceModelTooltipVolley(CharacterInstance actor,
            ImportedCombatSkill skill, string primaryEnemyId,
            IReadOnlyList<string> approvedNames, out AbilityCastResult result)
        {
            result = AbilityCastResult.UnsupportedEffect;
            if (!SourceModelTooltipVolleyCatalog.IsApproved(skill) ||
                actor == null || actor.Mapping == null || actor.Profile == null ||
                actor.Profile.UnitId != actor.Catalog.Id ||
                sourceModelRules == null || !UsesSourceModelBasicAttacks)
                return true;
            bool nameApproved = false;
            foreach (var name in approvedNames)
                if (name == skill.Name) nameApproved = true;
            if (!nameApproved ||
                (skill.Id == "E-06:skill:0" &&
                 (actor.Catalog.Id != "E-06" ||
                  actor.Mapping.AttackSpec.UnitRawCode != "h057")) ||
                (skill.Id == "E-12:skill:0" &&
                 (actor.Catalog.Id != "E-12" ||
                  actor.Mapping.AttackSpec.UnitRawCode != "h05C")))
                return true;
            SpatialCombatEnemy primary;
            if (string.IsNullOrWhiteSpace(primaryEnemyId) ||
                !enemies.TryGetValue(primaryEnemyId, out primary) ||
                !primary.Enemy.IsAlive || !CanAttackInThisLane(actor, primary))
            {
                result = AbilityCastResult.MissingTarget;
                return true;
            }
            List<SpatialCombatEnemy> targets;
            if (skill.Id == "E-12:skill:0")
                targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                    SourceScriptHitShape.AttackerCircle, 750, 0, 0, 1, 1,
                    Ordr323AttackType.Spells));
            else
            {
                targets = new List<SpatialCombatEnemy>();
                foreach (var enemy in enemies.Values)
                    if (enemy.Enemy.IsAlive && CanAttackInThisLane(actor, enemy))
                        targets.Add(enemy);
                targets.Sort((a, b) =>
                    StringComparer.Ordinal.Compare(a.Id, b.Id));
            }
            if (targets.Count == 0)
            {
                result = AbilityCastResult.MissingTarget;
                return true;
            }
            // Preflight every possible target BEFORE spending 150/175 mana.
            // Unsupported defense data must not create a free partial salvo.
            foreach (var target in targets)
            {
                Ordr323ArmorType armor;
                if (!target.Enemy.Defenses.BaseArmor.HasValue ||
                    !Ordr323OriginalDamageRules.TryReadArmorType(
                        target.Enemy.Defenses.WarcraftArmorType, out armor))
                    return true;
            }
            ImportedCombatSkill accepted;
            result = actor.Abilities.TryCast(skill.Id, true, true, component => {
                foreach (var allowed in skill.Components)
                    if (Object.ReferenceEquals(component, allowed)) return true;
                return false;
            }, out accepted);
            if (result != AbilityCastResult.Cast) return true;
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), primary.Id, accepted.Name,
                "TOOLTIP_MODEL_ONLY 다단타 타격 횟수 적용 / 원본 타수 간격 미확정", 0));

            if (accepted.Id == "E-06:skill:0")
            {
                // The tooltip says two RANDOM distinct enemies per hit,
                // not a 700-radius circle or the same initial pair forever.
                // Draw from the session's shared script RNG, no new Random().
                for (int hit = 0; hit < 20; hit++)
                {
                    var available = new List<SpatialCombatEnemy>();
                    foreach (var target in targets)
                        if (target.Enemy.IsAlive && CanAttackInThisLane(actor, target))
                            available.Add(target);
                    for (int chosen = 0; chosen < 2 && available.Count > 0; chosen++)
                    {
                        int index = sourceModelRules.NextScriptRoll(available.Count);
                        var victim = available[index];
                        available.RemoveAt(index);
                        ApplySourceModelTooltipDamage(actor, victim, accepted.Name,
                            "난사 고정 타격 " + (hit + 1) + "/20",
                            300000 + victim.Enemy.MaxHealth * .10, true);
                        // Alabasta Gun Lv.1 exists before any upgrades.
                        // The tooltip's extra maximum-HP 0.5% is PER hit
                        // and PER level, not one grant for the entire barrage.
                        int level = 1 + (int)Math.Min(9,
                            otamaRewardWallet == null ? 0 :
                            otamaRewardWallet.Balance(
                                Original323SourceNativeActionKeys.ViviLevel(actor.Id)));
                        ApplySourceModelTooltipDamage(actor, victim, accepted.Name,
                            "알라바스타 건 Lv." + level,
                            victim.Enemy.MaxHealth * .005 * level, true);
                        if (victim.Kind == CharacterEnemyKind.Boss)
                            ApplySourceModelTooltipDamage(actor, victim, accepted.Name,
                                "보스 추가 마법", 500000, false);
                    }
                }
            }
            else
            {
                // Hancock's 750 range is CASTER-centered. The extra 16% is
                // explicitly SINGLE-target; do not grant it to every enemy.
                SpatialCombatEnemy single = targets[0];
                foreach (var target in targets)
                    if (Object.ReferenceEquals(primary, target)) single = primary;
                for (int hit = 0; hit < 25; hit++)
                {
                    if (hit == 0 || hit == 24)
                    {
                        foreach (var target in targets)
                        {
                            if (!target.Enemy.IsAlive ||
                                !CanAttackInThisLane(actor, target)) continue;
                            string source = "practice-tooltip-stun:" +
                                InstanceKey(actor.Id) + ":" + accepted.Id;
                            target.Enemy.Status.RemoveSource(source);
                            target.Enemy.Status.Apply(new CombatEffect(source,
                                CombatEffectType.Stun,
                                CombatEffectOrigin.TriggeredSkill, 1, 2.5));
                            events.Add(new CharacterCombatEvent(
                                CharacterCombatEventType.StatusApplied,
                                InstanceKey(actor.Id), target.Id,
                                accepted.Name, "TOOLTIP_MODEL_ONLY 첫/마지막 타격 2.5초 기절(저항무시)", 0));
                        }
                    }
                    foreach (var target in targets)
                    {
                        ApplySourceModelTooltipDamage(actor, target, accepted.Name,
                            "25회 물리", 360000, false);
                        ApplySourceModelTooltipDamage(actor, target, accepted.Name,
                            "25회 마법", 50000, false);
                    }
                    if (single.Enemy.IsAlive)
                        ApplySourceModelTooltipDamage(actor, single, accepted.Name,
                            "단일 대상 25회 추가 마법",
                            single.Kind == CharacterEnemyKind.Boss ||
                            single.Kind == CharacterEnemyKind.Story
                                ? 300000 : single.Enemy.MaxHealth * .16,
                            false);
                }
            }
            RemoveDefeatedEnemies();
            return true;
        }

        // Bso already owns Marco's 25-HP threshold, consumes the source
        // counter once and applies 525-range physical damage. Its previous
        // 40% component was incorrectly modeled as armor-affected Chaos,
        // and its seven auxiliary magic hits were entirely missing.
        // Run precisely once in the existing .20s positive-hit continuation.
        private void ApplySourceModelMarcoFeatherFollowups(
            CharacterInstance actor, SpatialCombatEnemy primary,
            long positiveBasicHit, double impactX, double impactY)
        {
            if (actor == null || actor.Catalog.Id != "L-11" ||
                actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != "h02T" ||
                actor.Profile == null || actor.Profile.UnitId != "L-11" ||
                actor.Profile.Skills.Count == 0) return;
            var skill = actor.Profile.Skills[0];
            if (skill.Id != "L-11:skill:0" || skill.Name != "깃털폭격" ||
                skill.Components.Count != 3 ||
                skill.Components[0].Number("multiplier") != 4.75 ||
                skill.Components[0].Number("plusFlatAmount") != 475000 ||
                skill.Components[1].Number("percent") != 40 ||
                skill.Components[2].Number("amount") != 400000 ||
                skill.Components[2].Number("maxExtraTargets") != 7) return;
            // Same positive hit operand as the already registered Bso AoE.
            double basePacket = 475000 + 4.75 * positiveBasicHit;
            if (primary.Enemy.IsAlive)
                ApplySourceModelTooltipDamage(actor, primary, skill.Name,
                    "단일 고정 피해 40%", basePacket * .4, true);
            var area = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.Circle, 525, 0, 0, 1, 1,
                Ordr323AttackType.Spells), impactX, impactY);
            int added = 0;
            foreach (var target in area)
            {
                if (target.Id == primary.Id || !target.Enemy.IsAlive) continue;
                ApplySourceModelTooltipDamage(actor, target, skill.Name,
                    "부가 7명 마법 피해", 400000, false);
                if (++added == 7) break;
            }
        }
    }
}
