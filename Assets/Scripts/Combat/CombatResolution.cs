using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // 피해량은 원작 공식 검증 후 외부 계산기에서 전달한다.
    public sealed class CombatEnemy
    {
        public string Id { get; }
        public long MaxHealth { get; }
        public long Health { get; private set; }
        public bool IsAlive => Health > 0;
        public CombatEnemy(string id, long health)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("적 ID가 필요합니다.");
            if (health <= 0) throw new ArgumentOutOfRangeException(nameof(health));
            Id = id; MaxHealth = health; Health = health;
        }
        public long ReceiveDamage(long damage)
        {
            if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
            if (!IsAlive) return 0;
            long dealt = Math.Min(Health, damage);
            Health -= dealt;
            return dealt;
        }
    }

    public sealed class CombatResolution
    {
        private readonly GameSession session;
        private readonly CombatScheduler scheduler;
        private readonly Dictionary<string, CombatEnemy> enemies =
            new Dictionary<string, CombatEnemy>(StringComparer.Ordinal);

        public CombatResolution(GameSession session, CombatScheduler scheduler)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        public void Spawn(CombatEnemy enemy)
        {
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            if (!enemy.IsAlive) throw new InvalidOperationException("사망한 적은 소환할 수 없습니다.");
            if (enemies.ContainsKey(enemy.Id)) throw new InvalidOperationException("중복 적 ID입니다.");
            session.SpawnEnemy();
            enemies.Add(enemy.Id, enemy);
        }

        public bool TryAttack(string attackerId, string enemyId, double nextAttackDelay,
            long verifiedDamage, out long actualDamage)
        {
            actualDamage = 0;
            if (verifiedDamage < 0) throw new ArgumentOutOfRangeException(nameof(verifiedDamage));
            CombatEnemy enemy;
            if (!enemies.TryGetValue(enemyId, out enemy) || !enemy.IsAlive) return false;
            if (!scheduler.TryCommitAttack(attackerId, nextAttackDelay)) return false;
            actualDamage = enemy.ReceiveDamage(verifiedDamage);
            if (!enemy.IsAlive)
            {
                session.DefeatEnemy();
                enemies.Remove(enemyId);
            }
            return true;
        }

        public bool TryLeak(string enemyId)
        {
            CombatEnemy enemy;
            if (!enemies.TryGetValue(enemyId, out enemy)) return false;
            session.LeakEnemy();
            enemies.Remove(enemyId);
            return true;
        }

        public CombatEnemy GetEnemy(string enemyId)
        {
            CombatEnemy enemy;
            return enemies.TryGetValue(enemyId, out enemy) ? enemy : null;
        }
    }
}
