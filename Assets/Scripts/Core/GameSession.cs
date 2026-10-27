using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    public enum SessionPhase { NotStarted, Running, Won, Lost }

    // Offline mobile lifecycle gate. It controls WHEN the existing combat
    // clock advances, not the original Warcraft tick rate or RNG formulas.
    public sealed class OfflineSessionRunGate
    {
        public bool IsManuallyPaused { get; private set; }
        public bool IsApplicationPaused { get; private set; }
        public bool HasFocus { get; private set; } = true;
        public bool CanAdvance => !IsManuallyPaused && !IsApplicationPaused && HasFocus;

        public void SetManualPause(bool paused) { IsManuallyPaused = paused; }
        public void SetApplicationPause(bool paused) { IsApplicationPaused = paused; }
        public void SetFocus(bool focused) { HasFocus = focused; }
    }

    // 악몽 이세계모드 전용 세션. 라운드와 패배 수치는 외부 검증 데이터에서 주입한다.
    public sealed class GameSession
    {
        public const string ModeId = "nightmare-otherworld";
        public SessionPhase Phase { get; private set; } = SessionPhase.NotStarted;
        public int CurrentWave { get; private set; }
        public int AliveEnemies { get; private set; }
        public int LeakedEnemies { get; private set; }
        public int DefeatedEnemies { get; private set; }

        public void Start()
        {
            if (Phase != SessionPhase.NotStarted) throw new InvalidOperationException("이미 시작한 게임입니다.");
            Phase = SessionPhase.Running;
        }

        public void BeginWave(int waveNumber)
        {
            RequireRunning();
            if (waveNumber != CurrentWave + 1) throw new ArgumentOutOfRangeException(nameof(waveNumber));
            CurrentWave = waveNumber;
        }

        public void SpawnEnemy()
        {
            RequireRunning();
            if (CurrentWave == 0) throw new InvalidOperationException("라운드 시작 전입니다.");
            checked { AliveEnemies++; }
        }

        public void DefeatEnemy()
        {
            RequireRunning();
            if (AliveEnemies <= 0) throw new InvalidOperationException("살아있는 적이 없습니다.");
            AliveEnemies--;
            checked { DefeatedEnemies++; }
        }

        public void LeakEnemy()
        {
            RequireRunning();
            if (AliveEnemies <= 0) throw new InvalidOperationException("살아있는 적이 없습니다.");
            AliveEnemies--;
            checked { LeakedEnemies++; }
            // 원작 패배 기준은 미확인: 임의로 패배 처리하지 않는다.
        }

        public void Finish(bool victory)
        {
            RequireRunning();
            Phase = victory ? SessionPhase.Won : SessionPhase.Lost;
        }

        private void RequireRunning()
        {
            if (Phase != SessionPhase.Running) throw new InvalidOperationException("진행 중인 게임이 아닙니다.");
        }
    }
}
