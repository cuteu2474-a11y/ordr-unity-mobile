using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    [Serializable]
    public sealed class Original323CreepEncounterCheckpoint
    {
        public string EnemyId;
        public int Stage;
        public string Rawcode;
        public double X;
        public double Y;
        public Original323CreepEncounterState State;
    }

    [Serializable]
    public sealed class Original323CreepPracticeState
    {
        public int Version = 1;
        public int NextSerial;
        public int DefeatedCount;
        public int EscapedCount;
        public List<Original323CreepEncounterCheckpoint> Encounters =
            new List<Original323CreepEncounterCheckpoint>();
    }

    public sealed partial class Original323CreepPracticeWorld
    {
        public Original323CreepPracticeState CaptureState()
        {
            if (disposed) throw new ObjectDisposedException(nameof(Original323CreepPracticeWorld));
            var state = new Original323CreepPracticeState {
                NextSerial = nextSerial, DefeatedCount = DefeatedCount,
                EscapedCount = EscapedCount
            };
            foreach (var entry in encounters)
                state.Encounters.Add(new Original323CreepEncounterCheckpoint {
                    EnemyId = entry.EnemyId, Stage = entry.OriginalObject.Stage,
                    Rawcode = entry.OriginalObject.Rawcode, X = entry.X, Y = entry.Y,
                    State = entry.State
                });
            ValidateState(state);
            ValidatePhysicalEnemies(state);
            return state;
        }

        public static void ValidateState(Original323CreepPracticeState state)
        {
            if (state == null || state.Version != 1 ||
                state.Encounters == null || state.NextSerial < 0 ||
                state.NextSerial > 100000 || state.Encounters.Count != state.NextSerial ||
                state.DefeatedCount < 0 || state.EscapedCount < 0 ||
                state.DefeatedCount + state.EscapedCount > state.NextSerial)
                throw new FormatException("크립 출현·처치 장부가 잘못됐습니다.");
            int defeated = 0, escaped = 0;
            var livingStages = new HashSet<int>();
            for (int i = 0; i < state.Encounters.Count; i++)
            {
                var entry = state.Encounters[i];
                string raw = entry == null ? null :
                    (entry.Stage == 1 ? "n00E" :
                     entry.Stage == 2 ? "n00F" :
                     entry.Stage == 3 ? "n00G" : null);
                if (raw == null || raw != entry.Rawcode ||
                    entry.EnemyId != "creep:object-base:" + raw + ":" + (i + 1) ||
                    !Enum.IsDefined(typeof(Original323CreepEncounterState), entry.State) ||
                    double.IsNaN(entry.X) || double.IsInfinity(entry.X) ||
                    double.IsNaN(entry.Y) || double.IsInfinity(entry.Y) ||
                    (entry.State == Original323CreepEncounterState.Alive &&
                     !livingStages.Add(entry.Stage)))
                    throw new FormatException("크립 원본 ID·단계·좌표가 잘못됐습니다.");
                if (entry.State == Original323CreepEncounterState.Defeated) defeated++;
                if (entry.State == Original323CreepEncounterState.Escaped) escaped++;
            }
            if (state.DefeatedCount != defeated || state.EscapedCount != escaped)
                throw new FormatException("크립 사망/탈출 합계가 원본 기록과 다릅니다.");
        }

        private void ValidatePhysicalEnemies(Original323CreepPracticeState state)
        {
            var living = new HashSet<string>(StringComparer.Ordinal);
            foreach (var enemy in combat.MobileEnemySnapshot())
                if (enemy.Kind == CharacterEnemyKind.Creep)
                    living.Add(enemy.Id);
            foreach (var record in state.Encounters)
            {
                bool exists = living.Remove(record.EnemyId);
                if ((record.State == Original323CreepEncounterState.Alive) != exists)
                    throw new FormatException("크립 장부와 실제 살아있는 적 신원이 다릅니다.");
            }
            if (living.Count != 0)
                throw new FormatException("기록에 없는 원본 크립이 전장에 남았습니다.");
        }

        // Called only after physical CombatEnemy objects were rebuilt.
        // Never call SpawnPracticeStage: it would issue extra enemy spawns.
        public void RestoreState(Original323CreepPracticeState state)
        {
            ValidateState(state);
            if (disposed || nextSerial != 0 || encounters.Count != 0 ||
                byEnemy.Count != 0 || combat.Game.Phase != SessionPhase.Running)
                throw new InvalidOperationException("새 크립 장부에서만 복원할 수 있습니다.");
            ValidatePhysicalEnemies(state);
            var staged = new List<Original323CreepEncounter>();
            foreach (var entry in state.Encounters)
            {
                var source = catalog.Find(entry.Stage);
                if (source == null || source.Rawcode != entry.Rawcode)
                    throw new FormatException("현재 원본 크립 객체 자료가 저장본과 다릅니다.");
                staged.Add(new Original323CreepEncounter(entry.EnemyId, source,
                    entry.X, entry.Y) { State = entry.State });
            }
            foreach (var entry in staged)
            {
                encounters.Add(entry);
                byEnemy.Add(entry.EnemyId, entry);
            }
            nextSerial = state.NextSerial;
            DefeatedCount = state.DefeatedCount;
            EscapedCount = state.EscapedCount;
        }
    }
}
