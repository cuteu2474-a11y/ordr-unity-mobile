using System;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private static bool SourceModelEighteenthComponentTargets(
            ImportedCombatSkill skill, ImportedCombatEffect component,
            SpatialCombatEnemy primary, SpatialCombatEnemy target)
        {
            if (!SourceModelEighteenthSkillCatalog.IsSpecial(skill)) return true;
            if (skill.Id == "MY-10:skill:3" && component.Type == "fixed")
                return ReferenceEquals(primary, target) &&
                    target.Kind != CharacterEnemyKind.Story;
            if (skill.Id == "R-42:skill:1" && component.Type == "explosive")
                return ReferenceEquals(primary, target) &&
                    target.Kind == CharacterEnemyKind.Line;
            if (skill.Id == "R-25:skill:1")
                return target.Kind == CharacterEnemyKind.Line;
            return true;
        }

        // No new RNG or hero attributes: the existing one-roll skill trigger,
        // shared source-object basic attack power, current target health and
        // common armor/HP ledger provide the explicit tooltip results.
        private static double SourceModelEighteenthRawDamageAdjustment(
            CharacterInstance actor, SpatialCombatEnemy primary,
            ImportedCombatSkill skill, ImportedCombatEffect component,
            SpatialCombatEnemy target, double raw)
        {
            if (!SourceModelEighteenthSkillCatalog.IsSpecial(skill)) return raw;
            switch (skill.Id)
            {
                case "RN-10:skill:0":
                    return ReferenceEquals(primary, target) ? raw * 2 : raw;
                case "D-05:skill:0":
                    return raw + (ReferenceEquals(primary, target) ? 500000 : 0);
                case "R-42:skill:1":
                    return component.Type == "explosive" ?
                        target.Enemy.Health * .1 : raw;
                case "R-25:skill:1":
                    return target.Enemy.Health * .15;
                case "MY-10:skill:3":
                    if (component.Type == "physical")
                        return actor.Mapping.AttackSpec.BasicAttack.NominalAttackPower *
                            8 + 900000;
                    if (component.Type == "fixed")
                        return target.Enemy.MaxHealth * .04;
                    break;
            }
            return raw;
        }
    }
}
