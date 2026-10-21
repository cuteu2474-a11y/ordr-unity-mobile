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

        internal JassFusionMaterial(string rawcode, string catalogUnitId,
            string unitName, int required, int owned)
        {
            Rawcode = rawcode;
            CatalogUnitId = catalogUnitId;
            Name = string.IsNullOrWhiteSpace(unitName) ?
                "미매핑 원본 ID " + rawcode : unitName;
            RequiredCount = required;
            OwnedCount = owned;
        }
    }

    // The exact instance assignment for each original UNIT rawcode. The UI may
    // present a flat selection, but the transactional world must not guess which
    // unit an original rawcode refers to (especially for duplicate materials).
    public sealed class JassFusionMaterialBinding
    {
        public string Rawcode { get; private set; }
        public IReadOnlyList<long> InstanceIds { get; private set; }

        internal JassFusionMaterialBinding(string rawcode, List<long> ids)
        {
            Rawcode = rawcode;
            InstanceIds = new List<long>(ids).AsReadOnly();
        }
    }

    public sealed class JassFusionReadiness
    {
        public string AbilityRawcode { get; private set; }
        public string ResultRawcode { get; private set; }
        public string ResultUnitId { get; private set; }
        public string ResultName { get; private set; }
        public IReadOnlyList<JassFusionMaterial> Materials { get; private set; }
        public IReadOnlyList<JassFusionMaterialBinding> MaterialBindings { get; private set; }
        internal OriginalJassFusionRegistration SourceRegistration { get; private set; }
        public IReadOnlyList<string> BlockingReasons { get; private set; }
        public IReadOnlyList<long> SuggestedMaterialIds { get; private set; }
        public int GoldCost { get; private set; }
        public int LumberCost { get; private set; }
        public bool IsReadyForUserConfirmation { get { return BlockingReasons.Count == 0; } }

        internal JassFusionReadiness(OriginalJassFusionRegistration registration,
            string resultRawcode, string resultUnitId, string resultName,
            List<JassFusionMaterial> materials, List<JassFusionMaterialBinding> bindings,
            List<string> reasons, List<long> selected, int gold, int lumber)
        {
            AbilityRawcode = registration.AbilityRawcode;
            ResultRawcode = resultRawcode;
            ResultUnitId = resultUnitId;
            ResultName = string.IsNullOrWhiteSpace(resultName) ?
                "미매핑 " + resultRawcode : resultName;
            SourceRegistration = registration;
            Materials = materials.AsReadOnly();
            MaterialBindings = bindings.AsReadOnly();
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
        private readonly CharacterUnitCatalog catalog;
        private IOriginalJassSpecialConditionEvaluator specialRules;
        private IVerifiedUnitRawcodeMap verifiedObjectMap;

        public OriginalJassFusionPlanner(OriginalJassFusionCatalog original,
            OriginalUnitRawcodeLabelCatalog labels, Func<long, bool> storyDeployed,
            IOriginalJassSpecialConditionEvaluator specialRules = null,
            CharacterUnitCatalog verifiedUnits = null)
        {
            this.original = original ?? throw new ArgumentNullException(nameof(original));
            this.labels = labels ?? throw new ArgumentNullException(nameof(labels));
            this.deployed = storyDeployed ?? throw new ArgumentNullException(nameof(storyDeployed));
            this.specialRules = specialRules;
            this.catalog = verifiedUnits;
        }

        public void SetSpecialRules(IOriginalJassSpecialConditionEvaluator rules)
        {
            specialRules = rules;
        }

        // Requires separately verified original object IDs; Bi8/Bi9 name links are
        // insufficient evidence for an irreversible transaction.
        public void SetVerifiedObjectMap(IVerifiedUnitRawcodeMap verified)
        {
            verifiedObjectMap = verified;
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
            var bindings = new List<JassFusionMaterialBinding>();
            var selected = new List<long>();
            var selectedOnce = new HashSet<long>();
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
            string resultId, resultName, resultReason;
            if (recipe.Results.Count != 1 || recipe.Results[0].Kind != "UNIT" ||
                recipe.Results[0].Count != 1)
                reasons.Add("원본 결과 수량 또는 결과 타입 미지원: BGH");
            if (!ResolveOriginalUnit(resultCode, out resultId,
                out resultName, out resultReason))
                reasons.Add("결과 " + resultReason);

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
                    string reason = null;
                    // UPUN appears as a special presentation/operation tag in CHV/CHX.
                    // Even when a handler approves it, original post-commit behavior
                    // must still be implemented by an authoritative transaction service.
                    if (specialRules == null ||
                        !specialRules.CanSatisfy(condition, recipe.AbilityRawcode, out reason))
                    {
                        reasons.Add("원본 추가 조건 " + condition.Kind + "/" +
                            condition.Rawcode + ": " +
                            (string.IsNullOrWhiteSpace(reason)
                                ? "검증 구현체 미연결" : reason));
                    }
                }
            }

            foreach (string rawcode in neededOrder)
            {
                string ingredientId, ingredientName, ingredientReason;
                bool mapped = ResolveOriginalUnit(rawcode, out ingredientId,
                    out ingredientName, out ingredientReason);
                int required = needed[rawcode];
                int owned = 0;
                List<long> list = null;
                if (!mapped)
                    reasons.Add("재료 " + ingredientReason);
                var boundIds = new List<long>();
                if (mapped && allUnits.TryGetValue(ingredientId, out list))
                {
                    owned = Math.Min(list.Count, required);
                    for (int i = 0; i < owned; i++)
                    {
                        // A malformed or inconsistent mapping can associate
                        // distinct original rawcodes with one catalog ID.
                        // Never plan to consume one live instance twice.
                        if (!selectedOnce.Add(list[i]))
                        {
                            reasons.Add("동일 유닛이 서로 다른 원본 재료로 중복 선택됨: " +
                                list[i]);
                            continue;
                        }
                        selected.Add(list[i]);
                        boundIds.Add(list[i]);
                    }
                    owned = boundIds.Count;
                }
                bindings.Add(new JassFusionMaterialBinding(rawcode, boundIds));
                materials.Add(new JassFusionMaterial(rawcode, ingredientId,
                    ingredientName, required, owned));
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

            // The commit service is a separate authorization gate; even a fully
            // resolved plan must never mutate the game world without that service.
            return new JassFusionReadiness(recipe, resultCode, resultId,
                resultName, materials, bindings, reasons, selected, gold, lumber);
        }

        // Original JASS labels are useful for display, not for spending.
        // A manually reviewed object map can independently authorize previously
        // unlabelled lower grades, provided their catalog ID is also known.
        private bool ResolveOriginalUnit(string rawcode, out string unitId,
            out string unitName, out string rejectionReason)
        {
            var label = labels.FindRawcode(rawcode);
            unitId = label == null ? null : label.CatalogUnitId;
            unitName = label == null ? null : label.UnitDisplayName;
            rejectionReason = null;

            string verifiedId;
            if (verifiedObjectMap == null ||
                !verifiedObjectMap.TryResolveUnit(rawcode, out verifiedId) ||
                string.IsNullOrWhiteSpace(verifiedId))
            {
                rejectionReason = label == null ?
                    "유닛 원시 ID 미매핑: " + rawcode :
                    "원본 객체 ID의 최종 검증 미완료: " + rawcode;
                return false;
            }

            if (label != null && verifiedId != label.CatalogUnitId)
            {
                rejectionReason = "객체 검수 결과와 JASS 이름이 충돌: " + rawcode;
                return false;
            }

            if (catalog == null)
            {
                if (label == null)
                {
                    rejectionReason = "원본 매핑의 카탈로그 대조 서비스 미연결: " +
                        rawcode;
                    return false;
                }
            }
            else
            {
                var unit = catalog.Find(verifiedId);
                if (unit == null)
                {
                    rejectionReason = "객체 매핑 대상 캐릭터 카탈로그에 없음: " +
                        rawcode;
                    return false;
                }
                unitName = unit.Name;
            }

            unitId = verifiedId;
            return true;
        }

        public IReadOnlyList<JassFusionReadiness> AssessNamedResults(
            IReadOnlyList<MobileUnitSnapshot> ownedUnits,
            int? verifiedGold, int? verifiedLumber)
        {
            var result = new List<JassFusionReadiness>();
            foreach (var entry in original.All)
            {
                if (entry.Results.Count == 0) continue;
                // Previously unnamed lower-grade outputs become available only
                // when their original object ID is independently approved.
                string rawcode = entry.Results[0].Rawcode;
                string resolved;
                if (labels.FindRawcode(rawcode) == null &&
                    (verifiedObjectMap == null ||
                     !verifiedObjectMap.TryResolveUnit(rawcode, out resolved) ||
                     catalog == null || catalog.Find(resolved) == null))
                    continue;
                result.Add(Assess(entry.AbilityRawcode, ownedUnits, verifiedGold, verifiedLumber));
            }
            result.Sort((a, b) => string.CompareOrdinal(a.ResultName, b.ResultName));
            return result.AsReadOnly();
        }
    }
}
