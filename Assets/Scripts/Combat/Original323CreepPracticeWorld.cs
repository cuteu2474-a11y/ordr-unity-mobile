using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public enum Original323CreepEncounterState { Alive, Defeated, Escaped }

    public sealed class Original323CreepEncounter
    {
        public string EnemyId { get; private set; }
        public Original323CreepObject OriginalObject { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public Original323CreepEncounterState State { get; internal set; }
        public bool SourceConfirmed { get; internal set; }
        public bool RewardSettled { get; internal set; }
        // The actual reward table/roll has not yet been source-approved.
        // A real defeat creates a pending outcome, NOT coins or wood.
        public bool RewardAwaitingSourceVerification
        {
            get { return State == Original323CreepEncounterState.Defeated && !RewardSettled; }
        }

        internal Original323CreepEncounter(string enemyId,
            Original323CreepObject originalObject, double x, double y)
        {
            EnemyId = enemyId; OriginalObject = originalObject;
            X = x; Y = y; State = Original323CreepEncounterState.Alive;
        }
    }

    // Explicit, source-object-backed CREEP lane inside the canonical combat world.
    // No guessed spawn clocks, pathing, hunting unit whitelist, sniper ranges,
    // Amim activation, respawn timers, or reward odds are enabled by this class.
    public sealed partial class Original323CreepPracticeWorld : IDisposable
    {
        private readonly CharacterCombatRuntime combat;
        private readonly Original323CreepObjectCatalog catalog;
        private readonly Dictionary<string, Original323CreepEncounter> byEnemy =
            new Dictionary<string, Original323CreepEncounter>(StringComparer.Ordinal);
        private readonly List<Original323CreepEncounter> encounters =
            new List<Original323CreepEncounter>();
        private int nextSerial;
        private bool disposed;
        internal Original323CreepObjectCatalog SourceCatalog => catalog;
        internal bool IsAttachedTo(CharacterCombatRuntime owner) =>
            !disposed && ReferenceEquals(combat, owner);


        public IReadOnlyList<Original323CreepEncounter> Encounters
        { get { return encounters.AsReadOnly(); } }
        public int DefeatedCount { get; private set; }
        public int EscapedCount { get; private set; }
        public event Action<Original323CreepEncounter> EncounterDefeated;
        // The combat lane may inherit an already-approved deployed attacker
        // when a physically spawned NEXT stage arrives. This is not itself
        // a spawn, reward or player dispatch request.
        public event Action<Original323CreepEncounter> EncounterSpawned;
        public event Action<Exception> EncounterSpawnObserverFailed;

        public Original323CreepPracticeWorld(CharacterCombatRuntime combat,
            Original323CreepObjectCatalog catalog)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (catalog.Count != 3)
                throw new ArgumentException("원본 3단계 크립 자료가 필요합니다.", nameof(catalog));
            combat.EnemyExited += OnEnemyExited;
        }

        // Used only by an explicit stage-start request. The original placement
        // and appearance rule must be supplied by the game caller later.
        public Original323CreepEncounter SpawnPracticeStage(int stage,
            double x, double y)
        {
            if (disposed) throw new ObjectDisposedException(nameof(Original323CreepPracticeWorld));
            if (combat.Game.Phase != SessionPhase.Running ||
                combat.Game.CurrentWave == 0)
                throw new InvalidOperationException("전투 라운드가 시작되어야 크립을 배치할 수 있습니다.");
            if (double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y))
                throw new ArgumentOutOfRangeException(nameof(x), "크립 원본 또는 연습 배치 좌표가 필요합니다.");
            var source = catalog.Find(stage);
            if (source == null) throw new ArgumentOutOfRangeException(nameof(stage));
            return SpawnStage(stage, x, y, source.ObjectBaseHitPoints,
                source.CreateObjectBasePracticeDefenses(), false);
        }

        internal Original323CreepEncounter SpawnSourceConfirmedStage(int stage,
            double x, double y, long finalHealth, EnemyDefenseProfile finalDefenses)
        {
            if (disposed) throw new ObjectDisposedException(nameof(Original323CreepPracticeWorld));
            if (combat.Game.Phase != SessionPhase.Running ||
                finalHealth <= 0 || finalDefenses == null ||
                double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y))
                throw new InvalidOperationException("원본 크립 출현 값이 유효하지 않습니다.");
            return SpawnStage(stage, x, y, finalHealth, finalDefenses, true);
        }

        private Original323CreepEncounter SpawnStage(int stage, double x, double y,
            long health, EnemyDefenseProfile defenses, bool sourceConfirmed)
        {
            var source = catalog.Find(stage);
            if (source == null) throw new ArgumentOutOfRangeException(nameof(stage));
            foreach (var existing in encounters)
                if (existing.OriginalObject.Stage == stage &&
                    existing.State == Original323CreepEncounterState.Alive)
                    throw new InvalidOperationException("동일 단계 크립이 이미 살아 있습니다.");
            string id = (sourceConfirmed ? "creep:source:" : "creep:object-base:") +
                source.Rawcode + ":" + (nextSerial + 1);
            var encounter = new Original323CreepEncounter(id, source, x, y) {
                SourceConfirmed = sourceConfirmed
            };
            combat.SpawnEnemyWithDefenses(id, health,
                CharacterEnemyKind.Creep, x, y, 0, defenses);
            nextSerial++;
            byEnemy.Add(id, encounter);
            encounters.Add(encounter);
            var listeners = EncounterSpawned;
            if (listeners != null)
                foreach (Action<Original323CreepEncounter> handler
                    in listeners.GetInvocationList())
                    try { handler(encounter); }
                    catch (Exception error)
                    {
                        try { EncounterSpawnObserverFailed?.Invoke(error); }
                        catch { }
                    }
            return encounter;
        }

        public Original323CreepEncounter FindEncounter(string enemyId)
        {
            Original323CreepEncounter encounter;
            return enemyId != null && byEnemy.TryGetValue(enemyId, out encounter)
                ? encounter : null;
        }

        private void OnEnemyExited(string enemyId, CombatEnemyExitReason reason)
        {
            Original323CreepEncounter encounter;
            if (disposed || !byEnemy.TryGetValue(enemyId, out encounter) ||
                encounter.State != Original323CreepEncounterState.Alive)
                return;
            if (reason == CombatEnemyExitReason.Defeated)
            {
                encounter.State = Original323CreepEncounterState.Defeated;
                DefeatedCount++;
                var listeners = EncounterDefeated;
                if (listeners != null)
                    foreach (Action<Original323CreepEncounter> listener
                        in listeners.GetInvocationList())
                        try { listener(encounter); }
                        catch (Exception error)
                        { try { EncounterSpawnObserverFailed?.Invoke(error); } catch { } }
            }
            else
            {
                encounter.State = Original323CreepEncounterState.Escaped;
                EscapedCount++;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            combat.EnemyExited -= OnEnemyExited;
        }
    }
}
