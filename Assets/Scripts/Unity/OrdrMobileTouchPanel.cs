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
    // 임시 Android 터치 패널. Unity UI 패키지/추가 이미지 없이 동작하도록
    // OnGUI로 구현했으며, 원작 전투 규칙이나 경제 수치는 변경하지 않는다.
    // 스토리·조합확인·상점·도박·위습 버튼은 실제 사용자 탭에서만 호출된다.
    public sealed class OrdrMobileTouchPanel : MonoBehaviour
    {
        private enum Tab { Play, Units, Story, Fusion, Shop, Gambling, Wisp, LogPose, Helper, Alchemy, Missions }

        [SerializeField] private OrdrCharacterCombatController controller;
        [SerializeField] private bool openByDefault;
        private bool visible;
        private Tab currentTab = Tab.Play;
        private float nextFusionRefresh;
        private string fusionInventorySignature;
        private int selectedPracticeRounds = 20;
        private bool browsingFullCatalog;
        private bool showUnitDetails;
        private long? expandedOwnedUnitId;
        private string expandedRecipeId;
        private string catalogSearch = "";
        private bool showChoiceWisp;
        private readonly Dictionary<Tab, Vector2> tabScrollPositions = new Dictionary<Tab, Vector2>();
        private int catalogGradeIndex;
        private int catalogPage;
        private int ownedPage;
        private string ownedSearch = "";
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
            float stripWidth = Mathf.Min(760f, area.width - 12f);
            area = new Rect(area.xMax - stripWidth - 6f, area.y, stripWidth, area.height);
            var economy=controller.SharedEconomy;
            var chips=MobileMapSkillLayout.StatusChipRects(area);
            OrdrMobileUiTheme.ResourceChip(chips[0],"골드",
                economy==null?"--":economy.Wallet.Balance("gold").ToString(),OrdrMobileUiTheme.Gold);
            OrdrMobileUiTheme.ResourceChip(chips[1],"목재",
                economy==null?"--":economy.Wallet.Balance("wood").ToString(),OrdrMobileUiTheme.Aqua);
            OrdrMobileUiTheme.ResourceChip(chips[2],"위습",
                economy==null?"--":economy.Wallet.Balance(WispResourceIds.Random).ToString(),
                new Color(.77f,.63f,1f,1f));
            OrdrMobileUiTheme.ResourceChip(chips[3], "특성포인트",
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

            GUILayout.Label("플레이",OrdrMobileUiTheme.Overline);
            DrawLeftTab(Tab.Play, "점수판");
            DrawLeftTab(Tab.Wisp, "위습");
            DrawLeftTab(Tab.Helper, "도움소");
            if (controller.Original323HelperCow != null &&
                controller.Original323HelperCow.AlchemyNavigationChosen)
                DrawLeftTab(Tab.Alchemy, "연금술");
            GUILayout.Space(5f);
            GUILayout.Label("편성",OrdrMobileUiTheme.Overline);
            DrawLeftTab(Tab.Units, "유닛");
            DrawLeftTab(Tab.Fusion, "조합");
            DrawLeftTab(Tab.Story, "스토리");
            GUILayout.Space(5f);
            GUILayout.Label("운영",OrdrMobileUiTheme.Overline);
            DrawLeftTab(Tab.LogPose, "기록지침");
            DrawLeftTab(Tab.Shop, "상점");
            DrawLeftTab(Tab.Gambling, "도박");
            DrawLeftTab(Tab.Missions, "미션");

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
        }

        private void DrawRightDrawer(MobileMapSkillLayout layout, float scale)
        {
            Rect area = MobileMapSkillLayout.Scaled(layout.RightDrawer, scale);
            GUILayout.BeginArea(area, OrdrMobileUiTheme.Panel);
            OrdrMobileUiTheme.DrawPanelAccent(new Rect(0,0,area.width,area.height),
                OrdrMobileUiTheme.Gold);
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label(TabTitle(currentTab), OrdrMobileUiTheme.SectionTitle,
                GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            if (GUILayout.Button("닫기", GUILayout.Width(64f), GUILayout.MinHeight(52f)))
                CloseDrawer();
            GUILayout.EndHorizontal();

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
                    case Tab.Shop: DrawEconomy(ManualEconomyCategory.Shop); break;
                    case Tab.Gambling: DrawEconomy(ManualEconomyCategory.Gambling); break;
                    case Tab.Wisp: DrawWisp(); break;
                    case Tab.LogPose: DrawTreasureGacha(); break;
                    case Tab.Helper: DrawHelperCow(); break;
                    case Tab.Alchemy: DrawAlchemyPanel(); break;
                    case Tab.Missions: DrawPersonalMissions(); break;
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
            Rect area=MobileMapSkillLayout.Scaled(layout.RightDrawer,scale);
            GUILayout.BeginArea(area,OrdrMobileUiTheme.Panel);
            GUILayout.Label("점수판",OrdrMobileUiTheme.SectionTitle);
            GUILayout.Space(5f);
            dashboardScroll = BeginPanelScroll(dashboardScroll);
            if(Combat==null)
                GUILayout.Label("전투 대기");
            else
            {
                var game=Combat.Game;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(controller.IsUnitPreparation?"출격 준비":"현재 전황",OrdrMobileUiTheme.Subheading);
                GUILayout.Label("라운드 "+game.CurrentWave+
                    "   전투 유닛 "+Combat.CharacterCount+
                    "   남은 적 "+game.AliveEnemies);
                if (controller.OriginalTreasureHunt != null)
                    GUILayout.Label("보물상자 미수집 " +
                        controller.OriginalTreasureHunt.UncollectedCount + "개");
                if(controller.PracticeBattle!=null)
                {
                    GUILayout.Label("남은 시간 " + Math.Ceiling(controller.PracticeBattle.SecondsRemaining) + "초");
                    GUILayout.Label("처치 "+game.DefeatedEnemies+"   제한 "+
                        controller.PracticeBattle.EnemyLimit+"마리");
                }
                GUILayout.EndVertical();
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
                case Tab.Gambling: return "도박";
                case Tab.Missions: return "개인 미션";
                default: throw new ArgumentOutOfRangeException(nameof(tab));
            }
        }

        private void DrawPlay()
        {
            if (Combat == null || controller.SharedEconomy == null)
            {
                GUILayout.Label("전투 대기");
                return;
            }
            var game = Combat.Game;
            var practice = controller.PracticeBattle;
            GUILayout.Label("보유 전투 유닛 " + Combat.CharacterCount + " · 자동 공격 " + Combat.ReadyAttackers);
            if (controller.IsUnitPreparation)
            {
                bool previous = GUI.enabled;
                GUI.enabled = previous && Combat.CharacterCount > 0;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(selectedPracticeRounds == 10 ? "10R ✓" : "10R", GUILayout.MinHeight(54))) selectedPracticeRounds = 10;
                if (GUILayout.Button(selectedPracticeRounds == 20 ? "20R ✓" : "20R", GUILayout.MinHeight(54))) selectedPracticeRounds = 20;
                if (GUILayout.Button(selectedPracticeRounds == 65 ? "65R ✓" : "65R", GUILayout.MinHeight(54))) selectedPracticeRounds = 65;
                GUILayout.EndHorizontal();
                if (selectedPracticeRounds == 65)
                    GUILayout.Label("원본 출현 규칙·악몽 초기 수치 모델 기반 · 동적 면역/최종 피해 일치 검증 전");
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
                GUILayout.Label("남은 시간 " + Math.Ceiling(practice.SecondsRemaining) + "초");
                GUILayout.Label("처치 " + game.DefeatedEnemies + " · 남은 적 " + game.AliveEnemies +
                    "/" + practice.EnemyLimit + " · 전투 시간 " + Math.Floor(Combat.Time) + "초");
                GUILayout.Label("처치 골드 +" + practice.GoldAwarded +
                    " · 라운드 위습 +" + practice.RoundWispsAwarded);
                foreach (var enemy in Combat.MobileEnemySnapshot())
                    if (enemy.Kind == CharacterEnemyKind.Boss)
                        GUILayout.Label("보스 HP " + enemy.Health + " / " + enemy.MaxHealth);
                if (Combat.PracticeUnitEffects != null && Combat.PracticeUnitEffects.DeferredAuraGroups > 0)
                    GUILayout.Label("오라 중첩 확인 대기 " + Combat.PracticeUnitEffects.DeferredAuraGroups + "건");
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
            GUILayout.Label("목표 달성 시 보상이 자동으로 지급됩니다.");
            GUILayout.Label(missions.HasRerolled ? "미션 교체 사용 완료" :
                missions.CurrentRound >= 10 ? "미션 교체 기간 종료" :
                "10라운드 전까지 미완료 미션 1개를 한 번 교체할 수 있습니다.");
            bool hasPendingReward = false;
            foreach (var slot in missions.Slots)
            {
                hasPendingReward |= slot.RewardPending;
                var rule = slot.Mission;
                bool expired = !slot.Completed && !slot.RewardPending &&
                    rule.MustFinishBeforeRound > 0 && missions.CurrentRound >= rule.MustFinishBeforeRound;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label((slot.Index + 1) + ". " + rule.Name +
                    (slot.Completed ? "  · 완료" : slot.RewardPending ? "  · 보상 지급 대기" :
                     expired ? "  · 기한 종료" : ""));
                GUILayout.Label(rule.Objective);
                GUILayout.Label("진행 " + missions.ProgressAt(slot.Index) + " / " + rule.RequiredCount);
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
                    pendingMissionReroll = slot.Index;
                GUILayout.EndVertical();
            }
            GUILayout.Label("보유 특성포인트 " + controller.SharedEconomy.Wallet.Balance(
                OriginalMissionWalletRewards.TraitPointsId));
            if (missions.AllThreeCompleted)
                GUILayout.Label("개인 미션 3개 완료 · 승리 시 추가 베리 1개 대상");
            if (hasPendingReward)
            {
                GUILayout.Label("달성한 보상의 지급을 다시 시도할 수 있습니다.");
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

        // Every catalog unit can be inspected; only units actually owned
        // by the running world appear in the live selection list. No free spawns.
        private void DrawUnits()
        {
            var definitions = controller.UnitDefinitions;
            if (definitions == null)
            {
                GUILayout.Label("유닛 카탈로그를 불러오는 중입니다.");
                return;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(browsingFullCatalog ? "전체 도감" : "보유 유닛",
                OrdrMobileUiTheme.Subheading, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            if (GUILayout.Button(browsingFullCatalog ? "보유 목록" : "전체 도감",
                GUILayout.Width(110), GUILayout.MinHeight(56)))
            {
                browsingFullCatalog = !browsingFullCatalog;
                showUnitDetails = false;
                selectedCatalogUnitId = null;
                pendingCommonSaleId = null;
                pendingDecompositionId = null;
                scroll = Vector2.zero;
            }
            GUILayout.EndHorizontal();
            var detail = definitions.Find(selectedCatalogUnitId);
            if (showUnitDetails && detail != null)
            {
                if (GUILayout.Button("목록으로 돌아가기", GUILayout.MinHeight(56)))
                {
                    showUnitDetails = false;
                    scroll = Vector2.zero;
                }
                DrawUnitDetails(detail);
                return;
            }
            if (detail != null)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("선택: " + detail.DisplayName, OrdrMobileUiTheme.Subheading);
                if (GUILayout.Button("스킬·공격 정보 보기", GUILayout.MinHeight(56)))
                {
                    showUnitDetails = true;
                    scroll = Vector2.zero;
                }
                GUILayout.EndVertical();
            }
            if (browsingFullCatalog) DrawCatalogIndex(definitions);
            else DrawOwnedUnitIndex();
        }

        private void DrawUnitDetails(UnitImplementationDefinition detail)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(detail.DisplayName + " · " + detail.Grade);
            if (controller.PracticeBattle != null)
            {
                var native = controller.MapBaseStats.Find(detail.Unit.Id);
                GUILayout.Label(native?.Attack1 != null
                    ? "연습: 객체 기본 공격 · 아래 연결 효과 사용 / 전체 스킬 미완성"
                    : "기본 공격 없음 또는 원본 객체 연결 대기");
                var effects = Combat.PracticeUnitEffects;
                if (effects != null)
                {
                    var connected = effects.Describe(detail.Unit.Id, native?.OriginalRawcode);
                    foreach (string text in connected) GUILayout.Label("연습 연결 · " + text);
                    if (connected.Count == 0) GUILayout.Label("추가 효과 연결 없음 · 도감 설명은 실행 완료를 의미하지 않습니다.");
                }
            }
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
            if (detail.Grade == "안흔함" &&
                controller.OriginalUncommonAbilities != null)
            {
                var abilities = controller.OriginalUncommonAbilities.ForUnit(detail.Unit.Id);
                GUILayout.Label("원본 맵 고유 능력 " + abilities.Count +
                    "개 (전투 실행은 독립 검증 필요)");
                foreach (var native in abilities)
                    GUILayout.Label("· " + native.Name);
                if (abilities.Count == 0)
                    GUILayout.Label("원본에서 확인된 추가 공격 능력 없음. 조합·판매 능력은 별도.");
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
                GUILayout.Label("월드가 시작되면 보유한 유닛이 이곳에 표시됩니다.");
                return;
            }
            var owned = Combat.MobileUnitSnapshot();
            GUILayout.Label("보유 " + owned.Count + "기 · 이름/등급/번호 검색");
            string query = GUILayout.TextField(ownedSearch, GUILayout.MinHeight(56f));
            if (query != ownedSearch) { ownedSearch = query; ownedPage = 0; }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("전체" + (ownedFilter == OwnedRosterFilter.All ? " ✓" : ""), GUILayout.MinHeight(56)))
            { ownedFilter = OwnedRosterFilter.All; ownedPage = 0; }
            if (GUILayout.Button("판매 가능" + (ownedFilter == OwnedRosterFilter.Saleable ? " ✓" : ""), GUILayout.MinHeight(56)))
            { ownedFilter = OwnedRosterFilter.Saleable; ownedPage = 0; }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("분해 가능" + (ownedFilter == OwnedRosterFilter.Decomposable ? " ✓" : ""), GUILayout.MinHeight(56)))
            { ownedFilter = OwnedRosterFilter.Decomposable; ownedPage = 0; }
            if (GUILayout.Button("조건 초기화", GUILayout.MinHeight(56)))
            { ownedSearch = ""; ownedFilter = OwnedRosterFilter.All; ownedPage = 0; }
            GUILayout.EndHorizontal();
            int matches, pages, resolved;
            var displayed = MobileOwnedRosterBrowser.Select(owned, ownedSearch,
                ownedFilter, u => controller.LowerGradeSales != null &&
                    controller.LowerGradeSales.CanSell(u), ownedPage, 8,
                out matches, out pages, out resolved);
            ownedPage = resolved;
            GUILayout.Label("검색 " + matches + "기 · " + (resolved + 1) + "/" + pages + "쪽");
            if (matches == 0) GUILayout.Label("조건에 맞는 유닛이 없습니다. 검색어나 필터를 초기화해 보세요.");
            DrawPager(ownedPage, pages, next => ownedPage = next);
            DrawBatchSaleConfirm();
            if (controller.LowerGradeSales != null)
                GUILayout.Label("흔함 판매 포인트 " + controller.LowerGradeSales.CommonPoints +
                    "/3 · 3기마다 랜덤위습 1개");
            if (owned.Count > 0 && GUILayout.Button(controller.PracticeBattle == null ? "보유 유닛 전체 보기" : "전장 전체 보기", GUILayout.MinHeight(56)))
            {
                var presenter = controller.GetComponent<OrdrUnitWorldPresenter>();
                if (presenter != null) presenter.ShowAllOwnedUnits();
            }
            foreach (var entry in displayed)
            {
                bool selected = Combat.Roster.SelectedId == entry.InstanceId;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(entry.Unit.Name, OrdrMobileUiTheme.Subheading);
                GUILayout.Label(entry.Unit.Grade + " · #" + entry.InstanceId +
                    (selected ? " · 선택됨" : "") +
                    (pendingBatchSales.Contains(entry.InstanceId) ? " · 판매 선택" : ""));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(selected ? "선택됨" : "선택",
                    selected ? OrdrMobileUiTheme.SelectedTab : GUI.skin.button, GUILayout.MinHeight(56)))
                {
                    Combat.Roster.Select(entry.InstanceId);
                    selectedCatalogUnitId = entry.Unit.Id;
                    notice = entry.Unit.Name + " 선택";
                }
                if (GUILayout.Button(expandedOwnedUnitId == entry.InstanceId ? "관리 접기" : "관리",
                    GUILayout.MinHeight(56)))
                {
                    expandedOwnedUnitId = expandedOwnedUnitId == entry.InstanceId ? (long?)null : entry.InstanceId;
                    pendingCommonSaleId = null;
                    pendingDecompositionId = null;
                }
                GUILayout.EndHorizontal();
                if (expandedOwnedUnitId != entry.InstanceId)
                {
                    GUILayout.EndVertical();
                    continue;
                }
                if (GUILayout.Button("전장 위치 보기", GUILayout.MinHeight(56)))
                {
                    var presenter = controller.GetComponent<OrdrUnitWorldPresenter>();
                    if (presenter != null) presenter.ShowOwnedUnit(entry.InstanceId);
                }
                if (GUILayout.Button("재료로 쓰는 조합 찾기 · " + entry.Unit.Name,
                    GUILayout.MinHeight(54)))
                {
                    SelectTab(Tab.Fusion);
                    fusionSearch = entry.Unit.Name;
                    showOnlyBookmarkedFusion = false;
                    fusionPage = 0;
                    fusionMaterialsReadyOnly = false;
                    fusionExecutableOnly = true;
                    notice = "실제 제작식 검색만 열었습니다. 재료는 소비되지 않았습니다.";
                }
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

        private void DrawCatalogIndex(UnitImplementationRegistry definitions)
        {
            var grades = definitions.Grades;
            if (grades.Count == 0) return;
            catalogGradeIndex = Math.Max(0, Math.Min(catalogGradeIndex, grades.Count));
            GUILayout.Label("이름·등급·유닛 번호로 검색");
            string query = GUILayout.TextField(catalogSearch, GUILayout.MinHeight(56));
            if (query != catalogSearch) { catalogSearch = query; catalogPage = 0; }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(54), GUILayout.MinHeight(56)))
            {
                catalogGradeIndex = (catalogGradeIndex + grades.Count) % (grades.Count + 1);
                catalogPage = 0;
            }
            GUILayout.Label(catalogGradeIndex == 0 ? "모든 등급" : grades[catalogGradeIndex - 1],
                GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            if (GUILayout.Button("▶", GUILayout.Width(54), GUILayout.MinHeight(56)))
            {
                catalogGradeIndex = (catalogGradeIndex + 1) % (grades.Count + 1);
                catalogPage = 0;
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("검색·등급 초기화", GUILayout.MinHeight(56)))
            { catalogSearch = ""; catalogGradeIndex = 0; catalogPage = 0; }
            var source = catalogGradeIndex == 0 ? definitions.All : definitions.OfGrade(grades[catalogGradeIndex - 1]);
            var entries = new List<UnitImplementationDefinition>();
            string needle = catalogSearch.Trim();
            foreach (var item in source)
                if (needle.Length == 0 ||
                    item.DisplayName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Grade.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.UnitId.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    entries.Add(item);
            const int pageSize = 8;
            int pages = Math.Max(1, (entries.Count + pageSize - 1) / pageSize);
            catalogPage = Math.Min(catalogPage, pages - 1);
            GUILayout.Label("도감 검색 " + entries.Count + "종");
            if (entries.Count == 0) GUILayout.Label("조건에 맞는 유닛이 없습니다. 모든 등급에서 다시 찾아보세요.");
            DrawPager(catalogPage, pages, next => catalogPage = next);
            for (int i = catalogPage * pageSize; i < Math.Min(entries.Count, (catalogPage + 1) * pageSize); i++)
            {
                var unit = entries[i];
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(unit.DisplayName, OrdrMobileUiTheme.Subheading);
                GUILayout.Label(unit.Grade);
                if (GUILayout.Button("정보 보기", GUILayout.MinHeight(56)))
                {
                    selectedCatalogUnitId = unit.UnitId;
                    showUnitDetails = true;
                    scroll = Vector2.zero;
                }
                GUILayout.EndVertical();
            }
            DrawPager(catalogPage, pages, next => catalogPage = next);
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
            var catalog = controller.StoryBuildingCatalog;
            var world = controller.PracticeStoryBuildings;
            if (catalog == null)
                GUILayout.Label("원본 스토리 건물 자료는 전투 시작 후 표시됩니다.");
            else
            {
                for (int stage = 1; stage <= 14; stage++)
                {
                    var building = catalog.Find(stage);
                    if (building == null) continue;
                    string status = "미배치";
                    if (world != null)
                    {
                        foreach (var encounter in world.Encounters)
                            if (encounter.Building.Stage == stage)
                                status = encounter.State == Original323StoryBuildingState.Alive
                                    ? "건물 유지" : encounter.State == Original323StoryBuildingState.Destroyed
                                        ? "파괴 기록" : "제거 기록";
                    }
                    GUILayout.Label(building.Name + " · " + status);
                }
            }
            GUILayout.Label("원본 스토리 건물의 실전 출현 위치·진행 조건·보상은 미검증입니다. " +
                "연습 생성 API는 검증용이며 이 화면에서 임의로 소환하지 않습니다.");
            if (!controller.ManualActions.HasStoryService)
                GUILayout.Label("원작 스토리 파견 서비스 미연결: 파견 기능 비활성");
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
            DrawOriginalJassAudit();

            if (!controller.ManualActions.HasFusionService)
                GUILayout.Label("원작 조합식·재료·비용 시스템 미연결");
            else if (!controller.ManualActions.HasExecutableFusionService)
                GUILayout.Label("원작 제작 트리거 미연결: 미리보기 전용, 재료 소비 불가");
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
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(offer.Title, OrdrMobileUiTheme.Subheading);
                GUILayout.Label(!offer.IsReady ? "부족 재료 " + offer.MissingMaterials.Count + "종" :
                    executable ? "재료 준비 · 제작 가능" : "재료 준비 · 제작 연결 대기");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(expandedRecipeId == offer.RecipeId ? "상세 접기" : "상세 보기",
                    GUILayout.MinHeight(56)))
                    expandedRecipeId = expandedRecipeId == offer.RecipeId ? null : offer.RecipeId;
                bool previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && offer.IsReady && executable;
                if (GUILayout.Button("제작 선택", OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(56)))
                {
                    pendingFusion = offer;
                    fusionRepeatCount = 1;
                    pendingEconomy = null;
                    scroll = Vector2.zero;
                    notice = "제작 대기";
                }
                GUI.enabled = previousEnabled;
                GUILayout.EndHorizontal();
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
                GUILayout.Label("원본 재료 계보 (실제 제작 승인이 아님)");
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
            GUILayout.BeginVertical(GUI.skin.box);
            if (GUILayout.Button((showChoiceWisp ? "선택위습 접기 · " : "선택위습 사용 · ") +
                controller.WispBalance(WispResourceIds.Choice) + "개", GUILayout.MinHeight(56)))
                showChoiceWisp = !showChoiceWisp;
            if (!showChoiceWisp)
            {
                GUILayout.EndVertical();
                return;
            }
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
            GUILayout.Label("보물 탐색",OrdrMobileUiTheme.SectionTitle);
            var treasure=controller.OriginalTreasureHunt;
            GUILayout.BeginVertical(GUI.skin.box);
            if(treasure==null)
                GUILayout.Label("원본 보물 좌표·보상 월드 연결 대기",
                    OrdrMobileUiTheme.Explanation);
            else
            {
                GUILayout.Label("등장 보물 "+treasure.SpawnCount+
                    "개 · 남은 보물 "+treasure.UncollectedCount+"개",
                    OrdrMobileUiTheme.Subheading);
            }
            bool valid=treasure!=null && controller.MobileTreasureGacha!=null &&
                controller.MobileTreasureGacha.CanAttempt &&
                controller.HasOwnedOriginalGaimon &&
                controller.GaimonDream.Applied &&
                Combat!=null && Combat.Game.Phase==SessionPhase.Running &&
                !controller.IsUnitPreparation && Combat.Game.CurrentWave>0;
            bool before=GUI.enabled;
            GUI.enabled=before && valid;
            if (GUILayout.Button("보물 탐색",OrdrMobileUiTheme.PrimaryButton,
                GUILayout.MinHeight(78f)))
            {
                lastTreasureResult=controller.RequestMobileTreasureGacha();
                lastTreasureRound=Combat.Game.CurrentWave;
            }
            GUI.enabled=before;
            if(!controller.HasOwnedOriginalGaimon)
                GUILayout.Label("원본 가이몬(X-08/h0BG)을 실제 보유해야 탐색할 수 있습니다. 가이몬이 포함된 희귀위습 1% 분기는 확인됐지만 희귀위습 자동 지급은 아직 미완성입니다.",
                    OrdrMobileUiTheme.Explanation,GUILayout.MinHeight(86f));
            if(controller.HasOwnedOriginalGaimon)
                GUILayout.Label(controller.GaimonDream.Applied ?
                    "가이몬의 꿈 1회 적용 · 보물 탐색 범위 "+controller.GaimonDream.SearchHalfExtent.ToString("0") :
                    "가이몬의 꿈 원본 YL 플레이어 상태 확인 대기",
                    OrdrMobileUiTheme.Explanation,GUILayout.MinHeight(56f));
            if(treasure==null)
                GUILayout.Label("원본 보물 배치·실제 시전·보상 월드 연결 대기",
                    OrdrMobileUiTheme.Explanation,GUILayout.MinHeight(64f));
            if(controller.MobileTreasureGacha==null ||
                !controller.MobileTreasureGacha.CanAttempt)
                GUILayout.Label("원작 대비 발견 확률과 탐색 횟수 검증이 미완료입니다. 임의 확률과 무료 보상을 지급하지 않습니다.",
                    OrdrMobileUiTheme.Explanation,GUILayout.MinHeight(86f));
            // CCQ/R is automatic by policy. Only a witnessed original
            // event produces information, never a second payout button.
            if(controller.GaimonReveal != null &&
                controller.GaimonReveal.LastAuthorizedReveal != null)
            {
                var report=controller.GaimonReveal.LastAuthorizedReveal;
                GUILayout.Label("가이몬 R 보물 공개 · "+report.OriginalRound+
                    "라운드에 "+report.RevealedCount+
                    "개 표시 (미수집 · 미지급)",
                    OrdrMobileUiTheme.Explanation,GUILayout.MinHeight(64f));
            }
            GUILayout.EndVertical();
            if(lastTreasureResult!=null)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(lastTreasureRound+"라운드 탐색 결과",
                    OrdrMobileUiTheme.Subheading);
                GUILayout.Label(lastTreasureResult.Message,
                    OrdrMobileUiTheme.Explanation,GUILayout.MinHeight(60f));
                if(lastTreasureResult.Collected>0)
                    GUILayout.Label("획득 "+lastTreasureResult.RewardText(),
                        OrdrMobileUiTheme.Explanation,GUILayout.MinHeight(60f));
                GUILayout.EndVertical();
            }
        }

        private void DrawWisp()
        {
            if (controller.StartingEvolutionActorId.HasValue &&
                controller.SharedEconomy != null)
            {
                long id = controller.StartingEvolutionActorId.Value;
                GUILayout.BeginHorizontal();
                GUILayout.Label("시작 진화체 (비전투) 1개", GUILayout.ExpandWidth(true));
                if (GUILayout.Button("선택", GUILayout.Width(76), GUILayout.MinHeight(56)))
                {
                    controller.SharedEconomy.Units.Select(id);
                    notice = "진화체를 선택했습니다. 진화 기능은 원본 연결 대기 중입니다.";
                }
                GUILayout.EndHorizontal();
            }
            if (controller.PracticeBattle != null)
                GUILayout.Label("라운드마다 랜덤위습 2개 · 소환 / 조합 / 판매");
            GUILayout.Label("랜덤위습 " + controller.WispBalance(WispResourceIds.Random) +
                "개 · 선택위습 " + controller.WispBalance(WispResourceIds.Choice) + "개");
            GUILayout.Label("자동: 안흔함 " + controller.WispBalance(WispResourceIds.Uncommon) +
                " / 특별 " + controller.WispBalance(WispResourceIds.Special) +
                " / 희귀 " + controller.WispBalance(WispResourceIds.Rare) +
                " / 전설 " + controller.WispBalance(WispResourceIds.Legendary));
            if (controller.Wisps == null)
                GUILayout.Label("자동 위습 보상 연결 대기: 보유분 유지");
            else
            {
                GUILayout.Label("안흔함 13종 / 특별함 33종 · 원본 확률 자동 지급");
                if ((controller.WispBalance(WispResourceIds.Uncommon) > 0 ||
                     controller.WispBalance(WispResourceIds.Special) > 0) &&
                    GUILayout.Button("대기 중 위습 지급 처리", GUILayout.MinHeight(56)))
                {
                    try { notice = controller.Wisps.FlushAutomatic() + "개 지급 완료"; }
                    catch (Exception error) { notice = "보유분 유지: " + error.Message; }
                }
            }

            int? cost = controller.VerifiedCommonWispRecruitmentCost;
            if (!cost.HasValue || cost.Value <= 0)
                GUILayout.Label("흔함 소환: 원본 추첨·비용 연결 대기");
            else
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("흔함 소환 · 1회당 위습 " + cost.Value + "개",
                    OrdrMobileUiTheme.SectionTitle);
                bool wasEnabled = GUI.enabled;
                GUI.enabled = wasEnabled && controller.CanRecruitVerifiedCommonWisp;
                if (GUILayout.Button("1회 소환",
                    OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(64)))
                {
                    RunWispDirect("흔함 소환", () => controller.RequestCommonWispBatch(1));
                }
                GUI.enabled = wasEnabled;
                if (!string.IsNullOrEmpty(controller.VerifiedCommonWispUnavailableReason))
                    GUILayout.Label(controller.VerifiedCommonWispUnavailableReason);
                GUILayout.EndVertical();
            }

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

            DrawWispResourceShortcuts();
            DrawEconomy(ManualEconomyCategory.Wisp);
            if (wispActions.Recent.Count > 0)
            {
                if (GUILayout.Button(showWispHistory ?
                    "위습 사용 기록 닫기 ▲" : "최근 위습 사용 기록 ▼", GUILayout.MinHeight(66)))
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

        private void DrawWispResourceShortcuts()
        {
            bool canExchange = false, canRecharge = false;
            foreach (var offer in economyOffers)
            {
                if (offer.Id == OriginalRandomWispResourceExchange.OfferId)
                    canExchange = true;
                if (offer.Id == OriginalHelperCowRules.OfferId)
                    canRecharge = true;
            }
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("랜덤위습 자원 추첨", OrdrMobileUiTheme.SectionTitle);
            GUILayout.Label("목재 67% / 라운드 × 75골드 33% · 1회에 위습 1개");
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && canExchange &&
                Combat != null && Combat.Game.CurrentWave > 0 &&
                controller.WispBalance(WispResourceIds.Random) > 0;
            GUILayout.BeginHorizontal();
            foreach (int count in new[] { 1, 5, 10 })
            {
                if (GUILayout.Button(count + "회 사용", GUILayout.MinHeight(60)))
                {
                    int requested = count;
                    RunWispDirect("자원 추첨 " + count + "회", () =>
                        MobileManualBatchActions.RepeatExplicitResourceUse(requested,
                            () => controller.ManualActions.RequestEconomyAction(
                                ManualEconomyCategory.Wisp,
                                OriginalRandomWispResourceExchange.OfferId)));
                }
            }
            GUILayout.EndHorizontal();
            GUI.enabled = wasEnabled;
            if (!canExchange) GUILayout.Label("원본 자원 추첨 연결 대기");
            else if (Combat == null || Combat.Game.CurrentWave == 0)
                GUILayout.Label("라운드 시작 후 사용할 수 있습니다.");

            if (canRecharge)
            {
                GUILayout.Label("도움소 마나 · 위습 1개");
                GUI.enabled = wasEnabled &&
                    controller.WispBalance(WispResourceIds.Random) > 0 &&
                    controller.HelperCowRecharge != null &&
                    controller.HelperCowRecharge.CanRecharge;
                if (GUILayout.Button("도움소 마나 즉시 충전", GUILayout.MinHeight(60)))
                    RunWispDirect("도움소 마나 충전", () =>
                        controller.ManualActions.RequestEconomyAction(
                            ManualEconomyCategory.Wisp, OriginalHelperCowRules.OfferId));
                GUI.enabled = wasEnabled;
            }
            GUILayout.EndVertical();
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
                    GUILayout.Label("마나 포션: " + helper.ManaPotionChargesRemaining +
                        "회 남음 · 성공적으로 시전한 뒤 마나 300 회복");
                    bool wasEnabled = GUI.enabled;
                    GUI.enabled = wasEnabled && controller.Original323ManaPotion != null &&
                        controller.Original323ManaPotion.CanAttempt;
                    if (GUILayout.Button("마나 포션 사용", GUILayout.MinHeight(64)))
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
            if (GUILayout.Button("랜덤위습 1개로 마나 충전",
                GUILayout.MinHeight(64)))
                RunWispDirect("도움소 마나 충전", () =>
                    controller.ManualActions.RequestEconomyAction(
                        ManualEconomyCategory.Wisp, OriginalHelperCowRules.OfferId));
            GUI.enabled = previouslyEnabled;
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
                "  " + selectedTitle + "  (보유 " + count + "기)", GUILayout.MinHeight(60)))
                alchemyTargetsExpanded = !alchemyTargetsExpanded;

            if (alchemyTargetsExpanded)
            {
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
                if (GUILayout.Button("연금술 실행 선택", GUILayout.MinHeight(60)))
                    pendingAlchemyCastId = selected.InstanceId;
            }
            else
            {
                GUILayout.Label("선택한 유닛이 영구 소비됩니다. 원본 조건과 재료는 확정 시 재검증됩니다.");
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
                GUILayout.Label("원작 경제 서비스 미연결");
            if (GUILayout.Button("목록 새로고침", GUILayout.MinHeight(56)))
                RefreshCurrent();
            if (category == ManualEconomyCategory.Gambling && controller.PracticeMoneyGambling != null)
            {
                var shop = controller.PracticeMoneyGambling;
                GUILayout.Label("일반 돈 도박 재고 " + shop.Stock + "/3" +
                    (shop.Stock < SourceModelRegularMoneyGambling.MaximumStock ?
                        " · 다음 충전 " + Math.Ceiling(shop.SecondsUntilRestock) + "초" : "") +
                    " · 사용 " + shop.PurchaseCount + "회 / 반환 " + shop.GoldReturned + "골드");
                GUILayout.Label("현재 연습에서는 일반 돈 도박만 실행할 수 있습니다.");
            }
            if (economyOffers.Count == 0)
                GUILayout.Label("현재 이 메뉴에서 실행할 수 있는 상품이 없습니다.");
            foreach (var offer in economyOffers)
            {
                // Already available as immediate 1/5/10-use shortcuts.
                if (category == ManualEconomyCategory.Wisp &&
                    (offer.Id == OriginalRandomWispResourceExchange.OfferId ||
                     offer.Id == OriginalHelperCowRules.OfferId))
                    continue;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(offer.Title);
                GUILayout.Label(offer.OriginalPriceAndRules);
                if (category == ManualEconomyCategory.Wisp)
                {
                    if (GUILayout.Button("바로 사용", GUILayout.MinHeight(64)))
                    {
                        string id = offer.Id;
                        RunWispDirect(offer.Title, () =>
                            controller.ManualActions.RequestEconomyAction(category, id));
                    }
                }
                else if (GUILayout.Button("선택", GUILayout.MinHeight(64)))
                {
                    pendingEconomy = offer;
                    notice = "구매 대기";
                }
                GUILayout.EndVertical();
            }
            if (category != ManualEconomyCategory.Wisp && pendingEconomy != null)
            {
                GUILayout.Label("확인: " + pendingEconomy.Title);
                GUILayout.Label(pendingEconomy.OriginalPriceAndRules);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("직접 실행", GUILayout.MinHeight(66)))
                {
                    ShowResult(controller.ManualActions.RequestEconomyAction(
                        category, pendingEconomy.Id));
                    pendingEconomy = null;
                    RefreshCurrent();
                }
                if (GUILayout.Button("취소", GUILayout.MinHeight(66)))
                    pendingEconomy = null;
                GUILayout.EndHorizontal();
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

