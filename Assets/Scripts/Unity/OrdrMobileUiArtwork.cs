using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ordr.Mobile.Unity
{
    // Presentation-only assets. Source PNGs stay unchanged; small cached faces
    // remove transparent generation margins and keep text out of the artwork.
    internal static class OrdrMobileUiArtwork
    {
        internal enum Face { WideButton, SquareButton, Popup, Speech }

        private sealed class Pixels
        {
            internal int Width, Height;
            internal Color32[] Data;
        }

        private static readonly Dictionary<Face, Pixels> sources = new Dictionary<Face, Pixels>();
        private static readonly Dictionary<string, Texture2D> variants = new Dictionary<string, Texture2D>();

        internal static string ResourcePath(Face face)
        {
            switch (face)
            {
                case Face.WideButton: return "OrdrArt/Ui/WideButton";
                case Face.SquareButton: return "OrdrArt/Ui/SquareButton";
                case Face.Popup: return "OrdrArt/Ui/PopupPanel";
                case Face.Speech: return "OrdrArt/Ui/SpeechBubble";
                default: throw new ArgumentOutOfRangeException(nameof(face));
            }
        }

        private static Pixels Load(Face face)
        {
            Pixels cached;
            if (sources.TryGetValue(face, out cached)) return cached;
            // Cache missing assets too; never retry a failed load every OnGUI.
            sources.Add(face, null);
            Texture2D source = Resources.Load<Texture2D>(ResourcePath(face));
            if (source == null) return null;
            try
            {
                if (!source.isReadable)
                {
                    Debug.LogWarning("ORDR UI artwork requires Read/Write import: " + ResourcePath(face));
                    return null;
                }
                Color32[] input = source.GetPixels32();
                int left = source.width, bottom = source.height, right = -1, top = -1;
                for (int y = 0; y < source.height; y++)
                for (int x = 0; x < source.width; x++)
                {
                    // Ignore almost-transparent generation noise outside the
                    // face. Retain a small feather around its visible edge.
                    if (input[y * source.width + x].a < 32) continue;
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    bottom = Math.Min(bottom, y); top = Math.Max(top, y);
                }
                if (right < left || top < bottom) return null;
                left = Math.Max(0, left - 2); bottom = Math.Max(0, bottom - 2);
                right = Math.Min(source.width - 1, right + 2);
                top = Math.Min(source.height - 1, top + 2);
                int cropWidth = right - left + 1, cropHeight = top - bottom + 1;
                int longest = face == Face.SquareButton ? 96 : 384;
                float scale = Math.Min(1f, longest / (float)Math.Max(cropWidth, cropHeight));
                var pixels = new Pixels {
                    Width = Math.Max(1, Mathf.RoundToInt(cropWidth * scale)),
                    Height = Math.Max(1, Mathf.RoundToInt(cropHeight * scale))
                };
                pixels.Data = new Color32[pixels.Width * pixels.Height];
                for (int y = 0; y < pixels.Height; y++)
                for (int x = 0; x < pixels.Width; x++)
                {
                    float sx = left + (x + .5f) * cropWidth / pixels.Width - .5f;
                    float sy = bottom + (y + .5f) * cropHeight / pixels.Height - .5f;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(sx), left, right);
                    int y0 = Mathf.Clamp(Mathf.FloorToInt(sy), bottom, top);
                    int x1 = Math.Min(x0 + 1, right), y1 = Math.Min(y0 + 1, top);
                    Color row0 = Color.Lerp(input[y0 * source.width + x0],
                        input[y0 * source.width + x1], sx - x0);
                    Color row1 = Color.Lerp(input[y1 * source.width + x0],
                        input[y1 * source.width + x1], sy - y0);
                    pixels.Data[y * pixels.Width + x] = Color.Lerp(row0, row1, sy - y0);
                }
                sources[face] = pixels;
                return pixels;
            }
            finally
            {
                // Only the compact pixel cache and immutable tinted faces live
                // with the UI; do not retain four full-size readable PNGs.
                Resources.UnloadAsset(source);
            }
        }

        private static Texture2D Variant(Face face, Color tint, float brightness)
        {
            Color32 color = tint;
            string key = face + ":" + color.r + ":" + color.g + ":" + color.b + ":" + brightness;
            Texture2D texture;
            if (variants.TryGetValue(key, out texture)) return texture;
            Pixels source = Load(face);
            if (source == null) return null;
            var output = new Color32[source.Data.Length];
            for (int i = 0; i < output.Length; i++)
            {
                Color pixel = source.Data[i];
                pixel.r *= tint.r * brightness;
                pixel.g *= tint.g * brightness;
                pixel.b *= tint.b * brightness;
                output[i] = pixel; // Preserve source transparency, including the speech tail.
            }
            texture = new Texture2D(source.Width, source.Height, TextureFormat.RGBA32, false);
            texture.name = "ORDR UI " + key;
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(output);
            texture.Apply(false, true);
            variants.Add(key, texture);
            return texture;
        }

        private static void Set(GUIStyleState state, Texture2D texture)
        {
            state.background = texture;
            state.scaledBackgrounds = new Texture2D[0];
        }

        internal static void Apply(GUIStyle style, Face face, Color tint, bool interactive)
        {
            Texture2D normal = Variant(face, tint, 1f);
            if (normal == null) return; // Preserve the existing usable style on missing imports.
            Texture2D hover = interactive ? Variant(face, tint, 1.18f) : normal;
            Texture2D pressed = interactive ? Variant(face, tint, .72f) : normal;
            Set(style.normal, normal); Set(style.hover, hover); Set(style.active, pressed);
            Set(style.focused, hover); Set(style.onNormal, normal); Set(style.onHover, hover);
            Set(style.onActive, pressed); Set(style.onFocused, hover);
            int corner = Math.Max(1, Mathf.RoundToInt(Math.Min(normal.width, normal.height) * .18f));
            if (face == Face.Speech)
                // Keep the left-hand tail and lower edge in fixed slices.
                style.border = new RectOffset(Mathf.RoundToInt(normal.width * .24f),
                    corner, corner, Mathf.RoundToInt(normal.height * .35f));
            else style.border = new RectOffset(corner, corner, corner, corner);
        }
    }
}
