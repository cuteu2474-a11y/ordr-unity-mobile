using System;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // H-15's original no-knowledge branch is the default before the
        // Vegapunk knowledge facility is activated. A source-backed research
        // provider suppresses it after that research; the knowledge-only
        // active remains deferred until the actual upgrade path is connected.
        private Func<long, bool?> sourceModelSunnyKnowledgeEvidence;

        public void ConfigureSourceModelSunnyKnowledgeEvidence(Func<long, bool?> evidence)
        {
            if (!UsesSourceModelBasicAttacks || processingStep ||
                pendingStepSeconds != 0 || sourceModelSunnyKnowledgeEvidence != null)
                throw new InvalidOperationException("써니호 베가펑크 연구 상태는 전투 전 한 번만 연결합니다.");
            sourceModelSunnyKnowledgeEvidence = evidence ??
                throw new ArgumentNullException(nameof(evidence));
        }

        private bool SourceModelSixteenthAttackCondition(CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            if (skill.Id != "H-15:skill:1") return true;
            return !SourceModelSunnyHasVerifiedKnowledge(actor.Id);
        }

        private bool SourceModelSunnyHasVerifiedKnowledge(long instanceId)
        {
            // A missing provider is not proof that research was purchased.
            // When witnessed, the original normal shot is replaced by the
            // MP100 knowledge shot through the shared active-resource path.
            return sourceModelSunnyKnowledgeEvidence != null &&
                sourceModelSunnyKnowledgeEvidence(instanceId) == true;
        }

        private static double SourceModelSixteenthProcChance(ImportedCombatSkill skill,
            SpatialCombatEnemy primary, double chance)
        {
            if (!SourceModelSixteenthSkillCatalog.IsSpecial(skill)) return chance;
            if (skill.Id == "I-08:skill:3" &&
                (primary.Kind == CharacterEnemyKind.Boss ||
                 primary.Kind == CharacterEnemyKind.Story)) return 13;
            if (skill.Id == "LI-01:skill:2" &&
                primary.Kind == CharacterEnemyKind.Boss) return chance * 2;
            return chance;
        }

        private static bool SourceModelSixteenthComponentTargets(
            ImportedCombatSkill skill, ImportedCombatEffect component,
            SpatialCombatEnemy primary, SpatialCombatEnemy target)
        {
            if (!SourceModelSixteenthSkillCatalog.IsSpecial(skill)) return true;
            if ((skill.Id == "MY-04:skill:0" && component != skill.Components[0]) ||
                (skill.Id == "MY-11:skill:3" && component.Type == "fixed") ||
                (skill.Id == "D-02:skill:1" && component.Get("extraSkillDamagePercent") != null))
                return primary.Id == target.Id;
            return true;
        }

        private static double SourceModelSixteenthDamageAdjustment(
            CharacterInstance actor, SpatialCombatEnemy primary,
            ImportedCombatSkill skill, ImportedCombatEffect component,
            SpatialCombatEnemy target, double raw)
        {
            if (!SourceModelSixteenthSkillCatalog.IsSpecial(skill)) return raw;
            double basic = SourceModelUpgradeAdjustedAttackPower(actor);
            switch (skill.Id)
            {
                case "LI-05:skill:2":
                    return raw * (target.Kind == CharacterEnemyKind.Enraged ? 10 : 1);
                case "E-05:skill:0":
                    return component.Type == "physical" ? raw * 7.77 : raw;
                case "D-02:skill:1":
                    return component.Get("extraSkillDamagePercent") != null ?
                        625000 * component.Number("extraSkillDamagePercent").Value / 100 : raw;
                case "I-01:skill:1":
                    return component.Type == "physical" ? 615000 + basic * .95 : raw;
                case "I-01:skill:3":
                    return basic * .95;
                case "LI-01:skill:3":
                    return component.Type == "fixed" ? target.Enemy.MaxHealth * .0215 : raw;
                case "LI-01:skill:2":
                    return component.Type == "physical" &&
                        target.Kind == CharacterEnemyKind.Boss ? raw * 2 : raw;
                case "MY-04:skill:0":
                    if (component.Get("singleTargetMaxHealthPercent") != null)
                        return target.Enemy.MaxHealth *
                            (target.Kind == CharacterEnemyKind.Boss ||
                             target.Kind == CharacterEnemyKind.Story ? .015 : .25);
                    return raw;
                case "MY-11:skill:3":
                    return component.Type == "fixed" ?
                        skill.Components[0].Number("amount").Value * .5 : raw;
                case "RN-12:skill:1":
                    return component.Type == "fixed" ?
                        target.Enemy.MaxHealth * .015 : raw;
                case "E-10:skill:2":
                    return component.Type == "critical" ? basic * 9 : raw;
                case "I-08:skill:3":
                    return basic * 5.25;
                default:
                    return raw;
            }
        }
    }
}

