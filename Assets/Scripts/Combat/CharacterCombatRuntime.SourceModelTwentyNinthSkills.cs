using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // The source's 0.27-second burn is a refreshing owner/target effect.
        // The original JASS refresh-vs-stack rule remains unknown; this is
        // explicitly only the conservative mobile practice policy.
        private readonly Dictionary<string, long> sourceModelTwentyNinthFireRevision =
            new Dictionary<string, long>(StringComparer.Ordinal);

        private void SourceModelTwentyNinthForgetOwner(long owner)
        {
            string prefix = InstanceKey(owner) + ":";
            var keys = new List<string>();
            foreach (string key in sourceModelTwentyNinthFireRevision.Keys)
                if (key.StartsWith(prefix, StringComparison.Ordinal)) keys.Add(key);
            foreach (string key in keys) sourceModelTwentyNinthFireRevision.Remove(key);
        }

        private void SourceModelTwentyNinthPruneDefeated()
        {
            var keys = new List<string>();
            foreach (string key in sourceModelTwentyNinthFireRevision.Keys)
            {
                int delimiter = key.IndexOf(':');
                long owner;
                if (delimiter < 0 ||
                    !long.TryParse(key.Substring(0, delimiter), out owner) ||
                    !characters.ContainsKey(owner) ||
                    !enemies.ContainsKey(key.Substring(delimiter + 1)))
                    keys.Add(key);
            }
            foreach (string key in keys) sourceModelTwentyNinthFireRevision.Remove(key);
        }

        private void ApplySourceModelTwentyNinthKingFire(
            CharacterInstance actor, SpatialCombatEnemy primary,
            ImportedCombatSkill skill, IReadOnlyList<SpatialCombatEnemy> targets)
        {
            if (skill.Id != "L-34:skill:0" ||
                !SourceModelTwentyNinthSkillCatalog.HasExactApprovedSubset(actor.Profile))
                return;
            string attacker = InstanceKey(actor.Id);
            bool lane = IsStoryDeployed(actor.Id);
            long laneRevision = SourceModelLaneRevision(actor.Id);
            foreach (var target in targets)
            {
                if (!target.Enemy.IsAlive || !CanAttackInThisLane(actor, target))
                    continue;
                ApplySourceModelSeventhTimedHit(actor, target, skill.Name,
                    skill.Components[0].Number("amount").Value, "magical");
                if (!target.Enemy.IsAlive) continue;
                // The first -20 has no tooltip duration; do not invent one.
                // The +10 is separately timed for exactly two seconds.
                for (int i = 2; i <= 3; i++)
                {
                    var debuff = skill.Components[i];
                    string source = "practice-tooltip:" + attacker + ":" +
                        skill.Id + ":armor" + i;
                    target.Enemy.Status.RemoveSource(source);
                    target.Enemy.Status.Apply(new CombatEffect(source,
                        CombatEffectType.ArmorReduction,
                        // CombatStatusEngine requires Aura for effects without
                        // a documented expiry; using TriggeredSkill with 0
                        // would throw and abort the entire attack batch.
                        debuff.Number("durationSeconds").HasValue ?
                            CombatEffectOrigin.TriggeredSkill : CombatEffectOrigin.Aura,
                        debuff.Number("amount").Value,
                        debuff.Number("durationSeconds") ?? 0));
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.StatusApplied, attacker,
                        target.Id, skill.Name,
                        i == 2 ?
                            "TOOLTIP_MODEL_ONLY / 방어 감소 20, 원본 지속시간 없음" :
                            "TOOLTIP_MODEL_ONLY / 방어 감소 10, 2초", 0));
                }
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.EffectDeferred, attacker,
                    target.Id, skill.Name,
                    "TOOLTIP_MODEL_ONLY / 독약과 방어감소 배타 우선순위 미검증", 0));
                string key = attacker + ":" + target.Id;
                long previous;
                sourceModelTwentyNinthFireRevision.TryGetValue(key, out previous);
                long revision = checked(previous + 1);
                sourceModelTwentyNinthFireRevision[key] = revision;
                for (int tick = 1; tick <= 10; tick++)
                {
                    var victim = target;
                    effectExecutor.SchedulePracticeSourceAttack(attacker, victim.Id,
                        tick * skill.Components[1].Number("tickIntervalSeconds").Value,
                        () => {
                            long latest;
                            SpatialCombatEnemy current;
                            if (!CanFinishSourceModelDelayedAttack(actor.Id, lane,
                                    laneRevision) ||
                                !sourceModelTwentyNinthFireRevision.TryGetValue(
                                    key, out latest) || latest != revision ||
                                !enemies.TryGetValue(victim.Id, out current) ||
                                !ReferenceEquals(victim, current)) return;
                            ApplySourceModelSeventhTimedHit(actor, victim,
                                skill.Name, skill.Components[1].Number(
                                    "damagePerTick").Value, "magical");
                        });
                }
            }
        }
    }
}
