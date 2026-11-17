using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private static List<SourceFusionClockReceipt> CaptureFusionClocks(
            Dictionary<long, double> source)
        {
            var rows = new List<SourceFusionClockReceipt>();
            foreach (var pair in source)
                rows.Add(new SourceFusionClockReceipt {
                    OwnerId = pair.Key, Until = pair.Value
                });
            rows.Sort((a, b) => a.OwnerId.CompareTo(b.OwnerId));
            return rows;
        }

        private static List<SourceFusionCountReceipt> CaptureFusionCounts(
            Dictionary<long, int> source)
        {
            var rows = new List<SourceFusionCountReceipt>();
            foreach (var pair in source)
                rows.Add(new SourceFusionCountReceipt {
                    OwnerId = pair.Key, Count = pair.Value
                });
            rows.Sort((a, b) => a.OwnerId.CompareTo(b.OwnerId));
            return rows;
        }

        private SourceFusionNativeWorldCheckpoint CaptureSourceFusionNativeOwners()
        {
            var world = new SourceFusionNativeWorldCheckpoint {
                NextPulseId = sourceFusionNativePulseSerial,
                RogerCooldowns = CaptureFusionClocks(sourceModelRogerInfernoReadyAt),
                TesoroCounts = CaptureFusionCounts(sourceModelTesoroFrostCounts)
            };
            foreach (var pair in sourceModelNativeTesoroSpawnA11S)
                if (enemies.ContainsKey(pair.Key))
                    world.EnemyLevels.Add(new SourceFusionEnemyLevelReceipt {
                        EnemyId = pair.Key, Level = pair.Value
                    });
            world.EnemyLevels.Sort((a, b) =>
                StringComparer.Ordinal.Compare(a.EnemyId, b.EnemyId));
            foreach (var pair in sourceModelLineItemMagicFactorAtSpawn)
                if (enemies.ContainsKey(pair.Key))
                    world.EnemyMagicFactors.Add(new SourceFusionEnemyFactorReceipt {
                        EnemyId = pair.Key, Factor = pair.Value
                    });
            world.EnemyMagicFactors.Sort((a, b) =>
                StringComparer.Ordinal.Compare(a.EnemyId, b.EnemyId));
            world.ActiveSharkOwners.AddRange(sourceModelFusionSharkScheduled);
            world.ActiveSharkOwners.Sort();
            return world;
        }

        // Uta B61/B6r group rotation, h0AF form and the seven-second h04O
        // independent attacker still have external/native state. They must
        // not be silently lost when no anonymous timer happens to be queued.
        private void RejectUnrestorableSourceUtaState()
        {
            if (sourceModelUtaSongElapsed != 0 ||
                sourceModelUtaSongRotations != 0 ||
                sourceModelUtaSongSoldierKeys.Count != 0 ||
                sourceModelUtaSongActive.Count != 0 ||
                sourceModelUtaSongPending.Count != 0 ||
                sourceModelUtaIntroStarted.Count != 0 ||
                sourceModelUtaCycleReadyAt.Count != 0 ||
                sourceModelUtaSleepUntil.Count != 0 ||
                sourceModelUtaManaGateUntil.Count != 0)
                throw new NotSupportedException(
                    "우타 B61/B6r의 변신/노래 그룹·동적 주기 영수증이 아직 복원되지 않았습니다.");
            if (sourceModelRules != null)
                foreach (var actor in characters.Values)
                    if (sourceModelRules.SourceFormRawcodeOf(
                            InstanceKey(actor.Id)) == "h0AF")
                        throw new NotSupportedException(
                            "우타 h0AF 네이티브 변신 공격객체가 복원되지 않았습니다.");
        }

        private static Dictionary<long, string> ValidateFusionActorIds(
            SourceCombatRuntimeCheckpoint state)
        {
            var actors = new Dictionary<long, string>();
            foreach (var actor in state.Actors)
            {
                if (actor == null || actor.InstanceId <= 0 ||
                    actors.ContainsKey(actor.InstanceId))
                    throw new FormatException(
                        "Fusion Native 소유자 인스턴스가 중복됐습니다.");
                actors.Add(actor.InstanceId, actor.CatalogId);
            }
            return actors;
        }

        private static Dictionary<string, CharacterEnemyKind> ValidateFusionEnemies(
            SourceCombatRuntimeCheckpoint state)
        {
            var enemies = new Dictionary<string, CharacterEnemyKind>(
                StringComparer.Ordinal);
            foreach (var enemy in state.Enemies)
            {
                if (enemy == null || string.IsNullOrWhiteSpace(enemy.EnemyId) ||
                    enemies.ContainsKey(enemy.EnemyId))
                    throw new FormatException(
                        "Fusion Native 원본 적 실물 ID가 중복됐습니다.");
                enemies.Add(enemy.EnemyId, enemy.Kind);
            }
            return enemies;
        }

        public static void ValidateSourceFusionNativeOwners(
            SourceCombatRuntimeCheckpoint combat)
        {
            if (combat == null || combat.FusionNative == null ||
                combat.FusionNative.Version != 1 ||
                combat.FusionNative.NextPulseId < 0 ||
                combat.FusionNative.RogerCooldowns == null ||
                combat.FusionNative.TesoroCounts == null ||
                combat.FusionNative.EnemyLevels == null ||
                combat.FusionNative.EnemyMagicFactors == null ||
                combat.FusionNative.ActiveSharkOwners == null ||
                combat.Actors == null || combat.Enemies == null ||
                combat.Timed == null || combat.Timed.Pending == null ||
                !FusionFinite(combat.Time) || combat.Time < 0)
                throw new FormatException(
                    "Fusion Native 누적/예약 상태를 복원할 수 없습니다.");
            var save = combat.FusionNative;
            var actors = ValidateFusionActorIds(combat);
            var enemies = ValidateFusionEnemies(combat);
            if (save.RogerCooldowns.Count > actors.Count ||
                save.TesoroCounts.Count > actors.Count ||
                save.ActiveSharkOwners.Count > actors.Count ||
                save.EnemyLevels.Count > enemies.Count ||
                save.EnemyMagicFactors.Count > enemies.Count)
                throw new FormatException(
                    "Fusion Native 누적 수신자가 실제 전투 인원보다 많습니다.");
            var ready = new Dictionary<long, double>();
            long lastId = 0;
            foreach (var row in save.RogerCooldowns)
            {
                string catalog;
                if (row == null || row.OwnerId <= lastId ||
                    !actors.TryGetValue(row.OwnerId, out catalog) ||
                    catalog != "I-04" ||
                    !FusionFinite(row.Until) || row.Until < 0 ||
                    row.Until > combat.Time + 150.000001)
                    throw new FormatException("로저 150초 재시전 시각이 잘못됐습니다.");
                ready.Add(row.OwnerId, row.Until);
                lastId = row.OwnerId;
            }
            lastId = 0;
            foreach (var row in save.TesoroCounts)
            {
                string catalog;
                if (row == null || row.OwnerId <= lastId ||
                    !actors.TryGetValue(row.OwnerId, out catalog) ||
                    catalog != "E-11" || row.Count < 0 || row.Count > 2)
                    throw new FormatException("테조로 원본 3타 카운터가 잘못됐습니다.");
                lastId = row.OwnerId;
            }
            string lastEnemy = null;
            foreach (var row in save.EnemyLevels)
            {
                CharacterEnemyKind kind;
                if (row == null || string.IsNullOrWhiteSpace(row.EnemyId) ||
                    !enemies.TryGetValue(row.EnemyId, out kind) ||
                    (lastEnemy != null &&
                     StringComparer.Ordinal.Compare(lastEnemy, row.EnemyId) >= 0) ||
                    !(kind == CharacterEnemyKind.Line ||
                      kind == CharacterEnemyKind.Boss ||
                      kind == CharacterEnemyKind.Enraged) ||
                    (kind == CharacterEnemyKind.Enraged ?
                        row.Level != 1 : row.Level < 0))
                    throw new FormatException(
                        "원본 몬스터 생성 때 A11S 증빙이 잘못됐습니다.");
                lastEnemy = row.EnemyId;
            }
            lastEnemy = null;
            foreach (var row in save.EnemyMagicFactors)
            {
                CharacterEnemyKind kind;
                if (row == null || string.IsNullOrWhiteSpace(row.EnemyId) ||
                    !enemies.TryGetValue(row.EnemyId, out kind) ||
                    kind != CharacterEnemyKind.Line ||
                    (lastEnemy != null &&
                     StringComparer.Ordinal.Compare(lastEnemy, row.EnemyId) >= 0) ||
                    !FusionFinite(row.Factor) ||
                    row.Factor < 0 || row.Factor > 10)
                    throw new FormatException(
                        "몬스터 생성 당시 AI19/A04Q 마법 보정이 잘못됐습니다.");
                lastEnemy = row.EnemyId;
            }
            var sharks = new HashSet<long>();
            lastId = 0;
            foreach (long id in save.ActiveSharkOwners)
            {
                string catalog;
                if (id <= lastId || !actors.TryGetValue(id, out catalog) ||
                    catalog != "SR-03" || !sharks.Add(id))
                    throw new FormatException("S-샤크 내면의 불꽃 순번이 잘못됐습니다.");
                lastId = id;
            }
            var pulseIds = new HashSet<long>();
            var sharkCycles = new HashSet<long>();
            var rogerKinds = new HashSet<string>(StringComparer.Ordinal);
            var sharkAllyTargets = new HashSet<string>(StringComparer.Ordinal);
            var sharkAllyCounts = new Dictionary<string, int>(
                StringComparer.Ordinal);
            foreach (var pending in combat.Timed.Pending)
            {
                if (pending == null)
                    throw new FormatException("Fusion Native 예약 장부에 빈 타격이 있습니다.");
                bool related = pending.Kind == FusionRogerStunKind ||
                    pending.Kind == FusionRogerImpactKind ||
                    pending.Kind == FusionTesoroImpactKind ||
                    pending.Kind == FusionSharkCycleKind ||
                    pending.Kind == FusionSharkAllyKind;
                if (!related) continue;
                var pulse = DecodeFusionPulse(pending.Payload);
                string catalog;
                if (!combat.HasSourceModelBasicAttacks ||
                    pulse == null || pulse.Kind != pending.Kind ||
                    !actors.TryGetValue(pulse.OwnerId, out catalog) ||
                    pulse.Serial <= 0 || pulse.Serial > save.NextPulseId ||
                    !pulseIds.Add(pulse.Serial) ||
                    pending.CasterId != pulse.OwnerId.ToString(
                        CultureInfo.InvariantCulture) ||
                    pending.TargetId != FusionPulseTarget(pulse.Serial) ||
                    !FusionFinite(pulse.CastAt) ||
                    pulse.CastAt < 0 || pulse.CastAt > combat.Time ||
                    !FusionFinite(pulse.DelaySeconds) ||
                    pending.At != pulse.CastAt + pulse.DelaySeconds ||
                    pending.At < combat.Time ||
                    !FusionFinite(pulse.CenterX) ||
                    !FusionFinite(pulse.CenterY) ||
                    pulse.LaneRevision < 0)
                    throw new FormatException(
                        "Fusion Native 타격의 고유 ID/시간/시전자가 일치하지 않습니다.");
                SourceCombatActorCheckpoint actor = null;
                foreach (var owner in combat.Actors)
                    if (owner.InstanceId == pulse.OwnerId) { actor = owner; break; }
                if (actor == null ||
                    pulse.LaneRevision > actor.SourceLaneRevision ||
                    (pulse.Kind != FusionSharkCycleKind &&
                     pulse.LaneRevision == actor.SourceLaneRevision &&
                     pulse.StoryLane != actor.StoryDeployed))
                    throw new FormatException("Fusion Native 전장 리비전이 바뀌었습니다.");
                if (pending.Kind == FusionRogerStunKind ||
                    pending.Kind == FusionRogerImpactKind)
                {
                    double until;
                    if (catalog != "I-04" ||
                        !ready.TryGetValue(pulse.OwnerId, out until) ||
                        until != pulse.CastAt + 150 ||
                        string.IsNullOrWhiteSpace(pulse.TargetId) ||
                        pulse.TargetId.Length > 256 ||
                        (pending.Kind == FusionRogerImpactKind &&
                         !enemies.ContainsKey(pulse.TargetId) &&
                         pulse.LaneRevision == actor.SourceLaneRevision) ||
                        pulse.AllyId != 0 || pulse.GoldCap != 0 ||
                        pulse.ThirdImpact ||
                        pulse.DelaySeconds !=
                            (pending.Kind == FusionRogerStunKind ? .01 : 2.2) ||
                        !rogerKinds.Add(pulse.OwnerId + ":" + pending.Kind))
                        throw new FormatException(
                            "로저 인페르노의 원본 대상·150초 영수증이 없습니다.");
                }
                else if (pending.Kind == FusionTesoroImpactKind)
                {
                    if (catalog != "E-11" ||
                        string.IsNullOrWhiteSpace(pulse.TargetId) ||
                        pulse.TargetId.Length > 256 ||
                        pulse.AllyId != 0 ||
                        pulse.GoldCap < 0 || pulse.GoldCap > 250000 ||
                        pulse.DelaySeconds != .45)
                        throw new FormatException(
                            "테조로 B8S의 골드·타격 시각이 잘못됐습니다.");
                }
                else if (pending.Kind == FusionSharkCycleKind)
                {
                    if (catalog != "SR-03" || !sharks.Contains(pulse.OwnerId) ||
                        !sharkCycles.Add(pulse.OwnerId) ||
                        !string.IsNullOrEmpty(pulse.TargetId) || pulse.AllyId != 0 ||
                        pulse.GoldCap != 0 || pulse.ThirdImpact ||
                        !(pulse.DelaySeconds == .21 ||
                          pulse.DelaySeconds == 19.9))
                        throw new FormatException(
                            "S-샤크 주기 타격의 소유자·시계가 없습니다.");
                }
                else if (pending.Kind == FusionSharkAllyKind)
                {
                    string allyCatalog;
                    if (catalog != "SR-03" || !sharks.Contains(pulse.OwnerId) ||
                        !string.IsNullOrEmpty(pulse.TargetId) || pulse.AllyId <= 0 ||
                        pulse.AllyId == pulse.OwnerId ||
                        !actors.TryGetValue(pulse.AllyId, out allyCatalog) ||
                        pulse.GoldCap != 0 || pulse.ThirdImpact ||
                        !(pulse.DelaySeconds == .1 || pulse.DelaySeconds == .2))
                        throw new FormatException(
                            "S-샤크의 이전 예약 아군이 존재하지 않습니다.");
                    string key = pulse.OwnerId + ":" +
                        pulse.CastAt.ToString("R", CultureInfo.InvariantCulture);
                    string targetKey = key + ":" + pulse.AllyId;
                    if (!sharkAllyTargets.Add(targetKey))
                        throw new FormatException(
                            "같은 아군에게 내면의 불꽃이 중복 예약됐습니다.");
                    int count;
                    sharkAllyCounts.TryGetValue(key, out count);
                    sharkAllyCounts[key] = checked(count + 1);
                    if (count >= 2)
                        throw new FormatException(
                            "S-샤크의 원본 최대 2명 아군 상한을 넘었습니다.");
                }
            }
            if (sharkCycles.Count != sharks.Count)
                throw new FormatException(
                    "S-샤크 반복 예약이 누락됐거나 중복됐습니다.");
            if (!combat.HasSourceModelBasicAttacks &&
                (save.NextPulseId != 0 || save.RogerCooldowns.Count != 0 ||
                 save.TesoroCounts.Count != 0 || save.EnemyLevels.Count != 0 ||
                 save.EnemyMagicFactors.Count != 0 || sharks.Count != 0))
                throw new FormatException(
                    "원본 미연결 전투에 Fusion Native 상태를 주입할 수 없습니다.");
        }

        private void RestoreSourceFusionNativeOwners(
            SourceFusionNativeWorldCheckpoint state)
        {
            if (state == null || sourceFusionNativePulseSerial != 0 ||
                sourceModelRogerInfernoReadyAt.Count != 0 ||
                sourceModelTesoroFrostCounts.Count != 0 ||
                sourceModelNativeTesoroSpawnA11S.Count != 0 ||
                sourceModelLineItemMagicFactorAtSpawn.Count != 0 ||
                sourceModelFusionSharkScheduled.Count != 0)
                throw new InvalidOperationException(
                    "Fusion Native는 원본 유닛/적 ID가 복원된 새 월드에서만 연결합니다.");
            if ((state.ActiveSharkOwners.Count > 0 ||
                 state.RogerCooldowns.Count > 0 ||
                 state.TesoroCounts.Count > 0) &&
                sourceModelFusionNativeReceipt == null)
                throw new NotSupportedException(
                    "Fusion Native 원본 구매 영수증 공급자가 없습니다.");
            if (state.ActiveSharkOwners.Count > 0 &&
                SourceModelFusionNativeBalance(
                    Original323FusionNativeFollowupLedger.SharkCenId) <= 0)
                throw new NotSupportedException(
                    "S-샤크 내면의 불꽃 해금 기록이 없습니다.");
            if (state.RogerCooldowns.Count > 0 &&
                SourceModelFusionNativeBalance(
                    Original323FusionNativeFollowupLedger.RogerHelperId) <= 0)
                throw new NotSupportedException(
                    "로저 인페르노 원본 해금 영수증이 없습니다.");
            foreach (var owner in state.RogerCooldowns)
                if (SourceModelFusionNativeBalance(
                        Original323SourceNativeActionKeys.UnlockResource(
                            owner.OwnerId)) <= 0)
                    throw new NotSupportedException(
                        "로저 개인 인페르노 해금 영수증이 없습니다.");
            if (state.TesoroCounts.Count > 0 &&
                SourceModelFusionNativeBalance(
                    Original323FusionNativeFollowupLedger.TesoroHelperId) <= 0)
                throw new NotSupportedException(
                    "테조로 원본 B8S 해금 영수증이 없습니다.");
            sourceFusionNativePulseSerial = state.NextPulseId;
            foreach (var row in state.RogerCooldowns)
                sourceModelRogerInfernoReadyAt.Add(row.OwnerId, row.Until);
            foreach (var row in state.TesoroCounts)
                sourceModelTesoroFrostCounts.Add(row.OwnerId, row.Count);
            foreach (var row in state.EnemyLevels)
                sourceModelNativeTesoroSpawnA11S.Add(row.EnemyId, row.Level);
            foreach (var row in state.EnemyMagicFactors)
                sourceModelLineItemMagicFactorAtSpawn.Add(row.EnemyId, row.Factor);
            foreach (long owner in state.ActiveSharkOwners)
                sourceModelFusionSharkScheduled.Add(owner);
            // No cooldown, gold, random selection, buff or projectile executes here.
        }
    }
}
