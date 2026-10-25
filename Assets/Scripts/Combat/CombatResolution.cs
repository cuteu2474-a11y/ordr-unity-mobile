using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // A witness must observe the actual existing-unit A04Q operation (for example Bd5).
    // Global LK/NJ next-spawn events alone must never update an already spawned enemy.
    public interface IOriginal323LiveMonsterA04QWitness
    {
        bool TryConfirm(string enemyId, int expectedPreviousLevel,
            int observedCurrentLevel, out string observedJassEvidence);
    }

    // 피해량은 원작 공식 검증 후 외부 계산기에서 전달한다.
    public sealed class CombatEnemy
    {
        public string Id { get; }
        public long MaxHealth { get; }
        public long Health { get; private set; }
        public bool IsAlive => Health > 0;
        public EnemyDefenseProfile Defenses { get; private set; }
        public CombatStatusEngine Status { get; private set; }

        // Only source-proven levels may be loaded. Unknown is nullable,
        // not level 0; an absent original value must not be guessed.
        public int? OriginalA04RLevel { get; private set; }
        public string OriginalA04RProvenance { get; private set; }

        public void SetVerifiedOriginalA04RBaseline(int originalLevel, string evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence) ||
                originalLevel < 0 ||
                originalLevel > Ordr323OriginalA04R.LastVerifiedFieldLevel)
                throw new ArgumentException("원본 A04R 능력 레벨과 출처가 필요합니다.");
            if (OriginalA04RLevel.HasValue)
                throw new InvalidOperationException("A04R 시작 레벨 중복 지정");
            if (!IsAlive) throw new InvalidOperationException("죽은 적은 변경할 수 없습니다.");
            OriginalA04RLevel = originalLevel;
            OriginalA04RProvenance = evidence;
        }

        internal bool TryPrepareOriginalA04RIncrease(int delta, out int nextLevel)
        {
            nextLevel = 0;
            return IsAlive && OriginalA04RLevel.HasValue &&
                Ordr323OriginalA04R.TryPlanIncrease(
                    OriginalA04RLevel.Value, delta, out nextLevel);
        }

        internal void ApplyPreparedOriginalA04RIncrease(
            int expectedPreviousLevel, int nextLevel)
        {
            int computed;
            if (!IsAlive || !OriginalA04RLevel.HasValue ||
                OriginalA04RLevel.Value != expectedPreviousLevel ||
                !Ordr323OriginalA04R.TryPlanIncrease(
                    expectedPreviousLevel, nextLevel - expectedPreviousLevel -
                    (expectedPreviousLevel == 0 ? 1 : 0), out computed) ||
                computed != nextLevel)
                throw new InvalidOperationException(
                    "원본 A04R 누적 레벨이 사전 검증된 결과와 다릅니다.");
            OriginalA04RLevel = nextLevel;
        }

        public CombatEnemy(string id, long health)
            : this(id, health, EnemyDefenseProfile.Unknown) { }

        public CombatEnemy(string id, long health, EnemyDefenseProfile defenses)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("적 ID가 필요합니다.");
            if (health <= 0) throw new ArgumentOutOfRangeException(nameof(health));
            Id = id; MaxHealth = health; Health = health;
            Defenses = defenses ?? throw new ArgumentNullException(nameof(defenses));
            Status = new CombatStatusEngine();
        }
        // Atomically replace only the magic-defense level after a distinct, source-
        // verified operation on this live monster. Health and base armor are preserved.
        // An unknown baseline, a stale previous level or an unsupported level fails closed.
        public bool TryApplyObservedOriginalA04QLevel(int expectedPreviousLevel,
            int observedCurrentLevel, IOriginal323LiveMonsterA04QWitness witness)
        {
            var previous = Defenses;
            if (!IsAlive || witness == null || previous == null ||
                !previous.Kind.HasValue || !previous.MagicAbilityLevel.HasValue ||
                previous.MagicAbilityLevel.Value != expectedPreviousLevel ||
                expectedPreviousLevel == observedCurrentLevel ||
                string.IsNullOrWhiteSpace(previous.Source))
                return false;
            double verifiedResistance;
            if (!Ordr323VerifiedDefenseAbilities.TryGetMagicResistancePercent(
                observedCurrentLevel, out verifiedResistance))
                return false;
            string evidence;
            if (!witness.TryConfirm(Id, expectedPreviousLevel,
                observedCurrentLevel, out evidence) ||
                string.IsNullOrWhiteSpace(evidence) ||
                evidence.IndexOf("TEST_ONLY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                evidence.IndexOf("SYNTHETIC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                evidence.IndexOf("MOCK_ONLY", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            // Keep the original verified base armor, armor type and enemy kind.
            // Unknown armor stays unknown; the A04Q update does not approve HP or armor.
            var updated = Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                previous.Kind.Value, previous.BaseArmor, previous.WarcraftArmorType,
                observedCurrentLevel, previous.Source + " / observed live A04Q " +
                expectedPreviousLevel + "->" + observedCurrentLevel + ": " + evidence);
            Defenses = updated;
            return true;
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

        // 실제 적용된 피해만 전달. 평타/즉발/지속 스킬의 처치 참여를 공통 집계한다.
        public event Action<string, string, long, bool> DamageApplied;

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
            DamageApplied?.Invoke(attackerId, enemyId, actualDamage, !enemy.IsAlive);
            return true;
        }

        // 발동 스킬·광역 피해는 평타 쿨타임을 추가 소비하지 않는다.
        // 최종 피해량은 확인된 원작 공식 구현체가 계산해 전달해야 한다.
        public bool TryApplySkillDamage(string enemyId, long verifiedDamage, out long actualDamage)
        {
            return TryApplySkillDamage(null, enemyId, verifiedDamage, out actualDamage);
        }

        public bool TryApplySkillDamage(string attackerId, string enemyId,
            long verifiedDamage, out long actualDamage)
        {
            actualDamage = 0;
            if (verifiedDamage < 0) throw new ArgumentOutOfRangeException(nameof(verifiedDamage));
            if (string.IsNullOrWhiteSpace(enemyId))
                throw new ArgumentException("대상 ID가 필요합니다.", nameof(enemyId));
            CombatEnemy enemy;
            if (!enemies.TryGetValue(enemyId, out enemy) || !enemy.IsAlive) return false;
            actualDamage = enemy.ReceiveDamage(verifiedDamage);
            if (!enemy.IsAlive)
            {
                session.DefeatEnemy();
                enemies.Remove(enemyId);
            }
            DamageApplied?.Invoke(attackerId, enemyId, actualDamage, !enemy.IsAlive);
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

