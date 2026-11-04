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
            SourceScriptAttackSpec script = null;
            SourceModelBatchAttackSpec batch = null;
            SourceModelNextThirtyUnit nextThirty = null;
            SourceModelUnconnectedSkillRow unconnected = null;
            string sourceSpecialUnitId;
            string nativeSourceUnitId;
            bool specialFromJass =
                SourceModelOriginalSpecialCallbackRules.TryGetUnit(
                    rawcode, out sourceSpecialUnitId);
            bool nativeFromJass = SourceModelNativeCharacterRules.TryGetNativeAttack(
                rawcode, out nativeSourceUnitId);
            bool practiceBonney = rawcode == "H0BX" || rawcode == "H0BY";
            bool eleventhFormOwner = SourceModelEleventhFormHasAttack(rawcode);
            bool standaloneTemporary = IsStandaloneSourceTemporaryAttack(rawcode);
            bool ninthOwner = SourceModelNinthSkillCatalog.HasOwner(rawcode);
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                !processingStep || game.Phase != SessionPhase.Running ||
                (!practiceBonney && !eleventhFormOwner &&
                 !SourceModelScriptAttackCatalog.TryGet(rawcode, out script) &&
                 !SourceModelNextAttackCatalog.TryGet(rawcode, out script) &&
                 !SourceModelThirdAttackCatalog.TryGet(rawcode, out script) &&
                 !SourceModelFourthAttackCatalog.TryGet(rawcode, out script) &&
                 !SourceModelFifthAttackCatalog.TryGet(rawcode, out script) &&
                 !SourceModelBatchAttackCatalog.TryGet(rawcode, out batch) &&
                 !SourceModelNextThirtySkillCatalog.TryGet(rawcode, out nextThirty) &&
                 !SourceModelUnconnectedSkillCatalog.TryGet(rawcode, out unconnected) &&
                 !ninthOwner &&
                 !standaloneTemporary &&
                 !specialFromJass &&
                 !nativeFromJass))
                return;

            long id;
            CharacterInstance actor;
            SpatialCombatEnemy primary;
            if (!long.TryParse(attackerId, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out id) ||
                !characters.TryGetValue(id, out actor) ||
                !actor.AttackReady || actor.Mapping == null ||
                (actor.Mapping.AttackSpec.UnitRawCode != rawcode &&
                 sourceModelRules.SourceFormRawcodeOf(attackerId) != rawcode) ||
                !enemies.TryGetValue(primaryTargetId, out primary) ||
                !primary.Enemy.IsAlive || !CanAttackInThisLane(actor, primary))
                return;

            // B8V is an ATTACK-DECLARED self-buff, not a successful-hit
            // proc. It must run even when Tezoro's 12% spell does not roll.
            ApplySourceModelFusionTesoroFrostArmor(actor, rawcode);

            if (ninthOwner)
                ExecuteSourceModelNinthDeclaredAttack(actor, primary, rawcode);
            ExecuteSourceModelTenthFormDeclaredAttack(actor, primary, rawcode);

            // Ulti's tooltip combines her HP-cost active with +1 HP on each
            // declared basic attack. Only source-managed practice HP is
            // restored; externally supplied verified resource pools are not.
            if (actor.Catalog.Id == "L-25" && rawcode == "h03S" &&
                actor.Profile != null &&
                sourceModelManagedResourceRows.ContainsKey(actor.Id) &&
                actor.Abilities.Resources != null)
            {
                foreach (var ability in actor.Profile.Skills)
                    if (ability.Name == "미티어 울두건" &&
                        SourceModelNextThirtySkillCatalog.IsFirstCompositeActive(ability))
                    {
                        double restored = ability.Number("healthRegenPerAttack") ?? 0;
                        if (restored > 0)
                        {
                            var hp = actor.Abilities.Resources;
                            hp.SetHealth(Math.Min(hp.MaximumHealth, hp.Health + restored));
                        }
                        break;
                    }
            }

            RestoreSourceModelTwentyFourthAttackResources(actor, rawcode);

            if (specialFromJass)
            {
                if (actor.Catalog.Id == sourceSpecialUnitId)
                    ExecuteSourceModelOriginalSpecial(actor, primary);
                return;
            }
            if (nativeFromJass)
            {
                if (actor.Catalog.Id == nativeSourceUnitId)
                    ExecuteNativeSourceCharacterAttack(actor, primary);
                return;
            }

            if (standaloneTemporary)
            {
                ExecuteSourceTemporaryAttack(actor, primary);
                // TR-14's form proc and I-03's clone pulse coexist with
                // independently approved on-hit packets. The old early return
                // silently discarded those sixth-batch attacks on every hit.
                if (nextThirty != null && nextThirty.CatalogId == actor.Catalog.Id)
                    ExecuteSourceModelNextThirtyProfileAttacks(actor, primary, nextThirty);
                if (unconnected != null && unconnected.CatalogId == actor.Catalog.Id)
                    ExecuteSourceModelUnconnectedProfileAttacks(actor, primary, unconnected);
                return;
            }

            if (practiceBonney)
            {
                if (actor.Catalog.Id == "TR-21" && rawcode == "H0BX")
                    ApplySourceModelBonneyDeclaredArmor(actor, primary);
                return;
            }

            if (batch != null)
            {
                ExecuteSourceModelBatchAttack(actor, primary, batch);
                return;
            }

            if (nextThirty != null)
            {
                if (nextThirty.CatalogId == actor.Catalog.Id)
                    ExecuteSourceModelNextThirtyProfileAttacks(actor, primary, nextThirty);
                return;
            }

            if (unconnected != null)
            {
                if (unconnected.CatalogId == actor.Catalog.Id)
                    ExecuteSourceModelUnconnectedProfileAttacks(actor, primary, unconnected);
                return;
            }

            // JASS Btd (Cracker) uses Bcb every attacked event, THEN rolls
            // its separate e04X/area branch. Do not reverse this ordering.
            ApplySourceScriptHits(actor, primary, script, script.Always);
            int roll = sourceModelRules.NextScriptRoll(script.RollSides) + 1;
            // Independent clone proc, after the already-registered Bt2 roll:
            // retaining that order avoids changing its historical RNG draw.
            if (rawcode == "h042") ExecuteSourceTimedClone(actor, primary);
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
                bool sourceStoryLane = IsStoryDeployed(actor.Id);
                long sourceLaneRevision = SourceModelLaneRevision(actor.Id);
                effectExecutor.SchedulePracticeSourceAttack(attackerId,
                    "practice-slam:" + attackerId, script.DelaySeconds, () => {
                        if (CanFinishSourceModelDelayedAttack(actor.Id,
                                sourceStoryLane, sourceLaneRevision))
                            ApplySourceScriptHits(actor, primary, script, script.OnProc,
                                pinnedX, pinnedY);
                    });
            }
            else
                ApplySourceScriptHits(actor, primary, script, script.OnProc);

            // RN-04 already resolves CCg's 17%/1.5m damage and delayed
            // primary-target Faerie Fire in the JASS-backed callback. Only
            // attach the tooltip's 18% area magic amplification to that same
            // successful roll; never roll or deal the attack damage twice.
            if (script.CatalogId == "RN-04")
                ApplySourceModelSentinelMagicAmplification(actor, primary, script);

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
                SpatialCombatEnemy current;
                if (primary.Enemy.IsAlive &&
                    enemies.TryGetValue(primary.Id, out current) &&
                    ReferenceEquals(current, primary) &&
                    CanAttackInThisLane(actor, primary)) found.Add(primary);
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
            bool sourceStoryLane = IsStoryDeployed(actor.Id);
            long sourceLaneRevision = SourceModelLaneRevision(actor.Id);
            for (int tick = 1; tick <= 5; tick++)
            {
                effectExecutor.SchedulePracticeSourceAttack(attackerId,
                    "practice-phoenix:" + attackerId + ":" + tick, tick * .2, () => {
                        if (!CanFinishSourceModelDelayedAttack(actor.Id,
                                sourceStoryLane, sourceLaneRevision)) return;
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
                        "MODEL_ONLY " + script.JassFunction +
                        (script.JassFunction == "tooltip-clone-proxy"
                            ? " 도감 분신 파동 / 원본 연속 공격·네이티브 동등성 미검증"
                            : (script.CatalogId == "S-08" || script.CatalogId == "S-12" ||
                         script.CatalogId == "S-18" || script.CatalogId == "S-19"
                            ? " 네이티브 객체 수치 기반 모바일 근사, 실제 타격 미검증"
                            : " JASS 기본 피해·범위, 최종 엔진 면역 미검증")),
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
