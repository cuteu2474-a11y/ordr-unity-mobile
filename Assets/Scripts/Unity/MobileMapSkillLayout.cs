using System;
using UnityEngine;

namespace Ordr.Mobile.Unity
{
    // Landscape-only, top-to-bottom battlefield layout.
    // Shared by the shell, manual skill ribbon and world-tap hit testing.
    // Rect coordinates follow OnGUI (origin at the top left of the screen).
    public sealed class MobileMapSkillLayout
    {
        public Rect Battle { get; private set; }
        public Rect SkillMargin { get; private set; }
        public Rect Story { get; private set; }
        public Rect BottomControls { get; private set; }
        public float HeaderHeight { get; private set; }

        // Avoid enormous touch controls on very high-DPI landscape phones.
        public static float UiScale(Rect safe, float dpi)
        {
            float density = dpi > 0f ? dpi / 160f : 1f;
            return Mathf.Clamp(Mathf.Min(density, safe.height / 620f), 1f, 1.6f);
        }

        public static MobileMapSkillLayout Calculate(Rect safe, float screenHeight, float uiScale)
        {
            if (safe.width <= 0f || safe.height <= 0f || uiScale <= 0f ||
                float.IsNaN(uiScale) || float.IsInfinity(uiScale))
                throw new ArgumentOutOfRangeException(nameof(safe));
            if (safe.width <= safe.height)
                throw new ArgumentException("ORDR 모바일은 가로 화면 전용입니다.", nameof(safe));

            float margin = Mathf.Clamp(safe.width * 0.015f, 10f, 24f);
            float top = screenHeight - safe.yMax;
            float left = safe.xMin + margin;
            float width = safe.width - 2f * margin;
            float headerHeight = Mathf.Clamp(safe.height * 0.09f, 44f, 68f);
            float gap = Mathf.Clamp(safe.height * 0.009f, 5f, 9f);
            float dockHeight = 54f * uiScale;

            float mapTop = top + headerHeight + gap;
            float dockTop = top + safe.height - dockHeight;
            float available = dockTop - gap - mapTop;
            if (available < 120f || width < 120f)
                throw new ArgumentOutOfRangeException(nameof(safe), "가로 전장 표시 공간이 부족합니다.");

            // Horizontal swipe ribbon; never a side rail or portrait layout.
            float ribbon = Mathf.Min(Mathf.Max(52f * uiScale, safe.height * 0.083f),
                available * 0.19f);
            float fieldSpace = available - ribbon - gap * 2f;
            // 80:20 main/story split of the actual playable field area.
            float storyHeight = fieldSpace * 0.20f;
            float battleHeight = fieldSpace - storyHeight;
            float ribbonTop = mapTop + battleHeight + gap;
            float storyTop = ribbonTop + ribbon + gap;

            return new MobileMapSkillLayout
            {
                HeaderHeight = headerHeight,
                Battle = new Rect(left, mapTop, width, battleHeight),
                SkillMargin = new Rect(left, ribbonTop, width, ribbon),
                Story = new Rect(left, storyTop, width, storyHeight),
                BottomControls = new Rect(left, dockTop, width, dockHeight)
            };
        }

        public static Rect Scaled(Rect rect, float factor)
        {
            if (factor <= 0f) throw new ArgumentOutOfRangeException(nameof(factor));
            return new Rect(rect.x / factor, rect.y / factor,
                rect.width / factor, rect.height / factor);
        }
    }
}
