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

        public void Begin(Vector2 pointer, Rect viewport, float scrollY)
        {
            Cancel();
            // Leave the native scrollbar available for direct thumb dragging.
            if (!viewport.Contains(pointer) || pointer.x >= viewport.xMax - 22f) return;
            IsTracking = true;
            origin = pointer;
            initialScroll = scrollY;
        }

        public bool Move(Vector2 pointer, float maximum, ref float scrollY)
        {
            if (!IsTracking) return false;
            float distance = pointer.y - origin.y;
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
}
