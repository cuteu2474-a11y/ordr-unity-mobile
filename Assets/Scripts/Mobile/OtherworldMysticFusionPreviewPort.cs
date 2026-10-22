using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    // 실행 구현체는 원본 객체 ID·추가 조건·재화/아이템 소비를 검수하고
    // 하나의 롤백 가능한 월드 트랜잭션으로 처리한 경우에만 연결한다.
    public interface IVerifiedOtherworldMysticFusionCommit
    {
        ManualActionResult TryCommit(OtherworldMysticRecipe recipe,
            IReadOnlyList<long> exactMaterialInstanceIds);
    }

    // 실제 조합 실행이 아닌 모바일 재료 선택 보조. 미검증 JASS 조건으로 소비하지 않는다.
    public sealed class OtherworldMysticFusionPreviewPort : IOriginalFusionPort
    {
        private readonly OtherworldMysticFusionCatalog catalog;
        private readonly Func<long, bool> storyDeployed;
        private IVerifiedOtherworldMysticFusionCommit originalCommit;

        public bool CanCommit { get { return originalCommit != null; } }

        public OtherworldMysticFusionPreviewPort(OtherworldMysticFusionCatalog catalog,
            Func<long, bool> storyDeployed,
            IVerifiedOtherworldMysticFusionCommit verifiedCommit = null)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.storyDeployed = storyDeployed ?? throw new ArgumentNullException(nameof(storyDeployed));
            originalCommit = verifiedCommit;
        }

        public void AttachVerifiedCommit(IVerifiedOtherworldMysticFusionCommit commit)
        {
            originalCommit = commit;
        }

        public IReadOnlyList<MobileFusionPreview> AvailableRecipes(
            IReadOnlyList<MobileUnitSnapshot> ownedUnits)
        {
            if (ownedUnits == null) throw new ArgumentNullException(nameof(ownedUnits));
            var byId = new Dictionary<string, List<long>>(StringComparer.Ordinal);
            var seen = new HashSet<long>();
            var unitGrades = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var unit in ownedUnits)
            {
                if (unit == null || unit.InstanceId <= 0 || !seen.Add(unit.InstanceId))
                    throw new ArgumentException("보유 유닛의 인스턴스 ID가 중복되거나 유효하지 않습니다.");
                if (storyDeployed(unit.InstanceId)) continue;
                List<long> slots;
                if (!byId.TryGetValue(unit.Unit.Id, out slots))
                {
                    slots = new List<long>();
                    byId.Add(unit.Unit.Id, slots);
                }
                slots.Add(unit.InstanceId);
                unitGrades[unit.Unit.Id] = unit.Unit.Grade;
            }
            foreach (var slots in byId.Values) slots.Sort();

            var result = new List<MobileFusionPreview>();
            foreach (var recipe in catalog.All)
            {
                var selected = new List<long>();
                var taken = new HashSet<long>();
                var missing = new List<string>();
                foreach (var ingredient in recipe.Ingredients)
                {
                    if (ingredient.Kind == "any-random") continue;
                    List<long> available;
                    byId.TryGetValue(ingredient.UnitId, out available);
                    int assigned = 0;
                    if (available != null)
                        foreach (long instanceId in available)
                        {
                            if (assigned >= ingredient.Count) break;
                            if (!taken.Add(instanceId)) continue;
                            selected.Add(instanceId);
                            assigned++;
                        }
                    if (assigned < ingredient.Count)
                        missing.Add(ingredient.DisplayName + " " + (ingredient.Count - assigned) + "기");
                }
                foreach (var ingredient in recipe.Ingredients)
                {
                    if (ingredient.Kind != "any-random") continue;
                    var randomCandidates = new List<long>();
                    foreach (var pair in byId)
                        if (unitGrades[pair.Key] == "랜덤")
                            foreach (long id in pair.Value)
                                if (!taken.Contains(id)) randomCandidates.Add(id);
                    randomCandidates.Sort();
                    int assigned = Math.Min(randomCandidates.Count, ingredient.Count);
                    for (int i = 0; i < assigned; i++)
                    {
                        taken.Add(randomCandidates[i]);
                        selected.Add(randomCandidates[i]);
                    }
                    if (assigned < ingredient.Count)
                        missing.Add("랜덤 등급 유닛 " + (ingredient.Count - assigned) + "기");
                }
                // 보유 재화·토큰과 원본 소비 조건은 미검증이므로 '조합 준비 완료'로 표시하지 않음.
                if (originalCommit == null)
                    missing.Add("특수 요구/원본 조합 트리거 미검증 (제작 비활성)");
                var parts = new List<string>();
                foreach (var ingredient in recipe.Ingredients)
                    parts.Add(ingredient.DisplayName + " x" + ingredient.Count);
                string summary = string.Join(" + ", parts.ToArray()) +
                    " | " + recipe.Gold + "골드 / 목재 " + recipe.Lumber +
                    " / " + string.Join(" + ", new List<string>(recipe.SpecialRequirements).ToArray());
                result.Add(new MobileFusionPreview(recipe.ResultUnitId, recipe.ResultName,
                    summary, selected, missing));
            }
            result.Sort((a, b) => string.CompareOrdinal(a.RecipeId, b.RecipeId));
            return result.AsReadOnly();
        }

        public ManualActionResult ConfirmFusion(string recipeId,
            IReadOnlyList<long> selectedMaterialInstances)
        {
            var recipe = catalog.Find(recipeId);
            if (recipe == null)
                return new ManualActionResult(ManualActionState.Rejected,
                    "신비 도감에 없는 조합은 실행할 수 없습니다.");
            if (originalCommit == null)
                return ManualActionResult.Unavailable("신비 조합 원본 JASS 재료 소비·비용 검증");
            return originalCommit.TryCommit(recipe, selectedMaterialInstances) ??
                new ManualActionResult(ManualActionState.Rejected,
                    "원본 조합 거래의 성공 여부가 확인되지 않았습니다.");
        }
    }
}
