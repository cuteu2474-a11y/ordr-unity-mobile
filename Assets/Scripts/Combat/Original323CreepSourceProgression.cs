using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // CTs first spawn and CTp/CTq successor births are real SOURCE EVENTS.
    // The caller owns proof of the trigger and the FINAL Nightmare enemy
    // object. An SLK base HP/armor row is never a final combat authority.
    public interface IOriginal323CreepSourceSpawnPort
    {
        bool TryResolveOriginalSpawn(int stage, string previousDefeatedEnemyId,
            out Original323CreepSourceSpawn spawn);
    }

    public sealed class Original323CreepSourceSpawn
    {
        public int Stage { get; }
        public string SourceRawcode { get; }
        public string OriginalEventEvidence { get; }
        public long FinalHealth { get; }
        public EnemyDefenseProfile FinalDefenses { get; }
        public double X { get; }
        public double Y { get; }

        public Original323CreepSourceSpawn(int stage, string rawcode,
            string evidence, long health, EnemyDefenseProfile defenses,
            double x, double y)
        {
            Stage = stage;
            SourceRawcode = rawcode;
            OriginalEventEvidence = evidence;
            FinalHealth = health;
            FinalDefenses = defenses;
            X = x;
            Y = y;
        }
    }

    public sealed class Original323CreepSourceRewardClaim
    {
        public string ReceiptId { get; }
        public string EnemyId { get; }
        public int Stage { get; }
        public string LastHitActorId { get; }
        public string SourceFunction => Stage == 1 ? "CTp" :
            Stage == 2 ? "CTq" : "CTr";

        internal Original323CreepSourceRewardClaim(
            Original323CreepEncounter encounter, string creditedActor)
        {
            EnemyId = encounter.EnemyId;
            Stage = encounter.OriginalObject.Stage;
            LastHitActorId = creditedActor;
            ReceiptId = "original323:creep:" + SourceFunction + ":" + EnemyId;
        }
    }

    // Every successful native reward (including its original shared RNG,
    // physical h060 result and Bhd stack) is one durable wallet transaction.
    // Failure means "pending", not a zero-value invented payout.
    public interface IOriginal323CreepSourceRewardPort
    {
        bool TryCommitOriginalDeathReward(Original323CreepSourceRewardClaim claim);
    }

    // Joins the source-confirmed DEATH to the source-confirmed next physical
    // spawn without ever spawning inside resolution's enemy-death callback.
    // It cannot create a first creep merely because a user opened a menu.
    public sealed class Original323CreepSourceProgression : IDisposable
    {
        public const double SinglePlayerCreepX = -8448d; // KR[0] Rect K
        public const double SinglePlayerCreepY = -7552d;
        private readonly CharacterCombatRuntime combat;
        private readonly Original323CreepPracticeWorld world;
        private readonly IOriginal323CreepSourceSpawnPort spawns;
        private readonly IOriginal323CreepSourceRewardPort rewards;
        private readonly Dictionary<string, string> killingActors =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<int, Original323CreepSourceRewardClaim> claims =
            new Dictionary<int, Original323CreepSourceRewardClaim>();
        private readonly HashSet<int> issuedStages = new HashSet<int>();
        private readonly HashSet<int> settledStages = new HashSet<int>();
        private string firstEventId;
        private bool advancing, disposed;

        public bool HasObservedInitialSpawn => firstEventId != null;
        public int LatestIssuedStage { get; private set; }
        public int SettledRewardCount => settledStages.Count;
        public int PendingRewardCount => claims.Count - settledStages.Count;
        public event Action<Exception> ProgressionFailed;

        public Original323CreepSourceProgression(CharacterCombatRuntime combat,
            Original323CreepPracticeWorld world,
            IOriginal323CreepSourceSpawnPort originalSpawnPort,
            IOriginal323CreepSourceRewardPort originalRewardPort)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            spawns = originalSpawnPort ??
                throw new ArgumentNullException(nameof(originalSpawnPort));
            rewards = originalRewardPort ??
                throw new ArgumentNullException(nameof(originalRewardPort));
            combat.ActualEnemyDamage += OnActualEnemyDamage;
            world.EncounterDefeated += OnDefeated;
        }

        // Call ONLY on an independently observed original CTs/LB event.
        // Duplicate notifications are ignored before calling the source port.
        public bool ObserveOriginalFirstSpawn(string sourceEventId)
        {
            if (disposed || advancing ||
                string.IsNullOrWhiteSpace(sourceEventId) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(sourceEventId) ||
                HasObservedInitialSpawn) return false;
            Original323CreepEncounter spawned;
            if (!TrySpawn(1, null, out spawned)) return false;
            firstEventId = sourceEventId;
            return true;
        }

        private bool TrySpawn(int stage, string previousEnemyId,
            out Original323CreepEncounter encounter)
        {
            encounter = null;
            if (issuedStages.Contains(stage) ||
                combat.Game.Phase != SessionPhase.Running)
                return false;
            Original323CreepSourceSpawn data;
            if (!spawns.TryResolveOriginalSpawn(stage, previousEnemyId, out data) ||
                data == null || data.Stage != stage ||
                data.FinalHealth <= 0 ||
                data.FinalDefenses == null ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(
                    data.OriginalEventEvidence))
                return false;
            string raw = stage == 1 ? "n00E" :
                stage == 2 ? "n00F" : stage == 3 ? "n00G" : null;
            if (raw == null || data.SourceRawcode != raw ||
                world.SourceCatalog.Find(stage)?.Rawcode != raw ||
                data.FinalDefenses.Kind != CharacterEnemyKind.Creep ||
                string.IsNullOrEmpty(data.FinalDefenses.Source) ||
                !data.FinalDefenses.Source.StartsWith(
                    "original observed creep ", StringComparison.Ordinal) ||
                data.X != SinglePlayerCreepX || data.Y != SinglePlayerCreepY)
                return false;
            encounter = world.SpawnSourceConfirmedStage(stage, data.X, data.Y,
                data.FinalHealth, data.FinalDefenses);
            issuedStages.Add(stage);
            LatestIssuedStage = stage;
            return true;
        }

        private void OnActualEnemyDamage(string actor, string target,
            long hp, bool killed)
        {
            if (disposed || !killed || hp <= 0 || actor == null) return;
            // Unit ID strings are the owned hero's canonical attack identity.
            // A virtual projectile without an owner mapping remains unpaid;
            // never credit its source to an arbitrary nearby player.
            long id;
            if (long.TryParse(actor, out id) &&
                combat.IsOwnedCombatCharacter(id))
                killingActors[target] = actor;
        }

        private void OnDefeated(Original323CreepEncounter encounter)
        {
            if (disposed || !encounter.SourceConfirmed ||
                !issuedStages.Contains(encounter.OriginalObject.Stage))
                return;
            int stage = encounter.OriginalObject.Stage;
            if (claims.ContainsKey(stage)) return;
            string actor;
            killingActors.TryGetValue(encounter.EnemyId, out actor);
            claims.Add(stage, new Original323CreepSourceRewardClaim(encounter, actor));
            killingActors.Remove(encounter.EnemyId);
        }

        // Run after Combat.Step/SourceModelPracticeBattle.Advance completes.
        // Never advance during a death observer: resolution may still be
        // enumerating enemy and aura dictionaries at that point.
        public void AdvancePending()
        {
            if (disposed || advancing || combat.Game.Phase != SessionPhase.Running)
                return;
            advancing = true;
            try
            {
                for (int stage = 1; stage <= 3; stage++)
                {
                    Original323CreepSourceRewardClaim claim;
                    if (!claims.TryGetValue(stage, out claim)) break;
                    if (!settledStages.Contains(stage))
                    {
                        if (!rewards.TryCommitOriginalDeathReward(claim))
                            break;
                        settledStages.Add(stage);
                        var record = world.FindEncounter(claim.EnemyId);
                        if (record != null) record.RewardSettled = true;
                    }
                    if (stage < 3 && !issuedStages.Contains(stage + 1))
                    {
                        Original323CreepEncounter next;
                        if (!TrySpawn(stage + 1, claim.EnemyId, out next))
                            break;
                    }
                }
            }
            catch (Exception error)
            {
                // Preserve the pending record. A post-commit observer failure
                // is resolved via the reward port's wallet receipt on retry.
                var handlers = ProgressionFailed;
                if (handlers != null)
                    foreach (Action<Exception> listener in handlers.GetInvocationList())
                        try { listener(error); } catch { }
            }
            finally { advancing = false; }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            combat.ActualEnemyDamage -= OnActualEnemyDamage;
            world.EncounterDefeated -= OnDefeated;
        }
    }
}
