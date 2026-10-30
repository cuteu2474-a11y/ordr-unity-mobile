using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public enum Original323StoryBuildingState { Alive, Destroyed, Removed }

    // A sourced spawn is NOT the same thing as the SLK object base.
    // Only the real stage provider may supply its runtime HP, armor and position.
    public sealed class Original323StoryBuildingSpawnPlan
    {
        public int Stage { get; }
        public string Rawcode { get; }
        public string SourceEventId { get; }
        public long Health { get; }
        public double Armor { get; }
        public string ArmorType { get; }
        public double X { get; }
        public double Y { get; }
        public double? MagicResistancePercent { get; }

        public Original323StoryBuildingSpawnPlan(int stage, string rawcode,
            string sourceEventId, long health, double armor, string armorType,
            double x, double y, double? magicResistancePercent)
        {
            if (stage < 1 || stage > 14 || string.IsNullOrWhiteSpace(rawcode) ||
                rawcode.Length != 4 || string.IsNullOrWhiteSpace(sourceEventId) ||
                sourceEventId.Length > 256 || health <= 0 ||
                double.IsNaN(armor) || double.IsInfinity(armor) ||
                double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y) ||
                (magicResistancePercent.HasValue &&
                 (double.IsNaN(magicResistancePercent.Value) ||
                  double.IsInfinity(magicResistancePercent.Value) ||
                  magicResistancePercent.Value < 0 ||
                  magicResistancePercent.Value > 100)) ||
                armorType != "fort")
                throw new ArgumentException("스토리 건물의 원본 출현 자료가 유효하지 않습니다.");
            Stage = stage;
            Rawcode = rawcode;
            SourceEventId = sourceEventId;
            Health = health;
            Armor = armor;
            ArmorType = armorType;
            X = x;
            Y = y;
            MagicResistancePercent = magicResistancePercent;
        }
    }

    // The authorizer owns unlock requirements and actual story building spawns.
    // Do not replace this with "stage N-1 cleared" or guessed timer rules.
    public interface IOriginal323StoryBuildingSpawnAuthorization
    {
        bool TryGetAuthorizedSpawn(int stage,
            out Original323StoryBuildingSpawnPlan plan);
    }

    // An independent original-runtime/replay observer confirms the FULL plan.
    public interface IOriginal323StoryBuildingSpawnWitness
    {
        bool TryConfirmOriginalSpawn(Original323StoryBuildingSpawnPlan plan,
            out string sourceEvidence);
    }

    // The source settlement must have finished ALL original costs and rewards.
    // A kill or an unlocked object alone is never an award or clear receipt.
    public interface IOriginal323StoryBuildingClearWitness
    {
        bool TryConfirmSettledOriginalClear(int stage, string rawcode,
            string enemyId, string spawnEventId, string settlementEventId,
            out string sourceEvidence);
    }

    public sealed class Original323StoryBuildingEncounter
    {
        public string EnemyId { get; internal set; }
        public Original323StoryBuilding Building { get; internal set; }
        public Original323StoryBuildingState State { get; internal set; }
        public bool SourceVerifiedSpawn { get; internal set; }
        public string SourceSpawnEventId { get; internal set; }
        public string SourceSpawnEvidence { get; internal set; }
        public bool SourceSettlementConfirmed { get; internal set; }
        public string SourceSettlementEventId { get; internal set; }
        public bool RewardAwaitingSourceVerification =>
            State == Original323StoryBuildingState.Destroyed &&
            !SourceSettlementConfirmed;
    }

    // Story is a STATIC BUILDING lane, not an enemy waypoint or wave creep.
    // Practice and source-authorized spawns share a damage/death event pipeline
    // but NEVER share eligibility or reward permissions.
    public sealed class Original323StoryBuildingPracticeWorld : IDisposable
    {
        private readonly CharacterCombatRuntime combat;
        private readonly Original323StoryBuildingCatalog catalog;
        private readonly Dictionary<string, Original323StoryBuildingEncounter> byId =
            new Dictionary<string, Original323StoryBuildingEncounter>(StringComparer.Ordinal);
        private readonly HashSet<int> activeStages = new HashSet<int>();
        private readonly HashSet<string> usedSpawnEvents =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> usedSettlements =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<int> settledStages = new HashSet<int>();
        private readonly List<Original323StoryBuildingEncounter> history =
            new List<Original323StoryBuildingEncounter>();
        private int serial;
        private bool disposed;

        public IReadOnlyList<Original323StoryBuildingEncounter> Encounters => history.AsReadOnly();
        public int DestroyedCount { get; private set; }
        public int SettledCount => settledStages.Count;
        public Original323StoryBuildingEncounter FindEncounter(string enemyId) =>
            enemyId != null && byId.TryGetValue(enemyId, out var result) ? result : null;

        public Original323StoryBuildingPracticeWorld(CharacterCombatRuntime combat,
            Original323StoryBuildingCatalog catalog)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (catalog.Count != 14) throw new ArgumentException("14개 건물 객체 필요");
            combat.EnemyExited += OnExit;
        }

        private void RequireRunning()
        {
            if (disposed) throw new ObjectDisposedException(nameof(Original323StoryBuildingPracticeWorld));
            if (combat.Game.Phase != SessionPhase.Running || combat.Game.CurrentWave == 0)
                throw new InvalidOperationException("전투 라운드가 시작되지 않았습니다.");
        }

        // Explicit MODEL_ONLY entry, never a source-verified stage clear.
        public Original323StoryBuildingEncounter SpawnPracticeBuilding(int stage,
            double x, double y)
        {
            RequireRunning();
            if (double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y))
                throw new ArgumentOutOfRangeException(nameof(x));
            var building = catalog.Find(stage);
            if (building == null) throw new ArgumentOutOfRangeException(nameof(stage));
            return SpawnInternal(building, building.BaseHitPoints, x, y,
                building.PracticeDefense(), null, null);
        }

        // Spawns only after stage authorization AND independent current-runtime
        // observation. SLK base stats must not be substituted for this plan.
        public Original323StoryBuildingEncounter SpawnAuthorizedBuilding(
            int stage, IOriginal323StoryBuildingSpawnAuthorization authorizer,
            IOriginal323StoryBuildingSpawnWitness witness)
        {
            RequireRunning();
            if (authorizer == null || witness == null)
                throw new ArgumentNullException("스토리 건물 출현 원본 확인 공급자 필요");
            var building = catalog.Find(stage);
            if (building == null) throw new ArgumentOutOfRangeException(nameof(stage));
            if (activeStages.Contains(stage) || settledStages.Contains(stage))
                throw new InvalidOperationException("이미 출현하거나 정산된 스토리 건물입니다.");

            Original323StoryBuildingSpawnPlan plan;
            if (!authorizer.TryGetAuthorizedSpawn(stage, out plan) || plan == null ||
                plan.Stage != stage || plan.Rawcode != building.Rawcode ||
                usedSpawnEvents.Contains(plan.SourceEventId))
                throw new InvalidOperationException("스토리 건물의 원본 등장 조건이 승인되지 않았습니다.");

            string evidence;
            if (!witness.TryConfirmOriginalSpawn(plan, out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                throw new InvalidOperationException("스토리 건물의 실제 원본 출현이 확인되지 않았습니다.");

            var defenses = new EnemyDefenseProfile(CharacterEnemyKind.Story,
                plan.Armor, plan.MagicResistancePercent, plan.ArmorType,
                "original observed story building " + plan.SourceEventId + " / " + evidence);
            return SpawnInternal(building, plan.Health, plan.X, plan.Y,
                defenses, plan.SourceEventId, evidence);
        }

        private Original323StoryBuildingEncounter SpawnInternal(
            Original323StoryBuilding building, long health, double x, double y,
            EnemyDefenseProfile defenses, string sourceEventId, string evidence)
        {
            if (!activeStages.Add(building.Stage))
                throw new InvalidOperationException("동일 단계의 스토리 건물이 이미 살아 있습니다.");
            string id = (sourceEventId == null ? "story:building:practice:" :
                "story:building:observed:") + building.Rawcode + ":" + (serial + 1);
            try
            {
                combat.SpawnEnemyWithDefenses(id, health,
                    CharacterEnemyKind.Story, x, y, 0, defenses);
                var encounter = new Original323StoryBuildingEncounter {
                    EnemyId=id, Building=building,
                    State=Original323StoryBuildingState.Alive,
                    SourceVerifiedSpawn=sourceEventId != null,
                    SourceSpawnEventId=sourceEventId, SourceSpawnEvidence=evidence
                };
                byId.Add(id, encounter);
                history.Add(encounter);
                if (sourceEventId != null) usedSpawnEvents.Add(sourceEventId);
                serial++;
                return encounter;
            }
            catch
            {
                activeStages.Remove(building.Stage);
                // The source spawn call itself is authoritative. Rollback of a
                // partially failed combat mutation must be handled by the combat world.
                throw;
            }
        }

        // Recording a story completion requires a source-backed kill and a
        // separately witnessed FULL original settlement; never creates gold,
        // wood, wish wisps or any unit from a hypothetical victory.
        public bool TryRecordSettledClear(string enemyId, string settlementEventId,
            IOriginal323StoryBuildingClearWitness witness)
        {
            if (disposed || witness == null ||
                string.IsNullOrWhiteSpace(settlementEventId) ||
                settlementEventId.Length > 256 || usedSettlements.Contains(settlementEventId))
                return false;
            var encounter = FindEncounter(enemyId);
            if (encounter == null || !encounter.SourceVerifiedSpawn ||
                encounter.State != Original323StoryBuildingState.Destroyed ||
                encounter.SourceSettlementConfirmed ||
                settledStages.Contains(encounter.Building.Stage)) return false;
            string evidence;
            if (!witness.TryConfirmSettledOriginalClear(
                encounter.Building.Stage, encounter.Building.Rawcode, enemyId,
                encounter.SourceSpawnEventId, settlementEventId, out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;
            // No callback or resource operation between the dedupe writes.
            usedSettlements.Add(settlementEventId);
            settledStages.Add(encounter.Building.Stage);
            encounter.SourceSettlementConfirmed = true;
            encounter.SourceSettlementEventId = settlementEventId;
            return true;
        }

        private void OnExit(string id, CombatEnemyExitReason reason)
        {
            if (disposed || !byId.TryGetValue(id, out var encounter) ||
                encounter.State != Original323StoryBuildingState.Alive) return;
            encounter.State = reason == CombatEnemyExitReason.Defeated
                ? Original323StoryBuildingState.Destroyed
                : Original323StoryBuildingState.Removed;
            activeStages.Remove(encounter.Building.Stage);
            if (encounter.State == Original323StoryBuildingState.Destroyed)
                DestroyedCount++;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            combat.EnemyExited -= OnExit;
        }
    }
}
