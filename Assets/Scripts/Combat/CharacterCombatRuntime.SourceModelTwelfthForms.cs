using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Source tooltip follow-ups for the existing SAME-INSTANCE form engine.
    // The imported profile specifies damage, chances and durations; it does
    // not certify Warcraft's RNG phase or native immunity calculations.
    public sealed partial class CharacterCombatRuntime
    {
        private readonly HashSet<string> sourceModelBonneyAgeTriggered =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<long>> sourceModelBonneyRootOwner =
            new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        private readonly HashSet<string> sourceModelBonneyShrunk =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<long, double> sourceModelBonneyStunReadyAt =
            new Dictionary<long, double>();
        private readonly Dictionary<string, long> sourceModelKingSlowRevision =
            new Dictionary<string, long>(StringComparer.Ordinal);

        public int PracticeBonneyAgeStacksOf(long owner, string enemyId)
        {
            int count;
            return UsesSourceModelBasicAttacks && characters.ContainsKey(owner) &&
                enemyId != null && sourceModelBonneyAge.TryGetValue(
                    InstanceKey(owner) + ":" + enemyId, out count) ? count : 0;
        }

        public bool PracticeBonneyScaleReduced(string enemyId) =>
            UsesSourceModelBasicAttacks && enemyId != null &&
            sourceModelBonneyShrunk.Contains(enemyId);

        // The tooltip's root has no duration. Keep ordinary line targets
        // rooted for their remaining life in this reference model; never
        // fabricate a seconds value or apply root to bosses.
        private bool SourceModelTwelfthBonneyRooted(string enemyId) =>
            enemyId != null && sourceModelBonneyRootOwner.ContainsKey(enemyId);

        private void SourceModelTwelfthForgetOwner(long owner)
        {
            sourceModelBonneyStunReadyAt.Remove(owner);
            string prefix = InstanceKey(owner) + ":";
            sourceModelBonneyAgeTriggered.RemoveWhere(key =>
                key.StartsWith(prefix, StringComparison.Ordinal));
            var removed = new List<string>();
            foreach (var pair in sourceModelBonneyRootOwner)
                if (pair.Value.Remove(owner) && pair.Value.Count == 0)
                    removed.Add(pair.Key);
            foreach (var enemy in removed)
            {
                sourceModelBonneyRootOwner.Remove(enemy);
                sourceModelBonneyShrunk.Remove(enemy);
            }
            removed.Clear();
            foreach (var pair in sourceModelKingSlowRevision)
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                    removed.Add(pair.Key);
            foreach (var key in removed) sourceModelKingSlowRevision.Remove(key);
        }

        private void SourceModelTwelfthPruneDefeated()
        {
            sourceModelBonneyAgeTriggered.RemoveWhere(key => {
                int split = key.IndexOf(':');
                return split < 0 || !enemies.ContainsKey(key.Substring(split + 1));
            });
            var gone = new List<string>();
            foreach (var enemy in sourceModelBonneyRootOwner.Keys)
                if (!enemies.ContainsKey(enemy)) gone.Add(enemy);
            foreach (var enemy in gone)
            {
                sourceModelBonneyRootOwner.Remove(enemy);
                sourceModelBonneyShrunk.Remove(enemy);
            }
            gone.Clear();
            foreach (var pair in sourceModelKingSlowRevision)
            {
                int split = pair.Key.IndexOf(':');
                if (split < 0 || !enemies.ContainsKey(pair.Key.Substring(split + 1)))
                    gone.Add(pair.Key);
            }
            foreach (var key in gone) sourceModelKingSlowRevision.Remove(key);
        }

        private static bool SourceModelTwelfthBonneyAgePacket(
            ImportedCombatSkill skill, bool breakthrough)
        {
            if (!SourceModelEleventhSkill(skill,
                    breakthrough ? "TR-21:skill:5" : "TR-21:skill:6",
                    breakthrough ? "세월관통" : "니카펀치",
                    "onAgeStacksReached", breakthrough ? 3 : 1) ||
                skill.Number("requiredStacks") != 40) return false;
            var damage = skill.Components[0];
            var lineOnly = damage.Get("onlyTargetKinds") as List<object>;
            var rootOnly = breakthrough ?
                skill.Components[1].Get("onlyTargetKinds") as List<object> : null;
            return damage.Type == "magical" &&
                damage.Number("amount") == (breakthrough ? 500000 : 150000) &&
                damage.Number("plusTargetCurrentHealthPercent") == (breakthrough ? 8 : 12) &&
                (breakthrough ?
                    skill.Components[1].Type == "root" &&
                    rootOnly != null && rootOnly.Count == 1 &&
                    (rootOnly[0] as string) == "line" &&
                    skill.Components[2].Type == "scaleReduction" &&
                    skill.Components[2].Number("percent") == 40 :
                    skill.Target == "single" &&
                    lineOnly != null && lineOnly.Count == 1 &&
                    (lineOnly[0] as string) == "line");
        }

        private void SourceModelTwelfthBonneyAgeAdded(
            CharacterInstance actor, SpatialCombatEnemy target,
            int previous, int total)
        {
            if (previous >= 40 || total < 40 || !target.Enemy.IsAlive) return;
            string key = InstanceKey(actor.Id) + ":" + target.Id;
            if (!sourceModelBonneyAgeTriggered.Add(key)) return;
            var breakthrough = FindSourceSkill(actor, "세월관통");
            var punch = FindSourceSkill(actor, "니카펀치");
            if (!SourceModelTwelfthBonneyAgePacket(breakthrough, true) ||
                !SourceModelTwelfthBonneyAgePacket(punch, false))
            {
                sourceModelBonneyAgeTriggered.Remove(key);
                return;
            }
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), target.Id, breakthrough.Name,
                "TOOLTIP_MODEL_ONLY / 동일 대상 세월40스택 달성", total));
            ApplySourceModelSeventhTimedHit(actor, target, breakthrough.Name,
                500000 + target.Enemy.Health * .08, "magical");
            if (target.Enemy.IsAlive && target.Kind == CharacterEnemyKind.Line)
            {
                HashSet<long> owners;
                if (!sourceModelBonneyRootOwner.TryGetValue(target.Id, out owners))
                {
                    owners = new HashSet<long>();
                    sourceModelBonneyRootOwner.Add(target.Id, owners);
                }
                owners.Add(actor.Id);
                sourceModelBonneyShrunk.Add(target.Id);
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                    InstanceKey(actor.Id), target.Id, breakthrough.Name,
                    "TOOLTIP_MODEL_ONLY / 일반 몬스터 이동 봉쇄·크기40% 감소, 유지시간 원본 미기재", 0));
                ApplySourceModelSeventhTimedHit(actor, target, punch.Name,
                    150000 + target.Enemy.Health * .12, "magical");
            }
            RemoveDefeatedEnemies();
        }

        private void SourceModelTwelfthBonneyStun(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            var skill = FindSourceSkill(actor, "스턴 광역 마법");
            if (!SourceModelEleventhSkill(skill, "TR-21:skill:4", "스턴 광역 마법",
                    "onAttack", 2) || skill.ChancePercent != 12 ||
                skill.Number("absoluteCooldownSeconds") != 15 ||
                skill.Radius != 600 ||
                skill.Components[0].Type != "magical" ||
                skill.Components[0].Number("amount") != 2650000 ||
                skill.Components[0].Number("plusTargetCurrentHealthPercent") != 4 ||
                skill.Components[1].Type != "stun" ||
                skill.Components[1].Number("durationSeconds") != 1.05) return;
            double ready;
            if (sourceModelBonneyStunReadyAt.TryGetValue(actor.Id, out ready) &&
                effectExecutor.Time < ready) return;
            if (!SourceRollSkill(skill)) return;
            sourceModelBonneyStunReadyAt[actor.Id] = effectExecutor.Time + 15;
            foreach (var enemy in SourceModelEleventhCircle(actor, primary, 600))
            {
                ApplySourceModelSeventhTimedHit(actor, enemy, skill.Name,
                    2650000 + enemy.Enemy.Health * .04, "magical");
                SourceModelEleventhStun(actor, enemy, skill.Name, 1.05);
            }
            RemoveDefeatedEnemies();
        }

        private static bool SourceModelTwelfthKingSlowSkill(ImportedCombatSkill skill)
        {
            return SourceModelEleventhSkill(skill, "LI-10:skill:4", "단일 둔화",
                       "onEveryAttack", 2) && skill.Target == "single" &&
                   skill.Components[0].Type == "magicalDoT" &&
                   skill.Components[0].Number("perSecond") == 300000 &&
                   skill.Components[0].Number("durationSeconds") == 3 &&
                   skill.Components[1].Type == "movementSlow" &&
                   skill.Components[1].Number("percent") == 45 &&
                   skill.Components[1].Number("durationSeconds") == 3;
        }

        private void SourceModelTwelfthKingSlow(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            var skill = FindSourceSkill(actor, "단일 둔화");
            if (!SourceModelTwelfthKingSlowSkill(skill) || !primary.Enemy.IsAlive) return;
            string attacker = InstanceKey(actor.Id);
            string source = "practice-tooltip:" + attacker + ":" + skill.Id + ":movementSlow";
            primary.Enemy.Status.RemoveSource(source);
            primary.Enemy.Status.Apply(new CombatEffect(source,
                CombatEffectType.MovementSlowPercent, CombatEffectOrigin.TriggeredSkill,
                45, 3));
            string key = attacker + ":" + primary.Id;
            long previous;
            sourceModelKingSlowRevision.TryGetValue(key, out previous);
            long revision = checked(previous + 1);
            sourceModelKingSlowRevision[key] = revision;
            bool lane = IsStoryDeployed(actor.Id);
            long laneRevision = SourceModelLaneRevision(actor.Id);
            // Source specifies 300000/second over 3s, without exact tick
            // phase. Three one-second ticks preserve total listed damage;
            // subsequent hits refresh the ongoing single-target spell.
            for (int second = 1; second <= 3; second++)
            {
                effectExecutor.SchedulePracticeSourceAttack(attacker, primary.Id,
                    second, () => {
                        long latest;
                        SpatialCombatEnemy current;
                        if (!CanFinishSourceModelDelayedAttack(actor.Id, lane, laneRevision) ||
                            !sourceModelKingSlowRevision.TryGetValue(key, out latest) ||
                            latest != revision || !enemies.TryGetValue(primary.Id, out current) ||
                            !ReferenceEquals(primary, current)) return;
                        ApplySourceModelSeventhTimedHit(actor, primary,
                            skill.Name, 300000, "magical");
                    });
            }
        }

        private void SourceModelTwelfthKingFire(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            var skill = FindSourceSkill(actor, "화재");
            if (!SourceModelEleventhSkill(skill, "LI-10:skill:3", "화재",
                    "onAttack", 2) || skill.ChancePercent != 14.25 ||
                skill.Radius != 450 ||
                skill.Components[0].Type != "magicalDoT" ||
                skill.Components[0].Number("damagePerTick") != 42500 ||
                skill.Components[0].Number("intervalSeconds") != .27 ||
                skill.Components[0].Number("durationSeconds") != 2.7 ||
                skill.Components[1].Type != "armorReduction" ||
                skill.Components[1].Number("amount") != 20 ||
                skill.Components[1].Number("additionalAmount") != 10 ||
                skill.Components[1].Number("durationSeconds") != 2 ||
                !SourceRollSkill(skill)) return;
            string attacker = InstanceKey(actor.Id);
            bool lane = IsStoryDeployed(actor.Id);
            long revision = SourceModelLaneRevision(actor.Id);
            foreach (var target in SourceModelEleventhCircle(actor, primary, 450))
            {
                string status = "practice-tooltip:" + attacker + ":" +
                    skill.Id + ":armorReduction";
                target.Enemy.Status.RemoveSource(status);
                target.Enemy.Status.Apply(new CombatEffect(status,
                    CombatEffectType.ArmorReduction,
                    CombatEffectOrigin.TriggeredSkill, 20, 2));
                // The profile has '+10 additional' but not its trigger or
                // stacking equation. Keep base -20 live and flag the +10.
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                    attacker, target.Id, skill.Name,
                    "TOOLTIP_MODEL_ONLY / 추가 방어 감소10 발동 조건 미확인", 0));
                for (int tick = 1; tick <= 10; tick++)
                {
                    var victim = target;
                    effectExecutor.SchedulePracticeSourceAttack(attacker, victim.Id,
                        tick * .27, () => {
                            SpatialCombatEnemy current;
                            if (!CanFinishSourceModelDelayedAttack(actor.Id, lane, revision) ||
                                !enemies.TryGetValue(victim.Id, out current) ||
                                !ReferenceEquals(victim, current)) return;
                            ApplySourceModelSeventhTimedHit(actor, victim,
                                skill.Name, 42500, "magical");
                        });
                }
            }
        }
    }
}
