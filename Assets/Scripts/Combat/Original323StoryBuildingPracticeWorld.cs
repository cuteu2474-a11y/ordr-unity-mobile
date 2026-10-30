using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public enum Original323StoryBuildingState { Alive, Destroyed, Removed }

    public sealed class Original323StoryBuildingEncounter
    {
        public string EnemyId { get; internal set; }
        public Original323StoryBuilding Building { get; internal set; }
        public Original323StoryBuildingState State { get; internal set; }
        public bool RewardAwaitingSourceVerification =>
            State == Original323StoryBuildingState.Destroyed;
    }

    // Explicit building-target practice lane; never moves the structure.
    // No fabricated unlocks, reward amounts, deployment conditions or mode stats.
    public sealed class Original323StoryBuildingPracticeWorld : IDisposable
    {
        private readonly CharacterCombatRuntime combat;
        private readonly Original323StoryBuildingCatalog catalog;
        private readonly Dictionary<string, Original323StoryBuildingEncounter> byId =
            new Dictionary<string, Original323StoryBuildingEncounter>(StringComparer.Ordinal);
        private readonly HashSet<int> activeStages = new HashSet<int>();
        private readonly List<Original323StoryBuildingEncounter> history =
            new List<Original323StoryBuildingEncounter>();
        private int serial;
        private bool disposed;
        public IReadOnlyList<Original323StoryBuildingEncounter> Encounters => history.AsReadOnly();
        public int DestroyedCount { get; private set; }

        public Original323StoryBuildingPracticeWorld(CharacterCombatRuntime combat,
            Original323StoryBuildingCatalog catalog)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (catalog.Count != 14) throw new ArgumentException("14개 건물 객체 필요");
            combat.EnemyExited += OnExit;
        }

        public Original323StoryBuildingEncounter SpawnPracticeBuilding(int stage,
            double x, double y)
        {
            if (disposed) throw new ObjectDisposedException(nameof(Original323StoryBuildingPracticeWorld));
            if (combat.Game.Phase != SessionPhase.Running || combat.Game.CurrentWave == 0)
                throw new InvalidOperationException("전투 라운드가 시작되지 않았습니다.");
            if (double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y))
                throw new ArgumentOutOfRangeException(nameof(x));
            var building = catalog.Find(stage);
            if (building == null) throw new ArgumentOutOfRangeException(nameof(stage));
            if (!activeStages.Add(stage))
                throw new InvalidOperationException("이미 존재하는 스토리 건물입니다.");
            string id = "story:building:practice:" + building.Rawcode + ":" + (serial + 1);
            try
            {
                combat.SpawnEnemyWithDefenses(id, building.BaseHitPoints,
                    CharacterEnemyKind.Story, x, y, 0, building.PracticeDefense());
                serial++;
                var encounter = new Original323StoryBuildingEncounter {
                    EnemyId=id, Building=building, State=Original323StoryBuildingState.Alive
                };
                byId.Add(id, encounter);
                history.Add(encounter);
                return encounter;
            }
            catch
            {
                activeStages.Remove(stage);
                throw;
            }
        }

        private void OnExit(string id, CombatEnemyExitReason reason)
        {
            if (disposed || !byId.TryGetValue(id,out var encounter) ||
                encounter.State != Original323StoryBuildingState.Alive) return;
            encounter.State = reason == CombatEnemyExitReason.Defeated
                ? Original323StoryBuildingState.Destroyed
                : Original323StoryBuildingState.Removed;
            activeStages.Remove(encounter.Building.Stage);
            if (encounter.State == Original323StoryBuildingState.Destroyed) DestroyedCount++;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            combat.EnemyExited -= OnExit;
        }
    }
}
