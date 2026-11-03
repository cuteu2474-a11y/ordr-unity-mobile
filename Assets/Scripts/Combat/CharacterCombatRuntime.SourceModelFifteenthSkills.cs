using System;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // The source tooltip's H-21 boss override is applied before a proc
        // roll. It is not a second independent roll or a fabricated buff.
        private static double SourceModelFifteenthProcChance(ImportedCombatSkill skill,
            SpatialCombatEnemy primary, double ordinaryChance)
        {
            return skill.Id == "H-21:skill:0" &&
                SourceModelFifteenthSkillCatalog.CanModelExactSpecial(skill) &&
                primary.Kind == CharacterEnemyKind.Boss ? 40 : ordinaryChance;
        }

        private static bool SourceModelFifteenthComponentTargets(
            ImportedCombatSkill skill, ImportedCombatEffect component,
            SpatialCombatEnemy primary, SpatialCombatEnemy target)
        {
            if (!SourceModelFifteenthSkillCatalog.IsSpecial(skill)) return true;
            if (skill.Id == "H-14:skill:0" &&
                target.Kind == CharacterEnemyKind.Story) return false;
            if (skill.Id == "L-18:skill:0" && component.Type == "physical")
                return primary.Id == target.Id &&
                    target.Kind != CharacterEnemyKind.Line;
            return true;
        }

        private double SourceModelFifteenthStrength(long id)
        {
            if (sourceModelStrengthProvider == null) return 0;
            double? recorded = sourceModelStrengthProvider(id);
            return recorded.HasValue && recorded.Value >= 0 &&
                !double.IsNaN(recorded.Value) && !double.IsInfinity(recorded.Value)
                ? recorded.Value : 0;
        }

        // The shared resolver still owns armor, fixed damage, HP/death,
        // target/lane validity and status durations. Only exact tooltip
        // components that the generic numeric parser cannot express are
        // calculated here. Unprovided hero Strength means +0, not fake stats.
        private double SourceModelFifteenthDamageAdjustment(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            ImportedCombatEffect component, SpatialCombatEnemy target, double raw)
        {
            if (!SourceModelFifteenthSkillCatalog.IsSpecial(skill)) return raw;
            switch (skill.Id)
            {
                case "TR-03:skill:1":
                    return raw + SourceModelFifteenthStrength(actor.Id) * 10000;
                case "TR-27:skill:2":
                    return 350000 + SourceModelFifteenthStrength(actor.Id) * 2500 +
                        actor.Mapping.AttackSpec.BasicAttack.NominalAttackPower * 6;
                case "T-01:skill:0":
                    return raw + (target.Kind == CharacterEnemyKind.Line ?
                        target.Enemy.Health * .25 : 0);
                case "LI-05:skill:1":
                    return raw * (target.Kind == CharacterEnemyKind.Enraged ? 10 : 1);
                case "L-18:skill:0":
                    return component.Type == "physical" ? 245000 * 2.25 : raw;
                case "H-21:skill:0":
                    return raw + (target.Kind == CharacterEnemyKind.Boss &&
                        component.Get("extraBossCurrentHealthPercent") != null ?
                        target.Enemy.Health * .009 : 0);
                case "H-20:skill:0":
                    return raw + target.Enemy.MaxHealth *
                        (target.Kind == CharacterEnemyKind.Boss ? .07 :
                         target.Kind == CharacterEnemyKind.Line ||
                         target.Kind == CharacterEnemyKind.Enraged ? .35 : 0);
                case "I-08:skill:0":
                    return target.Enemy.MaxHealth *
                        (target.Kind == CharacterEnemyKind.Line ? .5 :
                         target.Kind == CharacterEnemyKind.Boss ? .1 :
                         target.Kind == CharacterEnemyKind.Story ? .05 : 0);
                default:
                    return raw;
            }
        }

        // LI-05's documented +300,000 primary magical packet is separate
        // from each of its two 320,000 hits. Never double the primary bonus
        // just because the first component carries hits=2.
        private void ApplySourceModelFifteenthPrimaryFollowup(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            ImportedCombatEffect component, SpatialCombatEnemy target)
        {
            if (skill.Id != "LI-05:skill:1" ||
                component != skill.Components[0] ||
                target.Id != primary.Id || !target.Enemy.IsAlive) return;
            ApplySourceModelAreaPacket(InstanceKey(actor.Id), IsStoryDeployed(actor.Id),
                primary, 0, target.Kind == CharacterEnemyKind.Enraged ? 3000000 : 300000,
                Ordr323AttackType.Spells, skill.Name + " · 단일 추가 마법");
        }
    }
}
