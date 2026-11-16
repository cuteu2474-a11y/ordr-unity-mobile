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
        public const string ViviUpgradePrefix = "source-native:vivi:alabasta:";
        public const string ViviLevelPrefix = "source-native:vivi:alabasta-level:";
        public const string BruleeCopyPrefix = "source-native:brulee:copy:";
        public const string HibariBentoPrefix = "source-native:hibari:bento:";
        public const string MariaCyclePrefix = "source-native:maria:cycle:";
        private static readonly int[] ViviChances =
            { 81, 72, 63, 54, 45, 36, 27, 18, 9 };
        public static string ViviUpgrade(long id) => ViviUpgradePrefix + id;
        public static string ViviLevel(long id) => ViviLevelPrefix + id;
        public static int ViviChance(int currentLevel) =>
            currentLevel >= 1 && currentLevel <= 9 ? ViviChances[currentLevel - 1] : 0;
        public static string BruleeCopy(long source, long template) =>
            BruleeCopyPrefix + source + ":" + template;
        public static string HibariBento(long source, long ally) =>
            HibariBentoPrefix + source + ":" + ally;
        // Original JASS Bux/Buy/Buz: A0HL spawns one h0CN,
        // A0HN consumes it on a valid ally, A0HM sells it for 1 choice Wisp.
        public const string HibariItemPrefix = "source-native:hibari:item:h0CN:";
        public const string HibariCreatePrefix = "source-native:hibari:create:";
        public const string HibariSellPrefix = "source-native:hibari:sell:";
        public static string HibariItem(long id) => HibariItemPrefix + id;
        public static string HibariCreate(long id) => HibariCreatePrefix + id;
        public static string HibariSell(long id) => HibariSellPrefix + id;
        public static string HibariCreateReceipt(long id) => HibariCreatePrefix + "A0HL:" + id;
        public static string HibariSellReceipt(long id) => HibariSellPrefix + "A0HM:" + id;
        public static string HibariReceipt(long id) => HibariBentoPrefix + "A0HN:spent:" + id;
        public static string MariaCycle(long id) => MariaCyclePrefix + id;
        public static string MarcoTransformAction(long id) => MarcoTransformActionPrefix + id;
        public static string UnlockResource(long id) => RogerUnlockResourcePrefix + id;
        public static string UnlockAction(long id) => RogerUnlockActionPrefix + id;
        public static string CastAction(long id) => RogerCastActionPrefix + id;
        public static string UtaUnlockResource(long id) => UtaUnlockResourcePrefix + id;
        public static string UtaUnlockAction(long id) => UtaUnlockActionPrefix + id;
    }

    public sealed class Original323SourceDedicatedSkillPort :
        IOriginalDedicatedSkillPort, IOriginalTargetedDedicatedSkillPort,
        IOriginalDedicatedSkillTargetQuery
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
            long firstVegapunkId = 0;
            long firstOtamaId = 0;
            var owned = combat.MobileUnitSnapshot();
            foreach (var unit in owned)
            {
                if (unit.Unit.Id == "E-06")
                {
                    int level = 1 + (int)Math.Min(9, wallet.Balance(
                        Original323SourceNativeActionKeys.ViviLevel(unit.InstanceId)));
                    int chance = Original323SourceNativeActionKeys.ViviChance(level);
                    if (chance > 0)
                        actions.Add(new MobileDedicatedSkillOffer(
                            Original323SourceNativeActionKeys.ViviUpgrade(unit.InstanceId),
                            "비비 · 알라바스타 강화 Lv." + level,
                            "목재 2 + 기본 랜덤위습 3 · 성공률 " + chance +
                            "% / 실패해도 소모",
                            true, combat.CanRollSourceModelViviUpgrade(
                                unit.InstanceId, chance) &&
                            wallet.Balance("wood") >= 2 &&
                            wallet.Balance(WispResourceIds.Random) >= 3,
                            unit.InstanceId, requiresConfirmation: true, actionLabel: "강화",
                            liveStatus: "Lv." + level + "/10 · 성공 " + chance +
                                "% · 목재 " + wallet.Balance("wood") +
                                "/2 · 위습 " + wallet.Balance(WispResourceIds.Random) +
                                "/3 · 타격당 최대 HP +" + (level * .5).ToString("0.#") + "%"));
                    else
                        actions.Add(new MobileDedicatedSkillOffer(
                            Original323SourceNativeActionKeys.ViviUpgrade(unit.InstanceId),
                            "비비 · 알라바스타 강화", "추가 재료 소모 없음", true, false,
                            unit.InstanceId, liveStatus: "Lv.10/10 · 최대 단계",
                            actionLabel: "강화", isCompleted: true, completedLabel: "최대 단계"));
                }
                if (unit.Unit.Id == "L-14")
                {
                    bool canCopy = false;
                    if (wallet.Balance(SourceModelNativeCharacterRules.TraitPointsResourceId) >= 1)
                        foreach (var candidate in owned)
                            if (combat.CanSourceModelBruleeCopy(
                                unit.InstanceId, candidate.InstanceId))
                            { canCopy = true; break; }
                    actions.Add(new MobileDedicatedSkillOffer(
                        Original323SourceNativeActionKeys.BruleeCopy(unit.InstanceId, 0),
                        "브륄레 · 미러미러열매",
                        "전설 아군 복사 · 특성포인트 1 · 복사체 공속 -5%",
                        true, canCopy, unit.InstanceId, requiresAllyTarget: true,
                        liveStatus: "특성포인트 " + wallet.Balance(
                            SourceModelNativeCharacterRules.TraitPointsResourceId) +
                            "/1 · " + (canCopy ? "대상 선택 가능" : "전설 대상·자원 확인")));
                }
                if (unit.Unit.Id == "L-39")
                {
                    long stock = wallet.Balance(
                        Original323SourceNativeActionKeys.HibariItem(unit.InstanceId));
                    if (!wallet.HasCompletedTransaction(
                        Original323SourceNativeActionKeys.HibariCreateReceipt(unit.InstanceId)))
                        actions.Add(new MobileDedicatedSkillOffer(
                            Original323SourceNativeActionKeys.HibariCreate(unit.InstanceId),
                            "히바리 · 사랑의 도시락 제작",
                            "도시락 1개 제작 · 캐릭터당 1회",
                            true, combat.CanCreateSourceModelHibariBento(
                                unit.InstanceId, wallet), unit.InstanceId, actionLabel: "제작"));
                    else
                        actions.Add(new MobileDedicatedSkillOffer(
                            Original323SourceNativeActionKeys.HibariCreate(unit.InstanceId),
                            "히바리 · 사랑의 도시락 제작", "캐릭터당 1회", true, false,
                            unit.InstanceId, liveStatus: "제작 완료 · 남은 도시락은 보유 아이템에서 사용",
                            actionLabel: "제작", isCompleted: true, completedLabel: "제작 완료"));
                    if (stock > 0)
                    {
                        bool eligible = false;
                        foreach (var ally in owned)
                            if (combat.CanUseSourceModelHibariBento(
                                unit.InstanceId, ally.InstanceId, wallet))
                            { eligible = true; break; }
                        actions.Add(new MobileDedicatedSkillOffer(
                            Original323SourceNativeActionKeys.HibariBento(unit.InstanceId, 0),
                            "사랑의 도시락 사용",
                            "도시락 1개 소비 · 전설 이상 공속 +12%, 코비 초월 +20%",
                            true, eligible, unit.InstanceId, requiresAllyTarget: true,
                            liveStatus: "도시락 " + stock + "개 보유", isDetachedItem: true));
                        actions.Add(new MobileDedicatedSkillOffer(
                            Original323SourceNativeActionKeys.HibariSell(unit.InstanceId),
                            "사랑의 도시락 판매",
                            "도시락 1개 → 흔함선택위습 1개",
                            true, true, unit.InstanceId, requiresConfirmation: true,
                            liveStatus: "도시락 " + stock + "개 보유",
                            isDetachedItem: true, actionLabel: "판매"));
                    }
                }
                if (unit.Unit.Id == "D-01")
                {
                    double remain = combat.SourceModelMariaCooldownRemaining(
                        unit.InstanceId);
                    actions.Add(new MobileDedicatedSkillOffer(
                        Original323SourceNativeActionKeys.MariaCycle(unit.InstanceId),
                        "블랙마리아 · " +
                            combat.SourceModelMariaCurrentModeName(unit.InstanceId) +
                            " → " + combat.SourceModelMariaNextModeName(unit.InstanceId),
                        "마리아네트 모드 전환 / 30초 재사용 · 5초 주기 지속" +
                        (remain > 0 ? " · 대기 " +
                            Math.Ceiling(remain * 10) / 10.0 + "초" : ""),
                        true, combat.CanCycleSourceModelMaria(unit.InstanceId),
                        unit.InstanceId,
                        liveStatus: remain > 0 ?
                            "현재 " + combat.SourceModelMariaCurrentModeName(unit.InstanceId) +
                            " · 대기 중" : "다음 형태 사용 가능",
                        cooldownRemainingSeconds: remain, actionLabel: "전환"));
                }
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
                        true, combat.CanToggleSourceModelMarcoForm(id), id,
                        liveStatus: "현재 " + (phoenix ? "불사조형" : "인간형") +
                            (remaining > 0 ? " · 전환 대기" : " · 전환 가능"),
                        cooldownRemainingSeconds: remaining, actionLabel: phoenix ? "복귀" : "변신");
                    // When the player selects a Marco, keep F in the visible
                    // leftmost slot of the existing battle/story skill ribbon.
                    if (combat.Roster.SelectedId.HasValue &&
                        combat.Roster.SelectedId.Value == id)
                        actions.Insert(0, offer);
                    else
                        actions.Add(offer);
                }
                if (unit.Unit.Id == SourceModelNativeCharacterRules.Vegapunk &&
                    firstVegapunkId == 0)
                    firstVegapunkId = unit.InstanceId;
                if (unit.Unit.Id == SpecialGradeMobilePolicy.Otama &&
                    firstOtamaId == 0)
                    firstOtamaId = unit.InstanceId;
                if (unit.Unit.Id == "I-04" &&
                    wallet.Balance(Original323FusionNativeFollowupLedger.RogerHelperId) > 0)
                {
                    bool unlocked = wallet.Balance(
                        Original323SourceNativeActionKeys.UnlockResource(unit.InstanceId)) > 0;
                    if (unlocked)
                        actions.Add(new MobileDedicatedSkillOffer(
                            Original323SourceNativeActionKeys.CastAction(unit.InstanceId),
                            "로저 · 인페르노 시전",
                            "보스·스토리 대상 · 재사용 150초 · 범위 800",
                            true, combat.CanCastSourceModelRogerInferno(unit.InstanceId),
                            unit.InstanceId, liveStatus: "보스·스토리 대상 필요",
                            cooldownRemainingSeconds:
                                combat.SourceModelRogerInfernoCooldownRemaining(
                                    unit.InstanceId), actionLabel: "시전"));
                    else
                        actions.Add(new MobileDedicatedSkillOffer(
                            Original323SourceNativeActionKeys.UnlockAction(unit.InstanceId),
                            "로저 · 인페르노 해금",
                            "특성포인트 4 소비 · 캐릭터당 1회",
                            true, combat.Game.Phase == SessionPhase.Running &&
                            wallet.Balance(SourceModelNativeCharacterRules.TraitPointsResourceId) >= 4,
                            unit.InstanceId, requiresConfirmation: true, actionLabel: "해금"));
                }
                if (unit.Unit.Id == "E-09" &&
                    wallet.Balance(Original323FusionNativeFollowupLedger.UtaSongId) > 0 &&
                    wallet.Balance(Original323SourceNativeActionKeys.UtaUnlockResource(
                        unit.InstanceId)) == 0)
                {
                    actions.Add(new MobileDedicatedSkillOffer(
                        Original323SourceNativeActionKeys.UtaUnlockAction(unit.InstanceId),
                        "우타 · 신세계 변신 해금",
                        "특성포인트 4 소비 · 캐릭터당 1회",
                        true, combat.Game.Phase == SessionPhase.Running &&
                        wallet.Balance(SourceModelNativeCharacterRules.TraitPointsResourceId) >= 4,
                        unit.InstanceId, requiresConfirmation: true, actionLabel: "해금"));
                }
            }
            // JASS Bux creates h0CN as a separate controllable item.
            // It survives the source h0CM unit's sale/fusion. Inventory
            // offers must not be tied to currently owned Hibari actors.
            var liveHibariIds = new HashSet<long>();
            foreach (var unit in owned)
                if (unit.Unit.Id == "L-39")
                    liveHibariIds.Add(unit.InstanceId);
            foreach (var balance in wallet.CaptureState().Balances)
            {
                if (balance == null || balance.Amount != 1 ||
                    string.IsNullOrEmpty(balance.Id) ||
                    !balance.Id.StartsWith(
                        Original323SourceNativeActionKeys.HibariItemPrefix,
                        StringComparison.Ordinal)) continue;
                long originalOwner;
                if (!long.TryParse(balance.Id.Substring(
                        Original323SourceNativeActionKeys.HibariItemPrefix.Length),
                        out originalOwner) || originalOwner <= 0 ||
                    liveHibariIds.Contains(originalOwner) ||
                    !wallet.HasCompletedTransaction(
                        Original323SourceNativeActionKeys.HibariCreateReceipt(
                            originalOwner))) continue;
                bool canBuff = false;
                foreach (var unit in owned)
                    if (combat.CanUseSourceModelHibariBento(
                        originalOwner, unit.InstanceId, wallet))
                    { canBuff = true; break; }
                actions.Add(new MobileDedicatedSkillOffer(
                    Original323SourceNativeActionKeys.HibariBento(originalOwner, 0),
                    "보유 도시락 · 아군 사용",
                    "도시락 1개 소비 · 전설 이상 공속 +12%, 코비 초월 +20%",
                    true, canBuff, originalOwner, requiresAllyTarget: true,
                    liveStatus: "제작자 판매 후에도 남은 도시락 1개",
                    isDetachedItem: true));
                actions.Add(new MobileDedicatedSkillOffer(
                    Original323SourceNativeActionKeys.HibariSell(originalOwner),
                    "보유 도시락 · 판매",
                    "도시락 1개 → 흔함선택위습 1개",
                    true, true, originalOwner, requiresConfirmation: true,
                    isDetachedItem: true, actionLabel: "판매"));
            }
            if (firstOtamaId != 0 &&
                wallet.Balance(Original323SourceNativeActionKeys.ResearchResource) == 0)
                actions.Add(new MobileDedicatedSkillOffer(
                    Original323SourceNativeActionKeys.ResearchAction,
                    "오타마 · 연구 등록",
                    "조합 이동속도 연구 등록 · 소모 없음 · 1회",
                    true, combat.Game.Phase == SessionPhase.Running,
                    firstOtamaId, actionLabel: "등록"));
            if (firstVegapunkId == 0) return actions.AsReadOnly();
            foreach (var rawcode in new[] { "H0AB", "H0AE", "H0AM" })
            {
                if (combat.HasPracticeSatellite(rawcode))
                {
                    actions.Add(new MobileDedicatedSkillOffer(
                        SourceModelNativeCharacterRules.SatelliteActionId(rawcode),
                        "베가펑크 · " + SourceModelNativeCharacterRules.SatelliteName(rawcode),
                        "중복 구매 불가", true, false, firstVegapunkId,
                        liveStatus: "보유 중", actionLabel: "구매", isCompleted: true, completedLabel: "보유 중"));
                    continue;
                }
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
                    "특성포인트 " + points + " / 목재 " + wood +
                    " (스토리·미션 보상 획득 후 구매 가능)",
                    true, combat.Game.Phase == SessionPhase.Running && affordable,
                    firstVegapunkId, requiresConfirmation: true, actionLabel: "구매", liveStatus: "위성 " +
                        combat.PracticeSatellites().Count + "/3 · 특성 " +
                        wallet.Balance(SourceModelNativeCharacterRules.TraitPointsResourceId) +
                        "/" + points + " · 목재 " +
                        wallet.Balance(SourceModelNativeCharacterRules.WoodResourceId) +
                        "/" + wood));
            }
            return actions.AsReadOnly();
        }

        public bool CanTarget(string offeredSkillId, long ownerInstanceId, long targetInstanceId)
        {
            if (ownerInstanceId <= 0 || targetInstanceId <= 0) return false;
            if (offeredSkillId == Original323SourceNativeActionKeys.BruleeCopy(ownerInstanceId, 0))
                return wallet.Balance(SourceModelNativeCharacterRules.TraitPointsResourceId) >= 1 &&
                    combat.CanSourceModelBruleeCopy(ownerInstanceId, targetInstanceId);
            if (offeredSkillId == Original323SourceNativeActionKeys.HibariBento(ownerInstanceId, 0))
                return combat.CanUseSourceModelHibariBento(ownerInstanceId, targetInstanceId, wallet);
            return false;
        }

        public ManualActionResult ExecuteForAlly(string offeredSkillId,
            long ownerInstanceId, long targetInstanceId)
        {
            if (ownerInstanceId <= 0 || targetInstanceId <= 0)
                return new ManualActionResult(ManualActionState.Rejected,
                    "유효한 시전자와 대상이 필요합니다.");
            // An h0CN is an owned item, not a spell cast by a living
            // Hibari. The original creator may have left the roster.
            if (offeredSkillId ==
                    Original323SourceNativeActionKeys.HibariBento(ownerInstanceId, 0) &&
                wallet.HasCompletedTransaction(
                    Original323SourceNativeActionKeys.HibariCreateReceipt(
                        ownerInstanceId)) &&
                wallet.Balance(Original323SourceNativeActionKeys.HibariItem(
                    ownerInstanceId)) == 1)
            {
                bool used = combat.TryUseSourceModelHibariBento(
                    ownerInstanceId, targetInstanceId, wallet);
                return new ManualActionResult(
                    used ? ManualActionState.Succeeded : ManualActionState.Rejected,
                    used ? "사랑의 도시락 사용 · 아군 공격속도 영구 강화" :
                        "도시락 보유·대상 등급·중복 버프를 확인해 주세요.");
            }
            foreach (var source in combat.MobileUnitSnapshot())
            {
                if (source.InstanceId != ownerInstanceId) continue;
                if (source.Unit.Id == "L-14" &&
                    offeredSkillId == Original323SourceNativeActionKeys.BruleeCopy(
                        ownerInstanceId, 0))
                {
                    long produced;
                    bool ok = combat.TrySourceModelBruleeCopy(ownerInstanceId,
                        targetInstanceId, wallet, out produced);
                    return new ManualActionResult(
                        ok ? ManualActionState.Succeeded : ManualActionState.Rejected,
                        ok ? "브륄레 복사 완료 · 새 전설 유닛 #" + produced +
                            " · 특성포인트 1 소모 · 공격속도 -5%" :
                            "보유 전설 대상·재화·전장 상태를 다시 확인해 주세요.");
                }
            }
            return new ManualActionResult(ManualActionState.Rejected,
                "스킬을 선택한 원래 캐릭터를 더 이상 보유하지 않습니다.");
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
            // Every tap re-validates the live unit, selected target,
            // shared wallet and current source-tooltip ownership.
            // Buz targets the h0CN item. Its original Hibari need not
            // remain owned for the player to sell the physical lunch.
            if (skillId.StartsWith(
                    Original323SourceNativeActionKeys.HibariSellPrefix,
                    StringComparison.Ordinal))
            {
                long itemOwner;
                if (!long.TryParse(skillId.Substring(
                        Original323SourceNativeActionKeys.HibariSellPrefix.Length),
                        out itemOwner) || itemOwner <= 0 ||
                    skillId != Original323SourceNativeActionKeys.HibariSell(itemOwner) ||
                    !wallet.HasCompletedTransaction(
                        Original323SourceNativeActionKeys.HibariCreateReceipt(itemOwner)))
                    return new ManualActionResult(ManualActionState.Rejected,
                        "원본 도시락 소유 기록이 없습니다.");
                bool sold = combat.TrySellSourceModelHibariBento(itemOwner, wallet);
                return new ManualActionResult(
                    sold ? ManualActionState.Succeeded : ManualActionState.Rejected,
                    sold ? "도시락 판매 · 선택위습 1개 획득" :
                        "미사용 도시락을 보유하지 않았습니다.");
            }
            if (skillId.StartsWith(
                    Original323SourceNativeActionKeys.HibariCreatePrefix,
                    StringComparison.Ordinal))
            {
                foreach (var unit in combat.MobileUnitSnapshot())
                {
                    if (unit.Unit.Id != "L-39") continue;
                    long id = unit.InstanceId;
                    if (skillId == Original323SourceNativeActionKeys.HibariCreate(id))
                    {
                        bool created = combat.TryCreateSourceModelHibariBento(id, wallet);
                        return new ManualActionResult(
                            created ? ManualActionState.Succeeded : ManualActionState.Rejected,
                            created ? "도시락 h0CN 생성 · 사용하거나 선택위습으로 판매 가능" :
                                "도시락을 이미 생성했거나 히바리 스킬 조건 불일치");
                    }
                }
                return new ManualActionResult(ManualActionState.Rejected,
                    "원본 도시락을 생성한 히바리를 찾을 수 없습니다.");
            }
            if (skillId.StartsWith(Original323SourceNativeActionKeys.ViviUpgradePrefix,
                    StringComparison.Ordinal) ||
                skillId.StartsWith(Original323SourceNativeActionKeys.BruleeCopyPrefix,
                    StringComparison.Ordinal) ||
                skillId.StartsWith(Original323SourceNativeActionKeys.HibariBentoPrefix,
                    StringComparison.Ordinal) ||
                skillId.StartsWith(Original323SourceNativeActionKeys.MariaCyclePrefix,
                    StringComparison.Ordinal))
            {
                foreach (var unit in combat.MobileUnitSnapshot())
                {
                    long id = unit.InstanceId;
                    if (unit.Unit.Id == "E-06" &&
                        skillId == Original323SourceNativeActionKeys.ViviUpgrade(id))
                    {
                        bool success;
                        int level;
                        if (!combat.TryUpgradeSourceModelViviAlabasta(
                                id, wallet, out success, out level))
                            return new ManualActionResult(ManualActionState.Rejected,
                                "강화 레벨·원본 확률·목재·기본 랜덤위습·전투 상태를 확인해 주세요.");
                        return new ManualActionResult(ManualActionState.Succeeded,
                            success ? "알라바스타 강화 성공 · Lv." + level :
                                "강화 실패 · 목재 2·기본 랜덤위습 3 소모");
                    }
                    if (unit.Unit.Id == "D-01" &&
                        skillId == Original323SourceNativeActionKeys.MariaCycle(id))
                    {
                        bool cast = combat.TryCycleSourceModelMaria(id);
                        return new ManualActionResult(
                            cast ? ManualActionState.Succeeded : ManualActionState.Rejected,
                            cast ? "마리아네트 · " +
                                combat.SourceModelMariaCurrentModeName(id) :
                                "재사용 대기 또는 전환 조건이 충족되지 않았습니다.");
                    }
                    // Brulee and Hibari now use ExecuteForAlly exclusively.
                    // The old direct path read Roster.SelectedId as target,
                    // which could accidentally consume the caster selection.
                }
                return new ManualActionResult(ManualActionState.Rejected,
                    "해당 유닛 또는 선택한 대상을 찾지 못했습니다.");
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
