using System;
using UnityEngine;
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

            GUI.Label(new Rect(left, top + 2f, width, layout.HeaderHeight * 0.56f),
                "ORDR MOBILE  /  NIGHTMARE - OTHERWORLD", title);

            string status;
            if (controller == null || controller.Units == null)
                status = "캐릭터 데이터 불러오기 대기 중";
            else if (controller.Combat == null ||
                controller.Combat.Game.Phase == SessionPhase.NotStarted)
                status = "캐릭터 도감 " + controller.Units.Count +
                    "종  |  검증된 첫 라운드 시작 대기";
            else
                status = "라운드 " + controller.Combat.Game.CurrentWave +
                    "  |  남은 몬스터 " + controller.Combat.Game.AliveEnemies +
                    "  |  상태 " + controller.Combat.Game.Phase;
            GUI.Label(new Rect(left, top + layout.HeaderHeight * 0.56f,
                width, layout.HeaderHeight * 0.44f), status, detail);

            // Do not cover the skill ribbon: OrdrMobileTouchPanel owns its
            // horizontally scrolling buttons in this dedicated blank margin.
            var area = new GUIStyle(GUI.skin.label);
            area.alignment = TextAnchor.UpperLeft;
            area.fontStyle = FontStyle.Bold;
            area.normal.textColor = new Color(0.80f, 0.88f, 0.98f);
            GUI.Box(layout.Battle, "메인 전장", area);
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
                GUI.enabled = ready;
                bool clicked = GUI.Button(button, "악몽 · 이세계모드 시작");
                GUI.enabled = previous;
                if (clicked)
                {
                    try
                    {
                        controller.BeginVerifiedFirstWave();
                        startFailure = null;
                    }
                    catch (Exception ex)
                    {
                        startFailure = ex.Message;
                        Debug.LogException(ex);
                    }
                }
                string message = string.IsNullOrEmpty(startFailure)
                    ? (ready ? "게임 시작 준비 완료" : reason) : startFailure;
                var hint = new GUIStyle(detail) { alignment = TextAnchor.UpperCenter };
                GUI.Label(new Rect(button.x, button.yMax + 9f,
                    button.width, layout.Battle.height * 0.3f), message, hint);
            }
            // Right-hand area stays free for the selected left-hand tab.
            // The panel draws itself only when a tab has been opened.
        }
    }
}
