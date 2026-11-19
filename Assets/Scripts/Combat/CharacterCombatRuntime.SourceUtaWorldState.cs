using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private static bool UtaFinite(double at) =>
            !double.IsNaN(at) && !double.IsInfinity(at);

        private static Dictionary<long, SourceCombatActorCheckpoint> UtaActors(
            SourceCombatRuntimeCheckpoint combat)
        {
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var actor in combat.Actors)
            {
                if (actor == null || actor.InstanceId <= 0 ||
                    actors.ContainsKey(actor.InstanceId))
                    throw new FormatException("우타 원본 소유 유닛 ID가 잘못됐습니다.");
                actors.Add(actor.InstanceId, actor);
            }
            return actors;
        }

        private static bool IsUtaSourceActor(
            Dictionary<long, SourceCombatActorCheckpoint> actors, long id,
            bool requireResources = false)
        {
            SourceCombatActorCheckpoint actor;
            return actors.TryGetValue(id, out actor) &&
                actor.CatalogId == "E-09" &&
                (!requireResources || actor.SourceManagedResources);
        }

        private static HashSet<long> ValidateUtaOwnerIds(
            List<long> ids, Dictionary<long, SourceCombatActorCheckpoint> actors,
            bool needResources, string description)
        {
            if (ids == null || ids.Count > actors.Count)
                throw new FormatException(description + " 명단이 없습니다.");
            var set = new HashSet<long>();
            long last = 0;
            foreach (long id in ids)
            {
                if (id <= last || !set.Add(id) ||
                    !IsUtaSourceActor(actors, id, needResources))
                    throw new FormatException(description + "의 우타 ID/순번이 잘못됐습니다.");
                last = id;
            }
            return set;
        }

        private static HashSet<long> ValidateUtaOwnerClocks(
            List<SourceUtaClockCheckpoint> clocks,
            Dictionary<long, SourceCombatActorCheckpoint> actors,
            double worldTime, double maxForward, string description)
        {
            if (clocks == null || clocks.Count > actors.Count)
                throw new FormatException(description + " 장부가 없습니다.");
            var set = new HashSet<long>();
            long last = 0;
            foreach (var row in clocks)
            {
                if (row == null || row.OwnerId <= last ||
                    !set.Add(row.OwnerId) ||
                    !IsUtaSourceActor(actors, row.OwnerId, true) ||
                    !UtaFinite(row.At) || row.At < 0 ||
                    row.At > worldTime + maxForward + .00000001)
                    throw new FormatException(description + "의 원본 시각이 잘못됐습니다.");
                last = row.OwnerId;
            }
            return set;
        }

        private static HashSet<long> ValidateUtaGroup(
            List<long> group, Dictionary<long, SourceCombatSummonCheckpoint> songs,
            string name)
        {
            if (group == null || group.Count > songs.Count)
                throw new FormatException(name + " 그룹 명단이 없습니다.");
            var seen = new HashSet<long>();
            long previous = 0;
            foreach (long id in group)
            {
                SourceCombatSummonCheckpoint soldier;
                if (id <= previous || !seen.Add(id) ||
                    !songs.TryGetValue(id, out soldier))
                    throw new FormatException(name + " 그룹의 h04O 실물 소유권이 잘못됐습니다.");
                previous = id;
            }
            return seen;
        }

        public static void ValidateSourceUtaOwners(
            SourceCombatRuntimeCheckpoint combat)
        {
            if (combat == null || combat.Uta == null ||
                combat.Uta.Version != 1 || combat.Uta.NextPulseId < 0 ||
                combat.Actors == null || combat.Summons == null ||
                combat.Timed == null || combat.Timed.Pending == null ||
                !UtaFinite(combat.Time) || combat.Time < 0 ||
                !UtaFinite(combat.Uta.SongElapsed) ||
                combat.Uta.SongElapsed < 0 || combat.Uta.SongElapsed >= 60 ||
                combat.Uta.SongRotations < 0 ||
                combat.Uta.SongRotations > (long)(combat.Time / 60) + 1 ||
                combat.Uta.SongRotations * 60.0 +
                    combat.Uta.SongElapsed > combat.Time + .00000001)
                throw new FormatException("우타 B6m 전역 노래 주기·버전이 잘못됐습니다.");
            var saved = combat.Uta;
            var actors = UtaActors(combat);
            var intro = ValidateUtaOwnerIds(saved.IntroOwners, actors, false,
                "우타 세계의 가희 1회");
            var transformed = ValidateUtaOwnerIds(saved.TransformedOwners, actors,
                true, "우타 h0AF 변신");
            foreach (long owner in transformed)
                if (!intro.Contains(owner))
                    throw new FormatException(
                        "B61 네이티브 변신은 세계의 가희 시계 없이 존재할 수 없습니다.");
            var cycles = ValidateUtaOwnerClocks(saved.CycleClocks, actors,
                combat.Time, 3, "우타 B61 반복");
            ValidateUtaOwnerClocks(saved.SleepClocks, actors,
                combat.Time, 3, "우타 휴식");
            ValidateUtaOwnerClocks(saved.ManaGates, actors,
                combat.Time, .72, "우타 B6r 마나");
            foreach (var row in saved.CycleClocks)
                if (!intro.Contains(row.OwnerId))
                    throw new FormatException(
                        "세계의 가희를 시작하지 않은 B61 시계가 있습니다.");
            foreach (var row in saved.SleepClocks)
                if (!cycles.Contains(row.OwnerId))
                    throw new FormatException(
                        "우타 휴식에는 동일 소유자 B61 반복 시계가 필요합니다.");
            var songs = new Dictionary<long, SourceCombatSummonCheckpoint>();
            foreach (var summon in combat.Summons)
            {
                if (summon == null) throw new FormatException("우타 소환 실물이 없습니다.");
                if (summon.SourceUnitRawcode != "h04O") continue;
                if (songs.ContainsKey(summon.Id) ||
                    !IsUtaSourceActor(actors, summon.OwnerId) ||
                    !summon.HasIndependentCombat ||
                    !summon.IsVirtualAttackEffect ||
                    summon.SkillName != "B6s · 음표 병사" ||
                    summon.Name != "우타 · 음표 병사" ||
                    summon.HasUnresolvedOriginalLootAbility ||
                    !UtaFinite(summon.ExpiresAt) ||
                    summon.ExpiresAt <= combat.Time ||
                    summon.ExpiresAt > combat.Time + 7.00000001)
                    throw new FormatException("우타 h04O 7초 비보유 공격 실물이 잘못됐습니다.");
                songs.Add(summon.Id, summon);
            }
            // Ordinary B6s attack procs can spawn song soldiers BEFORE
            // the manual B7W intro. Group ownership is E-09, not intro.
            var active = ValidateUtaGroup(saved.ActiveSongIds, songs,
                "B6m 활성");
            var pending = ValidateUtaGroup(saved.PendingSongIds, songs,
                "B6m 대기");
            foreach (long id in active)
                if (pending.Contains(id))
                    throw new FormatException("하나의 우타 병사가 두 노래 그룹에 있습니다.");

            var queued = new HashSet<long>();
            var introQueued = new HashSet<long>();
            foreach (var item in combat.Timed.Pending)
            {
                if (item == null) throw new FormatException("원본 우타 예약이 비었습니다.");
                if (item.Kind != UtaIntroSoldierKind &&
                    item.Kind != UtaAttackOrderKind &&
                    item.Kind != UtaHarmonyImpactKind)
                    continue;
                var pulse = ReadUtaPulse(item.Payload);
                SourceCombatActorCheckpoint owner;
                if (!combat.HasSourceModelBasicAttacks ||
                    pulse == null || pulse.Kind != item.Kind ||
                    !actors.TryGetValue(pulse.OwnerId, out owner) ||
                    owner.CatalogId != "E-09" ||
                    pulse.Serial <= 0 || pulse.Serial > saved.NextPulseId ||
                    !queued.Add(pulse.Serial) ||
                    item.CasterId != pulse.OwnerId.ToString(
                        CultureInfo.InvariantCulture) ||
                    item.TargetId != UtaPulseKey(pulse.Serial) ||
                    pulse.LaneRevision < 0 ||
                    pulse.LaneRevision > owner.SourceLaneRevision ||
                    (pulse.LaneRevision == owner.SourceLaneRevision &&
                     pulse.StoryLane != owner.StoryDeployed) ||
                    !UtaFinite(pulse.CastAt) ||
                    pulse.CastAt < 0 || pulse.CastAt > combat.Time ||
                    !UtaFinite(pulse.DelaySeconds) ||
                    !UtaFinite(pulse.CenterX) ||
                    !UtaFinite(pulse.CenterY) ||
                    item.At < combat.Time ||
                    item.At != pulse.CastAt + pulse.DelaySeconds ||
                    pulse.SongIds == null ||
                    pulse.SongIds.Count > combat.NextSummonId)
                    throw new FormatException(
                        "우타 지연 타격의 시전·타이머·전장·원본 순번이 잘못됐습니다.");
                if (item.Kind == UtaIntroSoldierKind)
                {
                    if (pulse.DelaySeconds != 1 ||
                        pulse.SongCount != 0 ||
                        pulse.SongIds.Count != 0 ||
                        !string.IsNullOrEmpty(pulse.OriginalPrimaryId) ||
                        !intro.Contains(pulse.OwnerId) ||
                        !introQueued.Add(pulse.OwnerId))
                        throw new FormatException(
                            "우타 세계의 가희 1회 병사 예약이 중복됐습니다.");
                }
                else
                {
                    if ((item.Kind == UtaAttackOrderKind &&
                         pulse.DelaySeconds != .02) ||
                        (item.Kind == UtaHarmonyImpactKind &&
                         pulse.DelaySeconds != SourceModelUtaB6rRules.WindupSeconds) ||
                        pulse.SongCount <= 0 ||
                        pulse.SongCount > combat.NextSummonId ||
                        string.IsNullOrWhiteSpace(pulse.OriginalPrimaryId) ||
                        pulse.OriginalPrimaryId.Length > 256 ||
                        (item.Kind == UtaHarmonyImpactKind &&
                         pulse.SongIds.Count != 0) ||
                        (item.Kind == UtaAttackOrderKind &&
                         pulse.SongIds.Count != pulse.SongCount))
                        throw new FormatException(
                            "우타 B6o/B6r 대상·병사 수·시간이 잘못됐습니다.");
                    var checkedIds = new HashSet<long>();
                    foreach (long summonId in pulse.SongIds)
                    {
                        SourceCombatSummonCheckpoint live;
                        if (summonId <= 0 || summonId > combat.NextSummonId ||
                            !checkedIds.Add(summonId) ||
                            (songs.TryGetValue(summonId, out live) &&
                             live.OwnerId != pulse.OwnerId))
                            throw new FormatException(
                                "우타 B6o 원본 병사 예약에 다른 유닛이 있습니다.");
                    }
                }
            }
            if (!combat.HasSourceModelBasicAttacks &&
                (saved.NextPulseId != 0 || saved.SongElapsed != 0 ||
                 saved.SongRotations != 0 || intro.Count != 0 ||
                 transformed.Count != 0 || saved.ActiveSongIds.Count != 0 ||
                 saved.PendingSongIds.Count != 0 ||
                 saved.CycleClocks.Count != 0 ||
                 saved.SleepClocks.Count != 0 ||
                 saved.ManaGates.Count != 0))
                throw new FormatException(
                    "일반 전투의 원본 우타 상태 복원은 지원하지 않습니다.");
        }

        private void RestoreSourceUtaOwners(SourceUtaOwnerCheckpoint save)
        {
            if (save == null || sourceUtaPulseSerial != 0 ||
                sourceModelUtaSongElapsed != 0 ||
                sourceModelUtaSongRotations != 0 ||
                sourceModelUtaIntroStarted.Count != 0 ||
                sourceModelUtaCycleReadyAt.Count != 0 ||
                sourceModelUtaSleepUntil.Count != 0 ||
                sourceModelUtaManaGateUntil.Count != 0 ||
                sourceModelUtaSongSoldierKeys.Count != 0 ||
                sourceModelUtaSongActive.Count != 0 ||
                sourceModelUtaSongPending.Count != 0)
                throw new InvalidOperationException(
                    "우타 B6r/B61 복원은 새로운 전투에서만 허용합니다.");
            bool hasEvidence = save.SongElapsed != 0 ||
                save.SongRotations != 0 || save.NextPulseId != 0 ||
                save.IntroOwners.Count != 0 ||
                save.TransformedOwners.Count != 0 ||
                save.CycleClocks.Count != 0 ||
                save.SleepClocks.Count != 0 ||
                save.ManaGates.Count != 0 ||
                save.ActiveSongIds.Count != 0 ||
                save.PendingSongIds.Count != 0;
            if (hasEvidence &&
                (sourceModelFusionNativeReceipt == null ||
                 SourceModelFusionNativeBalance(
                     Original323FusionNativeFollowupLedger.UtaSongId) <= 0))
                throw new NotSupportedException(
                    "원본 우타 B6m 해금/노래의 구매 증빙을 다시 연결할 수 없습니다.");
            foreach (long ownerId in save.TransformedOwners)
            {
                CharacterInstance actor;
                if (!characters.TryGetValue(ownerId, out actor) ||
                    actor.Mapping == null ||
                    actor.Mapping.AttackSpec.UnitRawCode != "h067" ||
                    actor.Abilities.Resources == null ||
                    !sourceModelManagedResourceRows.ContainsKey(ownerId) ||
                    sourceModelRules == null ||
                    sourceModelRules.SourceObjectAttack("h0AF") == null ||
                    sourceModelRules.SourceFormRawcodeOf(
                        InstanceKey(ownerId)) != null ||
                    SourceModelFusionNativeBalance(
                        Original323SourceNativeActionKeys.UtaUnlockResource(
                            ownerId)) <= 0)
                    throw new NotSupportedException(
                        "원본 우타 h0AF 공격체/자원/해금 자격을 재연결할 수 없습니다.");
            }
            sourceUtaPulseSerial = save.NextPulseId;
            sourceModelUtaSongElapsed = save.SongElapsed;
            sourceModelUtaSongRotations = save.SongRotations;
            foreach (long owner in save.IntroOwners)
                sourceModelUtaIntroStarted.Add(owner);
            foreach (var clock in save.CycleClocks)
                sourceModelUtaCycleReadyAt.Add(clock.OwnerId, clock.At);
            foreach (var clock in save.SleepClocks)
                sourceModelUtaSleepUntil.Add(clock.OwnerId, clock.At);
            foreach (var clock in save.ManaGates)
                sourceModelUtaManaGateUntil.Add(clock.OwnerId, clock.At);
            // Reuse the common restore of the existing virtual independent
            // attacker and its precise attack clock. No new roster unit,
            // summon, random roll, mana gain, or song event occurs here.
            foreach (var soldier in sourceTemporarySummons)
            {
                if (soldier.SourceUnitRawcode != "h04O") continue;
                string key = SummonAttackKey(soldier.Id);
                List<string> owned;
                if (!sourceModelUtaSongSoldierKeys.TryGetValue(
                        soldier.OwnerId, out owned))
                {
                    owned = new List<string>();
                    sourceModelUtaSongSoldierKeys.Add(soldier.OwnerId, owned);
                }
                owned.Add(key);
            }
            foreach (long id in save.ActiveSongIds)
                sourceModelUtaSongActive.Add(SummonAttackKey(id));
            foreach (long id in save.PendingSongIds)
                sourceModelUtaSongPending.Add(SummonAttackKey(id));
            foreach (long id in save.TransformedOwners)
                sourceModelRules.SetSourceForm(InstanceKey(id), "h0AF");
        }
    }
}
