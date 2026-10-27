using System;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // CSE/CSF: original 13 equal-weight non-hero units at Lq[0] = C's
    // center (-3760,3904). Only this reviewed pool is executable. Other
    // Wisp balances are retained until their complete worlds are connected.
    public sealed class Original323AutomaticUncommonWisps : IVerifiedWispRoller,
        IOriginalWispSpawnPositionPort, IOriginalWispBonusAbilityPort
    {
        private readonly OriginalWispRewardCatalog source;
        public Original323AutomaticUncommonWisps(string originalPoolJson,
            IOriginalWispRandom mobileNativePoolRandom)
        {
            source = OriginalWispRewardCatalog.FromOriginalJassJson(
                originalPoolJson, mobileNativePoolRandom);
        }
        public bool IsVerified(string id) => id == WispResourceIds.Uncommon && source.IsVerified(id);
        public WispReward Roll(string id)
        {
            if (!IsVerified(id)) throw new InvalidOperationException("이 등급의 자동 위습은 미연결입니다.");
            return source.Roll(id);
        }
        public bool TryGetPosition(string id, out double x, out double y)
        { x = -3760; y = 3904; return IsVerified(id); }
        public bool TryApplyOriginalAbility(long instanceId, string rawcode) => false;
    }
}
