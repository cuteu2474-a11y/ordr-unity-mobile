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
    // AOsf-based casts (two Special, one Rare) use an explicitly labelled
    // mobile result-only swap. The native AOsf callback is NOT parity-approved.
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
        // Batch 40: all three AOsf registrations have a source caster
        // (h009, h00G, h00D) and mapped UNIT materials. This mobile
        // atomic result swap does not certify the original native callback.
        private static readonly string[,] MobileOutcomeAOsfSpecial = {
            { "A19Z", "h01F", "S-14" }, // 스모커
            { "A1A1", "h012", "S-15" }  // 스쿼드
        };
        private static readonly string[,] MobileOutcomeAOsfRare = {
            { "A1A0", "h01N", "R-18" } // 브룩 음악가
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
        // Batch 39 mobile result-only recipes: source UNIT/WOOD/UPUN.
        // Additional JASS creation callbacks have not been certified.
        private static readonly string[,] MobileOutcomeLegendary = {
            { "A00O", "h02P", "L-02" }, // 나미
            { "A025", "h035", "L-17" }, // 샹크스
            { "A0UG", "h042", "L-21" }, // 시노부
            { "A02G", "h03B", "L-38" }, // 흰수염
            { "A02Q", "h087", "L-35" }  // 토키 · 9단계 고대의 배 실물 필요
        };
        private static readonly string[,] MobileOutcomeHidden = {
            { "YY01", "h03X", "H-09" }, // 방주맥심
            { "YY15", "h03Z", "H-17" }, // 아카이누
            { "YY20", "h041", "H-16" }  // 아오키지
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
            { "A12C", "h0BC", "MY-12" },
            // JASS CWR has exact physical materials (including LT-01),
            // 10,000 gold, 7 wood and KING. Unlike the six PICK:A800
            // registrations these seven have no unresolved extra condition.
            // Mobile result-mode CHu/CHx uses the existing atomic swap.
            { "A0Y3", "h09W", "MY-04" }, // 미나토
            { "A0Y9", "h0A2", "MY-03" }, // 료우기 시키
            { "A0XT", "h09N", "MY-06" }, // 부릉냐
            { "A1AD", "h08M", "MY-08" }, // 아냐 포저
            { "A0Y6", "h09R", "MY-11" }, // 쿠죠 죠타로
            { "A0YK", "h0BF", "MY-02" }, // 네즈코
            { "A10V", "h083", "MY-07" }, // 샌즈
            // Source CWR PICK:A800 plus published 2.323 any-random material:
            // mobile outcome-matched consumption of an ADDITIONAL random-only
            // instance, never the named caster. Native BGI callback parity is
            // still unverified. A0QG/Yujiro is W3X source-linked through
            // the existing MY-14 overlay, not through the older 277 rows.
            { "A19W", "h09M", "MY-01" }, // 고죠 사토루
            { "A19U", "h0AC", "MY-13" }, // 히그마
            { "A19S", "h0A6", "MY-09" }, // 야쿠모 유카리
            { "A19V", "h0CC", "MY-10" }, // 콘파쿠 요우무
            { "A19T", "h08I", "MY-05" }, // 쿠치키 뱌쿠야
            { "A0QG", "h0C2", "MY-14" }  // 한마 유지로 (W3X source-only result)
        };
        // 38차 모바일 결과 모델: 22개의 초월함 원본 CWR UNIT/UBAN/KING
        // (TR07만 UPUN 표시 포함) 재료를 실제 원자 소비하고 원본 결과 객체를
        // 생성한다. 원본 제작 후 네이티브 CH8/BI2 별도 부가효과는 아직
        // 인증되지 않았다. 이 배열은 완전한 원본 콜백 패리티가 아니다.
        // TR30 requires a source-witnessed h0AN actor; its W3X name alone
        // never grants an arbitrary Seraphim or free fusion material.
        private static readonly string[,] MobileOutcomeTranscend = {
            { "TR02", "H098", "TR-04" }, // 로빈
            { "TR03", "H05N", "TR-27" }, // 타시기
            { "TR04", "H09E", "TR-03" }, // 도플라밍고
            { "TR05", "H08W", "TR-07" }, // 루치
            { "TR06", "H09F", "TR-20" }, // 조로
            { "TR07", "H0B2", "TR-14" }, // 스네이크맨
            { "TR08", "H090", "TR-01" }, // 검은수염
            { "TR11", "H0B5", "TR-26" }, // 키자루
            { "TR12", "H09I", "TR-10" }, // 브룩
            { "TR13", "H092", "TR-11" }, // 사보
            { "TR14", "H09H", "TR-12" }, // 상디
            { "TR15", "H08Z", "TR-13" }, // 샹크스
            { "TR16", "H08U", "TR-15" }, // 시라호시
            { "TR17", "H0BL", "TR-06" }, // 료쿠규
            { "TR18", "H095", "TR-17" }, // 아카이누
            { "TR19", "H0BD", "TR-18" }, // 야마토
            { "TR20", "H09B", "TR-19" }, // 우솝
            { "TR22", "H09A", "TR-22" }, // 징베
            { "TR24", "H097", "TR-16" }, // 아오키지
            { "TR25", "H0B4", "TR-25" }, // 키드
            { "TR26", "H096", "TR-05" }, // 트라팔가 로우
            { "TR29", "H0CO", "TR-24" }, // 코비
            { "TR30", "H0AA", "TR-30" }, // 베가펑크 · h0AN 실제 재료
        };

        // Batch 45: source-declared follow-up receipts join the same wallet
        // transaction as the material swap. Original h04V dummy abilities,
        // CEN aura, and timers still need combat adapters; mobile outcome only.
        // Stage-nine story rewards now supply one non-attacking h05Y ship;
        // both affected recipes consume the actual owned source actor.
        // Batch 41: the SKPT:4 Alvida gate uses real mission/story trait points
        // in the SAME atomic source-material wallet transaction.
        private static readonly string[,] MobileOutcomeUpperTiers = {
            { "A007", "h05I", "LI-02" }, // 레베카, Bvg first-time helper
            { "A18P", "h08Q", "LI-06" }, // 알비다, SKPT 4, gold 5000/wood 5
            // Batch 43: exact ITEM:AI02/AI01 checked against witnessed item
            // receipts in the shared wallet; no synthetic item allocation.
            { "A011", "h08O", "LI-03" }, // 마르코, ITEM AI02
            { "ET02", "H0BK", "E-01" }, // 니카, ITEM AI01, H08T real Luffy form bucket
            { "A0S0", "h07I", "LI-08" }, // 카타쿠리, gold 30000/wood 5
            { "A0UH", "h084", "LI-04" }, // 시노부, gold 5000/wood 5
            { "A1A5", "h05E", "LI-07" }, // 에넬, gold 5000/wood 5
            { "A02O", "h05F", "LI-09" }, // 크로커다일, UPUN marker
            { "A1A6", "h05G", "LI-01" }, // 레드필드, source RMAX h05G/40
            { "A10A", "h0AI", "LI-10" }, // 킹, stage-nine h05Y material
            { "IM01", "h04J", "I-04" }, // 로저, B8Y first-time helper
            { "IM04", "h04D", "I-02" }, // 드래곤, B99 line explosion +20
            { "IM05", "h04Q", "I-06" }, // 빅맘, wood 10
            { "IM06", "h04E", "I-07" }, // 센고쿠
            { "IM07", "h04F", "I-08" }, // 스코퍼가반
            { "IM08", "h049", "I-03" }, // 레일리
            { "IM09", "h04A", "I-12" }, // 흰수염
            { "IM11", "h07M", "I-11" }, // 카이도
            { "IM12", "h081", "I-05" }, // 불릿
            { "ET04", "h0CJ", "E-03" }, // 류마, B7z once-per-player
            { "ET05", "h05A", "E-05" }, // 버기, wood 5
            { "ET06", "h05C", "E-12" }, // 핸콕
            { "ET07", "h067", "E-09" }, // 우타, ITEM AI00 -> AI01
            { "ET12", "h06A", "E-11" }, // 테조로, B8G helper
            { "ET10", "h08R", "E-08" }  // 오뎅
        };
        // Batch 42: source CWR CHCT×1/WOOD10 and IFCT×3 output lists.
        // The mobile quota counts successful crafts in the shared saved wallet.
        // Native counter mutation parity remains a separate source audit.
        private static readonly string[,] MobileOutcomeChanges = {
            { "A0K2", "h05S", "T-01" }, // 도플라밍고
            { "A0K3", "h07N", "T-03" }, // 베이비 5
            { "A0K4", "h05W", "T-02" }, // 비비
            { "A0K6", "h0CK", "T-04" }, // 카쿠
            { "A0K7", "h07J", "T-05" }  // 캐럿
        };
        private static readonly string[,] MobileOutcomeDistorted = {
            { "IF01", "h03V", "D-02" }, // 코알라
            { "IF02", "h048", "D-04" }, // 페로나
            { "IF03", "h0CI", "D-03" }, // 퀸
            { "IF04", "h04U", "D-01" }, // 블랙마리아
            { "IF05", "h09V", "D-06" }, // 바제스
            { "IF06", "h05V", "D-05" }  // 에이스
        };
        // Egghead clear grants a single A13A Green Blood cast in the source.
        // SPH3 also has a CEN post-hook; source callback state is now
        // transacted, but the native buff/dummy remains mobile-only.
        private static readonly string[,] MobileOutcomeGreenBloodSeraphim = {
            { "SPH1", "h09Y", "SR-01" },
            { "SPH2", "h0A3", "SR-02" },
            { "SPH3", "h0A0", "SR-03" },
            { "SPH4", "h0A1", "SR-04" }
        };
        // Five source/dex-correlated results plus the W3X-identified Hanma
        // result use the same mobile atomic swap. For Hanma the extra A800
        // material is a source-structure inference, not published dex evidence;
        // no native callback equivalence is asserted.
        private static readonly Dictionary<string, string> MobileMysticPickInputOrder =
            new Dictionary<string, string>(StringComparer.Ordinal) {
                { "A19W", "h06U:1,h03V:1,h03Z:1,h027:1,h060:1,h06G:1" },
                { "A19U", "h06X:1,h09Z:1,h029:1,h06G:1" },
                { "A19S", "h073:1,h048:1,h017:1,h06G:1" },
                { "A19V", "h09K:1,h03I:1,h05W:1,h06G:1" },
                { "A19T", "h071:1,h05V:1,h04H:1,h06G:1" },
                { "A0QG", "h06T:1,h02Y:1,h03Y:1,h06G:1" }
            };
        // Original chat wrappers call BI1(GetPlayerStartLocationX/Y).
        // config: SetPlayerStartLocation(Player(0),0); DefineStartLocation(0,-3776.,4480.).
        private const double HiddenResultX = -3776.0;
        private const double HiddenResultY = 4480.0;
        private readonly CharacterCombatRuntime combat;
        private readonly CharacterUnitCatalog unitCatalog;
        private readonly SharedEconomySession economy;
        private readonly OriginalMapBaseStatCatalog objects;
        private readonly OriginalJassFusionPlanner planner;
        private readonly OriginalJassFusionCatalog originalRegistrations;
        private readonly IOriginalFusionPort fallback;
        private readonly OriginalJassKnownConditions verifiedConditions;
        private readonly Original323FusionVerifiedRestrictions sourceRestrictions;
        private readonly Original323CwrAcquisitionRoutes acquisitionRoutes;
        private readonly Dictionary<string, string> mappings = new Dictionary<string, string>();
        private readonly HashSet<string> supported = new HashSet<string>();
        private readonly Dictionary<string, string> resultGrades = new Dictionary<string, string>();
        private bool busy;
        public event Action<long, string, string> FusionCommitted;
        public event Action<Exception> ObserverFailed;
        public bool CanCommit => true;
        public int ExecutableRecipeCount => supported.Count;
        public int ClassifiedNonCraftRegistrationCount =>
            acquisitionRoutes.NonCraftRegistrationCount;
        public int ExecutableCommonRecipeCount => Reviewed.GetLength(0);
        public int ExecutableSpecialRecipeCount => ReviewedSpecial.GetLength(0) + MobileOutcomeAOsfSpecial.GetLength(0);
        public int ExecutableRareRecipeCount => ReviewedRare.GetLength(0) + MobileOutcomeAOsfRare.GetLength(0);
        public int ExecutableLegendaryRecipeCount => ReviewedLegendary.GetLength(0) + MobileOutcomeLegendary.GetLength(0);
        public int ExecutableHiddenRecipeCount => ReviewedHidden.GetLength(0) + MobileOutcomeHidden.GetLength(0);
        public int ExecutableEternalRecipeCount => ReviewedEternal.GetLength(0);
        public int ExecutableRestrictedRecipeCount => ReviewedRestricted.GetLength(0) +
            MobileOutcomeTranscend.GetLength(0) + MobileOutcomeUpperTiers.GetLength(0);
        public int ExecutableMobileOutcomeTranscendCount => MobileOutcomeTranscend.GetLength(0);
        public int ExecutableMobileOutcomeUpperTierCount => MobileOutcomeUpperTiers.GetLength(0);
        public int ExecutableMobileSourceItemRecipeCount => 3;
        public bool TryRecordOriginalItemPickup(IOriginal323FusionItemReceiptWitness witness,
            string receiptId, string originalItemRawcode, int count)
        {
            return Original323FusionSourceItemLedger.TryRecordSourcePickup(
                economy.Wallet, witness, receiptId, originalItemRawcode, count);
        }
        public int ExecutableMobileGreenBloodSeraphimCount => MobileOutcomeGreenBloodSeraphim.GetLength(0);
        public int ExecutableMobileOutcomeChangesCount => MobileOutcomeChanges.GetLength(0);
        public int ExecutableMobileOutcomeDistortedCount => MobileOutcomeDistorted.GetLength(0);
        public int ExecutableMobileOutcomeLegendaryCount => MobileOutcomeLegendary.GetLength(0);
        public int ExecutableMobileOutcomeHiddenCount => MobileOutcomeHidden.GetLength(0);
        public int ExecutableMobileOutcomeAOsfCount => MobileOutcomeAOsfSpecial.GetLength(0) + MobileOutcomeAOsfRare.GetLength(0);
        public int ExecutableMysticRecipeCount
        {
            get
            {
                int count = 0;
                foreach (var tier in resultGrades.Values)
                    if (tier == "신비함") count++;
                return count;
            }
        }
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
            unitCatalog = units ?? throw new ArgumentNullException(nameof(units));
            economy = shared ?? throw new ArgumentNullException(nameof(shared));
            objects = originalObjects ?? throw new ArgumentNullException(nameof(originalObjects));
            originalRegistrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
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
            RegisterReviewed(units, registrations, MobileOutcomeAOsfSpecial, "특별함", true);
            // These source objects can also be acquired by CSC's Special
            // Wisp pool. Batch 40 separately authorizes only the mobile
            // source-material->result path, not native AOsf parity.
            Bind(units, "S-14", "h01F", "특별함");
            Bind(units, "S-15", "h012", "특별함");
            RegisterReviewed(units, registrations, ReviewedRare, "희귀함");
            RegisterReviewed(units, registrations, MobileOutcomeAOsfRare, "희귀함", true);
            Bind(units, "R-18", "h01N", "희귀함"); // A1A0 mobile result identity
            // Unique W3X name+grade identity. UU01 is a presentation tag,
            // NOT an authorized free Rare Rayleigh acquisition.
            Bind(units, "R-43", "h05X", "희귀함");
            Bind(units, Original323AncientShipStoryRewardPort.MaterialCatalogId,
                Original323AncientShipStoryRewardPort.MaterialRawcode, "희귀함");
            Bind(units, Original323SourceMaterialRewardWorld.SeraphimMaterialCatalogId,
                Original323SourceMaterialRewardWorld.SeraphimMaterialRawcode, "재료");
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
            RegisterReviewed(units, registrations, MobileOutcomeLegendary, "전설", true);
            RegisterReviewed(units, registrations, ReviewedHidden, "히든");
            RegisterReviewed(units, registrations, MobileOutcomeHidden, "히든", true);
            // These higher-grade ingredients are already present in the
            // catalog; binding is verified by original object rawcode.
            Bind(units, "LI-09", "h05F", "제한됨");
            Bind(units, "T-02", "h05W", "변화");
            RegisterReviewed(units, registrations, ReviewedEternal, "영원한");
            RegisterRestricted(units, registrations, ReviewedRestricted, false);
            RegisterRestricted(units, registrations, MobileOutcomeTranscend, true);
            RegisterRestricted(units, registrations, MobileOutcomeUpperTiers, true);
            RegisterRestricted(units, registrations, MobileOutcomeGreenBloodSeraphim, true);
            RegisterRestricted(units, registrations, MobileOutcomeChanges, true);
            RegisterRestricted(units, registrations, MobileOutcomeDistorted, true);
            // Fail loading if any of the 24 source-only acquisition/guide
            // registrations is accidentally turned into a paid CWR recipe.
            acquisitionRoutes = new Original323CwrAcquisitionRoutes(
                registrations, units, objects);
            planner = new OriginalJassFusionPlanner(registrations, labels,
                id => combat.IsStoryDeployed(id), this, units);
            planner.SetVerifiedObjectMap(this);
            var materialGroups = new Original323FusionMaterialGroups(objects);
            planner.SetVerifiedMaterialGroups(materialGroups);
            // The 14 A800-bearing RN source objects back the separate random
            // material. Six registered Mystic outcomes use the mobile atomic
            // swap; source-native BGI callback parity is not asserted.
            var pickCandidates = new Original323MysticPickCandidatePreview(objects, units);
            planner.SetPickMaterialPreview(pickCandidates);
            var ownedPreview = fallback as OriginalJassReadOnlyFusionPort;
            ownedPreview?.ConfigureOwnedMaterialPreview(this, units,
                materialGroups, pickCandidates);
        }

        private void RegisterReviewed(CharacterUnitCatalog units, OriginalJassFusionCatalog registrations,
            string[,] reviewed, string resultGrade, bool mobileOutcomeOnly = false)
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
                    if (mobileOutcomeOnly && instruction.Kind != "UNIT" &&
                        instruction.Kind != "WOOD" && instruction.Kind != "UPUN")
                        throw new FormatException("모바일 결과 조합의 미검수 조건: " + ability);
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
                        // The 286-entry runtime overlay regrades PS-01
                        // from its original "해적선" display category to
                        // "히든". Authorize only the same verified h060
                        // object in either catalogue view, never arbitrary
                        // additional Hidden-grade materials.
                        (resultGrade == "히든" &&
                            instruction.Rawcode == Original323SourceCommonWispWorld.PirateShipRawcode &&
                            id == Original323SourceCommonWispWorld.PirateShipCatalogId &&
                            (grade == "해적선" || grade == "히든") &&
                            objects.Find(id) != null &&
                            objects.Find(id).OriginalRawcode ==
                                Original323SourceCommonWispWorld.PirateShipRawcode) ||
                        (resultGrade == "영원한" && (grade == "흔함" || grade == "안흔함" ||
                            grade == "특별함" || grade == "희귀함" || grade == "전설" ||
                            grade == "히든" || grade == "변화" || grade == "제한됨"));
                    if (!permitted)
                        throw new FormatException("검수하지 않은 등급의 조합 재료입니다.");
                    hasMaterials = true;
                    var sourceMaterial = objects.Find(id);
                    if (sourceMaterial == null && !IsSourceOnlySupportMaterial(id,
                            instruction.Rawcode))
                        throw new FormatException("하위 조합 재료의 원본 객체가 없습니다.");
                    if (sourceMaterial != null)
                        foreach (var nativeAbility in sourceMaterial.OriginalAbilityRawcodes)
                            if (nativeAbility == ability) hasCaster = true;
                }
                bool chatRecipe = resultGrade == "히든" || resultGrade == "영원한";
                bool requiresWood = resultGrade == "전설" || resultGrade == "영원한";
                if (!hasMaterials || (!chatRecipe && !hasCaster) || (requiresWood && !hasWood))
                    throw new FormatException("원본 조합 시전자 또는 검수한 목재 비용 누락");
                if (!supported.Add(ability))
                    throw new FormatException("조합 실행 중복 등록: " + ability);
                resultGrades.Add(ability, resultGrade);
            }
        }

        private void Bind(CharacterUnitCatalog units, string id, string rawcode, string grade)
        {
            var row = objects.Find(id);
            var unit = units.Require(id);
            // The pinned 277 source-catalogue entries predate W3X-only MY-14.
            // Its wiki overlay is identity evidence, but not an execution
            // permit by itself. Require the exact original result rawcode,
            // source fingerprint and existing W3X attack object before allowing
            // the original CWR registration to pass the normal condition audit.
            bool sourceOnlyYujiro = id == "MY-14" && rawcode == "h0C2" &&
                grade == "신비함" && row == null &&
                unit.CatalogEntryKind == "fusion-result" &&
                unit.OriginalSourceRawcode == rawcode &&
                objects.SourceW3xSha256 == OriginalCommonSaleRules.OriginalMapSha256;
            if (sourceOnlyYujiro)
            {
                var attack = objects.FindMapObjectAttack(rawcode);
                sourceOnlyYujiro = attack != null &&
                    attack.BaseDamagePlus == 152500 &&
                    attack.DiceCount == 1 && attack.DiceSides == 4 &&
                    attack.MinimumBaseDamage == 152501 &&
                    attack.MaximumBaseDamage == 152504 &&
                    attack.AttackCooldownSeconds == 0.71 &&
                    attack.AttackPointSeconds == 0.26;
            }
            bool sourceOnlyAncientShip = IsSourceOnlyAncientShip(id, rawcode) &&
                grade == "희귀함" && row == null &&
                unit.CatalogEntryKind == "support-object" &&
                unit.OriginalSourceRawcode == rawcode &&
                objects.SourceW3xSha256 == OriginalCommonSaleRules.OriginalMapSha256 &&
                objects.HasNoncombatMapObject(rawcode,
                    Original323AncientShipStoryRewardPort.OriginalMapName);
            bool sourceOnlySeraphimMaterial =
                id == Original323SourceMaterialRewardWorld.SeraphimMaterialCatalogId &&
                rawcode == Original323SourceMaterialRewardWorld.SeraphimMaterialRawcode &&
                grade == "재료" && row == null &&
                unit.CatalogEntryKind == "support-object" &&
                unit.OriginalSourceRawcode == rawcode &&
                objects.SourceW3xSha256 == OriginalCommonSaleRules.OriginalMapSha256 &&
                objects.HasNoncombatMapObject(rawcode,
                    Original323SourceMaterialRewardWorld.SeraphimMaterialMapName);
            if (unit.Grade != grade ||
                (!sourceOnlyYujiro && !sourceOnlyAncientShip &&
                 !sourceOnlySeraphimMaterial &&
                    (row == null || row.OriginalRawcode != rawcode)))
                throw new FormatException("검수한 원본 객체가 다릅니다: " + id);
            string already;
            if (mappings.TryGetValue(rawcode, out already))
            {
                if (already != id) throw new FormatException("원본 객체 ID 충돌: " + rawcode);
            }
            else mappings.Add(rawcode, id);
        }

        private static bool IsSourceOnlyAncientShip(string id, string rawcode)
        {
            return id == Original323AncientShipStoryRewardPort.MaterialCatalogId &&
                rawcode == Original323AncientShipStoryRewardPort.MaterialRawcode;
        }

        private static bool IsSourceOnlySupportMaterial(string id, string rawcode)
        {
            return IsSourceOnlyAncientShip(id, rawcode) ||
                (id == Original323SourceMaterialRewardWorld.SeraphimMaterialCatalogId &&
                 rawcode == Original323SourceMaterialRewardWorld.SeraphimMaterialRawcode);
        }

        private void RegisterRestricted(CharacterUnitCatalog units,
            OriginalJassFusionCatalog registrations, string[,] reviewed,
            bool mobileOutcomeOnly)
        {
            for (int i = 0; i < reviewed.GetLength(0); i++)
            {
                string ability = reviewed[i, 0];
                string result = reviewed[i, 1];
                string id = reviewed[i, 2];
                string grade = units.Require(id).Grade;
                if (grade != "제한됨" && grade != "불멸의" && grade != "영원한" && grade != "초월함" && grade != "신비함" && grade != "세라핌" && grade != "변화" && grade != "왜곡됨")
                    throw new FormatException("상위 조합 결과 등급 불일치: " + ability);
                if (mobileOutcomeOnly && grade != "초월함" && grade != "제한됨" &&
                    grade != "불멸의" && grade != "영원한" && grade != "세라핌" &&
                    grade != "변화" && grade != "왜곡됨")
                    throw new FormatException("모바일 결과 조합의 결과 등급 불일치: " + ability);
                Bind(units, id, result, grade);
                var recipe = registrations.FindByAbility(ability);
                if (recipe == null || recipe.Results.Count != 1 ||
                    recipe.Results[0].Kind != "UNIT" || recipe.Results[0].Count != 1 ||
                    recipe.Results[0].Rawcode != result)
                    throw new FormatException("검수된 상위 조합 결과가 바뀌었습니다: " + ability);
                bool materials = false, wood = false, banned = false, king = false, caster = false, greenBlood = false, specialCounter = false, sourceItem = false, luffyGroup = false;
                string seraphimMaterial = null;
                int materialCount = 0;
                int mysticGold = 0, mysticWood = 0, physicalTokens = 0, mysticPicks = 0;
                int outcomeGold = 0, outcomeWood = 0;
                bool mysticOrdered = true;
                int mysticPhase = 0;
                var mysticMaterialOrder = new List<string>();
                foreach (var condition in recipe.OrderedConditions)
                {
                    if (mobileOutcomeOnly && condition.Kind != "UNIT" &&
                        condition.Kind != "UPUN" && condition.Kind != "UBAN" &&
                        condition.Kind != "KING" && condition.Kind != "GOLD" &&
                        condition.Kind != "WOOD" &&
                        !Original323FusionSpecialWalletCosts.IsApproved(ability, condition) &&
                        !Original323FusionCountLedger.IsApproved(ability, condition) &&
                        !Original323FusionSourceItemLedger.IsApproved(ability, condition) &&
                        !(ability == "A1A6" && condition.Kind == "RMAX" &&
                          condition.Rawcode == "h05G" && condition.Count == 40))
                        throw new FormatException("모바일 결과 조합의 미승인 추가 조건: " +
                            ability + "/" + condition.Kind);
                    string materialId;
                    switch (condition.Kind)
                    {
                        case "PICK":
                            // Six source CWR recipes have PICK:A800 x1 before
                            // named UNIT inputs (five dex-matched). A800
                            // exists only on the 14 RN objects in this source.
                            if (grade != "신비함" ||
                                !MobileMysticPickInputOrder.ContainsKey(ability) ||
                                condition.Rawcode != Original323MysticPickCandidatePreview.NativeRandomAbility ||
                                condition.Count != 1 || mysticPicks != 0 ||
                                mysticPhase != 0 || materials)
                                throw new FormatException("신비함 추가 랜덤 재료 조건이 변경됐습니다: " + ability);
                            mysticPicks = 1;
                            break;
                        case "UNIT":
                            if (grade == "신비함")
                            {
                                if (mysticPhase != 0) mysticOrdered = false;
                                mysticMaterialOrder.Add(condition.Rawcode + ":" + condition.Count);
                                if (condition.Rawcode == Original323SourceMaterialRewardWorld.TokenRawcode)
                                    physicalTokens = checked(physicalTokens + condition.Count);
                            }
                            // Source CHy/BH0 group: either of the two genuine
                            // Luffy forms satisfies ET02's one H08T slot.
                            // This is NOT another new actor/rawcode alias.
                            if (ability == "ET02" && condition.Rawcode == "H08T" &&
                                condition.Count == 1 && !luffyGroup)
                            {
                                var realGroup = new Original323FusionMaterialGroups(objects);
                                IReadOnlyList<string> luffys;
                                if (!realGroup.TryGetMaterialGroup("H08T", out luffys) ||
                                    luffys.Count != 2)
                                    throw new FormatException("원본 루피 형태 재료 분류 불일치");
                                luffyGroup = true;
                                materials = true;
                                materialCount = checked(materialCount + 1);
                                break;
                            }
                            if (condition.Count < 1 || !mappings.TryGetValue(condition.Rawcode, out materialId))
                                throw new FormatException("상위 조합의 원본 재료 객체가 없습니다: " + ability);
                            materials = true;
                            materialCount = checked(materialCount + condition.Count);
                            if (grade == "세라핌") seraphimMaterial = condition.Rawcode;
                            var originalMaterial = objects.Find(materialId);
                            if (originalMaterial == null &&
                                !IsSourceOnlySupportMaterial(materialId, condition.Rawcode))
                                throw new FormatException("원본 상위 조합 재료 객체 누락: " + ability);
                            if (originalMaterial != null)
                                foreach (string native in originalMaterial.OriginalAbilityRawcodes)
                                    if (native == ability) caster = true;
                            break;
                        case "GOLD":
                            if (grade == "신비함")
                            {
                                if (mysticPhase != 0) mysticOrdered = false;
                                mysticPhase = 1;
                                mysticGold = checked(mysticGold + condition.Count);
                            }
                            if (condition.Rawcode != "GOLD" || condition.Count < 1)
                                throw new FormatException("상위 조합 골드 원본 조건 불일치: " + ability);
                            outcomeGold = checked(outcomeGold + condition.Count);
                            break;
                        case "WOOD":
                            if (grade == "신비함")
                            {
                                if (mysticPhase != 1) mysticOrdered = false;
                                mysticPhase = 2;
                                mysticWood = checked(mysticWood + condition.Count);
                            }
                            if (condition.Rawcode != "WOOD" || condition.Count < 1 || wood)
                                throw new FormatException("상위 조합 목재 원본 조건 불일치: " + ability);
                            wood = true;
                            outcomeWood = checked(outcomeWood + condition.Count);
                            break;
                        case "SKPT":
                        case "GREN":
                            if (!Original323FusionSpecialWalletCosts.IsApproved(ability, condition))
                                throw new FormatException("원본 특성 포인트 소비 조건 불일치: " + ability);
                            if (condition.Kind == "GREN") greenBlood = true;
                            break;
                        case "ITEM":
                            if (sourceItem ||
                                !Original323FusionSourceItemLedger.IsApproved(ability, condition))
                                throw new FormatException("원본 아이템 조합 비용 불일치: " + ability);
                            sourceItem = true;
                            break;
                        case "CHCT":
                        case "IFCT":
                            if (specialCounter ||
                                !Original323FusionCountLedger.IsApproved(ability, condition))
                                throw new FormatException("원본 변화/왜곡 횟수 조건이 다릅니다: " + ability);
                            specialCounter = true;
                            break;
                        case "UPUN": break; // display/upgrade recommendation only
                        case "UBAN":
                            if (banned || condition.Rawcode != result)
                                throw new FormatException("상위 조합 밴 대상이 다릅니다: " + ability);
                            banned = true;
                            break;
                        case "KING":
                            if (grade == "신비함")
                            {
                                if (mysticPhase != 2) mysticOrdered = false;
                                mysticPhase = 3;
                            }
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
                string expectedPickInputs;
                if (MobileMysticPickInputOrder.TryGetValue(ability, out expectedPickInputs) &&
                    string.Join(",", mysticMaterialOrder.ToArray()) != expectedPickInputs)
                    throw new FormatException("신비 PICK 조합의 원본 고정 재료 순서가 변경됐습니다: " + ability);
                // The Seraphim source is a one-use target transformation,
                // NOT a KING/UBAN top-tier craft. The original CKe target
                // table must confirm an exact no-post-hook replacement.
                if (grade == "세라핌")
                {
                    Original323GreenBloodPlan transformation;
                    if (!materials || materialCount != 1 || !greenBlood ||
                        banned || king || wood || outcomeGold != 0 || outcomeWood != 0 ||
                        !Original323StoryGreenBloodRules.TryPlan(seraphimMaterial,
                            true, false, false, out transformation) ||
                        transformation.Kind != Original323GreenBloodActionKind.Transform ||
                        transformation.ReplacementRawcode != result ||
                        (transformation.ExecuteOriginalCENAfterTransformation !=
                            (ability == "SPH3")))
                        throw new FormatException("세라핌 원본 그린블러드 교체 조건 불일치: " + ability);
                }
                else if (sourceItem)
                {
                    // A011 costs AI02+UNIT2+gold5000+wood5 with UBAN/KING.
                    // ET02 costs AI01+H08T(Luffy form)+UNIT3+wood5.
                    // ET07 costs AI00+six units+wood5+KING, no UBAN.
                    if (!materials || specialCounter || greenBlood || !wood ||
                        outcomeWood != 5 || materialCount !=
                            (ability == "ET02" ? 4 : ability == "ET07" ? 6 : 2) ||
                        (ability == "A011" &&
                            (grade != "제한됨" || !banned || !king ||
                             luffyGroup || outcomeGold != 5000)) ||
                        (ability == "ET02" &&
                            (grade != "영원한" || banned || king || !luffyGroup ||
                             outcomeGold != 0)) ||
                        (ability == "ET07" &&
                            (grade != "영원한" || banned || !king || luffyGroup ||
                             outcomeGold != 0)))
                        throw new FormatException("원본 ITEM 조합 재료·비용 불일치: " + ability);
                }
                else if (grade == "변화" || grade == "왜곡됨")
                {
                    // Exactly one audited CHCT/IFCT condition, matched to
                    // the right outcome grade. No KING, ban, or fake item cost.
                    if (!materials || !specialCounter || banned || king || greenBlood ||
                        outcomeGold != 0 ||
                        (grade == "변화" &&
                            (!wood || outcomeWood != 10 || materialCount != 1)) ||
                        (grade == "왜곡됨" &&
                            (wood || outcomeWood != 0 || materialCount < 3)))
                        throw new FormatException("원본 변화/왜곡 조합 비용·재료 불일치: " + ability);
                }
                else
                {
                    // The seven reviewed TR registrations have NO GOLD/WOOD cost.
                    // Other reviewed restricted tiers retain their source WOOD.
                    if (!materials || (grade == "초월함" ? wood : !wood) ||
                        (grade == "신비함" ? banned : !banned) || !king ||
                        ((grade == "제한됨" || grade == "신비함") && !caster) ||
                        (grade == "신비함" && (!mysticOrdered || mysticPhase != 3 ||
                            mysticGold != 10000 || mysticWood != 7 || physicalTokens != 1 ||
                            mysticPicks != (MobileMysticPickInputOrder.ContainsKey(ability) ? 1 : 0))))
                        throw new FormatException("상위 조합 원본 시전자/필수 조건 누락: " + ability);
                }
                if (mobileOutcomeOnly)
                {
                    int expectedGold = grade == "제한됨" ? (ability == "A0S0" ? 30000 : 5000) : 0;
                    int expectedWood = (grade == "불멸의" || grade == "변화") ? 10 :
                        (grade == "제한됨" || grade == "영원한" ? 5 : 0);
                    if (outcomeGold != expectedGold || outcomeWood != expectedWood)
                        throw new FormatException("모바일 결과 조합의 원본 비용 불일치: " + ability);
                }
                if (!supported.Add(ability))
                    throw new FormatException("중복 조합 실행 등록: " + ability);
                resultGrades.Add(ability, grade);
            }
        }
        private static bool IsMobileOutcomeOnly(string ability)
        {
            for (int i = 0; i < MobileOutcomeAOsfSpecial.GetLength(0); i++)
                if (MobileOutcomeAOsfSpecial[i, 0] == ability) return true;
            for (int i = 0; i < MobileOutcomeAOsfRare.GetLength(0); i++)
                if (MobileOutcomeAOsfRare[i, 0] == ability) return true;
            for (int i = 0; i < MobileOutcomeTranscend.GetLength(0); i++)
                if (MobileOutcomeTranscend[i, 0] == ability) return true;
            for (int i = 0; i < MobileOutcomeUpperTiers.GetLength(0); i++)
                if (MobileOutcomeUpperTiers[i, 0] == ability) return true;
            for (int i = 0; i < MobileOutcomeGreenBloodSeraphim.GetLength(0); i++)
                if (MobileOutcomeGreenBloodSeraphim[i, 0] == ability) return true;
            for (int i = 0; i < MobileOutcomeChanges.GetLength(0); i++)
                if (MobileOutcomeChanges[i, 0] == ability) return true;
            for (int i = 0; i < MobileOutcomeDistorted.GetLength(0); i++)
                if (MobileOutcomeDistorted[i, 0] == ability) return true;
            for (int i = 0; i < MobileOutcomeLegendary.GetLength(0); i++)
                if (MobileOutcomeLegendary[i, 0] == ability) return true;
            for (int i = 0; i < MobileOutcomeHidden.GetLength(0); i++)
                if (MobileOutcomeHidden[i, 0] == ability) return true;
            return false;
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
            if (instruction.Kind == "PICK")
            {
                // The locked live planner still must supply a distinct
                // A800-bearing RN instance; this approves only the mobile
                // model's known extra-ingredient interpretation.
                bool approved = MobileMysticPickInputOrder.ContainsKey(ability) &&
                    instruction.Rawcode == Original323MysticPickCandidatePreview.NativeRandomAbility &&
                    instruction.Count == 1;
                reason = approved ? null : "원본 PICK 소비 결과 미확인";
                return approved;
            }
            string specialWalletId, specialLabel;
            if (Original323FusionSpecialWalletCosts.TryResolve(ability, instruction,
                out specialWalletId, out specialLabel))
            {
                long available = economy.Wallet.Balance(specialWalletId);
                bool enough = available >= instruction.Count;
                reason = enough ? null : specialLabel + " 부족 (필요 " +
                    instruction.Count + ", 보유 " + available + ")";
                return enough;
            }
            if (instruction.Kind == "ITEM")
                return Original323FusionSourceItemLedger.CanSpend(
                    economy.Wallet, ability, instruction, out reason);
            if (instruction.Kind == "CHCT" || instruction.Kind == "IFCT")
                return Original323FusionCountLedger.CanCreate(
                    economy.Wallet, ability, instruction, out reason);
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
                string nativeReason;
                if (!Original323FusionNativeFollowupLedger.CanCraft(
                    economy.Wallet, offer.RecipeId, out nativeReason))
                    missing.Add(nativeReason);
                bool allUnitsOwned = true;
                bool deficitsAreOnlyCommons = true;
                int missingCommonUnits = 0;
                int deficitMaterialKinds = 0;
                foreach (var material in ready.Materials)
                {
                    if (material.MissingCount <= 0) continue;
                    allUnitsOwned = false;
                    deficitMaterialKinds++;
                    var unit = string.IsNullOrWhiteSpace(material.CatalogUnitId) ?
                        null : unitCatalog.Find(material.CatalogUnitId);
                    if (unit == null || unit.Grade != "흔함")
                        deficitsAreOnlyCommons = false;
                    else
                        missingCommonUnits += material.MissingCount;
                }
                if (!economy.HasStarted || combat.Game.Phase != SessionPhase.Running)
                    missing.Add("먼저 유닛 준비 또는 게임을 시작해 주세요.");
                // Exactly one planner shortage blocker per deficient UNIT.
                // Any extra unresolved special rule, currency, PICK, duplicate
                // identity or native-ledger blocker disqualifies near-ready.
                int? nearReadyCommonCount = missingCommonUnits > 0 &&
                    deficitsAreOnlyCommons &&
                    ready.BlockingReasons.Count == deficitMaterialKinds &&
                    missing.Count == ready.BlockingReasons.Count ?
                    (int?)missingCommonUnits : null;
                var parts = new List<string>();
                foreach (var material in ready.Materials)
                    parts.Add(material.Name + " ×" + material.RequiredCount +
                        " (보유 " + material.OwnedCount + ")");
                var costs = new List<string>();
                if (ready.GoldCost > 0)
                    costs.Add("골드 " + ready.GoldCost + " (보유 " + economy.Wallet.Balance("gold") + ")");
                if (ready.LumberCost > 0)
                    costs.Add("목재 " + ready.LumberCost + " (보유 " + economy.Wallet.Balance("wood") + ")");
                var native = originalRegistrations.FindByAbility(offer.RecipeId);
                if (native != null)
                    foreach (var condition in native.OrderedConditions)
                    {
                        string walletId, label;
                        if (Original323FusionSpecialWalletCosts.TryResolve(
                            offer.RecipeId, condition, out walletId, out label))
                            costs.Add(label + " " + condition.Count + " (보유 " +
                                economy.Wallet.Balance(walletId) + ")");
                        if (Original323FusionCountLedger.TryResolve(
                            offer.RecipeId, condition, out walletId, out label))
                            costs.Add(label + " " + economy.Wallet.Balance(walletId) +
                                "/" + condition.Count + " 사용");
                        if (Original323FusionSourceItemLedger.TryResolve(
                            offer.RecipeId, condition, out walletId, out label))
                            costs.Add(label + " " + condition.Count +
                                " (보유 " + economy.Wallet.Balance(walletId) + ")");
                    }
                result.Add(new MobileFusionPreview(offer.RecipeId, ready.ResultName + " · " + resultGrades[offer.RecipeId] +
                    (IsMobileOutcomeOnly(offer.RecipeId) ? " · 모바일 제작" : ""),
                    string.Join(" + ", parts) + (costs.Count > 0
                        ? " · " + string.Join(" · ", costs) : " · 추가 재화 없음") +
                    (IsMobileOutcomeOnly(offer.RecipeId)
                        ? " · 원본 부가효과 미검증" : "") +
                    (Original323FusionCountLedger.IsCounterFusion(offer.RecipeId)
                        ? " · 횟수 한도는 모바일 모델" : ""),
                    ready.SuggestedMaterialIds, missing, allUnitsOwned,
                    resultGrades[offer.RecipeId], nearReadyCommonCount));
            }
            return result.AsReadOnly();
        }

        // CKe A13A is a single-target spell; no unit choice is delegated to
        // the planner when the player explicitly taps an owned actor.
        public bool TryPlanGreenBlood(long instanceId,
            out Original323GreenBloodPlan plan, out string unitName)
        {
            plan = null;
            unitName = null;
            foreach (var row in combat.MobileUnitSnapshot())
            {
                if (row.InstanceId != instanceId) continue;
                var source = objects.Find(row.Unit.Id);
                if (source == null) return false;
                bool hasLegendary = false, hasHidden = false;
                foreach (string ability in source.OriginalAbilityRawcodes)
                {
                    if (ability == "A912") hasLegendary = true;
                    if (ability == "A07N") hasHidden = true;
                }
                unitName = row.Unit.Name;
                return Original323StoryGreenBloodRules.TryPlan(source.OriginalRawcode,
                    true, hasLegendary, hasHidden, out plan);
            }
            return false;
        }

        public ManualActionResult UseGreenBloodOnOwnedUnit(long instanceId)
        {
            if (busy || !economy.HasStarted || combat.Game.Phase != SessionPhase.Running)
                return Reject("현재 그린블러드를 사용할 수 없습니다.");
            if (economy.Wallet.Balance(
                Original323StoryMobileProgression.GreenBloodUseResourceId) < 1)
                return Reject("그린블러드 사용권이 없습니다.");
            Original323GreenBloodPlan plan;
            string unitName;
            if (!TryPlanGreenBlood(instanceId, out plan, out unitName))
                return Reject("자신의 전설·히든 유닛만 선택할 수 있습니다.");
            if (plan.Kind == Original323GreenBloodActionKind.Transform)
            {
                string recipe = Original323StoryGreenBloodRules.SeraphimRecipe(
                    plan.OriginalTargetRawcode);
                if (recipe == null) return Reject("원본 세라핌 변환 대상이 아닙니다.");
                return ConfirmFusion(recipe, new[] { instanceId });
            }
            if (combat.HasOriginal323GreenBloodEnhancement(instanceId))
                return Reject("이미 그린블러드 강화를 받은 유닛입니다.");
            busy = true;
            try
            {
                var snapshot = economy.Wallet.CaptureState();
                try
                {
                    var cost = new ResourceBundle().Add(
                        Original323StoryMobileProgression.GreenBloodUseResourceId, 1);
                    if (!economy.Wallet.TryExchange(cost, new ResourceBundle()))
                        return Reject("그린블러드 사용권 차감에 실패했습니다.");
                    if (!combat.ApplyOriginal323GreenBloodEnhancement(instanceId))
                    {
                        economy.Wallet.RestoreState(snapshot);
                        return Reject("대상 유닛의 강화 상태가 변경됐습니다.");
                    }
                }
                catch
                {
                    economy.Wallet.RestoreState(snapshot);
                    throw;
                }
                return new ManualActionResult(ManualActionState.Succeeded,
                    unitName + " · 공속 +30% · 825 범위 폭발 · A134 표식 / A13C 강화");
            }
            catch (Exception error)
            {
                return Reject("그린블러드 강화를 취소했습니다: " + error.Message);
            }
            finally { busy = false; }
        }

        public ManualActionResult ConfirmFusion(string ability, IReadOnlyList<long> selected)
        {
            if (!CanCommitRecipe(ability)) return fallback.ConfirmFusion(ability, selected);
            if (busy || !economy.HasStarted || combat.Game.Phase != SessionPhase.Running)
                return Reject("현재 조합할 수 없습니다.");
            string nativeBlock;
            if (!Original323FusionNativeFollowupLedger.CanCraft(
                economy.Wallet, ability, out nativeBlock))
                return Reject(nativeBlock);
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
                IReadOnlyList<long> actualMaterials = ready.SuggestedMaterialIds;
                if (resultGrades[ability] == "세라핌")
                {
                    // CKe selects the tapped actor, not the first copy of its
                    // rawcode. Both actors must have the SAME source identity.
                    if (selected.Count != 1 || ready.SuggestedMaterialIds.Count != 1)
                        return Reject("원본 그린블러드는 유닛 한 기만 선택합니다.");
                    MobileUnitSnapshot selectedActor = null, suggestedActor = null;
                    foreach (var actor in owned)
                    {
                        if (actor.InstanceId == selected[0]) selectedActor = actor;
                        if (actor.InstanceId == ready.SuggestedMaterialIds[0])
                            suggestedActor = actor;
                    }
                    var selectedSource = selectedActor == null ? null :
                        objects.Find(selectedActor.Unit.Id);
                    var suggestedSource = suggestedActor == null ? null :
                        objects.Find(suggestedActor.Unit.Id);
                    if (selectedSource == null || suggestedSource == null ||
                        selectedSource.OriginalRawcode != suggestedSource.OriginalRawcode ||
                        Original323StoryGreenBloodRules.SeraphimRecipe(
                            selectedSource.OriginalRawcode) != ability)
                        return Reject("선택한 유닛이 원본 세라핌 변환 대상과 다릅니다.");
                    actualMaterials = selected;
                }
                else
                {
                    var exact = new HashSet<long>(selected);
                    if (exact.Count != selected.Count)
                        return Reject("같은 재료를 중복 사용할 수 없습니다.");
                    foreach (long id in ready.SuggestedMaterialIds)
                        if (!exact.Remove(id))
                            return Reject("확인한 재료와 현재 재료가 다릅니다.");
                }
                double resultX = HiddenResultX, resultY = HiddenResultY;
                if (resultGrades[ability] == "세라핌" || resultGrades[ability] == "변화" || ability == "A011")
                {
                    // Green Blood and Change replace the exact target actor; the
                    // ITEM Marco mobile outcome uses its first material's
                    // existing position (native cast policy remains unverified).
                    var greenBloodTarget = new HashSet<long>(actualMaterials);
                    foreach (var row in owned)
                        if (greenBloodTarget.Contains(row.InstanceId))
                        {
                            resultX = row.Position.X;
                            resultY = row.Position.Y;
                            break;
                        }
                }
                else if (resultGrades[ability] != "히든" && resultGrades[ability] != "영원한" &&
                    resultGrades[ability] != "불멸의" && resultGrades[ability] != "초월함" &&
                    resultGrades[ability] != "신비함" && resultGrades[ability] != "왜곡됨")
                {
                    // CT1 uses the exact material actor owning the cast ability.
                    MobileUnitSnapshot caster = null;
                    var materials = new HashSet<long>(ready.SuggestedMaterialIds);
                    foreach (var row in owned)
                    {
                        if (!materials.Contains(row.InstanceId)) continue;
                        var sourceObject = objects.Find(row.Unit.Id);
                        // Source-only support actors (for example h05Y/h0AN)
                        // are legitimate materials but have no combat-stat
                        // row or native cast ability.
                        if (sourceObject == null) continue;
                        foreach (string native in sourceObject.OriginalAbilityRawcodes)
                            if (native == ability) { caster = row; break; }
                        if (caster != null) break;
                    }
                    if (caster == null) return Reject("원본 조합 능력을 가진 재료가 없습니다.");
                    resultX = caster.Position.X;
                    resultY = caster.Position.Y;
                }
                var specialCosts = Original323FusionSpecialWalletCosts.BuildApprovedCosts(
                    ability, originalRegistrations.FindByAbility(ability));
                var usageReward = Original323FusionCountLedger.BuildUsageReward(
                    ability, originalRegistrations.FindByAbility(ability));
                var nativeReward = Original323FusionNativeFollowupLedger.BuildReward(
                    economy.Wallet, ability);
                var combinedReward = new ResourceBundle();
                if (usageReward != null)
                    foreach (var entry in usageReward.Entries)
                        combinedReward.Add(entry.Key, entry.Value);
                if (nativeReward != null)
                    foreach (var entry in nativeReward.Entries)
                        combinedReward.Add(entry.Key, entry.Value);
                using (var swap = combat.BeginOriginalFusionUnitSwap(actualMaterials,
                    ready.ResultUnitId, resultX, resultY))
                {
                    created = swap.ResultInstanceId;
                    if (ready.GoldCost == 0 && ready.LumberCost == 0 && specialCosts == null &&
                        usageReward == null && nativeReward == null) swap.Commit();
                    else
                    {
                        var beforeWallet = economy.Wallet.CaptureState();
                        try
                        {
                            var cost = new ResourceBundle();
                            if (ready.GoldCost > 0) cost.Add("gold", ready.GoldCost);
                            if (ready.LumberCost > 0) cost.Add("wood", ready.LumberCost);
                            if (specialCosts != null)
                                foreach (var special in specialCosts)
                                    cost.Add(special.ResourceId, special.Count);
                            if (!Original323FusionNativeFollowupLedger.CanCraft(
                                economy.Wallet, ability, out nativeBlock))
                                throw new InvalidOperationException(nativeBlock);
                            if (!economy.Wallet.TryExchange(cost, combinedReward))
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
            string nativeLineEffect = ability == "TR05" ? " · 라인 폭발 피해 보정 +10%" :
                ability == "TR29" ? " · 라인 폭발 피해 보정 -10%" : "";
            return new ManualActionResult(ManualActionState.Succeeded, ready.ResultName +
                (IsMobileOutcomeOnly(ability)
                    ? " 모바일 조합 완료 · 원본 부가효과 미검증" : " 조합 완료") +
                nativeLineEffect);
        }
        private int SourceBalance(string id) => (int)Math.Min(int.MaxValue, economy.Wallet.Balance(id));
        private static ManualActionResult Reject(string text)
        { return new ManualActionResult(ManualActionState.Rejected, text); }
    }
}
