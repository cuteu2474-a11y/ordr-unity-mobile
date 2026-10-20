using System;
using System.Collections.Generic;
using UnityEngine;
using Ordr.Mobile.Mobile;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Unity
{
    // 임시 Android 터치 패널. Unity UI 패키지/추가 이미지 없이 동작하도록
    // OnGUI로 구현했으며, 원작 전투 규칙이나 경제 수치는 변경하지 않는다.
    // 스토리·조합확인·상점·도박·위습 버튼은 실제 사용자 탭에서만 호출된다.
    public sealed class OrdrMobileTouchPanel : MonoBehaviour
    {
        private enum Tab { Story, Fusion, Shop, Gambling, Wisp }

        [SerializeField] private OrdrCharacterCombatController controller;
        [SerializeField] private bool openByDefault;
        private bool visible;
        private Tab currentTab = Tab.Story;
        private Vector2 scroll;
        private string notice = "모바일 조작 패널";
        private IReadOnlyList<MobileFusionPreview> fusionOffers =
            new List<MobileFusionPreview>().AsReadOnly();
        private IReadOnlyList<MobileManualOffer> economyOffers =
            new List<MobileManualOffer>().AsReadOnly();
        private MobileFusionPreview pendingFusion;
        private MobileManualOffer pendingEconomy;

        private void Awake()
        {
            if (controller == null)
                controller = GetComponent<OrdrCharacterCombatController>();
            visible = openByDefault;
        }

        private Ordr.Mobile.Combat.CharacterCombatRuntime Combat
        {
            get { return controller != null ? controller.Combat : null; }
        }

        private void OnGUI()
        {
            if (controller == null) return;

            float dpi = Screen.dpi > 0 ? Screen.dpi : 160f;
            float scale = Mathf.Clamp(dpi / 160f, 1f, 3f);
            Rect safe = Screen.safeArea;
            float left = safe.x / scale;
            float top = (Screen.height - safe.yMax) / scale;
            float width = safe.width / scale;
            float height = safe.height / scale;
            if (width < 120 || height < 100) return;

            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            float toggleHeight = 54f;
            Rect toggle = new Rect(left + width - Math.Min(132f, width), top + height -
                toggleHeight, Math.Min(132f, width), toggleHeight);
            if (GUI.Button(toggle, visible ? "운용 닫기" : "운용 메뉴"))
            {
                visible = !visible;
                if (visible) RefreshCurrent();
            }

            if (visible)
            {
                // 모바일 전장 전체를 덮지 않도록 기본 최대 54%만 사용.
                float panelHeight = Mathf.Min(height * 0.54f, 480f);
                Rect area = new Rect(left, top + height - toggleHeight - panelHeight,
                    width, panelHeight);
                GUILayout.BeginArea(area, GUI.skin.box);
                GUILayout.BeginVertical();
                GUILayout.Label("원랜디 모바일 운용");
                DrawTabs();
                DrawModeStatus();
                scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
                if (Combat == null)
                    GUILayout.Label("게임 월드 연결 전입니다. 원본 규칙이나 재화를 임의로 실행하지 않습니다.");
                else
                {
                    switch (currentTab)
                    {
                        case Tab.Story: DrawStory(); break;
                        case Tab.Fusion: DrawFusion(); break;
                        case Tab.Shop: DrawEconomy(ManualEconomyCategory.Shop); break;
                        case Tab.Gambling: DrawEconomy(ManualEconomyCategory.Gambling); break;
                        case Tab.Wisp: DrawEconomy(ManualEconomyCategory.Wisp); break;
                    }
                }
                GUILayout.EndScrollView();
                GUILayout.Label(notice, GUILayout.MinHeight(30));
                GUILayout.EndVertical();
                GUILayout.EndArea();
            }
            GUI.matrix = previous;
        }

        private void DrawTabs()
        {
            GUILayout.BeginHorizontal();
            DrawTab(Tab.Story, "스토리");
            DrawTab(Tab.Fusion, "조합");
            DrawTab(Tab.Shop, "상점");
            DrawTab(Tab.Gambling, "도박");
            DrawTab(Tab.Wisp, "위습");
            GUILayout.EndHorizontal();
        }

        private void DrawTab(Tab tab, string text)
        {
            string label = currentTab == tab ? "● " + text : text;
            if (GUILayout.Button(label, GUILayout.MinHeight(49)))
            {
                if (currentTab == tab) return;
                currentTab = tab;
                scroll = Vector2.zero;
                pendingFusion = null;
                pendingEconomy = null;
                RefreshCurrent();
            }
        }

        private void DrawModeStatus()
        {
            var status = Combat != null ? Combat.MobileAutomation.Status : null;
            string placement = status != null && status.PlacementConnected ? "연결" : "규칙 미연결";
            string boss = status != null && status.BossFocusConnected ? "연결" : "규칙 미연결";
            string skill = status != null && status.AutoSkillsConnected ? "연결" : "규칙 미연결";
            GUILayout.Label("공격·확률 스킬: 자동 | 배치: " + placement +
                " | 보스: " + boss + " | Q/W/R: " + skill);
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
            if (!controller.ManualActions.HasFusionService)
                GUILayout.Label("원작 조합식·재료·비용 시스템 미연결");
            if (GUILayout.Button("조합식 새로고침", GUILayout.MinHeight(48)))
                RefreshCurrent();
            foreach (var offer in fusionOffers)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(offer.Title);
                GUILayout.Label(offer.OriginalMaterialsAndCost);
                GUILayout.Label("재료 인스턴스: " + string.Join(", ", ToStringArray(offer.SuggestedMaterialInstances)));
                if (GUILayout.Button("조합 선택", GUILayout.MinHeight(48)))
                {
                    pendingFusion = offer;
                    pendingEconomy = null;
                    notice = "아직 재료를 소비하지 않았습니다. 아래에서 조합을 확정하세요.";
                }
                GUILayout.EndVertical();
            }
            if (pendingFusion != null)
            {
                GUILayout.Label("선택됨: " + pendingFusion.Title);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("조합 확정", GUILayout.MinHeight(52)))
                {
                    ShowResult(controller.ManualActions.ConfirmFusion(pendingFusion));
                    pendingFusion = null;
                    RefreshCurrent();
                }
                if (GUILayout.Button("취소", GUILayout.MinHeight(52))) pendingFusion = null;
                GUILayout.EndHorizontal();
            }
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
                fusionOffers = controller.ManualActions.PreviewFusion(Combat.MobileUnitSnapshot());
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

        private static string[] ToStringArray(IReadOnlyList<long> values)
        {
            var result = new string[values.Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = values[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
            return result;
        }
    }
}
