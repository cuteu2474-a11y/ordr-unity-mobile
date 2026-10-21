using System;
using System.Collections.Generic;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Mobile
{
    // 원본의 재료 소비, 추가 비용/아이템, 결과 유닛 생성 및 원본 제작 트리거를
    // 원자적으로 수행하는 구현체. UI/미리보기 서비스는 이 권한을 가지지 않는다.
    public interface IVerifiedOriginalFusionCommit
    {
        ManualActionResult TryCommit(VerifiedFusionRecipe verifiedRecipe,
            IReadOnlyList<long> exactMaterialInstanceIds);
    }

    // 실제 원작 조합식 일부가 제공되는 범위에서 '보유 재료 자동 선택'만 구현.
    // 재료 ID/등급/수량 검증은 조합 직전에도 다시 수행한다.
    public sealed class AssistedFusionService : IOriginalFusionPort
    {
        private readonly VerifiedFusionCatalog recipes;
        private readonly Func<IReadOnlyList<MobileUnitSnapshot>> ownedUnits;
        private readonly Func<long, bool> storyDeployed;
        private IVerifiedOriginalFusionCommit originalCommit;

        public bool CanCommit { get { return originalCommit != null; } }

        public AssistedFusionService(VerifiedFusionCatalog recipes,
            Func<IReadOnlyList<MobileUnitSnapshot>> ownedUnits,
            Func<long, bool> storyDeployed,
            IVerifiedOriginalFusionCommit originalCommit = null)
        {
            this.recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            this.ownedUnits = ownedUnits ?? throw new ArgumentNullException(nameof(ownedUnits));
            this.storyDeployed = storyDeployed ?? throw new ArgumentNullException(nameof(storyDeployed));
            this.originalCommit = originalCommit;
        }

        public void AttachOriginalCommit(IVerifiedOriginalFusionCommit commit)
        {
            originalCommit = commit;
        }

        public IReadOnlyList<MobileFusionPreview> AvailableRecipes(
            IReadOnlyList<MobileUnitSnapshot> owned)
        {
            if (owned == null) throw new ArgumentNullException(nameof(owned));
            var result = new List<MobileFusionPreview>();
            foreach (var recipe in recipes.All)
            {
                List<long> selected;
                List<string> missing;
                FindMaterialsAndDeficits(recipe, owned, out selected, out missing);
                result.Add(new MobileFusionPreview(recipe.Id, recipe.Name,
                    BuildMaterialSummary(recipe), selected, missing));
            }
            result.Sort((a, b) => string.CompareOrdinal(a.RecipeId, b.RecipeId));
            return result.AsReadOnly();
        }

        public ManualActionResult ConfirmFusion(string recipeId,
            IReadOnlyList<long> requestedMaterialIds)
        {
            var recipe = recipes.Find(recipeId);
            if (recipe == null)
                return new ManualActionResult(ManualActionState.Rejected,
                    "도감에서 확인되지 않은 조합식은 실행할 수 없습니다.");
            if (requestedMaterialIds == null)
                return new ManualActionResult(ManualActionState.Rejected,
                    "선택한 재료가 없습니다.");
            if (!recipe.ExtraCostVerified)
                return ManualActionResult.Unavailable(
                    "도감에 표시되지 않은 추가 재화 비용 확인");

            // 미리보기 후 보유 유닛의 사망·합성·스토리 파견이 바뀌었어도
            // 현재 재고를 다시 조회하여 유효하지 않은 선택은 거부한다.
            var latest = ownedUnits();
            if (latest == null)
                return new ManualActionResult(ManualActionState.Rejected,
                    "현재 보유 유닛을 확인할 수 없습니다.");
            var liveById = new Dictionary<long, MobileUnitSnapshot>();
            foreach (var unit in latest)
            {
                if (unit == null || liveById.ContainsKey(unit.InstanceId))
                    return new ManualActionResult(ManualActionState.Rejected,
                        "보유 유닛 인스턴스 정보가 유효하지 않습니다.");
                liveById.Add(unit.InstanceId, unit);
            }

            var selectedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var seen = new HashSet<long>();
            foreach (var id in requestedMaterialIds)
            {
                MobileUnitSnapshot unit;
                if (id <= 0 || !seen.Add(id) || !liveById.TryGetValue(id, out unit))
                    return new ManualActionResult(ManualActionState.Rejected,
                        "중복되거나 더 이상 보유하지 않은 재료가 있습니다.");
                if (storyDeployed(id))
                    return new ManualActionResult(ManualActionState.Rejected,
                        "스토리 파견 중인 유닛은 조합 재료로 자동 선택하지 않습니다.");
                int count;
                selectedCounts.TryGetValue(unit.Unit.Id, out count);
                selectedCounts[unit.Unit.Id] = checked(count + 1);
            }

            int totalRequired = 0;
            foreach (var requirement in recipe.Ingredients)
            {
                int actual;
                selectedCounts.TryGetValue(requirement.UnitId, out actual);
                if (actual != requirement.Count)
                    return new ManualActionResult(ManualActionState.Rejected,
                        "원본 조합식의 재료 종류 또는 수량이 다릅니다.");
                totalRequired = checked(totalRequired + requirement.Count);
            }
            if (requestedMaterialIds.Count != totalRequired)
                return new ManualActionResult(ManualActionState.Rejected,
                    "원본 조합식에 없는 재료가 포함됐습니다.");

            // 원본 게임이 인정하는 골드/목재와 실제 유닛 소모·성공 판정은
            // 외부 원본 구현체에서 하나의 트랜잭션으로 처리한다.
            if (originalCommit == null)
                return ManualActionResult.Unavailable(
                    "원작 재료 소비·비용 지불·결과 소환을 수행할 조합 트리거");
            return originalCommit.TryCommit(recipe, requestedMaterialIds) ??
                new ManualActionResult(ManualActionState.Rejected,
                    "원작 조합 처리 결과가 없어 재료를 소비하지 않았습니다.");
        }

        private void FindMaterialsAndDeficits(VerifiedFusionRecipe recipe,
            IReadOnlyList<MobileUnitSnapshot> owned, out List<long> selected,
            out List<string> missing)
        {
            selected = new List<long>();
            missing = new List<string>();
            var byType = new Dictionary<string, List<long>>(StringComparer.Ordinal);
            foreach (var unit in owned)
            {
                if (unit == null || unit.InstanceId <= 0 || storyDeployed(unit.InstanceId)) continue;
                List<long> slots;
                if (!byType.TryGetValue(unit.Unit.Id, out slots))
                {
                    slots = new List<long>();
                    byType.Add(unit.Unit.Id, slots);
                }
                slots.Add(unit.InstanceId);
            }
            foreach (var values in byType.Values) values.Sort();
            foreach (var need in recipe.Ingredients)
            {
                List<long> slots;
                byType.TryGetValue(need.UnitId, out slots);
                int available = slots == null ? 0 : Math.Min(slots.Count, need.Count);
                for (int i = 0; i < available; i++) selected.Add(slots[i]);
                if (available < need.Count)
                    missing.Add(need.DisplayName + " (" + need.UnitId + ") " +
                        (need.Count - available) + "개 부족");
            }
        }

        private static string BuildMaterialSummary(VerifiedFusionRecipe recipe)
        {
            var parts = new List<string>();
            foreach (var need in recipe.Ingredients)
                parts.Add(need.DisplayName + " x" + need.Count);
            return string.Join(" + ", parts.ToArray()) + " | " +
                (recipe.ExtraCostVerified ?
                    "골드 " + recipe.Gold + " / 목재 " + recipe.Lumber :
                    "추가 재화 비용 미확인") +
                " (조합 확정 기능 준비 중)";
        }
    }
}
