using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // Executable scope reviewed against CWR/CH5/CHu/CHx/CHy/CT1/CT2.
    // Reviewed recipes use original UNIT/GOLD/WOOD costs and UPUN
    // presentation markers. CH5/CHi register no UPUN check/payment callback.
    // AOsf-based casts (two Special, one Rare) remain pending a native-cast audit.
    // Legendary entries with additional creation handlers or unmapped materials stay locked.
    // Reviewed Hidden/ET01 chat recipes use the player-zero start position, not a caster.
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
        private static readonly string[,] ReviewedRare = {
            { "A0XP", "h09X", "R-01" },
            { "A02W", "h01Q", "R-02" },
            { "A016", "h025", "R-07" },
            { "A02M", "h01L", "R-04" },
            { "A02R", "h02F", "R-33" },
            { "A00I", "h022", "R-08" },
            { "A01F", "h021", "R-03" },
            { "A028", "h020", "R-28" },
            { "A01M", "h01Z", "R-09" },
            { "A02K", "h01Y", "R-10" },
            { "A00F", "h01X", "R-06" },
            { "A02F", "h01W", "R-12" },
            { "A013", "h01V", "R-13" },
            { "A0IO", "h01U", "R-14" },
            { "A03G", "h01T", "R-15" },
            { "A00A", "h02M", "R-16" },
            { "A00Q", "h01S", "R-17" },
            { "A0N6", "h01R", "R-39" },
            { "A0ND", "h01M", "R-42" },
            { "A02J", "h01O", "R-19" },
            { "A00X", "h05L", "R-20" },
            { "A00R", "h01P", "R-27" },
            { "A0NF", "h02I", "R-21" },
            { "A03B", "h029", "R-22" },
            { "A02V", "h028", "R-23" },
            { "A02L", "h027", "R-24" },
            { "A039", "h026", "R-25" },
            { "A01J", "h02A", "R-31" },
            { "A0NB", "h024", "R-29" },
            { "A01W", "h02C", "R-30" },
            { "A00U", "h02D", "R-37" },
            { "A008", "h02G", "R-32" },
            { "A02A", "h02E", "R-34" },
            { "A02T", "h04H", "R-11" },
            { "A00C", "h023", "R-36" },
            { "A0NC", "h02B", "R-26" },
            { "A02N", "h02H", "R-38" },
            { "A02S", "h02J", "R-40" },
            { "A010", "h02K", "R-35" },
            { "A02Z", "h02L", "R-05" },
            { "A01V", "h05K", "R-41" },
        };
        private static readonly string[,] ReviewedLegendary = {
            { "A037", "h03K", "L-29" },
            { "A01G", "h02N", "L-12" },
            { "A00N", "h02O", "L-24" },
            { "A0XS", "h09Z", "L-03" },
            { "A03C", "h02Q", "L-05" },
            { "A02P", "h02R", "L-09" },
            { "A0NH", "h02S", "L-27" },
            { "A036", "h02U", "L-01" },
            { "A00T", "h02Y", "L-10" },
            { "A03D", "h02X", "L-15" },
            { "A00Y", "h02W", "L-04" },
            { "A00J", "h02Z", "L-13" },
            { "A027", "h030", "L-32" },
            { "A0N9", "h032", "L-36" },
            { "A00G", "h033", "L-06" },
            { "A01U", "h034", "L-16" },
            { "A02C", "h03H", "L-33" },
            { "A00M", "h036", "L-18" },
            { "A0NI", "h037", "L-19" },
            { "A03H", "h02V", "L-20" },
            { "A009", "h038", "L-22" },
            { "A02X", "h039", "L-23" },
            { "A005", "h03S", "L-25" },
            { "A0NG", "h03I", "L-26" },
            { "A0NE", "h03G", "L-28" },
            { "A026", "h03F", "L-30" },
            { "A01T", "h03E", "L-31" },
            { "A108", "h0AH", "L-34" },
            { "A02H", "h03C", "L-08" },
            { "A00H", "h031", "L-37" },
            { "A1CC", "h0CM", "L-39" },
            { "A113", "h08S", "L-14" },
            { "A01S", "h03A", "L-07" },
            // Pinned CH8 calls only the discarded, read-only Bjp skin query
            // for h02T; A02D has the regular AHwe caster/material swap.
            { "A02D", "h02T", "L-11" },
        };
        private static readonly string[,] ReviewedHidden = {
            { "YY02", "h044", "H-05" }, { "YY03", "h03W", "H-10" },
            { "YY05", "h03T", "H-03" }, { "YY06", "h03N", "H-04" },
            { "YY10", "h03O", "H-11" }, { "YY11", "h03M", "H-12" },
            { "YY14", "h03J", "H-14" }, { "YY16", "h03Y", "H-18" },
            { "YY17", "h043", "H-01" }, { "YY22", "h046", "H-20" },
            { "YY23", "h045", "H-21" },
            { "YY18", "h07L", "H-19" }, { "YY25", "h047", "H-22" },
            // Pinned JASS BsB/BsC, Brw/Brx, BqW/BqX, Brz/Br0,
            // Brs/Brt: same BIw -> BI1 -> BI2 chat transaction;
            // their handlers only narrate/play local sound after commit.
            // h060 is the verified source-only pirate ship, not a Common.
            { "YY04", "h03U", "H-02" },
            { "YY07", "h03Q", "H-06" },
            { "YY08", "h03R", "H-07" },
            { "YY09", "h03P", "H-08" },
            { "YY12", "h03L", "H-15" },
            // BrG/BrH is BI2 followed by local sound/text. CH8's h055
            // branch only discards Bjp's read-only skin entitlement query.
            { "YY13", "h055", "H-13" }
        };
        // Source ET01 is a chat-driven BI2 swap; its callback adds only
        // text/audio setup, not an additional gameplay resource or result.
        private static readonly string[,] ReviewedEternal = {
            { "ET01", "h057", "E-06" }
        };
        // Callback audit (pinned JASS): Bvu/B9k/B9K/B_M/B5Q/B4r
        // perform only BI2 plus narration/audio; no extra creation, item,
        // player-state, trait or permanent ability effects. All other
        // KING/UBAN recipes retain their original execution locks.
        private static readonly string[,] ReviewedRestricted = {
            { "A0RP", "h040", "LI-05" },
            { "IM02", "h04B", "I-09" },
            { "IM03", "h04C", "I-01" },
            { "IM10", "h04G", "I-10" },
            { "ET09", "h05B", "E-10" },
            { "ET11", "h059", "E-07" },
            // Pinned TR/ET callbacks: narration/audio or an unused, read-only
            // Bjp skin entitlement check. No helper/item/permanent mutations.
            // TR09 uses the separately source-reviewed CHy/BH0 material bucket.
            { "TR01", "H08V", "TR-02" },
            { "TR10", "H094", "TR-09" },
            { "TR21", "H0BX", "TR-21" },
            { "TR23", "H091", "TR-23" },
            { "TR27", "H08Y", "TR-28" },
            { "TR28", "H08X", "TR-29" },
            { "ET08", "h058", "E-04" },
            { "TR09", "H099", "TR-08" },
            { "A12C", "h0BC", "MY-12" }
        };
        // Original chat wrappers call BI1(GetPlayerStartLocationX/Y).
        // config: SetPlayerStartLocation(Player(0),0); DefineStartLocation(0,-3776.,4480.).
        private const double HiddenResultX = -3776.0;
        private const double HiddenResultY = 4480.0;
        private readonly CharacterCombatRuntime combat;
        private readonly SharedEconomySession economy;
        private readonly OriginalMapBaseStatCatalog objects;
        private readonly OriginalJassFusionPlanner planner;
        private readonly IOriginalFusionPort fallback;
        private readonly OriginalJassKnownConditions verifiedConditions;
        private readonly Original323FusionVerifiedRestrictions sourceRestrictions;
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
        public int ExecutableRareRecipeCount => ReviewedRare.GetLength(0);
        public int ExecutableLegendaryRecipeCount => ReviewedLegendary.GetLength(0);
        public int ExecutableHiddenRecipeCount => ReviewedHidden.GetLength(0);
        public int ExecutableEternalRecipeCount => ReviewedEternal.GetLength(0);
        public int ExecutableRestrictedRecipeCount => ReviewedRestricted.GetLength(0);
        public int SourceBannedUnitCount => sourceRestrictions.BannedUnitCount;

        internal void ConfirmObservedOriginalRound(int round)
        {
            sourceRestrictions.ConfirmObservedOriginalRound(round);
        }

        public Original323CommonFusionPort(CharacterCombatRuntime world, SharedEconomySession shared,
            CharacterUnitCatalog units, OriginalMapBaseStatCatalog originalObjects,
            OriginalJassFusionCatalog registrations, OriginalUnitRawcodeLabelCatalog labels,
            IOriginalFusionPort readOnlyFallback,
            Original323FusionVerifiedRestrictions verifiedSourceRestrictions = null)
        {
            combat = world ?? throw new ArgumentNullException(nameof(world));
            economy = shared ?? throw new ArgumentNullException(nameof(shared));
            objects = originalObjects ?? throw new ArgumentNullException(nameof(originalObjects));
            fallback = readOnlyFallback ?? throw new ArgumentNullException(nameof(readOnlyFallback));
            if (!ReferenceEquals(world.Roster, shared.Units) ||
                objects.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256 || registrations == null ||
                registrations.SourceSha256 != OriginalVerifiedUnitObjectMap.ExpectedJassSha256)
                throw new ArgumentException("원본 조합과 실제 보유 세션이 일치하지 않습니다.");
            if (verifiedSourceRestrictions != null &&
                !ReferenceEquals(verifiedSourceRestrictions.SourceSession, combat.Game))
                throw new ArgumentException("조합 조건의 원본 세션이 다릅니다.");
            sourceRestrictions = verifiedSourceRestrictions ??
                new Original323FusionVerifiedRestrictions(combat.Game);
            verifiedConditions = new OriginalJassKnownConditions(sourceRestrictions);
            // Source-backed object identities are reusable by every fusion tier.
            // This alone grants no ability and does not make a recipe executable.
            foreach (var original in objects.All)
                if (!string.IsNullOrEmpty(original.OriginalRawcode))
                    Bind(units, original.UnitId, original.OriginalRawcode,
                        units.Require(original.UnitId).Grade);
            for (int i = 0; i < CommonRawcodes.Length; i++)
                Bind(units, "C-" + (i + 1).ToString("D2"), CommonRawcodes[i], "흔함");
            Bind(units, "U-14", "h00H", "안흔함"); // original zombie material, not a free acquisition
            RegisterReviewed(units, registrations, Reviewed, "안흔함");
            RegisterReviewed(units, registrations, ReviewedSpecial, "특별함");
            // These source objects can be acquired by CSC's 33-unit Special
            // Wisp pool. Mapping them does not authorize their AOsf fusion.
            Bind(units, "S-14", "h01F", "특별함");
            Bind(units, "S-15", "h012", "특별함");
            RegisterReviewed(units, registrations, ReviewedRare, "희귀함");
            Bind(units, "R-18", "h01N", "희귀함"); // material identity; A1A0 remains locked
            // Unique W3X name+grade identity. UU01 is a presentation tag,
            // NOT an authorized free Rare Rayleigh acquisition.
            Bind(units, "R-43", "h05X", "희귀함");
            // Original CSI 1% Rare-Wisp special pool includes h0BG. This
            // VERIFIED identity is for resolving an actual Wisp result only:
            // it does not grant SP08 fusion execution or approve A0YE
            // on the unrelated 99% rare-Wisp branch.
            // CSI 1% native pool includes these eight original Special
            // identities. Mapping does not authorize SP08 or any new recipe.
            Bind(units, "X-01", "h05H", "특수함");
            Bind(units, "X-02", "h08N", "특수함");
            Bind(units, "X-03", "h05Q", "특수함");
            Bind(units, "X-04", "h05O", "특수함");
            Bind(units, "X-05", "h05P", "특수함");
            Bind(units, "X-06", "h05R", "특수함");
            Bind(units, "X-07", "h09T", "특수함");
            Bind(units, SpecialGradeMobilePolicy.Gaimon, "h0BG", "특수함");
            RegisterReviewed(units, registrations, ReviewedLegendary, "전설");
            RegisterReviewed(units, registrations, ReviewedHidden, "히든");
            // These higher-grade ingredients are already present in the
            // catalog; binding is verified by original object rawcode.
            Bind(units, "LI-09", "h05F", "제한됨");
            Bind(units, "T-02", "h05W", "변화");
            RegisterReviewed(units, registrations, ReviewedEternal, "영원한");
            RegisterRestricted(units, registrations);
            planner = new OriginalJassFusionPlanner(registrations, labels,
                id => combat.IsStoryDeployed(id), this, units);
            planner.SetVerifiedObjectMap(this);
            var materialGroups = new Original323FusionMaterialGroups(objects);
            planner.SetVerifiedMaterialGroups(materialGroups);
            var ownedPreview = fallback as OriginalJassReadOnlyFusionPort;
            ownedPreview?.ConfigureOwnedMaterialPreview(this, units, materialGroups);
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
                bool hasMaterials = false, hasCaster = false, hasWood = false;
                foreach (var instruction in recipe.OrderedConditions)
                {
                    if (instruction.Kind == "UPUN") continue;
                    if (instruction.Kind == "WOOD" &&
                        (resultGrade == "전설" || resultGrade == "영원한") &&
                        instruction.Rawcode == "WOOD" && instruction.Count > 0 && !hasWood)
                    { hasWood = true; continue; }
                    string id;
                    if (instruction.Kind != "UNIT" || !mappings.TryGetValue(instruction.Rawcode, out id))
                        throw new FormatException("하위 조합의 원본 조건 범위가 변경됐습니다.");
                    string grade = units.Require(id).Grade;
                    bool permitted = grade == "흔함" ||
                        ((resultGrade == "특별함" || resultGrade == "희귀함" || resultGrade == "전설" || resultGrade == "히든") && grade == "안흔함") ||
                        ((resultGrade == "희귀함" || resultGrade == "전설" || resultGrade == "히든") && grade == "특별함") ||
                        ((resultGrade == "전설" || resultGrade == "히든") && grade == "희귀함") ||
                        // Source YY04 consumes Legendary Shanks h035 as well
                        // as h060. Permit only that audited identity/recipe.
                        (resultGrade == "히든" && ability == "YY04" &&
                            instruction.Rawcode == "h035" && id == "L-17" && grade == "전설") ||
                        (resultGrade == "히든" &&
                            instruction.Rawcode == Original323SourceCommonWispWorld.PirateShipRawcode &&
                            id == Original323SourceCommonWispWorld.PirateShipCatalogId &&
                            grade == "해적선") ||
                        (resultGrade == "영원한" && (grade == "흔함" || grade == "안흔함" ||
                            grade == "특별함" || grade == "희귀함" || grade == "전설" ||
                            grade == "히든" || grade == "변화" || grade == "제한됨"));
                    if (!permitted)
                        throw new FormatException("검수하지 않은 등급의 조합 재료입니다.");
                    hasMaterials = true;
                    foreach (var nativeAbility in objects.Find(id).OriginalAbilityRawcodes)
                        if (nativeAbility == ability) hasCaster = true;
                }
                bool chatRecipe = resultGrade == "히든" || resultGrade == "영원한";
                bool requiresWood = resultGrade == "전설" || resultGrade == "영원한";
                if (!hasMaterials || (!chatRecipe && !hasCaster) || (requiresWood && !hasWood))
                    throw new FormatException("원본 조합 시전자 또는 검수한 목재 비용 누락");
                supported.Add(ability);
                resultGrades.Add(ability, resultGrade);
            }
        }

        private void Bind(CharacterUnitCatalog units, string id, string rawcode, string grade)
        {
            var row = objects.Find(id);
            if (units.Require(id).Grade != grade || row == null || row.OriginalRawcode != rawcode)
                throw new FormatException("검수한 하위 등급 원본 객체가 다릅니다: " + id);
            string already;
            if (mappings.TryGetValue(rawcode, out already))
            {
                if (already != id) throw new FormatException("원본 객체 ID 충돌: " + rawcode);
            }
            else mappings.Add(rawcode, id);
        }

        private void RegisterRestricted(CharacterUnitCatalog units,
            OriginalJassFusionCatalog registrations)
        {
            for (int i = 0; i < ReviewedRestricted.GetLength(0); i++)
            {
                string ability = ReviewedRestricted[i, 0];
                string result = ReviewedRestricted[i, 1];
                string id = ReviewedRestricted[i, 2];
                string grade = units.Require(id).Grade;
                if (grade != "제한됨" && grade != "불멸의" && grade != "영원한" && grade != "초월함" && grade != "신비함")
                    throw new FormatException("상위 조합 결과 등급 불일치: " + ability);
                Bind(units, id, result, grade);
                var recipe = registrations.FindByAbility(ability);
                if (recipe == null || recipe.Results.Count != 1 ||
                    recipe.Results[0].Kind != "UNIT" || recipe.Results[0].Count != 1 ||
                    recipe.Results[0].Rawcode != result)
                    throw new FormatException("검수된 상위 조합 결과가 바뀌었습니다: " + ability);
                bool materials = false, wood = false, banned = false, king = false, caster = false;
                foreach (var condition in recipe.OrderedConditions)
                {
                    string materialId;
                    switch (condition.Kind)
                    {
                        case "UNIT":
                            if (condition.Count < 1 || !mappings.TryGetValue(condition.Rawcode, out materialId))
                                throw new FormatException("상위 조합의 원본 재료 객체가 없습니다: " + ability);
                            materials = true;
                            foreach (string native in objects.Find(materialId).OriginalAbilityRawcodes)
                                if (native == ability) caster = true;
                            break;
                        case "GOLD":
                            if (condition.Rawcode != "GOLD" || condition.Count < 1)
                                throw new FormatException("상위 조합 골드 원본 조건 불일치: " + ability);
                            break;
                        case "WOOD":
                            if (condition.Rawcode != "WOOD" || condition.Count < 1 || wood)
                                throw new FormatException("상위 조합 목재 원본 조건 불일치: " + ability);
                            wood = true;
                            break;
                        case "UPUN": break; // display/upgrade recommendation only
                        case "UBAN":
                            if (banned || condition.Rawcode != result)
                                throw new FormatException("상위 조합 밴 대상이 다릅니다: " + ability);
                            banned = true;
                            break;
                        case "KING":
                            if (king || condition.Rawcode != result)
                                throw new FormatException("상위 조합 항법 대상이 다릅니다: " + ability);
                            king = true;
                            break;
                        case "RMAX":
                            if (condition.Count < 1)
                                throw new FormatException("상위 조합 라운드 상한이 다릅니다: " + ability);
                            break;
                        default:
                            throw new FormatException("상위 조합의 미검수 조건: " + ability + "/" + condition.Kind);
                    }
                }
                // The seven reviewed TR registrations have NO GOLD/WOOD cost.
                // Other reviewed restricted tiers retain their source WOOD.
                if (!materials || (grade == "초월함" ? wood : !wood) ||
                    (grade == "신비함" ? banned : !banned) || !king ||
                    ((grade == "제한됨" || grade == "신비함") && !caster))
                    throw new FormatException("상위 조합 원본 시전자/필수 조건 누락: " + ability);
                supported.Add(ability);
                resultGrades.Add(ability, grade);
            }
        }
        public bool TryResolveUnit(string rawcode, out string id)
        { return mappings.TryGetValue(rawcode ?? "", out id); }
        public bool CanSatisfy(OriginalJassFusionInstruction instruction, string ability, out string reason)
        {
            if (instruction == null || !supported.Contains(ability))
            {
                reason = "실행 검수를 통과하지 않은 원본 조합입니다.";
                return false;
            }
            return verifiedConditions.CanSatisfy(instruction, ability, out reason);
        }
        public bool CanCommitRecipe(string id) => id != null && supported.Contains(id);

        public IReadOnlyList<MobileFusionPreview> AvailableRecipes(IReadOnlyList<MobileUnitSnapshot> owned)
        {
            var result = new List<MobileFusionPreview>();
            foreach (var offer in fallback.AvailableRecipes(owned))
            {
                if (!CanCommitRecipe(offer.RecipeId)) { result.Add(offer); continue; }
                var ready = planner.Assess(offer.RecipeId, owned, SourceBalance("gold"), SourceBalance("wood"));
                var missing = new List<string>(ready.BlockingReasons);
                if (!economy.HasStarted || combat.Game.Phase != SessionPhase.Running)
                    missing.Add("먼저 유닛 준비 또는 게임을 시작해 주세요.");
                var parts = new List<string>();
                foreach (var material in ready.Materials)
                    parts.Add(material.Name + " ×" + material.RequiredCount +
                        " (보유 " + material.OwnedCount + ")");
                var costs = new List<string>();
                if (ready.GoldCost > 0)
                    costs.Add("골드 " + ready.GoldCost + " (보유 " + economy.Wallet.Balance("gold") + ")");
                if (ready.LumberCost > 0)
                    costs.Add("목재 " + ready.LumberCost + " (보유 " + economy.Wallet.Balance("wood") + ")");
                result.Add(new MobileFusionPreview(offer.RecipeId, ready.ResultName + " · " + resultGrades[offer.RecipeId],
                    string.Join(" + ", parts) + (costs.Count > 0
                        ? " · " + string.Join(" · ", costs) : " · 추가 재화 없음"),
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
                ready = planner.Assess(ability, owned, SourceBalance("gold"), SourceBalance("wood"));
                if (!ready.IsReadyForUserConfirmation || selected == null ||
                    selected.Count != ready.SuggestedMaterialIds.Count)
                    return Reject("재료가 변경됐습니다. 조합 목록을 새로고침해 주세요.");
                var exact = new HashSet<long>(selected);
                if (exact.Count != selected.Count) return Reject("같은 재료를 중복 사용할 수 없습니다.");
                foreach (long id in ready.SuggestedMaterialIds)
                    if (!exact.Remove(id)) return Reject("확인한 재료와 현재 재료가 다릅니다.");
                double resultX = HiddenResultX, resultY = HiddenResultY;
                if (resultGrades[ability] != "히든" && resultGrades[ability] != "영원한" &&
                    resultGrades[ability] != "불멸의" && resultGrades[ability] != "초월함" && resultGrades[ability] != "신비함")
                {
                    // CT1 uses the exact material actor owning the cast ability.
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
                    resultX = caster.Position.X;
                    resultY = caster.Position.Y;
                }
                using (var swap = combat.BeginOriginalFusionUnitSwap(ready.SuggestedMaterialIds,
                    ready.ResultUnitId, resultX, resultY))
                {
                    created = swap.ResultInstanceId;
                    if (ready.GoldCost == 0 && ready.LumberCost == 0) swap.Commit();
                    else
                    {
                        var beforeWallet = economy.Wallet.CaptureState();
                        try
                        {
                            var cost = new ResourceBundle();
                            if (ready.GoldCost > 0) cost.Add("gold", ready.GoldCost);
                            if (ready.LumberCost > 0) cost.Add("wood", ready.LumberCost);
                            if (!economy.Wallet.TryExchange(cost, new ResourceBundle()))
                                throw new InvalidOperationException("조합 비용이 변경됐습니다.");
                            swap.Commit();
                        }
                        catch
                        {
                            economy.Wallet.RestoreState(beforeWallet);
                            throw;
                        }
                    }
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
        private int SourceBalance(string id) => (int)Math.Min(int.MaxValue, economy.Wallet.Balance(id));
        private static ManualActionResult Reject(string text)
        { return new ManualActionResult(ManualActionState.Rejected, text); }
    }
}
