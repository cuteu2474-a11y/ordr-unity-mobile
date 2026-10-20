using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    public sealed class VerifiedFusionIngredient
    {
        public string UnitId { get; private set; }
        public int Count { get; private set; }

        internal VerifiedFusionIngredient(Dictionary<string, object> data)
        {
            UnitId = OrdrJson.RequiredString(data, "unitId");
            double? count = OrdrJson.Number(data, "count");
            if (!count.HasValue || count.Value < 1 ||
                count.Value > int.MaxValue || Math.Floor(count.Value) != count.Value)
                throw new FormatException("조합 재료 수량은 양의 정수여야 합니다.");
            Count = (int)count.Value;
        }
    }

    public sealed class VerifiedFusionRecipe
    {
        public string Id { get; private set; }
        public string ResultUnitId { get; private set; }
        public string Name { get; private set; }
        public string SourceUrl { get; private set; }
        public int Gold { get; private set; }
        public int Lumber { get; private set; }
        public IReadOnlyList<VerifiedFusionIngredient> Ingredients { get; private set; }
        public string CostSummary { get; private set; }

        internal VerifiedFusionRecipe(Dictionary<string, object> source, CharacterUnitCatalog units)
        {
            Id = OrdrJson.RequiredString(source, "id");
            ResultUnitId = OrdrJson.RequiredString(source, "resultUnitId");
            Name = OrdrJson.RequiredString(source, "name");
            SourceUrl = OrdrJson.RequiredString(source, "sourceUrl");
            if (units.Find(ResultUnitId) == null)
                throw new FormatException("조합 결과 유닛이 카탈로그에 없습니다: " + ResultUnitId);
            if (!SourceUrl.StartsWith("https://ordr.kr/character/", StringComparison.Ordinal))
                throw new FormatException("원작 도감 출처가 확인되지 않은 조합식: " + Id);
            if (OrdrJson.RequiredString(source, "verifiedAs") != "ordr-2.323-web-catalog")
                throw new FormatException("미확인 버전의 조합식을 사용할 수 없습니다.");

            var costs = OrdrJson.Object(OrdrJson.Value(source, "extraCost"), "extraCost");
            Gold = GetNonNegativeInt(costs, "gold");
            Lumber = GetNonNegativeInt(costs, "lumber");
            var specialItems = OrdrJson.Array(OrdrJson.Value(costs, "items"), "extraCost.items");
            if (specialItems.Count != 0)
                throw new FormatException("특수 아이템 비용을 계산하는 조합식은 아직 미지원.");

            var materials = new List<VerifiedFusionIngredient>();
            var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in OrdrJson.Array(OrdrJson.Value(source, "ingredients"), "ingredients"))
            {
                var material = new VerifiedFusionIngredient(OrdrJson.Object(item, "ingredient"));
                if (units.Find(material.UnitId) == null)
                    throw new FormatException(Id + "에 없는 재료 ID: " + material.UnitId);
                if (!uniqueIds.Add(material.UnitId))
                    throw new FormatException("중복 재료 ID: " + material.UnitId);
                materials.Add(material);
            }
            if (materials.Count == 0)
                throw new FormatException("재료가 없는 조합식은 등록할 수 없습니다.");
            Ingredients = materials.AsReadOnly();
            CostSummary = "원작 재료 " + SumQuantity(materials) + "개 / 골드 " + Gold +
                " / 목재 " + Lumber + " (최종 제작은 원본 트리거 검증 필요)";
        }

        private static int SumQuantity(List<VerifiedFusionIngredient> materials)
        {
            int total = 0;
            foreach (var material in materials) checked { total += material.Count; }
            return total;
        }

        private static int GetNonNegativeInt(Dictionary<string, object> dict, string key)
        {
            double? value = OrdrJson.Number(dict, key);
            if (!value.HasValue || value.Value < 0 || value.Value > int.MaxValue ||
                Math.Floor(value.Value) != value.Value)
                throw new FormatException(key + "는 0 이상의 정수여야 합니다.");
            return (int)value.Value;
        }
    }

    // 현재 확인된 3개 조합식을 기반으로 작동한다.
    // 이 카탈로그는 제작 완료를 주장하지 않고 원본 데이터 추가를 지원한다.
    public sealed class VerifiedFusionCatalog
    {
        private readonly Dictionary<string, VerifiedFusionRecipe> recipes =
            new Dictionary<string, VerifiedFusionRecipe>(StringComparer.Ordinal);
        public IEnumerable<VerifiedFusionRecipe> All { get { return recipes.Values; } }
        public int Count { get { return recipes.Count; } }

        public VerifiedFusionRecipe Find(string id)
        {
            VerifiedFusionRecipe recipe;
            return id != null && recipes.TryGetValue(id, out recipe) ? recipe : null;
        }

        public static VerifiedFusionCatalog FromJson(string json, CharacterUnitCatalog units)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            var root = OrdrJson.Object(OrdrJson.Parse(json), "fusion catalog");
            if (OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]")
                throw new FormatException("조합식 버전이 일치하지 않습니다.");
            var catalog = new VerifiedFusionCatalog();
            foreach (var item in OrdrJson.Array(OrdrJson.Value(root, "recipes"), "recipes"))
            {
                var recipe = new VerifiedFusionRecipe(
                    OrdrJson.Object(item, "recipe"), units);
                if (catalog.recipes.ContainsKey(recipe.Id))
                    throw new FormatException("중복 조합식: " + recipe.Id);
                catalog.recipes.Add(recipe.Id, recipe);
            }
            return catalog;
        }
    }
}
