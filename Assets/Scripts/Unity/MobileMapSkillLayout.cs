using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
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

            float margin = 6f * uiScale;
            float top = screenHeight - safe.yMax;
            float left = safe.xMin + margin;
            float usableWidth = safe.width - 2f * margin;
            // No title header. Only a thin RTS-style resource strip remains.
            float headerHeight = 0f;
            float resourceHeight = 52f * uiScale;
            float gap = Mathf.Clamp(safe.height * 0.009f, 5f, 9f);
            // Korean labels need room after IMGUI padding and scrollbar gutter.
            // The old 116px cap wrapped navigation to one character per line.
            float railWidth = Mathf.Clamp(usableWidth * 0.115f, 150f * uiScale, 270f * uiScale);
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

            float mapTop = top + margin + resourceHeight + gap;
            float available = top + safe.height - margin - mapTop;
            if (available < 120f || mapWidth < 120f)
                throw new ArgumentOutOfRangeException(nameof(safe), "가로 전장 표시 공간이 부족합니다.");

            // Fill the space below the resource strip to the bottom inset.
            // The ribbon stays touch-sized; the smaller story area gets 24% of field space for the visible fortress.
            float playableHeight = available;
            float ribbonHeight = Mathf.Min(
                Mathf.Max(84f * uiScale, safe.height * 0.083f), playableHeight * 0.30f);
            float fieldSpace = playableHeight - ribbonHeight - gap * 2f;
            float storyHeight = fieldSpace * 0.24f;
            float battleHeight = fieldSpace - storyHeight;
            float ribbonTop = mapTop + battleHeight + gap;
            float storyTop = ribbonTop + ribbonHeight + gap;

            return new MobileMapSkillLayout
            {
                HeaderHeight = headerHeight,
                LeftTabs = new Rect(left, top + margin, railWidth, safe.height - margin * 2f),
                Battle = new Rect(mapLeft, mapTop, mapWidth, battleHeight),
                SkillMargin = new Rect(mapLeft, ribbonTop, mapWidth, ribbonHeight),
                Story = new Rect(mapLeft, storyTop, mapWidth, storyHeight),
                StatusMargin = new Rect(mapLeft, top + margin,
                    horizontalSpace + gap, resourceHeight),
                // Right-side content retains the full available height even
                // though the battlefields themselves have become smaller.
                RightDrawer = new Rect(mapLeft + mapWidth + gap,
                    mapTop, drawerWidth, available)
            };
        }

        public static Rect[] StatusChipRects(Rect area, int count = 4)
        {
            if (area.width <= 0f || area.height <= 0f)
                throw new ArgumentOutOfRangeException(nameof(area));
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            float gap = Mathf.Clamp(area.width * .01f, 3f, 8f);
            float width = (area.width - gap * (count - 1)) / count;
            if (width <= 0f) throw new ArgumentOutOfRangeException(nameof(area));
            var slots = new Rect[count];
            for (int i = 0; i < count; i++)
                slots[i] = new Rect(area.x + (width + gap) * i, area.y, width, area.height);
            return slots;
        }

        // Used by the world presenter to reject UI taps even when
        // underlying unit markers occupy the same world-space point.
        public bool BlocksWorldTap(Vector2 onGui, bool drawerOpen)
        {
            return LeftTabs.Contains(onGui) || StatusMargin.Contains(onGui) ||
                SkillMargin.Contains(onGui) ||
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

    // Read-only layout for hovering unused-Wisp reminders. Units and pathing
    // remain unchanged: these are screen-space decorations over the main field.
    // Single source for both rendering and tap blocking.
    public static class MobileUnspentWispOverlay
    {
        public static bool ShouldShow(long balance) { return balance > 0; }

        // Keep the source of truth explicit: e018 Choice Wisps stay out
        // of the battlefield reminder even when their balance is positive.
        // Story transcend choices have their own pending-reward counter.
        public static bool ShouldShowWispResource(string resourceId, long balance)
        {
            return resourceId == WispResourceIds.Random && ShouldShow(balance);
        }

        public static Rect MarkerRect(Rect physicalBattle, float scale,
            int index, int count, float unscaledSeconds)
        {
            if (scale <= 0 || float.IsNaN(scale) || float.IsInfinity(scale) ||
                float.IsNaN(unscaledSeconds) || float.IsInfinity(unscaledSeconds) ||
                physicalBattle.width <= 0 || physicalBattle.height <= 0 ||
                count < 1 || count > 3 || index < 0 || index >= count)
                throw new ArgumentOutOfRangeException(nameof(index),
                    "미사용 위습의 배치 입력이 올바르지 않습니다.");
            Rect battle = MobileMapSkillLayout.Scaled(physicalBattle, scale);
            float inset = 6f;
            float gap = 6f;
            int columns = count == 3 && battle.width < 285f ? 2 : count;
            float width = Mathf.Min(142f,
                (battle.width - 2f * inset - gap * (columns - 1)) / columns);
            float height = Mathf.Min(62f, Mathf.Max(36f, battle.height * .34f));
            int col = index % columns;
            int row = index / columns;
            float bob = Mathf.Sin(unscaledSeconds * 2.6f + index * 1.65f) * 5f;
            float x = battle.xMin + inset + col * (width + gap);
            float y = battle.yMin + inset + row * (height + gap) + bob;
            y = Mathf.Clamp(y, battle.yMin + inset,
                battle.yMax - height - inset);
            return new Rect(x, y, width, height);
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

        // Maximum half-height that fits the ENTIRE orthographic viewport inside
        // a fixed authored board. Zooming out must never expose the void beyond it.
        public static float MaximumContainedHalfHeight(Rect worldBounds, float aspect)
        {
            if (!Finite(worldBounds.xMin) || !Finite(worldBounds.yMin) ||
                !Finite(worldBounds.width) || !Finite(worldBounds.height) ||
                !Finite(aspect) || worldBounds.width <= 0f ||
                worldBounds.height <= 0f || aspect <= 0f) return 0f;
            return Mathf.Min(worldBounds.height * .5f,
                worldBounds.width / (2f * aspect));
        }

        public static bool TryClampToWorldBounds(Vector2 requestedCenter,
            float halfHeight, float viewportAspect, Rect worldBounds,
            out Vector2 clampedCenter)
        {
            clampedCenter = requestedCenter;
            float maximum = MaximumContainedHalfHeight(worldBounds, viewportAspect);
            if (!Finite(requestedCenter.x) || !Finite(requestedCenter.y) ||
                !Finite(halfHeight) || halfHeight <= 0f ||
                maximum <= 0f || halfHeight > maximum + 0.001f) return false;
            float halfWidth = halfHeight * viewportAspect;
            clampedCenter = new Vector2(
                Mathf.Clamp(requestedCenter.x, worldBounds.xMin + halfWidth,
                    worldBounds.xMax - halfWidth),
                Mathf.Clamp(requestedCenter.y, worldBounds.yMin + halfHeight,
                    worldBounds.yMax - halfHeight));
            return true;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}

