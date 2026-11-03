using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // The first ten complex active packets reuse the *existing* enemy
        // status, armor-break, shared damage and resource ledgers. An omitted
        // tooltip duration means an effect lives on that enemy until it dies;
        // never invent a number of seconds in the imported skill definition.
        private void ApplySourceModelCompositeStatus(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            ImportedCombatEffect component,
            IReadOnlyList<SpatialCombatEnemy> targets)
        {
            CombatEffectType type;
            double amount, duration;
            bool timed = CommonSkillStatusEffects.TryRead(component,
                out type, out amount, out duration);
            if (!timed)
            {
                amount = component.Number(component.Type == "armorReduction" ?
                    "amount" : "percent").Value;
                duration = 0;
                switch (component.Type)
                {
                    case "armorReduction": type = CombatEffectType.ArmorReduction; break;
                    case "magicResistanceReduction":
                        type = CombatEffectType.MagicResistanceReduction; break;
                    case "magicDamageAmplification":
                        type = CombatEffectType.MagicDamageAmplification; break;
                    case "explosiveDamageAmplification":
                        type = CombatEffectType.ExplosiveDamageAmplification; break;
                    case "movementSlow":
                        type = CombatEffectType.MovementSlowPercent; break;
                    default: throw new InvalidOperationException("미승인 복합 상태효과");
                }
            }
            string attackerId = InstanceKey(actor.Id);
            string source = "practice-tooltip:" + attackerId + ":" +
                skill.Id + ":" + component.Type;
            foreach (var target in targets)
            {
                if (!target.Enemy.IsAlive ||
                    !SourceModelNextThirtyComponentTargets(component, primary, target))
                    continue;
                if ((skill.Id == "L-28:skill:0" ||
                     skill.Id == "TR-22:skill:0") &&
                    component.Type == "armorReduction")
                {
                    // Both Jinbe packets require ten accumulated breaks and
                    // are mutually exclusive within the same battle lane.
                    double required = component.Number("requiredArmorBreakStacks") ?? 0;
                    if (skill.Id == "L-28:skill:0")
                    {
                        var condition = component.Get("condition") as Dictionary<string, object>;
                        object value;
                        if (condition == null ||
                            !condition.TryGetValue("requiredArmorBreakStacks", out value))
                            continue;
                        required = Convert.ToDouble(value);
                    }
                    if (ArmorBreakStacksOf(target.Id) < required)
                        continue;
                    string excludedAlly = skill.Id == "L-28:skill:0" ?
                        "TR-22" : "L-28";
                    bool hasExcludedJinbe = false;
                    foreach (var ally in characters.Values)
                        if (ally.Catalog.Id == excludedAlly && ally.AttackReady &&
                            IsStoryDeployed(ally.Id) == IsStoryDeployed(actor.Id))
                            hasExcludedJinbe = true;
                    if (hasExcludedJinbe) continue;
                    bool appliedPreviously = false;
                    foreach (var existing in target.Enemy.Status.Snapshot())
                        if (existing.SourceId == source) appliedPreviously = true;
                    if (appliedPreviously) continue;
                }
                target.Enemy.Status.RemoveSource(source);
                target.Enemy.Status.Apply(new CombatEffect(source, type,
                    timed ? CombatEffectOrigin.TriggeredSkill : CombatEffectOrigin.Aura,
                    amount, duration));
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.StatusApplied, attackerId,
                    target.Id, skill.Name,
                    timed ? "TOOLTIP_MODEL_ONLY / 제한시간 효과" :
                        "TOOLTIP_MODEL_ONLY / 원본 지속시간 미기재: 적 생존 중 유지", 0));
            }
        }

        private void ApplySourceModelCompositeArmorBreak(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            ImportedCombatEffect component,
            IReadOnlyList<SpatialCombatEnemy> targets)
        {
            int added = (int)component.Number("stacks").Value;
            double? max = component.Number("max") ??
                component.Number("maxStacks") ?? component.Number("maxArmorReduction");
            int limit = max.HasValue ? (int)max.Value : int.MaxValue;
            foreach (var target in targets)
            {
                if (!target.Enemy.IsAlive ||
                    !SourceModelNextThirtyComponentTargets(component, primary, target))
                    continue;
                int current = ArmorBreakStacksOf(target.Id);
                int next = (int)Math.Min((long)current + added, (long)limit);
                armorBreakStacks[target.Id] = next;
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.ArmorBreakApplied,
                    InstanceKey(actor.Id), target.Id, skill.Name,
                    "TOOLTIP_MODEL_ONLY / 공통 아머브레이크 스택", next));
            }
        }

        private readonly Dictionary<long, List<double>> sourceSongSoldierExpiry =
            new Dictionary<long, List<double>>();
        private Func<long, double?> sourceModelStrengthProvider;
        private Func<long, double?> sourceModelAgilityProvider;
        private readonly Dictionary<long, double> sourceModelFireFistReadyAt =
            new Dictionary<long, double>();

        // An actual hero-attribute provider may be attached later without
        // changing any skill formula; absent data contributes ZERO scaling.
        public void ConfigureSourceModelHeroStrengthProvider(Func<long, double?> provider)
        {
            sourceModelStrengthProvider = provider;
        }

        public void ConfigureSourceModelHeroAgilityProvider(Func<long, double?> provider)
        {
            sourceModelAgilityProvider = provider;
        }

        private int SourceModelSongSoldiers(long casterId)
        {
            List<double> expiry;
            if (!characters.ContainsKey(casterId) ||
                !sourceSongSoldierExpiry.TryGetValue(casterId, out expiry)) return 0;
            expiry.RemoveAll(until => until <= Time);
            if (expiry.Count == 0) sourceSongSoldierExpiry.Remove(casterId);
            return expiry.Count;
        }

        public int PracticeSongSoldierCount(long casterId) =>
            SourceModelSongSoldiers(casterId);

        private void TrySourceModelSongSoldier(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            ImportedCombatEffect component)
        {
            // 25% secondary roll occurs only after the parent 4% multi-hit
            // proc. There is no confirmed soldier rawcode or attack profile:
            // track its seven-second contribution, not a fake combat unit.
            int threshold = (int)Math.Round(component.Number("chancePercent").Value *
                10000, MidpointRounding.AwayFromZero);
            if (sourceModelRules.NextScriptRoll(1000000) >= threshold) return;
            List<double> expiry;
            if (!sourceSongSoldierExpiry.TryGetValue(actor.Id, out expiry))
            {
                expiry = new List<double>();
                sourceSongSoldierExpiry.Add(actor.Id, expiry);
            }
            expiry.RemoveAll(until => until <= Time);
            expiry.Add(Time + component.Number("durationSeconds").Value);
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 7초 음표 병사 기여 수치; 실제 소환 전투 객체 미검증", 0));
        }

        private double SourceModelCompositeRawDamage(
            CharacterInstance actor, ImportedCombatSkill skill,
            ImportedCombatEffect component,
            SpatialCombatEnemy target, double multiplier)
        {
            if (SourceModelNextThirtySkillCatalog.IsThirdCompositeAttack(skill) ||
                SourceModelNextThirtySkillCatalog.IsFourthCompositeAttack(skill) ||
                SourceModelNextThirtySkillCatalog.IsFifthCompositeSkill(skill) ||
                SourceModelNextThirtySkillCatalog.IsSeventhCompositeSkill(skill) ||
                SourceModelNextThirtySkillCatalog.IsEighthCompositeSkill(skill))
            {
                double basic = actor.Mapping.AttackSpec.BasicAttack.NominalAttackPower;
                // Kizaru's second component is a primary-only bonus based on
                // the full three-hit packet; the first packet itself is not
                // repeated by this component.
                if (skill.Id == "TR-26:skill:2" &&
                    component.Get("singleExtraPercent") != null)
                    return SourceModelCompositeRawDamage(actor, skill,
                        skill.Components[0], target, 1) *
                        skill.Components[0].Number("hits").Value *
                        component.Number("singleExtraPercent").Value / 100 * multiplier;
                if (component.Type == "critical")
                    // The normal hit already happened in the attack pipeline;
                    // only the 4.25x *increment* should hit again.
                    return basic * (component.Number("multiplier").Value - 1) * multiplier;
                if (skill.Id == "TR-04:skill:3" &&
                    component.Text("basis") == "basicAttackDamage")
                    return basic * component.Number("multiplier").Value * multiplier;
                if (component.Get("amountPerSoldier") != null)
                    return component.Number("amountPerSoldier").Value *
                        SourceModelSongSoldiers(actor.Id) * multiplier;
                if (component.Type == "explosive")
                {
                    if (component.Text("basis") == "targetCurrentHealth")
                        return target.Enemy.Health *
                            component.Number("percentPerSoldier").Value / 100 *
                            SourceModelSongSoldiers(actor.Id) * multiplier;
                    return (target.Enemy.MaxHealth *
                        component.Number("percent").Value / 100 +
                        (target.Enemy.MaxHealth - target.Enemy.Health) *
                        (component.Number("plusMissingHealthPercent") ?? 0) / 100) *
                        multiplier;
                }
                if (component.Text("basis") == "targetCurrentHealth")
                    return target.Enemy.Health *
                        component.Number("percent").Value / 100 * multiplier;
                if (component.Get("skillDamagePercent") != null &&
                    skill.Id == "TR-22:skill:1")
                    return skill.Components[1].Number("amount").Value *
                        component.Number("skillDamagePercent").Value / 100 * multiplier;
                if (component.Get("bonusPercent") != null &&
                    skill.Id == "LI-02:skill:2")
                    return (skill.Components[0].Number("amount").Value +
                        skill.Components[0].Number("plusLegacyLevelDamage").Value) *
                        component.Number("bonusPercent").Value / 100 * multiplier;
                if (component.Get("basicAttackPercent") != null)
                    return (basic * component.Number("basicAttackPercent").Value / 100 +
                        (component.Number("flatAmount") ?? 0)) * multiplier;
                if (component.Get("basicAttackMultiplier") != null)
                    return (basic * component.Number("basicAttackMultiplier").Value +
                        (component.Number("flatAmount") ?? 0)) * multiplier;
                if (component.Get("strengthScalingPerPoint") != null &&
                    sourceModelStrengthProvider != null)
                {
                    double? strength = sourceModelStrengthProvider(actor.Id);
                    if (strength.HasValue && strength.Value >= 0 &&
                        !double.IsNaN(strength.Value) &&
                        !double.IsInfinity(strength.Value))
                        return ((component.Number("amount") ?? 0) +
                            strength.Value * component.Number("strengthScalingPerPoint").Value) *
                            multiplier;
                }
                if (component.Get("agilityScalingPerPoint") != null)
                {
                    double agility = 0;
                    if (sourceModelAgilityProvider != null)
                    {
                        double? provided = sourceModelAgilityProvider(actor.Id);
                        if (provided.HasValue && provided.Value >= 0 &&
                            !double.IsNaN(provided.Value) &&
                            !double.IsInfinity(provided.Value))
                            agility = provided.Value;
                    }
                    return ((component.Number("amountPerHit") ?? 0) +
                        agility * component.Number("agilityScalingPerPoint").Value) *
                        multiplier;
                }
                // TR-22 Jinbe's +17500 per strength is sourced, but the
                // currently configured runtime has no original strength
                // attribute. Defer scaling instead of inventing its value.
            }
            if (component.Text("basis") == "skillDamage" &&
                skill.Id == "SR-03:skill:0")
                return skill.Components[0].Number("amount").Value *
                    component.Number("percent").Value / 100 * multiplier;
            if (component.Text("basis") == "basicAttackDamage")
            {
                double rawAttack = actor.Mapping.AttackSpec.BasicAttack.NominalAttackPower;
                double total = rawAttack * component.Number("percent").Value / 100 +
                    (component.Number("flatAmount") ?? 0);
                var kinds = component.Get("targetKindMultiplier") as
                    Dictionary<string, object>;
                if (kinds != null)
                {
                    string key = target.Kind == CharacterEnemyKind.Boss ? "boss" :
                        target.Kind == CharacterEnemyKind.Story ? "story" : null;
                    object factor;
                    if (key != null && kinds.TryGetValue(key, out factor))
                        total *= Convert.ToDouble(factor);
                }
                return total * multiplier;
            }
            double packet = (component.Number("amount") ??
                component.Number("amountPerHit") ?? 0) * multiplier;
            var targetKindMultipliers = component.Get("targetKindMultiplier") as
                Dictionary<string, object>;
            if (targetKindMultipliers != null)
            {
                string kind = target.Kind == CharacterEnemyKind.Boss ? "boss" :
                    target.Kind == CharacterEnemyKind.Story ? "story" : null;
                object factor;
                if (kind != null && targetKindMultipliers.TryGetValue(kind, out factor))
                    packet *= Convert.ToDouble(factor);
            }
            return packet;
        }

        private static double SourceModelCompositeMagicFactor(
            SpatialCombatEnemy target, string componentType)
        {
            if (componentType == "explosive")
                return 1 + SourceModelUnitEffects.ResolvePracticeMagicModifier(
                    target.Enemy.Status.Snapshot(),
                    CombatEffectType.ExplosiveDamageAmplification) / 100;
            if (componentType != "magical") return 1;
            var statuses = target.Enemy.Status.Snapshot();
            double amp = SourceModelUnitEffects.ResolvePracticeMagicModifier(
                statuses, CombatEffectType.MagicDamageAmplification);
            double resistanceReduction = SourceModelUnitEffects.ResolvePracticeMagicModifier(
                statuses, CombatEffectType.MagicResistanceReduction);
            // Practice enemies without a native resistance provider receive
            // a relative bonus for the documented reduction; do not overwrite
            // a hypothetical native base resistance or original combat rules.
            return (1 + amp / 100) * (1 + resistanceReduction / 100);
        }

        private void ApplySourceModelCompositeAllyPower(CharacterInstance actor,
            ImportedCombatSkill skill, ImportedCombatEffect component)
        {
            string source = "practice-tooltip:allypower:" + actor.Id + ":" + skill.Id;
            foreach (var ally in characters.Values)
            {
                if (!ally.AttackReady ||
                    IsStoryDeployed(ally.Id) != IsStoryDeployed(actor.Id)) continue;
                CombatStatusEngine status;
                if (!unitStatuses.TryGetValue(InstanceKey(ally.Id), out status)) continue;
                status.RemoveSource(source);
                status.Apply(new CombatEffect(source,
                    CombatEffectType.AttackPowerBuff,
                    CombatEffectOrigin.TriggeredSkill,
                    component.Number("percent").Value,
                    component.Number("durationSeconds").Value));
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                    InstanceKey(actor.Id), InstanceKey(ally.Id), skill.Name,
                    "TOOLTIP_MODEL_ONLY / 같은 전장 아군 평타 강화", 0));
            }
        }

        private void ApplySourceModelSentinelMagicAmplification(CharacterInstance actor,
            SpatialCombatEnemy primary, SourceScriptAttackSpec script)
        {
            if (actor.Catalog.Id != "RN-04" || actor.Profile == null ||
                actor.Profile.UnitId != "RN-04") return;
            ImportedCombatSkill selected = null;
            foreach (var skill in actor.Profile.Skills)
                if (skill.Id == "RN-04:skill:0") { selected = skill; break; }
            if (!SourceModelNextThirtySkillCatalog.IsSecondCompositeSkill(selected) ||
                !SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(selected)) return;
            var targets = SourceScriptTargets(actor, primary, script.OnProc[0]);
            if (targets.Count == 0) return;
            // The -50 armor on the primary is already resolved via
            // ScheduleSourceModelSentinelFaerie: adding it here would make
            // its timed 60-second native effect incorrectly permanent.
            ApplySourceModelCompositeStatus(actor, primary, selected,
                selected.Components[2], targets);
        }

        // A second damage school, not an extra spell multiplier. This is
        // resolved through the common target-defense and damage ledger.
        private void ApplySourceModelSecondMissingHealthPhysical(
            CharacterInstance actor, SpatialCombatEnemy target,
            ImportedCombatSkill skill, ImportedCombatEffect component,
            double originalMissingHealth)
        {
            Ordr323ArmorType armor;
            if (!target.Enemy.Defenses.BaseArmor.HasValue ||
                !Ordr323OriginalDamageRules.TryReadArmorType(
                    target.Enemy.Defenses.WarcraftArmorType, out armor)) return;
            double raw = originalMissingHealth *
                component.Number("plusTargetMissingHealthPercentPhysical").Value / 100;
            double modeled = Ordr323MapDamageMath.ApplyVerifiedFactors(raw,
                Ordr323AttackType.Spells, armor, true, true,
                target.Enemy.Defenses.BaseArmor.Value -
                    SourceModelUnitEffects.ResolvePracticeArmorReduction(
                        target.Enemy.Status.Snapshot()) -
                    ArmorBreakStacksOf(target.Id), false, 0, 1);
            if (double.IsNaN(modeled) || double.IsInfinity(modeled) ||
                modeled >= long.MaxValue) throw new OverflowException(
                    "복합 스킬 결손 체력 추가 피해 범위 초과");
            long actual;
            if (!resolution.TryApplySkillDamage(InstanceKey(actor.Id), target.Id,
                    (long)Math.Floor(modeled), out actual)) return;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillDamage,
                InstanceKey(actor.Id), target.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 결손 체력 비례 별도 물리 피해", actual));
            if (!target.Enemy.IsAlive)
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.EnemyDefeated,
                    InstanceKey(actor.Id), target.Id, skill.Name,
                    "연습 복합 스킬 추가 피해로 처치", actual));
        }
    }
}
