using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Unity
{
    // Game-start UI only enters a source-verified playable session. Rendering
    // placeholders never fabricate a wave, damage or starting resources.
    [DisallowMultipleComponent]
    public sealed class OrdrMainSceneShell : MonoBehaviour
    {
        private OrdrCharacterCombatController controller;
        private string startFailure;
        private bool confirmNewGame;

        private void Awake()
        {
            controller = GetComponent<OrdrCharacterCombatController>();
            if (controller == null)
                Debug.LogError("ORDR 시작 화면에 전투/도감 컨트롤러가 없습니다.");
        }

        private void OnGUI()
        {
            Rect safe = Screen.safeArea;
            if (safe.width < 180f || safe.height < 160f) return;
            if (safe.width <= safe.height)
            {
                GUI.Label(new Rect(16f, 16f, Screen.width - 32f, 60f),
                    "원랜디 모바일은 가로 화면 전용입니다. 기기를 가로로 돌려 주세요.");
                return;
            }

            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
            float scale = MobileMapSkillLayout.UiScale(safe, dpi);
            var layout = MobileMapSkillLayout.Calculate(safe, Screen.height, scale);
            float top = Screen.height - safe.yMax;
            float left = layout.Battle.x;
            float width = layout.Battle.width;

            var title = new GUIStyle(GUI.skin.label);
            title.alignment = TextAnchor.MiddleLeft;
            title.fontSize = Mathf.RoundToInt(Mathf.Clamp(safe.height * 0.034f, 17f, 28f));
            title.fontStyle = FontStyle.Bold;
            title.normal.textColor = Color.white;

            var detail = new GUIStyle(GUI.skin.label);
            detail.fontSize = Mathf.RoundToInt(Mathf.Clamp(safe.height * 0.020f, 11f, 17f));
            detail.normal.textColor = new Color(0.81f, 0.89f, 0.96f, 1f);
            detail.wordWrap = true;

            string heading = controller != null && controller.PracticeBattle != null
                ? "ORDR MOBILE / 10R 연습 · 원작 최종 전투 미검증"
                : "ORDR MOBILE  /  NIGHTMARE - OTHERWORLD";
#if UNITY_EDITOR
            if (controller != null && controller.IsEditorPreview)
                heading = "EDITOR 체험 · 가상 능력치 / 정식 게임 아님";
#endif
            GUI.Label(new Rect(left, top + 2f, width, layout.HeaderHeight * 0.56f),
                heading, title);

            string status;
            if (controller == null || controller.Units == null)
                status = "캐릭터 데이터 불러오기 대기 중";
            else if (controller.Combat == null ||
                controller.Combat.Game.Phase == SessionPhase.NotStarted)
                status = "캐릭터 도감 " + controller.Units.Count +
                    "종  |  검증된 첫 라운드 시작 대기";
            else if (controller.PracticeBattle != null)
                status = controller.Combat.Game.Phase == SessionPhase.Running
                    ? "연습 " + controller.Combat.Game.CurrentWave + "/10 · " +
                        Math.Ceiling(controller.PracticeBattle.SecondsRemaining) + "초 · 적 " +
                        controller.Combat.Game.AliveEnemies + "/" + controller.PracticeBattle.EnemyLimit
                    : controller.PracticeBattle.ResultMessage;
            else if (controller.IsUnitPreparation)
                status = "유닛 준비 · 소환 / 조합 / 판매 가능 · 전투 연결 대기";
            else
                status = "라운드 " + controller.Combat.Game.CurrentWave +
                    "  |  남은 몬스터 " + controller.Combat.Game.AliveEnemies +
                    "  |  상태 " + controller.Combat.Game.Phase;
#if UNITY_EDITOR
            if (controller != null && controller.IsEditorPreview)
                status = "[개발용 체험] " + status;
#endif
            GUI.Label(new Rect(left, top + layout.HeaderHeight * 0.56f,
                width, layout.HeaderHeight * 0.44f), status, detail);
            if (controller != null && controller.Combat != null &&
                controller.Combat.Game.Phase == SessionPhase.Running &&
                !controller.IsUnitPreparation)
            {
                var pauseRect = new Rect(left + width - 91f, top + 4f, 89f,
                    Mathf.Min(38f, layout.HeaderHeight * 0.52f));
                if (GUI.Button(pauseRect, controller.IsGameplayPaused ? "계속" : "일시정지"))
                {
                    try { controller.SetManualGameplayPause(!controller.IsGameplayPaused); }
                    catch (InvalidOperationException ex) { Debug.LogWarning(ex.Message); }
                }
#if UNITY_EDITOR
                // Only explicitly started, synthetic Editor playtests get speed controls.
                if (controller.IsEditorPreview && width >= 440f)
                {
                    int[] speeds = { 1, 2, 4 };
                    const float buttonWidth = 43f;
                    const float gap = 3f;
                    float firstX = pauseRect.x -
                        speeds.Length * (buttonWidth + gap) - 5f;
                    for (int i = 0; i < speeds.Length; i++)
                    {
                        int speed = speeds[i];
                        Rect button = new Rect(firstX + i * (buttonWidth + gap),
                            pauseRect.y, buttonWidth, pauseRect.height);
                        string label = controller.EditorPreviewSpeed == speed
                            ? "[" + speed + "x]" : speed + "x";
                        if (GUI.Button(button, label))
                        {
                            try { controller.SetEditorPreviewSpeed(speed); }
                            catch (Exception ex) { Debug.LogWarning(ex.Message); }
                        }
                    }
                }
#endif
            }

            // Do not cover the skill ribbon: OrdrMobileTouchPanel owns its
            // horizontally scrolling buttons in this dedicated blank margin.
            var area = new GUIStyle(GUI.skin.label);
            area.alignment = TextAnchor.UpperLeft;
            area.fontStyle = FontStyle.Bold;
            area.normal.textColor = new Color(0.80f, 0.88f, 0.98f);
            // A full-size IMGUI box would paint over the camera's actual
            // SpriteRenderers, making the verified units appear invisible.
            GUI.Box(new Rect(layout.Battle.x, layout.Battle.y,
                Mathf.Min(132f, layout.Battle.width), 27f), "메인 전장", area);
            GUI.Box(layout.Story, "스토리 전장", area);
            if (controller != null &&
                (controller.Combat == null ||
                 controller.Combat.Game.Phase == SessionPhase.NotStarted))
            {
                string reason;
                bool ready = controller.CanBeginVerifiedFirstWave(out reason);
                float panelWidth = Mathf.Max(180f, layout.Battle.width * 0.65f);
                panelWidth = Mathf.Min(layout.Battle.width - 18f, panelWidth);
                float center = layout.Battle.x + layout.Battle.width * 0.5f;
                float y = layout.Battle.y + layout.Battle.height * 0.32f;
                var button = new Rect(center - panelWidth * 0.5f, y,
                    panelWidth, Mathf.Clamp(layout.Battle.height * 0.17f, 36f, 64f));
                bool previous = GUI.enabled;
                GUI.enabled = controller.Units != null;
                string startTitle = ready ? "악몽 · 이세계모드 시작" : "10R 연습 게임 시작";
#if UNITY_EDITOR
                if (controller.IsEditorPreview)
                    startTitle = "체험 첫 라운드 시작 (가상 능력치)";
#endif
                bool clicked = GUI.Button(button, startTitle);
                GUI.enabled = previous;
                if (clicked)
                {
                    try
                    {
                        if (ready) controller.BeginVerifiedFirstWave();
                        else controller.BeginPracticeBattle();
                        startFailure = null;
                    }
                    catch (Exception ex)
                    {
                        startFailure = ex.Message;
                        controller.RecordSessionIssue("첫 라운드 시작 실패", ex.Message);
                        Debug.LogException(ex);
                    }
                }
                string message = string.IsNullOrEmpty(startFailure)
                    ? (ready ? "게임 시작 준비 완료" :
                        "평타 중심 개발용 연습입니다. 몬스터 최종 수치와 스킬은 원작 대조 중입니다. 위습에서 유닛을 소환해 주세요.")
                    : startFailure;
                var hint = new GUIStyle(detail) { alignment = TextAnchor.UpperCenter };
                GUI.Label(new Rect(button.x, button.yMax + 9f,
                    button.width, Mathf.Min(64f, layout.Battle.height * 0.3f)),
                    message, hint);
                if (!ready && GUI.Button(
                    new Rect(button.x, button.yMax + 73f, button.width, 34f), "유닛 준비만 하기"))
                {
                    try { controller.BeginUnitPreparation(); startFailure = null; }
                    catch (Exception ex) { startFailure = ex.Message; }
                }
            }
            else if (controller != null && controller.Combat != null &&
                (controller.Combat.Game.Phase == SessionPhase.Won ||
                 controller.Combat.Game.Phase == SessionPhase.Lost || controller.IsUnitPreparation))
            {
                float center = layout.Battle.center.x;
                if (controller.IsUnitPreparation)
                {
                    var start = new Rect(center - 110f, layout.Battle.center.y - 46f, 220f, 44f);
                    if (GUI.Button(start, "10R 연습 전투 시작"))
                    {
                        try { controller.BeginPracticeBattle(); startFailure = null; }
                        catch (Exception ex) { startFailure = ex.Message; }
                    }
                    GUI.Label(new Rect(start.x - 20f, start.yMax + 6f, start.width + 40f, 58f),
                        string.IsNullOrEmpty(startFailure) ?
                        "위습 소환·조합 후 시작할 수 있습니다. 평타 중심 / 원작 최종 전투 미검증" : startFailure, detail);
                }
                else if (controller.PracticeBattle != null)
                    GUI.Label(new Rect(layout.Battle.x + 8f, layout.Battle.center.y - 70f,
                        layout.Battle.width - 16f, 42f), controller.PracticeBattle.ResultMessage, title);
                var restart = new Rect(center - 92f,
                    controller.IsUnitPreparation ? layout.Battle.yMax - 48f :
                        layout.Battle.center.y - 22f, 184f, 44f);
                if (!confirmNewGame)
                {
                    if (GUI.Button(restart, controller.IsUnitPreparation ? "준비 초기화" : "새 게임")) confirmNewGame = true;
                }
                else
                {
                    if (GUI.Button(new Rect(restart.x - 75f, restart.y, 155f, 44f),
                        "처음부터 다시 시작"))
                        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                    if (GUI.Button(new Rect(restart.x + 90f, restart.y, 90f, 44f), "취소"))
                        confirmNewGame = false;
                }
            }
            // Right-hand area stays free for the selected left-hand tab.
            // The panel draws itself only when a tab has been opened.
        }
    }
}
