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
        public bool CanCommit { get { return false; } }

        public OriginalJassReadOnlyFusionPort(OriginalJassFusionCatalog registrations,
            OriginalFusionObjectPreviewCatalog objectNames,
            Func<long, bool> deployed)
        {
            original = registrations ?? throw new ArgumentNullException(nameof(registrations));
            names = objectNames ?? throw new ArgumentNullException(nameof(objectNames));
            storyDeployed = deployed ?? throw new ArgumentNullException(nameof(deployed));
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
                        specials++;
                }

                foreach (string rawcode in order)
                {
                    var item = names.Find(rawcode);
                    int required = requirements[rawcode];
                    string title = item == null
                        ? "미확인 원본 객체" : item.DisplayName;
                    descriptions.Add(title + " x" + required);
                    if (item == null || item.CatalogUnitId == null)
                    {
                        missing.Add(title + " [" + rawcode +
                            "] (원본 객체는 확인됐지만 획득·보유 구현 미검증) x" +
                            required);
                        continue;
                    }
                    List<long> available;
                    byUnit.TryGetValue(item.CatalogUnitId, out available);
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
                        missing.Add(title + " " + (required - assigned) + "기 부족");
                }

                // Unapproved recipes must never look ready just because all
                // listed units happen to be owned.
                missing.Add("원본 제작 거래 미연결: 추가 조건/생성 효과 검수 필요");
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
                    " | 특수 조건 " + specials + "개 별도 검증 필요" +
                    " | 원본 제작 거래 미연결";
                result.Add(new MobileFusionPreview(recipe.AbilityRawcode,
                    titleResult + " (원본 등록식 · 미승인)",
                    summary, chosen, missing));
            }
            result.Sort((a, b) =>
                string.CompareOrdinal(a.Title, b.Title));
            return result.AsReadOnly();
        }

        public ManualActionResult ConfirmFusion(string recipeId,
            IReadOnlyList<long> selectedMaterialInstances)
        {
            return ManualActionResult.Unavailable(
                "원본 JASS 제작 조건·원자적 재료 소모와 결과 생성");
        }
    }
}
