using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private sealed class SourceModelZephyrChargeState
        {
            public double ExpiresAt;
            public long LaneRevision;
            public bool StoryLane;
        }

        private readonly Dictionary<long, SourceModelZephyrChargeState> zephyrCharge =
            new Dictionary<long, SourceModelZephyrChargeState>();

        private void SourceModelZephyrForgetOwner(long id)
        {
            zephyrCharge.Remove(id);
        }

        private static bool IsSourceModelZephyrChargeSkill(ImportedCombatSkill skill)
        {
            return skill != null && skill.Id == "I-10:skill:2" &&
                SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill);
        }

        // A 10% attack roll activates a 0.7s self state. Every declared
        // attack inside that state receives ONE entire area+primary packet.
        // This is explicit model timing: the source tooltip has no native
        // attack-animation or after-attack frame evidence.
        private void ActivateSourceModelZephyrCharge(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            bool alreadyHitThisAttack)
        {
            if (actor.Catalog.Id != "I-10" || actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != "h04G" ||
                !IsSourceModelZephyrChargeSkill(skill)) return;
            zephyrCharge[actor.Id] = new SourceModelZephyrChargeState {
                ExpiresAt = Time + skill.Components[0].Number("durationSeconds").Value,
                LaneRevision = SourceModelLaneRevision(actor.Id),
                StoryLane = IsStoryDeployed(actor.Id)
            };
            if (!alreadyHitThisAttack)
                ApplySourceModelZephyrChargedBasicAttack(actor, primary);
        }

        private bool ApplySourceModelZephyrChargedBasicAttack(
            CharacterInstance actor, SpatialCombatEnemy primary)
        {
            SourceModelZephyrChargeState state;
            if (actor.Catalog.Id != "I-10" || actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != "h04G" ||
                actor.Profile == null || actor.Profile.UnitId != "I-10" ||
                actor.Profile.Skills.Count < 3 ||
                !IsSourceModelZephyrChargeSkill(actor.Profile.Skills[2]) ||
                !zephyrCharge.TryGetValue(actor.Id, out state)) return false;
            if (state.ExpiresAt <= Time ||
                state.LaneRevision != SourceModelLaneRevision(actor.Id) ||
                state.StoryLane != IsStoryDeployed(actor.Id))
            {
                zephyrCharge.Remove(actor.Id);
                return false;
            }
            if (!primary.Enemy.IsAlive || !CanAttackInThisLane(actor, primary))
                return false;
            var skill = actor.Profile.Skills[2];
            var effect = skill.Components[1];
            var targets = SourceScriptTargets(actor, primary,
                new SourceScriptHit(SourceScriptHitShape.Circle, skill.Radius.Value,
                    0, 0, 1, 1, Ordr323AttackType.Spells));
            if (targets.Count == 0) return false;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 0.7초 충전 상태에서 평타 추가 타격", 0));
            foreach (var target in targets)
            {
                double amount = effect.Number("amount").Value +
                    (Object.ReferenceEquals(target, primary) ?
                        effect.Number("extraSingleMagicalAmount").Value : 0);
                ApplySourceModelSeventhTimedHit(actor, target, skill.Name,
                    amount, "magical");
            }
            return true;
        }
    }
}
