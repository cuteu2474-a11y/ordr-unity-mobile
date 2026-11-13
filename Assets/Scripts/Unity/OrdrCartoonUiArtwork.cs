using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ordr.Mobile.Unity
{
    // Shared visual adapter for the approved cartoon pirate UI pack.
    // Touch targets, action dispatch, economy and combat stay in their existing owners.
    internal static class OrdrCartoonUiArtwork
    {
        internal enum Face { TealWide, OrangeWide, Square, Parchment }

        private sealed class Pixels
        {
            internal int Width;
            internal int Height;
            internal Color32[] Data;
        }

        private static readonly Dictionary<Face, Pixels> sources = new Dictionary<Face, Pixels>();
        private static readonly Dictionary<int, Texture2D> variants = new Dictionary<int, Texture2D>();

        internal static string ResourcePath(Face face)
        {
            switch (face)
            {
                case Face.TealWide: return "OrdrArt/CartoonUi/TealWide";
                case Face.OrangeWide: return "OrdrArt/CartoonUi/OrangeWide";
                case Face.Square: return "OrdrArt/CartoonUi/Square";
                case Face.Parchment: return "OrdrArt/CartoonUi/Parchment";
                default: throw new ArgumentOutOfRangeException(nameof(face));
            }
        }

        private static Pixels Load(Face face)
        {
            Pixels cached;
            if (sources.TryGetValue(face, out cached))
                return cached;

            // Remember missing/unreadable imports to avoid repeated loads during OnGUI.
            sources.Add(face, null);
            Texture2D original = Resources.Load<Texture2D>(ResourcePath(face));
            if (original == null)
            {
                Debug.LogWarning("ORDR cartoon UI image missing: " + ResourcePath(face));
                return null;
            }
            try
            {
                if (!original.isReadable)
                {
                    Debug.LogWarning("ORDR cartoon UI image must be imported with Read/Write: " + ResourcePath(face));
                    return null;
                }

                Color32[] input = original.GetPixels32();
                int left = original.width, bottom = original.height, right = -1, top = -1;
                for (int y = 0; y < original.height; y++)
                for (int x = 0; x < original.width; x++)
                {
                    if (input[y * original.width + x].a < 24)
                        continue;
                    left = Math.Min(left, x);
                    bottom = Math.Min(bottom, y);
                    right = Math.Max(right, x);
                    top = Math.Max(top, y);
                }
                if (right < left || top < bottom)
                    return null;
                left = Math.Max(0, left - 2);
                right = Math.Min(original.width - 1, right + 2);
                bottom = Math.Max(0, bottom - 2);
                top = Math.Min(original.height - 1, top + 2);

                int width = right - left + 1, height = top - bottom + 1;
                int longest = face == Face.Square ? 128 :
                    face == Face.Parchment ? 512 : 384;
                float scale = Math.Min(1f, longest / (float)Math.Max(width, height));
                var pixels = new Pixels
                {
                    Width = Math.Max(1, Mathf.RoundToInt(width * scale)),
                    Height = Math.Max(1, Mathf.RoundToInt(height * scale))
                };
                pixels.Data = new Color32[pixels.Width * pixels.Height];

                // Sample the approved PNG once into a compact GPU-friendly face.
                for (int y = 0; y < pixels.Height; y++)
                for (int x = 0; x < pixels.Width; x++)
                {
                    float sx = left + (x + .5f) * width / pixels.Width - .5f;
                    float sy = bottom + (y + .5f) * height / pixels.Height - .5f;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(sx), left, right);
                    int y0 = Mathf.Clamp(Mathf.FloorToInt(sy), bottom, top);
                    int x1 = Math.Min(x0 + 1, right), y1 = Math.Min(y0 + 1, top);
                    Color lower = Color.Lerp(input[y0 * original.width + x0],
                        input[y0 * original.width + x1], sx - x0);
                    Color upper = Color.Lerp(input[y1 * original.width + x0],
                        input[y1 * original.width + x1], sx - x0);
                    pixels.Data[y * pixels.Width + x] = Color.Lerp(lower, upper, sy - y0);
                }

                sources[face] = pixels;
                return pixels;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("ORDR cartoon UI import failed for " + ResourcePath(face) + ": " + exception.Message);
                return null;
            }
            finally
            {
                // Only the trimmed compact faces remain in memory.
                Resources.UnloadAsset(original);
            }
        }

        private static Texture2D Variant(Face face, int state)
        {
            int key = (int)face * 3 + state;
            Texture2D existing;
            if (variants.TryGetValue(key, out existing))
                return existing;
            Pixels pixels = Load(face);
            if (pixels == null)
                return null;

            float brightness = state == 1 ? 1.12f : state == 2 ? .77f : 1f;
            bool parchment = face == Face.Parchment;
            var colored = new Color32[pixels.Data.Length];
            for (int i = 0; i < colored.Length; i++)
            {
                Color color = pixels.Data[i];
                if (parchment)
                {
                    // Light labels are already used inside every confirmation
                    // and help panel. Darken the paper instead of changing them.
                    color.r *= .43f;
                    color.g *= .34f;
                    color.b *= .25f;
                }
                color.r = Mathf.Clamp01(color.r * brightness);
                color.g = Mathf.Clamp01(color.g * brightness);
                color.b = Mathf.Clamp01(color.b * brightness);
                colored[i] = color; // Keep the original transparent silhouette.
            }
            Texture2D texture = new Texture2D(pixels.Width, pixels.Height,
                TextureFormat.RGBA32, false);
            texture.name = "ORDR Cartoon UI " + face + " state " + state;
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(colored);
            texture.Apply(false, true);
            variants.Add(key, texture);
            return texture;
        }

        internal static bool Apply(GUIStyle style, Face face, bool interactive)
        {
            if (style == null)
                return false;
            Texture2D normal = Variant(face, 0);
            if (normal == null)
                return false; // Retain the existing functional IMGUI style.

            Texture2D hover = interactive ? Variant(face, 1) : normal;
            Texture2D pressed = interactive ? Variant(face, 2) : normal;
            style.normal.background = normal;
            style.hover.background = hover;
            style.active.background = pressed;
            style.focused.background = hover;
            style.onNormal.background = normal;
            style.onHover.background = hover;
            style.onActive.background = pressed;
            style.onFocused.background = hover;

            // Nine-slice keeps corner bevels and decorative gold trim intact
            // when the existing layout resizes controls on narrow screens.
            int bx = face == Face.Square ? Math.Min(18, normal.width / 6) :
                face == Face.Parchment ? Math.Min(42, normal.width / 9) :
                Math.Min(36, normal.width / 9);
            int by = face == Face.Square ? Math.Min(18, normal.height / 6) :
                face == Face.Parchment ? Math.Min(26, normal.height / 10) :
                Math.Min(19, normal.height / 5);
            style.border = new RectOffset(Math.Max(1, bx), Math.Max(1, bx),
                Math.Max(1, by), Math.Max(1, by));

            if (interactive)
            {
                // Text remains dynamic (cost, count, selected state, cooldown).
                style.normal.textColor = Color.white;
                style.hover.textColor = Color.white;
                style.active.textColor = Color.white;
                style.focused.textColor = Color.white;
                style.onNormal.textColor = Color.white;
                style.onHover.textColor = Color.white;
                style.onActive.textColor = Color.white;
                style.onFocused.textColor = Color.white;
            }
            return true;
        }
    }
}
