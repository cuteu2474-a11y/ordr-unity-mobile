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
                OrdrMobileUiTheme.DrawMapCorners(layout.Battle, OrdrMobileUiTheme.Gold);
                GUI.Label(new Rect(layout.Battle.x + 8f, layout.Battle.y + 4f,
                    layout.Battle.width - 16f, 22f), "메인 전장 · 보유 " +
                    (GetComponent<OrdrUnitWorldPresenter>()?.MainOwnedUnitCount ?? 0) + "기",
                    OrdrMobileUiTheme.Overline);
                GUI.Box(layout.Story, GUIContent.none, GUI.skin.box);
                OrdrMobileUiTheme.DrawMapCorners(layout.Story, OrdrMobileUiTheme.Aqua);
                GUI.Label(new Rect(layout.Story.x + 8f, layout.Story.y + 4f,
                    layout.Story.width - 16f, 22f), "스토리 전장", OrdrMobileUiTheme.Overline);

                var combat = controller == null ? null : controller.Combat;
                if (combat != null && (combat.Game.Phase == SessionPhase.Won ||
                    combat.Game.Phase == SessionPhase.Lost))
                {
                    float width = Mathf.Min(300f * scale, layout.Battle.width - 20f);
                    var panel = new Rect(layout.Battle.center.x - width * .5f,
                        layout.Battle.center.y - 62f * scale, width, 124f * scale);
                    GUI.Box(panel, GUIContent.none);
                    var resultStyle = new GUIStyle(GUI.skin.label) {
                        fontSize = Mathf.RoundToInt(17f * scale), alignment = TextAnchor.MiddleCenter };
                    GUI.Label(new Rect(panel.x + 8f, panel.y + 8f, panel.width - 16f, 46f * scale),
                        controller.PracticeBattle == null ? combat.Game.Phase.ToString() :
                            controller.PracticeBattle.ResultMessage, resultStyle);
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
                "원본 기반 전투 · 10R / 20R / 65R (최종 수치 검증 중)" : startFailure, subtitle);
        }
    }
}
