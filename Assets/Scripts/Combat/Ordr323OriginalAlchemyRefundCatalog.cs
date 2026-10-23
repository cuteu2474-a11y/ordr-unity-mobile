using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // CJY creates only the UNIT-tagged BGI materials recorded in R3 -> Wq/Wr/Ws.
    // This immutable preview is NOT authority to consume a unit or debit mana.
    public sealed class Ordr323AlchemyRefundUnit
    {
        public string Rawcode { get; private set; }
        public int Count { get; private set; }

        internal Ordr323AlchemyRefundUnit(Dictionary<string, object> json)
        {
            Rawcode = OrdrJson.RequiredString(json, "rawcode");
            var count = OrdrJson.Number(json, "count");
            if (Rawcode.Length != 4 || !count.HasValue || count.Value < 1 ||
                count.Value > int.MaxValue || Math.Floor(count.Value) != count.Value)
                throw new FormatException("연금술 UNIT 환급 rawcode 또는 수량이 잘못됐습니다.");
            Count = (int)count.Value;
        }
    }

    public sealed class Ordr323AlchemyRefundRecipe
    {
        public string TargetRawcode { get; private set; }
        public IReadOnlyList<string> FusionAbilities { get; private set; }
        public IReadOnlyList<Ordr323AlchemyRefundUnit> RefundUnits { get; private set; }
        public int IgnoredNonUnitConditions { get; private set; }

        internal Ordr323AlchemyRefundRecipe(Dictionary<string, object> json)
        {
            TargetRawcode = OrdrJson.RequiredString(json, "targetRawcode");
            if (TargetRawcode.Length != 4)
                throw new FormatException("연금술 대상의 워크래프트 rawcode는 4자여야 합니다.");
            var abilities = new List<string>();
            foreach (var item in OrdrJson.Array(OrdrJson.Value(json, "fusionAbilities"),
                "fusionAbilities"))
            {
                var ability = item as string;
                if (ability == null || ability.Length != 4)
                    throw new FormatException("연금술 R3 능력 rawcode가 잘못됐습니다.");
                abilities.Add(ability);
            }
            if (abilities.Count == 0)
                throw new FormatException("등록된 연금술 대상의 R3 능력이 비었습니다.");
            FusionAbilities = abilities.AsReadOnly();

            var refunded = new List<Ordr323AlchemyRefundUnit>();
            foreach (var item in OrdrJson.Array(OrdrJson.Value(json, "refundUnits"),
                "refundUnits"))
                refunded.Add(new Ordr323AlchemyRefundUnit(
                    OrdrJson.Object(item, "refund UNIT")));
            RefundUnits = refunded.AsReadOnly();
            var ignored = OrdrJson.Number(json, "ignoredNonUnitConditions");
            if (!ignored.HasValue || ignored.Value < 0 || ignored.Value > int.MaxValue ||
                Math.Floor(ignored.Value) != ignored.Value)
                throw new FormatException("연금술 비-UNIT 조건 수량이 잘못됐습니다.");
            IgnoredNonUnitConditions = (int)ignored.Value;
        }
    }

    public sealed class Ordr323OriginalAlchemyRefundCatalog
    {
        public const string ExpectedMapSha256 =
            "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c";
        public const string ExpectedJassSha256 =
            "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d";

        private readonly Dictionary<string, Ordr323AlchemyRefundRecipe> entries =
            new Dictionary<string, Ordr323AlchemyRefundRecipe>(StringComparer.Ordinal);
        public int Count { get { return entries.Count; } }
        public int ExistingFusionArchiveInterleavingMismatches { get; private set; }

        public Ordr323AlchemyRefundRecipe Find(string targetRawcode)
        {
            Ordr323AlchemyRefundRecipe recipe;
            return !string.IsNullOrWhiteSpace(targetRawcode) &&
                entries.TryGetValue(targetRawcode, out recipe) ? recipe : null;
        }

        public static Ordr323OriginalAlchemyRefundCatalog FromJson(string json)
        {
            var data = OrdrJson.Object(OrdrJson.Parse(json), "CJY source-ordered refunds");
            if (OrdrJson.RequiredString(data, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(data, "sourceMapSha256") != ExpectedMapSha256 ||
                OrdrJson.RequiredString(data, "sourceJassSha256") != ExpectedJassSha256)
                throw new FormatException("기준 W3X/JASS 이외의 연금술 환급표입니다.");
            var rules = OrdrJson.Object(OrdrJson.Value(data, "semantics"), "CJY semantics");
            foreach (string flag in new[] {
                "targetMustPassCJZOwnershipAndAbilityCheck",
                "onlyBgiUnitConditionsAreRefunded", "preserveR3AndBgiRegistrationOrder",
                "missingR3RecipeConsumesTargetWithoutRefund" })
            {
                var value = OrdrJson.Value(rules, flag);
                if (!(value is bool) || !(bool)value)
                    throw new FormatException("원본 CJY 환급 조건이 보존되지 않았습니다: " + flag);
            }
            var authorized = OrdrJson.Value(rules, "includesTargetConsumptionAndManaTransaction");
            if (!(authorized is bool) || (bool)authorized)
                throw new FormatException("검증되지 않은 연금술 시전 권한이 포함됐습니다.");
            if (OrdrJson.RequiredString(rules, "createdUnitOrder") != "holdposition" ||
                OrdrJson.RequiredString(rules, "undeadCreatedUnitAbility") != "A07Y" ||
                OrdrJson.RequiredString(rules, "creationPosition") != "player start location")
                throw new FormatException("원본 CJY 생성 후속 효과가 일치하지 않습니다.");
            var result = new Ordr323OriginalAlchemyRefundCatalog();
            var reordered = OrdrJson.Number(data, "existingFusionArchiveInterleavingMismatches");
            if (!reordered.HasValue || reordered.Value != 143)
                throw new FormatException("기존 CWR 자료와 원본의 비-UNIT 섞인 순서 불일치 건수가 다릅니다.");
            result.ExistingFusionArchiveInterleavingMismatches = (int)reordered.Value;
            foreach (var item in OrdrJson.Array(OrdrJson.Value(data, "entries"), "entries"))
            {
                var recipe = new Ordr323AlchemyRefundRecipe(
                    OrdrJson.Object(item, "alchemy recipe"));
                if (result.entries.ContainsKey(recipe.TargetRawcode))
                    throw new FormatException("중복 연금술 대상: " + recipe.TargetRawcode);
                result.entries.Add(recipe.TargetRawcode, recipe);
            }
            var total = OrdrJson.Number(data, "targetCount");
            if (!total.HasValue || total.Value != 265 || result.Count != 265)
                throw new FormatException("원본 BjB의 연금술 대상 265개가 누락됐습니다.");
            return result;
        }
    }
}
