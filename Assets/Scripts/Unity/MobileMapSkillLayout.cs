using UnityEngine;

namespace Ordr.Mobile.Unity
{
    // One layout shared by the battlefield shell and the touch panel.
    // All Rects use unscaled OnGUI screen coordinates, accounting for the safe area.
    public sealed class MobileMapSkillLayout
    {
        public Rect Battle { get; private set; }
        public Rect Story { get; private set; }
        public Rect SkillMargin { get; private set; }
        public bool SideMargin { get; private set; }

        public static MobileMapSkillLayout Calculate(Rect safe, float screenHeight, float uiScale)
        {
            if (safe.width <= 0f || safe.height <= 0f || uiScale <= 0f)
                throw new System.ArgumentOutOfRangeException(nameof(safe));
            float margin = Mathf.Clamp(safe.width * 0.015f, 12f, 24f);
            float left = safe.xMin + margin;
            float top = screenHeight - safe.yMax;
            float contentWidth = safe.width - 2f * margin;
            float headerHeight = Mathf.Clamp(safe.height * 0.11f, 52f, 78f);
            float mapY = top + headerHeight + 8f;
            float gap = Mathf.Max(8f, contentWidth * 0.012f);
            bool side = contentWidth / uiScale >= 560f;

            float mapsWidth = contentWidth;
            Rect rail;
            if (side)
            {
                float railWidth = Mathf.Min(contentWidth * 0.26f,
                    Mathf.Max(118f * uiScale, contentWidth * 0.19f));
                mapsWidth -= railWidth + gap;
                rail = new Rect(left + mapsWidth + gap, mapY, railWidth,
                    Mathf.Max(30f, safe.height * 0.49f));
            }
            else
            {
                float mapHeight = Mathf.Max(30f, safe.height * 0.42f);
                rail = new Rect(left, mapY + mapHeight + gap, contentWidth,
                    Mathf.Clamp(safe.height * 0.12f, 58f * uiScale, 94f * uiScale));
            }

            float height = Mathf.Max(30f, safe.height * (side ? 0.49f : 0.42f));
            float storyWidth = mapsWidth * 0.285f;
            float battleWidth = mapsWidth - storyWidth - gap;
            if (battleWidth <= 0f) throw new System.InvalidOperationException("전장 화면 폭 부족");
            return new MobileMapSkillLayout
            {
                Battle = new Rect(left, mapY, battleWidth, height),
                Story = new Rect(left + battleWidth + gap, mapY, storyWidth, height),
                SkillMargin = rail,
                SideMargin = side
            };
        }

        public static Rect Scaled(Rect rect, float factor)
        {
            return new Rect(rect.x / factor, rect.y / factor,
                rect.width / factor, rect.height / factor);
        }
    }
}
