using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;
using UnityEngine;

namespace Ordr.Mobile.Combat
{
    [Serializable]
    public sealed class SourceCompositeOwnerInt
    {
        public long OwnerId;
        public int Value;
    }

    [Serializable]
    public sealed class SourceCompositeOwnerClock
    {
        public long OwnerId;
        public double DueAt;
    }

    [Serializable]
    public sealed class SourceCompositePeriodicClock
    {
        public long OwnerId;
        public string SkillId;
        public double DueAt;
    }

    [Serializable]
    public sealed class SourceCompositeTargetClaim
    {
        public string EnemyId;
        public string Claimant;
    }

    [Serializable]
    public sealed class SourceCompositeKingSlowRevision
    {
        public long OwnerId;
        public string EnemyId;
        public long Revision;
    }

    [Serializable]
    public sealed class SourceCompositeSkillOwnerCheckpoint
    {
        public int Version = 1;
        public List<SourceCompositeOwnerInt> MariaAttackCount =
            new List<SourceCompositeOwnerInt>();
        public List<SourceCompositeOwnerInt> KobyRoundStacks =
            new List<SourceCompositeOwnerInt>();
        public List<SourceCompositeOwnerInt> KobyLastUpgradeWave =
            new List<SourceCompositeOwnerInt>();
        public List<SourceCompositePeriodicClock> PeriodicDue =
            new List<SourceCompositePeriodicClock>();
        public List<string> ApproachInside = new List<string>();
        public List<string> ApproachRolled = new List<string>();
        public List<SourceCompositeTargetClaim> HakiClaims =
            new List<SourceCompositeTargetClaim>();
        public List<long> KingExecutionUsed = new List<long>();
        public List<SourceCompositeKingSlowRevision> KingSlowRevisions =
            new List<SourceCompositeKingSlowRevision>();
        public List<SourceCompositeOwnerClock> BonneyStunReady =
            new List<SourceCompositeOwnerClock>();
    }

    [Serializable]
    public sealed class SourceKingDelayedPulse
    {
        public long OwnerId;
        public string EnemyId;
        public bool StoryLane;
        public long LaneRevision;
        public double CastAt;
        public int Tick;
        public long Revision; // Slow renewal receipt. Fire uses zero.
    }

    public sealed partial class CharacterCombatRuntime
    {
        private const string KingSlowPulseKind = "source-king-slow-v1";
        private const string KingFirePulseKind = "source-king-fire-v1";

        private static string EncodeKingPulse(SourceKingDelayedPulse p) =>
            JsonUtility.ToJson(p);

