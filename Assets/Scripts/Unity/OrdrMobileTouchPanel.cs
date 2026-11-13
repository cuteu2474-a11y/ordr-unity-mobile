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
        private enum Tab { Play, Units, Story, Fusion, Gambling, Wisp, LogPose, Helper, Alchemy, Missions, UpgradeOne, UpgradeTwo, Research }
        private enum ScoreboardPage { Summary, Commons, Utility }
        private enum FusionListCategory { All, Ready, Owned, Bookmarked }

        [SerializeField] private OrdrCharacterCombatController controller;
        [SerializeField] private bool openByDefault;
        private bool visible;
        private Tab currentTab = Tab.Play;
        private ScoreboardPage scoreboardPage;
        private MobileUtilitySpecSnapshot utilitySpecSnapshot;
        private float nextFusionRefresh;
        private string fusionInventorySignature;
        private bool showUnitDetails;
        private long? expandedOwnedUnitId;
        private string expandedOwnedGroupId;
        private int expandedOwnedGroupPage;
        private string expandedRecipeId;
        private bool showChoiceWisp;
        private bool showRandomWispActions = true;
        private bool showTranscendBoxChoices;
        private bool showTranscendBoxItemChoices;
        private int transcendBoxItemPage;
        private const int TranscendBoxItemPageSize = 5;
        private readonly HashSet<string> expandedResearchCodes =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<int> expandedMissionSlots = new HashSet<int>();
        private bool showStoryGreenBloodTargets;
        private int storyGreenBloodPage;
        private const int StoryGreenBloodPageSize = 6;
        private bool showStoryDispatch;
        private bool showStoryStageHistory;
        private bool showStoryNativeRewards;
        private bool confirmStoryRecallAll;
        private int selectedStoryRewardStage;
        private int storyRosterPage;
        private MobileStoryRosterFilter storyRosterFilter = MobileStoryRosterFilter.All;
        private bool showOriginalAudit;
        private readonly Dictionary<Tab, Vector2> tabScrollPositions = new Dictionary<Tab, Vector2>();
        private int ownedPage;
        private int ownedFrameSnapshotAt = -1;
        private CharacterCombatRuntime ownedFrameCombat;
        private IReadOnlyList<MobileUnitSnapshot> ownedFrameSnapshot;
        private string ownedGradeFilter;
        private bool showOwnedGradeFilters;
        private bool showSelectedUnitExtras;
        private bool showEvolutionManagement;
        private bool showLuckyTokenManagement;
        private bool showOriginalItemManagement;
        private OwnedRosterFilter ownedFilter = OwnedRosterFilter.All;
        private readonly List<long> pendingBatchSales = new List<long>();
        private bool confirmBatchSale;
        private readonly MobileWispActionJournal wispActions = new MobileWispActionJournal();
        private bool showWispHistory;
        private bool showMoneyGamblingHistory;
        private bool showGamblingExtras;
        private float nextGamblingRefresh;
        private Original323GamblingNavigation? pendingGamblingNavigation;
        private long? pendingUnitReroll;
        private MobileTreasureGachaResult lastTreasureResult;
        private int lastTreasureRound;
        private long observedTreasureAlarmSequence;
        private int poneglyphUnitPage;
        private long? pendingCommonSaleId;
        private string pendingCommonSaleName;
        private int pendingMissionReroll = -1;
        private bool showSharedMissions = true;
        private int selectedSharedMission = -1;
        private string selectedCatalogUnitId;
        private long? lastUnitSkillSelection;
        private bool showAllSelectedSkills;
        private Vector2 scroll;
        private Vector2 tabsScroll;
        private Vector2 dashboardScroll;
        private readonly MobilePanelScrollGesture panelScrollGesture = new MobilePanelScrollGesture();
        private readonly MobilePanelScrollGesture tabsScrollGesture = new MobilePanelScrollGesture();
        private readonly MobilePanelScrollGesture skillScrollGesture = new MobilePanelScrollGesture();
        private Rect tabsScrollViewport;
        private Rect skillScrollViewport;
        private float tabsScrollMaximum;
        private float skillScrollMaximum;
        private Rect previousSafeArea;
        private float previousUiScale;
        private readonly MobileLongPressGesture gamblingPressGesture = new MobileLongPressGesture();
        private readonly MobileLongPressGesture upgradePressGesture = new MobileLongPressGesture();
        private string expandedGamblingOfferId;
        private Rect panelScrollViewport;
        private float panelScrollMaximum;
        private Vector2 noticeScroll;
        private bool confirmReturnToTitle;
        private bool showFullNotice;
        private string lastNotice;
        private Vector2 dedicatedScroll;
        private long? dedicatedSkillLastSelectedUnit;
        private string dedicatedNotice;
        private string notice = "";
        // A compact, transient three-line log shared by unit selections,
        // actual gambling results and wisp awards. It never awards anything.
        private sealed class BattleLogItem
        {
            internal string Text;
            internal string UnitId;
            internal float StartedAt;
        }
        private readonly List<BattleLogItem> battleLog = new List<BattleLogItem>(3);
        private const float BattleLogLifetimeSeconds = 3.2f;
        private IReadOnlyList<MobileFusionPreview> fusionOffers =
            new List<MobileFusionPreview>().AsReadOnly();
        private IReadOnlyList<MobileManualOffer> economyOffers =
            new List<MobileManualOffer>().AsReadOnly();
        private MobileFusionPreview pendingFusion;
        private readonly MobileFusionBookmarks fusionBookmarks = new MobileFusionBookmarks();
        private readonly MobileFusionGoalTracker fusionGoal = new MobileFusionGoalTracker();
        // UI-only browsing: original source checks remain authoritative.
        private string fusionGradeFilter = "전체";
        private string fusionShortcutQuery = "";
        private bool showFusionGradeButtons;
        private Vector2 fusionSelectedMaterialScroll;
        private FusionListCategory fusionListCategory = FusionListCategory.Owned;
        private int fusionPage;
        private int originalJassPage;
        private string routeId;
        private IReadOnlyList<OriginalJassFusionDependencyStep> routeSteps =
            new OriginalJassFusionDependencyStep[0];
        private const int FusionPageSize = 8;
        private const int OriginalJassPageSize = 10;
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

        // Called only from a real battlefield-unit tap. The selected
        // character's skills appear at the TOP of the ordinary Units drawer.
        // Re-tapping a selected unit must not close that drawer.
        public void OpenSelectedUnitSkills()
        {
            if (Combat == null || !Combat.Roster.SelectedId.HasValue) return;
            if (!visible || currentTab != Tab.Units) SelectTab(Tab.Units);
            lastUnitSkillSelection = null;
            showUnitDetails = false;
            showAllSelectedSkills = false;
            scroll = Vector2.zero;
            long instanceId = Combat.Roster.SelectedId.Value;
            foreach (var actor in Combat.MobileUnitSnapshot())
                if (actor.InstanceId == instanceId)
                {
                    AddBattleLog(actor.Unit.Name + " 선택", actor.Unit.Id);
                    break;
                }
        }

        private void Update()
        {
            // BJH alert is shown in the normal action notice across all
            // menus, not only while the record-guidance drawer is open.
            if (controller != null &&
                controller.TreasureAlarmNoticeSequence != observedTreasureAlarmSequence)
            {
                observedTreasureAlarmSequence =
                    controller.TreasureAlarmNoticeSequence;
                if (!string.IsNullOrWhiteSpace(controller.TreasureAlarmNotice))
                    notice = controller.TreasureAlarmNotice;
            }
            if (visible && (currentTab == Tab.Gambling || currentTab == Tab.Wisp) &&
                Combat != null && Time.unscaledTime >= nextGamblingRefresh)
            {
                // Pure UI refresh: replenish display stock from the existing
                // service; never perform a gamble on a timed update.
                nextGamblingRefresh = Time.unscaledTime + .5f;
                RefreshCurrent();
            }
            if (Combat == null ||
                !((visible && (currentTab == Tab.Play || currentTab == Tab.Fusion)) ||
                  fusionGoal.HasGoal) ||
                Time.unscaledTime < nextFusionRefresh) return;
            nextFusionRefresh = Time.unscaledTime + 0.5f;
            var owned = Combat.MobileUnitSnapshot();
            var signature = new System.Text.StringBuilder();
            signature.Append(Combat.Game.Phase).Append(':').Append(Combat.Game.CurrentWave);
            // Recipe affordability can change without roster or wood changes:
            // gold, special counters, items and one-time wallet receipts all
            // participate in the live category refresh.
            var walletState = controller.SharedEconomy.Wallet.CaptureState();
            foreach (var balance in walletState.Balances)
                signature.Append('|').Append(balance.Id).Append(':').Append(balance.Amount);
            signature.Append("|receipts:").Append(walletState.CompletedRewards.Count);
            foreach (var unit in owned)
                signature.Append('|').Append(unit.InstanceId).Append(':').Append(unit.Unit.Id)
                    .Append(':').Append(Combat.IsStoryDeployed(unit.InstanceId))
                    .Append(':').Append(Combat.IsSharedObjectiveDeployed(unit.InstanceId))
                    .Append(':').Append(Combat.IsOriginalPoneglyphChanneling(unit.InstanceId));
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
            float scale = MobileMapSkillLayout.UiScale(safe, Screen.dpi);
            // World taps are physical pixels; the toast is laid out in
            // GUI.matrix-scaled logical pixels.
            return layout.BlocksWorldTap(onGui, visible) ||
                (RewardToastVisible && RewardToastRect(layout, scale)
                    .Contains(onGui / scale));
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
            panelScrollViewport = new Rect();
            gamblingPressGesture.Cancel();
            expandedGamblingOfferId = null;
            currentTab = selected;
            visible = true;
            if (selected != Tab.Fusion) fusionShortcutQuery = "";
            if (selected == Tab.Play) scoreboardPage = ScoreboardPage.Summary;
            Vector2 remembered;
            scroll = restoreScroll && tabScrollPositions.TryGetValue(selected, out remembered)
                ? remembered : Vector2.zero;
            confirmReturnToTitle = false;
            pendingFusion = null;
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
            panelScrollViewport = new Rect();
            gamblingPressGesture.Cancel();
            expandedGamblingOfferId = null;
            tabScrollPositions[currentTab] = scroll;
            visible = false;
            confirmReturnToTitle = false;
            pendingFusion = null;
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

            if (safe != previousSafeArea || scale != previousUiScale)
            {
                CancelScrollGestures();
                panelScrollViewport = tabsScrollViewport = skillScrollViewport = new Rect();
                previousSafeArea = safe;
                previousUiScale = scale;
            }
            HandleStripScroll(tabsScrollGesture, tabsScrollViewport, tabsScrollMaximum,
                ref tabsScroll, false, scale);
            HandleStripScroll(skillScrollGesture, skillScrollViewport, skillScrollMaximum,
                ref dedicatedScroll, true, scale);
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
                DrawRewardToast(layout, scale);
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
                    // A drag cannot complete a gambling action or reveal details.
                    gamblingPressGesture.Cancel();
                    // Cancel the button/text control that received MouseDown.
                    GUIUtility.hotControl = 0;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && panelScrollGesture.End())
            {
                gamblingPressGesture.Cancel();
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

        private void HandleStripScroll(MobilePanelScrollGesture gesture, Rect viewport,
            float maximum, ref Vector2 position, bool horizontal, float scale)
        {
            var e = Event.current;
            Vector2 pointer = e.mousePosition / scale;
            if (e.type == EventType.MouseDown && e.button == 0)
                gesture.Begin(pointer, viewport, horizontal ? position.x : position.y,
                    horizontal, false);
            else if (e.type == EventType.MouseDrag && e.button == 0)
            {
                float value = horizontal ? position.x : position.y;
                if (!gesture.Move(pointer, maximum, ref value)) return;
                if (horizontal) position.x = value; else position.y = value;
                GUIUtility.hotControl = 0;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && gesture.End())
            {
                GUIUtility.hotControl = 0;
                e.Use();
            }
        }

        private void CancelScrollGestures()
        {
            panelScrollGesture.Cancel();
            tabsScrollGesture.Cancel();
            skillScrollGesture.Cancel();
            gamblingPressGesture.Cancel();
            upgradePressGesture.Cancel();
        }

        private void OnDisable()
        {
            CancelScrollGestures();
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!focused) CancelScrollGestures();
        }
        private void OnApplicationPause(bool paused)
        {
            if (paused) CancelScrollGestures();
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
            GUILayout.BeginVertical();

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
            if (controller.Original323HelperCow != null &&
                controller.Original323HelperCow.AlchemyNavigationChosen)
                DrawLeftTab(Tab.Alchemy, "연금술");

            GUILayout.EndVertical();
            Rect tabsContent = GUILayoutUtility.GetLastRect();
            GUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint)
            {
                Rect viewport = GUILayoutUtility.GetLastRect();
                tabsScrollViewport = new Rect(area.x + viewport.x, area.y + viewport.y,
                    viewport.width, viewport.height);
                tabsScrollMaximum = Mathf.Max(0f, tabsContent.height - viewport.height);
                tabsScroll.y = Mathf.Clamp(tabsScroll.y, 0f, tabsScrollMaximum);
            }
            // Keep essential game controls on the persistent left rail,
            // outside the three compact, read-only scoreboard pages.
            if (Combat != null && controller.IsUnitPreparation)
            {
                GUILayout.Space(8f);
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && Combat.CharacterCount > 0;
                if (GUILayout.Button("전투 시작", OrdrMobileUiTheme.PrimaryButton,
                    GUILayout.MinHeight(56f)))
                {
                    try
                    {
                        controller.BeginStandaloneBattle();
                        notice = "전투 시작 · 소환과 조합은 계속할 수 있습니다.";
                    }
                    catch (Exception error) { notice = error.Message; }
                }
                GUI.enabled = enabled;
            }
            if (Combat != null && Combat.Game.Phase == SessionPhase.Running &&
                !controller.IsUnitPreparation)
            {
                GUILayout.Space(8f);
                if (GUILayout.Button(controller.IsGameplayPaused ? "계속" : "일시정지",
                    GUILayout.MinHeight(56)))
                    controller.SetManualGameplayPause(!controller.IsGameplayPaused);
            }
            if (Combat != null && !controller.IsUnitPreparation)
            {
                if (GUILayout.Button(confirmReturnToTitle ? "처음으로 확정" : "처음으로",
                    GUILayout.MinHeight(48f)))
                {
                    if (confirmReturnToTitle)
                        UnityEngine.SceneManagement.SceneManager.LoadScene(
                            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                    else confirmReturnToTitle = true;
                }
                if (confirmReturnToTitle && GUILayout.Button("취소",
                    GUILayout.MinHeight(44f)))
                    confirmReturnToTitle = false;
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
            {
                SelectTab(tab, true);
            }
            if (Event.current.type == EventType.Repaint)
            {
                Color tint = GetFacilityTabTint(tab);
                OrdrMobileUiTheme.DrawNavigationMark(GUILayoutUtility.GetLastRect(),
                    tint, chosen);
            }
        }

        // The seven source-backed building tabs keep distinct navigation
        // accents, without introductory slogans or decorative header plates.
        private static Color GetFacilityTabTint(Tab tab)
        {
            switch (tab)
            {
                case Tab.Gambling: return new Color(1f, .68f, .35f);
                case Tab.UpgradeOne: return new Color(1f, .69f, .38f);
                case Tab.UpgradeTwo: return new Color(1f, .77f, .42f);
                case Tab.Helper: return new Color(.41f, .9f, .76f);
                case Tab.Missions: return new Color(.43f, .78f, 1f);
                case Tab.Research: return new Color(.44f, .88f, .96f);
                case Tab.LogPose: return new Color(.76f, .67f, 1f);
                default: return OrdrMobileUiTheme.Aqua;
            }
        }

        private void DrawRightDrawer(MobileMapSkillLayout layout, float scale)
        {
            Rect area = MobileMapSkillLayout.Scaled(layout.RightDrawer, scale);
            GUILayout.BeginArea(area, OrdrMobileUiTheme.BorderlessPanel);
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            // The chat scoreboard uses its own page selector; a second large
            // '점수판' heading wastes the phone-sized right drawer.
            if (currentTab != Tab.Play)
                GUILayout.Label(TabTitle(currentTab), OrdrMobileUiTheme.SectionTitle,
                    GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            else
            {
                // Scoreboard tabs share the header row with Close. No
                // duplicate title or second line of navigation controls.
                DrawScoreboardPageButton(ScoreboardPage.Summary, "개인 기록");
                DrawScoreboardPageButton(ScoreboardPage.Commons, "흔함 획득");
                DrawScoreboardPageButton(ScoreboardPage.Utility, "유틸보드");
            }
            if (GUILayout.Button(new GUIContent("×", "닫기"), OrdrMobileUiTheme.CloseButton,
                GUILayout.Width(56f), GUILayout.MinHeight(52f)))
                CloseDrawer();
            GUILayout.EndHorizontal();

            if (currentTab == Tab.Fusion && Combat != null)
                DrawFusionSelectedHeader();
            scroll = BeginPanelScroll(scroll);
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
                    case Tab.Gambling: DrawGambling(); break;
                    case Tab.Wisp: DrawWisp(); break;
                    case Tab.LogPose: DrawTreasureGacha(); break;
                    case Tab.Helper: DrawHelperCow(); break;
                    case Tab.Alchemy: DrawAlchemyPanel(); break;
                    case Tab.Missions: DrawMissions(); break;
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
                GUILayout.BeginVertical(OrdrMobileUiTheme.HelpBubble);
                float noticeWidth = Mathf.Max(60f, area.width - 72f);
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
            // The one-transaction confirmation is last in the right drawer,
            // outside the scroll even when an action notice is present.
            if (currentTab == Tab.Fusion)
                DrawFusionConfirmation();

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        // Read-only tactics dashboard shown when the detail pane is closed.
        // It contains battle state only; navigation stays in the left rail.
        private void DrawStandbyDashboard(MobileMapSkillLayout layout,float scale)
        {
            Rect area = MobileMapSkillLayout.Scaled(layout.RightDrawer, scale);
            GUILayout.BeginArea(area, OrdrMobileUiTheme.BorderlessPanel);
            dashboardScroll = BeginPanelScroll(dashboardScroll);
            if (Combat == null)
                GUILayout.Label("전투 대기");
            else
            {
                var game = Combat.Game;
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
                case Tab.Gambling: return "도박소";
                case Tab.Missions: return "미션";
                case Tab.UpgradeOne: return "강화소 1";
                case Tab.UpgradeTwo: return "강화소 2";
                case Tab.Research: return "연구소";
                default: throw new ArgumentOutOfRangeException(nameof(tab));
            }
        }

        private void DrawPlayerBuildingResearch(string buildingRawcode)
        {
            var original = Original323PlayerBuildingResearchCatalog.ForBuilding(buildingRawcode);
            if (buildingRawcode == Original323PlayerBuildingResearchCatalog.ResearchBuilding)
            {
                var laboratory = controller != null ? controller.PlayerBuildingResearch : null;
                foreach (var research in original)
                {
                    var quote = laboratory?.Quote(research.Rawcode);
                    bool started = quote?.Level == 1;
                    string title = research.Name + "  " + (started ? "1" : "0") +
                        "/" + research.MaximumLevel;
                    string caption = ResearchStatusText(quote);
                    if (quote?.Cost != null)
                        caption = UpgradePriceText(quote.Cost) + " · " + caption;
                    if (quote?.Status == Original323ResearchStartStatus.InsufficientResources)
                        caption = UpgradeShortageText(quote.Cost);
                    string key = "research:" + research.Rawcode;
                    var press = OrdrMobileUiTheme.DrawHoldActionRow(key, title,
                        caption, new Color(.44f, .88f, .96f),
                        upgradePressGesture, 88f);
                    if (press == MobileLongPressResult.Tap)
                    {
                        if (laboratory == null)
                            notice = "연구소 모델이 연결되지 않았습니다.";
                        else
                        {
                            try
                            {
                                var result = laboratory.Start(research.Rawcode);
                                notice = result == Original323ResearchStartStatus.Started
                                    ? research.Name + " 연구 시작 · 오라 적용"
                                    : result == Original323ResearchStartStatus.InsufficientResources
                                        ? UpgradeShortageText(quote?.Cost)
                                        : ResearchStatusText(laboratory.Quote(research.Rawcode));
                            }
                            catch (Exception error)
                            {
                                notice = "연구 시작 실패 · 거래 복구: " + error.Message;
                            }
                        }
                    }
                    else if (press == MobileLongPressResult.Hold)
                    {
                        if (expandedResearchCodes.Contains(key))
                            expandedResearchCodes.Remove(key);
                        else expandedResearchCodes.Add(key);
                    }
                    if (!expandedResearchCodes.Contains(key)) continue;
                    GUILayout.BeginVertical(OrdrMobileUiTheme.HelpBubble);
                    GUILayout.Label(research.EffectSummary);
                    if (quote?.Cost != null)
                        GUILayout.Label("연구 시작 비용: " + UpgradePriceText(quote.Cost));
                    GUILayout.Label("원본 CV8: 연구 시작 시 오라 부여");
                    GUILayout.Label("연구 완료·취소·환불은 원본 실행 확인 전");
                    if (research.Rawcode == "Rhfc" &&
                        quote?.Status == Original323ResearchStartStatus.EffectUnavailable)
                        GUILayout.Label("이동 감속 원본 처리기 연결 시 구매 가능");
                    GUILayout.EndVertical();
                }
                return;
            }

            var service = controller != null ? controller.PlayerBuildingUpgrades : null;
            var tint = buildingRawcode == Original323PlayerBuildingResearchCatalog.FirstUpgradeBuilding
                ? new Color(1f, .68f, .36f) : new Color(1f, .81f, .47f);
            // Presentation order only. Source catalog, rawcodes, prices and
            // purchase conditions remain in their original data order.
            var displayOrder = new List<Original323PlayerBuildingResearch>(original);
            if (buildingRawcode == Original323PlayerBuildingResearchCatalog.FirstUpgradeBuilding)
                displayOrder.Sort((a, b) => UpgradeDisplayRank(a.Rawcode).CompareTo(
                    UpgradeDisplayRank(b.Rawcode)));
            else
                displayOrder.Sort((a, b) => string.Compare(
                    a.Name, b.Name, StringComparison.Ordinal));
            foreach (var upgrade in displayOrder)
            {
                var quote = service != null ? service.Quote(upgrade.Rawcode) : null;
                int currentLevel = quote == null ? 0 : quote.Level;
                string title = upgrade.Name.Replace(" 업그레이드", "");
                string stage = currentLevel + " / " + upgrade.MaximumLevel;
                string caption = UpgradeStatusText(quote);
                if (quote?.NextCost != null) caption = UpgradePriceText(quote.NextCost) + "\n" + caption;
                if (quote?.Status == Original323UpgradePurchaseStatus.InsufficientResources)
                    caption = UpgradePriceText(quote.NextCost) + "\n" + UpgradeShortageText(quote.NextCost);
                string key = "upgrade:" + upgrade.Rawcode;
                MobileLongPressResult press = OrdrMobileUiTheme.DrawHoldActionRow(
                    key, title, caption, tint, upgradePressGesture, 88f,
                    levelText: stage);
                if (press == MobileLongPressResult.Tap)
                {
                    if (service == null)
                        notice = "강화소 연결이 필요합니다.";
                    else
                    {
                        try
                        {
                            var result = service.Purchase(upgrade.Rawcode);
                            notice = result == Original323UpgradePurchaseStatus.Succeeded
                                ? upgrade.Name + " " + service.LevelOf(upgrade.Rawcode) + "단계"
                                : result == Original323UpgradePurchaseStatus.InsufficientResources
                                    ? UpgradeShortageText(quote?.NextCost) : UpgradeFailureText(result);
                        }
                        catch (Exception error)
                        {
                            notice = "강화 실패: " + error.Message;
                        }
                    }
                }
                else if (press == MobileLongPressResult.Hold)
                {
                    if (expandedResearchCodes.Contains(key)) expandedResearchCodes.Remove(key);
                    else expandedResearchCodes.Add(key);
                }
                // A tap buys one level; hold reveals effects and prerequisites.
                if (!expandedResearchCodes.Contains(key)) continue;
                GUILayout.BeginVertical(OrdrMobileUiTheme.HelpBubble);
                if (quote == null || quote.NextCost == null)
                    GUILayout.Label("다음 단계의 원본 비용·효과 자료 없음");
                else
                {
                    GUILayout.Label("필요: " + UpgradePriceText(quote.NextCost));
                    GUILayout.Label(quote.NextEffectSummary);
                    if (quote.Status == Original323UpgradePurchaseStatus.PrerequisiteMissing)
                        GUILayout.Label("20라운드 보스 크로커다일 처치 후 강화할 수 있습니다.");
                    if (quote.CooldownRemaining > 0)
                        GUILayout.Label("대기 " +
                            Math.Ceiling(quote.CooldownRemaining).ToString("0") + "초");
                }
                GUILayout.EndVertical();
            }
        }

        // Only the onscreen order is normalized: base ranks ascend through
        // tiers, and unique eternal upgrades are easy to find by character.
        // No upgrade cost, cap, rawcode, purchase order or effect is changed.
        private static int UpgradeDisplayRank(string rawcode)
        {
            switch (rawcode)
            {
                case "R00F": return 0; // 흔함류
                case "R00E": return 1; // 특별
                case "R008": return 2; // 희귀
                case "R000": return 3; // 전설
                case "R003": return 4; // 초월
                case "R002": return 5; // 불멸
                case "R004": return 6; // 제한
                case "R019": return 7; // 랜덤전용
                default: return int.MaxValue;
            }
        }

        private static string ResearchStatusText(Original323ResearchQuote quote)
        {
            if (quote == null) return "연구소 미연결";
            switch (quote.Status)
            {
                case Original323ResearchStartStatus.Started: return "연구 시작";
                case Original323ResearchStartStatus.AlreadyStarted: return "시작됨 · 오라 적용";
                case Original323ResearchStartStatus.InsufficientResources: return "재화 부족";
                case Original323ResearchStartStatus.NotRunning: return "전투 시작 대기";
                case Original323ResearchStartStatus.EffectUnavailable:
                    return "전투 효과 미연결 · 결제 불가";
                case Original323ResearchStartStatus.InconsistentReceipt:
                    return "연구 장부·영수증 불일치";
                default: return "연구 시작 불가";
            }
        }

        private string UpgradeShortageText(ResourceBundle cost)
        {
            if (cost == null || controller?.SharedEconomy == null) return "필요 재화 확인 불가";
            var parts = new List<string>();
            foreach (var entry in cost.Entries)
            {
                long missing = Math.Max(0, entry.Value - controller.SharedEconomy.Wallet.Balance(entry.Key));
                if (missing > 0) parts.Add(UpgradeResourceName(entry.Key) + " " + missing.ToString("N0") + " 부족");
            }
            return string.Join(" · ", parts.ToArray());
        }

        private static string UpgradeResourceName(string id) => id == "gold" ? "골드" : (id == "wood" || id == "lumber") ? "목재" : id;

        private static string UpgradePriceText(ResourceBundle cost)
        {
            var parts = new List<string>();
            foreach (var entry in cost.Entries)
                parts.Add(UpgradeResourceName(entry.Key) + " " + entry.Value.ToString("N0"));
            return string.Join(" · ", parts.ToArray());
        }

        private static string UpgradeStatusText(Original323UpgradeQuote quote)
        {
            if (quote == null) return "미연결";
            switch (quote.Status)
            {
                case Original323UpgradePurchaseStatus.Succeeded: return "강화";
                case Original323UpgradePurchaseStatus.MaximumLevel: return "최대";
                case Original323UpgradePurchaseStatus.SourceMissing: return "원본 수치 미확인";
                case Original323UpgradePurchaseStatus.WorldMissing: return "전투 효과 미연결";
                case Original323UpgradePurchaseStatus.NotRunning: return "대기";
                case Original323UpgradePurchaseStatus.Cooldown: return "재사용 대기";
                case Original323UpgradePurchaseStatus.InsufficientResources: return "재화 부족";
                case Original323UpgradePurchaseStatus.PrerequisiteMissing: return "20R 보스 처치 필요";
                default: return "강화 불가";
            }
        }

        private static string UpgradeFailureText(Original323UpgradePurchaseStatus status)
        {
            switch (status)
            {
                case Original323UpgradePurchaseStatus.SourceMissing:
                    return "원본 단계별 비용·효과 자료가 아직 없습니다.";
                case Original323UpgradePurchaseStatus.WorldMissing:
                    return "원본 강화 전투 효과가 연결되지 않았습니다.";
                case Original323UpgradePurchaseStatus.InsufficientResources:
                    return "강화에 필요한 재화가 부족합니다.";
                case Original323UpgradePurchaseStatus.PrerequisiteMissing:
                    return "20라운드 보스 크로커다일을 처치해야 합니다.";
                case Original323UpgradePurchaseStatus.MaximumLevel:
                    return "최대 강화 단계입니다.";
                case Original323UpgradePurchaseStatus.NotRunning:
                    return "전투 세션 시작 후 사용할 수 있습니다.";
                case Original323UpgradePurchaseStatus.Cooldown:
                    return "강화 대기 시간이 남았습니다.";
                case Original323UpgradePurchaseStatus.EffectRejected:
                    return "강화 효과가 적용되지 않아 비용을 복구했습니다.";
                default: return "강화를 진행하지 못했습니다.";
            }
        }

        // Keep only the useful personal stats and confirmed common-wisp draws.
        // Original + / - breakdown and final score tracking are intentionally
        // out of scope for the mobile remake at the player's request.


        private void DrawOriginalChatScoreboard()
        {
            switch (scoreboardPage)
            {
                case ScoreboardPage.Summary: DrawOriginalChatScoreSummary(); break;
                case ScoreboardPage.Commons: DrawOriginalChatCommonDraws(); break;
                case ScoreboardPage.Utility: DrawOriginalUtilityBoard(); break;
            }
        }

        private void DrawScoreboardPageButton(ScoreboardPage page, string label)
        {
            if (GUILayout.Button(label,
                scoreboardPage == page ? OrdrMobileUiTheme.SelectedTab : GUI.skin.button,
                GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true), GUILayout.MinHeight(48f)))
            {
                scoreboardPage = page;
                scroll = Vector2.zero;
            }
        }

        private void DrawScoreMetric(string label, string value, Color highlight)
        {
            OrdrMobileUiTheme.DrawStatusRow(label, value, highlight, 48f);
        }

        private void DrawOriginalChatScoreSummary()
        {
            var game = Combat.Game;
            var practice = controller.PracticeBattle;
            GUILayout.BeginHorizontal();
            OrdrMobileUiTheme.DrawScoreHeroCard("현재 라운드",
                controller.IsUnitPreparation ? "준비" : game.CurrentWave + "R",
                "/65R", OrdrMobileUiTheme.Gold);
            OrdrMobileUiTheme.DrawScoreHeroCard("남은 시간",
                practice == null ? "--" :
                    TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling(practice.SecondsRemaining)))
                        .ToString(@"mm\:ss"),
                null, OrdrMobileUiTheme.Aqua);
            OrdrMobileUiTheme.DrawScoreHeroCard("남은 적",
                game.AliveEnemies.ToString(),
                practice == null ? null : "/ " + practice.EnemyLimit + "기",
                OrdrMobileUiTheme.Gold);
            GUILayout.EndHorizontal();
            GUILayout.Space(7f);

            DrawScoreMetric("적 처치", game.DefeatedEnemies + "기", OrdrMobileUiTheme.Gold);
            DrawScoreMetric("보유 전투 유닛", Combat.CharacterCount + "기",
                OrdrMobileUiTheme.Aqua);
            if (controller.OriginalTreasureHunt != null)
                DrawScoreMetric("보물 수집",
                    controller.OriginalTreasureHunt.CollectedCount + "개", OrdrMobileUiTheme.Gold);
            if (controller.PracticeMoneyGambling != null)
                DrawScoreMetric("일반 돈 도박",
                    controller.PracticeMoneyGambling.PurchaseCount + "회",
                    OrdrMobileUiTheme.Gold);
            if (controller.Original323HelperNavigation != null &&
                controller.Original323HelperNavigation.ChosenNavigation != null)
                DrawScoreMetric("항법",
                    controller.Original323HelperNavigation.ChosenNavigation,
                    OrdrMobileUiTheme.Aqua);
        }

        private void DrawOriginalChatCommonDraws()
        {
            var journal = controller.ChatScoreboard;
            OrdrMobileUiTheme.DrawScoreHeroCard("누적 흔함 획득",
                journal.ConfirmedCommonDraws + "기", null, OrdrMobileUiTheme.Gold, 98f);
            GUILayout.Space(7f);
            for (int i = 0; i < MobileOriginalChatScoreboard.CommonCatalogIds.Length; i++)
                DrawScoreMetric(MobileOriginalChatScoreboard.CommonNames[i],
                    journal.CommonDrawCount(
                        MobileOriginalChatScoreboard.CommonCatalogIds[i]) + "기",
                    OrdrMobileUiTheme.Aqua);
        }

        private void RefreshOriginalUtilitySpec()
        {
            if (Combat == null)
            {
                utilitySpecSnapshot = null;
                return;
            }
            var unitIds = new List<string>();
            foreach (var owned in Combat.MobileUnitSnapshot())
                unitIds.Add(owned.Unit.Id);
            utilitySpecSnapshot = MobileOriginalUtilityBoard.Capture(
                unitIds, controller.Skills, controller.OriginalCombinedAuras);
        }

        private void DrawOriginalUtilityBoard()
        {
            // Source-derived potential totals; they remain documented as
            // estimates, not guaranteed applied combat effects.
            if (utilitySpecSnapshot == null) RefreshOriginalUtilitySpec();
            var spec = utilitySpecSnapshot;
            if (spec == null) return;
            DrawScoreMetric("방어력 감소 오라",
                spec.AuraDefinitions == 0 ? "자료 없음" :
                "-" + spec.AuraArmorReduction.ToString("0.##"),
                OrdrMobileUiTheme.Aqua);
            DrawScoreMetric("이동속도 감소 오라",
                spec.AuraDefinitions == 0 ? "자료 없음" :
                "-" + spec.AuraMovementSlowPercent.ToString("0.##") + "%",
                OrdrMobileUiTheme.Aqua);
            DrawScoreMetric("발동형 방어력 감소",
                spec.TriggeredDefinitions == 0 ? "자료 없음" :
                "-" + spec.TriggeredArmorReduction.ToString("0.##"),
                OrdrMobileUiTheme.Gold);
            DrawScoreMetric("발동형 이동속도 감소",
                spec.TriggeredDefinitions == 0 ? "자료 없음" :
                "-" + spec.TriggeredMovementSlowPercent.ToString("0.##") + "%",
                OrdrMobileUiTheme.Gold);
        }

        private void DrawPlay()
        {
            if (Combat == null || controller.SharedEconomy == null) return;
            DrawOriginalChatScoreboard();
        }

        private void DrawMissions()
        {
            // Separate the three personal assignments from the eight verified
            // shared goals. Switching views never modifies mission state.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("공유 미션", showSharedMissions ?
                OrdrMobileUiTheme.SelectedTab : OrdrMobileUiTheme.NavButton,
                GUILayout.MinHeight(58f), GUILayout.ExpandWidth(true)))
            {
                if (!showSharedMissions) { showSharedMissions = true; scroll = Vector2.zero; }
            }
            if (GUILayout.Button("개인 미션", !showSharedMissions ?
                OrdrMobileUiTheme.SelectedTab : OrdrMobileUiTheme.NavButton,
                GUILayout.MinHeight(58f), GUILayout.ExpandWidth(true)))
            {
                if (showSharedMissions) { showSharedMissions = false; scroll = Vector2.zero; }
            }
            GUILayout.EndHorizontal();

            if (showSharedMissions) DrawSharedMissionBoard();
            else DrawPersonalMissions();
        }

        // A mission row can be selected now. Manual "perform" eligibility
        // stays unconnected until the original per-character list is provided.
        // Only already-observed combat targets keep their existing dispatch
        // action; UI selection can never create a target or award a mission.
        private void DrawSharedMissionBoard()
        {
            var session = controller.Poneglyph;
            if (session == null)
            {
                GUILayout.Label("공유 미션 준비 중",
                    OrdrMobileUiTheme.Subheading);
                return;
            }
            OrdrMobileUiTheme.DrawStatusRow("공유 미션",
                session.SharedCompletedCount + "/" +
                    Original323PoneglyphSession.SharedMissionNames.Length,
                OrdrMobileUiTheme.Gold);
            for (int i = 0; i < Original323PoneglyphSession.SharedMissionNames.Length; i++)
            {
                var kind = (Original323SharedMissionKind)i;
                bool completed = session.IsSharedMissionCompleted(kind);
                bool selected = selectedSharedMission == i;
                string title = (completed ? "✓ " : "") +
                    Original323PoneglyphSession.SharedMissionNames[i];
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(title, selected ?
                    OrdrMobileUiTheme.SelectedTab : OrdrMobileUiTheme.NavButton,
                    GUILayout.MinHeight(60f), GUILayout.MinWidth(0f),
                    GUILayout.ExpandWidth(true)))
                    selectedSharedMission = selected ? -1 : i;
                if (completed)
                    GUILayout.Label("완료", OrdrMobileUiTheme.FusionReadyChip,
                        GUILayout.Width(62f), GUILayout.MinHeight(60f));
                GUILayout.EndHorizontal();
                if (selected)
                    DrawSharedMissionSelectedDetail(kind, completed);
            }
        }

        private void DrawSharedMissionSelectedDetail(
            Original323SharedMissionKind kind, bool completed)
        {
            GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
            GUILayout.Label(SharedMissionObjectiveText(kind),
                OrdrMobileUiTheme.Subheading);
            if (completed)
            {
                GUILayout.Label("완료", OrdrMobileUiTheme.FusionReadyChip);
                GUILayout.EndVertical();
                return;
            }
            int round = Combat == null ? 0 : Combat.Game.CurrentWave;
            if ((kind == Original323SharedMissionKind.FastAlabasta && round >= 9) ||
                (kind == Original323SharedMissionKind.DestructionKing && round >= 30))
            {
                GUILayout.Label("기한 종료", OrdrMobileUiTheme.FusionMissingChip);
                GUILayout.EndVertical();
                return;
            }

            bool observed = false;
            if (controller.SharedObjectives != null && Combat != null)
                foreach (var encounter in controller.SharedObjectives.Encounters)
                {
                    if (encounter.Plan.Mission != kind) continue;
                    observed = true;
                    DrawSharedMissionObservedEncounter(encounter);
                }

            // No guessed eligibility: future character matching uses the
            // verified original mission + concrete owned instance mapping.
            // Story/treasure objectives remain source-event-driven and must
            // not be faked by a tap on this card.
            if (!observed)
            {
                string action = Original323SharedObjectiveSpawnPlan
                    .RequiresCombatDefeat(kind) ? "수행 준비 중" :
                    kind == Original323SharedMissionKind.TreasureHunter ?
                        "보물 탐색에서 진행" : "스토리에서 진행";
                bool prior = GUI.enabled;
                GUI.enabled = false;
                GUILayout.Button(action, OrdrMobileUiTheme.NavButton,
                    GUILayout.MinHeight(56f), GUILayout.ExpandWidth(true));
                GUI.enabled = prior;
            }
            GUILayout.EndVertical();
        }

        private static string SharedMissionObjectiveText(
            Original323SharedMissionKind kind)
        {
            switch (kind)
            {
                case Original323SharedMissionKind.RumbarSouls:
                    return "1시 지역의 룸바 해적단 영혼 해방";
                case Original323SharedMissionKind.JusticeGate:
                    return "3시 지역의 정의의 문 파괴";
                case Original323SharedMissionKind.ShandoraBell:
                    return "7시 지역의 샨도라 황금 종 파괴";
                case Original323SharedMissionKind.SeaKing:
                    return "9시 지역의 해왕류 격파";
                case Original323SharedMissionKind.FastAlabasta:
                    return "9라운드 시작 전 알라바스타 파괴";
                case Original323SharedMissionKind.DestructionKing:
                    return "30라운드 시작 전 드레스로자 파괴";
                case Original323SharedMissionKind.MarineAdmiral:
                    return "정의의 문 파괴 후 등장한 해군대장 격파";
                case Original323SharedMissionKind.TreasureHunter:
                    return "보물 탐색으로 목표 수량 달성";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private void DrawSharedMissionObservedEncounter(
            Original323SharedObjectiveEncounter encounter)
        {
            bool alive = encounter.State == Original323SharedObjectiveState.Alive;
            string state = alive ? "전투 중" :
                encounter.State == Original323SharedObjectiveState.Completed ?
                    "완료" :
                encounter.State == Original323SharedObjectiveState.DefeatedUnverified ?
                    "격파 · 정산 대기" : "소멸";
            long health = 0, maxHealth = 0;
            if (alive && Combat != null)
                foreach (var enemy in Combat.MobileEnemySnapshot())
                    if (enemy.Id == encounter.EnemyId)
                    {
                        health = enemy.Health;
                        maxHealth = enemy.MaxHealth;
                        break;
                    }
            GUILayout.Label(state + (alive && maxHealth > 0 ?
                " · HP " + health + "/" + maxHealth : ""),
                OrdrMobileUiTheme.Subheading);

            if (Combat == null) return;
            long? selected = Combat.Roster.SelectedId;
            bool deployed = selected.HasValue &&
                Combat.SharedObjectiveTargetOf(selected.Value) == encounter.EnemyId;
            bool previous = GUI.enabled;
            GUI.enabled = previous && selected.HasValue &&
                (alive || deployed) && !controller.IsUnitPreparation;
            // Existing verified encounter dispatch/recall is preserved, not
            // replaced with an invented generic mission-start command.
            if (GUILayout.Button(deployed ? "복귀" : "출전",
                OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(56f)) &&
                selected.HasValue)
                notice = deployed
                    ? controller.RequestSharedObjectiveRecall(selected.Value)
                    : controller.RequestSharedObjectiveDispatch(
                        selected.Value, encounter.EnemyId);
            GUI.enabled = previous;
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
                string visibleRewards = MissionRewardText(rule.Rewards);
                if (!string.IsNullOrEmpty(visibleRewards))
                    GUILayout.Label("보상: " + visibleRewards);
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
            if (missions.AllThreeCompleted)
                GUILayout.Label("개인 미션 3개 완료",
                    OrdrMobileUiTheme.Subheading);
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
                    case OriginalMissionRewardKind.TraitPoint:
                        // Mission panel intentionally omits trait-point UI.
                        // The original mission reward transaction still pays it.
                        continue;
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
            // Preserve the latest main behavior: selecting an owned Marco
            // reveals its F action at the start of the existing skill ribbon.
            long? selected = Combat == null ? null : Combat.Roster.SelectedId;
            if (selected != dedicatedSkillLastSelectedUnit)
            {
                dedicatedSkillLastSelectedUnit = selected;
                dedicatedScroll = Vector2.zero;
                skillScrollGesture.Cancel();
                skillScrollViewport = new Rect();
            }
            GUILayout.BeginArea(area, OrdrMobileUiTheme.Panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label("스킬 발동",OrdrMobileUiTheme.Overline,GUILayout.Width(65f),
                GUILayout.MinHeight(48f));

            var actions = controller.DedicatedSkills;
            if (Event.current.type == EventType.Repaint)
                skillScrollViewport = new Rect();
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
                    dedicatedScroll = GUILayout.BeginScrollView(dedicatedScroll, false, false,
                        GUIStyle.none, GUIStyle.none, GUILayout.ExpandHeight(true));
                    GUILayout.BeginHorizontal();
                    foreach (var skill in available)
                    {
                        if (skill == null) continue;
                        bool previous = GUI.enabled;
                        GUI.enabled = previous && skill.IsUnlocked && skill.CanExecute;
                        bool pressed = GUILayout.Button(
                            skill.Title + "\n" + skill.OriginalCostAndRules,
                            OrdrMobileUiTheme.SkillButton,GUILayout.Width(220f),
                            GUILayout.Height(Mathf.Max(48f, area.height - 30f)));
                        GUI.enabled = previous;
                        if (pressed)
                        {
                            ManualActionResult result = actions.Request(skill.Id);
                            dedicatedNotice = result.Message;
                            ShowResult(result);
                        }
                    }
                    GUILayout.EndHorizontal();
                    Rect content = GUILayoutUtility.GetLastRect();
                    GUILayout.EndScrollView();
                    if (Event.current.type == EventType.Repaint)
                    {
                        Rect viewport = GUILayoutUtility.GetLastRect();
                        skillScrollViewport = new Rect(area.x + viewport.x, area.y + viewport.y,
                            viewport.width, viewport.height);
                        skillScrollMaximum = Mathf.Max(0f, content.width - viewport.width);
                        dedicatedScroll.x = Mathf.Clamp(dedicatedScroll.x, 0f, skillScrollMaximum);
                    }
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
            // Selection always reveals the skill summary at the TOP of the
            // same panel, never navigates to a separate details page.
            long? selectedInstance = Combat == null ? (long?)null :
                Combat.Roster.SelectedId;
            if (selectedInstance != lastUnitSkillSelection)
            {
                lastUnitSkillSelection = selectedInstance;
                showUnitDetails = false;
                showAllSelectedSkills = false;
                scroll = Vector2.zero;
            }
            // A real roster selection is the only source of unit detail.
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
            // Skill details remain inline with the unit header rather than
            // displacing the roster or opening a different page.
            showUnitDetails = false;

            // The selected character and inventory are the everyday workflow.
            // Evolution, tokens and source item shells are still available
            // below in collapsible management sections, without new tabs.
            GUILayout.Label("유닛 관리", OrdrMobileUiTheme.Subheading);
            if (detail != null)
            {
                DrawSelectedUnitCard(detail);
                if (Combat.Roster.SelectedId.HasValue) DrawSelectedUnitReroll(Combat.Roster.SelectedId.Value);
            }
            DrawOwnedUnitIndex();
            DrawOptionalUnitManagement();
        }

        private void DrawSelectedUnitReroll(long actor)
        {
            var service = controller.PracticeUnitReroll;
            if (service == null || !service.HasRerollAbility(actor)) return;
            if (pendingUnitReroll.HasValue && pendingUnitReroll.Value != actor) pendingUnitReroll = null;
            GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
            GUILayout.Label("유닛 리롤 · " + service.CostLabel(actor));
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && service.CanReroll(actor);
            if (pendingUnitReroll == actor)
            {
                GUILayout.Label("선택한 유닛을 소모하고 다른 유닛 1기로 교체합니다.");
                if (GUILayout.Button("리롤 확정", GUILayout.MinHeight(64f)))
                {
                    ShowResult(controller.RequestUnitReroll(actor));
                    pendingUnitReroll = null;
                }
            }
            else if (GUILayout.Button("선택 유닛 리롤", GUILayout.MinHeight(64f)))
                pendingUnitReroll = actor;
            GUI.enabled = enabled;
            if (pendingUnitReroll.HasValue && GUILayout.Button("취소", GUILayout.MinHeight(60f)))
                pendingUnitReroll = null;
            GUILayout.EndVertical();
        }

        private void DrawSelectedUnitCard(UnitImplementationDefinition detail)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            DrawCharacterPortrait(detail.Unit.Id, 72f);
            GUILayout.BeginVertical();
            OrdrMobileUiTheme.DrawStatusRow("선택 유닛",
                detail.DisplayName + " · " + detail.Grade, OrdrMobileUiTheme.Gold);
            if (Combat != null && Combat.Roster.SelectedId.HasValue)
            {
                var resources = Combat.ResourcesOf(Combat.Roster.SelectedId.Value);
                if (resources != null)
                    GUILayout.Label("HP " + resources.Health.ToString("0.#") +
                        "/" + resources.MaximumHealth.ToString("0.#") +
                        "  ·  MP " + resources.Mana.ToString("0.#") +
                        "/" + resources.MaximumMana.ToString("0.#"),
                        OrdrMobileUiTheme.Overline);
                DrawSelectedHeroProgress(Combat.Roster.SelectedId.Value);
                var form = Combat.PracticeFormOf(Combat.Roster.SelectedId.Value);
                if (!string.IsNullOrEmpty(form))
                {
                    string localized = form == "giant" ? "거대화" :
                        form == "nika" ? "니카같은 미래" :
                        form == "executioner" ? "처형자" :
                        form == "phoenix" ? "불사조" :
                        form == "human" ? "인간형" : form;
                    GUILayout.Label("변신 상태: " + localized, OrdrMobileUiTheme.Overline);
                }
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            if (detail.Unit.Id == "MY-07" && Combat != null &&
                Combat.UsesSourceModelBasicAttacks &&
                Combat.Roster.SelectedId.HasValue)
                DrawSansBoneFeedingPanel(Combat.Roster.SelectedId.Value);
            DrawSelectedUnitSkillSummary(detail);

            if (OrdrMobileUiTheme.DrawAccordionHeader("선택 유닛 세부 상태",
                detail.Grade, showSelectedUnitExtras, OrdrMobileUiTheme.Aqua))
                showSelectedUnitExtras = !showSelectedUnitExtras;
            if (showSelectedUnitExtras && Combat != null &&
                Combat.Roster.SelectedId.HasValue)
            {
                long selectedId = Combat.Roster.SelectedId.Value;
                GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
                if (detail.Unit.Id == "TR-30")
                    GUILayout.Label("위성 구매: " + Combat.PracticeSatellites().Count +
                        "/3 (객체 평타 모델 가동 · 고유 스킬 미검증)",
                        OrdrMobileUiTheme.Overline);
                if (detail.Unit.Id == "I-05" && Combat.UsesSourceModelBasicAttacks)
                {
                    var equipped = Combat.SourceModelEquippedItemsOf(selectedId);
                    GUILayout.Label("확인된 장착: 공격 " + equipped.Attack +
                        " · 공속 " + equipped.AttackSpeed + " · 방어 " + equipped.Armor,
                        OrdrMobileUiTheme.Overline);
                    GUILayout.Label("유니크 " +
                        (equipped.UniqueAttack + equipped.UniqueSpeed + equipped.UniqueArmor) +
                        " · 전설 " +
                        (equipped.LegendaryAttack + equipped.LegendarySpeed +
                         equipped.LegendaryArmor) +
                        " · 아르마다 자격 " +
                        Combat.SourceModelBulletArmadaThresholdOf(selectedId) + "/2" +
                        " (실제 기술 단계와 별개)", OrdrMobileUiTheme.Overline);
                    if (controller.HasOriginalEquippedInventorySnapshotWitness &&
                        GUILayout.Button("원본 장비 동기화", GUILayout.MinHeight(56)))
                        notice = controller.TrySynchronizeOriginalEquippedInventory(selectedId)
                            ? "원본 장비 보유 상태 갱신" :
                            "변경된 원본 장비 증거가 없거나 동기화가 제한됩니다.";
                }
                if (Combat.UsesSourceModelBasicAttacks)
                {
                    var nativeArmor = Combat.PracticeAllyNativeArmorBuffOf(selectedId);
                    var effectiveArmor = Combat.PracticeAllyEffectiveArmorOf(selectedId);
                    if (nativeArmor.HasValue && nativeArmor.Value > 0 &&
                        effectiveArmor.HasValue)
                        GUILayout.Label("원본 방어 상태 +" +
                            nativeArmor.Value.ToString("0.##") +
                            " · 현재 방어 " + effectiveArmor.Value.ToString("0.##") +
                            " (아군 피격 계산 연결 전)", OrdrMobileUiTheme.Overline);
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndVertical();
        }

        private void DrawSelectedUnitSkillSummary(UnitImplementationDefinition detail)
        {
            GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
            GUILayout.Label("스킬 · 공격 정보", OrdrMobileUiTheme.Subheading);
            if (detail.Profile == null)
                GUILayout.Label(detail.Unit.Id == "C-01" ?
                    "추가 스킬 없음" : "스킬 설명 준비 중",
                    OrdrMobileUiTheme.Explanation);
            else if (SourceModelTwentyFifthSkillCatalog.IsKnownForeignProfile(
                detail.Profile))
                GUILayout.Label("다른 유닛의 기술 설명은 표시하지 않습니다.",
                    OrdrMobileUiTheme.Explanation);
            else
            {
                int total = detail.Profile.Skills.Count;
                int visible = showAllSelectedSkills ? total : Math.Min(3, total);
                for (int i = 0; i < visible; i++)
                {
                    var skill = detail.Profile.Skills[i];
                    string meta = "";
                    if (skill.ChancePercent.HasValue)
                        meta = "발동 " + skill.ChancePercent.Value.ToString("0.##") + "%";
                    if (skill.CooldownSeconds.HasValue)
                        meta += (meta.Length == 0 ? "" : " · ") +
                            "쿨 " + skill.CooldownSeconds.Value.ToString("0.##") + "초";
                    GUILayout.Label("• " + skill.Name +
                        (meta.Length == 0 ? "" : "  " + meta),
                        OrdrMobileUiTheme.Explanation);
                }
                if (total == 0)
                    GUILayout.Label("추가 발동 스킬 없음",
                        OrdrMobileUiTheme.Explanation);
                if (total > 3 && GUILayout.Button(showAllSelectedSkills ?
                    "스킬 접기" : "스킬 전체 " + total + "개",
                    OrdrMobileUiTheme.NavButton, GUILayout.MinHeight(48f)))
                    showAllSelectedSkills = !showAllSelectedSkills;
                if (detail.PassiveCount > 0)
                    GUILayout.Label("패시브 " + detail.PassiveCount + "개",
                        OrdrMobileUiTheme.Explanation);
            }
            GUILayout.EndVertical();
        }

        // Selected Sans only: two large explicit consumption actions, not a
        // background auto-cast that could silently eat fusion materials.
        private void DrawSansBoneFeedingPanel(long sansInstanceId)
        {
            int bones = Combat.SourceModelSansBonesOf(sansInstanceId);
            bool enhanced = bones >= 30;
            int uncommon = 0, rare = 0;
            bool storyLane = Combat.IsStoryDeployed(sansInstanceId);
            foreach (var unit in OwnedUnitsForFrame())
            {
                if (Combat.IsStoryDeployed(unit.InstanceId) != storyLane) continue;
                if (unit.Unit.Id == "U-03") uncommon++;
                else if (unit.Unit.Id == "R-18") rare++;
            }
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("체력본충(Q) · 브룩 먹이기", OrdrMobileUiTheme.Subheading);
            OrdrMobileUiTheme.DrawStatusRow("샌즈 뼈",
                bones + "개" + (enhanced ? " · 강화 완료" : " / 30개"),
                enhanced ? OrdrMobileUiTheme.Gold : OrdrMobileUiTheme.Aqua);
            Rect bar = GUILayoutUtility.GetRect(10f, 14f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                Color previousColor = GUI.color;
                GUI.color = new Color(.13f, .19f, .30f, 1f);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = enhanced ? OrdrMobileUiTheme.Gold : OrdrMobileUiTheme.Aqua;
                GUI.DrawTexture(new Rect(bar.x, bar.y,
                    bar.width * Mathf.Clamp01(bones / 30f), bar.height),
                    Texture2D.whiteTexture);
                GUI.color = previousColor;
            }
            GUILayout.Label(enhanced ?
                "강화 상태 · 심판자 해금 · 강화 가스터 적용" :
                "뼈 30개 달성 시 강화 · 뼈 5개마다 단단한 뼈 추가 시전",
                OrdrMobileUiTheme.Overline);
            GUILayout.BeginHorizontal();
            DrawSansBrookFeedingOption(sansInstanceId, "U-03",
                "브룩 · 안흔함", 2, uncommon);
            DrawSansBrookFeedingOption(sansInstanceId, "R-18",
                "브룩 · 희귀함", 12, rare);
            GUILayout.EndHorizontal();
            GUILayout.Label("누르면 해당 브룩 1기가 즉시 소모됩니다. 별도 확인창은 없습니다.",
                OrdrMobileUiTheme.Overline);
            GUILayout.EndVertical();
        }

        private void DrawSansBrookFeedingOption(long sansInstanceId,
            string brookId, string label, int boneGain, int available)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true),
                GUILayout.MinWidth(0f));
            DrawCharacterPortrait(brookId, 54f);
            GUILayout.Label(label, OrdrMobileUiTheme.Overline);
            GUILayout.Label("보유 " + available + "기", OrdrMobileUiTheme.Overline);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && available > 0 &&
                Combat.Game.Phase == SessionPhase.Running;
            bool clicked = GUILayout.Button("먹이기 · 뼈 +" + boneGain,
                OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(64f));
            GUI.enabled = enabled;
            if (clicked)
            {
                ShowResult(controller.RequestSansBoneFeeding(sansInstanceId, brookId));
                // A consumed Brook must disappear from every owned-unit view
                // during the same input frame, not a stale cached roster page.
                ownedFrameSnapshotAt = -1;
            }
            GUILayout.EndVertical();
        }

        // Use the same live XP/trait state as the attack and mana resolvers.
        // Non-hero units never receive fabricated levels or upgrade effects.
        private void DrawSelectedHeroProgress(long instanceId)
        {
            var experience = controller.StoryHeroExperience;
            var hero = experience == null ? null : experience.LevelStateOf(instanceId);
            if (hero == null) return;

            OrdrMobileUiTheme.DrawStatusRow("영웅 레벨",
                "Lv." + hero.Level + " / " + Original323HeroLevelRules.MaximumLevel,
                OrdrMobileUiTheme.Gold);
            long completed = Original323HeroLevelRules.TotalExperienceForLevel(hero.Level);
            if (hero.Level < Original323HeroLevelRules.MaximumLevel)
            {
                long next = Original323HeroLevelRules.TotalExperienceForLevel(hero.Level + 1);
                float ratio = (float)Math.Max(0d, Math.Min(1d,
                    (double)(hero.Experience - completed) / (next - completed)));
                GUILayout.Label("경험치  " + (hero.Experience - completed).ToString("N0") +
                    " / " + (next - completed).ToString("N0") + " XP",
                    OrdrMobileUiTheme.Overline);
                Rect bar = GUILayoutUtility.GetRect(10f, 16f, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint)
                {
                    Color savedColor = GUI.color;
                    GUI.color = new Color(0.13f, 0.19f, 0.3f, 1f);
                    GUI.DrawTexture(bar, Texture2D.whiteTexture);
                    GUI.color = OrdrMobileUiTheme.Gold;
                    GUI.DrawTexture(new Rect(bar.x, bar.y,
                        bar.width * ratio, bar.height), Texture2D.whiteTexture);
                    GUI.color = savedColor;
                }
                GUILayout.Label("다음 레벨까지 " +
                    hero.ExperienceToNextLevel.ToString("N0") + " XP",
                    OrdrMobileUiTheme.Overline);
            }
            else
                GUILayout.Label("최대 레벨 · 누적 " +
                    hero.Experience.ToString("N0") + " XP", OrdrMobileUiTheme.Overline);

            GUILayout.Label("레벨 성장  힘 +" + hero.AddedStrength +
                " · 민첩 +" + hero.AddedAgility +
                " · 지능 +" + hero.AddedIntelligence,
                OrdrMobileUiTheme.Overline);
            DrawSelectedTraitUpgrade(instanceId);
        }

        private void DrawSelectedTraitUpgrade(long instanceId)
        {
            var trait = controller.ShirahoshiTrait;
            if (trait == null) return;
            GUILayout.Label(
                "특성포인트 획득: 스토리 8~13단계 · 개인 미션 '트리플' · 졸업 도박소 15000골드",
                OrdrMobileUiTheme.Overline);
            if (!trait.IsEligible(instanceId))
            {
                GUILayout.Label("특성강화: 이 유닛의 원본 구매 효과는 아직 연결되지 않았습니다.",
                    OrdrMobileUiTheme.Overline);
                return;
            }
            long balance = controller.SharedEconomy.Wallet.Balance(
                OriginalMissionWalletRewards.TraitPointsId);
            bool activated = trait.HasActivated(instanceId);
            OrdrMobileUiTheme.DrawStatusRow("시라호시 4포인트 특성",
                activated ? "해금 완료 · 레벨 16/20/24 효과 적용" :
                    "특성포인트 " + balance + "개 보유 / " +
                    Original323ShirahoshiTraitPurchase.PointCost + "개 필요",
                OrdrMobileUiTheme.Aqua);
            if (activated) return;
            bool previous = GUI.enabled;
            GUI.enabled = previous && trait.CanPurchase(instanceId);
            bool pressed = GUILayout.Button("특성강화 · 4포인트 사용",
                OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(60));
            GUI.enabled = previous;
            if (pressed)
            {
                try
                {
                    notice = trait.TryPurchase(instanceId)
                        ? "시라호시 특성강화 완료 · 레벨 16/20/24 효과 활성화"
                        : "특성포인트 또는 원본 영웅 자격을 다시 확인해 주세요.";
                }
                catch (InvalidOperationException error)
                {
                    notice = "특성강화 실패 · " + error.Message;
                }
            }
        }

        private void DrawOptionalUnitManagement()
        {
            GUILayout.Space(8f);
            GUILayout.Label("기타 유닛 관리", OrdrMobileUiTheme.Subheading);
            var evolution = controller.EvolutionProgress;
            string evolutionStatus = evolution == null ? "상태 없음" :
                evolution.IsReleased ? "해방 완료" :
                evolution.IsActivated ? "해방 진행 중" : "자동 활성화 대기";
            if (OrdrMobileUiTheme.DrawAccordionHeader("진화체",
                evolutionStatus, showEvolutionManagement, OrdrMobileUiTheme.Gold))
                showEvolutionManagement = !showEvolutionManagement;
            if (showEvolutionManagement)
            {
                GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
                DrawStartingEvolutionSelection();
                GUILayout.EndVertical();
            }

            int tokenCount = Combat == null ? 0 :
                MobileLuckyTokenInventory.Count(OwnedUnitsForFrame());
            if (OrdrMobileUiTheme.DrawAccordionHeader("행운의 토큰",
                tokenCount + "개", showLuckyTokenManagement, OrdrMobileUiTheme.Gold))
                showLuckyTokenManagement = !showLuckyTokenManagement;
            if (showLuckyTokenManagement)
            {
                GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
                DrawLuckyTokenManagement();
                GUILayout.EndVertical();
            }

            if (OrdrMobileUiTheme.DrawAccordionHeader("원본 조합 아이템",
                "보유 수량·재료", showOriginalItemManagement, OrdrMobileUiTheme.Aqua))
                showOriginalItemManagement = !showOriginalItemManagement;
            if (showOriginalItemManagement)
            {
                GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
                DrawOriginalFusionItemShells();
                GUILayout.EndVertical();
            }
        }

        // Lucky tokens remain LT-01 physical units in the actual roster.
        // Do not spend them via a separate virtual currency or unverified UI.
        private void DrawLuckyTokenManagement()
        {
            if (Combat == null) return;
            int owned = MobileLuckyTokenInventory.Count(Combat.MobileUnitSnapshot());
            GUILayout.BeginVertical(GUI.skin.box);
            OrdrMobileUiTheme.DrawStatusRow("행운의 토큰", owned + "개",
                OrdrMobileUiTheme.Gold);
            GUILayout.Label("신비함 유닛 조합 재료 · 실제 보유 유닛과 수량 공유",
                OrdrMobileUiTheme.Explanation);
            GUILayout.Label("원본 토큰 조합 14건 중 결과 매핑 13건 · " +
                "제작 연결 " + controller.ExecutableMysticFusionCount +
                "건 (PICK 미검증 6건 · 도감 외 유지로 포함)",
                OrdrMobileUiTheme.Explanation);
            GUILayout.Label("위습 교환: 3개 → 특별위습 80% / 희귀위습 20%",
                OrdrMobileUiTheme.Explanation);
            if (GUILayout.Button("행운의 토큰 사용 조합 보기", GUILayout.MinHeight(64)))
            {
                fusionListCategory = FusionListCategory.All;
                fusionGradeFilter = "전체";
                fusionPage = 0;
                originalJassPage = 0;
                SelectTab(Tab.Fusion);
                // A token shortcut is a removable list filter, not a name-search box.
                fusionShortcutQuery = "토큰";
            }
            OrdrMobileUiTheme.DrawStatusRow("위습 교환",
                owned < MobileLuckyTokenInventory.WispExchangeCost
                    ? "보유 " + owned + "/3 · 토큰 부족"
                    : controller.CanExchangeLuckyTokens
                        ? "교환 가능 · 특별 80% / 희귀 20%"
                        : "보상 풀 미연결 또는 전투 일시중지",
                OrdrMobileUiTheme.Aqua);
            bool previouslyEnabled = GUI.enabled;
            GUI.enabled = previouslyEnabled && controller.CanExchangeLuckyTokens;
            if (GUILayout.Button("행운의 토큰 3개 교환",
                OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(64)))
                RunWispDirect("행운의 토큰 위습 교환",
                    () => controller.RequestLuckyTokenWispExchange());
            GUI.enabled = previouslyEnabled;
            GUILayout.EndVertical();
        }

        // AI00/AI01/AI02 are deliberately non-interactive shells until
        // the native item abilities and inventory-slot visuals are modeled.
        // Their counts come from the SAME wallet consumed by CWR fusion.
        private void DrawOriginalFusionItemShells()
        {
            if (controller.SharedEconomy == null) return;
            var wallet = controller.SharedEconomy.Wallet;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("원본 조합 아이템", OrdrMobileUiTheme.Subheading);
            GUILayout.Label("아이템 효과는 추후 구현 · 조합 재료/보유 수량만 연결",
                OrdrMobileUiTheme.Explanation);
            foreach (var item in Original323FusionSourceItemLedger.ItemShells)
            {
                long count = Original323FusionSourceItemLedger.OwnedShellCount(
                    wallet, item.Rawcode);
                OrdrMobileUiTheme.DrawStatusRow(item.DisplayName,
                    count + "개", OrdrMobileUiTheme.Gold);
                GUILayout.Label(item.OriginalFusionUse,
                    OrdrMobileUiTheme.Explanation);
            }
            GUILayout.EndVertical();
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
            if (Combat != null && Combat.Roster.SelectedId.HasValue)
            {
                DrawSelectedHeroProgress(Combat.Roster.SelectedId.Value);
                if (detail.Unit.Id == "MY-07" && Combat.UsesSourceModelBasicAttacks)
                    DrawSansBoneFeedingPanel(Combat.Roster.SelectedId.Value);
            }
            if (controller.PracticeBattle != null)
            {
                var native = controller.MapBaseStats.Find(detail.Unit.Id);
                var sourceAttack = native?.Attack1 ??
                    controller.MapBaseStats.FindMapObjectAttack(
                        detail.Unit.OriginalSourceRawcode);
                OrdrMobileUiTheme.DrawStatusRow("기본 공격",
                    sourceAttack != null ? "객체 모델" : "정보 없음",
                    OrdrMobileUiTheme.Aqua);
                var effects = Combat.PracticeUnitEffects;
                if (effects != null)
                {
                    var connected = effects.Describe(detail.Unit.Id,
                        native?.OriginalRawcode ?? detail.Unit.OriginalSourceRawcode);
                    foreach (string text in connected)
                        GUILayout.Label("· " + text);
                }
            }
            if (detail.Profile == null && detail.Unit.Id == "C-01")
                GUILayout.Label("추가 스킬 없음");
            else if (detail.Profile == null)
                GUILayout.Label("스킬 정보 준비 중");
            else if (SourceModelTwentyFifthSkillCatalog.IsKnownForeignProfile(
                detail.Profile))
            {
                // The imported tooltip belongs to Wiper/Sai, not the map's
                // Kalgara/distorted Burgess. Do not present it as playable.
                GUILayout.Label("원본 고유 스킬 확인 중 · 잘못된 도감 기술 제외");
                GUILayout.Label("기본 공격 및 검증된 원본 오라/패시브만 적용");
            }
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
                if (detail.Unit.Id == "TR-25" &&
                    SourceModelTwentyFifthSkillCatalog.CanModelExactKidEnhancement(
                        detail.Profile))
                    GUILayout.Label("특성강화: 펑크 크래시 · 댐드 펑크 (구매 자격 확인 시에만 교체)");
                if (detail.Unit.Id == "E-05" && Combat != null &&
                    Combat.Roster.SelectedId.HasValue &&
                    Combat.UsesSourceModelBasicAttacks)
                {
                    long selectedId = Combat.Roster.SelectedId.Value;
                    int eloquence = Combat.SourceBuggyEloquenceUsesOf(selectedId);
                    GUILayout.Label("언변 강화 " + eloquence + "/5 · 1회 3333골드");
                    bool enabled = GUI.enabled;
                    GUI.enabled = enabled && eloquence < 5 &&
                        controller.SharedEconomy != null &&
                        controller.SharedEconomy.Wallet.Balance("gold") >= 3333;
                    if (GUILayout.Button("언변 강화 구매",
                        OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(64)))
                        notice = Combat.TryPurchaseSourceBuggyEloquence(selectedId)
                            ? "언변 강화 " + (eloquence + 1) + "/5 완료"
                            : "강화 대상·골드·구매 횟수를 확인해 주세요.";
                    GUI.enabled = enabled;
                }
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
            var attack = originalMap != null ? originalMap.Attack1 :
                controller.MapBaseStats == null ? null :
                controller.MapBaseStats.FindMapObjectAttack(detail.Unit.OriginalSourceRawcode);
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
            if (detail.Unit.Id == "E-09" && Combat != null)
            {
                // B7W's chat-only "세계의가희" is represented as a
                // deliberate one-shot action, never an on-attack proc.
                foreach (var unit in Combat.MobileUnitSnapshot())
                {
                    if (unit.Unit.Id != "E-09") continue;
                    bool ready = Combat.CanStartSourceModelUtaIntro(unit.InstanceId);
                    bool previousEnabled = GUI.enabled;
                    GUI.enabled = previousEnabled && ready;
                    if (GUILayout.Button("우타 · 세계의 가희" +
                        " (" + unit.InstanceId + ")",
                        OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(64)))
                        notice = Combat.TryStartSourceModelUtaIntro(unit.InstanceId)
                            ? "세계의 가희 공연 시작" : "공연 실행 조건을 확인해 주세요.";
                    GUI.enabled = previousEnabled;
                    if (!ready)
                        GUILayout.Label("해당 유닛 공연 진행 완료 또는 현재 이용 불가",
                            OrdrMobileUiTheme.Explanation);
                }
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
            GUILayout.BeginVertical(OrdrMobileUiTheme.ConfirmationPanel);
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

        // OnGUI handles more than one event per rendered frame. Keep each
        // frame's read-only roster stable without caching past gameplay ticks.
        private IReadOnlyList<MobileUnitSnapshot> OwnedUnitsForFrame()
        {
            var runtime = Combat;
            if (runtime == null) return Array.Empty<MobileUnitSnapshot>();
            if (ownedFrameSnapshotAt != Time.frameCount ||
                !ReferenceEquals(ownedFrameCombat, runtime) ||
                ownedFrameSnapshot == null)
            {
                ownedFrameSnapshot = runtime.MobileUnitSnapshot();
                ownedFrameCombat = runtime;
                ownedFrameSnapshotAt = Time.frameCount;
            }
            return ownedFrameSnapshot;
        }

        private void DrawOwnedGradePicker(IReadOnlyList<MobileUnitSnapshot> owned)
        {
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var unit in owned)
            {
                int previous;
                totals.TryGetValue(unit.Unit.Grade, out previous);
                totals[unit.Unit.Grade] = previous + 1;
            }
            var grades = new List<string>(totals.Keys);
            grades.Sort((a, b) =>
            {
                int rank = MobileFusionBrowser.GradePriority(b)
                    .CompareTo(MobileFusionBrowser.GradePriority(a));
                return rank != 0 ? rank : string.Compare(a, b, StringComparison.Ordinal);
            });
            string currentGrade = string.IsNullOrEmpty(ownedGradeFilter)
                ? "전체 등급" : ownedGradeFilter;
            if (GUILayout.Button("등급별 보기 · " + currentGrade +
                (showOwnedGradeFilters ? " ▲" : " ▼"), GUILayout.MinHeight(56)))
                showOwnedGradeFilters = !showOwnedGradeFilters;
            if (!showOwnedGradeFilters) return;
            GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
            if (GUILayout.Button(string.IsNullOrEmpty(ownedGradeFilter)
                ? "전체 등급 ✓" : "전체 등급", GUILayout.MinHeight(54)))
            {
                ownedGradeFilter = null;
                ownedPage = 0;
                expandedOwnedGroupId = null;
                showOwnedGradeFilters = false;
            }
            foreach (string grade in grades)
            {
                string label = grade + " · " + totals[grade] + "기" +
                    (ownedGradeFilter == grade ? " ✓" : "");
                if (!GUILayout.Button(label, GUILayout.MinHeight(54))) continue;
                ownedGradeFilter = ownedGradeFilter == grade ? null : grade;
                ownedPage = 0;
                expandedOwnedGroupId = null;
                showOwnedGradeFilters = false;
            }
            GUILayout.EndVertical();
        }

        private void PruneStaleBatchSaleSelection(IReadOnlyList<MobileUnitSnapshot> owned)
        {
            if (pendingBatchSales.Count == 0) return;
            var alive = new HashSet<long>();
            foreach (var unit in owned) alive.Add(unit.InstanceId);
            int before = pendingBatchSales.Count;
            pendingBatchSales.RemoveAll(id => !alive.Contains(id));
            if (before != pendingBatchSales.Count) confirmBatchSale = false;
        }

        private void DrawOwnedUnitIndex()
        {
            if (Combat == null)
            {
                GUILayout.Label("월드가 시작되면 보유 유닛이 표시됩니다.");
                return;
            }
            var owned = OwnedUnitsForFrame();
            PruneStaleBatchSaleSelection(owned);
            OrdrMobileUiTheme.DrawStatusRow("보유 유닛", owned.Count + "기", OrdrMobileUiTheme.Aqua);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("전체" + (ownedFilter == OwnedRosterFilter.All ? " ✓" : ""), GUILayout.MinHeight(56)))
            { ownedFilter = OwnedRosterFilter.All; ownedPage = 0; expandedOwnedGroupId = null; }
            if (GUILayout.Button("판매 가능" + (ownedFilter == OwnedRosterFilter.Saleable ? " ✓" : ""), GUILayout.MinHeight(56)))
            { ownedFilter = OwnedRosterFilter.Saleable; ownedPage = 0; expandedOwnedGroupId = null; }
            if (GUILayout.Button("분해 가능" + (ownedFilter == OwnedRosterFilter.Decomposable ? " ✓" : ""), GUILayout.MinHeight(56)))
            { ownedFilter = OwnedRosterFilter.Decomposable; ownedPage = 0; expandedOwnedGroupId = null; }
            GUILayout.EndHorizontal();
            DrawOwnedGradePicker(owned);

            int unitCount, groupCount, pages, resolved;
            var displayed = MobileOwnedRosterBrowser.Group(owned, ownedFilter,
                x => controller.LowerGradeSales != null && controller.LowerGradeSales.CanSell(x),
                ownedPage, 8, out unitCount, out groupCount, out pages, out resolved,
                ownedGradeFilter);
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

            string previousGrade = null;
            foreach (var group in displayed)
            {
                if (previousGrade != group.Grade)
                {
                    previousGrade = group.Grade;
                    GUILayout.Label(group.Grade + " · 높은 등급순",
                        OrdrMobileUiTheme.Subheading);
                }
                bool isSelected = group.Contains(Combat.Roster.SelectedId);
                bool open = expandedOwnedGroupId == group.UnitId;
                string meta = group.Grade + " · " + group.Count + "기" +
                    (isSelected ? " · #" + Combat.Roster.SelectedId.Value + " 선택" : "");
                GUILayout.BeginHorizontal();
                DrawCharacterPortrait(group.UnitId, 58f);
                if (OrdrMobileUiTheme.DrawActionRow(group.Name + " ×" + group.Count,
                    meta, OrdrMobileUiTheme.Aqua, 72f))
                {
                    // The grouped card selects a representative; the
                    // instance list below selects a precise copy.
                    long next = isSelected ? Combat.Roster.SelectedId.Value :
                        group.Members[0].InstanceId;
                    Combat.Roster.Select(next);
                    selectedCatalogUnitId = group.UnitId;
                    showUnitDetails = false;
                    showAllSelectedSkills = false;
                    scroll = Vector2.zero;
                    AddBattleLog(group.Name + " 선택", group.UnitId);
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
                            showUnitDetails = false;
                            showAllSelectedSkills = false;
                            scroll = Vector2.zero;
                            AddBattleLog(entry.Unit.Name + " 선택", entry.Unit.Id);
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
                GUILayout.BeginVertical(OrdrMobileUiTheme.ConfirmationPanel);
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
                GUILayout.EndVertical();
            }
            if (pendingDecompositionId == instanceId)
            {
                GUILayout.BeginVertical(OrdrMobileUiTheme.ConfirmationPanel);
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
                GUILayout.EndVertical();
            }
        }

        private static void DrawCharacterPortrait(string unitId, float size)
        {
            Sprite portrait = OrdrCharacterPortraits.Find(unitId);
            if (portrait == null)
                portrait = OrdrBattlefieldArt.UnitSprite(unitId);
            if (portrait != null)
                DrawSpritePortrait(portrait, size);
        }

        // Sprite.texture can be an atlas; drawing the whole texture makes the
        // wrong character art appear. Always crop by the source sprite rect.
        // Shared by owned units, fusion result and fusion ingredients.
        private static void DrawSpritePortrait(Sprite portrait, float size)
        {
            Rect area = GUILayoutUtility.GetRect(size, size,
                GUILayout.Width(size), GUILayout.Height(size));
            if (portrait == null || Event.current.type != EventType.Repaint)
                return;
            Rect part = portrait.rect;
            Texture texture = portrait.texture;
            if (texture == null) return;
            GUI.DrawTextureWithTexCoords(area, texture, new Rect(
                part.x / texture.width, part.y / texture.height,
                part.width / texture.width, part.height / texture.height));
        }

        private void DrawPager(int page, int pages, Action<int> changePage)
        {
            if (pages < 2) return;
            bool previous = GUI.enabled;
            GUILayout.BeginHorizontal();
            GUI.enabled = previous && page > 0;
            if (GUILayout.Button("이전", OrdrMobileUiTheme.SmallButton,
                GUILayout.Width(68f), GUILayout.MinHeight(56)))
            { changePage(page - 1); scroll = Vector2.zero; }
            GUI.enabled = previous;
            GUILayout.Label((page + 1) + " / " + pages, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            GUI.enabled = previous && page + 1 < pages;
            if (GUILayout.Button("다음", OrdrMobileUiTheme.SmallButton,
                GUILayout.Width(68f), GUILayout.MinHeight(56)))
            { changePage(page + 1); scroll = Vector2.zero; }
            GUI.enabled = previous;
            GUILayout.EndHorizontal();
        }

        // One shared menu in Story and Wisp. ① expands into direct mobile
        // redemptions; neither path creates an inert intermediate box actor.
        private void DrawOriginalTranscendWispChoiceButtons()
        {
            GUILayout.Label("초월위습 1개로 보상 하나 선택",
                OrdrMobileUiTheme.Explanation);
            if (OrdrMobileUiTheme.DrawAccordionHeader(
                "① 꾸러미 상자 - 초월", "보상 2종", showTranscendBoxChoices,
                OrdrMobileUiTheme.Gold))
                showTranscendBoxChoices = !showTranscendBoxChoices;
            if (showTranscendBoxChoices)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("상자 생성 없이 보상 즉시 수령 · 추가 확인 없음",
                    OrdrMobileUiTheme.Explanation);
                if (GUILayout.Button("골드 " +
                    Original323KumaWispChoiceWorld.BoxGoldMin.ToString("N0") +
                    "~" + Original323KumaWispChoiceWorld.BoxGoldMax.ToString("N0") +
                    " · 목재 " + Original323KumaWispChoiceWorld.BoxWoodMin +
                    "~" + Original323KumaWispChoiceWorld.BoxWoodMax +
                    " 무작위 수령",
                    OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(58)))
                    RunWispDirect("초월위습 · 골드·목재 보상",
                        () => controller.RequestOriginalTranscendBoxResourcesChoice());
                GUILayout.Label("원본 CRL: 골드·목재를 각각 독립적으로 추첨합니다.",
                    OrdrMobileUiTheme.Explanation);

                if (OrdrMobileUiTheme.DrawAccordionHeader(
                    "고유 아이템 선택",
                    "10종 중 1개 · 목재 +4",
                    showTranscendBoxItemChoices, OrdrMobileUiTheme.Aqua))
                    showTranscendBoxItemChoices = !showTranscendBoxItemChoices;
                if (showTranscendBoxItemChoices)
                {
                    GUILayout.Label("원본 CRM: 미획득 아이템 1개 선택 시 목재 4개 추가 지급. " +
                        "이미 획득한 고유 아이템은 다시 선택할 수 없습니다.",
                        OrdrMobileUiTheme.Explanation);
                    var candidates = Original323FusionSourceItemLedger.ItemShells;
                    int pageCount = (candidates.Count + TranscendBoxItemPageSize - 1) /
                        TranscendBoxItemPageSize;
                    transcendBoxItemPage = Mathf.Clamp(transcendBoxItemPage, 0,
                        Math.Max(0, pageCount - 1));
                    int start = transcendBoxItemPage * TranscendBoxItemPageSize;
                    for (int i = start; i < Math.Min(candidates.Count,
                            start + TranscendBoxItemPageSize); i++)
                    {
                        var item = candidates[i];
                        string rawcode = item.Rawcode;
                        var wallet = controller.SharedEconomy == null ? null :
                            controller.SharedEconomy.Wallet;
                        bool owned = wallet != null &&
                            (wallet.Balance(Original323FusionSourceItemLedger.WalletId(rawcode)) > 0 ||
                             wallet.HasCompletedTransaction("original323:unique-box-item:" + rawcode));
                        bool enabled = GUI.enabled;
                        GUI.enabled = enabled && !owned;
                        if (GUILayout.Button(item.DisplayName +
                            (owned ? " · 획득 완료" : " · 선택 (+목재 4)"),
                            OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(56)))
                        {
                            string chosen = rawcode;
                            RunWispDirect("초월위습 · 고유 아이템 " + chosen,
                                () => controller.RequestOriginalTranscendBoxItemChoice(chosen));
                        }
                        GUI.enabled = enabled;
                        GUILayout.Label(item.OriginalFusionUse, OrdrMobileUiTheme.Explanation);
                    }
                    DrawPager(transcendBoxItemPage, pageCount,
                        next => transcendBoxItemPage = next);
                }
                GUILayout.EndVertical();
            }
            if (GUILayout.Button("② 바솔로뮤 쿠마",
                OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(58)))
                RunWispDirect("스토리 초월위습 · 쿠마",
                    () => controller.RequestOriginalTranscendKumaChoice());
            if (GUILayout.Button("③ 실버즈 레일리 + 해적선",
                OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(58)))
                RunWispDirect("스토리 초월위습 · 레일리·해적선",
                    () => controller.RequestOriginalTranscendRayleighShipChoice());
        }

        private static string GreenBloodSeraphimName(string rawcode)
        {
            switch (rawcode)
            {
                case "h09Y": return "S-베어";
                case "h0A3": return "S-호크";
                case "h0A0": return "S-샤크";
                case "h0A1": return "S-스네이크";
                default: return "세라핌";
            }
        }

        private void DrawStory()
        {
            var scenario = controller.SoloStory;
            var catalog = controller.StoryBuildingCatalog;
            var world = controller.PracticeStoryBuildings;
            if (scenario == null || catalog == null || world == null)
            {
                GUILayout.Label("스토리 건물과 전투 월드 연결을 기다리는 중입니다.");
                return;
            }

            // The player needs one immediate objective, not fourteen large
            // buttons hiding the live fight under a scrolling panel.
            GUILayout.Label("스토리 항해", OrdrMobileUiTheme.Subheading);
            GUILayout.BeginVertical(GUI.skin.box);
            int activeStage = scenario.ActiveStage;
            int cleared = scenario.HighestDestroyedStage;
            string headline = scenario.CompletedAllBuildings ? "14개 스토리 건물 격파 완료" :
                scenario.LostByStory ? "스토리 패배" :
                scenario.EggheadWaitRemaining > 0 ? "에그헤드 등장 대기" :
                activeStage <= 0 ? "첫 스토리 건물 준비 중" :
                activeStage.ToString("00") + " / 14 · " +
                    (catalog.Find(activeStage) == null ? "스토리 건물" :
                        catalog.Find(activeStage).Name);
            OrdrMobileUiTheme.DrawStatusRow("현재 목표", headline,
                scenario.LostByStory ? OrdrMobileUiTheme.Gold : OrdrMobileUiTheme.Aqua);
            OrdrMobileUiTheme.DrawStatusRow("격파 진행",
                cleared + " / " + Original323StoryUnlockRules.FinalStage + "개",
                OrdrMobileUiTheme.Gold);
            Original323StoryBuildingEncounter active = null;
            foreach (var entry in world.Encounters)
                if (entry.Building.Stage == activeStage &&
                    entry.State == Original323StoryBuildingState.Alive)
                    active = entry;
            if (active != null)
            {
                var enemy = Combat.EnemyOf(active.EnemyId);
                if (enemy != null)
                {
                    double percent = enemy.MaxHealth <= 0 ? 0 :
                        Math.Max(0, Math.Min(100, 100.0 * enemy.Health / enemy.MaxHealth));
                    OrdrMobileUiTheme.DrawStatusRow("현재 건물 HP",
                        enemy.Health + " / " + enemy.MaxHealth +
                        " · " + percent.ToString("0.#") + "%",
                        OrdrMobileUiTheme.Aqua);
                }
                if (!active.SourceVerifiedSpawn)
                    GUILayout.Label("전투 HP는 원본 객체 기반 연습 모델입니다.",
                        OrdrMobileUiTheme.Overline);
            }
            if (scenario.EggheadWaitRemaining > 0)
                OrdrMobileUiTheme.DrawStatusRow("에그헤드 출현까지",
                    Math.Ceiling(scenario.EggheadWaitRemaining) + "초",
                    OrdrMobileUiTheme.Gold);
            else if (scenario.EggheadDeadlineRemaining > 0)
                OrdrMobileUiTheme.DrawStatusRow("에그헤드 파괴 기한",
                    Math.Ceiling(scenario.EggheadDeadlineRemaining) + "초",
                    OrdrMobileUiTheme.Gold);
            if (!scenario.CompletedAllBuildings && !scenario.LostByStory &&
                cleared < Original323StoryUnlockRules.NightmareRequiredDestroyedStage &&
                Combat.Game.CurrentWave <= Original323StoryUnlockRules.CheckpointRound)
                GUILayout.Label("악몽 목표: 35라운드 시작 전 와노쿠니(13단계) 격파" +
                    " · 현재 " + Combat.Game.CurrentWave + "라운드",
                    OrdrMobileUiTheme.Explanation);
            if (!string.IsNullOrWhiteSpace(scenario.LastNotice))
                GUILayout.Label(scenario.LastNotice, OrdrMobileUiTheme.Overline);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUI.skin.box);
            OrdrMobileUiTheme.DrawStatusRow("스토리 획득",
                "골드 " + scenario.AwardedGold + " · 목재 " + scenario.AwardedWood,
                OrdrMobileUiTheme.Gold);
            var progression = controller.StoryProgression;
            if (progression != null)
                OrdrMobileUiTheme.DrawStatusRow("연구·유닛 도박",
                    "연구 " + progression.Researches.Count + "개 · 남은 도박 충전 " +
                    progression.GamblingCharges + "회", OrdrMobileUiTheme.Aqua);
            if (controller.StoryHeroExperience != null &&
                scenario.HighestDestroyedStage >= 13)
                OrdrMobileUiTheme.DrawStatusRow("스토리 영웅 경험치",
                    "13·14단계 격파 시 원본 A07O 영웅 그룹에 +300 XP" +
                    " · 영웅 레벨/스탯에 반영",
                    OrdrMobileUiTheme.Aqua);
            if (!string.IsNullOrEmpty(scenario.LastUniqueItemAward))
                OrdrMobileUiTheme.DrawStatusRow("스토리 고유 아이템",
                    scenario.LastUniqueItemAward + " 획득 · 원본 5% 추첨 보상",
                    OrdrMobileUiTheme.Gold);
            OrdrMobileUiTheme.DrawStatusRow("원본 추가 보상",
                "실제 적용 대기 " + scenario.NativeRewards.PendingCount + "건",
                OrdrMobileUiTheme.Aqua);
            if (controller.PendingAllStoryTranscendWisps > 0)
            {
                OrdrMobileUiTheme.DrawStatusRow("10단계 초월위습",
                    "남은 선택 " + controller.PendingAllStoryTranscendWisps + "회",
                    OrdrMobileUiTheme.Gold);
                DrawOriginalTranscendWispChoiceButtons();
            }
            if (controller.SharedEconomy != null && controller.Combat != null &&
                controller.SharedEconomy.Wallet.Balance(
                    Original323StoryMobileProgression.GreenBloodUseResourceId) > 0)
            {
                long remaining = controller.SharedEconomy.Wallet.Balance(
                    Original323StoryMobileProgression.GreenBloodUseResourceId);
                OrdrMobileUiTheme.DrawStatusRow("14단계 그린블러드",
                    "사용권 " + remaining + "회 · 자신의 대상 유닛을 눌러 즉시 적용",
                    OrdrMobileUiTheme.Gold);
                if (OrdrMobileUiTheme.DrawAccordionHeader(
                    "그린블러드 대상 선택", "세라핌 변환 또는 전설·히든 강화",
                    showStoryGreenBloodTargets, OrdrMobileUiTheme.Gold))
                {
                    showStoryGreenBloodTargets = !showStoryGreenBloodTargets;
                    storyGreenBloodPage = 0;
                }
                if (showStoryGreenBloodTargets)
                {
                    var eligible = new List<MobileUnitSnapshot>();
                    foreach (var unit in controller.Combat.MobileUnitSnapshot())
                    {
                        Original323GreenBloodPlan plan;
                        string unitName;
                        if (controller.TryPlanOriginalStoryGreenBlood(
                            unit.InstanceId, out plan, out unitName) &&
                            (plan.Kind == Original323GreenBloodActionKind.Transform ||
                             !controller.Combat.HasOriginal323GreenBloodEnhancement(
                                 unit.InstanceId)))
                            eligible.Add(unit);
                    }
                    if (eligible.Count == 0)
                        GUILayout.Label("현재 사용 가능한 전설·히든 유닛이 없습니다.",
                            OrdrMobileUiTheme.Explanation);
                    else
                    {
                        int pages = (eligible.Count + StoryGreenBloodPageSize - 1) /
                            StoryGreenBloodPageSize;
                        storyGreenBloodPage = Math.Max(0,
                            Math.Min(storyGreenBloodPage, pages - 1));
                        int begin = storyGreenBloodPage * StoryGreenBloodPageSize;
                        for (int index = begin;
                             index < Math.Min(begin + StoryGreenBloodPageSize, eligible.Count);
                             index++)
                        {
                            var unit = eligible[index];
                            Original323GreenBloodPlan plan;
                            string unitName;
                            if (!controller.TryPlanOriginalStoryGreenBlood(
                                    unit.InstanceId, out plan, out unitName)) continue;
                            string action = plan.Kind == Original323GreenBloodActionKind.Transform
                                ? " → " + GreenBloodSeraphimName(plan.ReplacementRawcode)
                                : " · 공속 +30% 및 주위 피해";
                            if (GUILayout.Button(unitName + " #" + unit.InstanceId + action,
                                OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(58)))
                                ShowResult(controller.RequestOriginalStoryGreenBlood(
                                    unit.InstanceId));
                        }
                        if (pages > 1)
                        {
                            GUILayout.BeginHorizontal();
                            if (GUILayout.Button("이전", OrdrMobileUiTheme.SmallButton, GUILayout.Width(68f), GUILayout.MinHeight(52)) &&
                                storyGreenBloodPage > 0) storyGreenBloodPage--;
                            GUILayout.Label((storyGreenBloodPage + 1) + " / " + pages,
                                OrdrMobileUiTheme.Explanation);
                            if (GUILayout.Button("다음", OrdrMobileUiTheme.SmallButton, GUILayout.Width(68f), GUILayout.MinHeight(52)) &&
                                storyGreenBloodPage + 1 < pages) storyGreenBloodPage++;
                            GUILayout.EndHorizontal();
                        }
                    }
                }
            }
            int rewardStage = selectedStoryRewardStage >= 1 &&
                selectedStoryRewardStage <= Original323StoryUnlockRules.FinalStage ?
                selectedStoryRewardStage :
                Math.Max(1, Math.Min(Original323StoryUnlockRules.FinalStage,
                    scenario.CompletedAllBuildings ||
                    scenario.EggheadWaitRemaining > 0 ? 14 :
                    activeStage > 0 ? activeStage : cleared + 1));
            var rewardBuilding = catalog.Find(rewardStage);
            GUILayout.Label("기본 보상 미리보기 · " + rewardStage + "단계 " +
                (rewardBuilding == null ? "" : rewardBuilding.Name),
                OrdrMobileUiTheme.Subheading);
            GUILayout.Label(Original323SoloStoryScenario.BaseRewardSummary(rewardStage),
                OrdrMobileUiTheme.Explanation);
            GUILayout.Label("원본 추가 보상은 실제 대상에 적용될 때만 완료 처리됩니다.",
                OrdrMobileUiTheme.Overline);
            if (OrdrMobileUiTheme.DrawAccordionHeader("추가 보상 적용 상태",
                scenario.NativeRewards.PendingCount + "건 대기",
                showStoryNativeRewards, OrdrMobileUiTheme.Gold))
                showStoryNativeRewards = !showStoryNativeRewards;
            if (showStoryNativeRewards)
            {
                GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
                if (scenario.NativeRewards.Awards.Count == 0)
                    GUILayout.Label("아직 격파로 발생한 원본 추가 보상이 없습니다.");
                foreach (var award in scenario.NativeRewards.Awards)
                {
                    string kind = award.Kind == Original323StoryNativeRewardKind.Research ?
                        "연구 해금" :
                        award.Kind == Original323StoryNativeRewardKind.TraitPoints ?
                        "특성포인트" :
                        award.Kind == Original323StoryNativeRewardKind.GamblingCharges ?
                        "유닛 도박 충전" :
                        award.Kind == Original323StoryNativeRewardKind.AncientShip ?
                        "고대의 배" :
                        award.Kind == Original323StoryNativeRewardKind.HeroExperience ?
                        "영웅 경험치" : "그린블러드";
                    OrdrMobileUiTheme.DrawStatusRow(award.Stage + "단계 · " + kind,
                        award.Applied ? "실제 적용 완료" : "대상 연결·적용 대기",
                        award.Applied ? OrdrMobileUiTheme.Aqua : OrdrMobileUiTheme.Gold);
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndVertical();

            DrawStoryDispatchPanel();

            if (OrdrMobileUiTheme.DrawAccordionHeader("스토리 건물 14종 기록",
                "격파 " + cleared + " / 14 · 건물별 보상 보기",
                showStoryStageHistory, OrdrMobileUiTheme.Aqua))
                showStoryStageHistory = !showStoryStageHistory;
            if (!showStoryStageHistory) return;
            GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
            for (int stage = 1; stage <= Original323StoryUnlockRules.FinalStage; stage++)
            {
                var building = catalog.Find(stage);
                if (building == null) continue;
                Original323StoryBuildingEncounter encountered = null;
                foreach (var entry in world.Encounters)
                    if (entry.Building.Stage == stage) encountered = entry;
                bool alive = encountered != null &&
                    encountered.State == Original323StoryBuildingState.Alive;
                bool modelCleared = stage <= cleared;
                string state = encountered != null && encountered.SourceSettlementConfirmed ?
                    "원본 정산 확인" : modelCleared ? "격파 완료" :
                    alive ? "공격 중" :
                    stage == 14 && scenario.EggheadWaitRemaining > 0 ? "등장 대기" :
                    "미등장";
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(stage.ToString("00") + "  " + building.Name +
                    " · " + state +
                    (selectedStoryRewardStage == stage ? " ✓" : ""),
                    GUILayout.MinHeight(58)))
                    selectedStoryRewardStage = stage;
                if (controller.CanRequestOriginalStoryBuilding && !alive &&
                    (encountered == null || !encountered.SourceSettlementConfirmed) &&
                    GUILayout.Button("원본 출현 확인",
                        GUILayout.Width(112), GUILayout.MinHeight(58)))
                    ShowResult(controller.RequestOriginalStoryBuilding(stage));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        private void DrawStoryDispatchPanel()
        {
            var manual = controller.ManualActions;
            if (manual == null || !manual.HasStoryService)
                OrdrMobileUiTheme.DrawStatusRow("스토리 파견",
                    "원본 이동 서비스 준비 중", OrdrMobileUiTheme.Gold);
            if (Combat == null || manual == null) return;
            long? selected = Combat.Roster.SelectedId;
            if (selected.HasValue)
            {
                bool deployed = manual.IsStoryDeployed(selected.Value);
                bool shared = Combat.IsSharedObjectiveDeployed(selected.Value);
                bool decoding = Combat.IsOriginalPoneglyphChanneling(selected.Value);
                bool canAct = deployed ? manual.CanRecallStory(selected.Value) :
                    manual.CanDispatchStory(selected.Value);
                GUILayout.BeginHorizontal();
                GUILayout.Label("선택 유닛 #" + selected.Value +
                    (deployed ? " · 스토리 파견 중" :
                     shared ? " · 공유 목표 출전 중" :
                     decoding ? " · 포네그리프 해독 중" : " · 메인 전장"),
                    OrdrMobileUiTheme.Overline);
                bool wasEnabled = GUI.enabled;
                GUI.enabled = wasEnabled && canAct;
                if (GUILayout.Button(deployed ? "선택 유닛 복귀" : "선택 유닛 파견",
                    GUILayout.MinHeight(60)))
                    ShowResult(deployed ?
                        manual.RequestStoryRecall(selected.Value) :
                        manual.RequestStoryDispatch(selected.Value));
                GUI.enabled = wasEnabled;
                GUILayout.EndHorizontal();
            }
            if (OrdrMobileUiTheme.DrawAccordionHeader("스토리 파견 관리",
                "파견 유닛·파견 가능 유닛", showStoryDispatch, OrdrMobileUiTheme.Gold))
                showStoryDispatch = !showStoryDispatch;
            if (!showStoryDispatch) return;

            GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
            var owned = OwnedUnitsForFrame();
            int deployedCount, readyCount, recallCount, matched, pages, resolved;
            var visibleUnits = MobileStoryDispatchBrowser.Select(owned,
                manual.IsStoryDeployed, manual.CanDispatchStory, manual.CanRecallStory,
                storyRosterFilter, storyRosterPage, 8,
                out deployedCount, out readyCount, out recallCount,
                out matched, out pages, out resolved);
            storyRosterPage = resolved;
            OrdrMobileUiTheme.DrawStatusRow("파견 현황",
                deployedCount + "기 파견 · " + readyCount + "기 파견 가능",
                OrdrMobileUiTheme.Aqua);
            if (readyCount == 0)
                GUILayout.Label("보스·미션 진행 중이거나 원본 입장 조건에 맞지 않으면" +
                    " 새 유닛을 파견할 수 없습니다.", OrdrMobileUiTheme.Overline);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(storyRosterFilter == MobileStoryRosterFilter.All ?
                "전체 ✓" : "전체", GUILayout.MinHeight(54)))
            { storyRosterFilter = MobileStoryRosterFilter.All; storyRosterPage = 0; }
            if (GUILayout.Button(storyRosterFilter == MobileStoryRosterFilter.ReadyToDispatch ?
                "파견 가능 ✓" : "파견 가능", GUILayout.MinHeight(54)))
            { storyRosterFilter = MobileStoryRosterFilter.ReadyToDispatch; storyRosterPage = 0; }
            if (GUILayout.Button(storyRosterFilter == MobileStoryRosterFilter.Deployed ?
                "파견 중 ✓" : "파견 중", GUILayout.MinHeight(54)))
            { storyRosterFilter = MobileStoryRosterFilter.Deployed; storyRosterPage = 0; }
            GUILayout.EndHorizontal();

            if (recallCount > 0)
            {
                if (!confirmStoryRecallAll)
                {
                    if (GUILayout.Button("파견 유닛 최대 10기 일괄 복귀",
                        GUILayout.MinHeight(60)))
                        confirmStoryRecallAll = true;
                }
                else
                {
                    GUILayout.Label("복귀 가능한 파견 유닛을 최대 10기까지 원본 조건으로 순차 복귀합니다." +
                        " 일부 실패 시 이미 복귀한 유닛은 유지됩니다.",
                        OrdrMobileUiTheme.Explanation);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("복귀 확정", GUILayout.MinHeight(60)))
                    {
                        var targetIds = new List<long>();
                        // Read a fresh roster when committing, not a cached UI page.
                        foreach (var unit in Combat.MobileUnitSnapshot())
                            if (manual.CanRecallStory(unit.InstanceId) &&
                                targetIds.Count < MobileManualBatchActions.MaximumStoryRecalls)
                                targetIds.Add(unit.InstanceId);
                        if (targetIds.Count > 0)
                            ShowResult(MobileManualBatchActions.RecallStoryUnits(
                                targetIds, manual.CanRecallStory, manual.RequestStoryRecall));
                        else notice = "현재 복귀 가능한 스토리 유닛이 없습니다.";
                        confirmStoryRecallAll = false;
                    }
                    if (GUILayout.Button("취소", GUILayout.MinHeight(60)))
                        confirmStoryRecallAll = false;
                    GUILayout.EndHorizontal();
                }
            }
            else confirmStoryRecallAll = false;

            GUILayout.Label("조건에 맞는 유닛 " + matched + "기 · 높은 등급순",
                OrdrMobileUiTheme.Overline);
            DrawPager(storyRosterPage, pages, page => storyRosterPage = page);
            foreach (var unit in visibleUnits)
            {
                var actor = unit.Unit;
                GUILayout.BeginHorizontal();
                DrawCharacterPortrait(actor.Unit.Id, 48f);
                if (OrdrMobileUiTheme.DrawActionRow(actor.Unit.Name,
                    actor.Unit.Grade + " · #" + actor.InstanceId +
                    (unit.IsDeployed ? " · 파견 중" : ""),
                    OrdrMobileUiTheme.Aqua, 68f))
                {
                    Combat.Roster.Select(actor.InstanceId);
                    notice = actor.Unit.Name + " 선택";
                }
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && (unit.IsDeployed ? unit.CanRecall : unit.CanDispatch);
                if (GUILayout.Button(unit.IsDeployed ? "복귀" : "파견",
                    GUILayout.Width(68f), GUILayout.MinHeight(68f)))
                    ShowResult(unit.IsDeployed ?
                        manual.RequestStoryRecall(actor.InstanceId) :
                        manual.RequestStoryDispatch(actor.InstanceId));
                GUI.enabled = enabled;
                GUILayout.EndHorizontal();
            }
            DrawPager(storyRosterPage, pages, page => storyRosterPage = page);
            GUILayout.EndVertical();
        }

        // The battlefield and left menu remain visible while this compact
        // recipe browser lives in the existing right-hand drawer.
        // Compact list under one fixed selected-recipe header. Both filter
        // rows are ordinary buttons; neither can swallow the battle swipe.
        private void DrawFusion()
        {
            GUILayout.BeginHorizontal();
            DrawFusionCategoryButton(FusionListCategory.Ready, "가능");
            DrawFusionCategoryButton(FusionListCategory.Owned, "보유");
            DrawFusionCategoryButton(FusionListCategory.All, "전체");
            DrawFusionCategoryButton(FusionListCategory.Bookmarked, "★");
            GUILayout.EndHorizontal();

            if (GUILayout.Button("등급 " + fusionGradeFilter +
                (showFusionGradeButtons ? " ▲" : " ▼"),
                OrdrMobileUiTheme.NavButton, GUILayout.MinHeight(46f)))
                showFusionGradeButtons = !showFusionGradeButtons;
            if (showFusionGradeButtons)
            {
                GUILayout.BeginHorizontal();
                DrawFusionGradeButton("전체");
                DrawFusionGradeButton("희귀");
                DrawFusionGradeButton("전설");
                DrawFusionGradeButton("히든");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                DrawFusionGradeButton("초월");
                DrawFusionGradeButton("불멸");
                DrawFusionGradeButton("기타");
                GUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(fusionShortcutQuery) &&
                GUILayout.Button("토큰 조합 ×", OrdrMobileUiTheme.NavButton,
                    GUILayout.MinHeight(46f)))
            {
                fusionShortcutQuery = "";
                fusionPage = 0;
                scroll = Vector2.zero;
            }

            DrawFusionExpandedRequirements();

            var filtered = new List<MobileFusionPreview>();
            foreach (var offer in fusionOffers)
            {
                if (fusionListCategory == FusionListCategory.Ready &&
                    !CanCraftFusion(offer)) continue;
                if (fusionListCategory == FusionListCategory.Owned &&
                    !HasOwnedFusionMaterial(offer)) continue;
                if (fusionListCategory == FusionListCategory.Bookmarked &&
                    !fusionBookmarks.Contains(offer.RecipeId)) continue;
                if (fusionGradeFilter != "전체" &&
                    FusionGradeBucket(offer.ResultGrade) != fusionGradeFilter)
                    continue;
                filtered.Add(offer);
            }
            if (fusionListCategory == FusionListCategory.Owned)
                filtered.Sort((a, b) =>
                {
                    int priority = FusionOwnedSortPriority(a).CompareTo(
                        FusionOwnedSortPriority(b));
                    return priority != 0 ? priority :
                        string.CompareOrdinal(a.Title, b.Title);
                });

            int matches, pages, resolved;
            var visibleOffers = MobileFusionBrowser.Select(filtered.AsReadOnly(),
                fusionShortcutQuery, false, fusionPage, FusionPageSize,
                out matches, out pages, out resolved);
            fusionPage = resolved;
            if (matches == 0)
                GUILayout.Label("조합 없음", OrdrMobileUiTheme.Subheading);
            foreach (var offer in visibleOffers)
                DrawFusionRecipeCard(offer);

            if (pages > 1)
                DrawPager(resolved, pages, page => fusionPage = page);
            DrawChoiceWispMerge();
            if (GUILayout.Button(showOriginalAudit ? "원본 닫기" : "원본 조합식",
                OrdrMobileUiTheme.NavButton, GUILayout.MinHeight(48f)))
                showOriginalAudit = !showOriginalAudit;
            if (showOriginalAudit) DrawOriginalJassAudit();
        }

        private bool CanCraftFusion(MobileFusionPreview offer)
        {
            if (offer == null || !offer.IsReady || Combat == null ||
                !controller.ManualActions.CanExecuteFusionRecipe(offer.RecipeId))
                return false;
            foreach (long id in offer.SuggestedMaterialInstances)
                if (Combat.IsLockedFromRosterTransactions(id)) return false;
            return true;
        }

        private static bool HasOwnedFusionMaterial(MobileFusionPreview offer)
        {
            foreach (var ingredient in offer.Ingredients)
                if (ingredient.OwnedCount > 0) return true;
            return offer.HasAllUnitMaterials &&
                (offer.SuggestedMaterialInstances.Count > 0 ||
                 offer.StoryMaterialInstances.Count > 0);
        }

        private int FusionOwnedSortPriority(MobileFusionPreview offer)
        {
            if (CanCraftFusion(offer)) return 0;
            if (offer.HasAllUnitMaterials && offer.StoryMaterialInstances.Count > 0)
                return 1;
            if (offer.HasAllUnitMaterials) return 2;
            int missing = 0;
            foreach (var ingredient in offer.Ingredients)
                missing += Math.Max(0, ingredient.RequiredCount - ingredient.OwnedCount);
            return 3 + Math.Min(missing, 100);
        }

        private void DrawFusionCategoryButton(FusionListCategory category, string title)
        {
            bool selected = fusionListCategory == category;
            if (!GUILayout.Button(title, selected ? OrdrMobileUiTheme.SelectedTab :
                OrdrMobileUiTheme.NavButton, GUILayout.MinHeight(50f),
                GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true))) return;
            if (selected) return;
            fusionListCategory = category;
            fusionPage = 0;
            scroll = Vector2.zero;
        }

        private void DrawFusionGradeButton(string grade)
        {
            bool selected = fusionGradeFilter == grade;
            if (!GUILayout.Button(grade, selected ? OrdrMobileUiTheme.SelectedTab :
                OrdrMobileUiTheme.NavButton, GUILayout.MinHeight(46f),
                GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true))) return;
            if (selected) return;
            fusionGradeFilter = grade;
            fusionPage = 0;
            scroll = Vector2.zero;
            showFusionGradeButtons = false;
        }

        // The selected result is pinned above the independently scrollable
        // recipe list. Refreshes preserve the recipe ID and list scroll.
        private MobileFusionPreview CurrentFusionSelection()
        {
            if (fusionOffers == null || fusionOffers.Count == 0) return null;
            if (pendingFusion != null)
                foreach (var candidate in fusionOffers)
                    if (candidate.RecipeId == pendingFusion.RecipeId)
                    {
                        pendingFusion = candidate;
                        return candidate;
                    }
            MobileFusionPreview chosen = null;
            if (fusionGoal.HasGoal)
                foreach (var offer in fusionOffers)
                    if (offer.RecipeId == fusionGoal.RecipeId)
                    {
                        chosen = offer;
                        break;
                    }
            if (chosen == null)
                foreach (var offer in fusionOffers)
                    if (CanCraftFusion(offer)) { chosen = offer; break; }
            if (chosen == null)
                foreach (var offer in fusionOffers)
                    if (HasOwnedFusionMaterial(offer)) { chosen = offer; break; }
            if (chosen == null) chosen = fusionOffers[0];
            pendingFusion = chosen;
            fusionSelectedMaterialScroll = Vector2.zero;
            return chosen;
        }

        private string FusionStatus(MobileFusionPreview offer)
        {
            if (CanCraftFusion(offer)) return "제작 가능";
            if (!controller.ManualActions.CanExecuteFusionRecipe(offer.RecipeId))
                return "제작 잠김";
            if (!offer.HasAllUnitMaterials) return "재료 부족";
            if (offer.StoryMaterialInstances.Count > 0) return "복귀 필요";
            foreach (long id in offer.SuggestedMaterialInstances)
                if (Combat != null && Combat.IsLockedFromRosterTransactions(id))
                    return "사용 중";
            if (offer.HasAllUnitMaterials) return "조건 미충족";
            return "재료 부족";
        }

        private void DrawFusionSelectedHeader()
        {
            MobileFusionPreview offer = CurrentFusionSelection();
            if (offer == null) return;
            GUILayout.BeginVertical(OrdrMobileUiTheme.FusionSelectedCard);
            GUILayout.BeginHorizontal();
            DrawFusionResultPortrait(offer, 54f);
            GUILayout.BeginVertical(GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            GUILayout.Label(FusionDisplayName(offer), OrdrMobileUiTheme.Subheading,
                GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true),
                GUILayout.MinHeight(29f));
            GUILayout.Label(FusionStatus(offer),
                CanCraftFusion(offer) ? OrdrMobileUiTheme.FusionReadyChip :
                OrdrMobileUiTheme.FusionMissingChip, GUILayout.MinHeight(30f));
            GUILayout.EndVertical();
            if (GUILayout.Button(fusionGoal.RecipeId == offer.RecipeId ? "목표 ✓" : "목표",
                fusionGoal.RecipeId == offer.RecipeId ?
                    OrdrMobileUiTheme.SelectedTab : OrdrMobileUiTheme.NavButton,
                GUILayout.Width(70f), GUILayout.MinHeight(54f)))
                fusionGoal.Toggle(offer.RecipeId);
            if (GUILayout.Button(expandedRecipeId == offer.RecipeId ? "접기" : "조건",
                expandedRecipeId == offer.RecipeId ?
                    OrdrMobileUiTheme.SelectedTab : OrdrMobileUiTheme.NavButton,
                GUILayout.Width(64f), GUILayout.MinHeight(54f)))
                expandedRecipeId = expandedRecipeId == offer.RecipeId ?
                    null : offer.RecipeId;
            GUILayout.EndHorizontal();

            if (offer.Ingredients.Count > 0)
            {
                fusionSelectedMaterialScroll = GUILayout.BeginScrollView(
                    fusionSelectedMaterialScroll, true, false,
                    GUI.skin.horizontalScrollbar, GUIStyle.none,
                    GUILayout.Height(140f));
                GUILayout.BeginHorizontal(GUILayout.ExpandWidth(false));
                foreach (var ingredient in offer.Ingredients)
                    DrawFusionIngredient(ingredient);
                GUILayout.EndHorizontal();
                GUILayout.EndScrollView();
            }

            if (offer.GoldCost > 0 || offer.WoodCost > 0)
            {
                GUILayout.BeginHorizontal();
                if (offer.GoldCost > 0)
                {
                    long owned = controller.SharedEconomy.Wallet.Balance("gold");
                    GUILayout.Label("돈 " + owned + "/" + offer.GoldCost,
                        owned >= offer.GoldCost ? OrdrMobileUiTheme.FusionReadyChip :
                        OrdrMobileUiTheme.FusionMissingChip, GUILayout.MinHeight(34f),
                        GUILayout.ExpandWidth(true));
                }
                if (offer.WoodCost > 0)
                {
                    long owned = controller.SharedEconomy.Wallet.Balance("wood");
                    GUILayout.Label("나무 " + owned + "/" + offer.WoodCost,
                        owned >= offer.WoodCost ? OrdrMobileUiTheme.FusionReadyChip :
                        OrdrMobileUiTheme.FusionMissingChip, GUILayout.MinHeight(34f),
                        GUILayout.ExpandWidth(true));
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        private void DrawFusionIngredient(MobileFusionIngredient ingredient)
        {
            GUILayout.BeginVertical(OrdrMobileUiTheme.FusionNeutralChip,
                GUILayout.Width(120f), GUILayout.Height(130f));
            Sprite portrait = string.IsNullOrEmpty(ingredient.CatalogUnitId) ?
                null : OrdrCharacterPortraits.Find(ingredient.CatalogUnitId);
            if (portrait == null && !string.IsNullOrEmpty(ingredient.CatalogUnitId))
                portrait = OrdrBattlefieldArt.UnitSprite(ingredient.CatalogUnitId);
            if (portrait != null)
                DrawSpritePortrait(portrait, 44f);
            else
                GUILayout.Box("◆", GUILayout.Width(44f), GUILayout.Height(44f));
            string name = ingredient.Name;
            int gradeSuffix = name.IndexOf(" · ", StringComparison.Ordinal);
            if (gradeSuffix > 0) name = name.Substring(0, gradeSuffix);
            GUILayout.Label(name + " ×" + ingredient.RequiredCount,
                OrdrMobileUiTheme.FusionIngredientName, GUILayout.Height(46f),
                GUILayout.Width(107f));
            GUILayout.Label("보유 " + ingredient.OwnedCount,
                ingredient.OwnedCount >= ingredient.RequiredCount ?
                    OrdrMobileUiTheme.FusionReadyChip :
                    OrdrMobileUiTheme.FusionMissingChip,
                GUILayout.Height(29f), GUILayout.Width(107f));
            GUILayout.EndVertical();
        }

        // Full conditions and source-backed dependency routes are available
        // only by tapping the prominent "조건" button, not in every list card.
        private void DrawFusionExpandedRequirements()
        {
            MobileFusionPreview offer = CurrentFusionSelection();
            if (offer == null || expandedRecipeId != offer.RecipeId) return;
            GUILayout.BeginVertical(GUI.skin.box);
            if (!string.IsNullOrWhiteSpace(offer.OriginalMaterialsAndCost))
                GUILayout.Label(offer.OriginalMaterialsAndCost,
                    OrdrMobileUiTheme.Explanation);
            bool recallMentioned = false;
            foreach (string reason in offer.MissingMaterials)
            {
                // Location-specific inventory counts have no place in the UI.
                // The selected action already offers explicit recall.
                if (reason.Contains("스토리 파견") ||
                    reason.Contains("복귀 필요") ||
                    reason.Contains("복귀 후 조합")) { recallMentioned = true; continue; }
                GUILayout.Label(reason, OrdrMobileUiTheme.Explanation);
            }
            if (recallMentioned)
                GUILayout.Label("복귀가 필요한 유닛이 있습니다.",
                    OrdrMobileUiTheme.Explanation);
            if (controller.OriginalJassFusions != null &&
                controller.OriginalFusionObjectPreview != null &&
                controller.OriginalJassFusions.FindByAbility(offer.RecipeId) != null &&
                GUILayout.Button(routeId == offer.RecipeId ? "경로 접기" : "조합 경로",
                    OrdrMobileUiTheme.NavButton, GUILayout.MinHeight(48f)))
            {
                if (routeId == offer.RecipeId)
                {
                    routeId = null;
                    routeSteps = new OriginalJassFusionDependencyStep[0];
                }
                else
                {
                    routeSteps = new OriginalJassFusionDependencyPlanner(
                        controller.OriginalJassFusions,
                        controller.OriginalFusionObjectPreview)
                        .Build(offer.RecipeId, 4);
                    routeId = offer.RecipeId;
                }
            }
            if (routeId == offer.RecipeId)
                foreach (var step in routeSteps)
                    GUILayout.Label(new string('　', Math.Min(step.Depth, 5)) +
                        "· " + step.Name + " ×" + step.Count,
                        OrdrMobileUiTheme.Explanation);
            GUILayout.EndVertical();
        }

        private static string FusionGradeBucket(string grade)
        {
            if (grade == "희귀함" || grade == "희귀") return "희귀";
            if (grade == "전설") return "전설";
            if (grade == "히든" || grade == "해적선") return "히든";
            if (grade == "초월함" || grade == "초월") return "초월";
            if (grade == "불멸의" || grade == "불멸") return "불멸";
            return "기타";
        }

        private static string FusionDisplayName(MobileFusionPreview offer)
        {
            string label = offer.Title;
            int separator = label.IndexOf(" · ", StringComparison.Ordinal);
            if (separator >= 0) label = label.Substring(0, separator);
            int sourceSuffix = label.IndexOf(" (원본", StringComparison.Ordinal);
            if (sourceSuffix > 0) label = label.Substring(0, sourceSuffix);
            return label;
        }

        private void DrawFusionResultPortrait(MobileFusionPreview offer, float size)
        {
            string unitId = null;
            var registration = controller.OriginalJassFusions == null ? null :
                controller.OriginalJassFusions.FindByAbility(offer.RecipeId);
            if (registration != null && registration.Results.Count == 1 &&
                registration.Results[0].Kind == "UNIT" &&
                controller.OriginalFusionObjectPreview != null)
            {
                var result = controller.OriginalFusionObjectPreview.Find(
                    registration.Results[0].Rawcode);
                if (result != null) unitId = result.CatalogUnitId;
            }
            if (!string.IsNullOrEmpty(unitId))
            {
                Sprite portrait = OrdrCharacterPortraits.Find(unitId);
                if (portrait == null) portrait = OrdrBattlefieldArt.UnitSprite(unitId);
                if (portrait != null)
                {
                    DrawSpritePortrait(portrait, size);
                    return;
                }
            }
            GUILayout.Box("◆", GUILayout.Width(size), GUILayout.Height(size));
        }

        private void DrawFusionRecipeCard(MobileFusionPreview offer)
        {
            bool selected = pendingFusion != null &&
                pendingFusion.RecipeId == offer.RecipeId;
            GUILayout.BeginHorizontal(selected ? OrdrMobileUiTheme.FusionSelectedCard :
                GUI.skin.box);
            DrawFusionResultPortrait(offer, 48f);
            if (GUILayout.Button(FusionDisplayName(offer),
                selected ? OrdrMobileUiTheme.SelectedTab :
                OrdrMobileUiTheme.FusionListName,
                GUILayout.MinHeight(54f), GUILayout.MinWidth(0f),
                GUILayout.ExpandWidth(true)))
            {
                if (!selected)
                {
                    pendingFusion = offer;
                    expandedRecipeId = null;
                    routeId = null;
                    fusionSelectedMaterialScroll = Vector2.zero;
                }
                notice = "";
            }
            GUILayout.Label(FusionStatus(offer),
                CanCraftFusion(offer) ? OrdrMobileUiTheme.FusionReadyChip :
                OrdrMobileUiTheme.FusionMissingChip,
                GUILayout.Width(98f), GUILayout.MinHeight(52f));
            if (GUILayout.Button(fusionBookmarks.Contains(offer.RecipeId) ? "★" : "☆",
                OrdrMobileUiTheme.SmallButton, GUILayout.Width(46f),
                GUILayout.MinHeight(54f)))
                fusionBookmarks.Toggle(offer.RecipeId);
            GUILayout.EndHorizontal();
        }

        // One fixed bottom action, rechecked against the live fusion preview.
        // Never moves a deployed actor implicitly or duplicates the source sale.
        private void DrawFusionConfirmation()
        {
            MobileFusionPreview latest = CurrentFusionSelection();
            if (latest == null) return;
            bool canCraft = CanCraftFusion(latest);
            long recallId = 0;
            if (!canCraft && latest.HasAllUnitMaterials &&
                latest.StoryMaterialInstances.Count > 0 &&
                controller.ManualActions.HasStoryService &&
                controller.ManualActions.CanExecuteFusionRecipe(latest.RecipeId))
                foreach (long id in latest.StoryMaterialInstances)
                    if (controller.ManualActions.CanRecallStory(id))
                    {
                        recallId = id;
                        break;
                    }

            bool enabled = GUI.enabled;
            GUI.enabled = enabled && (canCraft || recallId > 0);
            if (GUILayout.Button(recallId > 0 ? "유닛 복귀" : "조합 확정",
                OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(62f),
                GUILayout.ExpandWidth(true)))
            {
                if (recallId > 0)
                    ShowResult(controller.ManualActions.RequestStoryRecall(recallId));
                else
                    ShowResult(MobileFusionProductionAssistant.CraftRepeated(
                        latest.RecipeId, 1,
                        controller.ManualActions.CanExecuteFusionRecipe,
                        () => controller.ManualActions.PreviewFusion(
                            Combat.MobileUnitSnapshot()),
                        controller.ManualActions.ConfirmFusion));
                // Keep selected recipe and scroll position for repeated crafting.
                RefreshCurrent();
            }
            GUI.enabled = enabled;
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
            DrawPoneglyphPanel();
            var treasure = controller.OriginalTreasureHunt;
            if (treasure != null)
                OrdrMobileUiTheme.DrawStatusRow("남은 보물",
                    treasure.UncollectedCount + "개", OrdrMobileUiTheme.Aqua);
            if (controller.TreasureAlarms != null &&
                controller.TreasureAlarms.PendingCount > 0 && Combat != null)
                OrdrMobileUiTheme.DrawStatusRow("탐색 알람",
                    controller.TreasureAlarms.PendingCount + "건 예약 · 다음 " +
                    Math.Ceiling(controller.TreasureAlarms.SecondsUntilNext(
                        Combat.Time)) + "초 후 (전투 시간)",
                    OrdrMobileUiTheme.Aqua);

            bool valid = treasure != null && controller.MobileTreasureGacha != null &&
                controller.MobileTreasureGacha.CanAttempt &&
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
                GUILayout.Label(controller.MobileTreasureGacha == null ||
                    !controller.MobileTreasureGacha.CanAttempt
                        ? "원본 탐색 기회·실제 보물 출현 확인 대기"
                        : "진행 중인 라운드에서 사용 가능",
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

        private void DrawPoneglyphPanel()
        {
            GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
            GUILayout.Label("포네그리프 해독", OrdrMobileUiTheme.Subheading);
            var session = controller.Poneglyph;
            if (session == null)
            {
                GUILayout.Label("게임 시작 후 해독 가능",
                    OrdrMobileUiTheme.Explanation);
                GUILayout.EndVertical();
                return;
            }

            OrdrMobileUiTheme.DrawStatusRow("포네그리프",
                session.Phase == Original323PoneglyphPhase.Locked ? "잠김" :
                session.Phase == Original323PoneglyphPhase.Ready ? "해독 가능" :
                session.Phase == Original323PoneglyphPhase.Decoding ? "해독 진행 중" :
                "해독 완료", OrdrMobileUiTheme.Aqua);
            OrdrMobileUiTheme.DrawStatusRow("해석자 보정",
                session.InterpreterActive ? "활성 · 50초" : "미획득 · 150초",
                OrdrMobileUiTheme.Gold);

            if (session.Phase == Original323PoneglyphPhase.Locked)
                GUILayout.Label("8종 모두 실제로 완료해야 해독이 열립니다. " +
                    "원본 공유 미션 사건이 아직 연결되지 않은 경우 자동 달성 처리하지 않습니다.",
                    OrdrMobileUiTheme.Explanation);
            else if (session.Phase == Original323PoneglyphPhase.Decoding)
            {
                OrdrMobileUiTheme.DrawStatusRow("해독 유닛",
                    "#" + session.ActiveUnitId.Value, OrdrMobileUiTheme.Aqua);
                OrdrMobileUiTheme.DrawStatusRow("남은 시간",
                    Math.Ceiling(session.RemainingSeconds(Combat.Time)) + "초",
                    OrdrMobileUiTheme.Gold);
                if (OrdrMobileUiTheme.DrawActionRow("해독 중단",
                    "중단", OrdrMobileUiTheme.Gold, 80f))
                    notice = controller.RequestStopPoneglyph();
            }
            else if (session.Phase == Original323PoneglyphPhase.Ready &&
                Combat != null)
            {
                GUILayout.Label("실제로 보유한 유닛을 선택해 해독을 시작합니다. " +
                    "원본 금지 기술 9종(Aloc 포함)을 현재 인스턴스 기준으로 확인합니다.",
                    OrdrMobileUiTheme.Explanation);
                var owned = OwnedUnitsForFrame();
                const int pageSize = 8;
                int pages = Math.Max(1, (owned.Count + pageSize - 1) / pageSize);
                poneglyphUnitPage = Math.Max(0, Math.Min(poneglyphUnitPage, pages - 1));
                if (pages > 1)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("이전", OrdrMobileUiTheme.SmallButton, GUILayout.Width(68f), GUILayout.MinHeight(50)))
                        poneglyphUnitPage = Math.Max(0, poneglyphUnitPage - 1);
                    GUILayout.Label((poneglyphUnitPage + 1) + "/" + pages,
                        OrdrMobileUiTheme.Overline);
                    if (GUILayout.Button("다음", OrdrMobileUiTheme.SmallButton, GUILayout.Width(68f), GUILayout.MinHeight(50)))
                        poneglyphUnitPage = Math.Min(pages - 1, poneglyphUnitPage + 1);
                    GUILayout.EndHorizontal();
                }
                for (int i = poneglyphUnitPage * pageSize;
                    i < owned.Count && i < (poneglyphUnitPage + 1) * pageSize; i++)
                {
                    var actor = owned[i];
                    string reason;
                    bool eligible = controller.CanDecodePoneglyph(actor.InstanceId, out reason);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(actor.Unit.Name + " · #" + actor.InstanceId,
                        OrdrMobileUiTheme.Overline,
                        GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
                    bool enabled = GUI.enabled;
                    GUI.enabled = enabled && eligible &&
                        Combat.Game.Phase == SessionPhase.Running &&
                        !controller.IsUnitPreparation && Combat.Game.CurrentWave > 0;
                    if (GUILayout.Button(eligible ? "해독 시작" : "불가",
                        GUILayout.Width(108), GUILayout.MinHeight(54)))
                        notice = controller.RequestStartPoneglyph(actor.InstanceId);
                    GUI.enabled = enabled;
                    GUILayout.EndHorizontal();
                    if (!eligible)
                        GUILayout.Label(reason, OrdrMobileUiTheme.Overline);
                }
                if (owned.Count == 0)
                    GUILayout.Label("보유 중인 해독 후보 유닛이 없습니다.",
                        OrdrMobileUiTheme.Explanation);
            }
            else if (session.Phase == Original323PoneglyphPhase.Completed)
            {
                var revealed = session.VisibleRevealedChests(controller.OriginalTreasureHunt);
                OrdrMobileUiTheme.DrawStatusRow("공개된 보물",
                    revealed.Count + "개 남음", OrdrMobileUiTheme.Aqua);
                foreach (var chest in revealed)
                    GUILayout.Label("보물 " + chest.OriginalChestId +
                        " · X " + chest.X.ToString("0.#") +
                        " / Y " + chest.Y.ToString("0.#"),
                        OrdrMobileUiTheme.Overline);
            }
            GUILayout.Label(session.LastMessage, OrdrMobileUiTheme.Explanation);
            GUILayout.EndVertical();
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
                balance + "개", OrdrMobileUiTheme.Aqua, 76f, true, balance))
                RunWispDirect(name, () => controller.RequestAutomaticWispRetry());
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
            // Source-observed CQB tokens and story-10 practice entitlements
            // share only the user choice. Neither is granted by opening UI.
            if (controller.PendingAllStoryTranscendWisps > 0)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("스토리 보상 초월위습 " +
                    controller.PendingAllStoryTranscendWisps + "개",
                    OrdrMobileUiTheme.SectionTitle);
                if (controller.PendingPracticeStoryTranscendWisps > 0)
                    GUILayout.Label("10단계 격파 보상 " +
                        controller.PendingPracticeStoryTranscendWisps +
                        "개 · 아래 원본 세 선택지 중 하나를 선택하세요.",
                        OrdrMobileUiTheme.Explanation);
                DrawOriginalTranscendWispChoiceButtons();
                GUILayout.EndVertical();
            }

            DrawWispEconomyOffers();
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
            // The same actual roster-delta announcement is used by every
            // wisp recruitment path, including selectable/transcend rewards.
            var before = CaptureRewardRosterIds();
            try
            {
                ManualActionResult result = wispActions.ExecuteDirect(label, action);
                ShowResult(result);
                if (PublishRewardToast(result, before, false))
                    notice = "";
            }
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
                showRandomWispActions, OrdrMobileUiTheme.Gold, 80f, true,
                controller.WispBalance(WispResourceIds.Random)))
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
                var filtered = new List<JassFusionReadiness>(originalJassPreview);
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

        // Compact arcade desk. Gambling and gambling-for-unit (including
        // Otherworld) are one-tap rows, with only on-demand hold details.
        private void DrawGambling()
        {
            GUILayout.Label("돈 도박", OrdrMobileUiTheme.Subheading);
            bool foundMoney = false;
            foreach (var offer in economyOffers)
                if (IsMoneyGamblingOffer(offer.Id))
                {
                    DrawCompactGamblingOffer(offer);
                    foundMoney = true;
                }
            if (controller.PracticeMoneyGamblingDesk != null &&
                controller.PracticeMoneyGamblingDesk.Graduated)
            {
                DrawGraduationHoldRow();
                foundMoney = true;
            }
            if (!foundMoney)
                GUILayout.Label("이용 가능한 돈 도박 없음", OrdrMobileUiTheme.Explanation);

            // Original ORDR has a paid 100-gold common/uncommon support action.
            // An older guide confirms its purpose, NOT the 2.323[C] source
            // cost/stock/lottery rules. Route only an actual registered source
            // offer; never invent a paid RNG transaction or grant a free unit.
            bool supportConnected = false;
            foreach (var offer in economyOffers)
                if (IsMaterialSupportOffer(offer))
                {
                    DrawCompactGamblingOffer(offer);
                    supportConnected = true;
                }
            if (!supportConnected)
            {
                bool priorEnabled = GUI.enabled;
                GUI.enabled = false;
                OrdrMobileUiTheme.DrawActionRow("물품 지원",
                    "100G · 연결 대기", OrdrMobileUiTheme.Gold, 62f);
                GUI.enabled = priorEnabled;
            }

            GUILayout.Space(8f);
            GUILayout.Label("유닛 도박", OrdrMobileUiTheme.Subheading);
            bool foundUnits = false;
            foreach (var offer in economyOffers)
                if (IsUnitGamblingOffer(offer.Id))
                {
                    DrawCompactGamblingOffer(offer);
                    foundUnits = true;
                }
            if (!foundUnits)
                GUILayout.Label("이용 가능한 유닛 도박 없음", OrdrMobileUiTheme.Explanation);

            GUILayout.Space(8f);
            if (GUILayout.Button(showGamblingExtras ? "기타 ▲" : "기타 ▼",
                OrdrMobileUiTheme.NavButton, GUILayout.MinHeight(56f)))
                showGamblingExtras = !showGamblingExtras;
            if (!showGamblingExtras) return;

            // Navigation selection is deliberately kept in its existing
            // implementation until the user defines that screen separately.
            DrawGamblingNavigation();
            DrawCompuExchange();
            DrawItemExcavation();
            foreach (var offer in economyOffers)
                if (!IsMoneyGamblingOffer(offer.Id) &&
                    !IsUnitGamblingOffer(offer.Id) &&
                    !IsMaterialSupportOffer(offer))
                    DrawCompactGamblingOffer(offer);

            var moneyDesk = controller.PracticeMoneyGamblingDesk;
            if (moneyDesk != null && GUILayout.Button(
                showMoneyGamblingHistory ? "기록 접기" : "최근 결과",
                OrdrMobileUiTheme.NavButton, GUILayout.MinHeight(52f)))
                showMoneyGamblingHistory = !showMoneyGamblingHistory;
            if (showMoneyGamblingHistory && moneyDesk != null)
            {
                foreach (string result in moneyDesk.Regular.RecentResults)
                    GUILayout.Label(result, OrdrMobileUiTheme.Explanation);
                foreach (string result in moneyDesk.High.RecentResults)
                    GUILayout.Label(result, OrdrMobileUiTheme.Explanation);
            }
        }

        private static bool IsMaterialSupportOffer(MobileManualOffer offer)
        {
            return offer != null &&
                (offer.Title.IndexOf("물품 지원", StringComparison.Ordinal) >= 0 ||
                 offer.Title.IndexOf("물품지원", StringComparison.Ordinal) >= 0);
        }

        private static bool IsMoneyGamblingOffer(string id)
        {
            return id == SourceModelRegularMoneyGambling.OfferId ||
                id == SourceModelMoneyGamblingDesk.HighOfferId ||
                id == SourceModelMoneyGamblingDesk.WoodOfferId ||
                id == SourceModelMoneyGamblingDesk.TraitOfferId ||
                id == SourceModelMoneyGamblingDesk.RareOfferId;
        }

        private static bool IsUnitGamblingOffer(string id)
        {
            return id == Original323UnitGamblingRules.OfferId(OriginalGambleTier.Low) ||
                id == Original323UnitGamblingRules.OfferId(OriginalGambleTier.Middle) ||
                id == Original323UnitGamblingRules.OfferId(OriginalGambleTier.High) ||
                id == SourceModelOtherworldUnitGambling.OfferId;
        }

        private string GamblingShortName(MobileManualOffer offer)
        {
            string id = offer.Id;
            if (IsMaterialSupportOffer(offer)) return "물품 지원";
            if (id == SourceModelRegularMoneyGambling.OfferId)
                return "일반 " + (controller.PracticeMoneyGambling != null ?
                    controller.PracticeMoneyGambling.Stock + "/3" : "");
            if (id == SourceModelMoneyGamblingDesk.HighOfferId)
            {
                var desk = controller.PracticeMoneyGamblingDesk;
                return desk == null ? "고급" :
                    "고급 " + desk.High.Stock + "/" + desk.High.Capacity;
            }
            if (id == SourceModelMoneyGamblingDesk.WoodOfferId) return "목재 구매";
            if (id == SourceModelMoneyGamblingDesk.TraitOfferId) return "특성포인트";
            if (id == SourceModelMoneyGamblingDesk.RareOfferId) return "졸업 유닛";
            if (id == Original323UnitGamblingRules.OfferId(OriginalGambleTier.Low)) return "하급";
            if (id == Original323UnitGamblingRules.OfferId(OriginalGambleTier.Middle)) return "중급";
            if (id == Original323UnitGamblingRules.OfferId(OriginalGambleTier.High)) return "고급";
            if (id == SourceModelOtherworldUnitGambling.OfferId)
                return "이세계 " + (controller.PracticeUnitGambling == null ? "" :
                    controller.PracticeUnitGambling.Charges + "회");
            return offer.Title;
        }

        private string GamblingShortCost(string id)
        {
            if (id == SourceModelRegularMoneyGambling.OfferId)
                return SourceModelRegularMoneyGambling.GoldCost + "G";
            if (id == SourceModelMoneyGamblingDesk.HighOfferId)
                return (controller.PracticeMoneyGamblingDesk == null ? 500 :
                    controller.PracticeMoneyGamblingDesk.High.Price) + "G";
            if (id == SourceModelMoneyGamblingDesk.WoodOfferId) return "10,000G";
            if (id == SourceModelMoneyGamblingDesk.TraitOfferId)
                return SourceModelMoneyGamblingDesk.TraitGoldCost.ToString("N0") + "G";
            if (id == SourceModelMoneyGamblingDesk.RareOfferId)
                return SourceModelMoneyGamblingDesk.RareGoldCost.ToString("N0") + "G";
            foreach (OriginalGambleTier tier in Enum.GetValues(typeof(OriginalGambleTier)))
                if (id == Original323UnitGamblingRules.OfferId(tier))
                    return Original323UnitGamblingRules.GoldCost(tier).ToString("N0") +
                        "G · 목" + Original323UnitGamblingRules.WoodCost(tier);
            if (id == SourceModelOtherworldUnitGambling.OfferId)
                return Original323OtherworldGamblingRules.GoldCost.ToString("N0") +
                    "G · 목" + Original323OtherworldGamblingRules.WoodCost;
            return "규칙 확인";
        }

        private void DrawCompactGamblingOffer(MobileManualOffer offer)
        {
            MobileLongPressResult action = OrdrMobileUiTheme.DrawHoldActionRow(
                "gamble:" + offer.Id, GamblingShortName(offer),
                GamblingShortCost(offer.Id), OrdrMobileUiTheme.Gold,
                gamblingPressGesture, 62f, true);
            if (action == MobileLongPressResult.Tap)
                RunGamblingOffer(offer);
            else if (action == MobileLongPressResult.Hold)
                expandedGamblingOfferId = expandedGamblingOfferId == offer.Id ?
                    null : offer.Id;
            if (expandedGamblingOfferId == offer.Id)
                DrawExpandedGamblingOffer(offer);
        }

        private void DrawExpandedGamblingOffer(MobileManualOffer offer)
        {
            GUILayout.BeginVertical(OrdrMobileUiTheme.HelpBubble);
            // Preserve the authoritative source price, lottery odds and
            // restrictions. They are hidden only when the row is collapsed.
            GUILayout.Label(offer.OriginalPriceAndRules,
                OrdrMobileUiTheme.Explanation);
            var desk = controller.PracticeMoneyGamblingDesk;
            if (desk != null && offer.Id == SourceModelMoneyGamblingDesk.HighOfferId)
                GUILayout.Label("졸업 " + desk.High.GoldReturned.ToString("N0") +
                    " / " + SourceModelMoneyGamblingDesk.GraduationGold.ToString("N0") +
                    " 골드", OrdrMobileUiTheme.Subheading);
            if (desk != null && offer.Id == SourceModelMoneyGamblingDesk.WoodOfferId)
                GUILayout.Label("남은 구매 " + desk.WoodRights + "회",
                    OrdrMobileUiTheme.Explanation);
            var world = controller.PracticeUnitGambling;
            if (world != null && offer.Id == SourceModelOtherworldUnitGambling.OfferId)
                GUILayout.Label("충전 " + world.Charges + "회 · 당첨 " +
                    world.SuccessPercent + "%", OrdrMobileUiTheme.Subheading);
            GUILayout.EndVertical();
        }

        private void DrawGraduationHoldRow()
        {
            const string id = "gamble:graduation-status";
            MobileLongPressResult result = OrdrMobileUiTheme.DrawHoldActionRow(
                id, "고급 졸업", "완료", OrdrMobileUiTheme.Gold,
                gamblingPressGesture, 62f, true);
            if (result == MobileLongPressResult.Hold)
                expandedGamblingOfferId = expandedGamblingOfferId == id ? null : id;
            if (expandedGamblingOfferId != id) return;
            var desk = controller.PracticeMoneyGamblingDesk;
            GUILayout.BeginVertical(OrdrMobileUiTheme.HelpBubble);
            GUILayout.Label("고급 누적 반환 " + desk.High.GoldReturned.ToString("N0") +
                " / " + SourceModelMoneyGamblingDesk.GraduationGold.ToString("N0") +
                "골드", OrdrMobileUiTheme.Explanation);
            GUILayout.Label("목재 구매 기회 " + desk.WoodRights + "회",
                OrdrMobileUiTheme.Explanation);
            GUILayout.EndVertical();
        }

        // The generic shop had no source-backed items in the base build.
        // Its UI entry is removed, while the verified shop/economy backend is
        // retained for future original transactions. The Wisp menu keeps its
        // own distinct, direct one-transaction buttons.
        private void DrawWispEconomyOffers()
        {
            foreach (var offer in economyOffers)
            {
                if (offer.Id == OriginalRandomWispResourceExchange.OfferId ||
                    offer.Id == OriginalHelperCowRules.OfferId)
                    continue;
                if (OrdrMobileUiTheme.DrawActionRow(offer.Title,
                    "1회", OrdrMobileUiTheme.Gold))
                {
                    string offerId = offer.Id;
                    RunWispDirect(offer.Title, () =>
                        controller.ManualActions.RequestEconomyAction(
                            ManualEconomyCategory.Wisp, offerId));
                }
            }
        }

        private void DrawGamblingNavigation()
        {
            var navigation = controller.PracticeGamblingNavigation;
            if (navigation == null) return;
            GUILayout.BeginVertical(OrdrMobileUiTheme.AccordionBody);
            OrdrMobileUiTheme.DrawStatusRow("항법",
                navigation.ChosenNavigation ?? "21R부터 한 가지 선택", OrdrMobileUiTheme.Aqua);
            GUILayout.Label("희귀 리롤 " + navigation.RareRerolls + "회 · 보유 유닛을 선택해 사용");
            if (navigation.ChosenNavigation == null)
            {
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && navigation.CanChoose;
                if (pendingGamblingNavigation.HasValue)
                {
                    string label = pendingGamblingNavigation.Value == Original323GamblingNavigation.Casino
                        ? "카지노" : pendingGamblingNavigation.Value == Original323GamblingNavigation.RiskHedge
                        ? "리스크헷지" : "환전소";
                    GUILayout.Label(label + " 선택 후 다른 항법으로 바꿀 수 없습니다.");
                    if (GUILayout.Button(label + " 항법 확정", GUILayout.MinHeight(64f)))
                    {
                        notice = navigation.TryChoose(pendingGamblingNavigation.Value) ? "항법을 선택했습니다." : "항법 선택 조건 미충족";
                        pendingGamblingNavigation = null;
                        RefreshCurrent();
                    }
                }
                else
                {
                    MobileLongPressResult casinoPress =
                        OrdrMobileUiTheme.DrawHoldActionRow("nav:casino",
                            "카지노 선택", "선택", OrdrMobileUiTheme.Gold,
                            gamblingPressGesture);
                    if (casinoPress == MobileLongPressResult.Tap)
                        pendingGamblingNavigation = Original323GamblingNavigation.Casino;
                    else if (casinoPress == MobileLongPressResult.Hold)
                        expandedGamblingOfferId =
                            expandedGamblingOfferId == "nav:casino" ? null : "nav:casino";
                    if (expandedGamblingOfferId == "nav:casino")
                        GUILayout.Label("이세계 충전 2배 · 실패 보상 강화 · 하급·중급 도박 이용 불가");
                    MobileLongPressResult hedgePress =
                        OrdrMobileUiTheme.DrawHoldActionRow("nav:hedge",
                            "리스크헷지 선택", "선택", OrdrMobileUiTheme.Aqua,
                            gamblingPressGesture);
                    if (hedgePress == MobileLongPressResult.Tap)
                        pendingGamblingNavigation = Original323GamblingNavigation.RiskHedge;
                    else if (hedgePress == MobileLongPressResult.Hold)
                        expandedGamblingOfferId =
                            expandedGamblingOfferId == "nav:hedge" ? null : "nav:hedge";
                    if (expandedGamblingOfferId == "nav:hedge")
                        GUILayout.Label("실패마다 당첨 확률 +15%p · 리롤 재료 무료 · 고급 실패 보상 추가");
                    GUI.enabled = enabled && navigation.CanChooseExchangeHouse;
                    MobileLongPressResult exchangePress =
                        OrdrMobileUiTheme.DrawHoldActionRow("nav:exchange-house",
                            "환전소 선택", "선택", OrdrMobileUiTheme.Gold,
                            gamblingPressGesture);
                    GUI.enabled = enabled;
                    if (exchangePress == MobileLongPressResult.Tap)
                        pendingGamblingNavigation = Original323GamblingNavigation.ExchangeHouse;
                    else if (exchangePress == MobileLongPressResult.Hold)
                        expandedGamblingOfferId =
                            expandedGamblingOfferId == "nav:exchange-house" ? null : "nav:exchange-house";
                    if (expandedGamblingOfferId == "nav:exchange-house")
                        GUILayout.Label("21R 이후 원본 콤푸(I009) 전용 교환 7종 · 시작 충전 0 · 다른 항법과 중복 불가");
                    if (!navigation.CanChooseExchangeHouse)
                        GUILayout.Label("원본 콤푸 획득 사건이 연결되기 전에는 환전소 항법 선택 불가",
                            OrdrMobileUiTheme.Overline);
                }
                GUI.enabled = enabled;
                if (pendingGamblingNavigation.HasValue && GUILayout.Button("취소", GUILayout.MinHeight(60f)))
                    pendingGamblingNavigation = null;
                if (!navigation.CanChoose) GUILayout.Label("21R 이후 · 일시정지 해제 · 보류 거래 완료 후 선택");
            }
            GUILayout.EndVertical();
        }

        private void DrawCompuExchange()
        {
            var exchange = controller.PracticeCompuExchange;
            if (exchange == null ||
                controller.PracticeGamblingNavigation == null ||
                controller.PracticeGamblingNavigation.Navigation !=
                    Original323GamblingNavigation.ExchangeHouse)
                return;

            GUILayout.Label("콤푸 환전", OrdrMobileUiTheme.Subheading);
            OrdrMobileUiTheme.DrawStatusRow("보유 콤푸",
                exchange.Compu + "개", OrdrMobileUiTheme.Aqua);
            string[] recipes = { "CKD", "CKE", "CKF", "CKG", "CKH", "CKI", "CKJ" };
            string[] names = {
                "골드·위습", "목재", "흔함선택위습",
                "해적선", "일반 발굴권", "유니크 발굴권",
                "랜덤유닛·토큰"
            };
            for (int i = 0; i < recipes.Length; i++)
            {
                string recipe = recipes[i];
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && exchange.CanExchange(recipe);
                if (OrdrMobileUiTheme.DrawActionRow(
                    names[i] + " · " + SourceModelCompuExchange.Cost(recipe) +
                        "콤푸", "교환", OrdrMobileUiTheme.Gold, 66f))
                    RunGamblingRewardAction(() =>
                        controller.RequestCompuExchange(recipe));
                GUI.enabled = enabled;
            }
        }

        private void DrawItemExcavation()
        {
            var excavation = controller.PracticeItemExcavation;
            if (excavation == null) return;
            GUILayout.Label("아이템 발굴", OrdrMobileUiTheme.Subheading);
            OrdrMobileUiTheme.DrawStatusRow("일반",
                "스택 " + excavation.OrdinaryStock,
                OrdrMobileUiTheme.Aqua);
            OrdrMobileUiTheme.DrawStatusRow("유니크",
                "스택 " + excavation.UniqueStock,
                OrdrMobileUiTheme.Gold);

            bool enabled = GUI.enabled;
            GUI.enabled = enabled && excavation.CanDraw(false);
            if (OrdrMobileUiTheme.DrawActionRow(
                "일반 발굴", "90% / 10%",
                OrdrMobileUiTheme.Gold, 66f))
                RunGamblingRewardAction(() =>
                    controller.RequestItemExcavation(false));
            GUI.enabled = enabled && excavation.CanDraw(true);
            if (OrdrMobileUiTheme.DrawActionRow(
                "유니크 확정 발굴", "1스택",
                OrdrMobileUiTheme.Gold, 66f))
                RunGamblingRewardAction(() =>
                    controller.RequestItemExcavation(true));
            GUI.enabled = enabled;
        }

        private HashSet<long> CaptureRewardRosterIds()
        {
            var ids = new HashSet<long>();
            if (Combat == null) return ids;
            foreach (var entry in Combat.MobileUnitSnapshot())
                if (entry != null) ids.Add(entry.InstanceId);
            return ids;
        }

        private void RunGamblingOffer(MobileManualOffer offer)
        {
            RunGamblingRewardAction(() =>
                controller.ManualActions.RequestEconomyAction(
                    ManualEconomyCategory.Gambling, offer.Id));
        }

        private void RunGamblingRewardAction(Func<ManualActionResult> action)
        {
            var before = CaptureRewardRosterIds();
            try
            {
                // One explicit tap sends one authoritative transaction.
                ManualActionResult result = action();
                ShowResult(result);
                if (PublishRewardToast(result, before, true))
                    notice = "";
            }
            catch (Exception error)
            {
                // Never retry a draw after an uncertain post-commit failure.
                Debug.LogException(error);
                notice = "거래 상태 확인 필요 · 자동 재시도 안 함: " + error.Message;
            }
            RefreshCurrent();
        }

        private void AddBattleLog(string text, string unitId = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            battleLog.Add(new BattleLogItem {
                Text = text,
                UnitId = unitId,
                StartedAt = Time.unscaledTime
            });
            while (battleLog.Count > 3) battleLog.RemoveAt(0);
        }

        private bool PublishRewardToast(ManualActionResult result,
            HashSet<long> before, bool alsoShowNonUnitResult)
        {
            if (result == null || result.State != ManualActionState.Succeeded)
                return false;
            var newUnits = new List<MobileUnitSnapshot>();
            if (Combat != null)
                foreach (var row in Combat.MobileUnitSnapshot())
                    if (row != null && !before.Contains(row.InstanceId))
                        newUnits.Add(row);
            if (newUnits.Count == 0 && !alsoShowNonUnitResult)
                return false;
            if (newUnits.Count > 0)
            {
                newUnits.Sort((a, b) => a.InstanceId.CompareTo(b.InstanceId));
                // Group actual acquired instances by character ID, so log
                // quantities are never inferred from a roll preview.
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var unit in newUnits)
                {
                    if (!seen.Add(unit.Unit.Id)) continue;
                    int count = 0;
                    foreach (var row in newUnits)
                        if (row.Unit.Id == unit.Unit.Id) count++;
                    AddBattleLog("획득 · " + unit.Unit.Name +
                        (count > 1 ? " ×" + count : ""), unit.Unit.Id);
                }
            }
            else
                AddBattleLog(CompactRewardMessage(result.Message));
            return true;
        }

        private static string CompactRewardMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "결과 확인";
            int returned = message.IndexOf("골드 반환", StringComparison.Ordinal);
            if (returned >= 0)
            {
                int slash = message.LastIndexOf('/', returned);
                if (slash >= 0)
                {
                    string payout = message.Substring(slash + 1,
                        returned - slash + "골드 반환".Length - 1).Trim();
                    return payout + (message.Contains("졸업!") ? " · 졸업!" : "");
                }
            }
            int sep = message.IndexOf(" · ", StringComparison.Ordinal);
            return sep > 0 && message.Length > 60 ?
                message.Substring(sep + 3) : message;
        }

        private bool RewardToastVisible
        {
            get {
                for (int i = 0; i < battleLog.Count; i++)
                    if (Time.unscaledTime - battleLog[i].StartedAt <
                        BattleLogLifetimeSeconds) return true;
                return false;
            }
        }

        private static Rect RewardToastRect(MobileMapSkillLayout layout, float scale)
        {
            Rect battlefield = MobileMapSkillLayout.Scaled(layout.Battle, scale);
            float width = Mathf.Min(360f, Mathf.Max(65f, battlefield.width - 12f));
            float height = Mathf.Min(88f, Mathf.Max(24f, battlefield.height - 41f));
            // The permanent clock occupies the battlefield's first 30px.
            // Event log sits underneath without covering either clock/drawer.
            return new Rect(battlefield.x + 6f, battlefield.y + 37f,
                width, height);
        }

        private static string FitBattleLogText(GUIStyle style,
            string value, float width)
        {
            if (string.IsNullOrEmpty(value) || width <= 4f) return "";
            var label = new GUIContent(value);
            if (style.CalcSize(label).x <= width) return value;
            const string ellipsis = "…";
            int length = value.Length;
            while (length > 1 &&
                style.CalcSize(new GUIContent(value.Substring(0, length) +
                    ellipsis)).x > width) length--;
            return value.Substring(0, length) + ellipsis;
        }

        private void DrawRewardToast(MobileMapSkillLayout layout, float scale)
        {
            if (!RewardToastVisible || Event.current.type != EventType.Repaint)
                return;
            Rect area = RewardToastRect(layout, scale);
            int count = 0;
            for (int i = 0; i < battleLog.Count; i++)
                if (Time.unscaledTime - battleLog[i].StartedAt <
                    BattleLogLifetimeSeconds) count++;
            if (count == 0) return;
            float lineHeight = Mathf.Min(28f, area.height / count);
            float rowY = area.y;
            int oldDepth = GUI.depth;
            Color oldColor = GUI.color;
            try
            {
                GUI.depth = -900;
                var style = new GUIStyle(GUI.skin.label) {
                    fontStyle = FontStyle.Bold, fontSize = 16,
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = false, clipping = TextClipping.Clip,
                    padding = new RectOffset(0, 0, 0, 0)
                };
                style.normal.textColor = Color.white;
                for (int i = 0; i < battleLog.Count; i++)
                {
                    var item = battleLog[i];
                    float age = Time.unscaledTime - item.StartedAt;
                    if (age >= BattleLogLifetimeSeconds) continue;
                    float fade = Mathf.Clamp01(
                        (BattleLogLifetimeSeconds - age) / .7f);
                    Rect line = new Rect(area.x, rowY, area.width, lineHeight - 2f);
                    GUI.color = new Color(.035f,.065f,.10f,.82f * fade);
                    GUI.DrawTexture(line, Texture2D.whiteTexture);
                    GUI.color = new Color(1f, 1f, 1f, fade);
                    float labelX = line.x + 10f;
                    Sprite portrait = string.IsNullOrEmpty(item.UnitId) ?
                        null : OrdrCharacterPortraits.Find(item.UnitId);
                    if (portrait == null && !string.IsNullOrEmpty(item.UnitId))
                        portrait = OrdrBattlefieldArt.UnitSprite(item.UnitId);
                    if (portrait != null)
                    {
                        float portraitSize = Mathf.Min(line.height - 2f, 24f);
                        Rect image = new Rect(line.x + 3f, line.y + 1f,
                            portraitSize, portraitSize);
                        Rect src = portrait.rect;
                        GUI.DrawTextureWithTexCoords(image, portrait.texture,
                            new Rect(src.x / portrait.texture.width,
                                src.y / portrait.texture.height,
                                src.width / portrait.texture.width,
                                src.height / portrait.texture.height));
                        labelX = image.xMax + 6f;
                    }
                    float width = Mathf.Max(0f, line.xMax - labelX - 6f);
                    GUI.Label(new Rect(labelX, line.y, width, line.height),
                        FitBattleLogText(style, item.Text, width), style);
                    rowY += lineHeight;
                }
            }
            finally
            {
                GUI.color = oldColor;
                GUI.depth = oldDepth;
            }
        }

        private void RefreshCurrent()
        {
            if (Combat == null) return;
            if (currentTab == Tab.Play) utilitySpecSnapshot = null;
            if (currentTab == Tab.Fusion || currentTab == Tab.Play || fusionGoal.HasGoal)
            {
                fusionOffers = controller.ManualActions.PreviewFusion(Combat.MobileUnitSnapshot());
                if (showOriginalJassRecipes && controller.OriginalFusionPlanner != null)
                    originalJassPreview = controller.OriginalFusionPlanner.AssessNamedResults(
                        Combat.MobileUnitSnapshot(), null, null);
            }
            if (currentTab == Tab.Gambling || currentTab == Tab.Wisp)
                economyOffers = controller.ManualActions.ListEconomyOffers(
                    currentTab == Tab.Gambling ? ManualEconomyCategory.Gambling :
                        ManualEconomyCategory.Wisp);
        }

        private void ShowResult(ManualActionResult result)
        {
            if (result.State == ManualActionState.Succeeded)
                ownedFrameSnapshotAt = -1;
            notice = result.State == ManualActionState.Succeeded
                ? "실행 완료: " + result.Message : "실행 안 됨: " + result.Message;
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




