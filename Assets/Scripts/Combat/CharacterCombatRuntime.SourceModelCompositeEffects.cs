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
                if (skill.Id == "L-28:skill:0" &&
                    component.Type == "armorReduction")
                {
                    // Jinbe's rare debuff requires ten accumulated breaks;
                    // it cannot be retriggered by a second cast on that enemy.
                    var condition = component.Get("condition") as Dictionary<string, object>;
                    object required;
                    if (condition == null ||
                        !condition.TryGetValue("requiredArmorBreakStacks", out required) ||
                        ArmorBreakStacksOf(target.Id) < Convert.ToInt32(required))
                        continue;
                    bool hasTranscendentJinbe = false;
                    foreach (var ally in characters.Values)
                        if (ally.Catalog.Id == "TR-22" && ally.AttackReady &&
                            IsStoryDeployed(ally.Id) == IsStoryDeployed(actor.Id))
                            hasTranscendentJinbe = true;
                    if (hasTranscendentJinbe) continue;
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

        private static double SourceModelCompositeRawDamage(
            CharacterInstance actor, ImportedCombatEffect component,
            SpatialCombatEnemy target, double multiplier)
        {
            if (component.Text("basis") == "basicAttackDamage")
            {
                double rawAttack = actor.Mapping.AttackSpec.BasicAttack.NominalAttackPower;
                double total = rawAttack * component.Number("percent").Value / 100 +
                    component.Number("flatAmount").Value;
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
            return (component.Number("amount") ??
                component.Number("amountPerHit") ?? 0) * multiplier;
        }

        private static double SourceModelCompositeMagicFactor(
            SpatialCombatEnemy target, string componentType)
        {
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
    }
}
