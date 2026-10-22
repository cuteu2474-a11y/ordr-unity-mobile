using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // 2.323 공개 도감의 조합 미리보기 전용 데이터. 원본 JASS 소비 권한이 없다.
    public sealed class OtherworldMysticIngredient
    {
        public string Kind { get; private set; }
        public string UnitId { get; private set; }
        public string DisplayName { get; private set; }
        public int Count { get; private set; }

        internal OtherworldMysticIngredient(Dictionary<string, object> source, CharacterUnitCatalog units)
        {
            Kind = OrdrJson.RequiredString(source, "kind");
            Count = OtherworldMysticFusionCatalog.NonNegativeInteger(source, "count", false);
            if (Kind == "unit")
            {
                UnitId = OrdrJson.RequiredString(source, "unitId");
                DisplayName = units.Require(UnitId).Name;
            }
            else if (Kind == "any-random")
            {
                if (OrdrJson.Value(source, "unitId") != null)
                    throw new FormatException("랜덤 등급 슬롯에 특정 유닛 ID를 지정할 수 없습니다.");
                DisplayName = "랜덤 등급 유닛 (별도 1기)";
            }
            else throw new FormatException("알 수 없는 신비 조합 재료 구분: " + Kind);
        }
    }

    public sealed class OtherworldMysticRecipe
    {
        public string ResultUnitId { get; private set; }
        public string ResultName { get; private set; }
        public string SourceUrl { get; private set; }
        public int Gold { get; private set; }
        public int Lumber { get; private set; }
        public IReadOnlyList<string> SpecialRequirements { get; private set; }
        public IReadOnlyList<OtherworldMysticIngredient> Ingredients { get; private set; }
        public bool AuthorizedForConsumption { get { return false; } }

        internal OtherworldMysticRecipe(Dictionary<string, object> source, CharacterUnitCatalog units)
        {
            ResultUnitId = OrdrJson.RequiredString(source, "resultUnitId");
            ResultName = OrdrJson.RequiredString(source, "resultName");
            var result = units.Require(ResultUnitId);
            if (result.Grade != "신비함" || result.Name != ResultName)
                throw new FormatException("신비 등급 결과 유닛 이름/등급이 다릅니다: " + ResultUnitId);
            SourceUrl = OrdrJson.RequiredString(source, "sourceUrl");
            if (!SourceUrl.StartsWith("https://ordr.kr/character/MY", StringComparison.Ordinal))
                throw new FormatException("신비 조합 도감 링크가 없습니다: " + ResultUnitId);
            Gold = OtherworldMysticFusionCatalog.NonNegativeInteger(source, "gold", true);
            Lumber = OtherworldMysticFusionCatalog.NonNegativeInteger(source, "lumber", true);

            var specials = new List<string>();
            foreach (var item in OrdrJson.Array(OrdrJson.Value(source, "specialItems"), "specialItems"))
            {
                string label = item as string;
                if (string.IsNullOrWhiteSpace(label) || specials.Contains(label))
                    throw new FormatException("특수 비용 이름이 없거나 중복되었습니다.");
                specials.Add(label);
            }
            if (specials.Count == 0)
                throw new FormatException("행운의토큰 등 특수 요구사항을 누락했습니다.");
            SpecialRequirements = specials.AsReadOnly();

            var ingredients = new List<OtherworldMysticIngredient>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int wildcardCount = 0;
            bool hasRandomBase = false;
            foreach (var raw in OrdrJson.Array(OrdrJson.Value(source, "ingredients"), "ingredients"))
            {
                var ingredient = new OtherworldMysticIngredient(OrdrJson.Object(raw, "ingredient"), units);
                if (ingredient.Kind == "any-random") wildcardCount += ingredient.Count;
                else
                {
                    if (!seen.Add(ingredient.UnitId))
                        throw new FormatException("같은 재료 유닛 ID가 중복되었습니다.");
                    hasRandomBase |= units.Require(ingredient.UnitId).Grade == "랜덤";
                }
                ingredients.Add(ingredient);
            }
            if (ingredients.Count == 0 || wildcardCount > 1 || !hasRandomBase)
                throw new FormatException("신비 조합의 랜덤 재료와 수량이 유효하지 않습니다.");
            Ingredients = ingredients.AsReadOnly();
        }
    }

    public sealed class OtherworldMysticFusionPreview
    {
        public OtherworldMysticRecipe Recipe { get; private set; }
        public IReadOnlyList<string> MissingUnitMaterials { get; private set; }
        public bool HasAllUnitMaterials { get { return MissingUnitMaterials.Count == 0; } }
        // 도감에 적힌 항목만으로는 실제 소비 조건과 순서를 보장할 수 없다.
        public bool CanExecute { get { return false; } }

        internal OtherworldMysticFusionPreview(OtherworldMysticRecipe recipe, List<string> missing)
        {
            Recipe = recipe;
            MissingUnitMaterials = missing.AsReadOnly();
        }
    }

    public sealed class OtherworldMysticFusionCatalog
    {
        private readonly Dictionary<string, OtherworldMysticRecipe> entries =
            new Dictionary<string, OtherworldMysticRecipe>(StringComparer.Ordinal);
        private readonly CharacterUnitCatalog units;

        private OtherworldMysticFusionCatalog(CharacterUnitCatalog units) { this.units = units; }
        public int Count { get { return entries.Count; } }
        public IEnumerable<OtherworldMysticRecipe> All { get { return entries.Values; } }
        public OtherworldMysticRecipe Find(string resultUnitId)
        {
            OtherworldMysticRecipe result;
            return resultUnitId != null && entries.TryGetValue(resultUnitId, out result) ? result : null;
        }

        public static OtherworldMysticFusionCatalog FromJson(string json, CharacterUnitCatalog units)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            var root = OrdrJson.Object(OrdrJson.Parse(json), "otherworld-mystic-recipes");
            if (OrdrJson.RequiredString(root, "format") != "ordr-otherworld-mystic-web-recipes-v1" ||
                OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "mode") != "nightmare-otherworld" ||
                OrdrJson.RequiredString(root, "sourceVersion") != "2.323" ||
                OrdrJson.RequiredString(root, "catalogGrade") != "신비함" ||
                !(OrdrJson.Value(root, "authorizedForConsumption") is bool) ||
                (bool)OrdrJson.Value(root, "authorizedForConsumption") ||
                !(OrdrJson.Value(root, "originalJassConditionsVerified") is bool) ||
                (bool)OrdrJson.Value(root, "originalJassConditionsVerified"))
                throw new FormatException("원본 검증을 완료하지 않은 신비 도감 조합은 읽기 전용이어야 합니다.");

            var catalog = new OtherworldMysticFusionCatalog(units);
            foreach (var row in OrdrJson.Array(OrdrJson.Value(root, "recipes"), "recipes"))
            {
                var recipe = new OtherworldMysticRecipe(OrdrJson.Object(row, "recipe"), units);
                if (catalog.entries.ContainsKey(recipe.ResultUnitId))
                    throw new FormatException("중복 신비 조합 결과: " + recipe.ResultUnitId);
                catalog.entries.Add(recipe.ResultUnitId, recipe);
            }
            if (catalog.Count != NonNegativeInteger(root, "recipeCount", true))
                throw new FormatException("신비 조합 개수와 실제 목록이 다릅니다.");
            return catalog;
        }

        // 고정 재료부터 배정한 뒤 남은 랜덤 등급 유닛만 와일드카드에 배정한다.
        // 동일한 소유 유닛 한 기가 두 재료 슬롯에 중복으로 쓰이지 않게 한다.
        public OtherworldMysticFusionPreview Preview(string resultUnitId,
            IReadOnlyDictionary<string, int> ownedCounts)
        {
            var recipe = Find(resultUnitId);
            if (recipe == null) throw new KeyNotFoundException("신비 조합을 찾을 수 없습니다: " + resultUnitId);
            if (ownedCounts == null) throw new ArgumentNullException(nameof(ownedCounts));
            var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in ownedCounts)
            {
                if (units.Find(entry.Key) == null || entry.Value < 0)
                    throw new ArgumentException("보유 유닛 ID나 수량이 유효하지 않습니다.");
                remaining.Add(entry.Key, entry.Value);
            }
            var missing = new List<string>();
            foreach (var need in recipe.Ingredients)
            {
                if (need.Kind == "any-random") continue;
                int have;
                remaining.TryGetValue(need.UnitId, out have);
                int allocated = Math.Min(have, need.Count);
                remaining[need.UnitId] = have - allocated;
                if (allocated < need.Count)
                    missing.Add(need.DisplayName + " " + (need.Count - allocated) + "기");
            }
            foreach (var need in recipe.Ingredients)
            {
                if (need.Kind != "any-random") continue;
                int availableRandoms = 0;
                foreach (var owned in remaining)
                    if (units.Require(owned.Key).Grade == "랜덤")
                        availableRandoms = checked(availableRandoms + owned.Value);
                if (availableRandoms < need.Count)
                    missing.Add("랜덤 등급 유닛 " + (need.Count - availableRandoms) + "기");
            }
            return new OtherworldMysticFusionPreview(recipe, missing);
        }

        internal static int NonNegativeInteger(Dictionary<string, object> source, string key, bool allowZero)
        {
            double? value = OrdrJson.Number(source, key);
            if (!value.HasValue || value.Value > int.MaxValue ||
                value.Value < (allowZero ? 0 : 1) || Math.Floor(value.Value) != value.Value)
                throw new FormatException(key + "는 유효한 정수여야 합니다.");
            return (int)value.Value;
        }
    }
}
