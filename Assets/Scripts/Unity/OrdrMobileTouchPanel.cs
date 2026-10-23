using System;
using System.Collections.Generic;
using UnityEngine;
using Ordr.Mobile.Mobile;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Unity
{
    // 임시 Android 터치 패널. Unity UI 패키지/추가 이미지 없이 동작하도록
    // OnGUI로 구현했으며, 원작 전투 규칙이나 경제 수치는 변경하지 않는다.
    // 스토리·조합확인·상점·도박·위습 버튼은 실제 사용자 탭에서만 호출된다.
    public sealed class OrdrMobileTouchPanel : MonoBehaviour
    {
        private enum Tab { Units, Story, Fusion, Shop, Gambling, Wisp, Helper, Alchemy }

        [SerializeField] private OrdrCharacterCombatController controller;
        [SerializeField] private bool openByDefault;
        private bool visible;
        private Tab currentTab = Tab.Units;
        private bool browsingFullCatalog;
        private int catalogGradeIndex;
        private int catalogPage;
        private int ownedPage;
        private long? pendingCommonSaleId;
        private string selectedCatalogUnitId;
        private Vector2 scroll;
        private Vector2 tabsScroll;
        private Vector2 dedicatedScroll;
        private string dedicatedNotice;
        private string notice = "모바일 조작 패널";
        private IReadOnlyList<MobileFusionPreview> fusionOffers =
            new List<MobileFusionPreview>().AsReadOnly();
        private IReadOnlyList<MobileManualOffer> economyOffers =
            new List<MobileManualOffer>().AsReadOnly();
        private MobileFusionPreview pendingFusion;
        private MobileManualOffer pendingEconomy;
        private string pendingChoiceRawcode;
        private bool pendingHelperRecharge;
        private bool pendingMaximumOutput;
        private bool pendingAlchemy;
        private bool alchemyTargetsExpanded;
        private long? selectedAlchemyTargetId;
        private long? pendingAlchemyCastId;
        private Vector2 alchemyTargetsScroll;
        private bool showOriginalJassRecipes;
        private IReadOnlyList<JassFusionReadiness> originalJassPreview =
            new List<JassFusionReadiness>().AsReadOnly();

        private void Awake()
        {
            if (controller == null)
                controller = GetComponent<OrdrCharacterCombatController>();
            visible = openByDefault;
            if (visible) RefreshCurrent();
        }

        public bool BlocksWorldTap(Vector2 onGui)
        {
            Rect safe = Screen.safeArea;
            if (safe.width <= safe.height) return false;
            var layout = MobileMapSkillLayout.Calculate(safe, Screen.height,
                MobileMapSkillLayout.UiScale(safe, Screen.dpi));
            return layout.BlocksWorldTap(onGui, visible);
        }

        private Ordr.Mobile.Combat.CharacterCombatRuntime Combat
        {
            get { return controller != null ? controller.Combat : null; }
        }

        // The tab rail always stays visible. Tapping the selected tab again
        // closes its details; another tab replaces the right-hand content.
        private void SelectTab(Tab selected)
        {
            if (visible && currentTab == selected)
            {
                CloseDrawer();
                return;
            }
            currentTab = selected;
            visible = true;
            scroll = Vector2.zero;
            pendingFusion = null;
            pendingEconomy = null;
            pendingChoiceRawcode = null;
            pendingHelperRecharge = false;
            pendingMaximumOutput = false;
            pendingAlchemy = false;
            alchemyTargetsExpanded = false;
            selectedAlchemyTargetId = null;
            pendingAlchemyCastId = null;
            alchemyTargetsScroll = Vector2.zero;
            pendingCommonSaleId = null;
            notice = "";
            RefreshCurrent();
        }

        private void CloseDrawer()
        {
            visible = false;
            pendingFusion = null;
            pendingEconomy = null;
            pendingChoiceRawcode = null;
            pendingHelperRecharge = false;
            pendingMaximumOutput = false;
            pendingAlchemy = false;
            alchemyTargetsExpanded = false;
            selectedAlchemyTargetId = null;
            pendingAlchemyCastId = null;
            pendingCommonSaleId = null;
        }

        private void OnGUI()
        {
            if (controller == null) return;
            Rect safe = Screen.safeArea;
            if (safe.width <= safe.height) return;

            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
            float scale = MobileMapSkillLayout.UiScale(safe, dpi);
            var layout = MobileMapSkillLayout.Calculate(safe, Screen.height, scale);
            if (safe.width / scale < 120f || safe.height / scale < 100f) return;

            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            DrawLeftTabs(layout, scale);
            if (visible)
                DrawRightDrawer(layout, scale);

            // The horizontal dedicated-skill ribbon stays accessible between
            // the main and small story battlefields while a menu is open.
            DrawDedicatedSkillMargin(safe, scale);
            DrawEconomyStatus(layout, scale);
            GUI.matrix = previous;
        }

        private void DrawEconomyStatus(MobileMapSkillLayout layout, float scale)
        {
            Rect area = MobileMapSkillLayout.Scaled(layout.StatusMargin, scale);
            if (area.height < 22f) return;
            GUILayout.BeginArea(area, GUI.skin.box);
            var economy = controller.SharedEconomy;
            if (economy == null)
                GUILayout.Label("골드·목재·위습: 게임 경제 연결 대기");
            else
            {
                GUILayout.Label("골드 " + economy.Wallet.Balance("gold") +
                    "   목재 " + economy.Wallet.Balance("wood") +
                    "   랜덤위습 " + economy.Wallet.Balance(WispResourceIds.Random));
                if (area.height >= 45f)
                {
                    var game = Combat == null ? null : Combat.Game;
                    GUILayout.Label(game == null
                        ? "진행 라운드: 대기"
                        : "라운드 " + game.CurrentWave +
                          "   진행 상태 " + game.Phase +
                          "   필드 적 " + game.AliveEnemies);
                }
            }
            GUILayout.EndArea();
        }

        private void DrawLeftTabs(MobileMapSkillLayout layout, float scale)
        {
            Rect area = MobileMapSkillLayout.Scaled(layout.LeftTabs, scale);
            GUILayout.BeginArea(area, GUI.skin.box);
            tabsScroll = GUILayout.BeginScrollView(tabsScroll,
                false, true, GUILayout.ExpandHeight(true));

            // Order matches the most common manual activities.
            DrawLeftTab(Tab.Wisp, "위습");
            DrawLeftTab(Tab.Helper, "도움소");
            if (controller.Original323HelperCow != null &&
                controller.Original323HelperCow.AlchemyNavigationChosen)
                DrawLeftTab(Tab.Alchemy, "연금술");
            DrawLeftTab(Tab.Units, "유닛");
            DrawLeftTab(Tab.Fusion, "조합");
            DrawLeftTab(Tab.Story, "스토리");
            DrawLeftTab(Tab.Shop, "상점");
            DrawLeftTab(Tab.Gambling, "도박");

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawLeftTab(Tab tab, string title)
        {
            bool chosen = visible && currentTab == tab;
            string label = chosen ? "●\n" + title : title;
            if (GUILayout.Button(label, GUILayout.MinHeight(46f),
                GUILayout.ExpandWidth(true)))
                SelectTab(tab);
        }

        private void DrawRightDrawer(MobileMapSkillLayout layout, float scale)
        {
            Rect area = MobileMapSkillLayout.Scaled(layout.RightDrawer, scale);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label(TabTitle(currentTab), GUILayout.ExpandWidth(true));
            if (GUILayout.Button("닫기 ×", GUILayout.Width(82f), GUILayout.MinHeight(40f)))
                CloseDrawer();
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            if (Combat == null && currentTab != Tab.Units)
                GUILayout.Label("게임 월드 연결 전입니다. 재화나 유닛을 임의로 처리하지 않습니다.");
            else
            {
                switch (currentTab)
                {
                    case Tab.Units: DrawUnits(); break;
                    case Tab.Story: DrawStory(); break;
                    case Tab.Fusion: DrawFusion(); break;
                    case Tab.Shop: DrawEconomy(ManualEconomyCategory.Shop); break;
                    case Tab.Gambling: DrawEconomy(ManualEconomyCategory.Gambling); break;
                    case Tab.Wisp: DrawWisp(); break;
                    case Tab.Helper: DrawHelperCow(); break;
                    case Tab.Alchemy: DrawAlchemyPanel(); break;
                }
            }
            GUILayout.EndScrollView();
            if (!string.IsNullOrWhiteSpace(notice))
                GUILayout.Label(notice, GUILayout.MinHeight(24f));

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private static string TabTitle(Tab tab)
        {
            switch (tab)
            {
                case Tab.Wisp: return "위습";
                case Tab.Helper: return "도움소";
                case Tab.Alchemy: return "연금술";
                case Tab.Units: return "유닛";
                case Tab.Fusion: return "조합";
                case Tab.Story: return "스토리";
                case Tab.Shop: return "상점";
                case Tab.Gambling: return "도박";
                default: throw new ArgumentOutOfRangeException(nameof(tab));
            }
        }

        // These are facility/unit-specific manual actions (e.g. record
        // instructions or Vivi), not Q/W/R combat buttons.
        // Buttons appear ONLY when a verified original action port supplies them.
        private void DrawDedicatedSkillMargin(Rect safe, float scale)
        {
            var layout = MobileMapSkillLayout.Calculate(safe, Screen.height, scale);
            Rect area = MobileMapSkillLayout.Scaled(layout.SkillMargin, scale);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("전용 스킬", GUILayout.Width(76f),
                GUILayout.MinHeight(Mathf.Max(40f, area.height - 9f)));

            var actions = controller.DedicatedSkills;
            if (!actions.IsConnected)
                GUILayout.Label("원작 실행 기능 연결 대기");
            else
            {
                var available = actions.Available();
                if (available.Count == 0)
                    GUILayout.Label("현재 전용 스킬 없음");
                else
                {
                    // Always a single horizontal row, swiped left/right.
                    dedicatedScroll = GUILayout.BeginScrollView(dedicatedScroll, true, false,
                        GUILayout.Height(Mathf.Max(45f, area.height - 5f)));
                    GUILayout.BeginHorizontal();
                    foreach (var skill in available)
                    {
                        if (skill == null) continue;
                        bool previous = GUI.enabled;
                        GUI.enabled = previous && skill.IsUnlocked && skill.CanExecute;
                        bool pressed = GUILayout.Button(
                            skill.Title + "\n" + skill.OriginalCostAndRules,
                            GUILayout.Width(140f),
                            GUILayout.Height(Mathf.Max(34f, area.height - 27f)));
                        GUI.enabled = previous;
                        if (pressed)
                        {
                            ManualActionResult result = actions.Request(skill.Id);
                            dedicatedNotice = result.Message;
                            ShowResult(result);
                        }
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.EndScrollView();
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // All 271 catalog units can be inspected; only units actually owned
        // by the running world appear in the live selection list. No free spawns.
        private void DrawUnits()
        {
            var definitions = controller.UnitDefinitions;
            if (definitions == null)
            {
                GUILayout.Label("유닛 카탈로그가 로드되지 않았습니다.");
                return;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("유닛 " + definitions.Count + "종", GUILayout.ExpandWidth(true));
            if (GUILayout.Button(browsingFullCatalog ? "보유 목록" : "전체 도감",
                GUILayout.Width(110), GUILayout.MinHeight(46)))
            {
                browsingFullCatalog = !browsingFullCatalog;
                selectedCatalogUnitId = null;
                scroll = Vector2.zero;
            }
            GUILayout.EndHorizontal();

            if (browsingFullCatalog)
                DrawCatalogIndex(definitions);
            else
                DrawOwnedUnitIndex();

            var detail = definitions.Find(selectedCatalogUnitId);
            if (detail == null) return;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(detail.DisplayName + " · " + detail.Grade);
            if (detail.Profile == null && detail.Unit.Id == "C-01")
                GUILayout.Label("흔함 루피: 추가 공격 스킬 없음 (원본 JASS 등록 능력 기준)");
            else if (detail.Profile == null)
                GUILayout.Label("상세 기술: 원본 도감 정보 미수집");
            else
            {
                GUILayout.Label("스킬 " + detail.SkillCount +
                    "개 / 패시브 " + detail.PassiveCount + "개");
                int skillLimit = Math.Min(detail.Profile.Skills.Count, 8);
                for (int i = 0; i < skillLimit; i++)
                {
                    var skill = detail.Profile.Skills[i];
                    GUILayout.Label("· " + skill.Name);
                }
                if (detail.Profile.Skills.Count > skillLimit)
                    GUILayout.Label("외 " + (detail.Profile.Skills.Count - skillLimit) + "개");
            }
            // Original W3X base values are display-only until hit damage,
            // modifiers and JASS timing have their independent verification.
            var originalMap = controller.MapBaseStats == null ? null :
                controller.MapBaseStats.Find(detail.Unit.Id);
            var attack = originalMap == null ? null : originalMap.Attack1;
            if (attack != null && attack.MinimumBaseDamage.HasValue &&
                attack.MaximumBaseDamage.HasValue &&
                attack.BaseAttacksPerSecond.HasValue)
            {
                GUILayout.Label("맵 기본 공격력: " +
                    attack.MinimumBaseDamage.Value + "~" +
                    attack.MaximumBaseDamage.Value);
                GUILayout.Label("기본 공속: " +
                    attack.BaseAttacksPerSecond.Value.ToString("0.##") +
                    "회/초 (" + attack.AttackCooldownSeconds.Value.ToString("0.##") +
                    "초/타)");
            }
            else
            {
                GUILayout.Label("공격력·기본 공속: 원본 수치 미확인");
            }
            GUILayout.EndVertical();
        }

        private void DrawOwnedUnitIndex()
        {
            if (Combat == null)
            {
                GUILayout.Label("월드가 시작되면 보유한 유닛이 이곳에 표시됩니다.");
                return;
            }
            var owned = Combat.MobileUnitSnapshot();
            GUILayout.Label("보유 " + owned.Count + "기 · 선택 시 상세 보기");
            const int pageSize = 12;
            int pages = Math.Max(1, (owned.Count + pageSize - 1) / pageSize);
            ownedPage = Math.Min(ownedPage, pages - 1);
            for (int i = ownedPage * pageSize;
                 i < Math.Min(owned.Count, (ownedPage + 1) * pageSize); i++)
            {
                var entry = owned[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(entry.Unit.Name + " (" + entry.Unit.Grade + ")",
                    GUILayout.ExpandWidth(true));
                if (GUILayout.Button("선택", GUILayout.Width(78), GUILayout.MinHeight(48)))
                {
                    Combat.Roster.Select(entry.InstanceId);
                    selectedCatalogUnitId = entry.Unit.Id;
                    notice = entry.Unit.Name + " 선택";
                }
                GUILayout.EndHorizontal();
                // No A0JE/Avul buttons: mobile placement and protection are
                // non-manual policies. C-01 sale is manually confirmed only.
                if (entry.Unit.Id == OriginalLuffyCommonSaleCoordinator.UnitCatalogId)
                {
                    bool oldEnabled = GUI.enabled;
                    GUI.enabled = oldEnabled && controller.ManualActions.HasOriginalCommonSaleService;
                    if (GUILayout.Button("흔함 루피 판매", GUILayout.MinHeight(48)))
                    {
                        pendingCommonSaleId = entry.InstanceId;
                        notice = "루피를 판매하면 되돌릴 수 없습니다. 아래에서 확인하세요.";
                    }
                    GUI.enabled = oldEnabled;
                    if (!controller.ManualActions.HasOriginalCommonSaleService)
                        GUILayout.Label("원본 위습/재화 거래 미연결: 판매 불가");
                }
                if (SpecialGradeMobilePolicy.IsManualOnly(entry.Unit.Id) &&
                    GUILayout.Button("갱 뱃지 분해", GUILayout.MinHeight(48)))
                    notice = controller.ManualActions.RequestUnitDecomposition(entry).Message;
            }
            if (pendingCommonSaleId.HasValue)
            {
                GUILayout.Label("루피 #" + pendingCommonSaleId.Value + " 판매 확정?");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("판매 확인", GUILayout.MinHeight(48)))
                {
                    ShowResult(controller.ManualActions.RequestCommonSale(
                        pendingCommonSaleId.Value));
                    pendingCommonSaleId = null;
                }
                if (GUILayout.Button("취소", GUILayout.MinHeight(48)))
                    pendingCommonSaleId = null;
                GUILayout.EndHorizontal();
            }
            DrawPager(ownedPage, pages, next => ownedPage = next);
        }

        private void DrawCatalogIndex(Ordr.Mobile.Data.UnitImplementationRegistry definitions)
        {
            var grades = definitions.Grades;
            if (grades.Count == 0) return;
            catalogGradeIndex = Math.Min(catalogGradeIndex, grades.Count - 1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(54), GUILayout.MinHeight(44)))
            {
                catalogGradeIndex = (catalogGradeIndex + grades.Count - 1) % grades.Count;
                catalogPage = 0;
            }
            GUILayout.Label(grades[catalogGradeIndex], GUILayout.ExpandWidth(true));
            if (GUILayout.Button("▶", GUILayout.Width(54), GUILayout.MinHeight(44)))
            {
                catalogGradeIndex = (catalogGradeIndex + 1) % grades.Count;
                catalogPage = 0;
            }
            GUILayout.EndHorizontal();
            var entries = definitions.OfGrade(grades[catalogGradeIndex]);
            const int pageSize = 10;
            int pages = Math.Max(1, (entries.Count + pageSize - 1) / pageSize);
            catalogPage = Math.Min(catalogPage, pages - 1);
            for (int i = catalogPage * pageSize;
                 i < Math.Min(entries.Count, (catalogPage + 1) * pageSize); i++)
            {
                var unit = entries[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(unit.DisplayName, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("정보", GUILayout.Width(78), GUILayout.MinHeight(48)))
                    selectedCatalogUnitId = unit.UnitId;
                GUILayout.EndHorizontal();
            }
            DrawPager(catalogPage, pages, next => catalogPage = next);
        }

        private void DrawPager(int page, int pages, Action<int> changePage)
        {
            if (pages < 2) return;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("이전", GUILayout.MinHeight(44)))
                changePage((page + pages - 1) % pages);
            GUILayout.Label((page + 1) + " / " + pages,
                GUILayout.ExpandWidth(true));
            if (GUILayout.Button("다음", GUILayout.MinHeight(44)))
                changePage((page + 1) % pages);
            GUILayout.EndHorizontal();
        }

        private void DrawStory()
        {
            GUILayout.Label("스토리 파견은 반드시 직접 선택합니다. 자동 파견하지 않습니다.");
            if (!controller.ManualActions.HasStoryService)
                GUILayout.Label("원작 스토리 파견 서비스 미연결: 파견 기능 비활성");
            foreach (var unit in Combat.MobileUnitSnapshot())
            {
                GUILayout.BeginHorizontal();
                string name = unit.Unit.Name + " (" + unit.Unit.Grade + ") #" + unit.InstanceId;
                GUILayout.Label(name, GUILayout.MinWidth(110), GUILayout.ExpandWidth(true));
                if (GUILayout.Button("선택", GUILayout.Width(64), GUILayout.MinHeight(48)))
                {
                    Combat.Roster.Select(unit.InstanceId);
                    notice = name + " 선택";
                }
                if (controller.ManualActions.IsStoryDeployed(unit.InstanceId))
                {
                    if (controller.ManualActions.CanRecallStory(unit.InstanceId))
                    {
                        if (GUILayout.Button("복귀", GUILayout.Width(72), GUILayout.MinHeight(48)))
                            ShowResult(controller.ManualActions.RequestStoryRecall(unit.InstanceId));
                    }
                    else GUILayout.Label("파견 중", GUILayout.Width(72));
                }
                else if (GUILayout.Button("파견", GUILayout.Width(72), GUILayout.MinHeight(48)))
                    ShowResult(controller.ManualActions.RequestStoryDispatch(unit.InstanceId));
                GUILayout.EndHorizontal();
            }
        }

        private void DrawFusion()
        {
            GUILayout.Label("보유 유닛을 바탕으로 조합식을 보여줍니다. 재료 소비는 확인 버튼에서만 실행합니다.");
            DrawChoiceWispMerge();
            DrawOriginalJassAudit();
            if (!controller.ManualActions.HasFusionService)
                GUILayout.Label("원작 조합식·재료·비용 시스템 미연결");
            else if (!controller.ManualActions.HasExecutableFusionService)
                GUILayout.Label("검증된 조합식 미리보기만 가능: 원작 제작 트리거 미연결, 재료는 소비되지 않습니다.");
            if (GUILayout.Button("조합식 새로고침", GUILayout.MinHeight(48)))
                RefreshCurrent();
            foreach (var offer in fusionOffers)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(offer.Title);
                GUILayout.Label(offer.OriginalMaterialsAndCost);
                if (offer.MissingMaterials.Count != 0)
                {
                    GUILayout.Label("미확보: " + string.Join(", ",
                        ToStringArray(offer.MissingMaterials)));
                }
                else GUILayout.Label("재료 준비 완료");
                GUILayout.Label("보유 재료 ID: " + string.Join(", ",
                    ToStringArray(offer.SuggestedMaterialInstances)));
                bool previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && offer.IsReady;
                if (GUILayout.Button(offer.IsReady ? "조합 선택" : "재료 부족",
                    GUILayout.MinHeight(48)))
                {
                    pendingFusion = offer;
                    pendingEconomy = null;
                    notice = "아직 재료를 소비하지 않았습니다. 아래에서 조합을 확정하세요.";
                }
                GUI.enabled = previousEnabled;
                GUILayout.EndVertical();
            }
            if (pendingFusion != null)
            {
                GUILayout.Label("선택됨: " + pendingFusion.Title);
                GUILayout.BeginHorizontal();
                bool priorEnabled = GUI.enabled;
                GUI.enabled = priorEnabled && controller.ManualActions.HasExecutableFusionService;
                if (GUILayout.Button("조합 확정", GUILayout.MinHeight(52)))
                {
                    ShowResult(controller.ManualActions.ConfirmFusion(pendingFusion));
                    pendingFusion = null;
                    RefreshCurrent();
                }
                GUI.enabled = priorEnabled;
                if (GUILayout.Button("취소", GUILayout.MinHeight(52))) pendingFusion = null;
                GUILayout.EndHorizontal();
            }
        }

        private void DrawChoiceWispMerge()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("선택위습 Merge · " + controller.WispBalance(WispResourceIds.Choice) + "개");
            var targets = controller.ChoiceWispTargets;
            if (targets.Count == 0)
                GUILayout.Label("원본 선택·조합 규칙 미연결");
            else if (controller.WispBalance(WispResourceIds.Choice) > 0)
            {
                foreach (var target in targets)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(target.Name + " · " + target.OriginalRequirements,
                        GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("선택", GUILayout.Width(78), GUILayout.MinHeight(48)))
                        pendingChoiceRawcode = target.Rawcode;
                    GUILayout.EndHorizontal();
                }
            }
            if (!string.IsNullOrWhiteSpace(pendingChoiceRawcode))
            {
                GUILayout.Label("Merge 선택: " + pendingChoiceRawcode);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Merge 확정", GUILayout.MinHeight(52)))
                {
                    ShowResult(controller.RequestChoiceWispMerge(pendingChoiceRawcode));
                    pendingChoiceRawcode = null;
                    RefreshCurrent();
                }
                if (GUILayout.Button("취소", GUILayout.MinHeight(52)))
                    pendingChoiceRawcode = null;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        private void DrawWisp()
        {
            GUILayout.Label("랜덤위습 " + controller.WispBalance(WispResourceIds.Random) +
                "개 · 선택위습 " + controller.WispBalance(WispResourceIds.Choice) + "개");
            GUILayout.Label("자동: 안흔함 " + controller.WispBalance(WispResourceIds.Uncommon) +
                " / 특별 " + controller.WispBalance(WispResourceIds.Special) +
                " / 희귀 " + controller.WispBalance(WispResourceIds.Rare) +
                " / 전설 " + controller.WispBalance(WispResourceIds.Legendary));
            if (controller.Wisps == null)
                GUILayout.Label("자동 위습 보상 월드 미연결: 보유분 유지");
            DrawEconomy(ManualEconomyCategory.Wisp);
            GUILayout.Label("선택위습은 조합 탭의 Merge에서 사용합니다.");
        }

        // The resource and the two combat abilities are separate:
        // a recharge remains explicitly manual; auto casts require a verified
        // damage/immunity port and an A04R mechanics port for A07T.
        private void DrawHelperCow()
        {
            OriginalHelperCowManaSnapshot mana;
            bool connected = controller.ManualActions.TryGetHelperCowMana(out mana);
            GUILayout.Label(connected
                ? "도움소 h08A · 마나 " + mana.Current.ToString("0.#") +
                  " / " + mana.Maximum.ToString("0.#")
                : "도움소 생성 또는 실제 마나 월드 연결 대기");
            GUILayout.Label("랜덤위습 보유 " +
                controller.WispBalance(WispResourceIds.Random) + "개");
            if (controller.Original323HelperCow != null)
            {
                var helper = controller.Original323HelperCow;
                var values = new Ordr323OriginalHelperCastDataSource();
                foreach (string ability in new[] { "A0BZ", "A07T" })
                {
                    int level = helper.GetAbilityLevel(ability);
                    OriginalHelperCowVerifiedCastData verified;
                    if (values.TryRead(ability, level, out verified))
                        GUILayout.Label(ability + " · 레벨 " + level +
                            " · 마나 " + verified.ManaCost.ToString("0") +
                            " · 쿨타임 " + verified.CooldownSeconds.ToString("0") + "초");
                    else
                        GUILayout.Label(ability + " · 원본 시전 수치 확인 필요");
                }
                var navigation = controller.Original323HelperNavigation;
                if (navigation != null && navigation.ChosenNavigation != null)
                    GUILayout.Label("항법 선택: " + navigation.ChosenNavigation);
                else if (navigation != null)
                {
                    GUILayout.Label("항법은 21라운드부터 단 한 번 선택합니다.");
                    bool wasEnabled = GUI.enabled;
                    GUI.enabled = wasEnabled && navigation.CanChooseMaximumOutput;
                    if (!pendingMaximumOutput && !pendingAlchemy)
                    {
                        if (GUILayout.Button("최고의도움: 최대출력 선택", GUILayout.MinHeight(48)))
                            pendingMaximumOutput = true;
                    }
                    else if (pendingMaximumOutput)
                    {
                        GUILayout.Label("이 항법을 선택하면 다른 항법을 선택할 수 없습니다.");
                        if (GUILayout.Button("최대출력 항법 확정", GUILayout.MinHeight(48)))
                        {
                            notice = controller.ChooseOriginal323MaximumOutput()
                                ? "최대출력 항법을 선택했습니다." : "항법 선택 조건을 확인해 주세요.";
                            pendingMaximumOutput = false;
                        }
                    }
                    GUI.enabled = wasEnabled;
                    if (pendingMaximumOutput && GUILayout.Button("항법 선택 취소", GUILayout.MinHeight(40)))
                        pendingMaximumOutput = false;
                    GUI.enabled = wasEnabled && navigation.CanChooseAlchemy;
                    if (!pendingMaximumOutput && !pendingAlchemy)
                    {
                        if (GUILayout.Button("최고의도움: 연금술 선택", GUILayout.MinHeight(48)))
                            pendingAlchemy = true;
                    }
                    else if (pendingAlchemy)
                    {
                        GUILayout.Label("연금술 항법을 선택하면 최대출력을 선택할 수 없습니다.");
                        if (GUILayout.Button("연금술 항법 확정", GUILayout.MinHeight(48)))
                        {
                            notice = controller.ChooseOriginal323Alchemy()
                                ? "연금술 항법 선택 완료 (시전 대상 연동은 검증 대기 중)"
                                : "항법 선택 조건을 확인해 주세요.";
                            pendingAlchemy = false;
                        }
                    }
                    GUI.enabled = wasEnabled;
                    if (pendingAlchemy && GUILayout.Button("항법 선택 취소", GUILayout.MinHeight(40)))
                        pendingAlchemy = false;
                }
                if (helper.AlchemyNavigationChosen)
                {
                    if (GUILayout.Button("연금술 대상 목록 열기", GUILayout.MinHeight(44)))
                        SelectTab(Tab.Alchemy);
                }
                if (helper.ManaPotionChargesRemaining > 0)
                {
                    GUILayout.Label("마나 포션: " + helper.ManaPotionChargesRemaining +
                        "회 남음 · 성공적으로 시전한 뒤 마나 300 회복");
                    bool wasEnabled = GUI.enabled;
                    GUI.enabled = wasEnabled && controller.Original323ManaPotion != null &&
                        controller.Original323ManaPotion.CanAttempt;
                    if (GUILayout.Button("마나 포션 사용", GUILayout.MinHeight(48)))
                        notice = controller.UseOriginal323ManaPotion()
                            ? "마나 포션 사용 완료" : "원본 마나 포션 시전 조건 미충족";
                    GUI.enabled = wasEnabled;
                    if (controller.Original323ManaPotion == null)
                        GUILayout.Label("포션 시전 비용 검증 대기: 사용 불가");
                }
            }
            GUILayout.Label(controller.HelperCowAutoArea == null
                ? "자동 광역 스킬: 원본 피해·대상 판정기 미연결"
                : "자동 광역 스킬: 전투 조건 충족 시 검증된 처리기로 실행");
            GUILayout.Label("A07T의 A04R은 지속 능력 레벨 증가(+5/+8)이며 스턴이 아닙니다.");

            int round = Combat == null ? 0 : Combat.Game.CurrentWave;
            int gain = OriginalHelperCowRules.RechargeAmount(round);
            GUILayout.Label("랜덤위습 1개 사용 → 마나 " + gain +
                " 회복 (최대 마나 상태에서도 위습 소비)");
            bool canRecharge = controller.HelperCowRecharge != null &&
                controller.HelperCowRecharge.CanRecharge;
            bool previouslyEnabled = GUI.enabled;
            GUI.enabled = previouslyEnabled && canRecharge;
            if (!pendingHelperRecharge)
            {
                if (GUILayout.Button("도움소 마나 충전 선택", GUILayout.MinHeight(50)))
                    pendingHelperRecharge = true;
            }
            else
            {
                GUILayout.Label("랜덤위습 1개가 소비됩니다. 실행하시겠습니까?");
                if (GUILayout.Button("충전 확정", GUILayout.MinHeight(50)))
                {
                    ShowResult(controller.ManualActions.RequestEconomyAction(
                        ManualEconomyCategory.Wisp, OriginalHelperCowRules.OfferId));
                    pendingHelperRecharge = false;
                }
            }
            GUI.enabled = previouslyEnabled;
            if (pendingHelperRecharge &&
                GUILayout.Button("취소", GUILayout.MinHeight(44)))
                pendingHelperRecharge = false;
        }

        private void DrawAlchemyPanel()
        {
            if (controller.Original323HelperCow == null ||
                !controller.Original323HelperCow.AlchemyNavigationChosen)
            {
                GUILayout.Label("도움소에서 연금술 항법을 선택하면 열립니다.");
                return;
            }
            GUILayout.Label("연금술 · 마나 100 / 쿨타임 5초");
            DrawAlchemyTargetDropdown();
        }

        // A compact, expandable picker. The UI selection is NOT a spell cast:
        // verified original ownership/ability flags and unit-transaction hooks
        // are still required before the target can ever be consumed.
        private void DrawAlchemyTargetDropdown()
        {
            var owned = Combat == null ? null : Combat.MobileUnitSnapshot();
            int count = owned == null ? 0 : owned.Count;
            string selectedTitle = "대상 미선택";
            MobileUnitSnapshot selected = null;
            if (owned != null && selectedAlchemyTargetId.HasValue)
                foreach (var item in owned)
                    if (item.InstanceId == selectedAlchemyTargetId.Value)
                    {
                        selected = item;
                        selectedTitle = item.Unit.Name + " #" + item.InstanceId;
                        break;
                    }
            if (selected == null)
            {
                selectedAlchemyTargetId = null; // vanished/consumed elsewhere
                pendingAlchemyCastId = null;
            }

            if (GUILayout.Button("연금술 대상 " + (alchemyTargetsExpanded ? "▲" : "▼") +
                "  " + selectedTitle + "  (보유 " + count + "기)", GUILayout.MinHeight(46)))
                alchemyTargetsExpanded = !alchemyTargetsExpanded;

            if (alchemyTargetsExpanded)
            {
                if (count == 0)
                    GUILayout.Label("선택할 보유 유닛이 없습니다.");
                else
                {
                    alchemyTargetsScroll = GUILayout.BeginScrollView(
                        alchemyTargetsScroll, GUILayout.Height(195f));
                    foreach (var item in owned)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(item.Unit.Name + " · " + item.Unit.Grade +
                            "  #" + item.InstanceId, GUILayout.ExpandWidth(true));
                        if (GUILayout.Button(selectedAlchemyTargetId == item.InstanceId
                            ? "선택됨" : "선택", GUILayout.Width(72), GUILayout.MinHeight(36)))
                        {
                            selectedAlchemyTargetId = item.InstanceId;
                            pendingAlchemyCastId = null;
                            alchemyTargetsExpanded = false;
                            selected = item;
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndScrollView();
                }
            }

            if (selected == null) return;
            var links = controller.OriginalUnitRawcodes;
            var refunds = controller.OriginalAlchemyRefunds;
            var link = links == null ? null : links.FindUnit(selected.Unit.Id);
            var recipe = link == null || refunds == null ? null : refunds.Find(link.Rawcode);
            if (recipe == null)
            {
                GUILayout.Label("이름상 원본 R3 환급표 미확인: 대상의 실제 ID 검증 필요");
            }
            else
            {
                // Names are preview hints only; the locked world inspects the
                // actual original target rawcode immediately before spending.
                GUILayout.Label("원본 조합 재료 환급 미리보기 (실제 대상 검증 전)");
                if (recipe.RefundUnits.Count == 0)
                    GUILayout.Label("반환되는 UNIT 재료 없음");
                foreach (var unit in recipe.RefundUnits)
                {
                    var material = links.FindRawcode(unit.Rawcode);
                    string name = material != null ? material.UnitDisplayName :
                        "원본 ID " + unit.Rawcode;
                    GUILayout.Label("· " + name + " ×" + unit.Count);
                }
            }
            var casting = controller.Original323AlchemyCasting;
            if (casting == null)
            {
                GUILayout.Label("원본 객체/원자적 소비 월드 미연결 · 실행 대기");
                return;
            }
            if (casting.CooldownRemaining > 0)
                GUILayout.Label("연금술 남은 쿨타임: " +
                    casting.CooldownRemaining.ToString("0.0") + "초");
            bool enabledBefore = GUI.enabled;
            GUI.enabled = enabledBefore && casting.CanAttempt;
            if (pendingAlchemyCastId != selected.InstanceId)
            {
                if (GUILayout.Button("연금술 실행 선택", GUILayout.MinHeight(46)))
                    pendingAlchemyCastId = selected.InstanceId;
            }
            else
            {
                GUILayout.Label("선택한 유닛이 영구 소비됩니다. 원본 조건과 재료는 확정 시 재검증됩니다.");
                if (GUILayout.Button("유닛 소비 및 연금술 확정", GUILayout.MinHeight(48)))
                {
                    var result = controller.TryUseOriginal323Alchemy(selected.InstanceId);
                    ShowResult(result);
                    pendingAlchemyCastId = null;
                    if (result.State == ManualActionState.Succeeded)
                    {
                        selectedAlchemyTargetId = null;
                        RefreshCurrent();
                    }
                }
                GUI.enabled = enabledBefore;
                if (GUILayout.Button("연금술 취소", GUILayout.MinHeight(40)))
                    pendingAlchemyCastId = null;
            }
            GUI.enabled = enabledBefore;
        }

        private void DrawOriginalJassAudit()
        {
            var labels = controller.OriginalUnitRawcodes;
            var planner = controller.OriginalFusionPlanner;
            if (labels == null) return;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("원본 W3X 유닛 ID 대응 " + labels.Count + "종 (이름 근거, 소비 미승인)");
            if (planner == null)
            {
                GUILayout.Label("원본 JASS 등록식 JSON 복원 전: 도감 7종만 미리보기 가능.");
                GUILayout.Label("tools/unpack_ordered_jass_fusions.py 실행 후 Unity 데이터 동기화 필요.");
                GUILayout.EndVertical();
                return;
            }
            GUILayout.Label("원본 JASS 조합 등록 " + planner.RegistrationCount +
                "건. 재료·특수조건 미확인 시 제작 금지.");
            if (GUILayout.Button(showOriginalJassRecipes ?
                "원본 조합 확인 목록 닫기" : "원본 조합 확인 목록 보기", GUILayout.MinHeight(48)))
            {
                showOriginalJassRecipes = !showOriginalJassRecipes;
                RefreshCurrent();
            }
            if (showOriginalJassRecipes)
            {
                int visible = Math.Min(18, originalJassPreview.Count);
                GUILayout.Label("결과 이름이 확인된 " + originalJassPreview.Count +
                    "건 중 " + visible + "건 표시. 제작할 수 있다는 뜻은 아닙니다.");
                for (int i = 0; i < visible; i++)
                {
                    var result = originalJassPreview[i];
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label(result.ResultName + " [" + result.AbilityRawcode + "]");
                    foreach (var material in result.Materials)
                        GUILayout.Label(material.Name + " " + material.OwnedCount +
                            " / " + material.RequiredCount);
                    GUILayout.Label("골드 " + result.GoldCost +
                        " / 목재 " + result.LumberCost);
                    if (result.BlockingReasons.Count > 0)
                        GUILayout.Label("미확인: " + result.BlockingReasons[0]);
                    GUILayout.EndVertical();
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawEconomy(ManualEconomyCategory category)
        {
            GUILayout.Label("가격·확률·횟수는 원작 서비스가 판정합니다. 자동 구매/도박/위습 사용 없음.");
            if (!controller.ManualActions.HasEconomyService)
                GUILayout.Label("원작 경제 서비스 미연결");
            if (GUILayout.Button("목록 새로고침", GUILayout.MinHeight(48)))
                RefreshCurrent();
            foreach (var offer in economyOffers)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(offer.Title);
                GUILayout.Label(offer.OriginalPriceAndRules);
                if (GUILayout.Button("선택", GUILayout.MinHeight(48)))
                {
                    pendingEconomy = offer;
                    notice = "아직 비용을 소비하지 않았습니다. 아래에서 직접 확정하세요.";
                }
                GUILayout.EndVertical();
            }
            if (pendingEconomy != null)
            {
                GUILayout.Label("확인: " + pendingEconomy.Title);
                GUILayout.Label(pendingEconomy.OriginalPriceAndRules);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("직접 실행", GUILayout.MinHeight(52)))
                {
                    ShowResult(controller.ManualActions.RequestEconomyAction(
                        category, pendingEconomy.Id));
                    pendingEconomy = null;
                    RefreshCurrent();
                }
                if (GUILayout.Button("취소", GUILayout.MinHeight(52))) pendingEconomy = null;
                GUILayout.EndHorizontal();
            }
        }

        private void RefreshCurrent()
        {
            if (Combat == null) return;
            if (currentTab == Tab.Fusion)
            {
                fusionOffers = controller.ManualActions.PreviewFusion(Combat.MobileUnitSnapshot());
                if (showOriginalJassRecipes && controller.OriginalFusionPlanner != null)
                    originalJassPreview = controller.OriginalFusionPlanner.AssessNamedResults(
                        Combat.MobileUnitSnapshot(), null, null);
            }
            else if (currentTab == Tab.Shop || currentTab == Tab.Gambling || currentTab == Tab.Wisp)
                economyOffers = controller.ManualActions.ListEconomyOffers(ToEconomy(currentTab));
        }

        private void ShowResult(ManualActionResult result)
        {
            notice = result.State == ManualActionState.Succeeded
                ? "실행 완료: " + result.Message : "실행 안 됨: " + result.Message;
        }

        private static ManualEconomyCategory ToEconomy(Tab tab)
        {
            switch (tab)
            {
                case Tab.Shop: return ManualEconomyCategory.Shop;
                case Tab.Gambling: return ManualEconomyCategory.Gambling;
                case Tab.Wisp: return ManualEconomyCategory.Wisp;
                default: throw new ArgumentOutOfRangeException(nameof(tab));
            }
        }

        private static string[] ToStringArray(IReadOnlyList<string> values)
        {
            var result = new string[values.Count];
            for (int i = 0; i < result.Length; i++) result[i] = values[i];
            return result;
        }

        private static string[] ToStringArray(IReadOnlyList<long> values)
        {
            var result = new string[values.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = values[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
            return result;
        }
    }
}

