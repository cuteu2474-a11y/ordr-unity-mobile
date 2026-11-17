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
                "원본 JASS 검증된 20/25타 지연 타이밍 사용 · 네이티브 이펙트/면역 미검증", 0));

            // JASS B5s: .10 morph + .02 prep, 20 shots every .055s.
            // JASS B5F: .19 slam + .04 prep, 25 shots every .05s.
            // These must NOT run as 20/25 instantaneous for-loop attacks.
            bool storyLane = IsStoryDeployed(actor.Id);
            long revision = SourceModelLaneRevision(actor.Id);
            string owner = InstanceKey(actor.Id);
            int count = accepted.Id == "E-06:skill:0" ? 20 : 25;
            double startAt = count == 20 ? .12 : .23;
            double interval = count == 20 ? .055 : .05;
            string originalPrimaryId = primary.Id;
            if (sourceNextVolleyCastId == long.MaxValue)
                throw new OverflowException("원본 연타 시전 순번을 더 늘릴 수 없습니다.");
            long castId = ++sourceNextVolleyCastId;
            for (int hit = 0; hit < count; hit++)
            {
                var pulse = new SourceVolleyPulseCheckpoint {
                    OwnerId = actor.Id, SkillId = accepted.Id,
                    OriginalPrimaryId = originalPrimaryId,
                    StoryLane = storyLane, LaneRevision = revision,
                    CastId = castId, CastAt = effectExecutor.Time,
                    HitIndex = hit
                };
                effectExecutor.ScheduleRestorableSourceAttack(owner,
                    SourceVolleyPulseTarget(actor.Id, castId, hit),
                    startAt + hit * interval,
                    SourceVolleyPulseKind, EncodeSourceVolleyPulse(pulse),
                    CreateSourceVolleyPulse(pulse, actor, accepted));
            }
            RemoveDefeatedEnemies();
            return true;
        }

        // Original war3map.j B5s 33765-33859. Big() samples a random
        // enemy from the live 925 range separately for BOTH draws; the same
        // enemy may be selected twice. Warcraft-native random displacement
        // of the caster itself is intentionally not recreated in the fixed
        // left-side mobile formation.
        private void ApplySourceOriginalViviSalvoPulse(
            CharacterInstance actor, ImportedCombatSkill skill, int hit)
        {
            var pos = roster.Get(actor.Id);
            var eligible = new List<SpatialCombatEnemy>();
            foreach (var enemy in enemies.Values)
            {
                if (!enemy.Enemy.IsAlive || !CanAttackInThisLane(actor, enemy))
                    continue;
                double dx = enemy.X - pos.X, dy = enemy.Y - pos.Y;
                if (dx * dx + dy * dy <= 925 * 925) eligible.Add(enemy);
            }
            eligible.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            int level = 1 + (int)Math.Min(9,
                otamaRewardWallet == null ? 0 :
                otamaRewardWallet.Balance(
                    Original323SourceNativeActionKeys.ViviLevel(actor.Id)));
            for (int pick = 0; pick < 2 && eligible.Count != 0; pick++)
            {
                var target = eligible[sourceModelRules.NextScriptRoll(eligible.Count)];
                if (!target.Enemy.IsAlive) continue;
                if (target.Kind == CharacterEnemyKind.Line)
                {
                    // B5s source: (300000 + maxHP*0.08) * (1+I4*0.05),
                    // ATTACK_TYPE_CHAOS / DAMAGE_TYPE_UNIVERSAL.
                    // This replaces the older unverified 10%+0.5% additive
                    // formula, rather than paying the HP component twice.
                    double raw = (300000 + target.Enemy.MaxHealth * .08) *
                        (1 + level * .05);
                    ApplySourceModelTooltipDamage(actor, target, skill.Name,
                        "JASS B5s 난사 " + (hit + 1) + "/20",
                        raw, true, Ordr323AttackType.Chaos);
                }
                else
                    ApplySourceModelTooltipDamage(actor, target, skill.Name,
                        "JASS B5s 특수 적 마법 " + (hit + 1) + "/20",
                        500000, false, Ordr323AttackType.Spells);
            }
        }

        // B5F 32971-33071: the caster-centered 750-radius packets occur
        // 25 times, first at .23s and every .05s thereafter. Its 360k
        // physical AoE uses ATTACK_TYPE_CHAOS/DAMAGE_TYPE_NORMAL, whereas
        // 50k and the single-target bonus use ATTACK_TYPE_NORMAL/UNIVERSAL.
        private void ApplySourceOriginalHancockPulse(CharacterInstance actor,
            ImportedCombatSkill skill, string primaryId, int hit)
        {
            var pos = roster.Get(actor.Id);
            var targets = new List<SpatialCombatEnemy>();
            foreach (var enemy in enemies.Values)
            {
                if (!enemy.Enemy.IsAlive || !CanAttackInThisLane(actor, enemy))
                    continue;
                double dx = enemy.X - pos.X, dy = enemy.Y - pos.Y;
                if (dx * dx + dy * dy <= 750 * 750) targets.Add(enemy);
            }
            targets.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            SpatialCombatEnemy primary;
            if (!enemies.TryGetValue(primaryId, out primary) ||
                !primary.Enemy.IsAlive || !targets.Contains(primary))
                primary = targets.Count > 0 ? targets[0] : null;
            if (hit == 0 || hit == 24)
                foreach (var target in targets)
                {
                    string source = "practice-tooltip-stun:" +
                        InstanceKey(actor.Id) + ":" + skill.Id;
                    target.Enemy.Status.RemoveSource(source);
                    target.Enemy.Status.Apply(new CombatEffect(source,
                        CombatEffectType.Stun,
                        CombatEffectOrigin.TriggeredSkill, 1, 2.5));
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.StatusApplied,
                        InstanceKey(actor.Id), target.Id, skill.Name,
                        "도감 첫/마지막 타격 2.5초 기절(원본 네이티브 면역 미검증)", 0));
                }
            foreach (var target in targets)
            {
                ApplySourceModelTooltipDamage(actor, target, skill.Name,
                    "JASS B5F 물리 25타", 360000, false,
                    Ordr323AttackType.Chaos);
                ApplySourceModelTooltipDamage(actor, target, skill.Name,
                    "JASS B5F 마법 25타", 50000, false,
                    Ordr323AttackType.Spells);
            }
            if (primary != null && primary.Enemy.IsAlive)
                ApplySourceModelTooltipDamage(actor, primary, skill.Name,
                    "JASS B5F 단일 후속", primary.Kind == CharacterEnemyKind.Line
                        ? primary.Enemy.MaxHealth * .16 : 300000,
                    false, Ordr323AttackType.Spells);
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
