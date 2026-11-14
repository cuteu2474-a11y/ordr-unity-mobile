using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    [Serializable]
    public sealed class Original323StoryBuildingCheckpoint
    {
        public string EnemyId, Rawcode;
        public int Stage;
        public Original323StoryBuildingState State;
        public bool SourceVerifiedSpawn;
        public string SourceSpawnEventId, SourceSpawnEvidence;
        public bool SourceSettlementConfirmed;
        public string SourceSettlementEventId, SourceSettlementEvidence;
    }

    [Serializable]
    public sealed class Original323StoryBuildingPracticeState
    {
        public int Version = 1;
        public int Serial, DestroyedCount;
        public List<Original323StoryBuildingCheckpoint> Encounters =
            new List<Original323StoryBuildingCheckpoint>();
    }

    public sealed partial class Original323StoryBuildingPracticeWorld
    {
        public Original323StoryBuildingPracticeState CaptureState()
        {
            if (disposed) throw new ObjectDisposedException(nameof(Original323StoryBuildingPracticeWorld));
            var state = new Original323StoryBuildingPracticeState {
                Serial = serial, DestroyedCount = DestroyedCount
            };
            foreach (var encounter in history)
                state.Encounters.Add(new Original323StoryBuildingCheckpoint {
                    EnemyId = encounter.EnemyId,
                    Rawcode = encounter.Building.Rawcode,
                    Stage = encounter.Building.Stage,
                    State = encounter.State,
                    SourceVerifiedSpawn = encounter.SourceVerifiedSpawn,
                    SourceSpawnEventId = encounter.SourceSpawnEventId,
                    SourceSpawnEvidence = encounter.SourceSpawnEvidence,
                    SourceSettlementConfirmed = encounter.SourceSettlementConfirmed,
                    SourceSettlementEventId = encounter.SourceSettlementEventId,
                    SourceSettlementEvidence = encounter.SourceSettlementEvidence
                });
            ValidateState(state);
            VerifyLiveEnemyIdentities(state);
            return state;
        }

        public static void ValidateState(Original323StoryBuildingPracticeState state)
        {
            if (state == null || state.Version != 1 ||
                state.Encounters == null || state.Serial < 0 ||
                state.Serial != state.Encounters.Count ||
                state.Serial > 10000 || state.DestroyedCount < 0)
                throw new FormatException("스토리 실물 건물 장부가 잘못됐습니다.");
            var alive = new HashSet<int>();
            var spawns = new HashSet<string>(StringComparer.Ordinal);
            var settlements = new HashSet<string>(StringComparer.Ordinal);
            var settledStages = new HashSet<int>();
            int destroyed = 0;
            for (int i = 0; i < state.Encounters.Count; i++)
            {
                var entry = state.Encounters[i];
                if (entry == null || entry.Stage < 1 || entry.Stage > 14 ||
                    string.IsNullOrWhiteSpace(entry.Rawcode) || entry.Rawcode.Length != 4 ||
                    entry.EnemyId !=
                        (entry.SourceVerifiedSpawn ? "story:building:observed:" :
                            "story:building:practice:") + entry.Rawcode + ":" + (i + 1) ||
                    !Enum.IsDefined(typeof(Original323StoryBuildingState), entry.State) ||
                    (entry.State == Original323StoryBuildingState.Alive &&
                        !alive.Add(entry.Stage)))
                    throw new FormatException("스토리 건물 원본 ID/단계가 잘못됐습니다.");
                if (entry.State == Original323StoryBuildingState.Destroyed) destroyed++;
                if (entry.SourceVerifiedSpawn)
                {
                    if (string.IsNullOrWhiteSpace(entry.SourceSpawnEventId) ||
                        string.IsNullOrWhiteSpace(entry.SourceSpawnEvidence) ||
                        !spawns.Add(entry.SourceSpawnEventId))
                        throw new FormatException("스토리 원본 건물 등장 영수증이 중복됐습니다.");
                }
                else if (entry.SourceSpawnEventId != null ||
                    entry.SourceSpawnEvidence != null)
                    throw new FormatException("연습 건물에 원본 등장 영수증이 붙었습니다.");
                if (entry.SourceSettlementConfirmed)
                {
                    if (!entry.SourceVerifiedSpawn ||
                        entry.State != Original323StoryBuildingState.Destroyed ||
                        string.IsNullOrWhiteSpace(entry.SourceSettlementEventId) ||
                        string.IsNullOrWhiteSpace(entry.SourceSettlementEvidence) ||
                        !settlements.Add(entry.SourceSettlementEventId) ||
                        !settledStages.Add(entry.Stage))
                        throw new FormatException("스토리 건물 완료 원본 증빙이 중복됐습니다.");
                }
                else if (entry.SourceSettlementEventId != null ||
                    entry.SourceSettlementEvidence != null)
                    throw new FormatException("원본 정산되지 않은 건물에 영수증이 붙었습니다.");
            }
            if (state.DestroyedCount != destroyed)
                throw new FormatException("스토리 건물 파괴 횟수가 실물 장부와 다릅니다.");
        }

        private void VerifyLiveEnemyIdentities(Original323StoryBuildingPracticeState state)
        {
            var enemies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var enemy in combat.MobileEnemySnapshot())
                if (enemy.Kind == CharacterEnemyKind.Story) enemies.Add(enemy.Id);
            foreach (var entry in state.Encounters)
            {
                bool alive = enemies.Remove(entry.EnemyId);
                if ((entry.State == Original323StoryBuildingState.Alive) != alive)
                    throw new FormatException("스토리 건물/실제 전장 적의 신원이 다릅니다.");
            }
            if (enemies.Count != 0)
                throw new FormatException("장부에 없는 스토리 적이 복원 전장에 있습니다.");
        }

        // Physical CombatEnemy was restored already; never call
        // SpawnInternal (which spawns an enemy and changes IDs/counters).
        public void RestoreState(Original323StoryBuildingPracticeState state)
        {
            ValidateState(state);
            if (disposed || serial != 0 || history.Count != 0 ||
                byId.Count != 0 || combat.Game.Phase != SessionPhase.Running)
                throw new InvalidOperationException("새 스토리 건물 월드에서만 복원합니다.");
            VerifyLiveEnemyIdentities(state);
            var staged = new List<Original323StoryBuildingEncounter>();
            foreach (var entry in state.Encounters)
            {
                var def = catalog.Find(entry.Stage);
                if (def == null || def.Rawcode != entry.Rawcode)
                    throw new FormatException("원본 스토리 건물 객체 ID가 저장본과 다릅니다.");
                staged.Add(new Original323StoryBuildingEncounter {
                    EnemyId = entry.EnemyId, Building = def, State = entry.State,
                    SourceVerifiedSpawn = entry.SourceVerifiedSpawn,
                    SourceSpawnEventId = entry.SourceSpawnEventId,
                    SourceSpawnEvidence = entry.SourceSpawnEvidence,
                    SourceSettlementConfirmed = entry.SourceSettlementConfirmed,
                    SourceSettlementEventId = entry.SourceSettlementEventId,
                    SourceSettlementEvidence = entry.SourceSettlementEvidence
                });
            }
            foreach (var entry in staged)
            {
                history.Add(entry);
                byId.Add(entry.EnemyId, entry);
                if (entry.State == Original323StoryBuildingState.Alive)
                    activeStages.Add(entry.Building.Stage);
                if (entry.SourceVerifiedSpawn)
                    usedSpawnEvents.Add(entry.SourceSpawnEventId);
                if (entry.SourceSettlementConfirmed)
                {
                    usedSettlements.Add(entry.SourceSettlementEventId);
                    settledStages.Add(entry.Building.Stage);
                }
            }
            serial = state.Serial;
            DestroyedCount = state.DestroyedCount;
        }
    }
}
