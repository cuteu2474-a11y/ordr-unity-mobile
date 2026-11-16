using System;
using UnityEngine;

namespace Ordr.Mobile.Unity
{
    // Source-rect aware renderer shared by landscape formation, unit list,
    // fusion and choice-wisp portraits. Pixel dimensions of a private PNG,
    // cached portrait or atlas cell cannot enlarge the allocated UI frame.
    public static class MobileSpritePortraitDisplay
    {
        public static Rect Inset(Rect rect, float amount)
        {
            if (amount < 0f || float.IsNaN(amount) || float.IsInfinity(amount))
                throw new ArgumentOutOfRangeException(nameof(amount));
            float inset = Mathf.Min(amount, Mathf.Min(rect.width, rect.height) * .45f);
            return new Rect(rect.x + inset, rect.y + inset,
                Mathf.Max(0f, rect.width - inset * 2f),
                Mathf.Max(0f, rect.height - inset * 2f));
        }

        public static Rect FitInside(Rect bounds, Vector2 source)
        {
            if (bounds.width <= 0f || bounds.height <= 0f ||
                source.x <= 0f || source.y <= 0f ||
                float.IsNaN(source.x) || float.IsInfinity(source.x) ||
                float.IsNaN(source.y) || float.IsInfinity(source.y))
                return new Rect(bounds.center, Vector2.zero);
            float scale = Mathf.Min(bounds.width / source.x,
                bounds.height / source.y);
            float width = source.x * scale;
            float height = source.y * scale;
            return new Rect(bounds.center.x - width * .5f,
                bounds.center.y - height * .5f, width, height);
        }

        public static void DrawSprite(Sprite portrait, Rect frame)
        {
            if (Event.current == null ||
                Event.current.type != EventType.Repaint || portrait == null)
                return;
            Texture texture = portrait.texture;
            if (texture == null) return;
            Rect part = portrait.rect;
            Rect image = FitInside(frame, part.size);
            if (image.width <= 0f || image.height <= 0f) return;
            GUI.DrawTextureWithTexCoords(image, texture,
                new Rect(part.x / texture.width, part.y / texture.height,
                    part.width / texture.width, part.height / texture.height));
        }

        public static void DrawFramedSprite(Sprite portrait, Rect frame,
            Color borderTint)
        {
            if (Event.current == null ||
                Event.current.type != EventType.Repaint) return;
            float inset = Mathf.Max(2f, Mathf.Min(frame.width, frame.height) * .055f);
            DrawSprite(portrait, Inset(frame, inset));
            Color previous = GUI.color;
            try
            {
                GUI.color = previous * borderTint;
                DrawSprite(OrdrBattlefieldArt.GradeFrame, frame);
            }
            finally { GUI.color = previous; }
        }
    }
}
