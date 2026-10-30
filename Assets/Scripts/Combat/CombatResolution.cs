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

    // Only observed per-instance Warcraft changes may alter the live monster.
    // Neither SLK defaults nor a modeled research multiplier are live witnesses.
    public interface IOriginal323LiveMonsterStatWitness
    {
        bool TryConfirmMaxHealthChange(string monsterId,
            long expectedCurrentHealth, long expectedMaxHealth,
            long observedCurrentHealth, long observedMaxHealth,
            out string observedSource);

        bool TryConfirmPhysicalArmorChange(string monsterId,
            double expectedArmor, string expectedArmorType,
            double observedArmor, string observedArmorType,
            out string observedSource);
    }

    // 피해량은 원작 공식 검증 후 외부 계산기에서 전달한다.
    public sealed class CombatEnemy
    {
        public string Id { get; }
        public long MaxHealth { get; private set; }
        // Evidence for an observed non-damage max-health event, not a damage source.
        public string ObservedHealthSource { get; private set; }
        public long Health { get; private set; }
        public bool IsAlive => Health > 0;
        public EnemyDefenseProfile Defenses { get; private set; }
        public CombatStatusEngine Status { get; private set; }
        public Original323NativePassiveTargetProfile NativePassiveTargetProfile { get; private set; }
        public long NativePassiveTargetRevision { get; private set; }

        // Only the native target/classification owner supplies this state.
        // Boss/Story labels and zero armor never imply hero or immunity flags.
        public bool TrySetNativePassiveTargetProfile(long expectedRevision,
            Original323NativePassiveTargetProfile current)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (!IsAlive || NativePassiveTargetRevision != expectedRevision ||
                (NativePassiveTargetProfile != null &&
                 NativePassiveTargetProfile.OriginalUnitRawcode != current.OriginalUnitRawcode)) return false;
            long next = checked(NativePassiveTargetRevision + 1);
            NativePassiveTargetProfile = current;
            NativePassiveTargetRevision = next;
            return true;
        }

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

        private static bool ValidLiveWitnessEvidence(string evidence)
        {
            return !string.IsNullOrWhiteSpace(evidence) &&
                evidence.IndexOf("TEST_ONLY", StringComparison.OrdinalIgnoreCase) < 0 &&
                evidence.IndexOf("TEST_FIXTURE", StringComparison.OrdinalIgnoreCase) < 0 &&
                evidence.IndexOf("SYNTHETIC", StringComparison.OrdinalIgnoreCase) < 0 &&
                evidence.IndexOf("MOCK_ONLY", StringComparison.OrdinalIgnoreCase) < 0 &&
                evidence.IndexOf("MODEL_ONLY", StringComparison.OrdinalIgnoreCase) < 0;
        }

        // Max-HP upgrades can also change current HP in Warcraft. Do NOT infer
        // that change by full heal, proportional heal, or truncation. The witness
        // supplies both observed values. Damage and kills must instead use
        // CombatResolution so the session and participation counters stay valid.
        public bool TryApplyObservedMaximumHealthChange(
            long expectedCurrentHealth, long expectedMaxHealth,
            long observedCurrentHealth, long observedMaxHealth,
            IOriginal323LiveMonsterStatWitness witness)
        {
            if (!IsAlive || witness == null ||
                Health != expectedCurrentHealth || MaxHealth != expectedMaxHealth ||
                observedMaxHealth <= 0 || observedMaxHealth == expectedMaxHealth ||
                observedCurrentHealth <= 0 ||
                observedCurrentHealth > observedMaxHealth)
                return false;
            string evidence;
            if (!witness.TryConfirmMaxHealthChange(Id,
                expectedCurrentHealth, expectedMaxHealth,
                observedCurrentHealth, observedMaxHealth, out evidence) ||
                !ValidLiveWitnessEvidence(evidence) ||
                Health != expectedCurrentHealth || MaxHealth != expectedMaxHealth)
                return false;
            MaxHealth = observedMaxHealth;
            Health = observedCurrentHealth;
            ObservedHealthSource = evidence;
            return true;
        }

        // Physical armor from a source-confirmed live base-stat change.
        // This does not apply an aura twice, or silently alter A04Q magic defense.
        public bool TryApplyObservedPhysicalArmorChange(
            double expectedArmor, string expectedArmorType,
            double observedArmor, string observedArmorType,
            IOriginal323LiveMonsterStatWitness witness)
        {
            var prior = Defenses;
            if (!IsAlive || witness == null || prior == null ||
                !prior.Kind.HasValue || !prior.HasVerifiedArmor ||
                prior.BaseArmor.Value != expectedArmor ||
                !string.Equals(prior.WarcraftArmorType, expectedArmorType,
                    StringComparison.Ordinal) ||
                double.IsNaN(observedArmor) || double.IsInfinity(observedArmor) ||
                string.IsNullOrWhiteSpace(observedArmorType) ||
                (observedArmor == expectedArmor &&
                 string.Equals(observedArmorType, expectedArmorType,
                     StringComparison.Ordinal)))
                return false;
            Ordr323ArmorType parsed;
            if (!Ordr323OriginalDamageRules.TryReadArmorType(
                observedArmorType, out parsed)) return false;
            string evidence;
            if (!witness.TryConfirmPhysicalArmorChange(Id,
                expectedArmor, expectedArmorType,
                observedArmor, observedArmorType, out evidence) ||
                !ValidLiveWitnessEvidence(evidence) ||
                !ReferenceEquals(Defenses, prior))
                return false;
            Defenses = new EnemyDefenseProfile(prior.Kind, observedArmor,
                prior.MagicResistancePercent, observedArmorType,
                prior.Source + " / observed live physical armor: " + evidence,
                prior.MagicAbilityLevel);
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

    // Combat outcomes must be recorded from the same canonical damage/leak
    // path that updates GameSession, never inferred from missing spatial markers.
    public enum CombatEnemyExitReason { Defeated, Escaped }

    public sealed class CombatResolution
    {
        private readonly GameSession session;
        private readonly CombatScheduler scheduler;
        private readonly Dictionary<string, CombatEnemy> enemies =
            new Dictionary<string, CombatEnemy>(StringComparer.Ordinal);
        // Creep encounters are tracked independently of main lane-wave counts.
        private readonly HashSet<string> nonWaveEnemyIds =
            new HashSet<string>(StringComparer.Ordinal);

        // 실제 적용된 피해만 전달. 평타/즉발/지속 스킬의 처치 참여를 공통 집계한다.
        public event Action<string, string, long, bool> DamageApplied;
        public event Action<string, CombatEnemyExitReason> EnemyExited;

        public CombatResolution(GameSession session, CombatScheduler scheduler)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        public void Spawn(CombatEnemy enemy, bool countsForWave = true)
        {
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            if (!enemy.IsAlive) throw new InvalidOperationException("사망한 적은 소환할 수 없습니다.");
            if (enemies.ContainsKey(enemy.Id)) throw new InvalidOperationException("중복 적 ID입니다.");
            if (countsForWave) session.SpawnEnemy();
            else if (session.Phase != SessionPhase.Running)
                throw new InvalidOperationException("전투가 시작되어야 비웨이브 적을 생성할 수 있습니다.");
            enemies.Add(enemy.Id, enemy);
            if (!countsForWave) nonWaveEnemyIds.Add(enemy.Id);
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
                if (!nonWaveEnemyIds.Remove(enemyId)) session.DefeatEnemy();
                enemies.Remove(enemyId);
            }
            DamageApplied?.Invoke(attackerId, enemyId, actualDamage, !enemy.IsAlive);
            if (!enemy.IsAlive)
                EnemyExited?.Invoke(enemyId, CombatEnemyExitReason.Defeated);
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
                if (!nonWaveEnemyIds.Remove(enemyId)) session.DefeatEnemy();
                enemies.Remove(enemyId);
            }
            DamageApplied?.Invoke(attackerId, enemyId, actualDamage, !enemy.IsAlive);
            if (!enemy.IsAlive)
                EnemyExited?.Invoke(enemyId, CombatEnemyExitReason.Defeated);
            return true;
        }

        public bool TryLeak(string enemyId)
        {
            CombatEnemy enemy;
            if (!enemies.TryGetValue(enemyId, out enemy)) return false;
            if (!nonWaveEnemyIds.Remove(enemyId)) session.LeakEnemy();
            enemies.Remove(enemyId);
            EnemyExited?.Invoke(enemyId, CombatEnemyExitReason.Escaped);
            return true;
        }

        public CombatEnemy GetEnemy(string enemyId)
        {
            CombatEnemy enemy;
            return enemies.TryGetValue(enemyId, out enemy) ? enemy : null;
        }
    }
}

