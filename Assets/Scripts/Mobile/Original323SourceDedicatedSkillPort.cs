using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    public sealed class Original323SourceDedicatedSkillPort : IOriginalDedicatedSkillPort
    {
        private readonly CharacterCombatRuntime combat;
        private readonly EconomyWallet wallet;
        private readonly Original323OtamaDedicatedSkillPort otama;

        public Original323SourceDedicatedSkillPort(CharacterCombatRuntime combat,
            EconomyWallet sharedWallet)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            wallet = sharedWallet ?? throw new ArgumentNullException(nameof(sharedWallet));
            otama = new Original323OtamaDedicatedSkillPort(combat, wallet);
        }

        public IReadOnlyList<MobileDedicatedSkillOffer> AvailableSkills()
        {
            var actions = new List<MobileDedicatedSkillOffer>(otama.AvailableSkills());
            if (!combat.UsesSourceModelBasicAttacks) return actions.AsReadOnly();
            bool ownsVegapunk = false;
            foreach (var unit in combat.MobileUnitSnapshot())
                if (unit.Unit.Id == SourceModelNativeCharacterRules.Vegapunk)
                    { ownsVegapunk = true; break; }
            if (!ownsVegapunk) return actions.AsReadOnly();
            foreach (var rawcode in new[] { "H0AB", "H0AE", "H0AM" })
            {
                if (combat.HasPracticeSatellite(rawcode)) continue;
                long points, wood;
                if (!SourceModelNativeCharacterRules.TryPlanSatelliteCost(
                    rawcode, combat.PracticeSatelliteCorePurchases(),
                    out points, out wood)) continue;
                bool affordable =
                    wallet.Balance(SourceModelNativeCharacterRules.TraitPointsResourceId)
                        >= points &&
                    wallet.Balance(SourceModelNativeCharacterRules.WoodResourceId) >= wood;
                actions.Add(new MobileDedicatedSkillOffer(
                    SourceModelNativeCharacterRules.SatelliteActionId(rawcode),
                    "베가펑크 · " + SourceModelNativeCharacterRules.SatelliteName(rawcode),
                    "원본 특성포인트 " + points + " / 목재 " + wood +
                    " (공유 경제 특성포인트 공급 미연결 시 구매 불가)",
                    true, combat.Game.Phase == SessionPhase.Running && affordable));
            }
            return actions.AsReadOnly();
        }

        public ManualActionResult Execute(string skillId)
        {
            if (string.IsNullOrEmpty(skillId))
                return new ManualActionResult(ManualActionState.Rejected,
                    "전용 스킬 식별자가 필요합니다.");
            if (!skillId.StartsWith("vegapunk-satellite:", StringComparison.Ordinal))
                return otama.Execute(skillId);
            foreach (var rawcode in new[] { "H0AB", "H0AE", "H0AM" })
            {
                if (skillId != SourceModelNativeCharacterRules.SatelliteActionId(rawcode))
                    continue;
                foreach (var unit in combat.MobileUnitSnapshot())
                {
                    if (unit.Unit.Id != SourceModelNativeCharacterRules.Vegapunk)
                        continue;
                    bool succeeded = combat.TryAcquirePracticeSatellite(
                        unit.InstanceId, rawcode, wallet);
                    return new ManualActionResult(
                        succeeded ? ManualActionState.Succeeded : ManualActionState.Rejected,
                        succeeded ? SourceModelNativeCharacterRules.SatelliteName(rawcode) +
                            " 출현 (전투·능력치 검증 대기)" :
                            "원본 특성포인트·목재 또는 위성 구매 조건을 확인해 주세요.");
                }
            }
            return new ManualActionResult(ManualActionState.Rejected,
                "해당 베가펑크 위성 소환 권한이 없습니다.");
        }
    }
}
