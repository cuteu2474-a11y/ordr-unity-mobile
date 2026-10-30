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
        // The actual reward table/roll has not yet been source-approved.
        // A real defeat creates a pending outcome, NOT coins or wood.
        public bool RewardAwaitingSourceVerification
        {
            get { return State == Original323CreepEncounterState.Defeated; }
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
    public sealed class Original323CreepPracticeWorld : IDisposable
    {
        private readonly CharacterCombatRuntime combat;
        private readonly Original323CreepObjectCatalog catalog;
        private readonly Dictionary<string, Original323CreepEncounter> byEnemy =
            new Dictionary<string, Original323CreepEncounter>(StringComparer.Ordinal);
        private readonly List<Original323CreepEncounter> encounters =
            new List<Original323CreepEncounter>();
        private int nextSerial;
        private bool disposed;

        public IReadOnlyList<Original323CreepEncounter> Encounters
        { get { return encounters.AsReadOnly(); } }
        public int DefeatedCount { get; private set; }
        public int EscapedCount { get; private set; }

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
            foreach (var existing in encounters)
                if (existing.OriginalObject.Stage == stage &&
                    existing.State == Original323CreepEncounterState.Alive)
                    throw new InvalidOperationException("동일 단계 크립이 이미 살아 있습니다.");

            // IDs identify individual lives, so later verified respawn events
            // cannot retroactively change a prior kill/reward record.
            string id = "creep:object-base:" + source.Rawcode + ":" + (nextSerial + 1);
            var encounter = new Original323CreepEncounter(id, source, x, y);
            combat.SpawnEnemyWithDefenses(id, source.ObjectBaseHitPoints,
                CharacterEnemyKind.Creep, x, y, 0,
                source.CreateObjectBasePracticeDefenses());
            nextSerial++;
            byEnemy.Add(id, encounter);
            encounters.Add(encounter);
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
