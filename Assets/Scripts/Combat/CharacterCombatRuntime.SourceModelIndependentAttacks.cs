using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // These autonomous actors share the *existing* scheduler, dice resolver,
    // HP/death pipeline and same-lane targeting. They never enter the roster.
    public sealed partial class CharacterCombatRuntime
    {
        private sealed class SourceIndependentAttacker
        {
            public string Key;
            public string Rawcode;
            public bool StoryLane;
            public double X;
            public double Y;
            public double? ExpiresAt;
            public bool Blocked;
        }

        private readonly Dictionary<string, SourceIndependentAttacker> sourceIndependentAttackers =
            new Dictionary<string, SourceIndependentAttacker>(StringComparer.Ordinal);

        private static string SatelliteAttackKey(long id) => "practice-satellite:" + id;
        private static string SummonAttackKey(long id) => "practice-summon:" + id;

        private void RegisterSourceIndependentAttack(string key, string rawcode,
            bool storyLane, double x, double y, double? expiresAt = null)
        {
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y) ||
                (expiresAt.HasValue && expiresAt.Value <= scheduler.Time))
                throw new InvalidOperationException("연습 독립 공격자 생성 조건이 맞지 않습니다.");
            var attack = sourceModelRules.SourceObjectAttack(rawcode);
            if (attack == null) throw new InvalidOperationException("원본 독립 공격 주사위 미확인: " + rawcode);
            // The first full cooldown is a documented practice scheduling
            // convention. The native Warcraft initial attack phase is unknown.
            attackPipeline.RegisterUnit(key, new UnitCombatSpec(rawcode, attack,
                Array.Empty<CombatSkillSpec>(), SourceModelBasicCombatRules.Evidence),
                attack.IntervalSeconds);
            unitStatuses.Add(key, new CombatStatusEngine());
            sourceIndependentAttackers.Add(key, new SourceIndependentAttacker {
                Key = key, Rawcode = rawcode, StoryLane = storyLane,
                X = x, Y = y, ExpiresAt = expiresAt
            });
        }

        private void UnregisterSourceIndependentAttack(string key)
        {
            if (!sourceIndependentAttackers.Remove(key)) return;
            attackPipeline.RemoveUnit(key);
            unitStatuses.Remove(key);
        }

        private bool CanSourceIndependentAttack(SourceIndependentAttacker actor,
            SpatialCombatEnemy enemy)
        {
            if (!enemy.Enemy.IsAlive) return false;
            return actor.StoryLane ? enemy.Kind == CharacterEnemyKind.Story :
                enemy.Kind == CharacterEnemyKind.Line ||
                enemy.Kind == CharacterEnemyKind.Boss ||
                enemy.Kind == CharacterEnemyKind.Enraged;
        }

        private List<TargetCandidate> SourceIndependentTargets(SourceIndependentAttacker actor)
        {
            var candidates = new List<TargetCandidate>();
            foreach (var enemy in enemies.Values)
            {
                if (!CanSourceIndependentAttack(actor, enemy)) continue;
                double dx = enemy.X - actor.X, dy = enemy.Y - actor.Y;
                double distanceSquared = dx * dx + dy * dy;
                if (double.IsInfinity(distanceSquared)) continue;
                candidates.Add(new TargetCandidate(enemy.Id, distanceSquared,
                    enemy.PathProgress, true));
            }
            candidates.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            return candidates;
        }

        private double? NextSourceIndependentAttackAt()
        {
            double? next = null;
            foreach (var actor in sourceIndependentAttackers.Values)
            {
                if (actor.Blocked || SourceIndependentTargets(actor).Count == 0) continue;
                double? at = attackPipeline.NextAttackAt(actor.Key);
                if (!at.HasValue || (actor.ExpiresAt.HasValue &&
                    !CombatScheduler.HasReached(actor.ExpiresAt.Value, at.Value))) continue;
                if (!next.HasValue || at.Value < next.Value) next = at;
            }
            return next;
        }

        private int ExecuteReadySourceIndependentAttacks(int budget)
        {
            int fired = 0;
            var keys = new List<string>(sourceIndependentAttackers.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                if (fired >= budget) break;
                SourceIndependentAttacker actor;
                if (!sourceIndependentAttackers.TryGetValue(key, out actor) || actor.Blocked)
                    continue;
                var due = attackPipeline.NextAttackAt(key);
                if (!due.HasValue || !CombatScheduler.HasReached(scheduler.Time, due.Value))
                    continue;
                var candidates = SourceIndependentTargets(actor);
                if (candidates.Count == 0) continue;
                BasicAttackResult hit;
                try
                {
                    if (!attackPipeline.TryBasicAttack(key, candidates, out hit)) continue;
                }
                catch (NotSupportedException ex)
                {
                    actor.Blocked = true;
                    events.Add(new CharacterCombatEvent(
                        CharacterCombatEventType.EffectDeferred, key, null, null,
                        "MODEL_ONLY 독립 평타 피해 미연결: " + ex.Message, 0));
                    continue;
                }
                fired++;
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.Attack,
                    key, hit.TargetId, null, "MODEL_ONLY " + actor.Rawcode + " 기본 공격", hit.ActualDamage));
                SpatialCombatEnemy victim;
                if (enemies.TryGetValue(hit.TargetId, out victim) && !victim.Enemy.IsAlive)
                    events.Add(new CharacterCombatEvent(CharacterCombatEventType.EnemyDefeated,
                        key, hit.TargetId, null, "MODEL_ONLY 독립 공격 처치", 0));
            }
            RemoveDefeatedEnemies();
            return fired;
        }
    }
}
