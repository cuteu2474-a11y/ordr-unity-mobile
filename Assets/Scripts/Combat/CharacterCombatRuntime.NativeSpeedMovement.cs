using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // Supply the final original Warcraft movement for ONE advancing wave
    // segment. The proposed position is the unmodified scripted path point;
    // the implementation must independently verify native AOae stacking,
    // collision, immunity, movement caps, buff timing and elapsed time.
    public interface IOriginal323VerifiedNativeEnemyMovementStep
    {
        bool TryResolveMovement(SpatialCombatEnemy liveEnemy,
            double proposedX, double proposedY, double proposedPathProgress,
            double elapsedSeconds,
            IReadOnlyList<CombatEffect> currentOriginalEffects,
            double combatTime,
            out double verifiedX, out double verifiedY,
            out double verifiedPathProgress, out string sourceEvidence);
    }

    public sealed partial class CharacterCombatRuntime
    {
        private IOriginal323VerifiedNativeEnemyMovementStep verifiedNativeMovement;

        public void ConfigureVerifiedOriginal323NativeEnemyMovement(
            IOriginal323VerifiedNativeEnemyMovementStep verifiedOriginalRule)
        {
            if (characters.Count != 0 || verifiedNativeMovement != null)
                throw new InvalidOperationException(
                    "몬스터 원본 이동 규칙은 첫 유닛 생성 전에 한 번만 연결할 수 있습니다.");
            verifiedNativeMovement = verifiedOriginalRule ??
                throw new ArgumentNullException(nameof(verifiedOriginalRule));
        }

        // Use this for advancing an enemy along an otherwise UNMODIFIED
        // original wave route. Plain TryMoveEnemy(x,y,progress) deliberately
        // remains a setter for already-observed native positions so that
        // they are not slowed a second time. ForceMoveEnemy remains an
        // explicitly source-verified knockback/teleport escape hatch.
        public bool TryAdvanceEnemyAlongVerifiedOriginalRoute(
            string enemyId, double proposedX, double proposedY,
            double proposedPathProgress, double elapsedSeconds)
        {
            BlockExternalMutationDuringOriginalFusion();
            if (double.IsNaN(elapsedSeconds) ||
                double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0 ||
                double.IsNaN(proposedX) || double.IsInfinity(proposedX) ||
                double.IsNaN(proposedY) || double.IsInfinity(proposedY) ||
                double.IsNaN(proposedPathProgress) ||
                double.IsInfinity(proposedPathProgress))
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));

            SpatialCombatEnemy enemy;
            if (string.IsNullOrWhiteSpace(enemyId) ||
                !enemies.TryGetValue(enemyId, out enemy) ||
                !enemy.Enemy.IsAlive)
                return false;
            CombatStatusEngine originalStatuses = statuses[enemyId];
            if (originalStatuses.IsStunned()) return false;

            var active = originalStatuses.Snapshot();
            bool hasSpeedDebuff = false;
            foreach (var effect in active)
                if (effect.Type == CombatEffectType.MovementSlowPercent)
                    hasSpeedDebuff = true;

            if (!hasSpeedDebuff)
                return TryMoveEnemy(enemyId, proposedX, proposedY,
                    proposedPathProgress);
            if (verifiedNativeMovement == null)
                return false;

            double oldX = enemy.X, oldY = enemy.Y;
            double oldProgress = enemy.PathProgress;
            double oldTime = scheduler.Time;
            long oldStatusRevision = originalStatuses.Revision;
            int oldRosterCount = characters.Count;
            int oldEnemiesCount = enemies.Count;
            double x, y, progress;
            string evidence;
            if (!verifiedNativeMovement.TryResolveMovement(
                enemy, proposedX, proposedY, proposedPathProgress,
                elapsedSeconds, active, oldTime, out x, out y, out progress,
                out evidence) || string.IsNullOrWhiteSpace(evidence))
                return false;
            // An observation policy must never advance simulation, touch
            // health/statuses, or move actors as a side effect of preflight.
            if (enemy.X != oldX || enemy.Y != oldY ||
                enemy.PathProgress != oldProgress ||
                scheduler.Time != oldTime ||
                originalStatuses.Revision != oldStatusRevision ||
                characters.Count != oldRosterCount ||
                enemies.Count != oldEnemiesCount)
                throw new InvalidOperationException(
                    "원본 이동 검증 함수가 전투 월드 상태를 변경했습니다.");
            if (double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y) ||
                double.IsNaN(progress) || double.IsInfinity(progress))
                throw new InvalidOperationException(
                    "원본 검증 이동 목표 좌표가 잘못됐습니다.");
            return TryMoveEnemy(enemyId, x, y, progress);
        }
    }
}
