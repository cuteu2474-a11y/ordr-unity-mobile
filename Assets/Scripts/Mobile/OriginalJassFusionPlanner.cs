using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    public sealed class JassFusionMaterial
    {
        public string Rawcode { get; private set; }
        public string CatalogUnitId { get; private set; }
        public string Name { get; private set; }
        public int RequiredCount { get; private set; }
        public int OwnedCount { get; private set; }
        public int MissingCount { get { return Math.Max(0, RequiredCount - OwnedCount); } }

        internal JassFusionMaterial(string rawcode, OriginalUnitRawcodeLabelLink link,
            int required, int owned)
        {
            Rawcode = rawcode;
            CatalogUnitId = link == null ? null : link.CatalogUnitId;
            Name = link == null ? "미매핑 원본 ID " + rawcode : link.UnitDisplayName;
            RequiredCount = required;
            OwnedCount = owned;
        }
    }

    public sealed class JassFusionReadiness
    {
        public string AbilityRawcode { get; private set; }
        public string ResultRawcode { get; private set; }
        public string ResultUnitId { get; private set; }
        public string ResultName { get; private set; }
        public IReadOnlyList<JassFusionMaterial> Materials { get; private set; }
        public IReadOnlyList<string> BlockingReasons { get; private set; }
        public IReadOnlyList<long> SuggestedMaterialIds { get; private set; }
        public int GoldCost { get; private set; }
        public int LumberCost { get; private set; }
        public bool IsReadyForUserConfirmation { get { return BlockingReasons.Count == 0; } }

        internal JassFusionReadiness(OriginalJassFusionRegistration registration,
            string resultRawcode, OriginalUnitRawcodeLabelLink result,
            List<JassFusionMaterial> materials, List<string> reasons,
            List<long> selected, int gold, int lumber)
        {
            AbilityRawcode = registration.AbilityRawcode;
            ResultRawcode = resultRawcode;
            ResultUnitId = result == null ? null : result.CatalogUnitId;
            ResultName = result == null ? "미매핑 " + resultRawcode : result.UnitDisplayName;
            Materials = materials.AsReadOnly();
            BlockingReasons = reasons.AsReadOnly();
            SuggestedMaterialIds = selected.AsReadOnly();
            GoldCost = gold;
            LumberCost = lumber;
        }
    }

    // 구체적인 원본 규칙(UBAN/KING, 리셋/특수 라운드 등)은 외부 평가자가 제공해야 한다.
    // 단순 유닛 수량 체크만으로 합성이 허가되지 않도록 계획 단계에서 fail closed.
    public interface IOriginalJassSpecialConditionEvaluator
    {
        bool CanSatisfy(OriginalJassFusionInstruction instruction,
            string abilityRawcode, out string rejectionReason);
    }

    public sealed class OriginalJassFusionPlanner
    {
        private readonly OriginalJassFusionCatalog original;
        private readonly OriginalUnitRawcodeLabelCatalog labels;
        private readonly Func<long, bool> deployed;
        private IOriginalJassSpecialConditionEvaluator specialRules;

        public OriginalJassFusionPlanner(OriginalJassFusionCatalog original,
            OriginalUnitRawcodeLabelCatalog labels, Func<long, bool> storyDeployed,
            IOriginalJassSpecialConditionEvaluator specialRules = null)
        {
            this.original = original ?? throw new ArgumentNullException(nameof(original));
            this.labels = labels ?? throw new ArgumentNullException(nameof(labels));
            this.deployed = storyDeployed ?? throw new ArgumentNullException(nameof(storyDeployed));
            this.specialRules = specialRules;
        }

        public void SetSpecialRules(IOriginalJassSpecialConditionEvaluator rules)
        {
            specialRules = rules;
        }

        public int RegistrationCount { get { return original.Count; } }
        public int LabelMappedUnitCount { get { return labels.Count; } }

        public JassFusionReadiness Assess(string abilityRawcode,
            IReadOnlyList<MobileUnitSnapshot> ownedUnits,
            int? verifiedGold, int? verifiedLumber)
        {
            if (ownedUnits == null) throw new ArgumentNullException(nameof(ownedUnits));
            var recipe = original.FindByAbility(abilityRawcode);
            if (recipe == null)
                throw new KeyNotFoundException("원본 JASS에 없는 조합 능력: " + abilityRawcode);

            var reasons = new List<string>();
            var materials = new List<JassFusionMaterial>();
            var selected = new List<long>();
            var allUnits = new Dictionary<string, List<long>>(StringComparer.Ordinal);
            var uniqueIds = new HashSet<long>();
            foreach (var unit in ownedUnits)
            {
                if (unit == null || unit.InstanceId <= 0 || !uniqueIds.Add(unit.InstanceId))
                    throw new FormatException("보유 인스턴스 ID가 잘못됐거나 중복됐습니다.");
                if (deployed(unit.InstanceId)) continue;
                List<long> ids;
                if (!allUnits.TryGetValue(unit.Unit.Id, out ids))
                {
                    ids = new List<long>();
                    allUnits.Add(unit.Unit.Id, ids);
                }
                ids.Add(unit.InstanceId);
            }
            foreach (var ids in allUnits.Values) ids.Sort();

            string resultCode = recipe.Results[0].Rawcode;
            var resultLabel = labels.FindRawcode(resultCode);
            if (recipe.Results.Count != 1 || recipe.Results[0].Kind != "UNIT" ||
                recipe.Results[0].Count != 1)
                reasons.Add("원본 결과 수량 또는 결과 타입 미지원: BGH");
            if (resultLabel == null)
                reasons.Add("결과 캐릭터 원본 ID 미매핑: " + resultCode);

            int gold = 0, lumber = 0;
            var needed = new Dictionary<string, int>(StringComparer.Ordinal);
            var neededOrder = new List<string>();
            foreach (var condition in recipe.OrderedConditions)
            {
                if (condition.Kind == "UNIT")
                {
                    int quantity;
                    if (!needed.TryGetValue(condition.Rawcode, out quantity))
                    {
                        neededOrder.Add(condition.Rawcode);
                        quantity = 0;
                    }
                    needed[condition.Rawcode] = checked(quantity + condition.Count);
                }
                else if (condition.Kind == "GOLD" && condition.Rawcode == "GOLD")
                    gold = checked(gold + condition.Count);
                else if (condition.Kind == "WOOD" && condition.Rawcode == "WOOD")
                    lumber = checked(lumber + condition.Count);
                else
                {
                    string reason;
                    // UPUN appears as a special presentation/operation tag in CHV/CHX.
                    // Even when a handler approves it, original post-commit behavior
                    // must still be implemented by an authoritative transaction service.
                    if (specialRules == null ||
                        !specialRules.CanSatisfy(condition, recipe.AbilityRawcode, out reason))
                    {
                        reasons.Add("원본 추가 조건 미검증: " + condition.Kind +
                            "/" + condition.Rawcode);
                    }
                }
            }

            foreach (string rawcode in neededOrder)
            {
                var link = labels.FindRawcode(rawcode);
                int required = needed[rawcode];
                int owned = 0;
                List<long> list = null;
                if (link == null) reasons.Add("재료 유닛 원시 ID 미매핑: " + rawcode);
                else if (allUnits.TryGetValue(link.CatalogUnitId, out list))
                {
                    owned = Math.Min(list.Count, required);
                    for (int i = 0; i < owned; i++) selected.Add(list[i]);
                }
                materials.Add(new JassFusionMaterial(rawcode, link, required, owned));
                if (owned < required)
                    reasons.Add("재료 부족: " + rawcode + " " + (required - owned) + "기");
            }

            if (!verifiedGold.HasValue)
                reasons.Add("원작 골드 잔액 미연결");
            else if (verifiedGold.Value < gold || verifiedGold.Value < 0)
                reasons.Add("골드 부족");
            if (!verifiedLumber.HasValue)
                reasons.Add("원작 목재 잔액 미연결");
            else if (verifiedLumber.Value < lumber || verifiedLumber.Value < 0)
                reasons.Add("목재 부족");

            // 이름에 근거한 매핑은 확인자료일 뿐 실제 object ID 완전검증 아님.
            // 전체가 충족되더라도 원자적인 원본 결제/소환 서비스 전에는 승인하지 않는다.
            reasons.Add("원본 UNIT 오브젝트/제작 트랜잭션 최종 검증 필요");
            return new JassFusionReadiness(recipe, resultCode, resultLabel,
                materials, reasons, selected, gold, lumber);
        }

        public IReadOnlyList<JassFusionReadiness> AssessNamedResults(
            IReadOnlyList<MobileUnitSnapshot> ownedUnits,
            int? verifiedGold, int? verifiedLumber)
        {
            var result = new List<JassFusionReadiness>();
            foreach (var entry in original.All)
            {
                if (entry.Results.Count == 0 ||
                    labels.FindRawcode(entry.Results[0].Rawcode) == null) continue;
                result.Add(Assess(entry.AbilityRawcode, ownedUnits, verifiedGold, verifiedLumber));
            }
            result.Sort((a, b) => string.CompareOrdinal(a.ResultName, b.ResultName));
            return result.AsReadOnly();
        }
    }
}
