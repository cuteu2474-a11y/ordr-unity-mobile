using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ordr.Mobile.Mobile;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Unity
{
    // 가로형 게임 HUD. Unity UI 패키지/추가 이미지 없이 동작하도록
    // OnGUI로 구현했으며, 원작 전투 규칙이나 경제 수치는 변경하지 않는다.
    // 스토리·조합확인·상점·도박·위습 버튼은 실제 사용자 탭에서만 호출된다.
    public sealed class OrdrMobileTouchPanel : MonoBehaviour
    {
        private enum Tab { Play, Units, Story, Fusion, Shop, Gambling, Wisp, LogPose, Helper, Alchemy, Missions, UpgradeOne, UpgradeTwo, Research }
        private enum ScoreboardPage { Summary, Commons }

        private struct FacilityVisual
        {
            internal readonly string Eyebrow;
            internal readonly string Heading;
            internal readonly string Description;
            internal readonly string Counter;
            internal readonly Color Tint;

            internal FacilityVisual(string eyebrow, string heading,
                string description, string counter, Color tint)
            {
                Eyebrow = eyebrow;
                Heading = heading;
                Description = description;
                Counter = counter;
                Tint = tint;
            }
        }

        [SerializeField] private OrdrCharacterCombatController controller;
        [SerializeField] private bool openByDefault;
        private bool visible;
        private Tab currentTab = Tab.Play;
        private ScoreboardPage scoreboardPage;
        private float nextFusionRefresh;
        private string fusionInventorySignature;
        private int selectedPracticeRounds = 65;
        private bool showUnitDetails;
        private long? expandedOwnedUnitId;
        private string expandedOwnedGroupId;
        private int expandedOwnedGroupPage;
        private string expandedRecipeId;
        private bool showChoiceWisp;
        private bool showRandomWispActions = true;
        private readonly HashSet<string> expandedResearchCodes =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<int> expandedMissionSlots = new HashSet<int>();
        private bool showStoryDispatch;
        private bool showOriginalAudit;
        private readonly Dictionary<Tab, Vector2> tabScrollPositions = new Dictionary<Tab, Vector2>();
        private int ownedPage;
        private OwnedRosterFilter ownedFilter = OwnedRosterFilter.All;
        private bool showRoundHistory;
        private readonly List<long> pendingBatchSales = new List<long>();
        private bool confirmBatchSale;
        private readonly MobileWispActionJournal wispActions = new MobileWispActionJournal();
        private bool showWispHistory;
        private MobileTreasureGachaResult lastTreasureResult;
        private int lastTreasureRound;
        private long? pendingCommonSaleId;
        private string pendingCommonSaleName;
        private int pendingMissionReroll = -1;
        private string selectedCatalogUnitId;
        private Vector2 scroll;
        private Vector2 tabsScroll;
        private Vector2 dashboardScroll;
        private readonly MobilePanelScrollGesture panelScrollGesture = new MobilePanelScrollGesture();
        private Rect panelScrollViewport;
        private float panelScrollMaximum;
        private Vector2 noticeScroll;
        private bool confirmReturnToTitle;
        private bool showFullNotice;
        private string lastNotice;
        private Vector2 dedicatedScroll;
        private string dedicatedNotice;
        private string notice = "";
        private IReadOnlyList<MobileFusionPreview> fusionOffers =
            new List<MobileFusionPreview>().AsReadOnly();
        private IReadOnlyList<MobileManualOffer> economyOffers =
            new List<MobileManualOffer>().AsReadOnly();
        private MobileFusionPreview pendingFusion;
        private readonly MobileFusionBookmarks fusionBookmarks = new MobileFusionBookmarks();
        private readonly MobileFusionGoalTracker fusionGoal = new MobileFusionGoalTracker();
        private bool showOnlyBookmarkedFusion;
        private int fusionRepeatCount = 1;
        // The catalog has hundreds of recipes; only draw one phone-sized page.
        // These controls never authorize the original fusion transaction.
        private string fusionSearch = "";
        private bool fusionMaterialsReadyOnly;
        private bool fusionExecutableOnly;
        private int fusionPage;
        private int originalJassPage;
        private string routeId;
        private IReadOnlyList<OriginalJassFusionDependencyStep> routeSteps =
            new OriginalJassFusionDependencyStep[0];
        private const int FusionPageSize = 8;
        private const int OriginalJassPageSize = 10;
        private MobileManualOffer pendingEconomy;
        private long? pendingDecompositionId;
        private string pendingDecompositionName;
        private bool pendingMaximumOutput;
        private bool pendingAlchemy;
        private bool alchemyTargetsExpanded;
        private long? selectedAlchemyTargetId;
        private long? pendingAlchemyCastId;
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

        // Navigation only. Opening a drawer never spends or summons anything.
        public void OpenRecruitment()
        {
            if (visible && currentTab == Tab.Wisp) return;
            SelectTab(Tab.Wisp);
        }

        private void Update()
        {
            if (Combat == null ||
                !((visible && (currentTab == Tab.Play || currentTab == Tab.Fusion)) ||
                  fusionGoal.HasGoal) ||
                Time.unscaledTime < nextFusionRefresh) return;
            nextFusionRefresh = Time.unscaledTime + 0.5f;
            var owned = Combat.MobileUnitSnapshot();
            var signature = new System.Text.StringBuilder();
            signature.Append(Combat.Game.Phase);
            signature.Append(':').Append(controller.SharedEconomy.Wallet.Balance("wood"));
            foreach (var unit in owned)
                signature.Append('|').Append(unit.InstanceId).Append(':').Append(unit.Unit.Id)
                    .Append(':').Append(Combat.IsStoryDeployed(unit.InstanceId));
            string value = signature.ToString();
            if (value == fusionInventorySignature) return;
            fusionInventorySignature = value;
            RefreshCurrent();
        }

        public bool BlocksWorldTap(Vector2 onGui)
        {
            var shell = GetComponent<OrdrMainSceneShell>();
            if (shell != null && shell.IsStartScreenVisible) return true;
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
        private void SelectTab(Tab selected, bool restoreScroll = false)
        {
            if (visible && currentTab == selected)
            {
                CloseDrawer();
                return;
            }
            if (visible) tabScrollPositions[currentTab] = scroll;
            panelScrollGesture.Cancel();
            currentTab = selected;
            visible = true;
            if (selected == Tab.Play) scoreboardPage = ScoreboardPage.Summary;
            Vector2 remembered;
            scroll = restoreScroll && tabScrollPositions.TryGetValue(selected, out remembered)
                ? remembered : Vector2.zero;
            confirmReturnToTitle = false;
            pendingFusion = null;
            pendingEconomy = null;
            pendingMaximumOutput = false;
            pendingAlchemy = false;
            alchemyTargetsExpanded = false;
            selectedAlchemyTargetId = null;
            pendingAlchemyCastId = null;
            pendingCommonSaleId = null;
            pendingCommonSaleName = null;
            pendingDecompositionId = null;
            pendingDecompositionName = null;
            notice = "";
            pendingMissionReroll = -1;
            RefreshCurrent();
        }

        private void CloseDrawer()
        {
            panelScrollGesture.Cancel();
            tabScrollPositions[currentTab] = scroll;
            visible = false;
            confirmReturnToTitle = false;
            pendingFusion = null;
            pendingEconomy = null;
            pendingMaximumOutput = false;
            pendingAlchemy = false;
            alchemyTargetsExpanded = false;
            selectedAlchemyTargetId = null;
            pendingAlchemyCastId = null;
            pendingCommonSaleId = null;
            pendingCommonSaleName = null;
            pendingDecompositionId = null;
            pendingDecompositionName = null;
            pendingMissionReroll = -1;
        }

        private void OnGUI()
        {
            if (controller == null) return;
            var shell = GetComponent<OrdrMainSceneShell>();
            if (shell != null && shell.IsStartScreenVisible) return;
            Rect safe = Screen.safeArea;
            if (safe.width <= safe.height) return;

            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
            float scale = MobileMapSkillLayout.UiScale(safe, dpi);
            var layout = MobileMapSkillLayout.Calculate(safe, Screen.height, scale);
            if (safe.width / scale < 120f || safe.height / scale < 100f) return;

            HandlePanelScroll(scale);
            Matrix4x4 previous = GUI.matrix;
            GUISkin previousSkin = OrdrMobileUiTheme.Activate();
            try
            {
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                DrawLeftTabs(layout, scale);
                if (visible)
                    DrawRightDrawer(layout, scale);
                else
                    DrawStandbyDashboard(layout, scale);

                // Leave the central map render and world taps untouched.
                DrawDedicatedSkillMargin(safe, scale);
                DrawEconomyStatus(layout, scale);
            }
            finally
            {
                GUI.matrix = previous;
                GUI.skin = previousSkin;
            }
        }

        private void HandlePanelScroll(float scale)
        {
            var e = Event.current;
            Vector2 pointer = e.mousePosition / scale;
            if (e.type == EventType.MouseDown && e.button == 0)
                panelScrollGesture.Begin(pointer, panelScrollViewport,
                    visible ? scroll.y : dashboardScroll.y);
            else if (e.type == EventType.MouseDrag && e.button == 0)
            {
                float value = visible ? scroll.y : dashboardScroll.y;
                if (panelScrollGesture.Move(pointer, panelScrollMaximum, ref value))
                {
                    if (visible) scroll.y = value; else dashboardScroll.y = value;
                    // Cancel the button/text control that received MouseDown.
                    GUIUtility.hotControl = 0;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && panelScrollGesture.End())
            {
                GUIUtility.hotControl = 0;
                e.Use();
            }
        }

        private static Vector2 BeginPanelScroll(Vector2 value)
        {
            value = GUILayout.BeginScrollView(value, false, true, GUILayout.ExpandHeight(true));
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            return value;
        }

        private void EndPanelScroll(Rect area)
        {
            GUILayout.EndVertical();
            Rect content = GUILayoutUtility.GetLastRect();
            GUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint)
            {
                Rect viewport = GUILayoutUtility.GetLastRect();
                panelScrollViewport = new Rect(area.x + viewport.x, area.y + viewport.y,
                    viewport.width, viewport.height);
                panelScrollMaximum = Mathf.Max(0f, content.height - viewport.height);
            }
        }

        private void OnDisable() { panelScrollGesture.Cancel(); }
        private void OnApplicationFocus(bool focused)
        {
            if (!focused) panelScrollGesture.Cancel();
        }
        private void OnApplicationPause(bool paused)
        {
            if (paused) panelScrollGesture.Cancel();
        }

        private void DrawEconomyStatus(MobileMapSkillLayout layout, float scale)
        {
            Rect area=MobileMapSkillLayout.Scaled(layout.StatusMargin,scale);
            if(area.width<100f||area.height<22f)return;
            OrdrMobileUiTheme.DrawHeader(area);
            float stripWidth = Mathf.Min(900f, area.width - 12f);
            area = new Rect(area.xMax - stripWidth - 6f, area.y, stripWidth, area.height);
            var economy=controller.SharedEconomy;
            var chips=MobileMapSkillLayout.StatusChipRects(area, 5);
            OrdrMobileUiTheme.ResourceChip(chips[0],"골드",
                economy==null?"--":economy.Wallet.Balance("gold").ToString(),OrdrMobileUiTheme.Gold);
            OrdrMobileUiTheme.ResourceChip(chips[1],"목재",
                economy==null?"--":economy.Wallet.Balance("wood").ToString(),OrdrMobileUiTheme.Aqua);
            OrdrMobileUiTheme.ResourceChip(chips[2],"위습",
                economy==null?"--":economy.Wallet.Balance(WispResourceIds.Random).ToString(),
                new Color(.77f,.63f,1f,1f));
            OrdrMobileUiTheme.ResourceChip(chips[3], "선택위습",
                economy == null ? "--" : economy.Wallet.Balance(WispResourceIds.Choice).ToString(),
                new Color(.62f, .83f, 1f, 1f));
            OrdrMobileUiTheme.ResourceChip(chips[4], "특성포인트",
                economy == null ? "--" : economy.Wallet.Balance(OriginalMissionWalletRewards.TraitPointsId).ToString(),
                new Color(1f, .65f, .38f, 1f));
        }

        private void DrawLeftTabs(MobileMapSkillLayout layout, float scale)
        {
            Rect area = MobileMapSkillLayout.Scaled(layout.LeftTabs, scale);
            GUILayout.BeginArea(area, OrdrMobileUiTheme.Panel);
            // Use touch/trackpad scrolling without the large default Unity
            // scrollbar stealing width from Korean navigation labels.
            tabsScroll = GUILayout.BeginScrollView(tabsScroll, false, false,
                GUIStyle.none, GUIStyle.none, GUILayout.ExpandHeight(true));

            DrawLeftTab(Tab.Play, "점수판");
            DrawLeftTab(Tab.Wisp, "위습");
            GUILayout.Space(5f);
            GUILayout.Label("편성",OrdrMobileUiTheme.Overline);
            DrawLeftTab(Tab.Units, "유닛");
            DrawLeftTab(Tab.Fusion, "조합");
            DrawLeftTab(Tab.Story, "스토리");
            GUILayout.Space(5f);
            GUILayout.Label("운영",OrdrMobileUiTheme.Overline);
            // Original player buildings are DIRECT left-menu entries; no
            // extra facility submenu or duplicate Help/Mission/Log tabs.
            DrawLeftTab(Tab.Gambling, "도박소");
            DrawLeftTab(Tab.UpgradeOne, "강화소 1");
            DrawLeftTab(Tab.UpgradeTwo, "강화소 2");
            DrawLeftTab(Tab.Helper, "도움소");
            DrawLeftTab(Tab.Missions, "미션");
            DrawLeftTab(Tab.Research, "연구소");
            DrawLeftTab(Tab.LogPose, "기록지침");
            DrawLeftTab(Tab.Shop, "상점");
            if (controller.Original323HelperCow != null &&
                controller.Original323HelperCow.AlchemyNavigationChosen)
                DrawLeftTab(Tab.Alchemy, "연금술");

            GUILayout.EndScrollView();
            if (Combat != null && Combat.Game.Phase == SessionPhase.Running && !controller.IsUnitPreparation)
            {
                GUILayout.Space(8f);
                if (GUILayout.Button(controller.IsGameplayPaused ? "계속" : "일시정지",
                    GUILayout.MinHeight(56)))
                    controller.SetManualGameplayPause(!controller.IsGameplayPaused);
            }
            GUILayout.EndArea();
        }

        private void DrawLeftTab(Tab tab, string title)
        {
            bool chosen = visible && currentTab == tab;
            GUIStyle style = chosen ? OrdrMobileUiTheme.SelectedTab :
                OrdrMobileUiTheme.NavButton;
            if (GUILayout.Button(title, style,
                GUILayout.MinHeight(OrdrMobileUiTheme.ComfortableTapHeight),
                GUILayout.ExpandWidth(true)))
                SelectTab(tab, true);
            if (Event.current.type == EventType.Repaint)
            {
                FacilityVisual facility;
                Color tint = TryGetFacilityVisual(tab, out facility) ?
                    facility.Tint : OrdrMobileUiTheme.Aqua;
                OrdrMobileUiTheme.DrawNavigationMark(GUILayoutUtility.GetLastRect(),
                    tint, chosen);
            }
        }

        // Purely visual metadata for the seven direct left-menu buildings.
        // Does not add a facility tab, an economy action or a research effect.
        private static bool TryGetFacilityVisual(Tab tab, out FacilityVisual visual)
        {
            switch (tab)
            {
                case Tab.Gambling:
                    visual = new FacilityVisual("LUCK / 도박소", "운명의 항구",
                        "보유 재화와 원본 도박 목록을 확인하세요.", "도박 운영",
                        new Color(1f,.68f,.35f));
                    return true;
                case Tab.UpgradeOne:
                    visual = new FacilityVisual("FORGE I / 강화소", "전투 단련",
                        "등급별 유닛을 위한 업그레이드 장부입니다.", "8종 연구",
                        new Color(1f,.69f,.38f));
                    return true;
                case Tab.UpgradeTwo:
                    visual = new FacilityVisual("FORGE II / 강화소", "영원의 단련",
                        "영원 등급 유닛의 개별 연구 목록입니다.", "10종 연구",
                        new Color(1f,.77f,.42f));
                    return true;
                case Tab.Helper:
                    visual = new FacilityVisual("SUPPORT / 도움소", "항해 지원",
                        "항법 선택과 지원 기능을 확인하세요.", "지원 운영",
                        new Color(.41f,.9f,.76f));
                    return true;
                case Tab.Missions:
                    visual = new FacilityVisual("QUEST / 미션", "의뢰 게시판",
                        "진행 중인 개인 미션과 보상을 살펴보세요.", "임무 운영",
                        new Color(.43f,.78f,1f));
                    return true;
                case Tab.Research:
                    visual = new FacilityVisual("LAB / 연구소", "전술 연구",
                        "체력·마나 재생과 전장 지원을 연구합니다.", "4종 연구",
                        new Color(.44f,.88f,.96f));
                    return true;
                case Tab.LogPose:
                    visual = new FacilityVisual("EXPLORE / 기록지침", "탐험 기록",
                        "기록지침의 보물 탐색을 이용하세요.", "탐험 운영",
                        new Color(.76f,.67f,1f));
                    return true;
                default:
                    visual = default(FacilityVisual);
                    return false;
            }
        }

        private void DrawRightDrawer(MobileMapSkillLayout layout, float scale)
        {
            Rect area = MobileMapSkillLayout.Scaled(layout.RightDrawer, scale);
            GUILayout.BeginArea(area, OrdrMobileUiTheme.Panel);
            OrdrMobileUiTheme.DrawPanelAccent(new Rect(0,0,area.width,area.height),
                OrdrMobileUiTheme.Gold);
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            // The chat scoreboard uses its own page selector; a second large
            // '점수판' heading wastes the phone-sized right drawer.
            if (currentTab != Tab.Play)
                GUILayout.Label(TabTitle(currentTab), OrdrMobileUiTheme.SectionTitle,
                    GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            else GUILayout.FlexibleSpace();
            if (GUILayout.Button("닫기", GUILayout.Width(64f), GUILayout.MinHeight(52f)))
                CloseDrawer();
            GUILayout.EndHorizontal();

            scroll = BeginPanelScroll(scroll);
            FacilityVisual facilityVisual;
            if (TryGetFacilityVisual(currentTab, out facilityVisual))
            {
                Rect banner = GUILayoutUtility.GetRect(0f, 94f,
                    GUILayout.ExpandWidth(true));
                OrdrMobileUiTheme.DrawFacilityBanner(banner,
                    facilityVisual.Eyebrow, facilityVisual.Heading,
                    facilityVisual.Description, facilityVisual.Counter,
                    facilityVisual.Tint);
                GUILayout.Space(12f);
            }
            if (Combat == null && currentTab != Tab.Units)
                GUILayout.Label("게임 월드 연결 전입니다. 재화나 유닛을 임의로 처리하지 않습니다.");
            else
            {
                switch (currentTab)
                {
                    case Tab.Play: DrawPlay(); break;
                    case Tab.Units: DrawUnits(); break;
                    case Tab.Story: DrawStory(); break;
                    case Tab.Fusion: DrawFusion(); break;
                    case Tab.Shop: DrawEconomy(ManualEconomyCategory.Shop); break;
                    case Tab.Gambling: DrawEconomy(ManualEconomyCategory.Gambling); break;
                    case Tab.Wisp: DrawWisp(); break;
                    case Tab.LogPose: DrawTreasureGacha(); break;
                    case Tab.Helper: DrawHelperCow(); break;
                    case Tab.Alchemy: DrawAlchemyPanel(); break;
                    case Tab.Missions: DrawPersonalMissions(); break;
                    case Tab.UpgradeOne: DrawPlayerBuildingResearch(
                        Original323PlayerBuildingResearchCatalog.FirstUpgradeBuilding); break;
                    case Tab.UpgradeTwo: DrawPlayerBuildingResearch(
                        Original323PlayerBuildingResearchCatalog.SecondUpgradeBuilding); break;
                    case Tab.Research: DrawPlayerBuildingResearch(
                        Original323PlayerBuildingResearchCatalog.ResearchBuilding); break;
                }
            }
            EndPanelScroll(area);
            if (!string.IsNullOrWhiteSpace(notice))
            {
                if (!string.Equals(lastNotice, notice, StringComparison.Ordinal))
                {
                    lastNotice = notice;
                    showFullNotice = false;
                    noticeScroll = Vector2.zero;
                }
                GUILayout.BeginVertical(GUI.skin.box);
                float noticeWidth = Mathf.Max(60f, area.width - 48f);
                bool longNotice = GUI.skin.label.CalcHeight(new GUIContent(notice), noticeWidth) > 48f;
                GUILayout.BeginHorizontal();
                GUILayout.Label(notice, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true),
                    GUILayout.MinHeight(24f), GUILayout.MaxHeight(48f));
                if (longNotice && GUILayout.Button(showFullNotice ? "접기" : "전체",
                    GUILayout.Width(64f), GUILayout.MinHeight(48f)))
                    showFullNotice = !showFullNotice;
                GUILayout.EndHorizontal();
                if (showFullNotice && longNotice)
                {
                    noticeScroll = GUILayout.BeginScrollView(noticeScroll, GUILayout.Height(86f));
                    GUILayout.Label(notice);
                    GUILayout.EndScrollView();
                }
                GUILayout.EndVertical();
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        // Read-only tactics dashboard shown when the detail pane is closed.
        // It contains battle state only; navigation stays in the left rail.
        private void DrawStandbyDashboard(MobileMapSkillLayout layout,float scale)
        {
            Rect area = MobileMapSkillLayout.Scaled(layout.RightDrawer, scale);
            GUILayout.BeginArea(area, OrdrMobileUiTheme.Panel);
            dashboardScroll = BeginPanelScroll(dashboardScroll);
            if (Combat == null)
                GUILayout.Label("전투 대기");
            else
            {
                var game = Combat.Game;
                GUILayout.Label(controller.IsUnitPreparation ? "출격 준비" : "현재 전황",
                    OrdrMobileUiTheme.SectionTitle);
                OrdrMobileUiTheme.DrawStatusRow("라운드",
                    game.CurrentWave + "R", OrdrMobileUiTheme.Gold);
                OrdrMobileUiTheme.DrawStatusRow("전투 유닛",
                    Combat.CharacterCount + "기", OrdrMobileUiTheme.Aqua);
                OrdrMobileUiTheme.DrawStatusRow("남은 적",
                    game.AliveEnemies + "기", new Color(1f,.56f,.47f));
                if (controller.OriginalTreasureHunt != null)
                    OrdrMobileUiTheme.DrawStatusRow("남은 보물",
                        controller.OriginalTreasureHunt.UncollectedCount + "개",
                        OrdrMobileUiTheme.Gold);
                if (controller.PracticeBattle != null)
                {
                    OrdrMobileUiTheme.DrawStatusRow("남은 시간",
                        Math.Ceiling(controller.PracticeBattle.SecondsRemaining) + "초",
                        OrdrMobileUiTheme.Gold);
                    OrdrMobileUiTheme.DrawStatusRow("처치",
                        game.DefeatedEnemies + "기", OrdrMobileUiTheme.Aqua);
                }
            }
            EndPanelScroll(area);
            GUILayout.EndArea();
        }

        private void DrawFusionGoalStatus()
        {
            if (!fusionGoal.HasGoal) return;
            var tracked = fusionGoal.Resolve(fusionOffers,
                controller.ManualActions.CanExecuteFusionRecipe);
            if (tracked == null) return;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("목표 조합", OrdrMobileUiTheme.Overline);
            GUILayout.Label(tracked.Title, OrdrMobileUiTheme.Subheading);
            GUILayout.Label(tracked.Status, OrdrMobileUiTheme.Explanation);
            GUILayout.Label(tracked.MissingSummary(), OrdrMobileUiTheme.Explanation);
            if (GUILayout.Button("목표 해제", GUILayout.MinHeight(60f))) fusionGoal.Clear();
            GUILayout.EndVertical();
        }

        private static string TabTitle(Tab tab)
        {
            switch (tab)
            {
                case Tab.Play: return "점수판";
                case Tab.Wisp: return "위습";
                case Tab.LogPose: return "기록지침";
                case Tab.Helper: return "도움소";
                case Tab.Alchemy: return "연금술";
                case Tab.Units: return "유닛";
                case Tab.Fusion: return "조합";
                case Tab.Story: return "스토리";
                case Tab.Shop: return "상점";
                case Tab.Gambling: return "도박소";
                case Tab.Missions: return "개인 미션";
                case Tab.UpgradeOne: return "강화소 1";
                case Tab.UpgradeTwo: return "강화소 2";
                case Tab.Research: return "연구소";
                default: throw new ArgumentOutOfRangeException(nameof(tab));
            }
        }

        private void DrawPlayerBuildingResearch(string buildingRawcode)
        {
            var original = Original323PlayerBuildingResearchCatalog.ForBuilding(buildingRawcode);
            OrdrMobileUiTheme.DrawStatusRow("연구 목록",
                original.Count + "종 · 구매 준비 중", OrdrMobileUiTheme.Gold);
            GUILayout.Space(8f);
            Color tint = buildingRawcode == Original323PlayerBuildingResearchCatalog.ResearchBuilding
                ? new Color(.44f,.88f,.96f) : new Color(1f,.73f,.39f);
            for (int i = 0; i < original.Count; i++)
            {
                var research = original[i];
                string key = buildingRawcode + ":" + research.Rawcode;
                bool expanded = expandedResearchCodes.Contains(key);
                if (OrdrMobileUiTheme.DrawAccordionHeader(
                    (i + 1).ToString("00") + "  " + research.Name,
                    "최대 " + research.MaximumLevel + "단계", expanded, tint))
                {
                    if (expanded) expandedResearchCodes.Remove(key);
                    else expandedResearchCodes.Add(key);
                    expanded = !expanded;
                }
                if (!expanded) continue;
                GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
                Rect plate = GUILayoutUtility.GetRect(0f, 138f,
                    GUILayout.ExpandWidth(true));
                OrdrMobileUiTheme.DrawResearchPlate(plate, i + 1, research.Name,
                    string.IsNullOrEmpty(research.EffectSummary)
                        ? "원본 등급 연구 · 실제 효과 연결 예정" : research.EffectSummary,
                    research.MaximumLevel, tint);
                GUILayout.EndVertical();
            }
        }

        // Keep only the useful personal stats and confirmed common-wisp draws.
        // Original + / - breakdown and final score tracking are intentionally
        // out of scope for the mobile remake at the player's request.


        private void DrawOriginalChatScoreboard()
        {
            GUILayout.BeginHorizontal();
            DrawScoreboardPageButton(ScoreboardPage.Summary, "개인 기록");
            DrawScoreboardPageButton(ScoreboardPage.Commons, "흔함 획득");
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);

            switch (scoreboardPage)
            {
                case ScoreboardPage.Summary: DrawOriginalChatScoreSummary(); break;
                case ScoreboardPage.Commons: DrawOriginalChatCommonDraws(); break;
            }
        }

        private void DrawScoreboardPageButton(ScoreboardPage page, string label)
        {
            if (GUILayout.Button(label + (scoreboardPage == page ? " ✓" : ""),
                scoreboardPage == page ? OrdrMobileUiTheme.SelectedTab : GUI.skin.button,
                GUILayout.MinHeight(54f)))
            {
                scoreboardPage = page;
                scroll = Vector2.zero;
            }
        }

        private void DrawScoreMetric(string label, string value, Color highlight)
        {
            OrdrMobileUiTheme.DrawStatusRow(label, value, highlight, 68f);
        }

        private void DrawOriginalChatScoreSummary()
        {
            var gold = OrdrMobileUiTheme.Gold;
            var aqua = OrdrMobileUiTheme.Aqua;
            DrawScoreMetric("전투", controller.IsUnitPreparation ? "출격 준비" :
                controller.PracticeBattle != null ? selectedPracticeRounds + "R 연습" :
                "원본 세션", gold);
            if (controller.Original323HelperNavigation != null &&
                controller.Original323HelperNavigation.ChosenNavigation != null)
                DrawScoreMetric("항법",
                    controller.Original323HelperNavigation.ChosenNavigation, aqua);
            if (controller.OriginalTreasureHunt != null)
                DrawScoreMetric("보물 수집",
                    controller.OriginalTreasureHunt.CollectedCount + "개", aqua);
            if (controller.PracticeMoneyGambling != null)
                DrawScoreMetric("일반 돈 도박",
                    controller.PracticeMoneyGambling.PurchaseCount + "회", gold);
        }

        private void DrawOriginalChatCommonDraws()
        {
            var journal = controller.ChatScoreboard;
            DrawScoreMetric("흔함 획득",
                journal.ConfirmedCommonDraws + "기", OrdrMobileUiTheme.Gold);
            for (int i = 0; i < MobileOriginalChatScoreboard.CommonCatalogIds.Length; i++)
                OrdrMobileUiTheme.DrawStatusRow(
                    MobileOriginalChatScoreboard.CommonNames[i],
                    journal.CommonDrawCount(
                        MobileOriginalChatScoreboard.CommonCatalogIds[i]) + "기",
                    OrdrMobileUiTheme.Aqua);
        }

        private void DrawPlay()
        {
            if (Combat == null || controller.SharedEconomy == null)
            {
                GUILayout.Label("전투 대기");
                return;
            }
            DrawOriginalChatScoreboard();
            // Non-summary pages are read-only record pages. Preparation/start
            // and restart actions remain on the main scoreboard page.
            if (scoreboardPage != ScoreboardPage.Summary) return;
            GUILayout.Space(10f);
            var game = Combat.Game;
            var practice = controller.PracticeBattle;
            OrdrMobileUiTheme.DrawStatusRow("전투 유닛",
                Combat.CharacterCount + "기", OrdrMobileUiTheme.Aqua);
            OrdrMobileUiTheme.DrawStatusRow("공격 준비",
                Combat.ReadyAttackers + "기", OrdrMobileUiTheme.Gold);
            if (controller.IsUnitPreparation)
            {
                bool previous = GUI.enabled;
                GUI.enabled = previous && Combat.CharacterCount > 0;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(selectedPracticeRounds == 10 ? "10R ✓" : "10R", GUILayout.MinHeight(54))) selectedPracticeRounds = 10;
                if (GUILayout.Button(selectedPracticeRounds == 20 ? "20R ✓" : "20R", GUILayout.MinHeight(54))) selectedPracticeRounds = 20;
                if (GUILayout.Button(selectedPracticeRounds == 65 ? "65R ✓" : "65R", GUILayout.MinHeight(54))) selectedPracticeRounds = 65;
                GUILayout.EndHorizontal();
                if (GUILayout.Button("준비 완료 · " + selectedPracticeRounds + "R 전투 시작",
                    OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(64)))
                {
                    try { controller.BeginPracticeBattle(selectedPracticeRounds); notice = "전투 시작 · 소환과 조합은 계속할 수 있습니다."; }
                    catch (Exception error) { notice = error.Message; }
                }
                GUI.enabled = previous;
            }
            if (practice != null)
            {
                OrdrMobileUiTheme.DrawStatusRow("남은 시간",
                    Math.Ceiling(practice.SecondsRemaining) + "초", OrdrMobileUiTheme.Gold);
                OrdrMobileUiTheme.DrawStatusRow("남은 적",
                    game.AliveEnemies + "/" + practice.EnemyLimit,
                    OrdrMobileUiTheme.Aqua);
                OrdrMobileUiTheme.DrawStatusRow("처치",
                    game.DefeatedEnemies + "기", OrdrMobileUiTheme.Gold);
                GUILayout.Label("골드 +" + practice.GoldAwarded +
                    " · 위습 +" + practice.RoundWispsAwarded);
                foreach (var enemy in Combat.MobileEnemySnapshot())
                    if (enemy.Kind == CharacterEnemyKind.Boss)
                        GUILayout.Label("보스 HP " + enemy.Health + " / " + enemy.MaxHealth);
                if (Combat.PracticeUnitEffects != null && Combat.PracticeUnitEffects.DeferredAuraGroups > 0)
                    OrdrMobileUiTheme.DrawStatusRow("오라",
                        "처리 대기", OrdrMobileUiTheme.Gold);
            }
            if (GUILayout.Button(confirmReturnToTitle ? "확인 · 시작화면으로" : "시작화면으로",
                GUILayout.MinHeight(56)))
            {
                if (confirmReturnToTitle)
                    UnityEngine.SceneManagement.SceneManager.LoadScene(
                        UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                else confirmReturnToTitle = true;
            }
            if (confirmReturnToTitle && GUILayout.Button("취소", GUILayout.MinHeight(52)))
                confirmReturnToTitle = false;
            if (practice != null && practice.RoundHistory.Count > 0)
            {
                if (GUILayout.Button(showRoundHistory ? "라운드 기록 닫기 ▲" :
                    "라운드 기록 " + practice.RoundHistory.Count + "건 보기 ▼", GUILayout.MinHeight(54)))
                    showRoundHistory = !showRoundHistory;
                if (showRoundHistory)
                    foreach (var round in practice.RoundHistory)
                        GUILayout.Label(round.Wave + "R · " +
                            Math.Round(round.ElapsedSeconds, 1) + "초 · 처치 " +
                            round.Defeated + " · 골드 +" + round.GoldAwarded +
                            " · 남은 적 " + round.AliveEnemies);
            }
        }

        private void DrawPersonalMissions()
        {
            var gameplay = controller.PersonalMissions;
            if (gameplay == null)
            {
                GUILayout.Label("게임 시작 시 개인 미션 3개가 배정됩니다.");
                return;
            }
            var missions = gameplay.Missions;
            OrdrMobileUiTheme.DrawStatusRow("미션 교체",
                missions.HasRerolled ? "사용 완료" :
                missions.CurrentRound >= 10 ? "기간 종료" : "1회 가능",
                OrdrMobileUiTheme.Gold);
            bool hasPendingReward = false;
            foreach (var slot in missions.Slots)
            {
                hasPendingReward |= slot.RewardPending;
                var rule = slot.Mission;
                bool expired = !slot.Completed && !slot.RewardPending &&
                    rule.MustFinishBeforeRound > 0 && missions.CurrentRound >= rule.MustFinishBeforeRound;
                string state = slot.Completed ? "완료" :
                    slot.RewardPending ? "보상 지급 대기" :
                    expired ? "기한 종료" :
                    missions.ProgressAt(slot.Index) + "/" + rule.RequiredCount;
                bool expanded = expandedMissionSlots.Contains(slot.Index);
                if (OrdrMobileUiTheme.DrawAccordionHeader(
                    (slot.Index + 1) + ". " + rule.Name,
                    state, expanded, OrdrMobileUiTheme.Gold))
                {
                    if (expanded) expandedMissionSlots.Remove(slot.Index);
                    else expandedMissionSlots.Add(slot.Index);
                    expanded = !expanded;
                }
                if (!expanded) continue;
                GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
                GUILayout.Label(rule.Objective, OrdrMobileUiTheme.Subheading);
                OrdrMobileUiTheme.DrawStatusRow("진행",
                    missions.ProgressAt(slot.Index) + " / " + rule.RequiredCount,
                    OrdrMobileUiTheme.Aqua);
                if (rule.AllGambleTiersRequired)
                    GUILayout.Label("하급 " + missions.EventCount(OriginalMissionSignal.LowGambleAttempted) +
                        "  /  중급 " + missions.EventCount(OriginalMissionSignal.MidGambleAttempted) +
                        "  /  고급 " + missions.EventCount(OriginalMissionSignal.HighGambleAttempted));
                GUILayout.Label("보상: " + MissionRewardText(rule.Rewards));
                if (rule.CheckAtRoundEnd && !slot.Completed && !slot.RewardPending &&
                    missions.ProgressAt(slot.Index) >= rule.RequiredCount)
                    GUILayout.Label("목표 달성 · 라운드 종료 시 지급");
                if (pendingMissionReroll == slot.Index)
                {
                    GUILayout.Label("이 미션을 다른 미션으로 교체하시겠습니까?");
                    bool oldEnabled = GUI.enabled;
                    GUI.enabled = oldEnabled && gameplay.CanReroll(slot.Index);
                    bool confirm = GUILayout.Button("미션 교체 확정", GUILayout.MinHeight(56f));
                    GUI.enabled = oldEnabled;
                    if (confirm)
                    {
                        notice = gameplay.TryReroll(slot.Index) ? "미션을 교체했습니다." :
                            "현재 미션 교체 조건을 충족하지 않습니다.";
                        pendingMissionReroll = -1;
                    }
                    if (GUILayout.Button("취소", GUILayout.MinHeight(66f))) pendingMissionReroll = -1;
                }
                else if (gameplay.CanReroll(slot.Index) &&
                    GUILayout.Button("이 미션 교체", GUILayout.MinHeight(56f)))
                {
                    pendingMissionReroll = slot.Index;
                    expandedMissionSlots.Add(slot.Index);
                }
                GUILayout.EndVertical();
            }
            OrdrMobileUiTheme.DrawStatusRow("특성포인트",
                controller.SharedEconomy.Wallet.Balance(
                    OriginalMissionWalletRewards.TraitPointsId).ToString(),
                OrdrMobileUiTheme.Gold);
            if (missions.AllThreeCompleted)
                GUILayout.Label("개인 미션 3개 완료 · 승리 시 추가 베리 1개 대상");
            if (hasPendingReward)
            {
                if (GUILayout.Button("미지급 보상 다시 처리", GUILayout.MinHeight(56f)))
                    gameplay.RetryRewards();
            }
        }

        private static string MissionRewardText(IReadOnlyList<OriginalMissionReward> rewards)
        {
            var labels = new List<string>();
            foreach (var reward in rewards)
            {
                string name;
                switch (reward.Kind)
                {
                    case OriginalMissionRewardKind.Lumber: name = "목재"; break;
                    case OriginalMissionRewardKind.TraitPoint: name = "특성포인트"; break;
                    case OriginalMissionRewardKind.AddAbilityLevel:
                        labels.Add("기록지침 와일드카드 +" + reward.Count + "단계"); continue;
                    case OriginalMissionRewardKind.SetAbilityLevel:
                        labels.Add("기록지침 로얄로더 " + reward.Count + "단계"); continue;
                    case OriginalMissionRewardKind.Item:
                        switch (reward.RawId)
                        {
                            case "e018": name = "선택위습"; break;
                            case "e0IX": name = "랜덤위습"; break;
                            case "e017": name = "안흔함위습"; break;
                            case "e019": name = "희귀함위습"; break;
                            default: name = "아이템"; break;
                        }
                        break;
                    default: throw new ArgumentOutOfRangeException();
                }
                labels.Add(name + " " + reward.Count + "개");
            }
            return string.Join(" · ", labels.ToArray());
        }

        // These are facility/unit-specific manual actions (e.g. record
        // instructions or Vivi), not Q/W/R combat buttons.
        // Buttons appear ONLY when a verified original action port supplies them.
        private void DrawDedicatedSkillMargin(Rect safe, float scale)
        {
            var layout = MobileMapSkillLayout.Calculate(safe, Screen.height, scale);
            Rect area = MobileMapSkillLayout.Scaled(layout.SkillMargin, scale);
            GUILayout.BeginArea(area, OrdrMobileUiTheme.Panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label("스킬",OrdrMobileUiTheme.Overline,GUILayout.Width(48f),
                GUILayout.MinHeight(Mathf.Max(40f, area.height - 9f)));

            var actions = controller.DedicatedSkills;
            if (!actions.IsConnected)
                GUILayout.Label("스킬 준비 중");
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
                            OrdrMobileUiTheme.SkillButton,GUILayout.Width(140f),
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

        // The game UI shows actual owned units only; catalog data remains in the engine.
        private void DrawUnits()
        {
            var definitions = controller.UnitDefinitions;
            if (definitions == null)
            {
                GUILayout.Label("유닛 정보를 불러오는 중입니다.");
                return;
            }
            GUILayout.Label("보유 유닛", OrdrMobileUiTheme.Subheading);
            // Keep details in sync with the current live roster selection.
            if (Combat == null || !Combat.Roster.SelectedId.HasValue)
            {
                selectedCatalogUnitId = null;
                showUnitDetails = false;
            }
            else
            {
                try
                {
                    var current = Combat.Roster.Get(Combat.Roster.SelectedId.Value);
                    selectedCatalogUnitId = current.UnitId == Original323SoloStartProfile.EvolutionRawcode
                        ? null : current.UnitId;
                }
                catch (KeyNotFoundException)
                {
                    selectedCatalogUnitId = null;
                    showUnitDetails = false;
                }
            }
            var detail = definitions.Find(selectedCatalogUnitId);
            if (showUnitDetails && detail != null)
            {
                if (GUILayout.Button("목록으로", GUILayout.MinHeight(56)))
                {
                    showUnitDetails = false;
                    scroll = Vector2.zero;
                }
                DrawUnitDetails(detail);
                return;
            }
            DrawStartingEvolutionSelection();
            if (detail != null)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("선택: " + detail.DisplayName, OrdrMobileUiTheme.Subheading);
                if (GUILayout.Button("스킬·공격 정보", GUILayout.MinHeight(56)))
                {
                    showUnitDetails = true;
                    scroll = Vector2.zero;
                }
                GUILayout.EndVertical();
            }
            DrawOwnedUnitIndex();
        }

        private void DrawStartingEvolutionSelection()
        {
            if (Combat == null || controller.SharedEconomy == null) return;
            var state = controller.EvolutionProgress;
            if (state == null) return;
            if (!state.IsActivated)
            {
                OrdrMobileUiTheme.DrawStatusRow("진화체", "자동 활성화 대기",
                    OrdrMobileUiTheme.Aqua);
                return;
            }

            var dormant = controller.Units.Find(state.DormantSpecialCatalogId);
            string title = dormant == null ? "진화체" : dormant.Name;
            if (state.IsReleased)
            {
                if (state.ReleasedUnitId.HasValue &&
                    OrdrMobileUiTheme.DrawActionRow(title, "해방 완료",
                        OrdrMobileUiTheme.Gold, 72f))
                {
                    try
                    {
                        Combat.Roster.Select(state.ReleasedUnitId.Value);
                        selectedCatalogUnitId = state.DormantSpecialCatalogId;
                        notice = title + " 선택";
                    }
                    catch (KeyNotFoundException)
                    { notice = "해방 유닛이 이미 제거됐습니다."; }
                }
                return;
            }
            string status = state.IsUnsealing ?
                "해방 중 " + Math.Max(0,
                    state.UnsealStartTime + Original323EvolutionSourceRules.UnsealSeconds -
                    Combat.Time).ToString("0.0") + "초" :
                state.LineDefeats + " / " + Original323EvolutionSourceRules.RequiredLineKills;
            OrdrMobileUiTheme.DrawStatusRow(title + " 진화체",
                status, OrdrMobileUiTheme.Aqua);
        }

        private void DrawUnitDetails(UnitImplementationDefinition detail)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            DrawCharacterPortrait(detail.Unit.Id, 112f);
            GUILayout.Label(detail.DisplayName + " · " + detail.Grade);
            if (controller.PracticeBattle != null)
            {
                var native = controller.MapBaseStats.Find(detail.Unit.Id);
                OrdrMobileUiTheme.DrawStatusRow("기본 공격",
                    native?.Attack1 != null ? "연결" : "정보 없음",
                    OrdrMobileUiTheme.Aqua);
                var effects = Combat.PracticeUnitEffects;
                if (effects != null)
                {
                    var connected = effects.Describe(detail.Unit.Id, native?.OriginalRawcode);
                    foreach (string text in connected)
                        GUILayout.Label("· " + text);
                }
            }
            if (detail.Profile == null && detail.Unit.Id == "C-01")
                GUILayout.Label("추가 스킬 없음");
            else if (detail.Profile == null)
                GUILayout.Label("스킬 정보 준비 중");
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
            if (detail.Grade == "안흔함" &&
                controller.OriginalUncommonAbilities != null)
            {
                var abilities = controller.OriginalUncommonAbilities.ForUnit(detail.Unit.Id);
                GUILayout.Label("고유 능력 " + abilities.Count + "개",
                    OrdrMobileUiTheme.Subheading);
                foreach (var native in abilities)
                    GUILayout.Label("· " + native.Name);

            }
            // Original W3X base values are display-only until hit damage,
            // modifiers and JASS timing have their independent verification.
            var originalMap = controller.MapBaseStats == null ? null :
                controller.MapBaseStats.Find(detail.Unit.Id);
            var nativePassive = originalMap == null || controller.OriginalNativePassives == null ? null :
                controller.OriginalNativePassives.ForRawcode(originalMap.OriginalRawcode);
            if (nativePassive != null)
            {
                string chance = (nativePassive.ChancePerMillion / 10000.0).ToString("0.##") + "%";
                GUILayout.Label(nativePassive.Kind == OriginalNativePassiveKind.Critical
                    ? "원본 크리티컬: " + chance + " 확률 · " + nativePassive.CriticalMultiplier.ToString("0.##") + "배"
                    : "원본 기절: " + chance + " 확률 · 일반 " + nativePassive.NormalStunSeconds.ToString("0.##") +
                      "초 / 영웅 " + nativePassive.HeroStunSeconds.ToString("0.##") + "초");
            }
            var attack = originalMap == null ? null : originalMap.Attack1;
            if (attack != null && attack.MinimumBaseDamage.HasValue &&
                attack.MaximumBaseDamage.HasValue &&
                attack.BaseAttacksPerSecond.HasValue)
            {
                OrdrMobileUiTheme.DrawStatusRow("기본 공격력",
                    attack.MinimumBaseDamage.Value + "~" +
                    attack.MaximumBaseDamage.Value, OrdrMobileUiTheme.Gold);
                OrdrMobileUiTheme.DrawStatusRow("공격 간격",
                    attack.AttackCooldownSeconds.Value.ToString("0.##") +
                    "초", OrdrMobileUiTheme.Aqua);
            }
            else
            {
                GUILayout.Label("기본 공격 정보 없음");
            }
            GUILayout.EndVertical();
        }

        private bool CanBatchSellNow(long instanceId)
        {
            if (Combat == null || controller.LowerGradeSales == null) return false;
            foreach (var row in Combat.MobileUnitSnapshot())
                if (row.InstanceId == instanceId)
                    return controller.LowerGradeSales.CanSell(row);
            return false;
        }

        private void DrawBatchSaleConfirm()
        {
            if (pendingBatchSales.Count == 0) return;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("선택 판매 " + pendingBatchSales.Count +
                "기 · 각 유닛은 원본 판매 규칙으로 순차 처리됩니다.");
            if (!confirmBatchSale)
            {
                if (GUILayout.Button("선택 유닛 일괄 판매", GUILayout.MinHeight(60)))
                    confirmBatchSale = true;
            }
            else
            {
                GUILayout.Label("확정 시 취소할 수 없습니다. 도중 실패하면 이후 유닛은 판매되지 않습니다.");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("판매 확정", OrdrMobileUiTheme.PrimaryButton,
                    GUILayout.MinHeight(60)))
                {
                    var result = MobileManualBatchActions.SellSelected(
                        pendingBatchSales.ToArray(), CanBatchSellNow,
                        controller.RequestLowerGradeSale);
                    ShowResult(result);
                    pendingBatchSales.Clear();
                    confirmBatchSale = false;
                    RefreshCurrent();
                }
                if (GUILayout.Button("취소", GUILayout.MinHeight(60)))
                    confirmBatchSale = false;
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("일괄선택 해제", GUILayout.MinHeight(66)))
            { pendingBatchSales.Clear(); confirmBatchSale = false; }
            GUILayout.EndVertical();
        }

        private void DrawOwnedUnitIndex()
        {
            if (Combat == null)
            {
                GUILayout.Label("월드가 시작되면 보유 유닛이 표시됩니다.");
                return;
            }
            var owned = Combat.MobileUnitSnapshot();
            OrdrMobileUiTheme.DrawStatusRow("보유 유닛", owned.Count + "기", OrdrMobileUiTheme.Aqua);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("전체" + (ownedFilter == OwnedRosterFilter.All ? " ✓" : ""), GUILayout.MinHeight(56)))
            { ownedFilter = OwnedRosterFilter.All; ownedPage = 0; expandedOwnedGroupId = null; }
            if (GUILayout.Button("판매 가능" + (ownedFilter == OwnedRosterFilter.Saleable ? " ✓" : ""), GUILayout.MinHeight(56)))
            { ownedFilter = OwnedRosterFilter.Saleable; ownedPage = 0; expandedOwnedGroupId = null; }
            if (GUILayout.Button("분해 가능" + (ownedFilter == OwnedRosterFilter.Decomposable ? " ✓" : ""), GUILayout.MinHeight(56)))
            { ownedFilter = OwnedRosterFilter.Decomposable; ownedPage = 0; expandedOwnedGroupId = null; }
            GUILayout.EndHorizontal();

            int unitCount, groupCount, pages, resolved;
            var displayed = MobileOwnedRosterBrowser.Group(owned, ownedFilter,
                x => controller.LowerGradeSales != null && controller.LowerGradeSales.CanSell(x),
                ownedPage, 8, out unitCount, out groupCount, out pages, out resolved);
            ownedPage = resolved;
            GUILayout.Label(groupCount + "종 · " + unitCount + "기", OrdrMobileUiTheme.Subheading);
            if (groupCount == 0) GUILayout.Label("해당 유닛이 없습니다.");
            DrawPager(ownedPage, pages, next => ownedPage = next);
            DrawBatchSaleConfirm();
            if (controller.LowerGradeSales != null)
                GUILayout.Label("흔함 판매 포인트 " + controller.LowerGradeSales.CommonPoints +
                    "/3 · 3기마다 랜덤위습 1개");
            if (owned.Count > 0 && GUILayout.Button(
                controller.PracticeBattle == null ? "보유 유닛 전체 보기" : "전장 전체 보기", GUILayout.MinHeight(56)))
            {
                var presenter = controller.GetComponent<OrdrUnitWorldPresenter>();
                if (presenter != null) presenter.ShowAllOwnedUnits();
            }

            foreach (var group in displayed)
            {
                bool isSelected = group.Contains(Combat.Roster.SelectedId);
                bool open = expandedOwnedGroupId == group.UnitId;
                string meta = group.Grade + " · " + group.Count + "기" +
                    (isSelected ? " · #" + Combat.Roster.SelectedId.Value + " 선택" : "");
                GUILayout.BeginHorizontal();
                if (OrdrMobileUiTheme.DrawActionRow(group.Name + " ×" + group.Count,
                    meta, OrdrMobileUiTheme.Aqua, 72f))
                {
                    long next = group.NextSelectable(Combat.Roster.SelectedId);
                    Combat.Roster.Select(next);
                    selectedCatalogUnitId = group.UnitId;
                    notice = group.Name + " #" + next + " 선택";
                }
                if (GUILayout.Button(open ? "접기" : "관리",
                    GUILayout.Width(80f), GUILayout.MinHeight(72f)))
                {
                    expandedOwnedGroupId = open ? null : group.UnitId;
                    expandedOwnedGroupPage = 0;
                    expandedOwnedUnitId = !open && group.Count == 1
                        ? (long?)group.Members[0].InstanceId : null;
                    pendingCommonSaleId = null;
                    pendingDecompositionId = null;
                    open = !open;
                }
                GUILayout.EndHorizontal();
                if (!open) continue;

                GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("전장 위치", GUILayout.MinHeight(58)))
                {
                    var presenter = controller.GetComponent<OrdrUnitWorldPresenter>();
                    if (presenter != null) presenter.ShowOwnedUnit(isSelected
                        ? Combat.Roster.SelectedId.Value : group.Members[0].InstanceId);
                }
                if (GUILayout.Button("조합 찾기", GUILayout.MinHeight(58)))
                {
                    SelectTab(Tab.Fusion);
                    fusionSearch = group.Name;
                    showOnlyBookmarkedFusion = false;
                    fusionPage = 0;
                    fusionMaterialsReadyOnly = false;
                    fusionExecutableOnly = true;
                }
                GUILayout.EndHorizontal();

                int memberPages = Math.Max(1, (group.Count + 7) / 8);
                expandedOwnedGroupPage = Math.Max(0, Math.Min(expandedOwnedGroupPage, memberPages - 1));
                if (group.Count > 1)
                    DrawPager(expandedOwnedGroupPage, memberPages,
                        next => { expandedOwnedGroupPage = next; expandedOwnedUnitId = null; });
                int first = expandedOwnedGroupPage * 8;
                for (int i = first; i < Math.Min(group.Count, first + 8); i++)
                {
                    var entry = group.Members[i];
                    bool selected = Combat.Roster.SelectedId == entry.InstanceId;
                    bool expanded = expandedOwnedUnitId == entry.InstanceId;
                    if (group.Count > 1)
                    {
                        GUILayout.BeginHorizontal();
                        if (OrdrMobileUiTheme.DrawActionRow("#" + entry.InstanceId,
                            selected ? "선택됨" : "유닛 선택", OrdrMobileUiTheme.Aqua, 62f))
                        {
                            Combat.Roster.Select(entry.InstanceId);
                            selectedCatalogUnitId = entry.Unit.Id;
                            notice = entry.Unit.Name + " #" + entry.InstanceId + " 선택";
                        }
                        if (GUILayout.Button(expanded ? "접기" : "관리",
                            GUILayout.Width(80f), GUILayout.MinHeight(62f)))
                        {
                            expandedOwnedUnitId = expanded ? (long?)null : entry.InstanceId;
                            pendingCommonSaleId = null;
                            pendingDecompositionId = null;
                            expanded = !expanded;
                        }
                        GUILayout.EndHorizontal();
                    }
                    if (!expanded) continue;
                    GUILayout.BeginVertical(GUI.skin.box);
                DrawCharacterPortrait(entry.Unit.Id, 62f);
                // All nine original common objects have A09G. Never display
                // A0JE/Avul as manual abilities; the transaction rechecks
                // exact original rawcode and ownership under its world lock.
                bool hasSale = controller.LowerGradeSales != null &&
                    controller.LowerGradeSales.CanSell(entry);
                if (hasSale)
                {
                    bool marked = pendingBatchSales.Contains(entry.InstanceId);
                    if (GUILayout.Button(marked ? "선택 판매에서 제외 ✓" :
                        "선택 판매에 추가", GUILayout.MinHeight(56)))
                    {
                        confirmBatchSale = false;
                        if (marked) pendingBatchSales.Remove(entry.InstanceId);
                        else if (pendingBatchSales.Count < MobileManualBatchActions.MaximumUnitSales)
                            pendingBatchSales.Add(entry.InstanceId);
                        else notice = "일괄 판매는 한 번에 10기까지 선택할 수 있습니다.";
                    }
                }
                if (hasSale || entry.Unit.Grade == "흔함")
                {
                    bool oldEnabled = GUI.enabled;
                    GUI.enabled = oldEnabled && (hasSale ||
                        controller.ManualActions.HasOriginalCommonSaleService);
                    if (GUILayout.Button(entry.Unit.Name + " 판매", GUILayout.MinHeight(64)))
                    {
                        pendingCommonSaleId = entry.InstanceId;
                        pendingCommonSaleName = entry.Unit.Name;
                        pendingDecompositionId = null;
                        notice = entry.Unit.Name + " 판매 대기";
                    }
                    GUI.enabled = oldEnabled;
                    if (hasSale)
                        GUILayout.Label(Original323LowerGradeSaleService.SaleDescription(entry.Unit.Grade));
                    else if (!controller.ManualActions.HasOriginalCommonSaleService)
                        GUILayout.Label("판매 연결 대기");
                }
                if (Original323UnitDecompositionService.Supports(entry.Unit.Id))
                {
                    var service = controller.UnitDecompositions;
                    if (service != null && service.IsCasting(entry.InstanceId))
                    {
                        GUILayout.Label("분해 시전 중 · " + service.Remaining(entry.InstanceId).ToString("0.0") + "초 남음");
                        if (GUILayout.Button("분해 시전 취소", GUILayout.MinHeight(64)))
                            notice = service.Cancel(entry.InstanceId).Message;
                    }
                    else
                    {
                        GUILayout.Label(Original323UnitDecompositionService.Description(entry.Unit.Id));
                        if (GUILayout.Button(entry.Unit.Name + " 분해", GUILayout.MinHeight(64)))
                        {
                            pendingDecompositionId = entry.InstanceId;
                            pendingDecompositionName = entry.Unit.Name;
                            pendingCommonSaleId = null;
                        }
                    }
                }
                DrawUnitConfirmations(entry.InstanceId);
                    GUILayout.EndVertical();
                }
                if (group.Count > 1)
                    DrawPager(expandedOwnedGroupPage, memberPages,
                        next => { expandedOwnedGroupPage = next; expandedOwnedUnitId = null; });
                GUILayout.EndVertical();
            }
            if (!string.IsNullOrEmpty(controller.UnitDecompositions?.LastMessage))
                GUILayout.Label(controller.UnitDecompositions.LastMessage);
            DrawPager(ownedPage, pages, next => ownedPage = next);
        }

        private void DrawUnitConfirmations(long instanceId)
        {
            if (pendingCommonSaleId == instanceId)
            {
                GUILayout.Label((pendingCommonSaleName ?? "흔함 유닛") + " #" +
                    pendingCommonSaleId.Value + " 판매 확정?");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("판매 확인", GUILayout.MinHeight(64)))
                {
                    ShowResult(controller.RequestLowerGradeSale(pendingCommonSaleId.Value));
                    pendingCommonSaleId = null;
                    pendingCommonSaleName = null;
                }
                if (GUILayout.Button("취소", GUILayout.MinHeight(64)))
                {
                    pendingCommonSaleId = null;
                    pendingCommonSaleName = null;
                }
                GUILayout.EndHorizontal();
            }
            if (pendingDecompositionId == instanceId)
            {
                GUILayout.Label(pendingDecompositionName + " 분해를 시작할까요? 완료하면 원래 유닛이 사라집니다.");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("분해 시작", GUILayout.MinHeight(64)))
                {
                    MobileUnitSnapshot selected = null;
                    foreach (var row in Combat.MobileUnitSnapshot())
                        if (row.InstanceId == pendingDecompositionId.Value) selected = row;
                    notice = controller.ManualActions.RequestUnitDecomposition(selected).Message;
                    pendingDecompositionId = null;
                }
                if (GUILayout.Button("취소", GUILayout.MinHeight(64))) pendingDecompositionId = null;
                GUILayout.EndHorizontal();
            }
        }

        private static void DrawCharacterPortrait(string unitId, float size)
        {
            Sprite portrait = OrdrCharacterPortraits.Find(unitId);
            if (portrait != null)
                GUILayout.Box(portrait.texture, GUILayout.Width(size), GUILayout.Height(size));
        }

        private void DrawPager(int page, int pages, Action<int> changePage)
        {
            if (pages < 2) return;
            bool previous = GUI.enabled;
            GUILayout.BeginHorizontal();
            GUI.enabled = previous && page > 0;
            if (GUILayout.Button("이전", GUILayout.MinHeight(56)))
            { changePage(page - 1); scroll = Vector2.zero; }
            GUI.enabled = previous;
            GUILayout.Label((page + 1) + " / " + pages, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            GUI.enabled = previous && page + 1 < pages;
            if (GUILayout.Button("다음", GUILayout.MinHeight(56)))
            { changePage(page + 1); scroll = Vector2.zero; }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
        }

        private void DrawStory()
        {
            GUILayout.Label("스토리 건물 14종", OrdrMobileUiTheme.Subheading);
            var scenario = controller.SoloStory;
            if (scenario != null)
            {
                GUILayout.Label("스토리 진행 " + scenario.HighestDestroyedStage + "/14 격파 · " +
                    "현재 건물 " + scenario.ActiveStage, OrdrMobileUiTheme.Subheading);
                if (scenario.EggheadWaitRemaining > 0)
                    GUILayout.Label("에그헤드 등장까지 " +
                        Math.Ceiling(scenario.EggheadWaitRemaining) + "초");
                if (scenario.EggheadDeadlineRemaining > 0)
                    GUILayout.Label("에그헤드 파괴 제한시간 " +
                        Math.Ceiling(scenario.EggheadDeadlineRemaining) + "초");
                if (!string.IsNullOrWhiteSpace(scenario.LastNotice))
                    GUILayout.Label(scenario.LastNotice);
                var progression = controller.StoryProgression;
                if (progression != null)
                    GUILayout.Label("스토리 연구 " + progression.Researches.Count +
                        "개 · 기본 특성포인트 " + progression.TraitPoints + "개");
            }
            var catalog = controller.StoryBuildingCatalog;
            var world = controller.PracticeStoryBuildings;
            if (catalog == null)
                GUILayout.Label("스토리 건물 준비 중", OrdrMobileUiTheme.Overline);
            else
            {
                for (int stage = 1; stage <= 14; stage++)
                {
                    var building = catalog.Find(stage);
                    if (building == null) continue;
                    string status = "미배치";
                    bool alive = false, settled = false;
                    if (world != null)
                    {
                        foreach (var encounter in world.Encounters)
                        {
                            if (encounter.Building.Stage != stage) continue;
                            alive = encounter.State == Original323StoryBuildingState.Alive;
                            settled = encounter.SourceSettlementConfirmed;
                            status = encounter.SourceSettlementConfirmed ? "원본 정산 확인" :
                                scenario != null && encounter.State == Original323StoryBuildingState.Destroyed
                                    ? "연습 파괴 완료 · 기본 보상 지급" :
                                alive ? "건물 유지" :
                                encounter.State == Original323StoryBuildingState.Destroyed
                                    ? "파괴 · 원본 보상 대조 필요" : "제거 기록";
                            if (alive)
                            {
                                var enemy = Combat.EnemyOf(encounter.EnemyId);
                                if (enemy != null)
                                    status += " · HP " + enemy.Health + "/" + enemy.MaxHealth;
                            }
                        }
                    }
                    string shortStatus = settled ? "정산 확인" :
                        alive ? "건물 유지" :
                        status.StartsWith("연습 파괴 완료", StringComparison.Ordinal)
                            ? "파괴 완료" : status;
                    string name = stage.ToString("00") + "  " + building.Name;
                    if (controller.CanRequestOriginalStoryBuilding && !alive && !settled)
                    {
                        if (OrdrMobileUiTheme.DrawActionRow(name,
                            "출현", OrdrMobileUiTheme.Gold))
                            ShowResult(controller.RequestOriginalStoryBuilding(stage));
                    }
                    else
                        OrdrMobileUiTheme.DrawStatusRow(name,
                            shortStatus, OrdrMobileUiTheme.Aqua);
                    if (alive && status.IndexOf("HP ", StringComparison.Ordinal) >= 0)
                        GUILayout.Label(status, OrdrMobileUiTheme.Overline);
                }
            }
            if (!controller.ManualActions.HasStoryService)
                OrdrMobileUiTheme.DrawStatusRow("스토리 파견",
                    "준비 중", OrdrMobileUiTheme.Gold);
            if (OrdrMobileUiTheme.DrawAccordionHeader("스토리 파견 관리",
                "유닛 목록", showStoryDispatch, OrdrMobileUiTheme.Gold))
                showStoryDispatch = !showStoryDispatch;
            if (showStoryDispatch)
            {
                GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
                foreach (var unit in Combat.MobileUnitSnapshot())
                {
                    GUILayout.BeginHorizontal();
                    string name = unit.Unit.Name + " (" + unit.Unit.Grade + ") #" + unit.InstanceId;
                    GUILayout.Label(name, GUILayout.MinWidth(110), GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("선택", GUILayout.Width(64), GUILayout.MinHeight(64)))
                    {
                        Combat.Roster.Select(unit.InstanceId);
                        notice = name + " 선택";
                    }
                    if (controller.ManualActions.IsStoryDeployed(unit.InstanceId))
                    {
                        if (controller.ManualActions.CanRecallStory(unit.InstanceId))
                        {
                            if (GUILayout.Button("복귀", GUILayout.Width(72), GUILayout.MinHeight(64)))
                                ShowResult(controller.ManualActions.RequestStoryRecall(unit.InstanceId));
                        }
                        else GUILayout.Label("파견 중", GUILayout.Width(72));
                    }
                    else if (GUILayout.Button("파견", GUILayout.Width(72), GUILayout.MinHeight(64)))
                        ShowResult(controller.ManualActions.RequestStoryDispatch(unit.InstanceId));
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }
        }

        private void DrawFusion()
        {
            DrawFusionGoalStatus();
            DrawFusionConfirmation();
            DrawChoiceWispMerge();
            GUILayout.BeginVertical(GUI.skin.box);
            string query = GUILayout.TextField(fusionSearch, GUILayout.MinHeight(56));
            if (!string.Equals(query, fusionSearch, StringComparison.Ordinal))
            {
                fusionSearch = query;
                fusionPage = 0;
                originalJassPage = 0;
                scroll = Vector2.zero;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(fusionMaterialsReadyOnly ?
                "재료 확보만 ✓" : "모든 조합", GUILayout.MinHeight(60)))
            {
                fusionMaterialsReadyOnly = !fusionMaterialsReadyOnly;
                fusionPage = 0;
                scroll = Vector2.zero;
            }
            if (GUILayout.Button("검색 초기화", GUILayout.MinHeight(60)))
            {
                fusionSearch = "";
                fusionMaterialsReadyOnly = false;
                fusionPage = 0;
                originalJassPage = 0;
                scroll = Vector2.zero;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            bool onlyExecutable = GUILayout.Toggle(fusionExecutableOnly,
                "실제 제작만 (안흔함 " + controller.ExecutableCommonFusionCount +
                " / 특별함 " + controller.ExecutableSpecialFusionCount +
                " / 희귀함 " + controller.ExecutableRareFusionCount +
                " / 전설 " + controller.ExecutableLegendaryFusionCount + "종)",
                GUILayout.MinHeight(56));
            if (onlyExecutable != fusionExecutableOnly)
            {
                fusionExecutableOnly = onlyExecutable;
                fusionPage = 0;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(showOnlyBookmarkedFusion ? "전체 제작식" :
                "즐겨찾기 " + fusionBookmarks.Count + "개만", GUILayout.MinHeight(56)))
            {
                showOnlyBookmarkedFusion = !showOnlyBookmarkedFusion;
                fusionPage = 0;
                scroll = Vector2.zero;
            }
            GUILayout.EndHorizontal();

            if (!controller.ManualActions.HasExecutableFusionService)
                OrdrMobileUiTheme.DrawStatusRow("제작",
                    "준비 중", OrdrMobileUiTheme.Gold);
            if (GUILayout.Button("보유 재료 새로고침", GUILayout.MinHeight(64)))
            {
                RefreshCurrent();
                pendingFusion = null;
            }

            int matches, pages, resolved;
            IReadOnlyList<MobileFusionPreview> browsedOffers = fusionOffers;
            if (fusionExecutableOnly)
            {
                var executableOffers = new List<MobileFusionPreview>();
                foreach (var offer in fusionOffers)
                    if (controller.ManualActions.CanExecuteFusionRecipe(offer.RecipeId))
                        executableOffers.Add(offer);
                browsedOffers = executableOffers.AsReadOnly();
            }
            if (showOnlyBookmarkedFusion)
                browsedOffers = fusionBookmarks.Filter(browsedOffers);
            var visibleOffers = MobileFusionBrowser.Select(browsedOffers,
                fusionSearch, fusionMaterialsReadyOnly, fusionPage,
                FusionPageSize, out matches, out pages, out resolved);
            fusionPage = resolved;
            GUILayout.Label("조합식 " + matches + " / " + fusionOffers.Count +
                "건 · " + (resolved + 1) + "/" + pages + "쪽");
            if (matches == 0)
                GUILayout.Label("검색 결과 없음");
            foreach (var offer in visibleOffers)
            {
                bool executable = controller.ManualActions.CanExecuteFusionRecipe(offer.RecipeId);
                string status = !offer.IsReady ? "부족 재료 " + offer.MissingMaterials.Count + "종" :
                    executable ? "재료 준비 · 제작 가능" : "재료 준비 · 제작 연결 대기";
                bool expanded = expandedRecipeId == offer.RecipeId;
                GUILayout.BeginHorizontal();
                bool previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && offer.IsReady && executable;
                if (OrdrMobileUiTheme.DrawActionRow(offer.Title,
                    "제작", OrdrMobileUiTheme.Gold, 72f))
                {
                    pendingFusion = offer;
                    fusionRepeatCount = 1;
                    pendingEconomy = null;
                    scroll = Vector2.zero;
                    notice = "제작 대기";
                }
                GUI.enabled = previousEnabled;
                if (GUILayout.Button(expanded ? "접기" : "상세",
                    GUILayout.Width(80f), GUILayout.MinHeight(72f)))
                {
                    expandedRecipeId = expanded ? null : offer.RecipeId;
                    expanded = !expanded;
                }
                GUILayout.EndHorizontal();
                if (!expanded) continue;
                GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
                OrdrMobileUiTheme.DrawStatusRow("재료",
                    status, OrdrMobileUiTheme.Aqua);
                if (expandedRecipeId == offer.RecipeId)
                {
                    if (GUILayout.Button(fusionBookmarks.Contains(offer.RecipeId) ? "즐겨찾기 해제" : "즐겨찾기 저장",
                        GUILayout.MinHeight(56)))
                    {
                        bool nowSaved = fusionBookmarks.Toggle(offer.RecipeId);
                        if (!nowSaved && !fusionBookmarks.Contains(offer.RecipeId))
                            notice = "즐겨찾기 해제 또는 최대 12개입니다.";
                    }
                    if (GUILayout.Button(fusionGoal.RecipeId == offer.RecipeId ? "목표 조합 해제" : "목표로 추적",
                        GUILayout.MinHeight(56)))
                    {
                        bool tracking = fusionGoal.Toggle(offer.RecipeId);
                        notice = tracking ? "목표 설정 완료" : "목표 추적을 해제했습니다.";
                    }
                    GUILayout.Label(offer.OriginalMaterialsAndCost);
                    if (offer.MissingMaterials.Count != 0)
                        GUILayout.Label("미확보: " + string.Join(", ", ToStringArray(offer.MissingMaterials)));
                    else
                        GUILayout.Label("재료 확보");
                    if (controller.OriginalJassFusions != null &&
                        controller.OriginalFusionObjectPreview != null &&
                        controller.OriginalJassFusions.FindByAbility(offer.RecipeId) != null &&
                        GUILayout.Button(routeId == offer.RecipeId ? "경로 닫기" : "하위 조합 경로", GUILayout.MinHeight(56)))
                    {
                        if (routeId == offer.RecipeId)
                        {
                            routeId = null;
                            routeSteps = new OriginalJassFusionDependencyStep[0];
                        }
                        else
                        {
                            routeSteps = new OriginalJassFusionDependencyPlanner(controller.OriginalJassFusions,
                                controller.OriginalFusionObjectPreview).Build(offer.RecipeId, 4);
                            routeId = offer.RecipeId;
                        }
                    }
                }
                GUILayout.EndVertical();
            }
            if (routeId != null)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("하위 조합 경로", OrdrMobileUiTheme.Subheading);
                foreach (var step in routeSteps)
                    GUILayout.Label(new string('　', Math.Min(step.Depth, 5)) +
                        "· " + step.Name + " ×" + step.Count +
                        (step.Source == null ? " · 하위 조합 없음" :
                        " · 하위 조합 / 추가 조건 " + step.SpecialCount + "개") +
                        (step.DepthLimited ? " · 더 깊은 경로 있음" : ""));
                GUILayout.EndVertical();
            }
            if (pages > 1)
            {
                GUILayout.BeginHorizontal();
                bool previous = GUI.enabled;
                GUI.enabled = previous && resolved > 0;
                if (GUILayout.Button("이전", GUILayout.MinHeight(60)))
                {
                    fusionPage--;
                    scroll = Vector2.zero;
                }
                GUI.enabled = previous && resolved + 1 < pages;
                if (GUILayout.Button("다음", GUILayout.MinHeight(60)))
                {
                    fusionPage++;
                    scroll = Vector2.zero;
                }
                GUI.enabled = previous;
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button(showOriginalAudit ? "원본 등록식 닫기" : "원본 등록식 보기",
                GUILayout.MinHeight(56f)))
                showOriginalAudit = !showOriginalAudit;
            if (showOriginalAudit) DrawOriginalJassAudit();
        }

        private void DrawFusionConfirmation()
        {
            if (pendingFusion == null) return;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("제작 확인", OrdrMobileUiTheme.Subheading);
            GUILayout.Label("선택됨: " + pendingFusion.Title);
            GUILayout.Label(pendingFusion.OriginalMaterialsAndCost);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(fusionRepeatCount == 1 ? "1회 ✓" : "1회",
                GUILayout.MinHeight(66))) fusionRepeatCount = 1;
            if (GUILayout.Button(fusionRepeatCount == 3 ? "3회 ✓" : "3회",
                GUILayout.MinHeight(66))) fusionRepeatCount = 3;
            if (GUILayout.Button(fusionRepeatCount == 5 ? "5회 ✓" : "5회",
                GUILayout.MinHeight(66))) fusionRepeatCount = 5;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            bool priorEnabled = GUI.enabled;
            GUI.enabled = priorEnabled &&
                controller.ManualActions.CanExecuteFusionRecipe(
                    pendingFusion.RecipeId);
            if (GUILayout.Button(fusionRepeatCount == 1 ? "조합 확정" :
                "동일 조합 " + fusionRepeatCount + "회 확정",
                OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(66)))
            {
                string recipe = pendingFusion.RecipeId;
                int amount = fusionRepeatCount;
                ShowResult(MobileFusionProductionAssistant.CraftRepeated(
                    recipe, amount,
                    controller.ManualActions.CanExecuteFusionRecipe,
                    () => controller.ManualActions.PreviewFusion(
                        Combat.MobileUnitSnapshot()),
                    controller.ManualActions.ConfirmFusion));
                pendingFusion = null;
                fusionRepeatCount = 1;
                RefreshCurrent();
            }
            GUI.enabled = priorEnabled;
            if (GUILayout.Button("취소", GUILayout.MinHeight(66))) pendingFusion = null;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawChoiceWispMerge()
        {
            if (OrdrMobileUiTheme.DrawAccordionHeader("선택위습 사용",
                controller.WispBalance(WispResourceIds.Choice) + "개",
                showChoiceWisp, OrdrMobileUiTheme.Aqua))
                showChoiceWisp = !showChoiceWisp;
            if (!showChoiceWisp) return;
            GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
            var targets = controller.ChoiceWispTargets;
            if (targets.Count == 0)
                GUILayout.Label("선택 대상 준비 중", OrdrMobileUiTheme.Overline);
            else if (controller.WispBalance(WispResourceIds.Choice) > 0)
            {
                foreach (var target in targets)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(target.Name + " · " + target.OriginalRequirements,
                        GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("바로 사용", GUILayout.Width(102),
                        GUILayout.MinHeight(64)))
                        RunWispDirect("선택위습 · " + target.Name,
                            () => controller.RequestChoiceWispMerge(target.Rawcode));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();
        }



        private void DrawTreasureGacha()
        {
            var treasure = controller.OriginalTreasureHunt;
            if (treasure != null)
                OrdrMobileUiTheme.DrawStatusRow("남은 보물",
                    treasure.UncollectedCount + "개", OrdrMobileUiTheme.Aqua);

            bool valid = treasure != null && controller.MobileTreasureGacha != null &&
                controller.MobileTreasureGacha.CanAttempt &&
                controller.HasOwnedOriginalGaimon &&
                controller.GaimonDream.Applied &&
                Combat != null && Combat.Game.Phase == SessionPhase.Running &&
                !controller.IsUnitPreparation && Combat.Game.CurrentWave > 0;
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && valid;
            if (OrdrMobileUiTheme.DrawActionRow("보물 탐색",
                "실행", OrdrMobileUiTheme.Gold, 80f))
            {
                lastTreasureResult = controller.RequestMobileTreasureGacha();
                lastTreasureRound = Combat.Game.CurrentWave;
            }
            GUI.enabled = wasEnabled;
            if (!valid)
                GUILayout.Label(!controller.HasOwnedOriginalGaimon ? "가이몬 필요" :
                    controller.MobileTreasureGacha == null ||
                    !controller.MobileTreasureGacha.CanAttempt ? "탐색 준비 중" :
                    !controller.GaimonDream.Applied ? "가이몬의 꿈 필요" :
                    "진행 중인 라운드에서 사용 가능",
                    OrdrMobileUiTheme.Overline);

            // The verified reveal is only a status; it never pays out again.
            if (controller.GaimonReveal != null &&
                controller.GaimonReveal.LastAuthorizedReveal != null)
            {
                var report = controller.GaimonReveal.LastAuthorizedReveal;
                OrdrMobileUiTheme.DrawStatusRow("최근 공개",
                    report.RevealedCount + "개 · " + report.OriginalRound + "R",
                    OrdrMobileUiTheme.Aqua);
            }
            if (lastTreasureResult != null)
            {
                GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
                GUILayout.Label(lastTreasureRound + "R 탐색 결과",
                    OrdrMobileUiTheme.Subheading);
                GUILayout.Label(lastTreasureResult.Message);
                if (lastTreasureResult.Collected > 0)
                    GUILayout.Label("획득 " + lastTreasureResult.RewardText());
                GUILayout.EndVertical();
            }
        }

        private void DrawAutomaticWispStatus(string name, string id)
        {
            long balance = controller.WispBalance(id);
            string failure = controller.Wisps?.AutomaticFailure(id);
            bool ready = controller.Wisps != null &&
                controller.Wisps.CanAutomaticallyResolve(id);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && ready && balance > 0 &&
                controller.CanProcessAutomaticWisps;
            if (OrdrMobileUiTheme.DrawActionRow(name,
                balance + "개", OrdrMobileUiTheme.Aqua, 76f, true))
                notice = controller.RequestAutomaticWispRetry().Message;
            GUI.enabled = wasEnabled;
            // A failed automatic grant is never rerolled; keep its reason visible.
            if (!string.IsNullOrEmpty(failure))
                GUILayout.Label("지급 보류: " + failure, OrdrMobileUiTheme.Explanation);
        }

        private void DrawWisp()
        {
            DrawRandomWispActions();
            GUILayout.Space(10f);
            // Choice Wisp belongs exclusively to the Fusion menu; its balance
            // is already shown in the top resource strip.
            DrawAutomaticWispStatus("안흔함위습", WispResourceIds.Uncommon);
            DrawAutomaticWispStatus("특별위습", WispResourceIds.Special);
            DrawAutomaticWispStatus("희귀위습", WispResourceIds.Rare);
            DrawAutomaticWispStatus("전설위습", WispResourceIds.Legendary);
            // Source-confirmed story boss reward only. An ordinary menu
            // press can NEVER simulate a missing CQB award event.
            if (controller.OriginalSourceMaterialRewards != null)
            {
                var rewards = controller.OriginalSourceMaterialRewards;
                GUILayout.Label("행운의 토큰 " +
                    controller.Combat.MobileUnitSnapshot().Count(x => x.Unit.Id == "LT-01") +
                    "개 · 원본 도박/상자 결과에서만 획득");
                if (rewards.PendingTranscendWisps > 0)
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label("스토리 보상 초월위습 " +
                        rewards.PendingTranscendWisps + "개",
                        OrdrMobileUiTheme.SectionTitle);
                    if (GUILayout.Button("초월위습 1개 → 바솔로뮤 쿠마 선택",
                        OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(64)))
                        RunWispDirect("스토리 초월위습 · 쿠마",
                            () => controller.RequestOriginalTranscendKumaChoice());
                    GUILayout.EndVertical();
                }
            }

            DrawEconomy(ManualEconomyCategory.Wisp);
            if (wispActions.Recent.Count > 0)
            {
                if (OrdrMobileUiTheme.DrawAccordionHeader("최근 위습 사용 기록",
                    wispActions.Recent.Count + "건", showWispHistory,
                    OrdrMobileUiTheme.Gold))
                    showWispHistory = !showWispHistory;
                if (showWispHistory)
                    foreach (var entry in wispActions.Recent)
                        GUILayout.Label(entry);
            }
        }

        private void RunWispDirect(string label, Func<ManualActionResult> action)
        {
            // Directly invoked by an explicit on-screen user button.
            try { ShowResult(wispActions.ExecuteDirect(label, action)); }
            catch (Exception error)
            {
                // A committed transaction might have thrown during callbacks:
                // never automatically rerun it or pretend to know its outcome.
                Debug.LogException(error);
                notice = "원본 거래 상태 확인 필요 · 자동 재시도 안 함: " + error.Message;
            }
            RefreshCurrent();
        }

        private void DrawRandomWispActions()
        {
            if (OrdrMobileUiTheme.DrawAccordionHeader("랜덤위습",
                controller.WispBalance(WispResourceIds.Random) + "개",
                showRandomWispActions, OrdrMobileUiTheme.Gold, 80f, true))
                showRandomWispActions = !showRandomWispActions;
            if (!showRandomWispActions) return;

            GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
            bool wasEnabled = GUI.enabled;

            // One press is one verified purchase. Do not add a detail/confirm step.
            int? cost = controller.VerifiedCommonWispRecruitmentCost;
            GUI.enabled = wasEnabled && cost.HasValue && cost.Value > 0 &&
                controller.CanRecruitVerifiedCommonWisp;
            if (OrdrMobileUiTheme.DrawActionRow("1. 흔함 유닛 소환",
                "1회", OrdrMobileUiTheme.Aqua, 64f))
                RunWispDirect("흔함 소환", () => controller.RequestCommonWispBatch(1));
            GUI.enabled = wasEnabled;

            bool canExchange = false;
            foreach (var offer in economyOffers)
                if (offer.Id == OriginalRandomWispResourceExchange.OfferId)
                    canExchange = true;
            GUI.enabled = wasEnabled && canExchange &&
                Combat != null && Combat.Game.CurrentWave > 0 &&
                controller.WispBalance(WispResourceIds.Random) > 0;
            if (OrdrMobileUiTheme.DrawActionRow("2. 골드·목재 추첨",
                "1회", OrdrMobileUiTheme.Gold, 64f))
                RunWispDirect("자원 추첨 1회", () =>
                    controller.ManualActions.RequestEconomyAction(
                        ManualEconomyCategory.Wisp,
                        OriginalRandomWispResourceExchange.OfferId));
            GUI.enabled = wasEnabled;

            bool canRecharge = controller.HelperCowRecharge != null &&
                controller.HelperCowRecharge.CanRecharge;
            GUI.enabled = wasEnabled && canRecharge;
            if (OrdrMobileUiTheme.DrawActionRow("3. 도움소 마나 충전",
                "1회", OrdrMobileUiTheme.Aqua, 64f))
                RunWispDirect("도움소 마나 충전", () =>
                    controller.ManualActions.RequestEconomyAction(
                        ManualEconomyCategory.Wisp, OriginalHelperCowRules.OfferId));
            GUI.enabled = wasEnabled;
            GUILayout.EndVertical();
        }

        private void DrawHelperCow()
        {
            OriginalHelperCowManaSnapshot mana;
            bool connected = controller.ManualActions.TryGetHelperCowMana(out mana);
            OrdrMobileUiTheme.DrawStatusRow("도움소 마나",
                connected ? mana.Current.ToString("0.#") +
                    "/" + mana.Maximum.ToString("0.#") : "준비 중",
                OrdrMobileUiTheme.Aqua);
            OrdrMobileUiTheme.DrawStatusRow("랜덤위습",
                controller.WispBalance(WispResourceIds.Random) + "개",
                OrdrMobileUiTheme.Gold);
            if (controller.Original323HelperCow != null)
            {
                var helper = controller.Original323HelperCow;
                var navigation = controller.Original323HelperNavigation;
                if (navigation != null && navigation.ChosenNavigation != null)
                    OrdrMobileUiTheme.DrawStatusRow("항법",
                        navigation.ChosenNavigation, OrdrMobileUiTheme.Aqua);
                else if (navigation != null)
                {
                    GUILayout.Label("항법 선택 · 21R부터 1회", OrdrMobileUiTheme.Overline);
                    bool wasEnabled = GUI.enabled;
                    GUI.enabled = wasEnabled && navigation.CanChooseMaximumOutput;
                    if (!pendingMaximumOutput && !pendingAlchemy)
                    {
                        if (GUILayout.Button("최고의도움: 최대출력 선택", GUILayout.MinHeight(64)))
                            pendingMaximumOutput = true;
                    }
                    else if (pendingMaximumOutput)
                    {
                        GUILayout.Label("이 항법을 선택하면 다른 항법을 선택할 수 없습니다.");
                        if (GUILayout.Button("최대출력 항법 확정", GUILayout.MinHeight(64)))
                        {
                            notice = controller.ChooseOriginal323MaximumOutput()
                                ? "최대출력 항법을 선택했습니다." : "항법 선택 조건 미충족";
                            pendingMaximumOutput = false;
                        }
                    }
                    GUI.enabled = wasEnabled;
                    if (pendingMaximumOutput && GUILayout.Button("항법 선택 취소", GUILayout.MinHeight(66)))
                        pendingMaximumOutput = false;
                    GUI.enabled = wasEnabled && navigation.CanChooseAlchemy;
                    if (!pendingMaximumOutput && !pendingAlchemy)
                    {
                        if (GUILayout.Button("최고의도움: 연금술 선택", GUILayout.MinHeight(64)))
                            pendingAlchemy = true;
                    }
                    else if (pendingAlchemy)
                    {
                        GUILayout.Label("연금술 항법을 선택하면 최대출력을 선택할 수 없습니다.");
                        if (GUILayout.Button("연금술 항법 확정", GUILayout.MinHeight(64)))
                        {
                            notice = controller.ChooseOriginal323Alchemy()
                                ? "연금술 항법 선택 완료 (시전 대상 연동은 검증 대기 중)"
                                : "항법 선택 조건 미충족";
                            pendingAlchemy = false;
                        }
                    }
                    GUI.enabled = wasEnabled;
                    if (pendingAlchemy && GUILayout.Button("항법 선택 취소", GUILayout.MinHeight(66)))
                        pendingAlchemy = false;
                }
                if (helper.ManaPotionChargesRemaining > 0)
                {
                    OrdrMobileUiTheme.DrawStatusRow("마나 포션",
                        helper.ManaPotionChargesRemaining + "회",
                        OrdrMobileUiTheme.Gold);
                    bool wasEnabled = GUI.enabled;
                    GUI.enabled = wasEnabled && controller.Original323ManaPotion != null &&
                        controller.Original323ManaPotion.CanAttempt;
                    if (GUILayout.Button("마나 포션 사용 · 마나 +300",
                        OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(64)))
                        notice = controller.UseOriginal323ManaPotion()
                            ? "마나 포션 사용 완료" : "원본 마나 포션 시전 조건 미충족";
                    GUI.enabled = wasEnabled;
                    if (controller.Original323ManaPotion == null)
                        GUILayout.Label("마나 포션 사용 준비 중", OrdrMobileUiTheme.Overline);
                }
            }
            OrdrMobileUiTheme.DrawStatusRow("자동 광역 지원",
                controller.HelperCowAutoArea == null ? "준비 중" : "자동 적용",
                OrdrMobileUiTheme.Aqua);
        }

        private void DrawAlchemyPanel()
        {
            if (controller.Original323HelperCow == null ||
                !controller.Original323HelperCow.AlchemyNavigationChosen)
            {
                OrdrMobileUiTheme.DrawStatusRow("연금술",
                    "항법 선택 필요", OrdrMobileUiTheme.Aqua);
                return;
            }
            OrdrMobileUiTheme.DrawStatusRow("연금술",
                "마나 100 · 5초", OrdrMobileUiTheme.Aqua);
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

            if (OrdrMobileUiTheme.DrawAccordionHeader("연금술 대상",
                selectedTitle + " · 보유 " + count + "기",
                alchemyTargetsExpanded, OrdrMobileUiTheme.Aqua))
                alchemyTargetsExpanded = !alchemyTargetsExpanded;

            if (alchemyTargetsExpanded)
            {
                GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
                if (count == 0)
                    GUILayout.Label("선택할 보유 유닛이 없습니다.");
                else
                {
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
                }
                GUILayout.EndVertical();
            }

            if (selected == null) return;
            var links = controller.OriginalUnitRawcodes;
            var refunds = controller.OriginalAlchemyRefunds;
            var link = links == null ? null : links.FindUnit(selected.Unit.Id);
            var recipe = link == null || refunds == null ? null : refunds.Find(link.Rawcode);
            if (recipe == null)
            {
                GUILayout.Label("재료 환급 정보 준비 중", OrdrMobileUiTheme.Overline);
            }
            else
            {
                // Names are preview hints only; the locked world inspects the
                // actual original target rawcode immediately before spending.
                GUILayout.Label("환급 재료", OrdrMobileUiTheme.Subheading);
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
                GUILayout.Label("연금술 실행 준비 중", OrdrMobileUiTheme.Overline);
                return;
            }
            if (casting.CooldownRemaining > 0)
                GUILayout.Label("연금술 남은 쿨타임: " +
                    casting.CooldownRemaining.ToString("0.0") + "초");
            bool enabledBefore = GUI.enabled;
            GUI.enabled = enabledBefore && casting.CanAttempt;
            if (pendingAlchemyCastId != selected.InstanceId)
            {
                if (GUILayout.Button("연금술 실행 선택", GUILayout.MinHeight(60)))
                    pendingAlchemyCastId = selected.InstanceId;
            }
            else
            {
                GUILayout.Label("이 유닛이 영구 소비됩니다. 계속할까요?");
                if (GUILayout.Button("유닛 소비 및 연금술 확정", GUILayout.MinHeight(64)))
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
                if (GUILayout.Button("연금술 취소", GUILayout.MinHeight(66)))
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
                "원본 조합 확인 목록 닫기" : "원본 조합 확인 목록 보기", GUILayout.MinHeight(64)))
            {
                showOriginalJassRecipes = !showOriginalJassRecipes;
                RefreshCurrent();
            }
            if (showOriginalJassRecipes)
            {
                // Source-audit list is READ ONLY and has its own pagination.
                // Unlike the old 18-item cap, every named result is accessible.
                var filtered = new List<JassFusionReadiness>();
                foreach (var item in originalJassPreview)
                {
                    if (string.IsNullOrWhiteSpace(fusionSearch) ||
                        item.ResultName.IndexOf(fusionSearch.Trim(),
                            StringComparison.OrdinalIgnoreCase) >= 0 ||
                        item.AbilityRawcode.IndexOf(fusionSearch.Trim(),
                            StringComparison.OrdinalIgnoreCase) >= 0)
                        filtered.Add(item);
                }
                int pages = Math.Max(1,
                    1 + (Math.Max(1, filtered.Count) - 1) / OriginalJassPageSize);
                originalJassPage = Math.Max(0,
                    Math.Min(originalJassPage, pages - 1));
                GUILayout.Label("원본 등록식 " + filtered.Count + " / " +
                    originalJassPreview.Count + "건 · " +
                    (originalJassPage + 1) + "/" + pages +
                    "쪽 (원작 소비 승인과 무관)");
                if (filtered.Count == 0) GUILayout.Label("일치하는 원본 등록식이 없습니다.");
                int start = originalJassPage * OriginalJassPageSize;
                for (int i = start;
                    i < Math.Min(start + OriginalJassPageSize, filtered.Count); i++)
                {
                    var result = filtered[i];
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label(result.ResultName + " [원본 등록식]");
                    foreach (var material in result.Materials)
                        GUILayout.Label(material.Name + " " + material.OwnedCount +
                            " / " + material.RequiredCount);
                    GUILayout.Label("골드 " + result.GoldCost +
                        " / 목재 " + result.LumberCost);
                    if (result.BlockingReasons.Count > 0)
                        GUILayout.Label("미확인: " + result.BlockingReasons[0]);
                    GUILayout.EndVertical();
                }
                if (pages > 1)
                {
                    GUILayout.BeginHorizontal();
                    bool previous = GUI.enabled;
                    GUI.enabled = previous && originalJassPage > 0;
                    if (GUILayout.Button("이전 등록식", GUILayout.MinHeight(56)))
                    {
                        originalJassPage--;
                        scroll = Vector2.zero;
                    }
                    GUI.enabled = previous && originalJassPage + 1 < pages;
                    if (GUILayout.Button("다음 등록식", GUILayout.MinHeight(56)))
                    {
                        originalJassPage++;
                        scroll = Vector2.zero;
                    }
                    GUI.enabled = previous;
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawEconomy(ManualEconomyCategory category)
        {
            if (!controller.ManualActions.HasEconomyService &&
                category != ManualEconomyCategory.Wisp)
                OrdrMobileUiTheme.DrawStatusRow("거래",
                    "준비 중", OrdrMobileUiTheme.Gold);

            if (category == ManualEconomyCategory.Gambling &&
                controller.PracticeMoneyGambling != null)
            {
                var shop = controller.PracticeMoneyGambling;
                OrdrMobileUiTheme.DrawStatusRow("일반 돈 도박",
                    shop.Stock + "/3회", OrdrMobileUiTheme.Gold);
                if (shop.Stock < SourceModelRegularMoneyGambling.MaximumStock)
                    GUILayout.Label("다음 충전 " +
                        Math.Ceiling(shop.SecondsUntilRestock) + "초");
            }

            // Selection is one tap, but real currency spending still confirms.
            if (category != ManualEconomyCategory.Wisp && pendingEconomy != null)
            {
                GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
                GUILayout.Label((category == ManualEconomyCategory.Gambling ?
                    "도박 확인 · " : "구매 확인 · ") + pendingEconomy.Title,
                    OrdrMobileUiTheme.Subheading);
                GUILayout.Label(pendingEconomy.OriginalPriceAndRules);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(category == ManualEconomyCategory.Gambling
                    ? "도박 실행" : "구매 확정", OrdrMobileUiTheme.PrimaryButton,
                    GUILayout.MinHeight(66f)))
                {
                    ShowResult(controller.ManualActions.RequestEconomyAction(
                        category, pendingEconomy.Id));
                    pendingEconomy = null;
                    RefreshCurrent();
                }
                if (GUILayout.Button("취소", GUILayout.MinHeight(66f)))
                    pendingEconomy = null;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            if (GUILayout.Button("새로고침", GUILayout.MinHeight(56f)))
                RefreshCurrent();
            if (economyOffers.Count == 0)
                GUILayout.Label("이용 가능한 항목 없음");
            foreach (var offer in economyOffers)
            {
                if (category == ManualEconomyCategory.Wisp &&
                    (offer.Id == OriginalRandomWispResourceExchange.OfferId ||
                     offer.Id == OriginalHelperCowRules.OfferId))
                    continue;
                if (category == ManualEconomyCategory.Wisp)
                {
                    if (OrdrMobileUiTheme.DrawActionRow(offer.Title,
                        "1회", OrdrMobileUiTheme.Gold))
                    {
                        string id = offer.Id;
                        RunWispDirect(offer.Title, () =>
                            controller.ManualActions.RequestEconomyAction(category, id));
                    }
                }
                else if (OrdrMobileUiTheme.DrawActionRow(offer.Title,
                    "선택", OrdrMobileUiTheme.Gold))
                {
                    pendingEconomy = offer;
                    scroll = Vector2.zero;
                    notice = "구매 확인";
                }
            }
        }

        private void RefreshCurrent()
        {
            if (Combat == null) return;
            if (currentTab == Tab.Fusion || currentTab == Tab.Play || fusionGoal.HasGoal)
            {
                fusionOffers = controller.ManualActions.PreviewFusion(Combat.MobileUnitSnapshot());
                if (showOriginalJassRecipes && controller.OriginalFusionPlanner != null)
                    originalJassPreview = controller.OriginalFusionPlanner.AssessNamedResults(
                        Combat.MobileUnitSnapshot(), null, null);
            }
            if (currentTab == Tab.Shop || currentTab == Tab.Gambling || currentTab == Tab.Wisp)
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


