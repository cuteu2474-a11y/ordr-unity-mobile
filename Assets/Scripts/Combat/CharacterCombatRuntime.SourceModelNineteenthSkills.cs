using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // A failed proc must not start the documented three-second cooldown.
        // The keyed ledger also keeps two S-Hawk skills independent.
        private readonly Dictionary<string, double> sourceModelNineteenthCooldowns =
            new Dictionary<string, double>(StringComparer.Ordinal);

        private static string NineteenthCooldownKey(CharacterInstance actor,
            ImportedCombatSkill skill) => actor.Id + ":" + skill.Id;

        private bool SourceModelNineteenthReady(CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            if (skill.Id != "SR-02:skill:2") return true;
            double readyAt;
            return !sourceModelNineteenthCooldowns.TryGetValue(
                NineteenthCooldownKey(actor, skill), out readyAt) || readyAt <= Time;
        }

        private void SourceModelNineteenthCommit(CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            if (skill.Id == "SR-02:skill:2")
                sourceModelNineteenthCooldowns[NineteenthCooldownKey(actor, skill)] =
                    Time + skill.Number("absoluteCooldownSeconds").Value;
        }

        private static double SourceModelNineteenthChance(ImportedCombatSkill skill,
            SpatialCombatEnemy target, double baseChance)
        {
            if (skill.Id == "SR-02:skill:1" || skill.Id == "SR-02:skill:2")
                if (target.Kind == CharacterEnemyKind.Boss ||
                    target.Kind == CharacterEnemyKind.Enraged)
                    return baseChance + skill.Number("extraChancePercent").Value;
            return baseChance;
        }

        // A 20% second stage is rolled once per successful Ghost Rap cast,
        // not once per enemy. No invented extra chance/cooldown is added.
        private bool SourceModelNineteenthComponentProceeds(
            ImportedCombatSkill skill, ImportedCombatEffect component)
        {
            return skill.Id != "D-04:skill:1" ||
                !component.Number("chancePercent").HasValue ||
                sourceModelRules.NextScriptRoll(1000000) <
                    (int)(component.Number("chancePercent").Value * 10000);
        }

        private static double SourceModelNineteenthRawAdjustment(
            CharacterInstance actor, SpatialCombatEnemy primary,
            ImportedCombatSkill skill, ImportedCombatEffect component,
            SpatialCombatEnemy target, double raw)
        {
            if (!SourceModelNineteenthSkillCatalog.IsSpecial(skill)) return raw;
            switch (skill.Id)
            {
                case "L-06:skill:0":
                case "L-06:skill:1":
                    if (component.Type == "explosive")
                        return target.Enemy.Health *
                            component.Number("percentPerHit").Value / 100;
                    break;
                case "T-04:skill:0":
                    if (component.Type == "explosive")
                        return target.Enemy.MaxHealth * .05;
                    break;
                case "SR-02:skill:1":
                    if (component.Get("bonusPercent") != null)
                        return skill.Components[0].Number("amount").Value *
                            component.Number("bonusPercent").Value / 100;
                    break;
                case "T-03:skill:1":
                    if (component.Type == "magical" &&
                        ReferenceEquals(primary, target))
                        return raw + component.Number("extraSingleMagicalAmount").Value;
                    break;
            }
            return raw;
        }

        // Recalculate current-health damage between every Reiju hit.
        // A fixed pre-cast HP snapshot would turn repeated percent damage
        // into a different outcome when the preceding hit already landed.
        private static double SourceModelNineteenthRawPerHit(
            ImportedCombatSkill skill, ImportedCombatEffect component,
            SpatialCombatEnemy target, double raw)
        {
            if ((skill.Id == "L-06:skill:0" || skill.Id == "L-06:skill:1") &&
                component.Type == "explosive")
                return target.Enemy.Health *
                    component.Number("percentPerHit").Value / 100;
            return raw;
        }

        // Carrot's 1%-per-proc explosive amplification is a genuine stack,
        // capped at 25% across all same-lane sources. An untimed status uses
        // the existing enemy lifecycle rather than an invented duration.
        private void ApplySourceModelNineteenthLuna(CharacterInstance actor,
            ImportedCombatSkill skill, ImportedCombatEffect component,
            IReadOnlyList<SpatialCombatEnemy> targets)
        {
            const string prefix = "practice-tooltip:luna:";
            int maximum = (int)component.Number("maxPercent").Value;
            foreach (var target in targets)
            {
                SpatialCombatEnemy current;
                if (!target.Enemy.IsAlive ||
                    !enemies.TryGetValue(target.Id, out current) ||
                    !ReferenceEquals(current, target) ||
                    !CanAttackInThisLane(actor, target)) continue;
                int existing = 0;
                foreach (var status in target.Enemy.Status.Snapshot())
                    if (status.SourceId.StartsWith(prefix, StringComparison.Ordinal))
                        existing++;
                if (existing >= maximum) continue;
                string source = prefix + actor.Id + ":" + skill.Id + ":" +
                    target.Id + ":" + existing;
                target.Enemy.Status.Apply(new CombatEffect(source,
                    CombatEffectType.ExplosiveDamageAmplification,
                    CombatEffectOrigin.Aura,
                    component.Number("percent").Value, 0));
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.StatusApplied,
                    InstanceKey(actor.Id), target.Id, skill.Name,
                    "TOOLTIP_MODEL_ONLY 폭발 피해 증폭 누적 / 원본 중첩·지속시간 미검증",
                    existing + 1));
            }
        }
    }
}
