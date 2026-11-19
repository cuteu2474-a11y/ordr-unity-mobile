using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Combat
{
    [Serializable]
    public sealed class SourceBulkDelayedPayload
    {
        public long OwnerId, LaneRevision;
        public string EnemyId, SkillId, School;
        public bool StoryLane;
        public double CastAt, DelaySeconds;
        public int Tick;
        public double IntervalSeconds, DurationSeconds, RawDamage;
        public double SlowPercent, SlowDurationSeconds;
    }

    // Typed, interruption-safe mobile DoT and delayed slow. Snapshot the
    // already chosen cast packet: loading must never roll or cast again.
    public sealed partial class CharacterCombatRuntime
    {
        private const string BulkDelayedDamageKind = "source-bulk-dot-v1";
        private const string BulkDelayedSlowKind = "source-bulk-slow-v1";
        private static bool BulkDelayedFinite(double v) =>
            !double.IsNaN(v) && !double.IsInfinity(v);

        private static string SerializeBulkDelayed(SourceBulkDelayedPayload p) =>
            JsonUtility.ToJson(p);

        private static SourceBulkDelayedPayload ReadBulkDelayed(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 4096) return null;
            try
            {
                var value = JsonUtility.FromJson<SourceBulkDelayedPayload>(json);
                return value != null && SerializeBulkDelayed(value) == json ?
                    value : null;
            }
            catch (ArgumentException) { return null; }
        }

        private static bool IsBulkDelayedSkill(string catalog, string id,
            bool slow, string school)
        {
            if (slow)
                return catalog == "TR-01" && school == null &&
                    (id == "TR-01:skill:1" || id == "TR-01:skill:2");
            if (school == "fixed")
                return catalog == "MY-05" && id == "MY-05:skill:3";
            return school == "magical" &&
                (catalog == "MY-01" && id == "MY-01:skill:3" ||
                 catalog == "MY-07" && id == "MY-07:skill:2" ||
                 catalog == "I-02" && id == "I-02:skill:2" ||
                 catalog == "MY-05" && id == "MY-05:skill:3");
        }

        private static bool IsBulkDelayedPacketValid(
            SourceBulkDelayedPayload p, CombatTimedEffectState queued,
            SourceCombatActorCheckpoint owner, double time, bool slow)
        {
            if (p == null || queued == null || owner == null ||
                p.OwnerId != owner.InstanceId || p.OwnerId <= 0 ||
                queued.CasterId != p.OwnerId.ToString(
                    CultureInfo.InvariantCulture) ||
                p.EnemyId != queued.TargetId ||
                string.IsNullOrWhiteSpace(p.EnemyId) ||
                p.EnemyId.Length > 256 ||
                !IsBulkDelayedSkill(owner.CatalogId, p.SkillId,
                    slow, p.School) ||
                p.LaneRevision < 0 ||
                p.LaneRevision > owner.SourceLaneRevision ||
                (p.LaneRevision == owner.SourceLaneRevision &&
                 p.StoryLane != owner.StoryDeployed) ||
                !BulkDelayedFinite(p.CastAt) || p.CastAt < 0 ||
                p.CastAt > time ||
                !BulkDelayedFinite(p.DelaySeconds) || p.DelaySeconds <= 0 ||
                p.DelaySeconds > 3600 ||
                !BulkDelayedFinite(queued.At) || queued.At < time ||
                queued.At != p.CastAt + p.DelaySeconds) return false;
            if (slow)
                return p.DelaySeconds == 3 && p.Tick == 0 &&
                    p.IntervalSeconds == 0 && p.DurationSeconds == 0 &&
                    p.RawDamage == 0 &&
                    BulkDelayedFinite(p.SlowPercent) && p.SlowPercent >= 0 &&
                    BulkDelayedFinite(p.SlowDurationSeconds) &&
                    p.SlowDurationSeconds > 0 &&
                    p.SlowDurationSeconds <= 3600;
            if (p.SlowPercent != 0 || p.SlowDurationSeconds != 0 ||
                !BulkDelayedFinite(p.IntervalSeconds) ||
                !BulkDelayedFinite(p.DurationSeconds) ||
                p.IntervalSeconds <= 0 || p.DurationSeconds <= 0 ||
                p.DurationSeconds > 3600 ||
                !BulkDelayedFinite(p.RawDamage) || p.RawDamage < 0 ||
                p.Tick < 1 || p.Tick > 120 ||
                p.DelaySeconds != p.Tick * p.IntervalSeconds)
                return false;
            double count = Math.Floor(
                p.DurationSeconds / p.IntervalSeconds + .000001);
            if (!BulkDelayedFinite(count) || count < 1 || count > 120 ||
                p.Tick > count) return false;
            // These two already-modeled followups have fixed call-site
            // timing, unlike the remaining tooltip-derived intervals.
            if (p.SkillId == "I-02:skill:2" &&
                (p.IntervalSeconds != .15 || p.DurationSeconds != 2) ||
                p.SkillId == "MY-05:skill:3" &&
                (p.IntervalSeconds != .16 || p.DurationSeconds != 4))
                return false;
            return true;
        }

        public static void ValidateSourceBulkDelayedState(
            SourceCombatRuntimeCheckpoint combat)
        {
            if (combat == null || combat.Version != 4 ||
                combat.Actors == null || combat.Enemies == null ||
                combat.Timed == null || combat.Timed.Pending == null ||
                !BulkDelayedFinite(combat.Time) || combat.Time < 0)
                throw new FormatException("추가 자동기술 예약의 체크포인트가 없습니다.");
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var a in combat.Actors)
                if (a == null || a.InstanceId <= 0 ||
                    actors.ContainsKey(a.InstanceId))
                    throw new FormatException("지속 피해 시전자 ID가 중복됐습니다.");
                else actors.Add(a.InstanceId, a);
            var enemies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in combat.Enemies)
                if (e == null || string.IsNullOrWhiteSpace(e.EnemyId) ||
                    !enemies.Add(e.EnemyId))
                    throw new FormatException("지속 피해 대상 ID가 중복됐습니다.");
            foreach (var queued in combat.Timed.Pending)
            {
                if (queued == null)
                    throw new FormatException("예약 피해 항목이 누락됐습니다.");
                bool slow = queued.Kind == BulkDelayedSlowKind;
                if (!slow && queued.Kind != BulkDelayedDamageKind) continue;
                var p = ReadBulkDelayed(queued.Payload);
                SourceCombatActorCheckpoint owner;
                if (!combat.HasSourceModelBasicAttacks || p == null ||
                    !actors.TryGetValue(p.OwnerId, out owner) ||
                    !enemies.Contains(p.EnemyId) ||
                    !IsBulkDelayedPacketValid(p, queued, owner,
                        combat.Time, slow))
                    throw new FormatException(
                        "추가 자동기술의 지연 타격/슬로우 신원이 잘못됐습니다.");
            }
        }

        private Action BuildBulkDelayedAction(SourceBulkDelayedPayload p,
            CharacterInstance actor, SpatialCombatEnemy original,
            ImportedCombatSkill skill, bool slow)
        {
            return () => {
                SpatialCombatEnemy current;
                if (!CanFinishSourceModelDelayedAttack(p.OwnerId,
                        p.StoryLane, p.LaneRevision) ||
                    !enemies.TryGetValue(p.EnemyId, out current) ||
                    !ReferenceEquals(current, original)) return;
                if (slow)
                    BulkStatus(actor, current, skill,
                        CombatEffectType.MovementSlowPercent,
                        p.SlowPercent, p.SlowDurationSeconds, "lingering-slow");
                else
                    BulkDamage(actor, current, skill,
                        p.RawDamage, p.School);
            };
        }

        private Action RestoreBulkDelayedAction(string kind, string json,
            string casterId, string targetId)
        {
            bool slow = kind == BulkDelayedSlowKind;
            if (!slow && kind != BulkDelayedDamageKind) return null;
            var p = ReadBulkDelayed(json);
            CharacterInstance actor;
            SpatialCombatEnemy enemy;
            if (p == null || !characters.TryGetValue(p.OwnerId, out actor) ||
                !enemies.TryGetValue(p.EnemyId, out enemy) ||
                casterId != InstanceKey(p.OwnerId) ||
                targetId != p.EnemyId ||
                !IsBulkDelayedSkill(actor.Catalog.Id, p.SkillId,
                    slow, p.School))
                return null;
            if (p.LaneRevision < SourceModelLaneRevision(p.OwnerId))
                return () => { };
            if (p.LaneRevision != SourceModelLaneRevision(p.OwnerId) ||
                p.StoryLane != IsStoryDeployed(p.OwnerId)) return null;
            var skill = AdditionalAutoSkill(actor, p.SkillId);
            if (skill == null) return null;
            if (slow)
            {
                bool matched = false;
                foreach (var c in skill.Components)
                    if (c.Type == "lingeringSlow" &&
                        (c.Number("percent") ?? 0) == p.SlowPercent &&
                        (c.Number("durationSeconds") ?? 2) ==
                            p.SlowDurationSeconds)
                        matched = true;
                if (!matched) return null;
            }
            return BuildBulkDelayedAction(p, actor, enemy, skill, slow);
        }

        private void ScheduleBulkDelayedDamage(CharacterInstance actor,
            SpatialCombatEnemy enemy, ImportedCombatSkill skill,
            double amount, string school, double interval,
            double duration, int tick)
        {
            var p = new SourceBulkDelayedPayload {
                OwnerId = actor.Id, EnemyId = enemy.Id, SkillId = skill.Id,
                StoryLane = IsStoryDeployed(actor.Id),
                LaneRevision = SourceModelLaneRevision(actor.Id),
                CastAt = effectExecutor.Time,
                IntervalSeconds = interval, DurationSeconds = duration,
                DelaySeconds = tick * interval, Tick = tick,
                RawDamage = amount, School = school
            };
            effectExecutor.ScheduleRestorableSourceAttack(
                InstanceKey(actor.Id), enemy.Id, p.DelaySeconds,
                BulkDelayedDamageKind, SerializeBulkDelayed(p),
                BuildBulkDelayedAction(p, actor, enemy, skill, false));
        }

        private void ScheduleBulkDelayedSlow(CharacterInstance actor,
            SpatialCombatEnemy enemy, ImportedCombatSkill skill,
            double percent, double duration)
        {
            var p = new SourceBulkDelayedPayload {
                OwnerId = actor.Id, EnemyId = enemy.Id, SkillId = skill.Id,
                StoryLane = IsStoryDeployed(actor.Id),
                LaneRevision = SourceModelLaneRevision(actor.Id),
                CastAt = effectExecutor.Time, DelaySeconds = 3,
                SlowPercent = percent, SlowDurationSeconds = duration
            };
            effectExecutor.ScheduleRestorableSourceAttack(
                InstanceKey(actor.Id), enemy.Id, p.DelaySeconds,
                BulkDelayedSlowKind, SerializeBulkDelayed(p),
                BuildBulkDelayedAction(p, actor, enemy, skill, true));
        }
    }
}
