using System;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // A dispel cannot delete the enemy's detrimental statuses. Only
        // explicitly positive effects already present in this shared status
        // engine are removable; unmodeled native Warcraft buffs stay deferred.
        private void RemoveSourceModelTwentyFourthTargetBuffs(
            CharacterInstance actor, SpatialCombatEnemy primary,
            ImportedCombatSkill skill)
        {
            if (skill.Id != "L-20:skill:0" || !primary.Enemy.IsAlive)
                return;
            var status = primary.Enemy.Status;
            int removed = 0;
            foreach (var existing in status.Snapshot())
            {
                if (existing.Type != CombatEffectType.AttackSpeedBuff &&
                    existing.Type != CombatEffectType.AttackPowerBuff &&
                    existing.Type != CombatEffectType.HealthRegeneration)
                    continue;
                status.RemoveSource(existing.SourceId);
                removed++;
            }
            if (removed > 0)
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.StatusApplied,
                    InstanceKey(actor.Id), primary.Id, skill.Name,
                    "TOOLTIP_MODEL_ONLY / 기본 버프 해제 " + removed +
                    "종; 네이티브 버프 데이터 미연결", 0));
        }

        // Original SLK already supplies L-01's 0.5 HP/s and L-20's
        // 0.5 MP/s. Do not add tooltip regeneration a second time.
        // Their separately documented *per basic attack* resource credits
        // are applied once on the existing declaration boundary.
        private void RestoreSourceModelTwentyFourthAttackResources(
            CharacterInstance actor, string rawcode)
        {
            if (actor.Profile == null || actor.Abilities.Resources == null ||
                !sourceModelManagedResourceRows.ContainsKey(actor.Id)) return;
            string skillId;
            if (rawcode == "h02U" && actor.Catalog.Id == "L-01")
                skillId = "L-01:skill:0";
            else if (rawcode == "h02V" && actor.Catalog.Id == "L-20")
                skillId = "L-20:skill:0";
            else return;
            foreach (var skill in actor.Profile.Skills)
            {
                if (skill.Id != skillId ||
                    !SourceModelTwentyFourthSkillCatalog.CanModelExactActive(skill))
                    continue;
                var resource = actor.Abilities.Resources;
                if (skillId == "L-01:skill:0")
                    resource.SetHealth(Math.Min(resource.MaximumHealth,
                        resource.Health + skill.Number("healthRegenPerAttack").Value));
                else
                    resource.SetMana(Math.Min(resource.MaximumMana,
                        resource.Mana + skill.Number("manaRegenPerAttack").Value));
                break;
            }
        }
    }
}
