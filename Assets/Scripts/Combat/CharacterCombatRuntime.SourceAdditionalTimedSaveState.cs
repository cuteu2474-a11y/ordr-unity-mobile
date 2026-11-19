using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Combat
{
    [Serializable]
    public sealed class SourceAdditionalTimedPayload
    {
        public long OwnerId;
        public string EnemyId;
        public string SkillId;
        public bool StoryLane;
        public long LaneRevision;
        public double CastAt;
        public double DelaySeconds;
        public int Tick;
        public double IntervalSeconds;
        public double DurationSeconds;
        // RN-02: per-second magic, TR-20: UNBUFFED base physical per tick,
        // I-12: delayed movement-slow percent.
        public double OriginalAmount;
    }

    // Known tooltip-model closures share the queue-owned, typed clock.
    // No skill is re-cast, no original roll is consumed during reconstruction.
    public sealed partial class CharacterCombatRuntime
    {
        private const string AdditionalTimedWhitebeardSlowKind =
            "source-additional-whitebeard-slow-v1";
        private const string AdditionalTimedNarutoDotKind =
            "source-additional-naruto-dot-v1";
        private const string AdditionalTimedZoroDotKind =
            "source-additional-zoro-dot-v1";

        private static bool AdditionalTimedFinite(double v) =>
            !double.IsNaN(v) && !double.IsInfinity(v);

        private static string EncodeAdditionalTimed(SourceAdditionalTimedPayload p) =>
            JsonUtility.ToJson(p);

        private static SourceAdditionalTimedPayload DecodeAdditionalTimed(
            string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 4096)
                return null;
            try
            {
                var data = JsonUtility.FromJson<SourceAdditionalTimedPayload>(json);
                return data != null && EncodeAdditionalTimed(data) == json ?
                    data : null;
            }
            catch (ArgumentException) { return null; }
        }

        private static bool ValidAdditionalTimedState(
            SourceAdditionalTimedPayload p, CombatTimedEffectState queued,
            SourceCombatActorCheckpoint owner, double time, string kind)
        {
            if (p == null || queued == null || owner == null ||
                p.OwnerId <= 0 || p.OwnerId != owner.InstanceId ||
                queued.CasterId != p.OwnerId.ToString(
                    CultureInfo.InvariantCulture) ||
                queued.TargetId != p.EnemyId ||
                string.IsNullOrWhiteSpace(p.EnemyId) || p.EnemyId.Length > 256 ||
                p.LaneRevision < 0 ||
                p.LaneRevision > owner.SourceLaneRevision ||
                (p.LaneRevision == owner.SourceLaneRevision &&
                 p.StoryLane != owner.StoryDeployed) ||
                !AdditionalTimedFinite(p.CastAt) || p.CastAt < 0 ||
                p.CastAt > time ||
                !AdditionalTimedFinite(p.DelaySeconds) ||
                p.DelaySeconds <= 0 || p.DelaySeconds > 3600 ||
                !AdditionalTimedFinite(queued.At) || queued.At < time ||
                queued.At != p.CastAt + p.DelaySeconds ||
                !AdditionalTimedFinite(p.OriginalAmount) ||
                p.OriginalAmount < 0 ||
                !AdditionalTimedFinite(p.IntervalSeconds) ||
                !AdditionalTimedFinite(p.DurationSeconds))
                return false;

            if (kind == AdditionalTimedWhitebeardSlowKind)
                return owner.CatalogId == "I-12" &&
                    p.SkillId == "I-12:skill:2" && p.Tick == 0 &&
                    p.IntervalSeconds == 0 && p.DelaySeconds == 3.5 &&
                    p.DurationSeconds > 0 && p.DurationSeconds <= 3600 &&
                    p.OriginalAmount <= 100;
            if (kind == AdditionalTimedNarutoDotKind)
                return owner.CatalogId == "RN-02" &&
                    p.SkillId == "RN-02:skill:1" &&
                    p.IntervalSeconds == 1 &&
                    p.DurationSeconds > 0 && p.DurationSeconds <= 60 &&
                    p.Tick >= 1 && p.Tick <= 60 &&
                    p.Tick <= Math.Floor(p.DurationSeconds + .000001) &&
                    p.DelaySeconds == p.Tick;
            if (kind == AdditionalTimedZoroDotKind)
            {
                if (owner.CatalogId != "TR-20" ||
                    p.SkillId != "TR-20:skill:0" ||
                    p.IntervalSeconds <= 0 ||
                    p.DurationSeconds <= 0 || p.DurationSeconds > 3600 ||
                    p.Tick < 1 || p.Tick > 120 ||
                    p.DelaySeconds != p.Tick * p.IntervalSeconds)
                    return false;
                double ticks = Math.Floor(
                    p.DurationSeconds / p.IntervalSeconds + .000001);
                return AdditionalTimedFinite(ticks) && ticks >= 1 &&
                    ticks <= 120 && p.Tick <= ticks;
            }
            return false;
        }

        public static void ValidateSourceAdditionalTimedState(
            SourceCombatRuntimeCheckpoint combat)
        {
            if (combat == null || combat.Version != 4 ||
                combat.Actors == null || combat.Enemies == null ||
                combat.Timed == null || combat.Timed.Pending == null ||
                !AdditionalTimedFinite(combat.Time) || combat.Time < 0)
                throw new FormatException(
                    "추가 자동 스킬의 예약 체크포인트가 없습니다.");
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var actor in combat.Actors)
                if (actor == null || actor.InstanceId <= 0 ||
                    actors.ContainsKey(actor.InstanceId))
                    throw new FormatException(
                        "추가 예약 기술 시전자가 중복됐습니다.");
                else actors.Add(actor.InstanceId, actor);
            var enemies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var enemy in combat.Enemies)
                if (enemy == null || string.IsNullOrWhiteSpace(enemy.EnemyId) ||
                    !enemies.Add(enemy.EnemyId))
                    throw new FormatException(
                        "추가 예약 기술 대상이 중복됐습니다.");
            foreach (var queued in combat.Timed.Pending)
            {
                if (queued == null)
                    throw new FormatException("추가 예약 기술의 항목이 없습니다.");
                if (queued.Kind != AdditionalTimedWhitebeardSlowKind &&
                    queued.Kind != AdditionalTimedNarutoDotKind &&
                    queued.Kind != AdditionalTimedZoroDotKind)
                    continue;
                var packet = DecodeAdditionalTimed(queued.Payload);
                SourceCombatActorCheckpoint owner;
                if (!combat.HasSourceModelBasicAttacks ||
                    packet == null ||
                    !actors.TryGetValue(packet.OwnerId, out owner) ||
                    !enemies.Contains(packet.EnemyId) ||
                    !ValidAdditionalTimedState(packet, queued, owner,
                        combat.Time, queued.Kind))
                    throw new FormatException(
                        "지연 추가 기술의 소유자/대상/시계/피해값이 잘못됐습니다.");
            }
        }

        private Action CreateAdditionalTimedAction(
            SourceAdditionalTimedPayload packet, CharacterInstance actor,
            SpatialCombatEnemy originalTarget, ImportedCombatSkill skill,
            string kind)
        {
            return () => {
                SpatialCombatEnemy current;
                if (!CanFinishSourceModelDelayedAttack(packet.OwnerId,
                        packet.StoryLane, packet.LaneRevision) ||
                    !enemies.TryGetValue(packet.EnemyId, out current) ||
                    !ReferenceEquals(current, originalTarget)) return;
                if (kind == AdditionalTimedWhitebeardSlowKind)
                {
                    if (!current.Enemy.IsAlive) return;
                    string source = "practice-tooltip:" +
                        InstanceKey(actor.Id) + ":" + skill.Id + ":lingeringSlow";
                    var status = new CombatEffect(
                        source, CombatEffectType.MovementSlowPercent,
                        CombatEffectOrigin.TriggeredSkill,
                        packet.OriginalAmount, packet.DurationSeconds);
                    current.Enemy.Status.RemoveSource(source);
                    current.Enemy.Status.Apply(status);
                }
                else if (kind == AdditionalTimedNarutoDotKind)
                    ApplySourceModelSeventhTimedHit(actor, current, skill.Name,
                        packet.OriginalAmount, "magical");
                else if (kind == AdditionalTimedZoroDotKind)
                    ApplySourceModelSeventhTimedHit(actor, current, skill.Name,
                        // The original mobile model reads the LATEST buff on
                        // each tick. Never freeze this at the saved cast time.
                        SourceModelSeventhKnownAttackBuff(actor.Id,
                            packet.OriginalAmount), "physical");
            };
        }

        private bool MatchAdditionalTimedSource(
            SourceAdditionalTimedPayload p, ImportedCombatSkill skill,
            string kind)
        {
            if (skill == null || skill.Id != p.SkillId) return false;
            if (kind == AdditionalTimedWhitebeardSlowKind)
                return skill.Components.Count == 3 &&
                    skill.Components[1].Number("durationSeconds") ==
                        p.DelaySeconds &&
                    skill.Components[2].Number("percent") ==
                        p.OriginalAmount &&
                    skill.Components[2].Number("durationSeconds") ==
                        p.DurationSeconds;
            if (kind == AdditionalTimedNarutoDotKind)
                return skill.Components.Count == 1 &&
                    skill.Components[0].Number("perSecond") ==
                        p.OriginalAmount &&
                    skill.Components[0].Number("durationSeconds") ==
                        p.DurationSeconds;
            if (kind == AdditionalTimedZoroDotKind)
            {
                foreach (var component in skill.Components)
                    if (component.Type == "physicalDamageOverTime" &&
                        component.Number("perTick") == p.OriginalAmount &&
                        component.Number("tickIntervalSeconds") ==
                            p.IntervalSeconds &&
                        component.Number("totalDurationSeconds") ==
                            p.DurationSeconds)
                        return true;
            }
            return false;
        }

        private Action RestoreSourceAdditionalTimedEffect(
            string kind, string json, string casterId, string targetId)
        {
            if (kind != AdditionalTimedWhitebeardSlowKind &&
                kind != AdditionalTimedNarutoDotKind &&
                kind != AdditionalTimedZoroDotKind) return null;
            var p = DecodeAdditionalTimed(json);
            CharacterInstance owner;
            SpatialCombatEnemy enemy;
            if (p == null || !characters.TryGetValue(p.OwnerId, out owner) ||
                !enemies.TryGetValue(p.EnemyId, out enemy) ||
                casterId != InstanceKey(p.OwnerId) ||
                targetId != p.EnemyId) return null;
            var skill = AdditionalAutoSkill(owner, p.SkillId);
            if (!MatchAdditionalTimedSource(p, skill, kind)) return null;
            if (p.LaneRevision < SourceModelLaneRevision(p.OwnerId))
                return () => { }; // The lane switch already canceled this.
            if (p.LaneRevision != SourceModelLaneRevision(p.OwnerId) ||
                p.StoryLane != IsStoryDeployed(p.OwnerId)) return null;
            return CreateAdditionalTimedAction(p, owner, enemy, skill, kind);
        }

        private void ScheduleSourceAdditionalTimed(
            CharacterInstance actor, SpatialCombatEnemy enemy,
            ImportedCombatSkill skill, string kind, int tick,
            double interval, double duration, double amount, double delay)
        {
            var p = new SourceAdditionalTimedPayload {
                OwnerId = actor.Id, EnemyId = enemy.Id, SkillId = skill.Id,
                StoryLane = IsStoryDeployed(actor.Id),
                LaneRevision = SourceModelLaneRevision(actor.Id),
                CastAt = effectExecutor.Time, DelaySeconds = delay,
                Tick = tick, IntervalSeconds = interval,
                DurationSeconds = duration, OriginalAmount = amount
            };
            effectExecutor.ScheduleRestorableSourceAttack(
                InstanceKey(actor.Id), enemy.Id, delay, kind,
                EncodeAdditionalTimed(p),
                CreateAdditionalTimedAction(p, actor, enemy, skill, kind));
        }
    }
}
