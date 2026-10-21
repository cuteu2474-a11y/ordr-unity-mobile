using UnityEngine;

namespace Ordr.Mobile.Unity
{
    // Step 1: visual scene shell only. Never creates fake waves, units,
    // money, damage, skill events, or story completion.
    [DisallowMultipleComponent]
    public sealed class OrdrMainSceneShell : MonoBehaviour
    {
        private OrdrCharacterCombatController controller;

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

            float top = Screen.height - safe.yMax;
            float margin = Mathf.Clamp(safe.width * 0.015f, 12f, 24f);
            float x = safe.x + margin;
            float w = safe.width - 2f * margin;
            float headerHeight = Mathf.Clamp(safe.height * 0.11f, 52f, 78f);

            var title = new GUIStyle(GUI.skin.label);
            title.alignment = TextAnchor.MiddleLeft;
            title.fontSize = Mathf.RoundToInt(Mathf.Clamp(safe.height * 0.038f, 19f, 29f));
            title.fontStyle = FontStyle.Bold;
            title.normal.textColor = Color.white;

            var detail = new GUIStyle(GUI.skin.label);
            detail.fontSize = Mathf.RoundToInt(Mathf.Clamp(safe.height * 0.024f, 13f, 18f));
            detail.normal.textColor = new Color(0.81f, 0.89f, 0.96f, 1f);
            detail.wordWrap = true;

            GUI.Label(new Rect(x, top + 6f, w, headerHeight * 0.56f),
                "ORDR MOBILE  /  NIGHTMARE - OTHERWORLD", title);

            string status = controller == null || controller.Units == null
                ? "캐릭터 데이터 불러오기 대기 중 (Unity 상단 ORDR 메뉴에서 동기화)"
                : "캐릭터 도감 " + controller.Units.Count +
                  "종 연결  |  전투·스토리 실제 실행은 다음 개발 단계에서 연결";
            GUI.Label(new Rect(x, top + headerHeight * 0.57f, w, headerHeight * 0.42f),
                status, detail);

            // Reserve recognisable zones before actual battlefield physics
            // and original monster/wave rules are implemented.
            float mapTop = top + headerHeight + 8f;
            float available = Mathf.Max(30f, safe.height * 0.49f);
            float storyWidth = w * 0.285f;
            float gap = Mathf.Max(8f, w * 0.012f);
            float battleWidth = w - storyWidth - gap;

            GUI.Box(new Rect(x, mapTop, battleWidth, available),
                "메인 전장  /  전투 구역 준비 중");
            GUI.Box(new Rect(x + battleWidth + gap, mapTop, storyWidth, available),
                "스토리 전장  /  수동 파견 준비 중");

            float textTop = mapTop + available + 10f;
            GUI.Label(new Rect(x, textTop, w, 46f),
                "1단계: Unity 프로젝트와 첫 화면 구성. " +
                "유닛·적 생성, 웨이브, 경제와 전투는 아직 작동하지 않습니다.", detail);
        }
    }
}
