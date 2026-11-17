using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // B4a is a once-only PLAYER purchase: a satellite is not a roster unit,
    // and its creator may have been sold before the checkpoint was taken.
    [Serializable]
    public sealed class SourceSatelliteAttackCheckpoint
    {
        public long Id;
        public long CreatorId;
        public string OriginalRawcode;
        public double CreatedAt;
        public bool StoryLane;
        public double X;
        public double Y;
        public bool Blocked;
        public string AttackOnceTargetId;
        public double MaximumMana;
        public double Mana;
        public double SourceManaRegenerationPerSecond;
        public double RegenBeginsAt;
        public double RegenEndsAt;
        public double RegenPerSecond;
        public CombatStatusEngineState AttackStatus;
    }

    [Serializable]
    public sealed class SourceSatelliteWorldCheckpoint
    {
        public int Version = 1;
        public long NextSatelliteId;
        public List<SourceSatelliteAttackCheckpoint> Satellites =
            new List<SourceSatelliteAttackCheckpoint>();
    }

    public sealed partial class CharacterCombatRuntime
    {
        private static bool FiniteSatellite(double x) =>
            !double.IsNaN(x) && !double.IsInfinity(x);

        private SourceSatelliteWorldCheckpoint CaptureSourceSatelliteWorld()
        {
            var state = new SourceSatelliteWorldCheckpoint {
                NextSatelliteId = sourceNextSatelliteId
            };
            foreach (var satellite in sourceSatellites)
            {
                SourceIndependentAttacker attacker;
                CombatStatusEngine status;
                string key = SatelliteAttackKey(satellite.Id);
                if (!sourceIndependentAttackers.TryGetValue(key, out attacker) ||
                    !unitStatuses.TryGetValue(key, out status) ||
                    attacker.Rawcode != satellite.OriginalRawcode ||
                    attacker.OwnerInstanceId != satellite.CreatorId ||
                    attacker.ExpiresAt.HasValue ||
                    attacker.ModelShadowZombie || attacker.ModelBlackArrowEffect ||
                    attacker.RenderAsTransientProjectile ||
                    attacker.ShadowInheritedSources.Count != 0)
                    throw new NotSupportedException(
                        "위성의 실물·공격 시계·상태효과 소유권이 일치하지 않습니다.");
                state.Satellites.Add(new SourceSatelliteAttackCheckpoint {
                    Id = satellite.Id,
                    CreatorId = satellite.CreatorId,
                    OriginalRawcode = satellite.OriginalRawcode,
                    CreatedAt = satellite.CreatedAt,
                    StoryLane = attacker.StoryLane,
                    X = attacker.X, Y = attacker.Y,
                    Blocked = attacker.Blocked,
                    AttackOnceTargetId = attacker.AttackOnceTargetId,
                    MaximumMana = attacker.MaximumMana, Mana = attacker.Mana,
                    SourceManaRegenerationPerSecond =
                        attacker.SourceManaRegenPerSecond,
                    RegenBeginsAt = attacker.SatelliteRegenBeginsAt,
                    RegenEndsAt = attacker.SatelliteRegenEndsAt,
                    RegenPerSecond = attacker.SatelliteRegenPerSecond,
                    AttackStatus = status.CaptureState()
                });
            }
            return state;
        }

        public static void ValidateSourceSatelliteWorldState(
            SourceCombatRuntimeCheckpoint combat)
        {
            if (combat == null || combat.SatelliteWorld == null ||
                combat.SatelliteWorld.Version != 1 ||
                combat.SatelliteWorld.NextSatelliteId < 0 ||
                combat.SatelliteWorld.Satellites == null ||
                combat.SatelliteWorld.Satellites.Count > 3 ||
                combat.SatelliteWorld.NextSatelliteId !=
                    combat.SatelliteWorld.Satellites.Count ||
                combat.Actors == null || combat.Attacks == null ||
                combat.Attacks.Scheduler == null ||
                combat.Attacks.Scheduler.Attackers == null ||
                combat.Attacks.Events == null ||
                !FiniteSatellite(combat.Time) || combat.Time < 0)
                throw new FormatException("베가펑크 위성 복원 장부가 없습니다.");
            var state = combat.SatelliteWorld;
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var actor in combat.Actors)
                if (actor == null || actor.InstanceId <= 0 ||
                    actors.ContainsKey(actor.InstanceId))
                    throw new FormatException("위성 제작자 ID와 로스터 신원이 중복됐습니다.");
                else actors.Add(actor.InstanceId, actor);
            var timers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var timer in combat.Attacks.Scheduler.Attackers)
                if (timer == null || string.IsNullOrWhiteSpace(timer.UnitId) ||
                    !timers.Add(timer.UnitId))
                    throw new FormatException("위성 공격 시계 ID가 중복됐습니다.");
            var events = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in combat.Attacks.Events)
                if (entry == null || string.IsNullOrWhiteSpace(entry.UnitId) ||
                    !events.Add(entry.UnitId))
                    throw new FormatException("위성 공격 순번 ID가 중복됐습니다.");
            long previousId = 0;
            double previousCreatedAt = 0;
            var rawcodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var satellite in state.Satellites)
            {
                if (satellite == null ||
                    satellite.Id != previousId + 1 ||
                    satellite.Id > state.NextSatelliteId ||
                    satellite.CreatorId <= 0 ||
                    SourceModelNativeCharacterRules.SatelliteName(
                        satellite.OriginalRawcode) == null ||
                    !rawcodes.Add(satellite.OriginalRawcode) ||
                    !FiniteSatellite(satellite.CreatedAt) ||
                    satellite.CreatedAt < previousCreatedAt ||
                    satellite.CreatedAt < 0 ||
                    satellite.CreatedAt > combat.Time ||
                    !FiniteSatellite(satellite.X) ||
                    !FiniteSatellite(satellite.Y) ||
                    !FiniteSatellite(satellite.MaximumMana) ||
                    !FiniteSatellite(satellite.Mana) ||
                    !FiniteSatellite(satellite.SourceManaRegenerationPerSecond) ||
                    satellite.MaximumMana < 0 || satellite.Mana < 0 ||
                    satellite.Mana > satellite.MaximumMana ||
                    satellite.SourceManaRegenerationPerSecond < 0 ||
                    !FiniteSatellite(satellite.RegenBeginsAt) ||
                    !FiniteSatellite(satellite.RegenEndsAt) ||
                    !FiniteSatellite(satellite.RegenPerSecond) ||
                    satellite.RegenBeginsAt < 0 ||
                    satellite.RegenEndsAt < satellite.RegenBeginsAt ||
                    satellite.RegenBeginsAt > combat.Time ||
                    satellite.RegenPerSecond < 0 ||
                    (satellite.RegenPerSecond == 0 ?
                        (satellite.RegenBeginsAt != 0 ||
                         satellite.RegenEndsAt != 0) :
                        (satellite.RegenPerSecond != 25 ||
                         satellite.RegenEndsAt != satellite.RegenBeginsAt + 3)) ||
                    (satellite.AttackOnceTargetId != null &&
                     (string.IsNullOrWhiteSpace(satellite.AttackOnceTargetId) ||
                      satellite.AttackOnceTargetId.Length > 256)) ||
                    satellite.AttackStatus == null ||
                    satellite.AttackStatus.Time != combat.Time)
                    throw new FormatException(
                        "위성 ID·원본 객체·마나·재생 효과가 변조됐습니다.");
                SourceCombatActorCheckpoint creator;
                if (actors.TryGetValue(satellite.CreatorId, out creator) &&
                    creator.CatalogId != SourceModelNativeCharacterRules.Vegapunk)
                    throw new FormatException(
                        "위성 구매자는 베가펑크의 원래 실물 ID여야 합니다.");
                string key = SatelliteAttackKey(satellite.Id);
                if (!timers.Contains(key) || !events.Contains(key))
                    throw new FormatException(
                        "위성의 독립 공격 시계/사건 순번이 없습니다.");
                CombatStatusEngine.Restore(satellite.AttackStatus);
                previousId = satellite.Id;
                previousCreatedAt = satellite.CreatedAt;
            }
            // No phantom satellite clock may exist outside this purchased set.
            foreach (var timer in timers)
                if (timer.StartsWith("practice-satellite:",
                    StringComparison.Ordinal))
                {
                    bool found = false;
                    foreach (var satellite in state.Satellites)
                        if (timer == SatelliteAttackKey(satellite.Id))
                        { found = true; break; }
                    if (!found)
                        throw new FormatException(
                            "구매 장부에 없는 위성 타이머가 저장됐습니다.");
                }
            foreach (var sequence in events)
                if (sequence.StartsWith("practice-satellite:",
                    StringComparison.Ordinal) && !timers.Contains(sequence))
                    throw new FormatException("위성 공격 순번이 타이머와 다릅니다.");
            if (!combat.HasSourceModelBasicAttacks &&
                (state.Satellites.Count != 0 || state.NextSatelliteId != 0))
                throw new FormatException(
                    "원본 모델이 아닌 전투에 베가펑크 위성을 삽입할 수 없습니다.");
        }

        // Shared wallet receipts are the permanent B4a purchase authority.
        // All three source entitlements must match EXACTLY: missing objects
        // may not be purchased again, nor may objects be minted on resume.
        public static void ValidateSatelliteWalletReceipts(
            SourceCombatRuntimeCheckpoint combat, EconomyWalletState wallet)
        {
            ValidateSourceSatelliteWorldState(combat);
            if (wallet == null || wallet.CompletedRewards == null)
                throw new FormatException("베가펑크 구매 영수증 원장이 없습니다.");
            var completed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var receipt in wallet.CompletedRewards)
                if (string.IsNullOrWhiteSpace(receipt) ||
                    !completed.Add(receipt))
                    throw new FormatException("공유 지갑 구매 영수증이 중복됐습니다.");
            foreach (string rawcode in new[] { "H0AB", "H0AE", "H0AM" })
            {
                bool owned = false;
                foreach (var satellite in combat.SatelliteWorld.Satellites)
                    if (satellite.OriginalRawcode == rawcode) owned = true;
                if (owned != completed.Contains(
                    SourceModelNativeCharacterRules.SatelliteActionId(rawcode)))
                    throw new FormatException(
                        "베가펑크 실물 위성과 공유 지갑의 1회 구매 영수증이 다릅니다.");
            }
        }

        private void RestoreSourceSatelliteWorld(SourceSatelliteWorldCheckpoint saved)
        {
            if (saved == null || sourceSatellites.Count != 0 ||
                sourceNextSatelliteId != 0)
                throw new InvalidOperationException(
                    "베가펑크 위성은 빈 복원 전투에서만 연결할 수 있습니다.");
            // The historical creator ID remains valid even if sold: source
            // object skills come from the immutable TR-30 profile catalog,
            // never from an invented replacement creator.
            var profile = profiles.Find(SourceModelNativeCharacterRules.Vegapunk);
            if (saved.Satellites.Count > 0 &&
                (profile == null ||
                 profile.UnitId != SourceModelNativeCharacterRules.Vegapunk))
                throw new NotSupportedException(
                    "베가펑크 원본 기술 프로필을 다시 찾을 수 없습니다.");
            foreach (var record in saved.Satellites)
            {
                var nativeResources = sourceModelRules?.SourceObjectResources(
                    record.OriginalRawcode);
                double maxMana = nativeResources == null ? 0 :
                    nativeResources.MaximumMana;
                double regen = nativeResources == null ? 0 :
                    nativeResources.ManaRegeneration;
                if (record.MaximumMana != maxMana ||
                    record.SourceManaRegenerationPerSecond != regen ||
                    sourceModelRules?.SourceObjectAttack(
                        record.OriginalRawcode) == null)
                    throw new NotSupportedException(
                        "위성의 원본 공격/마나 SLK 자료가 달라졌습니다.");
                string key = SatelliteAttackKey(record.Id);
                RegisterSourceIndependentAttack(key, record.OriginalRawcode,
                    record.StoryLane, record.X, record.Y, record.CreatorId,
                    null, profile, restoreDetachedPurchasedSatellite: true);
                SourceIndependentAttacker actor = sourceIndependentAttackers[key];
                actor.Blocked = record.Blocked;
                actor.AttackOnceTargetId = record.AttackOnceTargetId;
                actor.Mana = record.Mana;
                actor.SatelliteRegenBeginsAt = record.RegenBeginsAt;
                actor.SatelliteRegenEndsAt = record.RegenEndsAt;
                actor.SatelliteRegenPerSecond = record.RegenPerSecond;
                unitStatuses[key] = CombatStatusEngine.Restore(record.AttackStatus);
                sourceSatellites.Add(new SourceModelVegapunkSatellite(
                    record.Id, record.CreatorId, record.OriginalRawcode,
                    record.CreatedAt));
            }
            sourceNextSatelliteId = saved.NextSatelliteId;
            // AttackPipeline.RestoreState runs AFTER this registration and
            // reinstates exactly the previously saved independent hit clocks.
        }
    }
}
