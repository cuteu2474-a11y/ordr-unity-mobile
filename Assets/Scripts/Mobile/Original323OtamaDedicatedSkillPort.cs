using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // Source-confirmed mobile override: Otama's manual Dango grants exactly
    // +16 wood once per actual owned unit. The existing wallet receipt is
    // authoritative; this class is ONLY the previously missing mobile UI port.
    public sealed class Original323OtamaDedicatedSkillPort : IOriginalDedicatedSkillPort
    {
        private readonly CharacterCombatRuntime combat;
        private readonly EconomyWallet wallet;

        public Original323OtamaDedicatedSkillPort(CharacterCombatRuntime combat,
            EconomyWallet wallet)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        }

        public IReadOnlyList<MobileDedicatedSkillOffer> AvailableSkills()
        {
            var result = new List<MobileDedicatedSkillOffer>();
            foreach (var owned in combat.MobileUnitSnapshot())
            {
                if (owned.Unit.Id != SpecialGradeMobilePolicy.Otama) continue;
                string actionId = SpecialGradeMobilePolicy.OtamaRewardId(owned.InstanceId);
                bool unused = !wallet.HasCompletedTransaction(actionId);
                result.Add(new MobileDedicatedSkillOffer(actionId,
                    "오타마 경단", "목재 +16 · 유닛당 1회",
                    true, unused && combat.Game.Phase == SessionPhase.Running,
                    owned.InstanceId));
            }
            return result.AsReadOnly();
        }

        public ManualActionResult Execute(string skillId)
        {
            foreach (var owned in combat.MobileUnitSnapshot())
            {
                if (owned.Unit.Id != SpecialGradeMobilePolicy.Otama ||
                    skillId != SpecialGradeMobilePolicy.OtamaRewardId(owned.InstanceId))
                    continue;
                if (wallet.HasCompletedTransaction(skillId))
                    return new ManualActionResult(ManualActionState.Rejected,
                        "이 오타마의 경단 보상은 이미 받았습니다.");
                return combat.TryUseOtamaDango(owned.InstanceId) ?
                    new ManualActionResult(ManualActionState.Succeeded,
                        "오타마 경단 · 목재 16개 획득") :
                    new ManualActionResult(ManualActionState.Rejected,
                        "경단 사용 조건이 충족되지 않았습니다.");
            }
            return new ManualActionResult(ManualActionState.Rejected,
                "해당 오타마는 더 이상 보유하고 있지 않습니다.");
        }
    }
}
