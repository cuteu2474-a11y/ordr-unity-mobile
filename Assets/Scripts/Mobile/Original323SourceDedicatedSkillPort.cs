using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // Stable shared-wallet action identity. Research does not consume an item,
    // a unit, or currency; Roger's A202 is an exact four-point debit per actor.
    public static class Original323SourceNativeActionKeys
    {
        public const string ResearchReceipt = "source:native-event:CH8:h05P";
        public const string ResearchResource = "source:native-research:R00R";
        public const string ResearchAction = "source-native:otama:research";
        public const string RogerUnlockResourcePrefix = "source:native-trait:A202:";
        public const string RogerUnlockActionPrefix = "source-native:roger:unlock:";
        public const string RogerCastActionPrefix = "source-native:roger:cast:";
        public const string UtaUnlockResourcePrefix = "source:native-trait:A0D2:";
        public const string UtaUnlockActionPrefix = "source-native:uta:unlock:";
        public const string MarcoTransformActionPrefix = "source-native:marco:transform:";
        public static string MarcoTransformAction(long id) => MarcoTransformActionPrefix + id;
        public static string UnlockResource(long id) => RogerUnlockResourcePrefix + id;
        public static string UnlockAction(long id) => RogerUnlockActionPrefix + id;
        public static string CastAction(long id) => RogerCastActionPrefix + id;
        public static string UtaUnlockResource(long id) => UtaUnlockResourcePrefix + id;
        public static string UtaUnlockAction(long id) => UtaUnlockActionPrefix + id;
    }

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
            bool ownsOtama = false;
            foreach (var unit in combat.MobileUnitSnapshot())
            {
                if (unit.Unit.Id == "LI-03")
                {
                    long id = unit.InstanceId;
                    bool phoenix = combat.PracticeFormOf(id) == "phoenix";
                    double remaining =
                        combat.SourceModelMarcoTransformCooldownRemaining(id);
                    string direction = phoenix ? "인간형 복귀" : "불사조 변신";
                    string timing = remaining > .00001
                        ? " · 대기 " +
                            Math.Ceiling(remaining * 10) / 10.0 + "초"
                        : "";
                    var offer = new MobileDedicatedSkillOffer(
                        Original323SourceNativeActionKeys.MarcoTransformAction(id),
                        "마르코 F · " + direction,
                        "수동 변신 / 10.75초 쿨타임" + timing,
                        true, combat.CanToggleSourceModelMarcoForm(id));
                    // When the player selects a Marco, keep F in the visible
                    // leftmost slot of the existing battle/story skill ribbon.
                    if (combat.Roster.SelectedId.HasValue &&
                        combat.Roster.SelectedId.Value == id)
                        actions.Insert(0, offer);
                    else
                        actions.Add(offer);
                }
                if (unit.Unit.Id == SourceModelNativeCharacterRules.Vegapunk)
                    ownsVegapunk = true;
                if (unit.Unit.Id == SpecialGradeMobilePolicy.Otama)
                    ownsOtama = true;
                if (unit.Unit.Id == "I-04" &&
                    wallet.Balance(Original323FusionNativeFollowupLedger.RogerHelperId) > 0)
                {
                    bool unlocked = wallet.Balance(
                        Original323SourceNativeActionKeys.UnlockResource(unit.InstanceId)) > 0;
                    if (unlocked)
                        actions.Add(new MobileDedicatedSkillOffer(
                            Original323SourceNativeActionKeys.CastAction(unit.InstanceId),
                            "로저 · 인페르노 시전",
                            "A202 / 보스·스토리 대상 / 재사용 대기 150초 · 범위 800",
                            true, combat.CanCastSourceModelRogerInferno(unit.InstanceId)));
                    else
                        actions.Add(new MobileDedicatedSkillOffer(
                            Original323SourceNativeActionKeys.UnlockAction(unit.InstanceId),
                            "로저 · 인페르노 해금",
                            "원본 B8Z / 특성포인트 4점 영구 소모 · 유닛당 1회",
                            true, combat.Game.Phase == SessionPhase.Running &&
                            wallet.Balance(SourceModelNativeCharacterRules.TraitPointsResourceId) >= 4));
                }
                if (unit.Unit.Id == "E-09" &&
                    wallet.Balance(Original323FusionNativeFollowupLedger.UtaSongId) > 0 &&
                    wallet.Balance(Original323SourceNativeActionKeys.UtaUnlockResource(
                        unit.InstanceId)) == 0)
                {
                    actions.Add(new MobileDedicatedSkillOffer(
                        Original323SourceNativeActionKeys.UtaUnlockAction(unit.InstanceId),
                        "우타 · 신세계 변신 해금",
                        "원본 B6n/A0D2 · 특성포인트 4점 영구 소모 · 유닛당 1회",
                        true, combat.Game.Phase == SessionPhase.Running &&
                        wallet.Balance(SourceModelNativeCharacterRules.TraitPointsResourceId) >= 4));
                }
            }
            if (ownsOtama &&
                wallet.Balance(Original323SourceNativeActionKeys.ResearchResource) == 0)
                actions.Add(new MobileDedicatedSkillOffer(
                    Original323SourceNativeActionKeys.ResearchAction,
                    "오타마 · 연구 등록",
                    "원본 CH8 / 오타마 지역 진입을 수동으로 대체 / 소모 없음 · 1회",
                    true, combat.Game.Phase == SessionPhase.Running));
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
                    " (스토리·미션 보상 획득 후 구매 가능)",
                    true, combat.Game.Phase == SessionPhase.Running && affordable));
            }
            return actions.AsReadOnly();
        }

        public ManualActionResult Execute(string skillId)
        {
            if (string.IsNullOrEmpty(skillId))
                return new ManualActionResult(ManualActionState.Rejected,
                    "전용 스킬 식별자가 필요합니다.");
            if (skillId == Original323SourceNativeActionKeys.ResearchAction)
            {
                if (!combat.UsesSourceModelBasicAttacks ||
                    combat.Game.Phase != SessionPhase.Running)
                    return new ManualActionResult(ManualActionState.Rejected,
                        "원본 연구는 진행 중인 전투에서만 등록할 수 있습니다.");
                bool owns = false;
                foreach (var unit in combat.MobileUnitSnapshot())
                    if (unit.Unit.Id == SpecialGradeMobilePolicy.Otama)
                        owns = true;
                if (!owns || wallet.Balance(
                    Original323SourceNativeActionKeys.ResearchResource) > 0)
                    return new ManualActionResult(ManualActionState.Rejected,
                        "오타마 보유 또는 연구 등록 상태를 확인해 주세요.");
                bool committed = wallet.TryGrantOnce(
                    Original323SourceNativeActionKeys.ResearchReceipt,
                    new ResourceBundle().Add(
                        Original323SourceNativeActionKeys.ResearchResource, 1));
                return new ManualActionResult(
                    committed ? ManualActionState.Succeeded : ManualActionState.Rejected,
                    committed ? "오타마 연구 R00R 등록 · 이후 해당 유닛 조합 시 이동속도 +3%" :
                        "연구가 이미 등록되었습니다.");
            }
            if (skillId.StartsWith(Original323SourceNativeActionKeys.RogerUnlockActionPrefix,
                    StringComparison.Ordinal) ||
                skillId.StartsWith(Original323SourceNativeActionKeys.RogerCastActionPrefix,
                    StringComparison.Ordinal))
            {
                foreach (var unit in combat.MobileUnitSnapshot())
                {
                    if (unit.Unit.Id != "I-04" ||
                        wallet.Balance(Original323FusionNativeFollowupLedger.RogerHelperId) < 1)
                        continue;
                    long id = unit.InstanceId;
                    if (skillId == Original323SourceNativeActionKeys.UnlockAction(id))
                    {
                        if (!combat.UsesSourceModelBasicAttacks ||
                            combat.Game.Phase != SessionPhase.Running) break;
                        bool success = wallet.TryExchangeOnce(
                            skillId,
                            new ResourceBundle().Add(
                                SourceModelNativeCharacterRules.TraitPointsResourceId, 4),
                            new ResourceBundle().Add(
                                Original323SourceNativeActionKeys.UnlockResource(id), 1));
                        return new ManualActionResult(
                            success ? ManualActionState.Succeeded : ManualActionState.Rejected,
                            success ? "로저 A202 인페르노 해금 (특성포인트 4 차감)" :
                                "특성포인트가 부족하거나 이미 해금한 로저입니다.");
                    }
                    if (skillId == Original323SourceNativeActionKeys.CastAction(id))
                    {
                        if (wallet.Balance(
                                Original323SourceNativeActionKeys.UnlockResource(id)) == 0)
                            break;
                        bool success = combat.TryCastSourceModelRogerInferno(id);
                        return new ManualActionResult(
                            success ? ManualActionState.Succeeded : ManualActionState.Rejected,
                            success ? "로저 인페르노 시전 · 2.2초 후 신의 심판" :
                                "보스·스토리 대상 또는 시전 대기시간을 확인해 주세요.");
                    }
                }
                return new ManualActionResult(ManualActionState.Rejected,
                    "해당 로저를 보유하지 않았거나 원본 해금 조건이 맞지 않습니다.");
            }
            if (skillId.StartsWith(
                    Original323SourceNativeActionKeys.MarcoTransformActionPrefix,
                    StringComparison.Ordinal))
            {
                foreach (var unit in combat.MobileUnitSnapshot())
                {
                    if (unit.Unit.Id != "LI-03" ||
                        skillId != Original323SourceNativeActionKeys.MarcoTransformAction(
                            unit.InstanceId)) continue;
                    bool succeeded = combat.TryToggleSourceModelMarcoForm(
                        unit.InstanceId);
                    string active = combat.PracticeFormOf(unit.InstanceId);
                    return new ManualActionResult(
                        succeeded ? ManualActionState.Succeeded : ManualActionState.Rejected,
                        succeeded
                            ? (active == "phoenix" ? "마르코 불사조형 변신 완료" :
                                "마르코 인간형 복귀 완료") +
                              " · 다음 F 변신까지 10.75초"
                            : "해독·공유 출전·재사용 대기·보유 상태를 확인해 주세요.");
                }
                return new ManualActionResult(ManualActionState.Rejected,
                    "해당 마르코를 보유하고 있지 않습니다.");
            }
            if (skillId.StartsWith(Original323SourceNativeActionKeys.UtaUnlockActionPrefix,
                    StringComparison.Ordinal))
            {
                if (!combat.UsesSourceModelBasicAttacks ||
                    combat.Game.Phase != SessionPhase.Running ||
                    wallet.Balance(Original323FusionNativeFollowupLedger.UtaSongId) < 1)
                    return new ManualActionResult(ManualActionState.Rejected,
                        "우타의 신세계 변신 해금 조건을 확인해 주세요.");
                foreach (var unit in combat.MobileUnitSnapshot())
                {
                    if (unit.Unit.Id != "E-09" ||
                        skillId != Original323SourceNativeActionKeys.UtaUnlockAction(
                            unit.InstanceId)) continue;
                    bool success = wallet.TryExchangeOnce(
                        skillId,
                        new ResourceBundle().Add(
                            SourceModelNativeCharacterRules.TraitPointsResourceId, 4),
                        new ResourceBundle().Add(
                            Original323SourceNativeActionKeys.UtaUnlockResource(
                                unit.InstanceId), 1));
                    return new ManualActionResult(
                        success ? ManualActionState.Succeeded : ManualActionState.Rejected,
                        success ? "우타 신세계 변신 해금 · 특성포인트 4 차감" :
                            "특성포인트가 부족하거나 이미 해금한 우타입니다.");
                }
                return new ManualActionResult(ManualActionState.Rejected,
                    "해당 우타를 보유하고 있지 않습니다.");
            }
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
