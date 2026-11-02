using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ordr.Mobile.Unity
{
    // Small authored-at-runtime visual assets: no downloaded IP or device
    // network traffic. Generated once, cached, and used only for rendering.
    public static class OrdrBattlefieldArt
    {
        private static Sprite board;
        private static Sprite gradeFrame;
        private static Sprite enemy;
        private static Sprite boss;
        private static Sprite evolution;
        private static Texture2D story;
        private static Texture2D header;
        private static readonly Dictionary<string, Sprite> units =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        public static Color BackgroundColor => new Color(0.035f, 0.070f, 0.110f, 1f);
        public static Sprite BoardSprite => board ?? (board = MakeSprite(BoardTexture(), 256));
        public static Sprite GradeFrame => gradeFrame ?? (gradeFrame = MakeSprite(FrameTexture(), 96));
        public static Sprite EnemySprite(bool isBoss)
        {
            if (isBoss) return boss ?? (boss = MakeSprite(HazardTexture(true), 80));
            return enemy ?? (enemy = MakeSprite(HazardTexture(false), 80));
        }
        public static Sprite EvolutionSprite =>
            evolution ?? (evolution = MakeSprite(EvolutionTexture(), 80));
        public static Texture2D StoryTexture =>
            story ?? (story = MakeTexture(256, 96, (x, y) =>
            {
                float g = 0.5f + 0.5f * Mathf.Sin(x * .07f);
                float edge = (x < 3 || x > 252 || y < 3 || y > 92) ? 1f : 0f;
                float line = (x % 32 == 0 || y % 24 == 0) ? .20f : 0f;
                float shade = 0.78f + 0.20f * (y / 96f);
                Color c = new Color(.075f, .13f, .19f, 1f) * shade;
                c = Color.Lerp(c, new Color(.13f, .28f, .31f), line + .12f * g);
                if (edge > 0) c = new Color(.42f, .64f, .67f, 1f);
                c.a = 1f;
                return c;
            }));
        public static Texture2D HeaderTexture =>
            header ?? (header = MakeTexture(256, 36, (x, y) =>
            {
                float edge = (y < 2 || y > 33) ? 1f : 0f;
                Color c = Color.Lerp(new Color(.038f, .072f, .12f, .96f),
                    new Color(.13f, .20f, .28f, .96f), y / 36f);
                if (edge > 0) c = new Color(.58f, .43f, .24f, .99f);
                if ((x % 52 == 3 || x % 52 == 4) && (y == 5 || y == 30))
                    c = new Color(.85f, .69f, .35f, 1f);
                return c;
            }));

        public static Sprite UnitSprite(string sourceCatalogId)
        {
            if (string.IsNullOrEmpty(sourceCatalogId)) return null;
            Sprite sprite;
            if (units.TryGetValue(sourceCatalogId, out sprite)) return sprite;
            uint hash = 2166136261u;
            foreach (char ch in sourceCatalogId)
                hash = unchecked((hash ^ ch) * 16777619u);
            int variant = (int)(hash % 4);
            float hue = ((hash >> 8) % 1000) / 1000f;
            Color costume = Color.HSVToRGB(.50f + .32f * hue, .55f, .67f);
            Color sky = Color.Lerp(new Color(.08f, .18f, .30f),
                new Color(.22f, .14f, .29f), hue);
            var texture = MakeTexture(96, 96, (x, y) =>
            {
                float xf = x - 47.5f, yf = y - 47.5f;
                float glow = Mathf.Clamp01(1f - Mathf.Abs(xf) / 60f -
                    Mathf.Abs(yf - 15f) / 115f);
                Color c = Color.Lerp(new Color(.032f, .061f, .102f), sky, glow);
                if (x % 24 == 0 || y % 24 == 0)
                    c = Color.Lerp(c, new Color(.31f, .43f, .48f), .18f);
                float emblem = Mathf.Sqrt((xf - 20f) * (xf - 20f) +
                    (yf - 23f) * (yf - 23f));
                if (emblem > 23f && emblem < 25f)
                    c = Color.Lerp(c, new Color(.59f, .69f, .66f), .40f);
                // Deliberately generic illustrated bust, not a claimed
                // likeness. A private approved portrait replaces this sprite.
                bool coat = y < 43 && Mathf.Abs(xf) < 28f - .13f * y &&
                    (xf * xf / 900f + (y - 9f) * (y - 9f) / 1600f < 1.3f);
                bool neck = y >= 39 && y < 54 && Mathf.Abs(xf) < 8f;
                bool head = xf * xf / 255f + (y - 60f) * (y - 60f) / 342f < 1f;
                bool hair = head && (y > 70f - (variant == 0 ? 5f : 0f) ||
                    (variant == 2 && xf < -9f));
                bool hat = variant == 3 && y >= 78 && y <= 83 &&
                    Mathf.Abs(xf) < 27f;
                if (coat) c = Color.Lerp(costume, new Color(.05f, .12f, .18f),
                    Mathf.Abs(xf) / 36f);
                if (neck || head) c = new Color(.77f, .60f, .46f);
                if (hair || hat) c = Color.Lerp(costume,
                    new Color(.14f, .13f, .20f), .75f);
                if (head && (y == 60 || y == 61) &&
                    (Mathf.Abs(xf - 6f) < 2 || Mathf.Abs(xf + 6f) < 2))
                    c = new Color(.16f, .20f, .23f);
                if (coat && Mathf.Abs(xf) < 3f && y < 29)
                    c = new Color(.76f, .67f, .43f);
                // Thin highlight ensures the icon reads as a square tile.
                if (x < 2 || x > 93 || y < 2 || y > 93)
                    c = new Color(.27f, .39f, .43f);
                return c;
            });
            sprite = MakeSprite(texture, 96);
            units.Add(sourceCatalogId, sprite);
            return sprite;
        }

        private static Texture2D BoardTexture()
        {
            return MakeTexture(256, 256, (x, y) =>
            {
                float grain = Mathf.PerlinNoise(x * .033f, y * .047f) * .12f;
                float shade = .79f + .24f * (y / 256f) + grain;
                Color color = new Color(.063f, .13f, .18f) * shade;
                bool grid = x % 32 == 0 || y % 32 == 0;
                if (grid) color = Color.Lerp(color,
                    new Color(.21f, .35f, .38f), .22f);
                float lane = 120f + 17f * Mathf.Sin(x * .029f + 1f);
                if (Mathf.Abs(y - lane) < 12f)
                    color = Color.Lerp(color, new Color(.18f, .35f, .34f), .29f);
                if (Mathf.Abs(y - lane) < 1.7f)
                    color = Color.Lerp(color, new Color(.56f, .68f, .48f), .35f);
                // Faint nautical compass-rose chart embellishment.
                float dx = x - 218f, dy = y - 43f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                if (Mathf.Abs(distance - 26f) < 1.25f ||
                    ((Mathf.Abs(dx) < 1.5f || Mathf.Abs(dy) < 1.5f) && distance < 30f))
                    color = Color.Lerp(color, new Color(.81f, .65f, .40f), .47f);
                if (x < 4 || x > 251 || y < 4 || y > 251)
                    color = new Color(.40f, .34f, .21f);
                if ((x < 9 || x > 246 || y < 9 || y > 246) &&
                    (x % 15 == 0 || y % 15 == 0))
                    color = Color.Lerp(color, new Color(.83f, .62f, .33f), .6f);
                color.a = 1f;
                return color;
            });
        }

        private static Texture2D FrameTexture()
        {
            return MakeTexture(96, 96, (x, y) =>
            {
                int d = Math.Min(Math.Min(x, y), Math.Min(95 - x, 95 - y));
                bool corners = (x < 15 || x > 80) && (y < 15 || y > 80);
                if (d < 3) return new Color(1f, .96f, .72f, 1f);
                if (d < 7) return new Color(.70f, .79f, .83f, .85f);
                if (corners && d < 13)
                    return new Color(.93f, .92f, .79f, .91f);
                return Color.clear;
            });
        }

        private static Texture2D HazardTexture(bool isBoss)
        {
            return MakeTexture(80, 80, (x, y) =>
            {
                float dx = Mathf.Abs(x - 39.5f), dy = Mathf.Abs(y - 39.5f);
                float manhattan = dx * .87f + dy;
                if (manhattan > 38f) return Color.clear;
                if (manhattan > 32f) return new Color(1f, .89f, .70f, 1f);
                Color dark = isBoss ? new Color(.40f, .15f, .25f) :
                    new Color(.25f, .17f, .18f);
                if (dy < 5f && dx < 19f) return Color.white;
                if (dy > 11f && dx < 4f) return new Color(1f, .84f, .70f);
                return dark;
            });
        }

        private static Texture2D EvolutionTexture()
        {
            return MakeTexture(80, 80, (x, y) =>
            {
                float dx = x - 39.5f, dy = y - 39.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > 38f || d < 12f && Mathf.Abs(dx) > 5f) return Color.clear;
                if (d > 31f) return new Color(.73f, .52f, 1f, .73f);
                if (Mathf.Abs(dx) < 4f && Mathf.Abs(dy) < 24f)
                    return new Color(.90f, .88f, 1f);
                if (Mathf.Abs(dy) < 4f && Mathf.Abs(dx) < 24f)
                    return new Color(.70f, .92f, 1f);
                return new Color(.25f, .18f, .49f, .88f);
            });
        }

        private static Texture2D MakeTexture(int w, int h, Func<int, int, Color> colorAt)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.name = "ORDR authored tactical art " + w + "x" + h;
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.hideFlags = HideFlags.DontSave;
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = colorAt(x, y);
            t.SetPixels(pixels);
            t.Apply(false, true);
            return t;
        }

        private static Sprite MakeSprite(Texture2D texture, int pixelsPerUnit)
        {
            var sprite = Sprite.Create(texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(.5f, .5f), pixelsPerUnit);
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
