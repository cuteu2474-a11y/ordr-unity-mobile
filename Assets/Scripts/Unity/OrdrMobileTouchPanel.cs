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
        private enum Tab { Play, Units, Story, Fusion, Shop, Gambling, Wisp, Helper, Alchemy, Missions }

        [SerializeField] private OrdrCharacterCombatController controller;
        [SerializeField] private bool openByDefault;
        private bool visible;
        private Tab currentTab = Tab.Play;
        private float nextFusionRefresh;
        private string fusionInventorySignature;
        private int selectedPracticeRounds = 20;
        private bool browsingFullCatalog;
        private int catalogGradeIndex;
        private int catalogPage;
        private int ownedPage;
        private long? pendingCommonSaleId;
        private string pendingCommonSaleName;
        private int pendingMissionReroll = -1;
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
        private string pendingChoiceRawcode;
        private long? pendingDecompositionId;
        private string pendingDecompositionName;
        private bool pendingCommonWispRecruitment;
        private int commonWispBatchCount = 1;
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

        // Navigation only. Opening a drawer never spends or summons anything.
        public void OpenRecruitment()
        {
            if (visible && currentTab == Tab.Wisp) return;
            SelectTab(Tab.Wisp);
        }

        private void Update()
        {
            if (!visible || Combat == null ||
                (currentTab != Tab.Play && currentTab != Tab.Fusion) ||
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
            pendingCommonWispRecruitment = false;
            pendingHelperRecharge = false;
            pendingMaximumOutput = false;
            pendingAlchemy = false;
            alchemyTargetsExpanded = false;
            selectedAlchemyTargetId = null;
            pendingAlchemyCastId = null;
            alchemyTargetsScroll = Vector2.zero;
            pendingCommonSaleId = null;
            pendingCommonSaleName = null;
            notice = "";
            pendingMissionReroll = -1;
            RefreshCurrent();
        }

        private void CloseDrawer()
        {
            visible = false;
            pendingFusion = null;
            pendingEconomy = null;
            pendingChoiceRawcode = null;
            pendingCommonWispRecruitment = false;
            pendingHelperRecharge = false;
            pendingMaximumOutput = false;
            pendingAlchemy = false;
            alchemyTargetsExpanded = false;
            selectedAlchemyTargetId = null;
            pendingAlchemyCastId = null;
            pendingCommonSaleId = null;
            pendingCommonSaleName = null;
            pendingMissionReroll = -1;
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
            GUISkin previousSkin = OrdrMobileUiTheme.Activate();
            try
            {
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                DrawLeftTabs(layout, scale);
                if (visible)
                    DrawRightDrawer(layout, scale);

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
                    "    ·    목재 " + economy.Wallet.Balance("wood") +
                    "    ·    위습 " + economy.Wallet.Balance(WispResourceIds.Random),
                    OrdrMobileUiTheme.CurrencyText);
                if (area.height >= 45f)
                {
                    var game = Combat == null ? null : Combat.Game;
                    GUILayout.Label(game == null
                        ? "진행 라운드: 대기"
                        : (controller.IsUnitPreparation ? "유닛 준비" : "라운드 " + game.CurrentWave) +
                          "   진행 상태 " + game.Phase +
                          "   필드 적 " + game.AliveEnemies);
                }
                if (area.height >= 70f && controller.VerifiedWaveDirector != null)
                {
                    var wave = controller.VerifiedWaveDirector.CurrentProgress;
                    if (wave != null)
                        GUILayout.Label("현재 라운드 출현 " + wave.Spawned + "/" + wave.Planned +
                            " · 처치 " + wave.Defeated + " · 이탈 " + wave.Escaped);
                }
            }
            GUILayout.EndArea();
        }

        private void DrawLeftTabs(MobileMapSkillLayout layout, float scale)
        {
            Rect area = MobileMapSkillLayout.Scaled(layout.LeftTabs, scale);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("ORDR", OrdrMobileUiTheme.SectionTitle,
                GUILayout.Height(30f));
            // Use touch/trackpad scrolling without the large default Unity
            // scrollbar stealing width from Korean navigation labels.
            tabsScroll = GUILayout.BeginScrollView(tabsScroll, false, false,
                GUIStyle.none, GUIStyle.none, GUILayout.ExpandHeight(true));

            // Order matches the most common manual activities.
            DrawLeftTab(Tab.Play, "플레이");
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
            DrawLeftTab(Tab.Missions, "개인 미션");

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawLeftTab(Tab tab, string title)
        {
            bool chosen = visible && currentTab == tab;
            GUIStyle style = chosen ? OrdrMobileUiTheme.SelectedTab : GUI.skin.button;
            if (GUILayout.Button(title, style, GUILayout.MinHeight(46f),
                GUILayout.ExpandWidth(true)))
                SelectTab(tab);
        }

        private void DrawRightDrawer(MobileMapSkillLayout layout, float scale)
        {
            Rect area = MobileMapSkillLayout.Scaled(layout.RightDrawer, scale);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label(TabTitle(currentTab), OrdrMobileUiTheme.SectionTitle,
                GUILayout.ExpandWidth(true));
            if (controller.Diagnostics.Count > 0 &&
                GUILayout.Button("기록 복사", GUILayout.Width(96f), GUILayout.MinHeight(40f)))
            {
                GUIUtility.systemCopyBuffer = controller.ExportSessionDiagnostics();
                notice = "진행 기록을 클립보드에 복사했습니다.";
            }
            if (controller.Combat == null &&
                GUILayout.Button("진단 복사", GUILayout.Width(96f), GUILayout.MinHeight(40f)))
            {
                GUIUtility.systemCopyBuffer = controller.ExportFirstWaveReadiness();
                notice = "실전 시작 차단 요인을 클립보드에 복사했습니다.";
            }
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
                    case Tab.Play: DrawPlay(); break;
                    case Tab.Units: DrawUnits(); break;
                    case Tab.Story: DrawStory(); break;
                    case Tab.Fusion: DrawFusion(); break;
                    case Tab.Shop: DrawEconomy(ManualEconomyCategory.Shop); break;
                    case Tab.Gambling: DrawEconomy(ManualEconomyCategory.Gambling); break;
                    case Tab.Wisp: DrawWisp(); break;
                    case Tab.Helper: DrawHelperCow(); break;
                    case Tab.Alchemy: DrawAlchemyPanel(); break;
                    case Tab.Missions: DrawPersonalMissions(); break;
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
                case Tab.Play: return "플레이";
                case Tab.Wisp: return "위습";
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
                GUILayout.Label("유닛을 준비한 뒤 연습 전투를 시작하세요.");
                return;
            }
            var game = Combat.Game;
            var practice = controller.PracticeBattle;
            bool playing = game.Phase == SessionPhase.Running;
            GUILayout.Label(controller.IsUnitPreparation ? "1. 위습 소환 → 2. 조합 → 3. 전투 시작" :
                practice == null ? "게임을 시작하면 진행 현황을 확인할 수 있습니다." :
                playing ? "전투 중에도 직접 소환·조합·판매할 수 있습니다." : practice.ResultMessage);
            GUILayout.Label("보유 전투 유닛 " + Combat.CharacterCount + " · 자동 공격 " + Combat.ReadyAttackers);
            if (playing)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("위습 소환으로", GUILayout.MinHeight(46))) OpenRecruitment();
                if (GUILayout.Button("보유 유닛 / 판매", GUILayout.MinHeight(46))) SelectTab(Tab.Units);
                GUILayout.EndHorizontal();
            }
            if (controller.IsUnitPreparation)
            {
                bool previous = GUI.enabled;
                GUI.enabled = previous && Combat.CharacterCount > 0;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(selectedPracticeRounds == 10 ? "10R ✓" : "10R", GUILayout.MinHeight(42))) selectedPracticeRounds = 10;
                if (GUILayout.Button(selectedPracticeRounds == 20 ? "20R ✓" : "20R", GUILayout.MinHeight(42))) selectedPracticeRounds = 20;
                GUILayout.EndHorizontal();
                if (GUILayout.Button("준비 완료 · " + selectedPracticeRounds + "R 연습 시작",
                    OrdrMobileUiTheme.PrimaryButton, GUILayout.MinHeight(48)))
                {
                    try { controller.BeginPracticeBattle(selectedPracticeRounds); notice = "전투 시작 · 소환과 조합은 계속할 수 있습니다."; }
                    catch (Exception error) { notice = error.Message; }
                }
                GUI.enabled = previous;
                if (Combat.CharacterCount == 0) GUILayout.Label("먼저 위습 메뉴에서 유닛을 소환하세요.");
            }
            if (practice != null)
            {
                GUILayout.Label("처치 " + game.DefeatedEnemies + " · 남은 적 " + game.AliveEnemies +
                    "/" + practice.EnemyLimit + " · 전투 시간 " + Math.Floor(Combat.Time) + "초");
                GUILayout.Label("처치 골드 +" + practice.GoldAwarded +
                    " · 라운드 위습 +" + practice.RoundWispsAwarded);
                foreach (var enemy in Combat.MobileEnemySnapshot())
                    if (enemy.Kind == CharacterEnemyKind.Boss)
                        GUILayout.Label("보스 HP " + enemy.Health + " / " + enemy.MaxHealth);
                if (Combat.PracticeUnitEffects != null && Combat.PracticeUnitEffects.DeferredAuraGroups > 0)
                    GUILayout.Label("오라 중첩 확인 대기 " + Combat.PracticeUnitEffects.DeferredAuraGroups + "건");
                if (playing && game.AliveEnemies >= practice.EnemyLimit * 0.8)
                    GUILayout.Label("적이 누적되고 있습니다. 소환과 조합으로 전력을 보강하세요.");
            }
            if (playing)
            {
                GUILayout.Label("지금 만들 수 있는 조합 (직접 선택 후 확정)");
                int ready = 0;
                foreach (var offer in fusionOffers)
                {
                    if (!offer.IsReady || !controller.ManualActions.CanExecuteFusionRecipe(offer.RecipeId)) continue;
                    ready++;
                    if (ready > 8) continue;
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label(offer.Title);
                    GUILayout.Label(offer.OriginalMaterialsAndCost);
                    if (GUILayout.Button("이 조합 선택", GUILayout.MinHeight(44)))
                    {
                        SelectTab(Tab.Fusion);
                        fusionExecutableOnly = true;
                        fusionSearch = offer.RecipeId;
                        fusionPage = 0;
                        pendingFusion = offer;
                        notice = "선택한 재료를 확인하고 조합 확정을 눌러 주세요.";
                    }
                    GUILayout.EndVertical();
                }
                if (ready == 0) GUILayout.Label("현재 제작 가능한 조합이 없습니다. 조합 메뉴에서 부족한 재료를 확인하세요.");
                if (ready > 8) GUILayout.Label("추가로 가능한 조합 " + (ready - 8) + "개");
                if (GUILayout.Button("전체 제작 조합 보기", GUILayout.MinHeight(44)))
                {
                    SelectTab(Tab.Fusion);
                    fusionExecutableOnly = true;
                }
            }
            if (practice != null && practice.RoundHistory.Count > 0)
            {
                GUILayout.Label("라운드 기록 · 해당 구간에 실제 발생한 처치/보상");
                foreach (var round in practice.RoundHistory)
                    GUILayout.Label(round.Wave + "R · " + Math.Round(round.ElapsedSeconds, 1) + "초 · 처치 " +
                        round.Defeated + " · 골드 +" + round.GoldAwarded + " · 남은 적 " + round.AliveEnemies);
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
                    bool confirm = GUILayout.Button("미션 교체 확정", GUILayout.MinHeight(44f));
                    GUI.enabled = oldEnabled;
                    if (confirm)
                    {
                        notice = gameplay.TryReroll(slot.Index) ? "미션을 교체했습니다." :
                            "현재 미션 교체 조건을 충족하지 않습니다.";
                        pendingMissionReroll = -1;
                    }
                    if (GUILayout.Button("취소", GUILayout.MinHeight(40f))) pendingMissionReroll = -1;
                }
                else if (gameplay.CanReroll(slot.Index) &&
                    GUILayout.Button("이 미션 교체", GUILayout.MinHeight(44f)))
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
                if (GUILayout.Button("미지급 보상 다시 처리", GUILayout.MinHeight(44f)))
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

        private void DrawOwnedUnitIndex()
        {
            if (Combat == null)
            {
                GUILayout.Label("월드가 시작되면 보유한 유닛이 이곳에 표시됩니다.");
                return;
            }
            var owned = Combat.MobileUnitSnapshot();
            GUILayout.Label("보유 " + owned.Count + "기 · 선택 시 상세 보기");
            if (controller.LowerGradeSales != null)
                GUILayout.Label("흔함 판매 포인트 " + controller.LowerGradeSales.CommonPoints +
                    "/3 · 3기마다 랜덤위습 1개");
            if (owned.Count > 0 && GUILayout.Button(controller.PracticeBattle == null ? "보유 유닛 전체 보기" : "전장 전체 보기", GUILayout.MinHeight(44)))
            {
                var presenter = controller.GetComponent<OrdrUnitWorldPresenter>();
                if (presenter != null) presenter.ShowAllOwnedUnits();
            }
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
                if (GUILayout.Button("보기", GUILayout.Width(64), GUILayout.MinHeight(48)))
                {
                    var presenter = controller.GetComponent<OrdrUnitWorldPresenter>();
                    if (presenter != null) presenter.ShowOwnedUnit(entry.InstanceId);
                }
                GUILayout.EndHorizontal();
                // All nine original common objects have A09G. Never display
                // A0JE/Avul as manual abilities; the transaction rechecks
                // exact original rawcode and ownership under its world lock.
                bool hasSale = controller.LowerGradeSales != null &&
                    controller.LowerGradeSales.CanSell(entry);
                if (hasSale || entry.Unit.Grade == "흔함")
                {
                    bool oldEnabled = GUI.enabled;
                    GUI.enabled = oldEnabled && (hasSale ||
                        controller.ManualActions.HasOriginalCommonSaleService);
                    if (GUILayout.Button(entry.Unit.Name + " 판매", GUILayout.MinHeight(48)))
                    {
                        pendingCommonSaleId = entry.InstanceId;
                        pendingCommonSaleName = entry.Unit.Name;
                        notice = entry.Unit.Name + "을(를) 판매하면 되돌릴 수 없습니다. 아래에서 확인하세요.";
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
                        if (GUILayout.Button("분해 시전 취소", GUILayout.MinHeight(48)))
                            notice = service.Cancel(entry.InstanceId).Message;
                    }
                    else
                    {
                        GUILayout.Label(Original323UnitDecompositionService.Description(entry.Unit.Id));
                        if (GUILayout.Button(entry.Unit.Name + " 분해", GUILayout.MinHeight(48)))
                        {
                            pendingDecompositionId = entry.InstanceId;
                            pendingDecompositionName = entry.Unit.Name;
                        }
                    }
                }
            }
            if (pendingCommonSaleId.HasValue)
            {
                GUILayout.Label((pendingCommonSaleName ?? "흔함 유닛") + " #" +
                    pendingCommonSaleId.Value + " 판매 확정?");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("판매 확인", GUILayout.MinHeight(48)))
                {
                    ShowResult(controller.RequestLowerGradeSale(pendingCommonSaleId.Value));
                    pendingCommonSaleId = null;
                    pendingCommonSaleName = null;
                }
                if (GUILayout.Button("취소", GUILayout.MinHeight(48)))
                {
                    pendingCommonSaleId = null;
                    pendingCommonSaleName = null;
                }
                GUILayout.EndHorizontal();
            }
            if (pendingDecompositionId.HasValue)
            {
                GUILayout.Label(pendingDecompositionName + " 분해를 시작할까요? 완료하면 원래 유닛이 사라집니다.");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("분해 시작", GUILayout.MinHeight(48)))
                {
                    MobileUnitSnapshot selected = null;
                    foreach (var row in Combat.MobileUnitSnapshot())
                        if (row.InstanceId == pendingDecompositionId.Value) selected = row;
                    notice = controller.ManualActions.RequestUnitDecomposition(selected).Message;
                    pendingDecompositionId = null;
                }
                if (GUILayout.Button("취소", GUILayout.MinHeight(48))) pendingDecompositionId = null;
                GUILayout.EndHorizontal();
            }
            if (!string.IsNullOrEmpty(controller.UnitDecompositions?.LastMessage))
                GUILayout.Label(controller.UnitDecompositions.LastMessage);
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
            GUILayout.Label("원본 조합식 검색 · 재료 보유 상태 확인 (제작은 직접 확정)");
            DrawChoiceWispMerge();
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("캐릭터·재료 이름으로 검색");
            string query = GUILayout.TextField(fusionSearch, GUILayout.MinHeight(44));
            if (!string.Equals(query, fusionSearch, StringComparison.Ordinal))
            {
                fusionSearch = query;
                fusionPage = 0;
                originalJassPage = 0;
                scroll = Vector2.zero;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(fusionMaterialsReadyOnly ?
                "재료 확보만 ✓" : "모든 조합", GUILayout.MinHeight(46)))
            {
                fusionMaterialsReadyOnly = !fusionMaterialsReadyOnly;
                fusionPage = 0;
                scroll = Vector2.zero;
            }
            if (GUILayout.Button("검색 초기화", GUILayout.MinHeight(46)))
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
                GUILayout.MinHeight(44));
            if (onlyExecutable != fusionExecutableOnly)
            {
                fusionExecutableOnly = onlyExecutable;
                fusionPage = 0;
            }
            DrawOriginalJassAudit();

            if (!controller.ManualActions.HasFusionService)
                GUILayout.Label("원작 조합식·재료·비용 시스템 미연결");
            else if (!controller.ManualActions.HasExecutableFusionService)
                GUILayout.Label("원작 제작 트리거 미연결: 미리보기 전용, 재료 소비 불가");
            if (GUILayout.Button("보유 재료 새로고침", GUILayout.MinHeight(48)))
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
            var visibleOffers = MobileFusionBrowser.Select(browsedOffers,
                fusionSearch, fusionMaterialsReadyOnly, fusionPage,
                FusionPageSize, out matches, out pages, out resolved);
            fusionPage = resolved;
            GUILayout.Label("조합식 " + matches + " / " + fusionOffers.Count +
                "건 · " + (resolved + 1) + "/" + pages + "쪽");
            if (matches == 0)
                GUILayout.Label("검색 결과가 없습니다. 이름이나 재료명을 바꿔 보세요.");
            foreach (var offer in visibleOffers)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(offer.Title);
                GUILayout.Label(offer.OriginalMaterialsAndCost);
                if (offer.MissingMaterials.Count != 0)
                    GUILayout.Label("미확보: " + string.Join(", ",
                        ToStringArray(offer.MissingMaterials)));
                else
                    GUILayout.Label("유닛 재료 확보 (추가 조건·제작 허가는 별도 확인)");
                bool previousEnabled = GUI.enabled;
                bool executable = controller.ManualActions.CanExecuteFusionRecipe(
                    offer.RecipeId);
                GUI.enabled = previousEnabled && offer.IsReady && executable;
                if (GUILayout.Button(!offer.IsReady ? "재료 부족" :
                    executable ? "조합 선택" : "제작 미연결",
                    GUILayout.MinHeight(48)))
                {
                    pendingFusion = offer;
                    pendingEconomy = null;
                    notice = "재료는 아직 소비되지 않았습니다. 확정 전에 조건을 재검증합니다.";
                }
                GUI.enabled = previousEnabled;
                if (controller.OriginalJassFusions != null &&
                    controller.OriginalFusionObjectPreview != null &&
                    controller.OriginalJassFusions.FindByAbility(offer.RecipeId) != null &&
                    GUILayout.Button(routeId == offer.RecipeId ? "경로 닫기" :
                        "하위 조합 경로", GUILayout.MinHeight(44)))
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
                if (GUILayout.Button("이전", GUILayout.MinHeight(46)))
                {
                    fusionPage--;
                    scroll = Vector2.zero;
                }
                GUI.enabled = previous && resolved + 1 < pages;
                if (GUILayout.Button("다음", GUILayout.MinHeight(46)))
                {
                    fusionPage++;
                    scroll = Vector2.zero;
                }
                GUI.enabled = previous;
                GUILayout.EndHorizontal();
            }
            // Keep confirmation accessible when a search/page changes;
            // the domain port always revalidates live inventory on commit.
            if (pendingFusion != null)
            {
                GUILayout.Label("선택됨: " + pendingFusion.Title);
                GUILayout.Label(pendingFusion.OriginalMaterialsAndCost);
                GUILayout.BeginHorizontal();
                bool priorEnabled = GUI.enabled;
                GUI.enabled = priorEnabled &&
                    controller.ManualActions.CanExecuteFusionRecipe(
                        pendingFusion.RecipeId);
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
            if (controller.StartingEvolutionActorId.HasValue &&
                controller.SharedEconomy != null)
            {
                long id = controller.StartingEvolutionActorId.Value;
                GUILayout.BeginHorizontal();
                GUILayout.Label("시작 진화체 (비전투) 1개", GUILayout.ExpandWidth(true));
                if (GUILayout.Button("선택", GUILayout.Width(76),
                    GUILayout.MinHeight(44)))
                {
                    controller.SharedEconomy.Units.Select(id);
                    notice = "진화체를 선택했습니다. 진화 기능은 원본 연결 대기 중입니다.";
                }
                GUILayout.EndHorizontal();
            }
            if (controller.PracticeBattle != null)
                GUILayout.Label("라운드마다 랜덤위습 2개 · 직접 소환 / 조합 / 판매");
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
                GUILayout.Label("안흔함 위습 13종 / 특별함 위습 33종 중 원본 확률로 자동 지급됩니다.");
                if ((controller.WispBalance(WispResourceIds.Uncommon) > 0 ||
                     controller.WispBalance(WispResourceIds.Special) > 0) &&
                    GUILayout.Button("대기 중인 안흔함·특별함 위습 처리", GUILayout.MinHeight(44)))
                {
                    try { notice = controller.Wisps.FlushAutomatic() + "개 지급 완료"; }
                    catch (Exception error) { notice = "보유분 유지: " + error.Message; }
                }
            }
            int? cost = controller.VerifiedCommonWispRecruitmentCost;
            if (!cost.HasValue)
                GUILayout.Label("흔함 소환: 원본 추첨·비용 검증 월드 연결 대기");
            else
            {
                GUILayout.Label("흔함 유닛 소환 · 1회당 랜덤위습 " + cost.Value + "개");
                if (!pendingCommonWispRecruitment)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("1회", GUILayout.MinHeight(44))) commonWispBatchCount = 1;
                    if (GUILayout.Button("최대 5회", GUILayout.MinHeight(44))) commonWispBatchCount = 5;
                    if (GUILayout.Button("보유분", GUILayout.MinHeight(44)))
                        commonWispBatchCount = (int)Math.Max(1, Math.Min(100,
                            controller.WispBalance(WispResourceIds.Random) / cost.Value));
                    GUILayout.EndHorizontal();
                }
                GUILayout.Label("이번 소환: 최대 " + commonWispBatchCount + "회");
                bool previouslyEnabled = GUI.enabled;
                GUI.enabled = previouslyEnabled &&
                    controller.CanRecruitVerifiedCommonWisp;
                if (!pendingCommonWispRecruitment)
                {
                    if (GUILayout.Button("흔함 소환", GUILayout.MinHeight(48)))
                        pendingCommonWispRecruitment = true;
                }
                else
                {
                    GUILayout.Label("최대 " + commonWispBatchCount + "회 직접 소환합니다. 실패하면 남은 위습은 보존됩니다.");
                    if (GUILayout.Button("소환 확정", GUILayout.MinHeight(48)))
                    {
                        ShowResult(controller.RequestCommonWispBatch(commonWispBatchCount));
                        pendingCommonWispRecruitment = false;
                        RefreshCurrent();
                    }
                }
                GUI.enabled = previouslyEnabled;
                if (!string.IsNullOrEmpty(controller.VerifiedCommonWispUnavailableReason))
                    GUILayout.Label(controller.VerifiedCommonWispUnavailableReason);
                if (pendingCommonWispRecruitment &&
                    GUILayout.Button("소환 취소", GUILayout.MinHeight(42)))
                    pendingCommonWispRecruitment = false;
            }
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
                    if (GUILayout.Button("이전 등록식", GUILayout.MinHeight(44)))
                    {
                        originalJassPage--;
                        scroll = Vector2.zero;
                    }
                    GUI.enabled = previous && originalJassPage + 1 < pages;
                    if (GUILayout.Button("다음 등록식", GUILayout.MinHeight(44)))
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
            if (currentTab == Tab.Fusion || currentTab == Tab.Play)
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

