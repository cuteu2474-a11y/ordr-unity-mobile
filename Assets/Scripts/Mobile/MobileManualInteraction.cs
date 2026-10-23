using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    public enum ManualActionState { Succeeded, Rejected, NotImplemented }

    public sealed class ManualActionResult
    {
        public ManualActionState State { get; private set; }
        public string Message { get; private set; }

        public ManualActionResult(ManualActionState state, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("작업 결과 메시지가 필요합니다.");
            State = state;
            Message = message;
        }

        public static ManualActionResult Unavailable(string operation)
        {
            return new ManualActionResult(ManualActionState.NotImplemented,
                operation + "의 원작 실행 시스템이 아직 연결되지 않았습니다.");
        }
    }

    public enum ManualEconomyCategory { Shop, Gambling, Wisp }

    public sealed class MobileManualOffer
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public string OriginalPriceAndRules { get; private set; }

        public MobileManualOffer(string id, string title, string originalPriceAndRules)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(originalPriceAndRules))
                throw new ArgumentException("원작 품목 및 비용·조건 설명이 필요합니다.");
            Id = id; Title = title;
            OriginalPriceAndRules = originalPriceAndRules;
        }
    }

    public sealed class MobileFusionPreview
    {
        public string RecipeId { get; private set; }
        public string Title { get; private set; }
        public string OriginalMaterialsAndCost { get; private set; }
        public IReadOnlyList<long> SuggestedMaterialInstances { get; private set; }
        public IReadOnlyList<string> MissingMaterials { get; private set; }
        public bool IsReady { get { return MissingMaterials.Count == 0; } }

        // 보유 유닛 기반 재료 강조만 보조한다. 소비/성공 판정은 원작 서비스가 한다.
        public MobileFusionPreview(string recipeId, string title,
            string originalMaterialsAndCost, IEnumerable<long> materialIds)
            : this(recipeId, title, originalMaterialsAndCost, materialIds, new string[0]) { }

        public MobileFusionPreview(string recipeId, string title,
            string originalMaterialsAndCost, IEnumerable<long> materialIds,
            IEnumerable<string> missingMaterials)
        {
            if (string.IsNullOrWhiteSpace(recipeId) || string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(originalMaterialsAndCost))
                throw new ArgumentException("검증된 조합식과 소모 설명이 필요합니다.");
            if (materialIds == null) throw new ArgumentNullException(nameof(materialIds));
            if (missingMaterials == null) throw new ArgumentNullException(nameof(missingMaterials));
            var distinct = new HashSet<long>();
            var materials = new List<long>();
            foreach (long id in materialIds)
            {
                if (id <= 0 || !distinct.Add(id))
                    throw new ArgumentException("재료 인스턴스 ID가 중복되거나 유효하지 않습니다.");
                materials.Add(id);
            }
            var missing = new List<string>();
            foreach (string value in missingMaterials)
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("미확보 재료 설명이 비어 있습니다.");
                missing.Add(value);
            }
            RecipeId = recipeId;
            Title = title;
            OriginalMaterialsAndCost = originalMaterialsAndCost;
            SuggestedMaterialInstances = materials.AsReadOnly();
            MissingMaterials = missing.AsReadOnly();
        }
    }

    // 원작 스토리 파견의 가능 여부, 자리 이동, 공격 중단, 보상/복귀는 서비스가 판단.
    // UI에서 사용자가 직접 탭했을 때만 Dispatch 호출. 자동 루프에서 접근하지 않는다.
    public interface IOriginalStoryDispatchPort
    {
        bool CanDispatch(long unitInstanceId);
        ManualActionResult Dispatch(long unitInstanceId);
        // 파견된 유닛은 메인 전장에서 공격하거나 자동 시전하면 안 된다.
        // 복귀시 false를 반환하는 원작 스토리 상태를 외부 구현이 보유한다.
        bool IsDeployed(long unitInstanceId);
        // 원작에서 유닛의 수동 복귀가 가능할 때만 노출. 불가능하면 false.
        bool CanRecall(long unitInstanceId);
        ManualActionResult Recall(long unitInstanceId);
    }

    public interface IOriginalFusionPort
    {
        IReadOnlyList<MobileFusionPreview> AvailableRecipes(
            IReadOnlyList<MobileUnitSnapshot> ownedUnits);
        // 원작 조합식·재료·재화·확률을 실행 직전에 다시 검증하는 최종 커밋.
        ManualActionResult ConfirmFusion(string recipeId,
            IReadOnlyList<long> selectedMaterialInstances);
    }

    // Voluntary common sale is distinct from shop, gambling and Wisp usage.
    // Implement the atomic transaction in the live player world before exposing UI.
    public interface IOriginalCommonSalePort
    {
        ManualActionResult ConfirmSale(long exactUnitInstanceId);
    }

    public interface IOriginalEconomyPort
    {
        IReadOnlyList<MobileManualOffer> AvailableOffers(ManualEconomyCategory category);
        // 원작 가격·재화·쿨다운·도박 횟수·위습 보상은 여기에만 존재한다.
        ManualActionResult Confirm(ManualEconomyCategory category, string offerId);
    }

    // 갱 뱃지 분해: 실제 5초 시전·유닛 교환·실패 복구를 담당하는 월드 서비스.
    public interface IManualUnitDecompositionPort
    {
        ManualActionResult Request(long unitInstanceId);
    }

    // 이 클래스에는 자동 실행 Tick이 없다.
    // 스토리 파견, 조합, 상점/도박/위습은 오직 화면의 명시적 탭을 통해 호출한다.
    public sealed class MobileManualInteraction
    {
        private IOriginalStoryDispatchPort story;
        private IOriginalFusionPort fusion;
        private IOriginalEconomyPort economy;
        private OriginalRandomWispResourceExchange resourceWispExchange;
        private IOriginalCommonSalePort commonSale;

        public void ConfigureRandomWispResourceExchange(
            OriginalRandomWispResourceExchange verifiedExchange)
        {
            resourceWispExchange = verifiedExchange ??
                throw new ArgumentNullException(nameof(verifiedExchange));
        }

        public bool HasOriginalCommonSaleService { get { return commonSale != null; } }
        public void ConfigureOriginalCommonSale(IOriginalCommonSalePort verifiedSale)
        {
            commonSale = verifiedSale ?? throw new ArgumentNullException(nameof(verifiedSale));
        }
        public ManualActionResult RequestCommonSale(long instanceId)
        {
            if (instanceId <= 0)
                return new ManualActionResult(ManualActionState.Rejected,
                    "판매할 실제 유닛을 선택해 주세요.");
            return commonSale == null ? ManualActionResult.Unavailable("원본 흔함 판매 거래") :
                commonSale.ConfirmSale(instanceId) ??
                new ManualActionResult(ManualActionState.Rejected,
                    "판매 원본 거래 결과가 확인되지 않았습니다.");
        }
        private IManualUnitDecompositionPort decomposition;

        public void ConfigureUnitDecomposition(IManualUnitDecompositionPort service)
        {
            decomposition = service ?? throw new ArgumentNullException(nameof(service));
        }

        public ManualActionResult RequestUnitDecomposition(MobileUnitSnapshot selected)
        {
            if (selected == null || !SpecialGradeMobilePolicy.IsManualOnly(selected.Unit.Id))
                return new ManualActionResult(ManualActionState.Rejected, "분해할 갱 뱃지를 선택해 주세요.");
            return decomposition == null ? ManualActionResult.Unavailable("갱 뱃지 분해") :
                decomposition.Request(selected.InstanceId) ??
                new ManualActionResult(ManualActionState.Rejected, "분해 결과를 확인하지 못했습니다.");
        }

        public bool HasStoryService { get { return story != null; } }
        public bool HasFusionService { get { return fusion != null; } }
        public bool HasExecutableFusionService
        {
            get
            {
                if (fusion == null) return false;
                var assist = fusion as AssistedFusionService;
                if (assist != null) return assist.CanCommit;
                var jass = fusion as OriginalJassFusionPort;
                if (jass != null) return jass.CanCommit;
                var mystic = fusion as OtherworldMysticFusionPreviewPort;
                if (mystic != null) return mystic.CanCommit;
                var combined = fusion as CombinedOriginalFusionPort;
                if (combined != null) return combined.CanCommit;
                return true;
            }
        }
        public bool HasEconomyService { get { return economy != null || resourceWispExchange != null; } }

        public void Configure(IOriginalStoryDispatchPort originalStory,
            IOriginalFusionPort originalFusion, IOriginalEconomyPort originalEconomy)
        {
            story = originalStory;
            fusion = originalFusion;
            economy = originalEconomy;
        }

        public ManualActionResult RequestStoryDispatch(long unitInstanceId)
        {
            if (unitInstanceId <= 0)
                return new ManualActionResult(ManualActionState.Rejected, "파견할 유닛을 선택해 주세요.");
            if (story == null) return ManualActionResult.Unavailable("스토리 파견");
            if (!story.CanDispatch(unitInstanceId))
                return new ManualActionResult(ManualActionState.Rejected,
                    "원작 파견 조건에 맞지 않는 유닛입니다.");
            return story.Dispatch(unitInstanceId) ??
                new ManualActionResult(ManualActionState.Rejected, "스토리 파견 결과를 확인하지 못했습니다.");
        }

        public bool IsStoryDeployed(long instanceId)
        {
            return story != null && story.IsDeployed(instanceId);
        }

        public bool CanRecallStory(long instanceId)
        {
            return story != null && story.IsDeployed(instanceId) && story.CanRecall(instanceId);
        }

        public ManualActionResult RequestStoryRecall(long instanceId)
        {
            if (story == null) return ManualActionResult.Unavailable("스토리 복귀");
            if (!story.IsDeployed(instanceId) || !story.CanRecall(instanceId))
                return new ManualActionResult(ManualActionState.Rejected,
                    "현재 원작 규칙에서 복귀할 수 없는 유닛입니다.");
            return story.Recall(instanceId) ??
                new ManualActionResult(ManualActionState.Rejected, "복귀 결과를 확인하지 못했습니다.");
        }

        public IReadOnlyList<MobileFusionPreview> PreviewFusion(IReadOnlyList<MobileUnitSnapshot> ownedUnits)
        {
            if (fusion == null) return new List<MobileFusionPreview>().AsReadOnly();
            var entries = fusion.AvailableRecipes(ownedUnits);
            return entries ?? new List<MobileFusionPreview>().AsReadOnly();
        }

        public ManualActionResult ConfirmFusion(MobileFusionPreview selected)
        {
            if (selected == null)
                return new ManualActionResult(ManualActionState.Rejected, "조합식을 선택해 주세요.");
            if (!selected.IsReady)
                return new ManualActionResult(ManualActionState.Rejected,
                    "필요한 원본 조합 재료가 모두 준비되지 않았습니다.");
            if (fusion == null) return ManualActionResult.Unavailable("캐릭터 조합");
            return fusion.ConfirmFusion(selected.RecipeId, selected.SuggestedMaterialInstances) ??
                new ManualActionResult(ManualActionState.Rejected, "조합 결과를 확인하지 못했습니다.");
        }

        public IReadOnlyList<MobileManualOffer> ListEconomyOffers(ManualEconomyCategory category)
        {
            if (!Enum.IsDefined(typeof(ManualEconomyCategory), category))
                throw new ArgumentOutOfRangeException(nameof(category));

            var combined = new List<MobileManualOffer>();
            // Existing original shop/gambling and other Wisp offers stay intact.
            var others = economy == null ? null : economy.AvailableOffers(category);
            if (others != null)
                foreach (var offer in others)
                {
                    if (offer == null) continue;
                    // Reserved ID must always route to the source-backed action.
                    if (category == ManualEconomyCategory.Wisp &&
                        offer.Id == OriginalRandomWispResourceExchange.OfferId) continue;
                    combined.Add(offer);
                }
            if (category == ManualEconomyCategory.Wisp && resourceWispExchange != null)
                combined.Insert(0, new MobileManualOffer(
                    OriginalRandomWispResourceExchange.OfferId, "랜덤위습: 골드·목재 추첨",
                    "랜덤위습 1개 | 목재 1개 67% / 현재 라운드 × 75골드 33%"));
            return combined.AsReadOnly();
        }

        public ManualActionResult RequestEconomyAction(ManualEconomyCategory category, string offerId)
        {
            if (!Enum.IsDefined(typeof(ManualEconomyCategory), category))
                throw new ArgumentOutOfRangeException(nameof(category));
            if (string.IsNullOrWhiteSpace(offerId))
                return new ManualActionResult(ManualActionState.Rejected, "실행할 항목을 선택해 주세요.");

            if (category == ManualEconomyCategory.Wisp &&
                offerId == OriginalRandomWispResourceExchange.OfferId)
            {
                if (resourceWispExchange == null)
                    return ManualActionResult.Unavailable("랜덤위습 자원 추첨");
                try
                {
                    OriginalRandomWispExchangeOutcome reward = resourceWispExchange.SpendOne();
                    if (reward == null)
                        return new ManualActionResult(ManualActionState.Rejected,
                            "진행 라운드가 시작되지 않았거나 랜덤위습이 부족합니다.");
                    return new ManualActionResult(ManualActionState.Succeeded,
                        reward.RewardResourceId == "wood"
                            ? "목재 " + reward.RewardAmount + "개 획득"
                            : "골드 " + reward.RewardAmount + " 획득");
                }
                catch (Exception)
                {
                    // Wallet.TryExchange is atomic: no partial Wisp spend.
                    return new ManualActionResult(ManualActionState.Rejected,
                        "자원 추첨을 완료할 수 없습니다. 원본 거래나 난수 상태를 확인하세요.");
                }
            }
            if (economy == null) return ManualActionResult.Unavailable(category.ToString());
            return economy.Confirm(category, offerId) ??
                new ManualActionResult(ManualActionState.Rejected, "실행 결과를 확인하지 못했습니다.");
        }
    }
}

