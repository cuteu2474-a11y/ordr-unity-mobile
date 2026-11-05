using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // Native CKe: A134 + A13C persist on the original target. A13C/AIas
        // DataA1=.30 is modeled by the existing native attack-cadence lookup.
        // A134's native Aimp row has NO active data, cast range or trigger.
        // It is an inert marker. Do not invent a passive impale proc. The one-shot CKd/CKc burst uses victim max HP, NOT the
        // green-blood target's max HP.
        private static string GreenBloodEnhancementSource(long instanceId) =>
            "story-native:CKe:A134+A13C:" + instanceId;

        public bool HasOriginal323GreenBloodEnhancement(long instanceId)
        {
            CombatStatusEngine status;
            if (!characters.ContainsKey(instanceId) ||
                !unitStatuses.TryGetValue(InstanceKey(instanceId), out status))
                return false;
            foreach (var effect in status.Snapshot())
                if (effect.SourceId == GreenBloodEnhancementSource(instanceId) &&
                    effect.Type == CombatEffectType.AttackSpeedBuff)
                    return true;
            return false;
        }

        public bool ApplyOriginal323GreenBloodEnhancement(long instanceId)
        {
            if (game.Phase != SessionPhase.Running || processingStep ||
                activeOriginalFusionSwap != null ||
                !characters.ContainsKey(instanceId) ||
                HasOriginal323GreenBloodEnhancement(instanceId))
                return false;
            var status = UnitStatusOf(instanceId);
            string key = GreenBloodEnhancementSource(instanceId);
            bool sourceStoryLane = IsStoryDeployed(instanceId);
            long sourceLaneRevision = SourceModelLaneRevision(instanceId);
            status.Apply(new CombatEffect(key, CombatEffectType.AttackSpeedBuff,
                CombatEffectOrigin.Aura,
                Original323StoryGreenBloodRules.EnhancementAttackSpeedPercent, 0));
            try
            {
                // CKd waits .03 seconds before CKc's 825-radius pulse.
                if (UsesSourceModelBasicAttacks)
                    effectExecutor.SchedulePracticeSourceAttack(InstanceKey(instanceId),
                        "story-native:CKd:green-blood-burst", .03, () =>
                        {
                            if (!CanFinishSourceModelDelayedAttack(
                                    instanceId, sourceStoryLane, sourceLaneRevision) ||
                                !HasOriginal323GreenBloodEnhancement(instanceId)) return;
                            // CKd reads the target unit's location AFTER
                            // its .03-second wait, not at item use time.
                            var current = roster.Get(instanceId);
                            ApplyOriginal323GreenBloodBurst(instanceId,
                                current.X, current.Y, sourceStoryLane);
                        });
            }
            catch
            {
                status.RemoveSource(key);
                throw;
            }
            var applied = new CharacterCombatEvent(
                CharacterCombatEventType.StatusApplied, InstanceKey(instanceId),
                InstanceKey(instanceId), "그린블러드 강화",
                "원본 CKe A134/A13C · A13C 공속 +30% · A134 원본 능력 소유",
                0);
            if (processingStep) events.Add(applied);
            else pendingCommandEvents.Add(applied);
            return true;
        }

        private void ApplyOriginal323GreenBloodBurst(long instanceId,
            double centerX, double centerY, bool storyLane)
        {
            string actorId = InstanceKey(instanceId);
            var destination = processingStep ? events : pendingCommandEvents;
            var affected = new List<SpatialCombatEnemy>();
            foreach (var target in enemies.Values)
            {
                if (!target.Enemy.IsAlive ||
                    (storyLane ? target.Kind != CharacterEnemyKind.Story :
                    target.Kind != CharacterEnemyKind.Line &&
                    target.Kind != CharacterEnemyKind.Boss &&
                    target.Kind != CharacterEnemyKind.Enraged)) continue;
                affected.Add(target);
            }
            affected.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            foreach (var target in affected)
            {
                double dx = target.X - centerX, dy = target.Y - centerY;
                if (dx * dx + dy * dy >
                    Original323StoryGreenBloodRules.EnhancementBurstRadius *
                    Original323StoryGreenBloodRules.EnhancementBurstRadius) continue;
                // CKc: Bcb(..., 400000 + GetUnitState(RN, MAX_LIFE)*.4*.01,
                // 1,1, ATTACK_TYPE_CHAOS, DAMAGE_TYPE_UNIVERSAL). Universal
                // damage bypasses armor/magic-resistance adjustments.
                double raw = Original323StoryGreenBloodRules.EnhancementBurstDamage(
                    target.Enemy.MaxHealth);
                if (raw >= long.MaxValue)
                    continue;
                long actual;
                if (!resolution.TryApplySkillDamage(actorId, target.Id,
                        (long)Math.Floor(raw), out actual))
                    continue;
                destination.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillDamage, actorId, target.Id,
                    "그린블러드 CKc", "원본 CHAOS/UNIVERSAL 825범위", actual));
                if (!target.Enemy.IsAlive)
                    destination.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EnemyDefeated, actorId, target.Id,
                        "그린블러드 CKc", "원본 강화 폭발 처치", actual));
            }
            RemoveDefeatedEnemies();
        }
    }
}
