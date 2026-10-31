using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // These twenty-six script callbacks run ONLY in the independent offline
        // practice model. The original verified/WC3 observer path is untouched.
        // Warcraft native dummy casts, exact defense semantics, original
        // enumeration order and final damage quantization remain unverified.
        private void OnSourceModelScriptAttackDeclared(
            string attackerId, string rawcode, string primaryTargetId)
        {
            // A08B has no w5 callback; its source object is an autonomous
            // Unholy Frenzy (Auhf). Cast targeting is a mobile policy, not
            // a fabricated JASS callback. See TrySourceModelLowFrenzy.
            if (rawcode == "h01H" && UsesSourceModelBasicAttacks && processingStep)
                TrySourceModelLowFrenzy(attackerId);
            SourceScriptAttackSpec script;
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                !processingStep || game.Phase != SessionPhase.Running ||
                (!SourceModelScriptAttackCatalog.TryGet(rawcode, out script) &&
                 !SourceModelNextAttackCatalog.TryGet(rawcode, out script)))
                return;

            long id;
            CharacterInstance actor;
            SpatialCombatEnemy primary;
            if (!long.TryParse(attackerId, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out id) ||
                !characters.TryGetValue(id, out actor) ||
                !actor.AttackReady || actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != rawcode ||
                !enemies.TryGetValue(primaryTargetId, out primary) ||
                !primary.Enemy.IsAlive || !CanAttackInThisLane(actor, primary))
                return;

            // JASS Btd (Cracker) uses Bcb every attacked event, THEN rolls
            // its separate e04X/area branch. Do not reverse this ordering.
            ApplySourceScriptHits(actor, primary, script, script.Always);
            int roll = sourceModelRules.NextScriptRoll(script.RollSides) + 1;
            if (roll > script.RollSuccess) return;
            string skillName = actor.Catalog.Name + " 공격 기술";
            if (script.CatalogId == "U-04" || script.CatalogId == "U-13")
                skillName = "람각";
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, attackerId,
                primaryTargetId, skillName,
                "MODEL_ONLY 원본 " + script.JassFunction + " 공격 선언 분기 " +
                roll + "/" + script.RollSides,
                0));
            BeforeSourceModelLegacyProc(actor, primary, script);
            if (script.DelaySeconds > 0)
            {
                // BoB captures the attacked unit's LOCATION at state 0. The
                // AoE still happens there if that primary dies before .29s.
                // Use a synthetic target key so the normal target-death
                // cancellation cannot erase a location-based JASS effect.
                double pinnedX = primary.X, pinnedY = primary.Y;
                effectExecutor.SchedulePracticeSourceAttack(attackerId,
                    "practice-slam:" + attackerId, script.DelaySeconds, () => {
                        if (characters.ContainsKey(actor.Id))
                            ApplySourceScriptHits(actor, primary, script, script.OnProc,
                                pinnedX, pinnedY);
                    });
            }
            else
                ApplySourceScriptHits(actor, primary, script, script.OnProc);

            if (script.CatalogId == "S-12")
                ScheduleSourceModelPhoenixFire(actor, primary, script);
            AfterSourceModelLegacyProc(actor, primary, script);
            if (!string.IsNullOrWhiteSpace(script.DeferredNativeEffect))
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.EffectDeferred, attackerId,
                    primaryTargetId, skillName,
                    "원본 네이티브 추가 효과 미검증: " + script.DeferredNativeEffect,
                    0));
        }

        private List<SpatialCombatEnemy> SourceScriptTargets(
            CharacterInstance actor, SpatialCombatEnemy primary, SourceScriptHit hit,
            double? pinnedX = null, double? pinnedY = null)
        {
            var found = new List<SpatialCombatEnemy>();
            if (hit.Shape == SourceScriptHitShape.Direct)
            {
                if (primary.Enemy.IsAlive) found.Add(primary);
                return found;
            }
            var position = roster.Get(actor.Id);
            double dx = primary.X - position.X;
            double dy = primary.Y - position.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            foreach (var candidate in enemies.Values)
            {
                if (!candidate.Enemy.IsAlive ||
                    !CanAttackInThisLane(actor, candidate)) continue;
                if (hit.Shape == SourceScriptHitShape.Circle ||
                    hit.Shape == SourceScriptHitShape.AttackerCircle)
                {
                    double cx = hit.Shape == SourceScriptHitShape.AttackerCircle
                        ? position.X : (pinnedX ?? primary.X);
                    double cy = hit.Shape == SourceScriptHitShape.AttackerCircle
                        ? position.Y : (pinnedY ?? primary.Y);
                    double x = candidate.X - cx;
                    double y = candidate.Y - cy;
                    if (x * x + y * y <= hit.Radius * hit.Radius)
                        found.Add(candidate);
                }
                else if ((hit.Shape == SourceScriptHitShape.RankyakuLine ||
                          hit.Shape == SourceScriptHitShape.ProjectileLine) && length > 0)
                {
                    // AUcs/A0VS geometry from W3A. Current target defines
                    // the MOBILE automatic direction; the original native
                    // PointAtAttackerLocation trajectory is still unverified.
                    double x = candidate.X - position.X;
                    double y = candidate.Y - position.Y;
                    double forward = (x * dx + y * dy) / length;
                    double side = Math.Abs(x * dy - y * dx) / length;
                    if (forward >= 0 && forward <= hit.Length &&
                        side <= hit.Radius)
                        found.Add(candidate);
                }
            }
            // Stable model enumeration only. Warcraft native enumeration
            // order (and therefore its BFh draw order) is NOT certified.
            found.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            return found;
        }

        private void ScheduleSourceModelPhoenixFire(CharacterInstance actor,
            SpatialCombatEnemy primary, SourceScriptAttackSpec script)
        {
            // Source: Bn4 creates h0C5 with A08O/Apxf at the attacker for
            // 1.2s. A08O has 250 damage, .2s cooldown, 800 acquisition range.
            // First fire and retargeting are not JASS-observed, hence this is
            // a deliberately labeled mobile result approximation. Five full
            // .2s intervals before expiry; no imaginary permanent summon.
            var spawn = roster.Get(actor.Id);
            string attackerId = InstanceKey(actor.Id);
            for (int tick = 1; tick <= 5; tick++)
            {
                effectExecutor.SchedulePracticeSourceAttack(attackerId,
                    "practice-phoenix:" + attackerId + ":" + tick, tick * .2, () => {
                        if (!characters.ContainsKey(actor.Id)) return;
                        var closest = NearestSourceModelTarget(actor, spawn.X, spawn.Y, 800);
                        if (closest == null) return;
                        ApplySourceScriptHits(actor, closest, script, new[] {
                            new SourceScriptHit(SourceScriptHitShape.Direct, 0, 0,
                                250, 1, 1, Ordr323AttackType.Spells) });
                    });
            }
        }

        private SpatialCombatEnemy NearestSourceModelTarget(CharacterInstance actor,
            double x, double y, double radius)
        {
            SpatialCombatEnemy closest = null;
            double nearest = radius * radius;
            foreach (var candidate in enemies.Values)
            {
                if (!candidate.Enemy.IsAlive ||
                    !CanAttackInThisLane(actor, candidate)) continue;
                double dx = candidate.X - x, dy = candidate.Y - y;
                double squared = dx * dx + dy * dy;
                if (squared > nearest || (squared == nearest && closest != null &&
                    StringComparer.Ordinal.Compare(candidate.Id, closest.Id) >= 0)) continue;
                closest = candidate;
                nearest = squared;
            }
            return closest;
        }

        private void ApplySourceScriptHits(CharacterInstance actor,
            SpatialCombatEnemy primary, SourceScriptAttackSpec script,
            IReadOnlyList<SourceScriptHit> hits,
            double? pinnedX = null, double? pinnedY = null)
        {
            string attackerId = InstanceKey(actor.Id);
            foreach (var hit in hits)
            {
                var targets = SourceScriptTargets(actor, primary, hit, pinnedX, pinnedY);
                foreach (var target in targets)
                {
                    Ordr323ArmorType armor;
                    if (!target.Enemy.Defenses.BaseArmor.HasValue ||
                        !Ordr323OriginalDamageRules.TryReadArmorType(
                            target.Enemy.Defenses.WarcraftArmorType, out armor))
                    {
                        events.Add(new CharacterCombatEvent(
                            CharacterCombatEventType.EffectDeferred,
                            attackerId, target.Id, actor.Catalog.Name + " 공격 기술",
                            "MODEL_ONLY 대상 방어 자료 부족: 피해 보류", 0));
                        continue;
                    }
                    double uniform = hit.Minimum == hit.Maximum ? 0 :
                        sourceModelRules.NextScriptRoll(1000000) / 1000000.0;
                    double sourceDamage = Ordr323MapDamageMath.ScriptDamageBeforeDefense(
                        hit.Damage, hit.Minimum, hit.Maximum, uniform);
                    // Consistent with the existing practice basic-damage
                    // reference model. Damage type / armor bypass / native
                    // immunity still need original Warcraft comparison.
                    double modeled = Ordr323MapDamageMath.ApplyVerifiedFactors(
                        sourceDamage, hit.Attack, armor, true, true,
                        target.Enemy.Defenses.BaseArmor.Value -
                        SourceModelUnitEffects.ResolvePracticeArmorReduction(
                            target.Enemy.Status.Snapshot()), false, 0, 1);
                    if (modeled >= long.MaxValue)
                        throw new OverflowException("모델 공격 스킬 피해 범위 초과");
                    long planned = (long)Math.Floor(modeled);
                    long actual;
                    if (!resolution.TryApplySkillDamage(attackerId,
                            target.Id, planned, out actual)) continue;
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.SkillDamage, attackerId,
                        target.Id, actor.Catalog.Name + " 공격 기술",
                        "MODEL_ONLY 원본 " + script.JassFunction +
                        (script.CatalogId == "S-08" || script.CatalogId == "S-12" ||
                         script.CatalogId == "S-18" || script.CatalogId == "S-19"
                            ? " 네이티브 객체 수치 기반 모바일 근사, 실제 타격 미검증"
                            : " JASS 기본 피해·범위, 최종 엔진 면역 미검증"),
                        actual));
                    if (!target.Enemy.IsAlive)
                        events.Add(new CharacterCombatEvent(
                            CharacterCombatEventType.EnemyDefeated,
                            attackerId, target.Id, actor.Catalog.Name + " 공격 기술",
                            "연습 공격 선언 스킬로 적 처치", actual));
                }
            }
        }
    }
}
