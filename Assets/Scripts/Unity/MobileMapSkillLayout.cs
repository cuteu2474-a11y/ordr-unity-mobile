using System;
using System.Collections.Generic;
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
        public Rect StatusMargin { get; private set; }
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
                StatusMargin = new Rect(mapLeft, storyTop + storyHeight + gap,
                    mapWidth, available - playableHeight - gap),
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

    // Presentation-only framing for the actual Warcraft map coordinates.
    // No combat distance, targeting range or movement coordinates are changed.
    public static class MobileBattlefieldCameraFraming
    {
        public static bool TryFrame(IReadOnlyList<Vector2> positions, float aspect,
            out Vector2 center, out float orthographicHalfHeight,
            float minimumHalfHeight = 420f)
        {
            center = Vector2.zero;
            orthographicHalfHeight = minimumHalfHeight;
            if (positions == null || positions.Count == 0 || aspect <= 0f ||
                float.IsNaN(aspect) || float.IsInfinity(aspect) ||
                minimumHalfHeight <= 0f || float.IsNaN(minimumHalfHeight) ||
                float.IsInfinity(minimumHalfHeight)) return false;

            double xMin = double.PositiveInfinity, yMin = double.PositiveInfinity;
            double xMax = double.NegativeInfinity, yMax = double.NegativeInfinity;
            foreach (var position in positions)
            {
                if (float.IsNaN(position.x) || float.IsInfinity(position.x) ||
                    float.IsNaN(position.y) || float.IsInfinity(position.y))
                    return false;
                xMin = Math.Min(xMin, position.x);
                xMax = Math.Max(xMax, position.x);
                yMin = Math.Min(yMin, position.y);
                yMax = Math.Max(yMax, position.y);
            }
            double cx = xMin + (xMax - xMin) * 0.5;
            double cy = yMin + (yMax - yMin) * 0.5;
            double halfHeight = Math.Max(minimumHalfHeight,
                Math.Max((yMax - yMin) * 0.65,
                    (xMax - xMin) * 0.65 / aspect));
            if (Math.Abs(cx) > float.MaxValue || Math.Abs(cy) > float.MaxValue ||
                halfHeight > float.MaxValue) return false;
            center = new Vector2((float)cx, (float)cy);
            orthographicHalfHeight = (float)halfHeight;
            return true;
        }
    }

    // Pure presentation-space camera math. Positions of monsters/characters,
    // targeting, attack range and the deterministic game clock stay untouched.
    public static class MobileBattlefieldCameraNavigation
    {
        public static bool TryPan(Vector2 center, Vector2 screenPixelsMoved,
            float halfHeight, float viewportPixelHeight, out Vector2 movedCenter)
        {
            movedCenter = center;
            if (!Finite(center.x) || !Finite(center.y) ||
                !Finite(screenPixelsMoved.x) || !Finite(screenPixelsMoved.y) ||
                !Finite(halfHeight) || !Finite(viewportPixelHeight) ||
                halfHeight <= 0f || viewportPixelHeight <= 0f) return false;
            float unitsPerPixel = 2f * halfHeight / viewportPixelHeight;
            Vector2 candidate = center - screenPixelsMoved * unitsPerPixel;
            if (!Finite(candidate.x) || !Finite(candidate.y)) return false;
            movedCenter = candidate;
            return true;
        }

        // anchorOffsetPixels is relative to the camera viewport CENTER,
        // with Unity's bottom-up Y. Zooming retains the selected map point.
        public static bool TryZoom(Vector2 center, float halfHeight,
            Vector2 anchorOffsetPixels, float viewportPixelHeight, float sizeFactor,
            float minimumHalfHeight, float maximumHalfHeight,
            out Vector2 movedCenter, out float zoomedHalfHeight)
        {
            movedCenter = center;
            zoomedHalfHeight = halfHeight;
            if (!Finite(center.x) || !Finite(center.y) ||
                !Finite(anchorOffsetPixels.x) || !Finite(anchorOffsetPixels.y) ||
                !Finite(halfHeight) || !Finite(viewportPixelHeight) ||
                !Finite(sizeFactor) || !Finite(minimumHalfHeight) ||
                !Finite(maximumHalfHeight) || halfHeight <= 0f ||
                viewportPixelHeight <= 0f || sizeFactor <= 0f ||
                minimumHalfHeight <= 0f || maximumHalfHeight < minimumHalfHeight)
                return false;
            float candidateSize = Mathf.Clamp(halfHeight * sizeFactor,
                minimumHalfHeight, maximumHalfHeight);
            if (!Finite(candidateSize)) return false;
            Vector2 anchoredWorld = center + anchorOffsetPixels *
                (2f * halfHeight / viewportPixelHeight);
            Vector2 candidateCenter = anchoredWorld - anchorOffsetPixels *
                (2f * candidateSize / viewportPixelHeight);
            if (!Finite(candidateCenter.x) || !Finite(candidateCenter.y)) return false;
            movedCenter = candidateCenter;
            zoomedHalfHeight = candidateSize;
            return true;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
