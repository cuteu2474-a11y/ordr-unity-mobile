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

    // The user explicitly requested empty character shells first.
    // A0YE/A0YZ are retained as UNIMPLEMENTED source metadata per spawned instance,
    // not a native buff or ability. This must never authorize combat parity.
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
