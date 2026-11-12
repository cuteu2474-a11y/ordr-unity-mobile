using UnityEngine;

namespace Ordr.Mobile.Unity
{
    // Logical IMGUI coordinates. A tap stays a tap until it crosses the
    // threshold; a drag release must never activate the underlying button.
    public sealed class MobilePanelScrollGesture
    {
        public bool IsTracking { get; private set; }
        public bool IsDragging { get; private set; }
        private Vector2 origin;
        private float initialScroll;
        private bool horizontal;

        public void Begin(Vector2 pointer, Rect viewport, float scrollY,
            bool horizontalAxis = false, bool reserveScrollbar = true)
        {
            Cancel();
            // Leave the native scrollbar available for direct thumb dragging.
            if (!viewport.Contains(pointer) || (reserveScrollbar &&
                (horizontalAxis ? pointer.y >= viewport.yMax - 22f :
                    pointer.x >= viewport.xMax - 22f))) return;
            IsTracking = true;
            horizontal = horizontalAxis;
            origin = pointer;
            initialScroll = scrollY;
        }

        public bool Move(Vector2 pointer, float maximum, ref float scrollY)
        {
            if (!IsTracking) return false;
            float distance = horizontal ? pointer.x - origin.x : pointer.y - origin.y;
            if (!IsDragging && Mathf.Abs(distance) < 8f) return false;
            IsDragging = true;
            scrollY = Mathf.Clamp(initialScroll - distance, 0f, Mathf.Max(0f, maximum));
            return true;
        }

        public bool End()
        {
            bool consumed = IsDragging;
            Cancel();
            return consumed;
        }

        public void Cancel()
        {
            IsTracking = false;
            IsDragging = false;
        }
    }

    public enum MobileLongPressResult { None, Tap, Hold }

    // One-up/one-down input policy shared by spend-on-tap actions. All
    // costs and eligibility still belong to the underlying economy service.
    public sealed class MobileLongPressGesture
    {
        public const float HoldThresholdSeconds = 0.55f;
        private const float MovementTolerance = 12f;
        private string activeKey;
        private float pressedAt;
        private Vector2 pressedPosition;
        private bool suppressNextClick;

        public void Begin(string key, float atSeconds, Vector2 atPosition)
        {
            activeKey = key;
            suppressNextClick = false;
            pressedAt = atSeconds;
            pressedPosition = atPosition;
        }

        public bool IsTracking(string key)
        {
            return activeKey == key && !string.IsNullOrEmpty(key);
        }

        public bool Move(string key, Vector2 position)
        {
            if (!IsTracking(key)) return false;
            if (Vector2.Distance(pressedPosition, position) <= MovementTolerance)
                return false;
            activeKey = null;
            suppressNextClick = true;
            return true;
        }

        public MobileLongPressResult Complete(string key, float atSeconds)
        {
            // Keyboard/other GUI activation retains the default one-tap
            // behavior; a canceled drag cannot accidentally spend resources.
            if (activeKey == null)
            {
                bool wasDrag = suppressNextClick;
                suppressNextClick = false;
                return wasDrag ? MobileLongPressResult.None : MobileLongPressResult.Tap;
            }
            if (!IsTracking(key)) return MobileLongPressResult.None;
            bool hold = atSeconds - pressedAt >= HoldThresholdSeconds;
            Cancel();
            return hold ? MobileLongPressResult.Hold : MobileLongPressResult.Tap;
        }

        public void Cancel()
        {
            activeKey = null;
            suppressNextClick = false;
        }
    }
}

