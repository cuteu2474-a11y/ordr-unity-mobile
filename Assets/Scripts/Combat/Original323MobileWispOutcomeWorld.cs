using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // A mobile-only INDEPENDENT native-pool lane. Its values follow the
    // original uniform pool weights, not an observed Warcraft native seed.
    public sealed class Original323MobileNativePoolRandom : IOriginal323NativeUnitPoolRandom
    {
        private readonly IOriginalWispRandom source;
        public Original323MobileNativePoolRandom(IOriginalWispRandom mobileRandom)
        {
            source = mobileRandom ?? throw new ArgumentNullException(nameof(mobileRandom));
        }
        public int NextInclusive(int minimum, int maximum)
        {
            return source.NextInclusive(minimum, maximum);
        }
    }

    // Source reward OUTCOMES for mobile shell mode: 13/33/42+8/60 units,
    // including the 99:1 Rare branch. This does not reproduce the unknown
    // Warcraft native unit-pool RNG seed or activate A0YE gameplay.
    public sealed class Original323MobileWispOutcomeRoller :
        IVerifiedWispRoller, IOriginalWispSpawnPositionPort, IOriginalWispBonusAbilityPort
    {
        private readonly OriginalWispRewardCatalog source;

        public Original323MobileWispOutcomeRoller(string originalPoolJson,
            IOriginalWispRandom scriptLane,
            IOriginal323NativeUnitPoolRandom nativeLane)
        {
            if (scriptLane == null || nativeLane == null)
                throw new ArgumentNullException(nameof(scriptLane));
            if (ReferenceEquals(scriptLane, nativeLane))
                throw new ArgumentException("위습 분기와 유닛 풀 난수는 별도 경로여야 합니다.");
            source = OriginalWispRewardCatalog.FromOriginalJassJson(
                originalPoolJson, scriptLane, nativeLane);
        }

        public bool IsVerified(string id) =>
            WispResourceIds.IsAuto(id) && source.IsVerified(id);

        public WispReward Roll(string id)
        {
            if (!IsVerified(id))
                throw new InvalidOperationException("원본 자동 위습 종류가 아닙니다.");
            return source.Roll(id);
        }

        public bool TryGetPosition(string id, out double x, out double y)
        {
            x = 0; y = 0;
            if (!IsVerified(id)) return false;
            // CSE uses Lq[0] centre; CSC/CSI/CR6 use player-zero start.
            x = id == WispResourceIds.Uncommon ? -3760.0 : -3776.0;
            y = id == WispResourceIds.Uncommon ? 3904.0 : 4480.0;
            return true;
        }

        // Never report an unimplemented native A0YE ability as applied.
        public bool TryApplyOriginalAbility(long unitId, string rawcode) => false;
    }

    [Serializable]
    public sealed class Original323DeferredAbilityCheckpoint
    {
        public long InstanceId;
        public string AbilityRawcode;
    }

    [Serializable]
    public sealed class Original323WispDeferredAbilityRegistryState
    {
        public int Version = 1;
        public List<Original323DeferredAbilityCheckpoint> Pending =
            new List<Original323DeferredAbilityCheckpoint>();
    }

    // The user explicitly requested empty character shells first.
    // A0YE/A0YZ ownership is retained per spawned instance for manual reroll,
    // not a combat buff or native execution witness. This never authorizes combat parity.
    public sealed class Original323WispDeferredAbilityRegistry :
        IOriginalWispDeferredBonusPort
    {
        private readonly CharacterCombatRuntime combat;
        private readonly Dictionary<long, string> pending =
            new Dictionary<long, string>();

        public Original323WispDeferredAbilityRegistry(CharacterCombatRuntime runtime)
        {
            combat = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool IsAttachedTo(CharacterCombatRuntime owner) =>
            ReferenceEquals(combat, owner);

        public Original323WispDeferredAbilityRegistryState CaptureState()
        {
            var state = new Original323WispDeferredAbilityRegistryState();
            foreach (var pair in pending)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(
                        combat.Roster.Get(pair.Key).UnitId))
                        throw new InvalidOperationException(
                            "원본 A0YE/A0YZ 소유 유닛의 ID가 없습니다.");
                }
                catch (KeyNotFoundException error)
                {
                    throw new InvalidOperationException(
                        "삭제된 유닛의 원본 능력 기록이 남았습니다.", error);
                }
                state.Pending.Add(new Original323DeferredAbilityCheckpoint {
                    InstanceId = pair.Key, AbilityRawcode = pair.Value
                });
            }
            state.Pending.Sort((a, b) => a.InstanceId.CompareTo(b.InstanceId));
            ValidateState(state, combat.Roster.CaptureState());
            return state;
        }

        public static void ValidateState(
            Original323WispDeferredAbilityRegistryState state,
            UnitRosterState roster)
        {
            if (state == null || state.Version != 1 ||
                state.Pending == null || state.Pending.Count > 10000 ||
                roster == null || roster.Units == null)
                throw new FormatException(
                    "위습/도박 원본 보류 능력 상태가 잘못됐습니다.");
            var owned = new HashSet<long>();
            foreach (var unit in roster.Units)
                if (unit != null && unit.InstanceId > 0)
                    owned.Add(unit.InstanceId);
            var unique = new HashSet<long>();
            foreach (var entry in state.Pending)
                if (entry == null || entry.InstanceId <= 0 ||
                    !unique.Add(entry.InstanceId) ||
                    !owned.Contains(entry.InstanceId) ||
                    (entry.AbilityRawcode != "A0YE" &&
                     entry.AbilityRawcode != "A0YZ"))
                    throw new FormatException(
                        "소유권 없는 유닛의 보류 능력 또는 중복 원본 능력이 있습니다.");
        }

        // Restore ownership only on a NEW registry attached to the already
        // reconstructed real roster. Do not apply Warcraft native abilities,
        // refund a roll or issue a new A0YE event.
        public void RestoreState(Original323WispDeferredAbilityRegistryState state)
        {
            ValidateState(state, combat.Roster.CaptureState());
            if (pending.Count != 0)
                throw new InvalidOperationException(
                    "이미 사용 중인 위습 원본 능력 장부에는 복원할 수 없습니다.");
            foreach (var entry in state.Pending)
                pending.Add(entry.InstanceId, entry.AbilityRawcode);
        }

        public bool TryDeferOriginalAbility(long instanceId, string rawcode)
        {
            if (instanceId <= 0 || (rawcode != "A0YE" && rawcode != "A0YZ") ||
                pending.ContainsKey(instanceId))
                return false;
            try
            {
                if (string.IsNullOrEmpty(combat.Roster.Get(instanceId).UnitId)) return false;
            }
            catch (KeyNotFoundException) { return false; }
            pending.Add(instanceId, rawcode);
            return true;
        }

        public bool HasAbility(long instanceId, string rawcode)
        {
            string value;
            return pending.TryGetValue(instanceId, out value) && value == rawcode;
        }

        public void Forget(long instanceId)
        {
            pending.Remove(instanceId);
        }

        public int PendingCount
        {
            get
            {
                var missing = new List<long>();
                foreach (var pair in pending)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(combat.Roster.Get(pair.Key).UnitId))
                            missing.Add(pair.Key);
                    }
                    catch (KeyNotFoundException) { missing.Add(pair.Key); }
                }
                foreach (long id in missing) pending.Remove(id);
                return pending.Count;
            }
        }
    }
}
