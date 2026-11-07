using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // These are native event identities, NOT guessed W3X destructible rawcodes.
    // Ck/Ci/Cj are the original handles observed by the original JASS events.
    public sealed class Original323SharedObjectiveSpawnPlan
    {
        public Original323SharedMissionKind Mission { get; }
        public string SourceKey { get; }
        public string ObjectRawcode { get; }
        public string SourceEventId { get; }
        public long Health { get; }
        public double Armor { get; }
        public string ArmorType { get; }
        public double? MagicResistancePercent { get; }
        public double X { get; }
        public double Y { get; }

        public Original323SharedObjectiveSpawnPlan(Original323SharedMissionKind mission,
            string sourceKey, string objectRawcode, string sourceEventId,
            long health, double armor, string armorType, double? magicResistance,
            double x, double y)
        {
            if (!IsOriginalIdentity(mission, sourceKey, objectRawcode) ||
                string.IsNullOrWhiteSpace(sourceEventId) ||
                sourceEventId.Length > 256 || health <= 0 ||
                double.IsNaN(armor) || double.IsInfinity(armor) ||
                double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y) ||
                (magicResistance.HasValue &&
                 (double.IsNaN(magicResistance.Value) ||
                  double.IsInfinity(magicResistance.Value) ||
                  magicResistance.Value < 0 || magicResistance.Value > 100)) ||
                string.IsNullOrWhiteSpace(armorType))
                throw new ArgumentException("공유 목표의 실제 원본 출현 신원·최종 방어 자료가 필요합니다.");
            Mission = mission;
            SourceKey = sourceKey;
            ObjectRawcode = objectRawcode;
            SourceEventId = sourceEventId;
            Health = health;
            Armor = armor;
            ArmorType = armorType;
            MagicResistancePercent = magicResistance;
            X = x;
            Y = y;
        }

        // Five world objectives require both a source-observed encounter and
        // an actual combat defeat. Story/timing/treasure missions have their
        // own separately witnessed completion paths.
        public static bool RequiresCombatDefeat(Original323SharedMissionKind kind)
        {
            switch (kind)
            {
                case Original323SharedMissionKind.RumbarSouls:
                case Original323SharedMissionKind.JusticeGate:
                case Original323SharedMissionKind.ShandoraBell:
                case Original323SharedMissionKind.SeaKing:
                case Original323SharedMissionKind.MarineAdmiral:
                    return true;
                default: return false;
            }
        }

        public static bool IsOriginalIdentity(Original323SharedMissionKind kind,
            string key, string rawcode)
        {
            if (string.IsNullOrEmpty(rawcode) || rawcode.Length != 4) return false;
            switch (kind)
            {
                case Original323SharedMissionKind.RumbarSouls: return key == "Ck";
                case Original323SharedMissionKind.JusticeGate: return key == "Ci";
                case Original323SharedMissionKind.ShandoraBell: return key == "Cj";
                case Original323SharedMissionKind.SeaKing:
                    return key == "CNW" && rawcode == "o02N";
                case Original323SharedMissionKind.MarineAdmiral:
                    return key == "CM3" && (rawcode == "o02M" ||
                        rawcode == "o02Q" || rawcode == "o02R");
                default: return false;
            }
        }
    }

    // Real source/replay supplies a full observed plan and independently
    // confirms the original actor's fatal event. A practice-stage kill, UI
    // selection or object label cannot impersonate either confirmation.
    public interface IOriginal323SharedObjectiveWitness
    {
        bool TryConfirmSpawn(Original323SharedObjectiveSpawnPlan plan,
            out string sourceEvidence);
        bool TryConfirmDefeat(Original323SharedObjectiveSpawnPlan plan,
            string liveEnemyId, string sourceDefeatEventId, out string sourceEvidence);
    }

    public enum Original323SharedObjectiveState
    {
        Alive, DefeatedUnverified, Completed, Removed
    }

    public sealed class Original323SharedObjectiveEncounter
    {
        public string EnemyId { get; internal set; }
        public Original323SharedObjectiveSpawnPlan Plan { get; internal set; }
        public Original323SharedObjectiveState State { get; internal set; }
        public string SpawnEvidence { get; internal set; }
        public string DefeatEvidence { get; internal set; }
        // Source death confirmation can precede the local combat death.
        // Neither confirmation alone grants the mission reward.
        public string ConfirmedDefeatEventId { get; internal set; }
        public string ConfirmedDefeatEvidence { get; internal set; }
        public bool GateSpawnDebtSettled { get; internal set; }
        public string AppliedAdmiralBuffEventId { get; internal set; }
        public string AppliedAdmiralBuffEvidence { get; internal set; }
        public bool AdmiralBuffDebtSettled { get; internal set; }
    }

    // Native-authorized side objectives share the real combat damage/death
    // pipeline, but are never wave monsters or manually spawned fake clears.
    public sealed class Original323SharedObjectiveWorld : IDisposable
    {
        private readonly CharacterCombatRuntime combat;
        private readonly Original323SharedMissionRewardWorld awards;
        private readonly Original323PoneglyphSession missions;
        private readonly IOriginal323SharedObjectiveWitness witness;
        private readonly List<Original323SharedObjectiveEncounter> history =
            new List<Original323SharedObjectiveEncounter>();
        private readonly Dictionary<string, Original323SharedObjectiveEncounter> byEnemy =
            new Dictionary<string, Original323SharedObjectiveEncounter>(
                StringComparer.Ordinal);
        private readonly HashSet<string> usedSpawnIds =
            new HashSet<string>(StringComparer.Ordinal);
        // Claim a trusted source-death ID as soon as it is witnessed,
        // including when the corresponding local enemy is still alive.
        private readonly HashSet<string> usedDefeatIds =
            new HashSet<string>(StringComparer.Ordinal);
        private int serial;
        private bool disposed;

        public Original323SharedObjectiveWorld(CharacterCombatRuntime combat,
            Original323SharedMissionRewardWorld awards,
            Original323PoneglyphSession missions,
            IOriginal323SharedObjectiveWitness witness)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.awards = awards ?? throw new ArgumentNullException(nameof(awards));
            this.missions = missions ?? throw new ArgumentNullException(nameof(missions));
            this.witness = witness ?? throw new ArgumentNullException(nameof(witness));
            combat.EnemyExited += OnEnemyExit;
        }

        public IReadOnlyList<Original323SharedObjectiveEncounter> Encounters =>
            history.AsReadOnly();

        public Original323SharedObjectiveEncounter Find(string enemyId)
        {
            Original323SharedObjectiveEncounter found;
            return !string.IsNullOrWhiteSpace(enemyId) &&
                byEnemy.TryGetValue(enemyId, out found) ? found : null;
        }

        public bool IsLiveTarget(string enemyId)
        {
            var found = Find(enemyId);
            return found != null && found.State == Original323SharedObjectiveState.Alive;
        }

        public Original323SharedObjectiveEncounter ObserveSpawn(
            Original323SharedObjectiveSpawnPlan plan)
        {
            if (disposed || plan == null || combat.Game.Phase != SessionPhase.Running)
                throw new InvalidOperationException("원본 공유 목표의 전투 시점이 아닙니다.");
            // Native replay may deliver the SAME spawn more than once.
            // It must not create a duplicate body or consume a second gate debt.
            foreach (var previous in history)
                if (previous.Plan.SourceEventId == plan.SourceEventId)
                {
                    if (!SameSpawnPlan(previous.Plan, plan))
                        throw new InvalidOperationException(
                            "동일한 원본 등장 ID에 서로 다른 목표 자료가 전달됐습니다.");
                    if (!previous.GateSpawnDebtSettled)
                        previous.GateSpawnDebtSettled =
                            awards.TryRecordRealGateAdmiralSpawn(plan.SourceEventId);
                    return previous;
                }
            if (missions.IsSharedMissionCompleted(plan.Mission) ||
                usedSpawnIds.Contains(plan.SourceEventId))
                throw new InvalidOperationException("실제 공유 목표의 신규 등장 사건이 아닙니다.");
            if (plan.Mission == Original323SharedMissionKind.MarineAdmiral &&
                awards.PendingGateAdmirals <= 0)
                throw new InvalidOperationException(
                    "정의의 문 대장 출현 원본 보상 대기 건이 없습니다.");
            foreach (var previous in history)
                if (previous.Plan.Mission == plan.Mission)
                    throw new InvalidOperationException("공유 목표의 한 판 중복 등장 방지");
            if (plan.Mission == Original323SharedMissionKind.MarineAdmiral &&
                !missions.IsSharedMissionCompleted(
                    Original323SharedMissionKind.JusticeGate))
                throw new InvalidOperationException("정의의 문 파괴 전 대장 출현 불가");

            string evidence;
            if (!witness.TryConfirmSpawn(plan, out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                throw new InvalidOperationException("원본 공유 목표의 실제 등장 미확인");

            string enemyId = "shared:objective:" + plan.Mission + ":" + (serial + 1);
            CharacterEnemyKind lane =
                plan.Mission == Original323SharedMissionKind.SeaKing ?
                    CharacterEnemyKind.SeaKing : CharacterEnemyKind.Challenge;
            var defenses = new EnemyDefenseProfile(lane, plan.Armor,
                plan.MagicResistancePercent, plan.ArmorType,
                "original observed shared objective " + plan.SourceEventId +
                " / " + evidence);
            combat.SpawnEnemyWithDefenses(enemyId, plan.Health, lane,
                plan.X, plan.Y, 0, defenses);
            var record = new Original323SharedObjectiveEncounter {
                EnemyId = enemyId, Plan = plan,
                SpawnEvidence = evidence,
                State = Original323SharedObjectiveState.Alive
            };
            byEnemy.Add(enemyId, record);
            history.Add(record);
            usedSpawnIds.Add(plan.SourceEventId);
            serial++;
            // Debit only AFTER the observed admiral has physically spawned.
            // A temporarily failing wallet transaction must not leave an
            // untracked living enemy or pretend its gate obligation was spent.
            record.GateSpawnDebtSettled =
                plan.Mission != Original323SharedMissionKind.MarineAdmiral ||
                awards.TryRecordRealGateAdmiralSpawn(plan.SourceEventId);
            return record;
        }

        private static bool SameSpawnPlan(Original323SharedObjectiveSpawnPlan a,
            Original323SharedObjectiveSpawnPlan b) =>
            a.Mission == b.Mission && a.SourceKey == b.SourceKey &&
            a.ObjectRawcode == b.ObjectRawcode &&
            a.SourceEventId == b.SourceEventId &&
            a.Health == b.Health && a.Armor == b.Armor &&
            a.ArmorType == b.ArmorType &&
            a.MagicResistancePercent == b.MagicResistancePercent &&
            a.X == b.X && a.Y == b.Y;

        // Native death and local combat death may arrive in either order.
        // A witnessed native event is claimed once, but payout requires both.
        public bool TrySettleDefeat(string enemyId, string sourceDefeatEventId)
        {
            if (disposed || string.IsNullOrWhiteSpace(sourceDefeatEventId) ||
                sourceDefeatEventId.Length > 256) return false;
            var record = Find(enemyId);
            if (record != null &&
                record.State == Original323SharedObjectiveState.Completed)
                return record.ConfirmedDefeatEventId == sourceDefeatEventId;
            if (record == null ||
                (record.State != Original323SharedObjectiveState.Alive &&
                 record.State != Original323SharedObjectiveState.DefeatedUnverified))
                return false;

            if (record.ConfirmedDefeatEventId != null)
            {
                // The original event has already been witnessed. A retry
                // never reuses the witness or swaps in another event ID.
                if (record.ConfirmedDefeatEventId != sourceDefeatEventId)
                    return false;
                return record.State == Original323SharedObjectiveState.Alive ||
                    TryFinalizeDefeat(record);
            }
            if (usedDefeatIds.Contains(sourceDefeatEventId)) return false;

            string evidence;
            if (!witness.TryConfirmDefeat(record.Plan, enemyId,
                sourceDefeatEventId, out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;
            record.ConfirmedDefeatEventId = sourceDefeatEventId;
            record.ConfirmedDefeatEvidence = evidence;
            usedDefeatIds.Add(sourceDefeatEventId);
            return record.State == Original323SharedObjectiveState.Alive ||
                TryFinalizeDefeat(record);
        }

        // The original CM1/CM0 buff may arrive before the mobile combat
        // death. Keep its verified event until both defeat receipts settle.
        public bool TryObserveAppliedAdmiralBuff(string enemyId,
            string sourceEventId, IOriginal323SharedMissionFollowUpWitness followUps)
        {
            if (disposed || followUps == null ||
                string.IsNullOrWhiteSpace(sourceEventId) ||
                sourceEventId.Length > 160 ||
                combat.Game.Phase != SessionPhase.Running)
                return false;
            var record = Find(enemyId);
            if (record == null || record.Plan.Mission !=
                Original323SharedMissionKind.MarineAdmiral ||
                record.State == Original323SharedObjectiveState.Removed)
                return false;
            if (record.AppliedAdmiralBuffEventId != null)
                return record.AppliedAdmiralBuffEventId == sourceEventId &&
                    (record.State != Original323SharedObjectiveState.Completed ||
                     TrySettleAppliedAdmiralBuff(record));
            string evidence;
            if (!followUps.TryConfirmAppliedAdmiralBuff(sourceEventId,
                    record.Plan.ObjectRawcode, out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;
            record.AppliedAdmiralBuffEventId = sourceEventId;
            record.AppliedAdmiralBuffEvidence = evidence;
            return record.State != Original323SharedObjectiveState.Completed ||
                TrySettleAppliedAdmiralBuff(record);
        }

        private bool TrySettleAppliedAdmiralBuff(
            Original323SharedObjectiveEncounter record)
        {
            if (record.AdmiralBuffDebtSettled) return true;
            if (record.State != Original323SharedObjectiveState.Completed ||
                string.IsNullOrWhiteSpace(record.AppliedAdmiralBuffEventId))
                return false;
            record.AdmiralBuffDebtSettled = awards.TryRecordRealAdmiralBuff(
                record.AppliedAdmiralBuffEventId, record.Plan.ObjectRawcode,
                record.AppliedAdmiralBuffEvidence);
            return record.AdmiralBuffDebtSettled;
        }

        private bool TryFinalizeDefeat(Original323SharedObjectiveEncounter record)
        {
            if (record.State != Original323SharedObjectiveState.DefeatedUnverified ||
                string.IsNullOrEmpty(record.ConfirmedDefeatEventId))
                return false;
            if (!record.GateSpawnDebtSettled)
            {
                record.GateSpawnDebtSettled =
                    awards.TryRecordRealGateAdmiralSpawn(record.Plan.SourceEventId);
                if (!record.GateSpawnDebtSettled) return false;
            }
            if (!awards.TrySettleDefeatedObjective(record, combat.Game.CurrentWave))
                return false;
            record.DefeatEvidence = record.ConfirmedDefeatEvidence;
            record.State = Original323SharedObjectiveState.Completed;
            if (record.AppliedAdmiralBuffEventId != null)
                TrySettleAppliedAdmiralBuff(record);
            return true;
        }

        private void OnEnemyExit(string id, CombatEnemyExitReason reason)
        {
            var record = Find(id);
            if (disposed || record == null ||
                record.State != Original323SharedObjectiveState.Alive)
                return;
            record.State = reason == CombatEnemyExitReason.Defeated
                ? Original323SharedObjectiveState.DefeatedUnverified
                : Original323SharedObjectiveState.Removed;
            combat.RecallSharedObjectiveParticipants(id);
            // Never fabricate a native death event ID from a combat callback.
            // Only an independently witnessed event can settle the reward.
            if (record.State == Original323SharedObjectiveState.DefeatedUnverified &&
                record.ConfirmedDefeatEventId != null)
                TryFinalizeDefeat(record);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            combat.EnemyExited -= OnEnemyExit;
        }
    }
}
