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
            if (disposed || plan == null || combat.Game.Phase != SessionPhase.Running ||
                missions.IsSharedMissionCompleted(plan.Mission) ||
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
            if (plan.Mission == Original323SharedMissionKind.MarineAdmiral &&
                !awards.TryRecordRealGateAdmiralSpawn(plan.SourceEventId))
                throw new InvalidOperationException(
                    "실제 해군대장 출현 후 출현 의무 정산 실패");
            return record;
        }

        // Delayed native defeat confirmation can arrive after the combat
        // death callback. The enemy must first have actually died in combat.
        public bool TrySettleDefeat(string enemyId, string sourceDefeatEventId)
        {
            if (disposed || string.IsNullOrWhiteSpace(sourceDefeatEventId) ||
                sourceDefeatEventId.Length > 256 ||
                usedDefeatIds.Contains(sourceDefeatEventId)) return false;
            var record = Find(enemyId);
            if (record == null ||
                record.State != Original323SharedObjectiveState.DefeatedUnverified)
                return false;
            string evidence;
            if (!witness.TryConfirmDefeat(record.Plan, enemyId,
                sourceDefeatEventId, out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;
            if (!awards.TrySettle(record.Plan.Mission,
                sourceDefeatEventId, combat.Game.CurrentWave, evidence))
                return false;
            usedDefeatIds.Add(sourceDefeatEventId);
            record.DefeatEvidence = evidence;
            record.State = Original323SharedObjectiveState.Completed;
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
            if (record.State == Original323SharedObjectiveState.DefeatedUnverified)
                TrySettleDefeat(id, "shared:combat-kill:" + record.Plan.SourceEventId);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            combat.EnemyExited -= OnEnemyExit;
        }
    }
}
