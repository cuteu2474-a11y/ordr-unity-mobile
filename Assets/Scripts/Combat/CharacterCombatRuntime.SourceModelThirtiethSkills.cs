using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // These are event-driven tooltip practice states, not native JASS
        // timing, Warcraft poison precedence or measured attack-speed parity.
        private sealed class KatakuriSpeedState
        {
            public double ExpiresAt;
            public long LaneRevision;
            public bool StoryLane;
        }

        private sealed class KingBasicDebuffState
        {
            public long OwnerId;
            public SpatialCombatEnemy Victim;
            public bool StoryLane;
            public long LaneRevision;
            public double ExpiresAt;
        }

        private readonly Dictionary<long, KatakuriSpeedState> thirtiethKatakuriSpeed =
            new Dictionary<long, KatakuriSpeedState>();
        private readonly Dictionary<string, KingBasicDebuffState> thirtiethKingBasic =
            new Dictionary<string, KingBasicDebuffState>(StringComparer.Ordinal);

        private void SourceModelThirtiethForgetOwner(long owner)
        {
            thirtiethKatakuriSpeed.Remove(owner);
            string prefix = InstanceKey(owner) + ":";
            var expired = new List<string>();
            foreach (string key in thirtiethKingBasic.Keys)
                if (key.StartsWith(prefix, StringComparison.Ordinal)) expired.Add(key);
            foreach (string key in expired) thirtiethKingBasic.Remove(key);
        }

        private void SourceModelThirtiethPruneDefeated()
        {
            var expired = new List<string>();
            foreach (var pair in thirtiethKingBasic)
            {
                SpatialCombatEnemy current;
                if (!characters.ContainsKey(pair.Value.OwnerId) ||
                    !enemies.TryGetValue(pair.Value.Victim.Id, out current) ||
                    !ReferenceEquals(current, pair.Value.Victim) ||
                    !current.Enemy.IsAlive)
                    expired.Add(pair.Key);
            }
            foreach (string key in expired) thirtiethKingBasic.Remove(key);
            var removed = new List<long>();
            foreach (var pair in thirtiethKatakuriSpeed)
                if (!characters.ContainsKey(pair.Key) || pair.Value.ExpiresAt <= Time ||
                    pair.Value.LaneRevision != SourceModelLaneRevision(pair.Key) ||
                    pair.Value.StoryLane != IsStoryDeployed(pair.Key))
                    removed.Add(pair.Key);
            foreach (long id in removed) thirtiethKatakuriSpeed.Remove(id);
        }

        private void ActivateSourceModelThirtiethKatakuriSpeed(
            CharacterInstance actor, ImportedCombatSkill skill)
        {
            if (actor.Catalog.Id != "LI-08" ||
                actor.Mapping.AttackSpec.UnitRawCode != "h07I" ||
                !SourceModelTwentyNinthSkillCatalog.HasExactApprovedSubset(actor.Profile) ||
                !SourceModelTwentyNinthSkillCatalog.CanModelExact(skill) ||
                actor.Abilities.Resources == null ||
                actor.Abilities.Resources.Health < skill.Number("requiresHealthAtLeast").Value ||
                !actor.Abilities.Resources.CanSpend(skill.Number("healthCost").Value, 0))
                return;
            actor.Abilities.Resources.Spend(skill.Number("healthCost").Value, 0);
            double seconds = skill.Components[0].Number("durationSeconds").Value;
            thirtiethKatakuriSpeed[actor.Id] = new KatakuriSpeedState {
                ExpiresAt = Time + seconds,
                LaneRevision = SourceModelLaneRevision(actor.Id),
                StoryLane = IsStoryDeployed(actor.Id)
            };
            string actorKey = InstanceKey(actor.Id);
            string statusKey = "practice-tooltip:katakuri-max-speed:" + actorKey;
            var status = unitStatuses[actorKey];
            status.RemoveSource(statusKey);
            // This is the source model's EXISTING 400% speed cap; not a new
            // Warcraft limit or an invented imported tooltip percentage.
            status.Apply(new CombatEffect(statusKey, CombatEffectType.AttackSpeedBuff,
                CombatEffectOrigin.TriggeredSkill, 400, seconds));
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                actorKey, actorKey, skill.Name,
                "TOOLTIP_MODEL_ONLY / HP 17 소모, 최대 공속 1.5초(연습 상한)", 0));
        }

        private void OnSourceModelThirtiethSuccessfulBasicHit(string attackerId,
            string rawcode, string targetId, long actual)
        {
            if (!UsesSourceModelBasicAttacks || !processingStep || actual <= 0 ||
                (rawcode != "h07I" && rawcode != "h0AH")) return;
            long id;
            CharacterInstance actor;
            SpatialCombatEnemy primary;
            if (!long.TryParse(attackerId, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out id) ||
                !characters.TryGetValue(id, out actor) || !actor.AttackReady ||
                actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != rawcode ||
                !SourceModelTwentyNinthSkillCatalog.HasExactApprovedSubset(actor.Profile) ||
                !enemies.TryGetValue(targetId, out primary) || !primary.Enemy.IsAlive ||
                !CanAttackInThisLane(actor, primary)) return;

            if (rawcode == "h07I" && actor.Catalog.Id == "LI-08")
            {
                KatakuriSpeedState active;
                if (!thirtiethKatakuriSpeed.TryGetValue(id, out active) ||
                    active.ExpiresAt <= Time ||
                    active.LaneRevision != SourceModelLaneRevision(id) ||
                    active.StoryLane != IsStoryDeployed(id)) return;
                var skill = actor.Profile.Skills[0];
                if (!SourceModelTwentyNinthSkillCatalog.CanModelExact(skill)) return;
                var component = skill.Components[1];
                double damage = actor.Mapping.AttackSpec.BasicAttack.NominalAttackPower *
                    component.Number("basicAttackPercent").Value / 100 +
                    component.Number("flatAmount").Value;
                var targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                    SourceScriptHitShape.Circle, skill.Radius.Value,
                    0, 0, 1, 1, Ordr323AttackType.Spells));
                foreach (var target in targets)
                    ApplySourceModelSeventhTimedHit(actor, target,
                        skill.Name, damage, "physical");
                return;
            }
            if (rawcode != "h0AH" || actor.Catalog.Id != "L-34") return;
            var passive = actor.Profile.Passives[2];
            double seconds = passive.Number("durationSeconds").Value;
            var status = primary.Enemy.Status;
            string source = "practice-tooltip:king-basic-slow:" + attackerId;
            status.RemoveSource(source);
            status.Apply(new CombatEffect(source, CombatEffectType.MovementSlowPercent,
                CombatEffectOrigin.TriggeredSkill,
                passive.Number("movementSlowPercent").Value, seconds));
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                attackerId, primary.Id, "킹 평타 둔화",
                "TOOLTIP_MODEL_ONLY / 둔화 45% · 3초 / 원본 독약 배타 미검증", 0));
            string key = attackerId + ":" + primary.Id;
            KingBasicDebuffState running;
            if (thirtiethKingBasic.TryGetValue(key, out running) &&
                ReferenceEquals(running.Victim, primary) &&
                running.LaneRevision == SourceModelLaneRevision(id) &&
                running.StoryLane == IsStoryDeployed(id))
            {
                // Refresh lifetime, never reset an already running 1s tick.
                running.ExpiresAt = Time + seconds;
                return;
            }
            running = new KingBasicDebuffState {
                OwnerId = id,
                Victim = primary,
                LaneRevision = SourceModelLaneRevision(id),
                StoryLane = IsStoryDeployed(id),
                ExpiresAt = Time + seconds
            };
            thirtiethKingBasic[key] = running;
            ScheduleSourceModelThirtiethKingBasicTick(actor, running, key);
        }

        private void ScheduleSourceModelThirtiethKingBasicTick(
            CharacterInstance actor, KingBasicDebuffState state,
            string key)
        {
            string attacker = InstanceKey(actor.Id);
            effectExecutor.SchedulePracticeSourceAttack(attacker, state.Victim.Id, 1,
                () => {
                    KingBasicDebuffState currentState;
                    SpatialCombatEnemy current;
                    if (!thirtiethKingBasic.TryGetValue(key, out currentState) ||
                        !ReferenceEquals(state, currentState) ||
                        !enemies.TryGetValue(state.Victim.Id, out current) ||
                        !ReferenceEquals(state.Victim, current) ||
                        !state.Victim.Enemy.IsAlive ||
                        !CanFinishSourceModelDelayedAttack(actor.Id,
                            state.StoryLane, state.LaneRevision) ||
                        Time > state.ExpiresAt + .000001) return;
                    ApplySourceModelSeventhTimedHit(actor, state.Victim,
                        "킹 평타 지속 피해",
                        actor.Profile.Passives[2].Number("magicalDamagePerSecond").Value,
                        "magical");
                    if (state.Victim.Enemy.IsAlive &&
                        Time + 1 <= state.ExpiresAt + .000001)
                        ScheduleSourceModelThirtiethKingBasicTick(actor,
                            state, key);
                    else
                        thirtiethKingBasic.Remove(key);
                });
        }

        private void ExecuteSourceModelThirtiethKatakuriApproach(
            CharacterInstance actor, ImportedCombatSkill skill)
        {
            if (actor.Catalog.Id != "LI-08" ||
                actor.Mapping.AttackSpec.UnitRawCode != "h07I" ||
                !SourceModelTwentyNinthSkillCatalog.HasExactApprovedSubset(actor.Profile) ||
                !SourceModelTwentyNinthSkillCatalog.CanModelKatakuriApproach(skill))
                return;
            double radius = skill.Number("triggerRadius").Value;
            var origin = roster.Get(actor.Id);
            var ids = new List<string>(enemies.Keys);
            ids.Sort(StringComparer.Ordinal);
            foreach (string enemyId in ids)
            {
                SpatialCombatEnemy primary;
                if (!enemies.TryGetValue(enemyId, out primary) ||
                    !primary.Enemy.IsAlive || !CanAttackInThisLane(actor, primary))
                    continue;
                string key = actor.Id + ":" + skill.Id + ":" + primary.Id;
                double dx = origin.X - primary.X, dy = origin.Y - primary.Y;
                if (dx * dx + dy * dy > radius * radius)
                {
                    sourceModelEighthWithinApproach.Remove(key);
                    continue;
                }
                if (!sourceModelEighthWithinApproach.Add(key)) continue;
                bool privileged = primary.Kind == CharacterEnemyKind.Boss ||
                    primary.Kind == CharacterEnemyKind.Enraged;
                double chance = privileged ? 100 : skill.ChancePercent.Value;
                if (chance < 100 && sourceModelRules.NextScriptRoll(1000000) >=
                    (int)Math.Round(chance * 10000,
                        MidpointRounding.AwayFromZero)) continue;
                var targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                    SourceScriptHitShape.Circle, skill.Number("targetRadius").Value,
                    0, 0, 1, 1, Ordr323AttackType.Spells));
                if (targets.Count == 0) continue;
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                    InstanceKey(actor.Id), primary.Id, skill.Name,
                    privileged ? "TOOLTIP_MODEL_ONLY / 보스·광폭화 접근 100%" :
                        "TOOLTIP_MODEL_ONLY / 일반 접근 20%", 0));
                foreach (var target in targets)
                {
                    ApplySourceModelSeventhTimedHit(actor, target, skill.Name,
                        skill.Components[0].Number("amount").Value, "physical");
                    if (ReferenceEquals(primary, target))
                        ApplySourceModelSeventhTimedHit(actor, target, skill.Name,
                            skill.Components[0].Number("extraSinglePhysicalAmount").Value,
                            "physical");
                }
                ApplySourceModelCompositeArmorBreak(actor, primary, skill,
                    skill.Components[1], targets);
            }
        }
    }
}
