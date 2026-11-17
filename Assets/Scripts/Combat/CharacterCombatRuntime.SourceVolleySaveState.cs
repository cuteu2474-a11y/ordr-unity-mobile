using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Combat
{
    [Serializable]
    public sealed class SourceVolleyWorldCheckpoint
    {
        public int Version = 1;
        public long NextCastId;
    }

    [Serializable]
    public sealed class SourceVolleyPulseCheckpoint
    {
        public long OwnerId;
        public string SkillId;
        public string OriginalPrimaryId;
        public bool StoryLane;
        public long LaneRevision;
        public long CastId;
        public double CastAt;
        public int HitIndex;
    }

    public sealed partial class CharacterCombatRuntime
    {
        private const string SourceVolleyPulseKind = "source-original-volley-v1";
        private long sourceNextVolleyCastId;

        private static bool SourceVolleyFinite(double x) =>
            !double.IsNaN(x) && !double.IsInfinity(x);

        private static string SourceVolleyPulseTarget(long id, long cast, int hit) =>
            "practice-volley:" + id.ToString(CultureInfo.InvariantCulture) +
            ":" + cast.ToString(CultureInfo.InvariantCulture) +
            ":" + hit.ToString(CultureInfo.InvariantCulture);

        private static string EncodeSourceVolleyPulse(SourceVolleyPulseCheckpoint p) =>
            JsonUtility.ToJson(p);

        private static SourceVolleyPulseCheckpoint ReadSourceVolleyPulse(
            string payload)
        {
            if (string.IsNullOrWhiteSpace(payload) || payload.Length > 2048)
                return null;
            try
            {
                var value = JsonUtility.FromJson<SourceVolleyPulseCheckpoint>(payload);
                return value != null && EncodeSourceVolleyPulse(value) == payload ?
                    value : null;
            }
            catch (ArgumentException) { return null; }
        }

        private SourceVolleyWorldCheckpoint CaptureSourceVolleyOwners() =>
            new SourceVolleyWorldCheckpoint { NextCastId = sourceNextVolleyCastId };

        private void RestoreSourceVolleyOwners(SourceVolleyWorldCheckpoint state)
        {
            if (state == null || sourceNextVolleyCastId != 0)
                throw new InvalidOperationException(
                    "원본 연타 시계는 새 전투에서만 복원할 수 있습니다.");
            sourceNextVolleyCastId = state.NextCastId;
        }

        public static void ValidateSourceVolleyOwners(
            SourceCombatRuntimeCheckpoint combat)
        {
            if (combat == null || combat.VolleyOwners == null ||
                combat.VolleyOwners.Version != 1 ||
                combat.VolleyOwners.NextCastId < 0 ||
                combat.Actors == null || combat.Timed == null ||
                combat.Timed.Pending == null ||
                !SourceVolleyFinite(combat.Time) || combat.Time < 0)
                throw new FormatException("원본 연타 체크포인트가 없습니다.");
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var actor in combat.Actors)
            {
                if (actor == null || actor.InstanceId <= 0 ||
                    actors.ContainsKey(actor.InstanceId))
                    throw new FormatException("원본 연타 소유자가 중복됐습니다.");
                actors.Add(actor.InstanceId, actor);
            }
            // In a single cast the queue contains a suffix of the original
            // shots: elapsed shots were already applied and must not replay.
            var castIndex = new Dictionary<long, HashSet<int>>();
            var castSignature = new Dictionary<long, string>();
            var counts = new Dictionary<long, int>();
            foreach (var item in combat.Timed.Pending)
            {
                if (item == null)
                    throw new FormatException("원본 연타 예약 큐가 잘못됐습니다.");
                if (item.Kind != SourceVolleyPulseKind) continue;
                var pulse = ReadSourceVolleyPulse(item.Payload);
                SourceCombatActorCheckpoint owner;
                if (pulse == null || !combat.HasSourceModelBasicAttacks ||
                    !actors.TryGetValue(pulse.OwnerId, out owner))
                    throw new FormatException("연타 예약의 살아 있는 시전자가 없습니다.");
                bool vivi = pulse.SkillId == "E-06:skill:0" &&
                    owner.CatalogId == "E-06";
                bool hancock = pulse.SkillId == "E-12:skill:0" &&
                    owner.CatalogId == "E-12";
                int total = vivi ? 20 : hancock ? 25 : 0;
                double start = vivi ? .12 : .23;
                double interval = vivi ? .055 : .05;
                double offset = start + pulse.HitIndex * interval;
                if (total == 0 || pulse.OwnerId <= 0 ||
                    pulse.CastId <= 0 || pulse.CastId > combat.VolleyOwners.NextCastId ||
                    pulse.HitIndex < 0 || pulse.HitIndex >= total ||
                    string.IsNullOrWhiteSpace(pulse.OriginalPrimaryId) ||
                    pulse.OriginalPrimaryId.Length > 256 ||
                    pulse.LaneRevision < 0 ||
                    pulse.LaneRevision > owner.SourceLaneRevision ||
                    (pulse.LaneRevision == owner.SourceLaneRevision &&
                     pulse.StoryLane != owner.StoryDeployed) ||
                    !SourceVolleyFinite(pulse.CastAt) ||
                    pulse.CastAt < 0 || pulse.CastAt > combat.Time ||
                    !SourceVolleyFinite(item.At) ||
                    item.At < combat.Time ||
                    item.At != pulse.CastAt + offset ||
                    item.CasterId != pulse.OwnerId.ToString(
                        CultureInfo.InvariantCulture) ||
                    item.TargetId != SourceVolleyPulseTarget(
                        pulse.OwnerId, pulse.CastId, pulse.HitIndex))
                    throw new FormatException(
                        "연타 타격의 소유권·전장·시각·기술 ID가 잘못됐습니다.");
                string signature = pulse.OwnerId.ToString(
                        CultureInfo.InvariantCulture) + ":" +
                    pulse.SkillId + ":" + pulse.OriginalPrimaryId + ":" +
                    pulse.LaneRevision.ToString(CultureInfo.InvariantCulture) + ":" +
                    (pulse.StoryLane ? "1" : "0") + ":" +
                    pulse.CastAt.ToString("R", CultureInfo.InvariantCulture);
                string previous;
                if (castSignature.TryGetValue(pulse.CastId, out previous) &&
                    signature != previous)
                    throw new FormatException("동일 연타 시전에 서로 다른 소유자가 있습니다.");
                if (previous == null)
                {
                    castSignature.Add(pulse.CastId, signature);
                    counts.Add(pulse.CastId, total);
                    castIndex.Add(pulse.CastId, new HashSet<int>());
                }
                if (!castIndex[pulse.CastId].Add(pulse.HitIndex))
                    throw new FormatException("같은 연타 틱이 중복 예약됐습니다.");
            }
            foreach (var cast in castIndex)
            {
                int count = counts[cast.Key];
                int first = count;
                foreach (int index in cast.Value)
                    first = Math.Min(first, index);
                if (cast.Value.Count != count - first)
                    throw new FormatException("남은 연타 타격 일부가 누락됐습니다.");
                for (int hit = first; hit < count; hit++)
                    if (!cast.Value.Contains(hit))
                        throw new FormatException("연속 예약의 중간 타격이 없습니다.");
            }
            if (!combat.HasSourceModelBasicAttacks &&
                combat.VolleyOwners.NextCastId != 0)
                throw new FormatException("기본 전투에 원본 연타 기록이 있습니다.");
        }

        private Action CreateSourceVolleyPulse(
            SourceVolleyPulseCheckpoint p, CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            return () => {
                CharacterInstance current;
                if (!characters.TryGetValue(p.OwnerId, out current) ||
                    !ReferenceEquals(actor, current) ||
                    !CanFinishSourceModelDelayedAttack(
                        p.OwnerId, p.StoryLane, p.LaneRevision))
                    return;
                if (p.SkillId == "E-06:skill:0")
                    ApplySourceOriginalViviSalvoPulse(actor, skill, p.HitIndex);
                else
                    ApplySourceOriginalHancockPulse(
                        actor, skill, p.OriginalPrimaryId, p.HitIndex);
                RemoveDefeatedEnemies();
            };
        }

        private Action RestoreSourceVolleyPulse(
            string kind, string payload, string casterId, string targetId)
        {
            if (kind != SourceVolleyPulseKind) return null;
            var p = ReadSourceVolleyPulse(payload);
            CharacterInstance actor;
            if (p == null || !characters.TryGetValue(p.OwnerId, out actor) ||
                casterId != InstanceKey(p.OwnerId) ||
                targetId != SourceVolleyPulseTarget(
                    p.OwnerId, p.CastId, p.HitIndex))
                return null;
            if (p.LaneRevision < SourceModelLaneRevision(p.OwnerId))
                return () => { };
            if (p.LaneRevision != SourceModelLaneRevision(p.OwnerId) ||
                p.StoryLane != IsStoryDeployed(p.OwnerId))
                return null;
            string name = p.SkillId == "E-06:skill:0" ? "광역 난사" :
                p.SkillId == "E-12:skill:0" ? "마나 25회 타격" : null;
            if (name == null || actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode !=
                    (p.SkillId == "E-06:skill:0" ? "h057" : "h05C"))
                return null;
            var skill = FindSourceSkill(actor, name);
            return skill != null && skill.Id == p.SkillId &&
                SourceModelTooltipVolleyCatalog.IsApproved(skill)
                ? CreateSourceVolleyPulse(p, actor, skill) : null;
        }
    }
}