        private static SourceKingDelayedPulse ReadKingPulse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 4096)
                return null;
            try
            {
                var p = JsonUtility.FromJson<SourceKingDelayedPulse>(json);
                return p != null && EncodeKingPulse(p) == json ? p : null;
            }
            catch (ArgumentException) { return null; }
        }

        private static List<SourceCompositeOwnerInt> CaptureIntOwners(
            Dictionary<long, int> source)
        {
            var result = new List<SourceCompositeOwnerInt>();
            var keys = new List<long>(source.Keys);
            keys.Sort();
            foreach (long id in keys)
                result.Add(new SourceCompositeOwnerInt {
                    OwnerId = id, Value = source[id]
                });
            return result;
        }

        private SourceCompositeSkillOwnerCheckpoint CaptureCompositeSkillOwners()
        {
            var saved = new SourceCompositeSkillOwnerCheckpoint {
                MariaAttackCount = CaptureIntOwners(sourceModelMariaAttackCount),
                KobyRoundStacks = CaptureIntOwners(sourceModelKobyRoundStacks),
                KobyLastUpgradeWave = CaptureIntOwners(sourceModelKobyLastUpgradeWave)
            };
            var keys = new List<string>(sourceModelEighthPeriodicDue.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                long owner;
                string skill;
                if (!TryReadCompositeSkillKey(key, out owner, out skill))
                    throw new NotSupportedException(
                        "원본 주기 기술의 소유자 형식을 해독할 수 없습니다.");
                saved.PeriodicDue.Add(new SourceCompositePeriodicClock {
                    OwnerId = owner, SkillId = skill,
                    DueAt = sourceModelEighthPeriodicDue[key]
                });
            }
            saved.ApproachInside.AddRange(sourceModelEighthWithinApproach);
            saved.ApproachInside.Sort(StringComparer.Ordinal);
            saved.ApproachRolled.AddRange(sourceModelEighthApproachRolled);
            saved.ApproachRolled.Sort(StringComparer.Ordinal);
            keys = new List<string>(sourceModelEighthHakiClaims.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
                saved.HakiClaims.Add(new SourceCompositeTargetClaim {
                    EnemyId = key, Claimant = sourceModelEighthHakiClaims[key]
                });
            saved.KingExecutionUsed.AddRange(sourceModelKingExecutionUsed);
            saved.KingExecutionUsed.Sort();
            keys = new List<string>(sourceModelKingSlowRevision.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                long owner;
                string enemy;
                if (!TrySplitCompositeOwnerEnemy(key, out owner, out enemy))
                    throw new NotSupportedException(
                        "킹 둔화 갱신 기록의 소유자를 해독할 수 없습니다.");
                saved.KingSlowRevisions.Add(new SourceCompositeKingSlowRevision {
                    OwnerId = owner, EnemyId = enemy,
                    Revision = sourceModelKingSlowRevision[key]
                });
            }
            var ids = new List<long>(sourceModelBonneyStunReadyAt.Keys);
            ids.Sort();
            foreach (long id in ids)
                saved.BonneyStunReady.Add(new SourceCompositeOwnerClock {
                    OwnerId = id, DueAt = sourceModelBonneyStunReadyAt[id]
                });
            return saved;
        }

        private static bool TrySplitCompositeOwnerEnemy(
            string key, out long owner, out string enemy)
        {
            owner = 0; enemy = null;
            int i = key == null ? -1 : key.IndexOf(':');
            if (i < 1 || i == key.Length - 1 ||
                !long.TryParse(key.Substring(0, i), NumberStyles.None,
                    CultureInfo.InvariantCulture, out owner) ||
                owner <= 0) return false;
            enemy = key.Substring(i + 1);
            return !string.IsNullOrWhiteSpace(enemy) && enemy.Length <= 256 &&
                key == owner.ToString(CultureInfo.InvariantCulture) + ":" + enemy;
        }

        private static bool TryReadCompositeSkillKey(
            string key, out long owner, out string skill)
        {
            owner = 0; skill = null;
            int i = key == null ? -1 : key.IndexOf(':');
            if (i < 1 || i == key.Length - 1 ||
                !long.TryParse(key.Substring(0, i), NumberStyles.None,
                    CultureInfo.InvariantCulture, out owner) ||
                owner <= 0) return false;
            skill = key.Substring(i + 1);
            return key == owner.ToString(CultureInfo.InvariantCulture) + ":" +
                skill;
        }

        private static bool CompositeApproachSkill(string id) =>
            id == "MY-08:skill:0" || id == "TR-13:skill:1" ||
            id == "LI-07:skill:2" || id == "L-17:skill:1";

        private static string CompositeCatalogOfSkill(string skill)
        {
            int split = skill == null ? -1 : skill.IndexOf(':');
            return split > 0 ? skill.Substring(0, split) : null;
        }

        private static void ValidateCompositeIntOwners(
            List<SourceCompositeOwnerInt> rows,
            Dictionary<long, SourceCombatActorCheckpoint> actors,
            string catalog, int maxValue, bool zeroAllowed)
        {
            if (rows == null || rows.Count > 100000)
                throw new FormatException("원본 합성 기술 누적 횟수가 없습니다.");
            long last = 0;
            foreach (var row in rows)
            {
                SourceCombatActorCheckpoint actor;
                if (row == null || row.OwnerId <= last ||
                    !actors.TryGetValue(row.OwnerId, out actor) ||
                    actor.CatalogId != catalog ||
                    row.Value < (zeroAllowed ? 0 : 1) ||
                    row.Value > maxValue)
                    throw new FormatException("원본 누적 횟수/유닛이 잘못됐습니다.");
                last = row.OwnerId;
            }
        }

        private static void ValidateCompositeApproach(
            List<string> keys,
            Dictionary<long, SourceCombatActorCheckpoint> actors,
            HashSet<string> enemies)
        {
            if (keys == null || keys.Count > 1000000)
                throw new FormatException("원본 접근 기술 영수증이 없습니다.");
            string last = null;
            foreach (string key in keys)
            {
                long owner;
                string rest;
                if (!TryReadCompositeSkillKey(key, out owner, out rest) ||
                    last != null && StringComparer.Ordinal.Compare(last, key) >= 0)
                    throw new FormatException("접근 기술 영수증 순서가 잘못됐습니다.");
                bool found = false;
                foreach (string skill in new[] {
                    "MY-08:skill:0", "TR-13:skill:1",
                    "LI-07:skill:2", "L-17:skill:1" })
                {
                    string prefix = skill + ":";
                    SourceCombatActorCheckpoint actor;
                    if (rest.StartsWith(prefix, StringComparison.Ordinal) &&
                        actors.TryGetValue(owner, out actor) &&
                        actor.CatalogId == CompositeCatalogOfSkill(skill) &&
                        enemies.Contains(rest.Substring(prefix.Length)))
                        found = true;
                }
                if (!found)
                    throw new FormatException("접근 기록의 시전자/대상 신원이 없습니다.");
                last = key;
            }
        }

        public static void ValidateCompositeSkillOwners(
            SourceCombatRuntimeCheckpoint combat)
        {
            if (combat == null || combat.CompositeSkillOwners == null ||
                combat.CompositeSkillOwners.Version != 1 ||
                combat.Actors == null || combat.Enemies == null ||
                combat.Timed == null || combat.Timed.Pending == null ||
                double.IsNaN(combat.Time) || double.IsInfinity(combat.Time) ||
                combat.Time < 0)
                throw new FormatException("원본 복합 기술 영수증이 없습니다.");
            var state = combat.CompositeSkillOwners;
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var actor in combat.Actors)
            {
                if (actor == null || actor.InstanceId <= 0 ||
                    actors.ContainsKey(actor.InstanceId))
                    throw new FormatException("복합 기술 소유자 신원이 중복됐습니다.");
                actors.Add(actor.InstanceId, actor);
            }
            var enemies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var enemy in combat.Enemies)
                if (enemy == null || string.IsNullOrWhiteSpace(enemy.EnemyId) ||
                    !enemies.Add(enemy.EnemyId))
                    throw new FormatException("복합 기술 적 신원이 중복됐습니다.");
            ValidateCompositeIntOwners(state.MariaAttackCount, actors,
                "D-01", int.MaxValue, false);
            ValidateCompositeIntOwners(state.KobyRoundStacks, actors,
                "TR-24", 6, false);
            ValidateCompositeIntOwners(state.KobyLastUpgradeWave, actors,
                "TR-24", 65, true);
            var kobyWaves = new HashSet<long>();
            foreach (var row in state.KobyLastUpgradeWave)
                kobyWaves.Add(row.OwnerId);
            foreach (var row in state.KobyRoundStacks)
                if (!kobyWaves.Contains(row.OwnerId))
                    throw new FormatException("코비 성장 기준 라운드가 없습니다.");
            if (state.PeriodicDue == null || state.PeriodicDue.Count > 100000)
                throw new FormatException("원본 주기 공격 시계가 없습니다.");
            string lastPeriodic = null;
            foreach (var row in state.PeriodicDue)
            {
                SourceCombatActorCheckpoint actor;
                string key = row == null ? null :
                    row.OwnerId.ToString(CultureInfo.InvariantCulture) + ":" +
                    row.SkillId;
                if (row == null || row.OwnerId <= 0 ||
                    !actors.TryGetValue(row.OwnerId, out actor) ||
                    !((row.SkillId == "E-11:skill:2" &&
                       actor.CatalogId == "E-11") ||
                      (row.SkillId == "TR-13:skill:0" &&
                       actor.CatalogId == "TR-13")) ||
                    double.IsNaN(row.DueAt) || double.IsInfinity(row.DueAt) ||
                    row.DueAt < 0 ||
                    (lastPeriodic != null &&
                     StringComparer.Ordinal.Compare(lastPeriodic, key) >= 0))
                    throw new FormatException("원본 주기 공격 소유권이 잘못됐습니다.");
                lastPeriodic = key;
            }
            ValidateCompositeApproach(state.ApproachInside, actors, enemies);
            ValidateCompositeApproach(state.ApproachRolled, actors, enemies);
            if (state.HakiClaims == null || state.HakiClaims.Count > 1000000)
                throw new FormatException("원본 패기 점유 영수증이 없습니다.");
            string lastEnemy = null;
            foreach (var claim in state.HakiClaims)
            {
                long claimant;
                string skill;
                if (claim == null || string.IsNullOrWhiteSpace(claim.EnemyId) ||
                    !enemies.Contains(claim.EnemyId) ||
                    !TryReadCompositeSkillKey(claim.Claimant,
                        out claimant, out skill) ||
                    !CompositeApproachSkill(skill) ||
                    skill == "LI-07:skill:2" ||
                    (lastEnemy != null &&
                     StringComparer.Ordinal.Compare(lastEnemy, claim.EnemyId) >= 0))
                    throw new FormatException("원본 패기 대상 점유 기록이 잘못됐습니다.");
                // A sold skill owner can leave a native target claim alive
                // until that enemy dies. Keep that historical owner ID.
                SourceCombatActorCheckpoint owner;
                if (actors.TryGetValue(claimant, out owner) &&
                    owner.CatalogId != CompositeCatalogOfSkill(skill))
                    throw new FormatException("패기 점유자가 다른 유닛이 됐습니다.");
                lastEnemy = claim.EnemyId;
            }
            if (state.KingExecutionUsed == null ||
                state.KingExecutionUsed.Count > 100000)
                throw new FormatException("킹 처형 후속 영수증이 없습니다.");
            long lastOwner = 0;
            foreach (long id in state.KingExecutionUsed)
            {
                SourceCombatActorCheckpoint actor;
                if (id <= lastOwner || !actors.TryGetValue(id, out actor) ||
                    actor.CatalogId != "LI-10")
                    throw new FormatException("킹 참수 1회권의 원래 주인이 없습니다.");
                lastOwner = id;
            }
            if (state.BonneyStunReady == null ||
                state.BonneyStunReady.Count > 100000)
                throw new FormatException("보니 기절 재사용 시계가 없습니다.");
            lastOwner = 0;
            foreach (var row in state.BonneyStunReady)
            {
                SourceCombatActorCheckpoint actor;
                if (row == null || row.OwnerId <= lastOwner ||
                    !actors.TryGetValue(row.OwnerId, out actor) ||
                    actor.CatalogId != "TR-21" ||
                    double.IsNaN(row.DueAt) || double.IsInfinity(row.DueAt) ||
                    row.DueAt < 0 || row.DueAt > combat.Time + 15)
                    throw new FormatException("보니 기절 재사용권 소유자가 잘못됐습니다.");
                lastOwner = row.OwnerId;
            }
            if (state.KingSlowRevisions == null ||
                state.KingSlowRevisions.Count > 1000000)
                throw new FormatException("킹 대상 둔화 갱신 순번이 없습니다.");
            var slow = new Dictionary<string, long>(StringComparer.Ordinal);
            string lastKey = null;
            foreach (var row in state.KingSlowRevisions)
            {
                SourceCombatActorCheckpoint actor;
                string key = row == null ? null :
                    row.OwnerId.ToString(CultureInfo.InvariantCulture) + ":" +
                    row.EnemyId;
                if (row == null || !actors.TryGetValue(row.OwnerId, out actor) ||
                    actor.CatalogId != "LI-10" ||
                    !enemies.Contains(row.EnemyId) ||
                    row.Revision <= 0 ||
                    (lastKey != null &&
                     StringComparer.Ordinal.Compare(lastKey, key) >= 0))
                    throw new FormatException("킹 둔화 이력의 유닛·적이 잘못됐습니다.");
                slow.Add(key, row.Revision);
                lastKey = key;
            }
            var seenKingPackets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pending in combat.Timed.Pending)
            {
                if (pending == null)
                    throw new FormatException("원본 합성 예약 항목이 없습니다.");
                bool slowKind = pending.Kind == KingSlowPulseKind;
                bool fireKind = pending.Kind == KingFirePulseKind;
                if (!slowKind && !fireKind) continue;
                var row = ReadKingPulse(pending.Payload);
                SourceCombatActorCheckpoint actor;
                if (!combat.HasSourceModelBasicAttacks || row == null ||
                    !actors.TryGetValue(row.OwnerId, out actor) ||
                    actor.CatalogId != "LI-10" ||
                    pending.CasterId != row.OwnerId.ToString(
                        CultureInfo.InvariantCulture) ||
                    pending.TargetId != row.EnemyId ||
                    string.IsNullOrWhiteSpace(row.EnemyId) ||
                    !enemies.Contains(row.EnemyId) ||
                    row.LaneRevision < 0 ||
                    row.LaneRevision > actor.SourceLaneRevision ||
                    (row.LaneRevision == actor.SourceLaneRevision &&
                     row.StoryLane != actor.StoryDeployed) ||
                    double.IsNaN(row.CastAt) ||
                    double.IsInfinity(row.CastAt) ||
                    row.CastAt < 0 || row.CastAt > combat.Time ||
                    row.Tick < 1 || row.Tick > (slowKind ? 3 : 10) ||
                    pending.At != row.CastAt +
                        (slowKind ? row.Tick : row.Tick * .27) ||
                    pending.At < combat.Time ||
                    (slowKind ?
                        (row.Revision <= 0 ||
                         !slow.ContainsKey(pending.CasterId + ":" + row.EnemyId) ||
                         row.Revision >
                            slow[pending.CasterId + ":" + row.EnemyId]) :
                        row.Revision != 0) ||
                    !seenKingPackets.Add(pending.Kind + ":" +
                        pending.CasterId + ":" + row.EnemyId + ":" +
                        row.CastAt.ToString("R", CultureInfo.InvariantCulture) +
                        ":" + row.Revision.ToString(CultureInfo.InvariantCulture) +
                        ":" + row.Tick.ToString(CultureInfo.InvariantCulture)))
                    throw new FormatException("킹 예약 DoT의 원본 시계·소유권이 잘못됐습니다.");
            }
            if (!combat.HasSourceModelBasicAttacks &&
                (state.MariaAttackCount.Count != 0 ||
                 state.KobyRoundStacks.Count != 0 ||
                 state.KobyLastUpgradeWave.Count != 0 ||
                 state.PeriodicDue.Count != 0 ||
                 state.ApproachInside.Count != 0 ||
                 state.ApproachRolled.Count != 0 ||
                 state.HakiClaims.Count != 0 ||
                 state.KingExecutionUsed.Count != 0 ||
                 state.KingSlowRevisions.Count != 0 ||
                 state.BonneyStunReady.Count != 0))
                throw new FormatException("기본 전투에 원본 합성 스킬 상태가 있습니다.");
        }

        private void RestoreCompositeSkillOwners(
            SourceCompositeSkillOwnerCheckpoint saved)
        {
            if (saved == null ||
                sourceModelMariaAttackCount.Count != 0 ||
                sourceModelKobyRoundStacks.Count != 0 ||
                sourceModelKobyLastUpgradeWave.Count != 0 ||
                sourceModelEighthPeriodicDue.Count != 0 ||
                sourceModelEighthWithinApproach.Count != 0 ||
                sourceModelEighthApproachRolled.Count != 0 ||
                sourceModelEighthHakiClaims.Count != 0 ||
                sourceModelKingExecutionUsed.Count != 0 ||
                sourceModelKingSlowRevision.Count != 0 ||
                sourceModelBonneyStunReadyAt.Count != 0)
                throw new InvalidOperationException("합성 기술 기록은 새 전투에만 복원합니다.");
            foreach (var row in saved.MariaAttackCount)
                sourceModelMariaAttackCount.Add(row.OwnerId, row.Value);
            foreach (var row in saved.KobyRoundStacks)
                sourceModelKobyRoundStacks.Add(row.OwnerId, row.Value);
            foreach (var row in saved.KobyLastUpgradeWave)
                sourceModelKobyLastUpgradeWave.Add(row.OwnerId, row.Value);
            foreach (var row in saved.PeriodicDue)
                sourceModelEighthPeriodicDue.Add(
                    row.OwnerId.ToString(CultureInfo.InvariantCulture) +
                    ":" + row.SkillId, row.DueAt);
            foreach (string key in saved.ApproachInside)
                sourceModelEighthWithinApproach.Add(key);
            foreach (string key in saved.ApproachRolled)
                sourceModelEighthApproachRolled.Add(key);
            foreach (var row in saved.HakiClaims)
                sourceModelEighthHakiClaims.Add(row.EnemyId, row.Claimant);
            foreach (long id in saved.KingExecutionUsed)
                sourceModelKingExecutionUsed.Add(id);
            foreach (var row in saved.KingSlowRevisions)
                sourceModelKingSlowRevision.Add(
                    row.OwnerId.ToString(CultureInfo.InvariantCulture) +
                    ":" + row.EnemyId, row.Revision);
            foreach (var row in saved.BonneyStunReady)
                sourceModelBonneyStunReadyAt.Add(row.OwnerId, row.DueAt);
        }

        private Action CreateKingSlowPulse(SourceKingDelayedPulse p)
        {
            SpatialCombatEnemy originalTarget;
            if (!enemies.TryGetValue(p.EnemyId, out originalTarget))
                return () => { };
            return () => {
                CharacterInstance actor;
                SpatialCombatEnemy enemy;
                long latest;
                if (!characters.TryGetValue(p.OwnerId, out actor) ||
                    !CanFinishSourceModelDelayedAttack(p.OwnerId,
                        p.StoryLane, p.LaneRevision) ||
                    !enemies.TryGetValue(p.EnemyId, out enemy) ||
                    !ReferenceEquals(originalTarget, enemy) ||
                    !sourceModelKingSlowRevision.TryGetValue(
                        InstanceKey(p.OwnerId) + ":" + p.EnemyId, out latest) ||
                    latest != p.Revision) return;
                var skill = FindSourceSkill(actor, "단일 둔화");
                if (!SourceModelTwelfthKingSlowSkill(skill)) return;
                ApplySourceModelSeventhTimedHit(actor, enemy,
                    skill.Name, 300000, "magical");
            };
        }

        private Action CreateKingFirePulse(SourceKingDelayedPulse p)
        {
            SpatialCombatEnemy originalTarget;
            if (!enemies.TryGetValue(p.EnemyId, out originalTarget))
                return () => { };
            return () => {
                CharacterInstance actor;
                SpatialCombatEnemy enemy;
                if (!characters.TryGetValue(p.OwnerId, out actor) ||
                    !CanFinishSourceModelDelayedAttack(p.OwnerId,
                        p.StoryLane, p.LaneRevision) ||
                    !enemies.TryGetValue(p.EnemyId, out enemy) ||
                    !ReferenceEquals(originalTarget, enemy)) return;
                var skill = FindSourceSkill(actor, "화재");
                if (!SourceModelTwelfthKingFireSkill(skill)) return;
                ApplySourceModelSeventhTimedHit(actor, enemy,
                    skill.Name, 42500, "magical");
            };
        }

        private Action RestoreSourceKingTimedEffect(
            string kind, string payload, string casterId, string targetId)
        {
            if (kind != KingSlowPulseKind && kind != KingFirePulseKind)
                return null;
            var p = ReadKingPulse(payload);
            CharacterInstance actor;
            if (p == null ||
                !characters.TryGetValue(p.OwnerId, out actor) ||
                actor.Catalog.Id != "LI-10" ||
                casterId != InstanceKey(p.OwnerId) || targetId != p.EnemyId)
                return null;
            // Owner left this lane. The queued callback would have been
            // inert at its original due time; never replay its last attack.
            if (p.LaneRevision < SourceModelLaneRevision(p.OwnerId))
                return () => { };
            if (p.LaneRevision != SourceModelLaneRevision(p.OwnerId) ||
                p.StoryLane != IsStoryDeployed(p.OwnerId) ||
                !enemies.ContainsKey(targetId)) return null;
            var skill = FindSourceSkill(actor, kind == KingSlowPulseKind ?
                "단일 둔화" : "화재");
            if (kind == KingSlowPulseKind)
                return SourceModelTwelfthKingSlowSkill(skill) ?
                    CreateKingSlowPulse(p) : null;
            return SourceModelTwelfthKingFireSkill(skill) ?
                CreateKingFirePulse(p) : null;
        }
    }
}
