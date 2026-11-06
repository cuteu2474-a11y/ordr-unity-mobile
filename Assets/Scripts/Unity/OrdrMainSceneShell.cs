using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Unity
{
    [DisallowMultipleComponent]
    public sealed class OrdrMainSceneShell : MonoBehaviour
    {
        private OrdrCharacterCombatController controller;
        private string startFailure;
        private bool gameScreenEntered;
        private bool confirmNewGame;
        private OrdrStoryBattlefieldView storyView;

        public bool IsStartScreenVisible
        {
            get
            {
                // Explicit Editor/test sessions can enter independently.
                return !gameScreenEntered && (controller == null || controller.Combat == null ||
                    controller.Combat.Game.Phase == SessionPhase.NotStarted);
            }
        }

        private void Awake()
        {
            controller = GetComponent<OrdrCharacterCombatController>();
            if (controller == null)
                Debug.LogError("ORDR 시작 화면에 전투/도감 컨트롤러가 없습니다.");
        }

        public void EnterGame()
        {
            // Repeated taps never recreate a session or grant another start.
            if (!IsStartScreenVisible) return;
            if (controller == null || controller.Units == null)
                throw new InvalidOperationException("캐릭터 데이터 로딩 중");
            string reason;
            bool verified = controller.CanBeginVerifiedFirstWave(out reason);
            if (verified) controller.BeginVerifiedFirstWave();
            else controller.BeginUnitPreparation();
            gameScreenEntered = true;
            startFailure = null;
            if (!verified) GetComponent<OrdrMobileTouchPanel>()?.OpenRecruitment();
        }

        private void OnEnable()
        {
            if (controller != null && storyView == null)
                storyView = new OrdrStoryBattlefieldView(controller);
        }

        private void OnDisable()
        {
            storyView?.Dispose();
            storyView = null;
        }

        private void Update()
        {
            storyView?.Tick();
        }

        private void OnGUI()
        {
            Rect safe = Screen.safeArea;
            if (safe.width < 180f || safe.height < 160f || safe.width <= safe.height) return;
            float scale = MobileMapSkillLayout.UiScale(safe, Screen.dpi);
            GUISkin previous = OrdrMobileUiTheme.Activate();
            try
            {
                if (IsStartScreenVisible)
                {
                    DrawStartScreen(safe, scale);
                    return;
                }
                var layout = MobileMapSkillLayout.Calculate(safe, Screen.height, scale);
                OrdrMobileUiTheme.DrawMapCorners(layout.Battle, new Color(.28f, .43f, .46f, .65f));
                var combat = controller == null ? null : controller.Combat;
                Rect hud = new Rect(layout.Battle.x + 5f * scale,
                    layout.Battle.y + 4f * scale,
                    layout.Battle.width - 10f * scale, 30f * scale);
                GUI.DrawTexture(hud, OrdrBattlefieldArt.HeaderTexture,
                    ScaleMode.StretchToFill);
                var hudStyle = new GUIStyle(OrdrMobileUiTheme.Overline) {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.RoundToInt(16f * scale),
                    fontStyle = FontStyle.Bold, wordWrap = false };
                hudStyle.normal.textColor = Color.white;
                int count = GetComponent<OrdrUnitWorldPresenter>()?.MainOwnedUnitCount ?? 0;
                string metrics = combat == null ? "메인 전장" :
                    "메인 전장   " + combat.Game.CurrentWave + "R   ·   적 " +
                    combat.Game.AliveEnemies + "   ·   유닛 " + count;
                GUI.Label(hud, metrics, hudStyle);

                // Story remains a smaller, visually separate battlefield.
                Color groundColor = GUI.color;
                try
                {
                    GUI.color = new Color(.94f, .88f, .76f, 1f);
                    GUI.DrawTexture(layout.Story, OrdrBattlefieldArt.StoryTexture,
                        ScaleMode.ScaleAndCrop);
                }
                finally { GUI.color = groundColor; }
                OrdrMobileUiTheme.DrawMapCorners(layout.Story, new Color(.38f, .46f, .43f, .6f));
                if (controller != null &&
                    Time.unscaledTime - controller.EvolutionNoticeShownAt < 2.3f &&
                    !string.IsNullOrEmpty(controller.EvolutionNotice))
                {
                    var noticeBox = new Rect(layout.Battle.center.x - layout.Battle.width * .34f,
                        layout.Battle.y + 42f * scale, layout.Battle.width * .68f,
                        35f * scale);
                    GUI.DrawTexture(noticeBox, OrdrBattlefieldArt.HeaderTexture,
                        ScaleMode.StretchToFill);
                    var noticeStyle = new GUIStyle(hudStyle) {
                        fontSize = Mathf.RoundToInt(17f * scale) };
                    noticeStyle.normal.textColor = OrdrMobileUiTheme.Gold;
                    GUI.Label(noticeBox, controller.EvolutionNotice, noticeStyle);
                }

                storyView?.Draw(layout.Story, scale);
                if (combat != null && (combat.Game.Phase == SessionPhase.Won ||
                    combat.Game.Phase == SessionPhase.Lost))
                {
                    float width = Mathf.Min(300f * scale, layout.Battle.width - 20f);
                    var panel = new Rect(layout.Battle.center.x - width * .5f,
                        layout.Battle.center.y - 62f * scale, width, 124f * scale);
                    GUI.Box(panel, GUIContent.none);
                    var resultStyle = new GUIStyle(GUI.skin.label) {
                        fontSize = Mathf.RoundToInt(17f * scale), alignment = TextAnchor.MiddleCenter };
                    string result = controller.SoloStory != null && controller.SoloStory.LostByStory
                            ? controller.SoloStory.LastNotice :
                        controller.PracticeBattle == null ? combat.Game.Phase.ToString() :
                            controller.PracticeBattle.ResultMessage;
                    GUI.Label(new Rect(panel.x + 8f, panel.y + 8f, panel.width - 16f, 46f * scale),
                        result + "\n누적 클리어 " + controller.AccumulatedClearCount + "회", resultStyle);
                    var restart = new Rect(panel.x + 8f, panel.yMax - 56f * scale,
                        panel.width - 16f, 48f * scale);
                    if (GUI.Button(restart, confirmNewGame ? "확인 · 시작화면으로" : "새 게임"))
                    {
                        if (confirmNewGame) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                        else confirmNewGame = true;
                    }
                }
            }
            finally { GUI.skin = previous; }
        }

        private void DrawStartScreen(Rect safe, float scale)
        {
            Color old = GUI.color;
            GUI.color = new Color(.025f, .045f, .085f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
            var area = new Rect(safe.x, Screen.height - safe.yMax, safe.width, safe.height);
            float width = Mathf.Min(500f * scale, area.width - 48f * scale);
            var panel = new Rect(area.center.x - width * .5f,
                area.center.y - 130f * scale, width, 260f * scale);
            OrdrMobileUiTheme.DrawMapCorners(panel, OrdrMobileUiTheme.Gold);
            var title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(46f * scale),
                fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = OrdrMobileUiTheme.Gold;
            GUI.Label(new Rect(panel.x, panel.y + 20f * scale, width, 70f * scale), "원랜디", title);
            var subtitle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(16f * scale),
                alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(panel.x, panel.y + 92f * scale, width, 28f * scale),
                "악몽 · 이세계", subtitle);
            GUI.Label(new Rect(panel.x, panel.y + 120f * scale, width, 22f * scale),
                "누적 클리어 " + (controller == null ? 0 : controller.AccumulatedClearCount) +
                "회", subtitle);
            bool previous = GUI.enabled;
            GUI.enabled = previous && controller != null && controller.Units != null;
            if (GUI.Button(new Rect(panel.x + 40f * scale, panel.y + 142f * scale,
                width - 80f * scale, 64f * scale),
                GUI.enabled ? "게임 시작" : "데이터 로딩 중", OrdrMobileUiTheme.PrimaryButton))
            {
                try { EnterGame(); }
                catch (Exception ex)
                {
                    startFailure = ex.Message;
                    controller?.RecordSessionIssue("게임 시작 실패", ex.Message);
                    Debug.LogException(ex);
                }
            }
            GUI.enabled = previous;
            GUI.Label(new Rect(panel.x + 8f, panel.y + 214f * scale,
                width - 16f, 38f * scale), string.IsNullOrEmpty(startFailure) ?
                "원본 기반 65R 전투 (최종 수치 검증 중)" : startFailure, subtitle);
        }
    }
}
