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
        private readonly IOriginalWispSpawnPositionPort rareSourcePosition;
        private readonly IOriginalWispBonusAbilityPort verifiedA0ye;
        private readonly bool rareEnabled;
        public Original323AutomaticUncommonWisps(string originalPoolJson,
            IOriginalWispRandom mobileNativePoolRandom)
        {
            source = OriginalWispRewardCatalog.FromOriginalJassJson(
                originalPoolJson, mobileNativePoolRandom);
        }
        // Opt-in original split RNG (BFi versus Warcraft native unit pool),
        // source position and actual A0YE effect. No source witness means
        // the 99% Rare branch remains pending; never silently grant Gaimon.
        public Original323AutomaticUncommonWisps(string originalPoolJson,
            IOriginalWispRandom verifiedScriptBranchRandom,
            IOriginal323NativeUnitPoolRandom verifiedNativePool,
            IOriginalWispSpawnPositionPort verifiedRarePosition,
            IOriginalWispBonusAbilityPort verifiedA0yeAbility)
        {
            if (verifiedScriptBranchRandom == null ||
                verifiedNativePool == null || verifiedRarePosition == null ||
                verifiedA0yeAbility == null)
                throw new ArgumentNullException(nameof(verifiedRarePosition),
                    "원본 희귀위습의 이중 RNG·좌표·A0YE 적용 공급자 모두 필요");
            source = OriginalWispRewardCatalog.FromOriginalJassJson(
                originalPoolJson,verifiedScriptBranchRandom,verifiedNativePool);
            rareSourcePosition = verifiedRarePosition;
            verifiedA0ye = verifiedA0yeAbility;
            rareEnabled = true;
        }
        public bool IsVerified(string id) =>
            (id == WispResourceIds.Uncommon || id == WispResourceIds.Special ||
             (rareEnabled && id == WispResourceIds.Rare)) && source.IsVerified(id);
        public WispReward Roll(string id)
        {
            if (!IsVerified(id)) throw new InvalidOperationException("이 등급의 자동 위습은 미연결입니다.");
            return source.Roll(id);
        }
        public bool TryGetPosition(string id, out double x, out double y)
        {
            if (id == WispResourceIds.Rare)
            {
                if (!rareEnabled) { x=0; y=0; return false; }
                return rareSourcePosition.TryGetPosition(id,out x,out y);
            }
            x = id == WispResourceIds.Special ? -3776 : -3760;
            y = id == WispResourceIds.Special ? 4480 : 3904;
            return IsVerified(id);
        }
        public bool TryApplyOriginalAbility(long instanceId, string rawcode) =>
            rareEnabled && instanceId>0 && rawcode=="A0YE" &&
            verifiedA0ye.TryApplyOriginalAbility(instanceId,rawcode);
    }
}
