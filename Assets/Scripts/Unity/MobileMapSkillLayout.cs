using System;
using UnityEngine;

namespace Ordr.Mobile.Unity
{
    // Landscape-only map with permanent left navigation and a right-side
    // detail drawer. The actual battlefield is not covered by the tab rail.
    // Coordinates follow OnGUI: origin at top left, physical screen pixels.
    public sealed class MobileMapSkillLayout
    {
        public Rect Battle { get; private set; }
        public Rect SkillMargin { get; private set; }
        public Rect Story { get; private set; }
        public Rect LeftTabs { get; private set; }
        public Rect RightDrawer { get; private set; }
        public float HeaderHeight { get; private set; }

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
            float usableWidth = safe.width - 2f * margin;
            float headerHeight = Mathf.Clamp(safe.height * 0.09f, 44f, 68f);
            float gap = Mathf.Clamp(safe.height * 0.009f, 5f, 9f);
            float railWidth = Mathf.Min(usableWidth * 0.17f,
                Mathf.Clamp(76f * uiScale, 64f, 116f));
            float mapLeft = left + railWidth + gap;

            // Two modest battlefields in the center, with a permanently
            // reserved right column for the tab detail panel. Showing or
            // closing the drawer never changes camera size or map geometry.
            float horizontalSpace = usableWidth - railWidth - gap * 2f;
            // Smaller battlefields: 54% of the post-navigation area is
            // reserved for the maps; 46% remains for readable right-hand tabs.
            // The drawer never covers the main battlefield.
            float mapWidth = horizontalSpace * 0.54f;
            float drawerWidth = horizontalSpace - mapWidth;

            float mapTop = top + headerHeight + gap;
            float available = top + safe.height - margin - mapTop;
            if (available < 120f || mapWidth < 120f)
                throw new ArgumentOutOfRangeException(nameof(safe), "가로 전장 표시 공간이 부족합니다.");

            // The main/story map stack is 78% of the usable height,
            // leaving space below for future resource/status controls.
            // Keep the swipeable ribbon comfortably touch-sized even as the
            // battlefields shrink.
            float playableHeight = available * 0.78f;
            float ribbonHeight = Mathf.Min(
                Mathf.Max(52f * uiScale, safe.height * 0.083f), playableHeight * 0.27f);
            float fieldSpace = playableHeight - ribbonHeight - gap * 2f;
            float storyHeight = fieldSpace * 0.20f;
            float battleHeight = fieldSpace - storyHeight;
            float ribbonTop = mapTop + battleHeight + gap;
            float storyTop = ribbonTop + ribbonHeight + gap;

            return new MobileMapSkillLayout
            {
                HeaderHeight = headerHeight,
                LeftTabs = new Rect(left, mapTop, railWidth, available),
                Battle = new Rect(mapLeft, mapTop, mapWidth, battleHeight),
                SkillMargin = new Rect(mapLeft, ribbonTop, mapWidth, ribbonHeight),
                Story = new Rect(mapLeft, storyTop, mapWidth, storyHeight),
                // Right-side content retains the full available height even
                // though the battlefields themselves have become smaller.
                RightDrawer = new Rect(mapLeft + mapWidth + gap,
                    mapTop, drawerWidth, available)
            };
        }

        // Used by the world presenter to reject UI taps even when
        // underlying unit markers occupy the same world-space point.
        public bool BlocksWorldTap(Vector2 onGui, bool drawerOpen)
        {
            return LeftTabs.Contains(onGui) ||
                (drawerOpen && RightDrawer.Contains(onGui));
        }

        // Unity camera.rect uses normalized bottom-left viewport coordinates.
        public Rect MainCameraViewport(float screenWidth, float screenHeight)
        {
            if (screenWidth <= 0f || screenHeight <= 0f)
                throw new ArgumentOutOfRangeException(nameof(screenWidth));
            return new Rect(Battle.xMin / screenWidth,
                1f - Battle.yMax / screenHeight,
                Battle.width / screenWidth,
                Battle.height / screenHeight);
        }

        public static Rect Scaled(Rect rect, float factor)
        {
            if (factor <= 0f) throw new ArgumentOutOfRangeException(nameof(factor));
            return new Rect(rect.x / factor, rect.y / factor,
                rect.width / factor, rect.height / factor);
        }
    }
}
