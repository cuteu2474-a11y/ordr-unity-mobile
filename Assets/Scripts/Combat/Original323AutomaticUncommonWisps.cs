using System;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // CSE/CSF: 13 Uncommon units at Lq[0] center (-3760,3904).
    // CSB/CSC: 33 Special units at player-zero start (-3776,4480).
    // Class name retained for compatibility. Rare/Legendary stay queued;
    // their bonus abilities / complete creation worlds remain unconnected.
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
        public bool IsVerified(string id) =>
            (id == WispResourceIds.Uncommon || id == WispResourceIds.Special) && source.IsVerified(id);
        public WispReward Roll(string id)
        {
            if (!IsVerified(id)) throw new InvalidOperationException("이 등급의 자동 위습은 미연결입니다.");
            return source.Roll(id);
        }
        public bool TryGetPosition(string id, out double x, out double y)
        {
            x = id == WispResourceIds.Special ? -3776 : -3760;
            y = id == WispResourceIds.Special ? 4480 : 3904;
            return IsVerified(id);
        }
        public bool TryApplyOriginalAbility(long instanceId, string rawcode) => false;
    }
}
