using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // Original W3X unit object names and catalog correspondences are useful
    // for READING the 265 CWR recipes. The reconciliation source explicitly
    // prohibits spending, and this class cannot grant that authorization.
    public sealed class OriginalFusionObjectPreviewCatalog
    {
        public sealed class Entry
        {
            public string Rawcode { get; private set; }
            public string CatalogUnitId { get; private set; }
            public string DisplayName { get; private set; }

            internal Entry(string rawcode, string unitId, string displayName)
            {
                Rawcode = rawcode;
                CatalogUnitId = unitId;
                DisplayName = displayName;
            }
        }

        private readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        public int Count { get { return entries.Count; } }

        public Entry Find(string rawcode)
        {
            Entry found;
            return rawcode != null && entries.TryGetValue(rawcode, out found)
                ? found : null;
        }

        public static OriginalFusionObjectPreviewCatalog FromJson(string json,
            OriginalJassFusionCatalog original, CharacterUnitCatalog units)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (units == null) throw new ArgumentNullException(nameof(units));
            var root = OrdrJson.Object(OrdrJson.Parse(json),
                "original object reconciliation");
            if (OrdrJson.RequiredString(root, "format") !=
                    "ordr-original-fusion-object-reconciliation-v1" ||
                OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "scope") !=
                    "source-identity-and-web-multiset-cross-check-only" ||
                !String.Equals(OrdrJson.RequiredString(root, "sourceW3xSha256"),
                    original.SourceSha256, StringComparison.OrdinalIgnoreCase) ||
                original.SourceSha256 != OriginalVerifiedUnitObjectMap.ExpectedJassSha256)
                throw new FormatException("조합 등록식과 객체 읽기 전용 자료의 원본 지문이 다릅니다.");

            object allow = OrdrJson.Value(root, "authorizedForFusionExecution");
            if (!(allow is bool) || (bool)allow)
                throw new FormatException("조합 객체 후보는 실행 승인 자료가 아닙니다.");
            var result = new OriginalFusionObjectPreviewCatalog();
            foreach (object raw in OrdrJson.Array(OrdrJson.Value(root, "entries"),
                "object reconciliation entries"))
            {
                var item = OrdrJson.Object(raw, "object candidate");
                string rawcode = OrdrJson.RequiredString(item, "rawcode");
                string originalName = OrdrJson.RequiredString(
                    item, "originalW3xObjectName");
                string unitId = OrdrJson.String(item, "catalogUnitId");
                object spending = OrdrJson.Value(item,
                    "approvedForMaterialConsumption");
                if (rawcode.Length != 4 ||
                    !(spending is bool) || (bool)spending ||
                    result.entries.ContainsKey(rawcode))
                    throw new FormatException("원본 객체 읽기 전용 행이 유효하지 않습니다.");

                string display = originalName;
                if (unitId != null)
                    display = units.Require(unitId).Name + " · " +
                        units.Require(unitId).Grade;
                result.entries.Add(rawcode,
                    new Entry(rawcode, unitId, display));
            }
            return result;
        }
    }

    // Populates the mobile player catalog from the real 265 source orders,
    // NEVER from invented Warcraft recipe rules. Readiness is only whether
    // known UNIT materials are owned, not whether crafting is authorized.
    public sealed class OriginalJassReadOnlyFusionPort :
        IOriginalFusionPort, IOriginalFusionCommitCapability
    {
        private readonly OriginalJassFusionCatalog original;
        private readonly OriginalFusionObjectPreviewCatalog names;
        private readonly Func<long, bool> storyDeployed;
        private IVerifiedUnitRawcodeMap liveObjects;
        private CharacterUnitCatalog liveUnits;
        private IOriginalJassFusionMaterialGroups liveGroups;
        private IOriginalJassPickMaterialPreview pickMaterialPreview;
        public bool CanCommit { get { return false; } }

        public OriginalJassReadOnlyFusionPort(OriginalJassFusionCatalog registrations,
            OriginalFusionObjectPreviewCatalog objectNames,
            Func<long, bool> deployed)
        {
            original = registrations ?? throw new ArgumentNullException(nameof(registrations));
            names = objectNames ?? throw new ArgumentNullException(nameof(objectNames));
            storyDeployed = deployed ?? throw new ArgumentNullException(nameof(deployed));
        }

        // Actual owned source objects can be shown even when their acquisition
        // route is unfinished. This is a preview; CanCommit remains false.
        internal void ConfigureOwnedMaterialPreview(IVerifiedUnitRawcodeMap objects,
            CharacterUnitCatalog units, IOriginalJassFusionMaterialGroups groups,
            IOriginalJassPickMaterialPreview sourcePickPreview = null)
        {
            liveObjects = objects ?? throw new ArgumentNullException(nameof(objects));
            liveUnits = units ?? throw new ArgumentNullException(nameof(units));
            liveGroups = groups ?? throw new ArgumentNullException(nameof(groups));
            pickMaterialPreview = sourcePickPreview;
        }

        public IReadOnlyList<MobileFusionPreview> AvailableRecipes(
            IReadOnlyList<MobileUnitSnapshot> owned)
        {
            if (owned == null) throw new ArgumentNullException(nameof(owned));
            var byUnit = new Dictionary<string, List<long>>(StringComparer.Ordinal);
            var seenInventory = new HashSet<long>();
            foreach (var unit in owned)
            {
                if (unit == null || unit.InstanceId <= 0 ||
                    !seenInventory.Add(unit.InstanceId))
                    throw new ArgumentException("보유 유닛 정보가 중복되거나 유효하지 않습니다.");
                if (storyDeployed(unit.InstanceId)) continue;
                List<long> ids;
                if (!byUnit.TryGetValue(unit.Unit.Id, out ids))
                {
                    ids = new List<long>();
                    byUnit.Add(unit.Unit.Id, ids);
                }
                ids.Add(unit.InstanceId);
            }
            foreach (var ids in byUnit.Values) ids.Sort();

            var result = new List<MobileFusionPreview>();
            foreach (var recipe in original.All)
            {
                var chosen = new List<long>();
                var used = new HashSet<long>();
                var missing = new List<string>();
                var descriptions = new List<string>();
                var requirements = new Dictionary<string, int>(StringComparer.Ordinal);
                var order = new List<string>();
                int gold = 0, wood = 0, specials = 0;
                bool allUnitsOwned = true;
                var conditionDescriptions = new List<string>();
                foreach (var instruction in recipe.OrderedConditions)
                {
                    if (instruction.Kind == "UNIT")
                    {
                        int old;
                        if (!requirements.TryGetValue(instruction.Rawcode, out old))
                        {
                            old = 0;
                            order.Add(instruction.Rawcode);
                        }
                        requirements[instruction.Rawcode] =
                            checked(old + instruction.Count);
                    }
                    else if (instruction.Kind == "GOLD" &&
                        instruction.Rawcode == "GOLD")
                        gold = checked(gold + instruction.Count);
                    else if (instruction.Kind == "WOOD" &&
                        instruction.Rawcode == "WOOD")
                        wood = checked(wood + instruction.Count);
                    else
                    {
                        specials++;
                        string label = ExplainSpecialCondition(instruction);
                        if (!conditionDescriptions.Contains(label))
                            conditionDescriptions.Add(label);
                    }
                }

                foreach (string rawcode in order)
                {
                    var item = names.Find(rawcode);
                    int required = requirements[rawcode];
                    string title = item == null
                        ? "미확인 원본 객체" : item.DisplayName;
                    string liveId = null;
                    IReadOnlyList<string> group = null;
                    var available = new List<long>();
                    bool mapped = false;
                    if (liveGroups != null && liveGroups.TryGetMaterialGroup(rawcode, out group))
                    {
                        var groupNames = new List<string>();
                        foreach (string member in group)
                        {
                            groupNames.Add(liveUnits.Require(member).Name);
                            List<long> ids;
                            if (byUnit.TryGetValue(member, out ids)) available.AddRange(ids);
                        }
                        title = string.Join(" 또는 ", groupNames);
                        mapped = true;
                    }
                    else
                    {
                        if (liveObjects != null) liveObjects.TryResolveUnit(rawcode, out liveId);
                        if (liveId == null) liveId = item?.CatalogUnitId;
                        if (liveId != null)
                        {
                            mapped = true;
                            if (liveUnits != null) title = liveUnits.Require(liveId).Name + " · " + liveUnits.Require(liveId).Grade;
                            List<long> ids;
                            if (byUnit.TryGetValue(liveId, out ids)) available.AddRange(ids);
                        }
                    }
                    descriptions.Add(title + " x" + required);
                    if (!mapped)
                    {
                        allUnitsOwned = false;
                        missing.Add(title + " [" + rawcode +
                            "] (원본 객체는 확인됐지만 획득·보유 구현 미검증) x" +
                            required);
                        continue;
                    }
                    available.Sort();
                    int assigned = 0;
                    if (available != null)
                        foreach (long id in available)
                        {
                            if (assigned >= required) break;
                            if (!used.Add(id)) continue;
                            chosen.Add(id);
                            assigned++;
                        }
                    if (assigned < required)
                    {
                        allUnitsOwned = false;
                        missing.Add(title + " " + (required - assigned) + "기 부족");
                    }
                }

                // A800 is present on the 14 source random-only objects. Show
                // possible additional PICK actors only AFTER assigning all fixed
                // UNIT materials; this preview never grants JASS consume rights.
                foreach (var instruction in recipe.OrderedConditions)
                {
                    if (instruction.Kind != "PICK" || pickMaterialPreview == null)
                        continue;
                    IReadOnlyList<long> candidates;
                    string pickTitle;
                    if (!pickMaterialPreview.TryPreview(instruction, owned,
                            storyDeployed, used, out candidates, out pickTitle))
                    {
                        allUnitsOwned = false;
                        missing.Add("PICK:A800 별도 랜덤 재료의 실물 보유를 확인할 수 없습니다.");
                        continue;
                    }
                    descriptions.Add(pickTitle + " x" + instruction.Count +
                        " (검토용·실제 소비 미승인)");
                    int assigned = 0;
                    foreach (long id in candidates)
                    {
                        if (!used.Add(id)) continue;
                        chosen.Add(id);
                        assigned++;
                    }
                    if (assigned < instruction.Count)
                    {
                        allUnitsOwned = false;
                        missing.Add("PICK용 별도 랜덤 유닛 " +
                            (instruction.Count - assigned) + "기 부족");
                    }
                    missing.Add("PICK:A800 원본 선택·소비 콜백 미검증 (제작 차단)");
                }

                // Unapproved recipes must never look ready just because all
                // listed units happen to be owned.
                string sourceRoute = ExplainNonCraftSourceRoute(recipe.AbilityRawcode);
                missing.Add(sourceRoute == null
                    ? "원본 제작 거래 미연결: 추가 조건/생성 효과 검수 필요"
                    : "일반 조합 버튼 대상 아님: " + sourceRoute);
                var resultObject = recipe.Results.Count == 1 &&
                    recipe.Results[0].Kind == "UNIT" &&
                    recipe.Results[0].Count == 1
                        ? names.Find(recipe.Results[0].Rawcode)
                        : null;
                string titleResult = resultObject == null
                    ? "원본 결과 객체 미확인"
                    : resultObject.DisplayName;
                if (resultObject == null || resultObject.CatalogUnitId == null)
                    missing.Add("원본 결과 객체 " + recipe.Results[0].Rawcode +
                        "의 카탈로그/생성 연결 미검증");
                string summary = (descriptions.Count == 0
                    ? "원본 UNIT 재료 없음" : string.Join(" + ", descriptions.ToArray())) +
                    " | 등록 GOLD " + gold + " / WOOD " + wood +
                    " | 특수 조건 " + specials + "개" +
                    (conditionDescriptions.Count > 0
                        ? " (" + string.Join(" · ", conditionDescriptions.ToArray()) + ")" : "") +
                    (sourceRoute == null ? " | 원본 제작 거래 미연결" :
                        " | 원본 별도 획득 경로: " + sourceRoute);
                result.Add(new MobileFusionPreview(recipe.AbilityRawcode,
                    titleResult + (sourceRoute == null
                        ? " (원본 등록식 · 미승인)" : " (원본 획득 분기 · 조합 아님)"),
                    summary, chosen, missing, allUnitsOwned));
            }
            result.Sort((a, b) =>
                string.CompareOrdinal(a.Title, b.Title));
            return result.AsReadOnly();
        }

        // CWR describes result presentation and acquisition branches as well
        // as paid material-fusion transactions. Do not sell the source pool
        // entries as zero-material recipes or double-spawn their unit rewards.
        private static string ExplainNonCraftSourceRoute(string ability)
        {
            if (ability == "YY26")
                return "랜덤 위습의 원본 24/10000 해적선 결과 (기존 위습 소환 경로)";
            if (ability == "UU01")
                return "희귀 레일리 선택 표시 전용 (원본 획득 이벤트 별도 확인 필요)";
            if (ability == null || ability.Length != 4) return null;
            int index;
            if (ability.StartsWith("SP", StringComparison.Ordinal) &&
                int.TryParse(ability.Substring(2), out index) && index >= 1 && index <= 8)
                return "희귀 위습 1% 특수 결과 풀 (기존 자동 위습 거래 경로)";
            if (ability.StartsWith("IS", StringComparison.Ordinal) &&
                int.TryParse(ability.Substring(2), out index) && index >= 1 && index <= 14)
                return "RDUN:A800 이세계 랜덤 결과 · CJM 실제 성공 정산 증거가 있을 때만 지급 (기본 도박 실행 미연결)";
            return null;
        }

        // Original CWR kind names are interpreted for information only.
        // Even UBAN/KING/UPUN, whose checks are known for Isekai, do not
        // prove the recipe's CH8/BI2 callback is side-effect-free.
        private static string ExplainSpecialCondition(OriginalJassFusionInstruction condition)
        {
            switch (condition.Kind)
            {
                case "UPUN": return "선택 표시 (소비 없음)";
                case "UBAN": return "밴 조건 (이세계 비활성)";
                case "KING": return "항법·상위 조합 제한";
                case "RMAX": return "원본 라운드 상한 " + condition.Count;
                case "PICK": return "별도 랜덤 재료 (원본 소비 미검증)";
                case "ITEM": return "아이템 " + condition.Rawcode + " ×" + condition.Count;
                case "IFCT": return "불멸 제작 상태·횟수";
                case "CHCT": return "선택/변화 횟수";
                case "RDUN": return "원본 라운드·특수 상태";
                case "SKPT": return "특성 포인트";
                case "GREN": return "그린블러드 상태";
                case "SPEC": return "특수 캐릭터 조건";
                default: return condition.Kind + ":" + condition.Rawcode + " ×" + condition.Count;
            }
        }

        public ManualActionResult ConfirmFusion(string recipeId,
            IReadOnlyList<long> selectedMaterialInstances)
        {
            return ManualActionResult.Unavailable(
                "원본 JASS 제작 조건·원자적 재료 소모와 결과 생성");
        }
    }
}
