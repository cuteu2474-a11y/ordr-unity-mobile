using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // Executable scope reviewed against CWR/CH5/CHu/CHx/CHy/CT1/CT2.
    // These 13 Uncommon and 31 Special non-hero results use UNIT costs and UPUN
    // presentation markers. CH5/CHi register no UPUN check/payment callback.
    // Two AOsf-based Special recipes remain pending a separate native-cast audit.
    // No upper-grade bans, research, mana, transforms or assumed prices.
    public sealed class Original323CommonFusionPort : IOriginalFusionPort,
        IOriginalFusionCommitCapability, IOriginalFusionRecipeCommitCapability,
        IVerifiedUnitRawcodeMap, IOriginalJassSpecialConditionEvaluator
    {
        private static readonly string[] CommonRawcodes = {
            "h003", "h002", "h001", "h007", "h004", "h008", "h005", "h009", "h006"
        };
        // ability, output rawcode, project ID. Explicit source identities;
        // the preview/name reconciliation is never execution authorization.
        private static readonly string[,] Reviewed = {
            { "A006", "h00O", "U-01" }, { "A00E", "h00C", "U-02" },
            { "A00W", "h00D", "U-03" }, { "A0N5", "h00M", "U-04" },
            { "A00D", "h00K", "U-05" }, { "A00P", "h00I", "U-06" },
            { "A00V", "h00N", "U-07" }, { "A02I", "h00G", "U-08" },
            { "A01R", "h00E", "U-09" }, { "A00L", "h00J", "U-10" },
            { "A01H", "h00L", "U-11" }, { "A01L", "h00F", "U-12" },
            { "A0N7", "h00A", "U-13" }
        };
        private static readonly string[,] ReviewedSpecial = {
            { "A012", "h00U", "S-07" },
            { "A015", "h00P", "S-03" },
            { "A032", "h00W", "S-22" },
            { "A018", "h015", "S-11" },
            { "A01B", "h013", "S-13" },
            { "A024", "h00T", "S-08" },
            { "A01A", "h019", "S-24" },
            { "A0NA", "h01C", "S-02" },
            { "A01O", "h00R", "S-06" },
            { "A03E", "h00S", "S-04" },
            { "A01X", "h01H", "S-05" },
            { "A017", "h017", "S-28" },
            { "A00K", "h00B", "S-09" },
            { "A019", "h01I", "S-33" },
            { "A00Z", "h00X", "S-21" },
            { "A01I", "h014", "S-12" },
            { "A01D", "h00Q", "S-18" },
            { "A01K", "h00Z", "S-20" },
            { "A02B", "h01K", "S-01" },
            { "A01C", "h01B", "S-31" },
            { "A00B", "h01D", "S-25" },
            { "A014", "h01E", "S-26" },
            { "A030", "h00Y", "S-30" },
            { "A01N", "h016", "S-10" },
            { "A00S", "h01J", "S-19" },
            { "A01E", "h018", "S-23" },
            { "A02Y", "h01G", "S-32" },
            { "A01Q", "h01A", "S-29" },
            { "A0N8", "h00V", "S-27" },
            { "A01P", "h011", "S-16" },
            { "A029", "h010", "S-17" },
        };
        private readonly CharacterCombatRuntime combat;
        private readonly SharedEconomySession economy;
        private readonly OriginalMapBaseStatCatalog objects;
        private readonly OriginalJassFusionPlanner planner;
        private readonly IOriginalFusionPort fallback;
        private readonly Dictionary<string, string> mappings = new Dictionary<string, string>();
        private readonly HashSet<string> supported = new HashSet<string>();
        private readonly Dictionary<string, string> resultGrades = new Dictionary<string, string>();
        private bool busy;
        public event Action<long, string, string> FusionCommitted;
        public event Action<Exception> ObserverFailed;
        public bool CanCommit => true;
        public int ExecutableRecipeCount => supported.Count;
        public int ExecutableCommonRecipeCount => Reviewed.GetLength(0);
        public int ExecutableSpecialRecipeCount => ReviewedSpecial.GetLength(0);

        public Original323CommonFusionPort(CharacterCombatRuntime world, SharedEconomySession shared,
            CharacterUnitCatalog units, OriginalMapBaseStatCatalog originalObjects,
            OriginalJassFusionCatalog registrations, OriginalUnitRawcodeLabelCatalog labels,
            IOriginalFusionPort readOnlyFallback)
        {
            combat = world ?? throw new ArgumentNullException(nameof(world));
            economy = shared ?? throw new ArgumentNullException(nameof(shared));
            objects = originalObjects ?? throw new ArgumentNullException(nameof(originalObjects));
            fallback = readOnlyFallback ?? throw new ArgumentNullException(nameof(readOnlyFallback));
            if (!ReferenceEquals(world.Roster, shared.Units) ||
                objects.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256 || registrations == null ||
                registrations.SourceSha256 != OriginalVerifiedUnitObjectMap.ExpectedJassSha256)
                throw new ArgumentException("원본 조합과 실제 보유 세션이 일치하지 않습니다.");
            for (int i = 0; i < CommonRawcodes.Length; i++)
                Bind(units, "C-" + (i + 1).ToString("D2"), CommonRawcodes[i], "흔함");
            Bind(units, "U-14", "h00H", "안흔함"); // original zombie material, not a free acquisition
            RegisterReviewed(units, registrations, Reviewed, "안흔함");
            RegisterReviewed(units, registrations, ReviewedSpecial, "특별함");
            planner = new OriginalJassFusionPlanner(registrations, labels,
                id => combat.IsStoryDeployed(id), this, units);
            planner.SetVerifiedObjectMap(this);
        }

        private void RegisterReviewed(CharacterUnitCatalog units, OriginalJassFusionCatalog registrations,
            string[,] reviewed, string resultGrade)
        {
            for (int i = 0; i < reviewed.GetLength(0); i++)
                Bind(units, reviewed[i, 2], reviewed[i, 1], resultGrade);
            for (int i = 0; i < reviewed.GetLength(0); i++)
            {
                string ability = reviewed[i, 0], rawcode = reviewed[i, 1];
                var recipe = registrations.FindByAbility(ability);
                if (recipe == null || recipe.Results.Count != 1 ||
                    recipe.Results[0].Kind != "UNIT" || recipe.Results[0].Count != 1 ||
                    recipe.Results[0].Rawcode != rawcode)
                    throw new FormatException("검수한 하위 등급 조합 결과가 변경됐습니다: " + ability);
                bool hasMaterials = false, hasCaster = false;
                foreach (var instruction in recipe.OrderedConditions)
                {
                    if (instruction.Kind == "UPUN") continue;
                    string id;
                    if (instruction.Kind != "UNIT" || !mappings.TryGetValue(instruction.Rawcode, out id))
                        throw new FormatException("하위 조합의 원본 조건 범위가 변경됐습니다.");
                    string grade = units.Require(id).Grade;
                    if (grade != "흔함" && !(resultGrade == "특별함" && grade == "안흔함"))
                        throw new FormatException("검수하지 않은 등급의 조합 재료입니다.");
                    hasMaterials = true;
                    foreach (var nativeAbility in objects.Find(id).OriginalAbilityRawcodes)
                        if (nativeAbility == ability) hasCaster = true;
                }
                if (!hasMaterials || !hasCaster) throw new FormatException("원본 조합 시전자 누락");
                supported.Add(ability);
                resultGrades.Add(ability, resultGrade);
            }
        }

        private void Bind(CharacterUnitCatalog units, string id, string rawcode, string grade)
        {
            var row = objects.Find(id);
            if (units.Require(id).Grade != grade || row == null || row.OriginalRawcode != rawcode)
                throw new FormatException("검수한 하위 등급 원본 객체가 다릅니다: " + id);
            mappings.Add(rawcode, id);
        }
        public bool TryResolveUnit(string rawcode, out string id)
        { return mappings.TryGetValue(rawcode ?? "", out id); }
        public bool CanSatisfy(OriginalJassFusionInstruction instruction, string ability, out string reason)
        {
            bool allowed = instruction != null && instruction.Kind == "UPUN" && supported.Contains(ability);
            reason = allowed ? null : "이 하위 조합에서 검증되지 않은 추가 조건입니다.";
            return allowed;
        }
        public bool CanCommitRecipe(string id) => id != null && supported.Contains(id);

        public IReadOnlyList<MobileFusionPreview> AvailableRecipes(IReadOnlyList<MobileUnitSnapshot> owned)
        {
            var result = new List<MobileFusionPreview>();
            foreach (var offer in fallback.AvailableRecipes(owned))
            {
                if (!CanCommitRecipe(offer.RecipeId)) { result.Add(offer); continue; }
                var ready = planner.Assess(offer.RecipeId, owned, 0, 0);
                var missing = new List<string>(ready.BlockingReasons);
                if (!economy.HasStarted || combat.Game.Phase != SessionPhase.Running)
                    missing.Add("먼저 유닛 준비 또는 게임을 시작해 주세요.");
                var parts = new List<string>();
                foreach (var material in ready.Materials)
                    parts.Add(material.Name + " ×" + material.RequiredCount +
                        " (보유 " + material.OwnedCount + ")");
                result.Add(new MobileFusionPreview(offer.RecipeId, ready.ResultName + " · " + resultGrades[offer.RecipeId],
                    string.Join(" + ", parts) + " · 추가 재화 없음",
                    ready.SuggestedMaterialIds, missing));
            }
            return result.AsReadOnly();
        }

        public ManualActionResult ConfirmFusion(string ability, IReadOnlyList<long> selected)
        {
            if (!CanCommitRecipe(ability)) return fallback.ConfirmFusion(ability, selected);
            if (busy || !economy.HasStarted || combat.Game.Phase != SessionPhase.Running)
                return Reject("현재 조합할 수 없습니다.");
            busy = true;
            long created;
            JassFusionReadiness ready;
            try
            {
                var owned = combat.MobileUnitSnapshot();
                ready = planner.Assess(ability, owned, 0, 0);
                if (!ready.IsReadyForUserConfirmation || selected == null ||
                    selected.Count != ready.SuggestedMaterialIds.Count)
                    return Reject("재료가 변경됐습니다. 조합 목록을 새로고침해 주세요.");
                var exact = new HashSet<long>(selected);
                if (exact.Count != selected.Count) return Reject("같은 재료를 중복 사용할 수 없습니다.");
                foreach (long id in ready.SuggestedMaterialIds)
                    if (!exact.Remove(id)) return Reject("확인한 재료와 현재 재료가 다릅니다.");
                // CT1 casts from an actual material actor that owns this ability,
                // and BI1 preserves that caster's exact current coordinates.
                MobileUnitSnapshot caster = null;
                var materials = new HashSet<long>(ready.SuggestedMaterialIds);
                foreach (var row in owned)
                {
                    if (!materials.Contains(row.InstanceId)) continue;
                    foreach (string native in objects.Find(row.Unit.Id).OriginalAbilityRawcodes)
                        if (native == ability) { caster = row; break; }
                    if (caster != null) break;
                }
                if (caster == null) return Reject("원본 조합 능력을 가진 재료가 없습니다.");
                using (var swap = combat.BeginOriginalFusionUnitSwap(ready.SuggestedMaterialIds,
                    ready.ResultUnitId, caster.Position.X, caster.Position.Y))
                {
                    created = swap.ResultInstanceId;
                    swap.Commit();
                }
            }
            catch (Exception error) { return Reject("조합을 취소했습니다: " + error.Message); }
            finally { busy = false; }
            // Notification errors cannot undo a completed unit transaction.
            if (FusionCommitted != null)
                foreach (Action<long, string, string> observer in FusionCommitted.GetInvocationList())
                    try { observer(created, ready.ResultUnitId, ready.ResultRawcode); }
                    catch (Exception error) { try { ObserverFailed?.Invoke(error); } catch { } }
            return new ManualActionResult(ManualActionState.Succeeded, ready.ResultName + " 조합 완료");
        }
        private static ManualActionResult Reject(string text)
        { return new ManualActionResult(ManualActionState.Rejected, text); }
    }
}
