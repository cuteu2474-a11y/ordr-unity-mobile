using UnityEngine;

namespace Ordr.Mobile.Unity
{
    // Presentation-only IMGUI theme. Does not affect game data, hit testing,
    // battle geometry, resources, action dispatch or camera viewports.
    internal static class OrdrMobileUiTheme
    {
        internal static readonly Color Gold = new Color(0.97f, 0.74f, 0.35f, 1f);
        private static readonly Color Text = new Color(0.91f, 0.95f, 1f, 1f);
        private static readonly Color Muted = new Color(0.72f, 0.83f, 0.94f, 1f);
        private static GUISkin themedSkin;
        private static GUIStyle selectedTab;
        private static GUIStyle primaryButton;
        private static GUIStyle sectionTitle;
        private static GUIStyle currencyText;

        internal static GUIStyle SelectedTab { get { return selectedTab; } }
        internal static GUIStyle PrimaryButton { get { return primaryButton; } }
        internal static GUIStyle SectionTitle { get { return sectionTitle; } }
        internal static GUIStyle CurrencyText { get { return currencyText; } }

        // Called only inside OnGUI. Return the previous skin so owners restore
        // it in finally, avoiding style leakage into other game components.
        internal static GUISkin Activate()
        {
            GUISkin previous = GUI.skin;
            if (themedSkin == null)
                Initialize(previous);
            GUI.skin = themedSkin;
            return previous;
        }

        private static void Initialize(GUISkin source)
        {
            themedSkin = Object.Instantiate(source);
            themedSkin.name = "ORDR Nightfall Touch Theme";
            themedSkin.hideFlags = HideFlags.HideAndDontSave;

            Texture2D card = Rounded(new Color(0.095f, 0.15f, 0.23f, 0.96f));
            Texture2D button = Rounded(new Color(0.15f, 0.23f, 0.34f, 0.98f));
            Texture2D hover = Rounded(new Color(0.22f, 0.32f, 0.44f, 0.99f));
            Texture2D gold = Rounded(new Color(0.43f, 0.32f, 0.17f, 0.99f));
            Texture2D teal = Rounded(new Color(0.07f, 0.54f, 0.49f, 1f));
            Texture2D tealHover = Rounded(new Color(0.10f, 0.65f, 0.57f, 1f));

            themedSkin.box = new GUIStyle(source.box);
            Background(themedSkin.box, card, card, Text);
            themedSkin.box.border = new RectOffset(8, 8, 8, 8);
            themedSkin.box.padding = new RectOffset(12, 12, 10, 10);
            themedSkin.box.fontSize = 14;

            themedSkin.button = new GUIStyle(source.button);
            Background(themedSkin.button, button, hover, Text);
            themedSkin.button.border = new RectOffset(8, 8, 8, 8);
            themedSkin.button.padding = new RectOffset(8, 8, 9, 9);
            themedSkin.button.fontStyle = FontStyle.Bold;
            themedSkin.button.fontSize = 15;
            themedSkin.button.alignment = TextAnchor.MiddleCenter;
            themedSkin.button.wordWrap = true;

            themedSkin.label = new GUIStyle(source.label);
            themedSkin.label.normal.textColor = Text;
            themedSkin.label.fontSize = 14;
            themedSkin.label.wordWrap = true;
            themedSkin.label.richText = true;

            themedSkin.textField = new GUIStyle(source.textField);
            Background(themedSkin.textField, button, hover, Text);
            themedSkin.textField.border = new RectOffset(8, 8, 8, 8);
            themedSkin.textField.padding = new RectOffset(8, 8, 6, 6);
            themedSkin.textField.fontSize = 14;
            themedSkin.textArea = new GUIStyle(themedSkin.textField);

            selectedTab = new GUIStyle(themedSkin.button);
            Background(selectedTab, gold, gold, Color.white);
            selectedTab.fontStyle = FontStyle.Bold;

            primaryButton = new GUIStyle(themedSkin.button);
            Background(primaryButton, teal, tealHover, Color.white);
            primaryButton.fontSize = 16;

            sectionTitle = new GUIStyle(themedSkin.label);
            sectionTitle.normal.textColor = Gold;
            sectionTitle.fontSize = 19;
            sectionTitle.fontStyle = FontStyle.Bold;
            sectionTitle.alignment = TextAnchor.MiddleLeft;
            sectionTitle.wordWrap = false;

            currencyText = new GUIStyle(themedSkin.label);
            currencyText.normal.textColor = Muted;
            currencyText.fontStyle = FontStyle.Bold;
            currencyText.fontSize = 15;
        }

        private static void Background(GUIStyle style, Texture2D normal,
            Texture2D highlighted, Color textColor)
        {
            style.normal.background = normal;
            style.hover.background = highlighted;
            style.active.background = highlighted;
            style.focused.background = normal;
            style.onNormal.background = highlighted;
            style.onHover.background = highlighted;
            style.onActive.background = highlighted;
            style.onFocused.background = highlighted;
            style.normal.textColor = textColor;
            style.hover.textColor = textColor;
            style.active.textColor = textColor;
            style.focused.textColor = textColor;
            style.onNormal.textColor = textColor;
            style.onHover.textColor = textColor;
            style.onActive.textColor = textColor;
            style.onFocused.textColor = textColor;
        }

        // 9-slice rounded corner pixel mask, no external assets or fonts.
        private static Texture2D Rounded(Color color)
        {
            const int size = 24;
            const float radius = 7f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0f, Mathf.Max(radius - (x + 0.5f),
                    x + 0.5f - (size - radius)));
                float dy = Mathf.Max(0f, Mathf.Max(radius - (y + 0.5f),
                    y + 0.5f - (size - radius)));
                float opacity = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                texture.SetPixel(x, y, new Color(color.r, color.g, color.b,
                    color.a * opacity));
            }
            texture.Apply(false, true);
            return texture;
        }
    }
}
