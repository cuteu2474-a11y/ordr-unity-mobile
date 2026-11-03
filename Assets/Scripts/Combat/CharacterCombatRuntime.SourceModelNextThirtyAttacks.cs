using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

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
            // No native on-attack callback is implied by an active-only
            // skill row (e.g. Big Mom). Do not fabricate deferred events.
            if (row.PracticeAttackSkills.Count == 0) return;
            ExecuteSourceModelApprovedProfileAttacks(actor, primary,
                row.CatalogId, row.PracticeAttackSkills);
        }

        private void ExecuteSourceModelUnconnectedProfileAttacks(
            CharacterInstance actor, SpatialCombatEnemy primary,
            SourceModelUnconnectedSkillRow row)
        {
            // Active-only rows must not fire phantom attack procs or spam
            // deferred skill warnings on every normal attack.
            if (row.PracticeAttackSkills.Count == 0) return;
            ExecuteSourceModelApprovedProfileAttacks(actor, primary,
                row.CatalogId, row.PracticeAttackSkills);
        }

        private void ExecuteSourceModelApprovedProfileAttacks(
            CharacterInstance actor, SpatialCombatEnemy primary, string catalogId,
            IReadOnlyList<string> approvedNames)
        {
            string attackerId = InstanceKey(actor.Id);
            if (actor.Profile == null || actor.Profile.UnitId != catalogId)
            {
                ReportSourceModelNextThirtyDeferred(attackerId, primary.Id,
                    "스킬 프로필 또는 유닛 ID 불일치");
                return;
            }

            int completedModels = 0;
            var activatedThisAttack = new HashSet<string>(StringComparer.Ordinal);
            foreach (string approvedName in approvedNames)
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
                if (SourceModelTwentiethSkillCatalog.IsSpecial(skill) &&
                    !SourceModelTwentiethCanActivate(actor, skill)) continue;
                if (!SourceModelEighthAttackCondition(actor, skill)) continue;
                if (!SourceModelSixteenthAttackCondition(actor, skill)) continue;
                if (!SourceModelNineteenthReady(actor, skill)) continue;
                // E-07's 4.25 s absolute cooldown is attached to the actual
                // successful proc; a failed roll neither consumes nor renews it.
                double readyAt;
                if (skill.Id == "E-07:skill:3" &&
                    sourceModelFireFistReadyAt.TryGetValue(actor.Id, out readyAt) &&
                    readyAt > Time) continue;
                if (skill.Id == "TR-15:skill:1" &&
                    sourceModelPoseidonReadyAt.TryGetValue(actor.Id, out readyAt) &&
                    readyAt > Time) continue;
                if (skill.Id == "TR-11:skill:4" &&
                    sourceModelSeventhReadyAt.TryGetValue(actor.Id, out readyAt) &&
                    readyAt > Time) continue;
                if (skill.Trigger == "onAttackWithMana" &&
                    (actor.Abilities.Resources == null ||
                     actor.Abilities.Resources.Mana < skill.Number("requiredMana").Value ||
                     !actor.Abilities.Resources.CanSpend(0, skill.Number("manaCost").Value)))
                    continue;
                if (skill.Id == "TR-16:skill:0" &&
                    (actor.Abilities.Resources == null ||
                     actor.Abilities.Resources.Mana < skill.Number("manaAtLeast").Value ||
                     !actor.Abilities.Resources.CanSpend(0, skill.Number("manaCost").Value)))
                    continue;
                if ((skill.Trigger == "onAttack" ||
                     skill.Trigger == "onAttackWithMana") &&
                    sourceModelRules.NextScriptRoll(1000000) >=
                    (int)Math.Round(SourceModelNineteenthChance(skill, primary,
                        SourceModelSixteenthProcChance(skill, primary,
                            SourceModelFifteenthProcChance(skill, primary,
                                SourceModelEighthProcChance(skill, primary, actor)))) * 10000,
                        MidpointRounding.AwayFromZero)) continue;
                // Preserve the tooltip target metadata, but apply the same
                // explicit MOBILE result policy as CharacterSkillEffectExecutor.
                // Explicit casterArea mobile exceptions follow the chosen
                // enemy at unlimited reach (Redfield and Rebecca).
                var shape = skill.Target == "single" ||
                    skill.Target == "eachMultishotTarget" ||
                    skill.Target == "summon" ||
                    (skill.Id == "E-03:skill:5" && skill.Target == null) ?
                    SourceScriptHitShape.Direct :
                    (skill.Id == "TR-26:skill:4" ||
                     SourceModelEighteenthSkillCatalog.IsSpecial(skill)) &&
                    skill.Target == "line" ?
                    SourceScriptHitShape.ProjectileLine :
                    skill.Target == "casterArea" &&
                    !SpecialGradeMobilePolicy.UsesEnemyCenteredArea(catalogId) &&
                    skill.Id != "TR-13:skill:3" &&
                    skill.Id != "I-05:skill:1" ?
                    SourceScriptHitShape.AttackerCircle : SourceScriptHitShape.Circle;
                double lineLength = 0;
                if (shape == SourceScriptHitShape.ProjectileLine)
                {
                    var position = roster.Get(actor.Id);
                    double dx = primary.X - position.X;
                    double dy = primary.Y - position.Y;
                    lineLength = Math.Sqrt(dx * dx + dy * dy) + skill.Radius.Value;
                }
                var template = new SourceScriptHit(shape, skill.Radius ?? 0,
                    lineLength, 0, 1, 1, Ordr323AttackType.Spells);
                var targets = skill.Id == "TR-07:skill:1" ?
                    SourceModelRankyakuTargets(actor, primary, skill.Radius.Value) :
                    skill.Id == "TR-04:skill:3" ?
                    SourceModelRobinMultishotTargets(actor, primary) :
                    SourceScriptTargets(actor, primary, template);
                if (targets.Count == 0) continue;
                SourceModelEighthCommitAttackCondition(actor, skill);
                SourceModelNineteenthCommit(actor, skill);
                if (skill.Trigger == "onAttackWithMana")
                    actor.Abilities.Resources.Spend(0, skill.Number("manaCost").Value);
                if (skill.Id == "TR-16:skill:0")
                    actor.Abilities.Resources.Spend(0, skill.Number("manaCost").Value);
                if (skill.Id == "E-07:skill:3")
                    sourceModelFireFistReadyAt[actor.Id] = Time +
                        skill.Number("absoluteCooldownSeconds").Value;
                if (skill.Id == "TR-15:skill:1")
                    sourceModelPoseidonReadyAt[actor.Id] = Time +
                        skill.Number("absoluteCooldownSeconds").Value;
                if (skill.Id == "TR-11:skill:4")
                    sourceModelSeventhReadyAt[actor.Id] = Time +
                        skill.Number("intervalSeconds").Value;
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated, attackerId,
                    primary.Id, skill.Name,
                    "TOOLTIP_MODEL_ONLY " + catalogId +
                    " 공격 선언 / 결과·시점·난수 원본 미검증", 0));
                if (SourceModelTwentiethSkillCatalog.IsSpecial(skill))
                    ApplySourceModelTwentiethSkill(actor, primary, skill, targets);
                else if (skill.Id == "TR-16:skill:0")
                    ScheduleSourceModelIceAge(actor, primary, skill);
                else
                    ApplySourceModelNextThirtySkill(actor, primary, skill, targets,
                        activatedThisAttack);
                activatedThisAttack.Add(skill.Name);
            }
            // ItemUpgrade is a separately observed passive level, never an
            // independent basic-attack proc or a deferred combat hit.
            if (catalogId == "I-05")
                ApplySourceModelBulletRokushikiAttack(actor, primary);
            int nonAttackAbilities = 0;
            foreach (var registered in actor.Profile.Skills)
                if ((registered.Trigger == "itemUpgrade" ||
                     registered.Trigger == "activeMana" ||
                     registered.Trigger == "activeHealth") &&
                    SourceModelTwentiethSkillCatalog.CanModelExact(registered))
                    nonAttackAbilities++;
            if (completedModels != actor.Profile.Skills.Count - nonAttackAbilities)
                ReportSourceModelNextThirtyDeferred(attackerId, primary.Id,
                    catalogId + " 도감 " + actor.Profile.Skills.Count +
                    "개 중 안전한 연습 타격 " + completedModels +
                    "개만 공격 선언에서 연결; 나머지는 이 경로에서 실행하지 않음");
        }

        // Reuse the SAME resource/cooldown ledger as verified active skills.
        // Never debit mana/health for a partial tooltip packet; never invent
        // resources for units lacking a source-backed health/mana provider.
        private bool TryCastSourceModelNextThirtyActive(CharacterInstance caster,
            string skillId, string primaryEnemyId, out AbilityCastResult result)
        {
            result = AbilityCastResult.UnsupportedEffect;
            SourceModelNextThirtyUnit row;
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                caster.Mapping == null || caster.Profile == null ||
                !SourceModelNextThirtySkillCatalog.TryGet(
                    caster.Mapping.AttackSpec.UnitRawCode, out row) ||
                row.CatalogId != caster.Catalog.Id) return false;
            return TryCastApprovedSourceModelActive(caster, skillId, primaryEnemyId,
                row.PracticeActiveSkills, out result);
        }

        private bool TryCastSourceModelUnconnectedActive(CharacterInstance caster,
            string skillId, string primaryEnemyId, out AbilityCastResult result)
        {
            result = AbilityCastResult.UnsupportedEffect;
            if (caster != null && caster.Catalog.Id == "H-15" &&
                skillId == "H-15:skill:0" &&
                !SourceModelSunnyHasVerifiedKnowledge(caster.Id))
            {
                result = AbilityCastResult.MissingOrUnsupportedRequirement;
                return true;
            }
            SourceModelUnconnectedSkillRow row;
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                caster.Mapping == null || caster.Profile == null ||
                !SourceModelUnconnectedSkillCatalog.TryGet(
                    caster.Mapping.AttackSpec.UnitRawCode, out row) ||
                row.CatalogId != caster.Catalog.Id) return false;
            return TryCastApprovedSourceModelActive(caster, skillId, primaryEnemyId,
                row.PracticeActiveSkills, out result);
        }

        private bool TryCastApprovedSourceModelActive(CharacterInstance caster,
            string skillId, string primaryEnemyId,
            IReadOnlyList<string> approvedNames, out AbilityCastResult result)
        {
            result = AbilityCastResult.UnsupportedEffect;
            ImportedCombatSkill selected = null;
            foreach (var candidate in caster.Profile.Skills)
                if (candidate.Id == skillId) { selected = candidate; break; }
            if (selected == null) return false;
            // Avoid generic fallback debit for any still-unapproved
            // resource ability on a known model character.
            if (caster.Profile.UnitId != caster.Catalog.Id ||
                !SourceModelNextThirtySkillCatalog.CanCastApprovedActive(
                    caster.Catalog.Id, approvedNames, selected))
                return selected.Trigger == "activeMana" ||
                    selected.Trigger == "activeHealth";
            if (SourceModelTwentiethSkillCatalog.IsSpecial(selected) &&
                !SourceModelTwentiethCanActivate(caster, selected))
            {
                result = AbilityCastResult.MissingOrUnsupportedRequirement;
                return true;
            }
            SpatialCombatEnemy primary;
            if (string.IsNullOrWhiteSpace(primaryEnemyId) ||
                !enemies.TryGetValue(primaryEnemyId, out primary) ||
                !primary.Enemy.IsAlive || !CanAttackInThisLane(caster, primary))
            {
                result = AbilityCastResult.MissingTarget;
                return true;
            }
            var shape = selected.Target == "single" ?
                SourceScriptHitShape.Direct : SourceScriptHitShape.Circle;
            var targets = SourceScriptTargets(caster, primary,
                new SourceScriptHit(shape, selected.Radius ?? 0,
                    0, 0, 1, 1, Ordr323AttackType.Spells));
            if (targets.Count == 0)
            {
                result = AbilityCastResult.MissingTarget;
                return true;
            }
            foreach (var component in selected.Components)
            {
                // Status effects and armor-break modify the target's state,
                // they do not require a damage armor type to debit resources.
                if (component.Type == "fixed" || component.Type == "stun" ||
                    component.Type == "armorBreak" ||
                    component.Type == "selfHeal" ||
                    component.Type == "allyAttackPowerBuff" ||
                    CommonSkillStatusEffects.IsTimedStatus(component.Type)) continue;
                foreach (var target in targets)
                {
                    if (!SourceModelNextThirtyComponentTargets(component, primary, target))
                        continue;
                    Ordr323ArmorType armor = default(Ordr323ArmorType);
                    if (!target.Enemy.Defenses.BaseArmor.HasValue ||
                        !Ordr323OriginalDamageRules.TryReadArmorType(
                            target.Enemy.Defenses.WarcraftArmorType, out armor))
                        return true; // Never debit without target defenses.
                }
            }
            ImportedCombatSkill accepted;
            // Only this path's FULL source-approved packet may enter the
            // multi-hit practice executor. The normal manual/unknown cast
            // path still rejects unsupported time-based packets. "true"
            // permits documented hit counts, not original Warcraft timing.
            result = caster.Abilities.TryCast(skillId, true, true, out accepted);
            if (result != AbilityCastResult.Cast) return true;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(caster.Id), primary.Id, accepted.Name,
                "TOOLTIP_MODEL_ONLY / 원본 타격·면역 미검증", 0));
            if (SourceModelTwentiethSkillCatalog.IsSpecial(accepted))
                ApplySourceModelTwentiethSkill(caster, primary, accepted, targets);
            else if (accepted.Id == "E-07:skill:0")
                SourceModelAceDelayedUltimate(caster, primary, accepted, targets);
            else
                ApplySourceModelNextThirtySkill(caster, primary, accepted, targets);
            RemoveDefeatedEnemies();
            return true;
        }

        private static bool SourceModelNextThirtyComponentTargets(
            ImportedCombatEffect component, SpatialCombatEnemy primary,
            SpatialCombatEnemy target)
        {
            if (component.Text("scope") == "singlePrimaryTarget" &&
                !ReferenceEquals(primary, target)) return false;
            var kinds = component.Get("onlyTargetKinds") as List<object>;
            if (kinds == null) return true;
            string kind = target.Kind == CharacterEnemyKind.Line ? "line" :
                target.Kind == CharacterEnemyKind.Boss ? "boss" :
                target.Kind == CharacterEnemyKind.Enraged ? "enraged" :
                target.Kind == CharacterEnemyKind.Story ? "story" : null;
            return kind != null && kinds.Contains(kind);
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
            IReadOnlyList<SpatialCombatEnemy> targets,
            ISet<string> activatedThisAttack = null,
            bool additionalCast = false)
        {
            string attackerId = InstanceKey(actor.Id);
            bool composite = SourceModelNextThirtySkillCatalog.IsFirstCompositeActive(skill) ||
                SourceModelNextThirtySkillCatalog.IsSecondCompositeSkill(skill) ||
                SourceModelNextThirtySkillCatalog.IsThirdCompositeAttack(skill) ||
                SourceModelNextThirtySkillCatalog.IsFourthCompositeAttack(skill) ||
                SourceModelNextThirtySkillCatalog.IsFifthCompositeSkill(skill) ||
                SourceModelNextThirtySkillCatalog.IsSixthCompositeSkill(skill) ||
                SourceModelNextThirtySkillCatalog.IsSeventhCompositeSkill(skill) ||
                SourceModelNextThirtySkillCatalog.IsEighthCompositeSkill(skill) ||
                SourceModelNineteenthSkillCatalog.IsSpecial(skill) ||
                SourceModelEighteenthSkillCatalog.IsSpecial(skill) ||
                SourceModelSixteenthSkillCatalog.IsSpecial(skill) ||
                SourceModelFifteenthSkillCatalog.IsSpecial(skill) ||
                IsSourceModelEighthStatusApproach(skill);
            foreach (var component in skill.Components)
            {
                if (!SourceModelNineteenthComponentProceeds(skill, component))
                    continue;
                if (SourceModelEighthSkipDamageComponent(skill, component))
                    continue;
                if (skill.Id == "TR-20:skill:0" &&
                    component.Type == "physicalDamageOverTime")
                {
                    ScheduleSourceModelZoroDot(actor, primary, skill, component, targets);
                    continue;
                }
                if (skill.Id == "TR-24:skill:2" && component.Type == "summon")
                {
                    SpawnSourceModelKobyClone(actor, primary, skill, component);
                    continue;
                }
                if (skill.Id == "TR-10:skill:1" &&
                    component.Type == "additionalCast")
                {
                    if (!additionalCast && sourceModelRules.NextScriptRoll(1000000) <
                        (int)(component.Number("chancePercent").Value * 10000))
                        ApplySourceModelNextThirtySkill(actor, primary, skill,
                            targets, activatedThisAttack, true);
                    continue;
                }
                if (skill.Id == "TR-15:skill:1" &&
                    component.Type == "magicalDamageOverTime")
                {
                    ScheduleSourceModelPoseidon(actor, primary, skill, component, targets);
                    continue;
                }
                // Sanji's 20% native object stun is already resolved by the
                // original-object basic attack path. Never stun twice.
                if (skill.Id == "TR-12:skill:1" && component.Type == "stun")
                    continue;
                if (skill.Id == "TR-24:skill:3" && component.Type == "stun")
                    continue; // Native object already guarantees this stun.
                if (skill.Id == "TR-12:skill:1" &&
                    component.Get("whenAnySkill") != null &&
                    (activatedThisAttack == null ||
                     !(activatedThisAttack.Contains("불꽃 스펙트럼") ||
                       activatedThisAttack.Contains("무통 샷") ||
                       activatedThisAttack.Contains("유성소바 킥"))))
                    continue;
                if (SourceModelNextThirtySkillCatalog.IsThirdCompositeAttack(skill) &&
                    component.Type == "summonSoldier")
                {
                    TrySourceModelSongSoldier(actor, primary, skill, component);
                    continue;
                }
                if (composite && component.Type == "selfHeal")
                {
                    if (actor.Abilities.Resources != null)
                    {
                        var hp = actor.Abilities.Resources;
                        hp.SetHealth(Math.Min(hp.MaximumHealth,
                            hp.Health + component.Number("amount").Value));
                    }
                    continue;
                }
                if (composite && component.Type == "allyAttackPowerBuff")
                {
                    ApplySourceModelCompositeAllyPower(actor, skill, component);
                    continue;
                }
                if (skill.Id == "T-05:skill:1" &&
                    component.Type == "explosiveDamageAmplification")
                {
                    ApplySourceModelNineteenthLuna(actor, skill, component, targets);
                    continue;
                }
                if (composite && CommonSkillStatusEffects.IsTimedStatus(component.Type) &&
                    component.Type != "stun")
                {
                    ApplySourceModelCompositeStatus(actor, primary, skill,
                        component, targets);
                    continue;
                }
                if (composite && component.Type == "armorBreak")
                {
                    ApplySourceModelCompositeArmorBreak(actor, primary, skill,
                        component, targets);
                    continue;
                }
                if (component.Type == "stun")
                {
                    // Native hero/boss resistance and immunity are not known.
                    // Only ordinary line enemies receive the practice stun.
                    foreach (var target in targets)
                    {
                        if (target.Kind != CharacterEnemyKind.Line ||
                            !target.Enemy.IsAlive ||
                            (skill.Id == "TR-20:skill:1" &&
                             !ReferenceEquals(primary, target)) ||
                            !SourceModelNextThirtyComponentTargets(
                                component, primary, target)) continue;
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
                foreach (var target in targets)
                {
                    SpatialCombatEnemy current;
                    if (!target.Enemy.IsAlive ||
                        !enemies.TryGetValue(target.Id, out current) ||
                        !ReferenceEquals(current, target) ||
                        !CanAttackInThisLane(actor, target) ||
                        (skill.Id == "TR-26:skill:2" &&
                         component.Get("singleExtraPercent") != null &&
                         !ReferenceEquals(primary, target)) ||
                        !SourceModelNextThirtyComponentTargets(component, primary, target) ||
                        !SourceModelFifteenthComponentTargets(skill, component,
                            primary, target) ||
                        !SourceModelSixteenthComponentTargets(skill, component,
                            primary, target) ||
                        !SourceModelEighteenthComponentTargets(skill, component,
                            primary, target))
                        continue;
                    double missingBeforeComponent =
                        target.Enemy.MaxHealth - target.Enemy.Health;
                    double raw = composite ? SourceModelCompositeRawDamage(actor,
                        skill, component, target, multiplier) :
                        (component.Number("amount") ??
                         component.Number("amountPerHit") ?? 0) * multiplier;
                    raw = SourceModelSixthRawDamageAdjustment(actor, primary,
                        skill, component, target, raw);
                    raw = SourceModelSeventhRawDamageAdjustment(actor, primary,
                        skill, component, target, raw);
                    raw = SourceModelEighthRawDamageAdjustment(actor, primary,
                        skill, component, target, raw);
                    raw = SourceModelNinthDamageAdjustment(actor,
                        skill, component, target, raw);
                    raw = SourceModelFifteenthDamageAdjustment(actor, primary,
                        skill, component, target, raw);
                    raw = SourceModelSixteenthDamageAdjustment(actor, primary,
                        skill, component, target, raw);
                    raw = SourceModelEighteenthRawDamageAdjustment(actor, primary,
                        skill, component, target, raw);
                    raw = SourceModelNineteenthRawAdjustment(actor, primary,
                        skill, component, target, raw);
                    // Rebecca's tooltip gives a per-legacy-level increment.
                    // The offline model has no native legacy-level provider:
                    // the first documented increment is the conservative
                    // mobile practice value, never a fabricated rank.
                    if ((skill.Id == "LI-02:skill:1" ||
                         skill.Id == "LI-02:skill:2") &&
                        component.Type == "physical")
                        raw += component.Number("plusLegacyLevelDamage") ?? 0;
                    if (component.Text("basis") == "targetMaxHealth" &&
                        component.Number("percent").HasValue)
                        raw += target.Enemy.MaxHealth * component.Number("percent").Value / 100;
                    if (component.Get("plusTargetCurrentHealthPercent") != null &&
                        !(skill.Id == "TR-07:skill:2" &&
                          (target.Kind == CharacterEnemyKind.Boss ||
                           target.Kind == CharacterEnemyKind.Story)))
                        raw += target.Enemy.Health *
                            component.Number("plusTargetCurrentHealthPercent").Value / 100;
                    if (skill.Id == "TR-07:skill:2" &&
                        (target.Kind == CharacterEnemyKind.Boss ||
                         target.Kind == CharacterEnemyKind.Story))
                        raw += 500000;
                    if (skill.Id == "TR-07:skill:2")
                        raw *= 1 + SourceModelLucciClears() * .0004;
                    raw += target.Enemy.MaxHealth *
                        ((component.Number("plusTargetMaxHealthPercent") ?? 0) +
                         (component.Number("extraTargetMaxHealthPercent") ?? 0)) / 100;
                    raw += (target.Enemy.MaxHealth - target.Enemy.Health) *
                        (component.Number("plusTargetMissingHealthPercent") ?? 0) / 100;
                    if (target.Kind == CharacterEnemyKind.Boss)
                        raw += target.Enemy.MaxHealth *
                            (component.Number("extraBossMaxHealthPercent") ?? 0) / 100;
                    if (target.Kind == CharacterEnemyKind.Enraged)
                        raw += target.Enemy.MaxHealth *
                            (component.Number("extraEnragedMaxHealthPercent") ?? 0) / 100;
                    raw += component.Number("extraAmount") ?? 0;
                    if (ReferenceEquals(primary, target))
                        raw += component.Number("extraSingleAmount") ?? 0;
                    if (ReferenceEquals(primary, target))
                        raw *= 1 + (component.Number("singlePrimaryTargetExtraPercent") ?? 0) / 100;
                    if (raw <= 0) continue;
                    // Red Hawk doubles the whole primary magic packet,
                    // including its documented max-HP component.
                    if (skill.Id == "TR-08:skill:0" &&
                        component.Type == "magical" &&
                        ReferenceEquals(primary, target))
                        raw *= skill.Number("singleTargetMagicalDamageMultiplier").Value;
                    if (double.IsNaN(raw) || double.IsInfinity(raw) || raw >= long.MaxValue)
                        throw new OverflowException("도감 모델 원시 피해량 범위 초과");
                    Ordr323ArmorType armor = default(Ordr323ArmorType);
                    if (component.Type != "fixed" &&
                        (!target.Enemy.Defenses.BaseArmor.HasValue ||
                        !Ordr323OriginalDamageRules.TryReadArmorType(
                            target.Enemy.Defenses.WarcraftArmorType, out armor)))
                    {
                        ReportSourceModelNextThirtyDeferred(attackerId,
                            target.Id, "대상 방어력·유형 자료 부재");
                        continue;
                    }
                    int hits = SourceModelSixthHitCount(skill, component, target);
                    for (int hit = 0; hit < hits && target.Enemy.IsAlive; hit++)
                    {
                        double hitRaw = SourceModelNineteenthRawPerHit(skill,
                            component, target, raw);
                        // Fixed tooltip packets bypass the practice armor path.
                        // This distinction is not yet native Warcraft parity.
                        double modeled = component.Type == "fixed" ? hitRaw :
                            Ordr323MapDamageMath.ApplyVerifiedFactors(
                                hitRaw, Ordr323AttackType.Spells, armor, true, true,
                                target.Enemy.Defenses.BaseArmor.Value -
                                SourceModelUnitEffects.ResolvePracticeArmorReduction(
                                    target.Enemy.Status.Snapshot()) -
                                ArmorBreakStacksOf(target.Id), false, 0, 1);
                        if (composite)
                            modeled *= SourceModelCompositeMagicFactor(target,
                                component.Type);
                        if (modeled >= long.MaxValue || double.IsNaN(modeled))
                            throw new OverflowException("도감 모델 피해량 범위 초과");
                        long actual;
                        if (!resolution.TryApplySkillDamage(attackerId, target.Id,
                                (long)Math.Floor(modeled), out actual)) break;
                        events.Add(new CharacterCombatEvent(
                            CharacterCombatEventType.SkillDamage, attackerId,
                            target.Id, skill.Name,
                            "TOOLTIP_MODEL_ONLY " + component.Type +
                            " / 원작 최종 방어·면역·속성 보정 미검증", actual));
                        if (skill.Id == "LI-06:skill:0" &&
                            component.Type == "magical" && target.Enemy.IsAlive)
                            ApplySourceModelSecondMissingHealthPhysical(actor,
                                target, skill, component, missingBeforeComponent);
                        if (!target.Enemy.IsAlive)
                            events.Add(new CharacterCombatEvent(
                                CharacterCombatEventType.EnemyDefeated, attackerId,
                                target.Id, skill.Name, "연습 도감 스킬로 적 처치", actual));
                    }
                    ApplySourceModelFifteenthPrimaryFollowup(actor, primary,
                        skill, component, target);
                    // Ryokugyu's documented story-only fixed bonus is a
                    // separate, armor-ignoring packet after the physical hit.
                    if (composite && component.Type == "physical" &&
                        target.Kind == CharacterEnemyKind.Story && target.Enemy.IsAlive &&
                        component.Get("extraStoryFixedPercentOfSkill") != null)
                    {
                        double extra = raw *
                            component.Number("extraStoryFixedPercentOfSkill").Value / 100;
                        if (double.IsNaN(extra) || double.IsInfinity(extra) ||
                            extra >= long.MaxValue) throw new OverflowException(
                                "도감 스토리 추가 피해량 범위 초과");
                        long actual;
                        if (resolution.TryApplySkillDamage(attackerId, target.Id,
                                (long)Math.Floor(extra), out actual))
                        {
                            events.Add(new CharacterCombatEvent(
                                CharacterCombatEventType.SkillDamage,
                                attackerId, target.Id, skill.Name,
                                "TOOLTIP_MODEL_ONLY / 스토리 전용 고정 추가 피해", actual));
                            if (!target.Enemy.IsAlive)
                                events.Add(new CharacterCombatEvent(
                                    CharacterCombatEventType.EnemyDefeated, attackerId,
                                    target.Id, skill.Name, "연습 스토리 추가 피해로 처치", actual));
                        }
                    }
                }
            }
            // E-07's independent onSkillActivated passive is activated only
            // by its three documented fire skills, never by every Ace proc.
            if (skill.Id == "E-07:skill:2" || skill.Id == "E-07:skill:3")
                SourceModelAceFlameOnSkill(actor, primary, skill, targets);
        }
    }
}
