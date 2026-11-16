using System;

namespace Ordr.Mobile.Combat
{
    // Combat-clock based monster cap grace period. This class never changes
    // monster HP, spawn ownership, death credit or actual round progression.
    // Only wave enemies in GameSession.AliveEnemies are counted by the caller.
    public sealed class MonsterCountDeathCountdown
    {
        public const double DefaultGraceSeconds = 20d;

        public int EnemyLimit { get; private set; }
        public double GraceSeconds { get; private set; }
        public bool IsActive { get; private set; }
        public double StartedAt { get; private set; }

        public MonsterCountDeathCountdown(int enemyLimit, double graceSeconds = DefaultGraceSeconds)
        {
            if (enemyLimit <= 0 || double.IsNaN(graceSeconds) ||
                double.IsInfinity(graceSeconds) || graceSeconds <= 0)
                throw new ArgumentOutOfRangeException("enemyLimit/graceSeconds");
            EnemyLimit = enemyLimit;
            GraceSeconds = graceSeconds;
            StartedAt = -1d;
        }

        public double RemainingSeconds(double gameTime)
        {
            ValidateTime(gameTime);
            return !IsActive ? 0d :
                Math.Max(0d, GraceSeconds - (gameTime - StartedAt));
        }

        // True only when the limit has remained continuously reached for
        // the entire grace interval. Recovering below the limit resets it.
        public bool Observe(int aliveEnemies, double gameTime)
        {
            ValidateTime(gameTime);
            if (aliveEnemies < 0) throw new ArgumentOutOfRangeException(nameof(aliveEnemies));
            if (aliveEnemies < EnemyLimit)
            {
                IsActive = false;
                StartedAt = -1d;
                return false;
            }
            if (!IsActive)
            {
                IsActive = true;
                StartedAt = gameTime;
                return false;
            }
            if (gameTime + 1e-8 < StartedAt)
                throw new InvalidOperationException("데스카운트 시간이 역행했습니다.");
            return gameTime - StartedAt + 1e-8 >= GraceSeconds;
        }

        // Loading a checkpoint does not replay an expired timer or start a
        // fresh grace period. Old checkpoints with missing fields are inactive.
        public void Restore(bool active, double startedAt,
            int aliveEnemies, double gameTime)
        {
            ValidateTime(gameTime);
            if (aliveEnemies < 0 || (active && (aliveEnemies < EnemyLimit ||
                double.IsNaN(startedAt) || double.IsInfinity(startedAt) ||
                startedAt < 0 || startedAt > gameTime + 1e-8 ||
                gameTime - startedAt >= GraceSeconds + 1e-8)))
                throw new FormatException("데스카운트 저장 시각/몬스터 수가 올바르지 않습니다.");
            IsActive = active;
            StartedAt = active ? startedAt : -1d;
        }

        private static void ValidateTime(double gameTime)
        {
            if (double.IsNaN(gameTime) || double.IsInfinity(gameTime) || gameTime < 0)
                throw new ArgumentOutOfRangeException(nameof(gameTime));
        }
    }
}
