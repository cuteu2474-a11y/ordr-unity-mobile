using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private IReadOnlyList<CombatPathPoint> sourceModelRouteCorners;
        private Func<int?> sourceModelAccumulatedClearCount;

        // The practice battle supplies its actual rectangle route. Choose the
        // segment hitting the most live enemies; a caster's location never
        // determines Rankyaku's direction. Keep story/main lanes separate.
        public void ConfigureSourceModelRouteCorners(IReadOnlyList<CombatPathPoint> corners)
        {
            if (!UsesSourceModelBasicAttacks || corners == null || corners.Count < 2)
                throw new ArgumentException("연습 라인 경로가 필요합니다.", nameof(corners));
            sourceModelRouteCorners = new List<CombatPathPoint>(corners).AsReadOnly();
        }

        // Profile clearCountScaling is cumulative *completed games*, not
        // in-match round count. Missing persistence is treated as zero.
        public void ConfigureSourceModelAccumulatedClearCount(Func<int?> provider)
        {
            sourceModelAccumulatedClearCount = provider;
        }

        private int SourceModelLucciClears()
        {
            int? count = sourceModelAccumulatedClearCount?.Invoke();
            return Math.Max(0, Math.Min(300, count ?? 0));
        }

        private double SourceModelFifthProcChance(ImportedCombatSkill skill)
        {
            // Guaranteed on-every-attack and conditional packets do not
            // declare chancePercent. The onAttack model is validated at
            // dispatch, so an absent chance there must never create a proc.
            double chance = skill.ChancePercent ??
                (skill.Trigger == "onAttack" || skill.Trigger == "onAttackWithMana"
                    ? 0d : 100d);
            if (skill.Id == "TR-07:skill:1" || skill.Id == "TR-07:skill:3")
                chance = Math.Min(skill.Number("maxProcChancePercent") ?? 100,
                    chance + SourceModelLucciClears() * .0025);
            return chance;
        }

        private List<SpatialCombatEnemy> SourceModelRankyakuTargets(
            CharacterInstance actor, SpatialCombatEnemy primary, double halfWidth)
        {
            var best = new List<SpatialCombatEnemy>();
            if (sourceModelRouteCorners == null)
                return best; // Don't fall back to caster-centered direction.
            for (int i = 0; i < sourceModelRouteCorners.Count; i++)
            {
                var start = sourceModelRouteCorners[i];
                var end = sourceModelRouteCorners[(i + 1) % sourceModelRouteCorners.Count];
                double dx = end.X - start.X, dy = end.Y - start.Y;
                double lengthSq = dx * dx + dy * dy;
                if (lengthSq <= 0) continue;
                var hits = new List<SpatialCombatEnemy>();
                foreach (var candidate in enemies.Values)
                {
                    if (!candidate.Enemy.IsAlive ||
                        !CanAttackInThisLane(actor, candidate)) continue;
                    double vx = candidate.X - start.X, vy = candidate.Y - start.Y;
                    double projection = (vx * dx + vy * dy) / lengthSq;
                    double perpendicular = Math.Abs(vx * dy - vy * dx) /
                        Math.Sqrt(lengthSq);
                    if (projection >= 0 && projection <= 1 && perpendicular <= halfWidth)
                        hits.Add(candidate);
                }
                if (hits.Count > best.Count ||
                    (hits.Count == best.Count && hits.Contains(primary) &&
                     !best.Contains(primary)))
                    best = hits;
            }
            best.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            return best;
        }

        private List<SpatialCombatEnemy> SourceModelRobinMultishotTargets(
            CharacterInstance actor, SpatialCombatEnemy primary)
        {
            var targets = new List<SpatialCombatEnemy> { primary };
            var extras = new List<SpatialCombatEnemy>();
            foreach (var candidate in enemies.Values)
                if (candidate.Id != primary.Id && candidate.Enemy.IsAlive &&
                    CanAttackInThisLane(actor, candidate)) extras.Add(candidate);
            extras.Sort((a, b) => {
                double ad = (a.X - primary.X) * (a.X - primary.X) +
                    (a.Y - primary.Y) * (a.Y - primary.Y);
                double bd = (b.X - primary.X) * (b.X - primary.X) +
                    (b.Y - primary.Y) * (b.Y - primary.Y);
                int compare = ad.CompareTo(bd);
                return compare != 0 ? compare :
                    StringComparer.Ordinal.Compare(a.Id, b.Id);
            });
            for (int i = 0; i < Math.Min(2, extras.Count); i++)
                targets.Add(extras[i]);
            return targets;
        }

        private sealed class SourceModelAceFlame
        {
            public long Owner;
            public string Target;
            public long Generation;
            public double DamagePerTick;
            public double ExpiresAt;
        }

        private readonly Dictionary<string, SourceModelAceFlame> sourceModelAceFlames =
            new Dictionary<string, SourceModelAceFlame>(StringComparer.Ordinal);
        private long sourceModelAceFlameGeneration;

        private void RemoveSourceModelAceFlames(long owner)
        {
            var keys = new List<string>();
            foreach (var entry in sourceModelAceFlames)
                if (entry.Value.Owner == owner) keys.Add(entry.Key);
            foreach (string key in keys) sourceModelAceFlames.Remove(key);
        }

        private void RemoveSourceModelAceFlamesForTarget(string targetId)
        {
            var keys = new List<string>();
            foreach (var entry in sourceModelAceFlames)
                if (entry.Value.Target == targetId) keys.Add(entry.Key);
            foreach (string key in keys) sourceModelAceFlames.Remove(key);
        }

        private void SourceModelAceStun(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill, double duration)
        {
            if (!primary.Enemy.IsAlive || !CanAttackInThisLane(actor, primary)) return;
            string source = "practice-ace-flame:" + actor.Id;
            primary.Enemy.Status.RemoveSource(source);
            primary.Enemy.Status.Apply(new CombatEffect(source,
                CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill, 1, duration));
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                InstanceKey(actor.Id), primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 발동형 불꽃 단일 기절", 0));
        }

        private void SourceModelAceFlameOnSkill(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            IReadOnlyList<SpatialCombatEnemy> targets)
        {
            double damage, duration;
            switch (skill.Id)
            {
                case "E-07:skill:3": damage = 120000; duration = 2; break;
                case "E-07:skill:2": damage = 200000; duration = 1.6; break;
                case "E-07:skill:0": damage = 250000; duration = 3; break;
                default: return;
            }
            SourceModelAceStun(actor, primary, skill, 3.5);
            bool lane = IsStoryDeployed(actor.Id);
            long revision = SourceModelLaneRevision(actor.Id);
            foreach (var target in targets)
            {
                if (!target.Enemy.IsAlive || !CanAttackInThisLane(actor, target)) continue;
                // Keep each source's remaining lifetime. Suppress weaker
                // ticks only while a stronger flame is actually active.
                string key = actor.Id + ":" + target.Id + ":" + skill.Id;
                var current = new SourceModelAceFlame {
                    Owner = actor.Id, Target = target.Id,
                    Generation = ++sourceModelAceFlameGeneration,
                    DamagePerTick = damage, ExpiresAt = Time + duration
                };
                sourceModelAceFlames[key] = current;
                int count = (int)Math.Round(duration / .2, MidpointRounding.AwayFromZero);
                for (int tick = 1; tick <= count; tick++)
                {
                    effectExecutor.SchedulePracticeSourceAttack(InstanceKey(actor.Id),
                        target.Id, tick * .2, () => {
                            SourceModelAceFlame active;
                            SpatialCombatEnemy enemy;
                            if (!sourceModelAceFlames.TryGetValue(key, out active) ||
                                active.Generation != current.Generation ||
                                !CanFinishSourceModelDelayedAttack(actor.Id, lane, revision) ||
                                !enemies.TryGetValue(target.Id, out enemy) ||
                                !ReferenceEquals(enemy, target) || !target.Enemy.IsAlive ||
                                !CanAttackInThisLane(actor, target)) return;
                            foreach (var other in sourceModelAceFlames.Values)
                                if (other.Target == target.Id &&
                                    other.ExpiresAt > effectExecutor.Time + .00001 &&
                                    other.DamagePerTick > current.DamagePerTick)
                                    return;
                            Ordr323ArmorType armor;
                            if (!target.Enemy.Defenses.BaseArmor.HasValue ||
                                !Ordr323OriginalDamageRules.TryReadArmorType(
                                    target.Enemy.Defenses.WarcraftArmorType, out armor)) return;
                            double modeled = Ordr323MapDamageMath.ApplyVerifiedFactors(
                                current.DamagePerTick, Ordr323AttackType.Spells, armor,
                                true, true, target.Enemy.Defenses.BaseArmor.Value -
                                SourceModelUnitEffects.ResolvePracticeArmorReduction(
                                    target.Enemy.Status.Snapshot()) -
                                ArmorBreakStacksOf(target.Id), false, 0, 1);
                            modeled *= SourceModelCompositeMagicFactor(target, "magical");
                            long actual;
                            if (resolution.TryApplySkillDamage(InstanceKey(actor.Id),
                                target.Id, (long)Math.Floor(modeled), out actual))
                            {
                                events.Add(new CharacterCombatEvent(
                                    CharacterCombatEventType.SkillDamage,
                                    InstanceKey(actor.Id), target.Id,
                                    "발동형 불꽃 지속 피해", "TOOLTIP_MODEL_ONLY / 최강 화염 도트", actual));
                                if (!target.Enemy.IsAlive)
                                    events.Add(new CharacterCombatEvent(
                                        CharacterCombatEventType.EnemyDefeated,
                                        InstanceKey(actor.Id), target.Id,
                                        "발동형 불꽃 지속 피해", "연습 화염 처치", actual));
                            }
                            if (Math.Abs(effectExecutor.Time - current.ExpiresAt) < .00001)
                                sourceModelAceFlames.Remove(key);
                        });
                }
            }
        }

        private void SourceModelAceDelayedUltimate(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            IReadOnlyList<SpatialCombatEnemy> targets)
        {
            // Activation statuses start now; the 2s hit and its second stun
            // run on the same scheduler as all other delayed attacks.
            string attacker = InstanceKey(actor.Id);
            string source = "practice-tooltip-stun:" + attacker + ":" + skill.Id;
            foreach (var target in targets)
            {
                if (target.Kind != CharacterEnemyKind.Line ||
                    !target.Enemy.IsAlive || !CanAttackInThisLane(actor, target))
                    continue;
                target.Enemy.Status.RemoveSource(source);
                target.Enemy.Status.Apply(new CombatEffect(source,
                    CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill,
                    1, skill.Components[1].Number("durationSeconds").Value));
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                    attacker, target.Id, skill.Name,
                    "TOOLTIP_MODEL_ONLY / 대염계 시전 직후 기절", 0));
            }
            SourceModelAceFlameOnSkill(actor, primary, skill, targets);
            bool lane = IsStoryDeployed(actor.Id);
            long revision = SourceModelLaneRevision(actor.Id);
            effectExecutor.SchedulePracticeSourceAttack(InstanceKey(actor.Id),
                primary.Id, skill.Number("damageDelaySeconds").Value, () => {
                    if (!CanFinishSourceModelDelayedAttack(actor.Id, lane, revision)) return;
                    ApplySourceModelNextThirtySkill(actor, primary, skill, targets);
                });
        }
    }
}
