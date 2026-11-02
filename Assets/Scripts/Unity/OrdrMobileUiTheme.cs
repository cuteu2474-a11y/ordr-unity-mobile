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
        private static GUIStyle panel;
        private static GUIStyle navButton;
        private static GUIStyle overline;
        private static GUIStyle subheading;
        private static GUIStyle skillButton;
        private static GUIStyle explanation;
        private static GUIStyle facilityHeading;
        private static GUIStyle facilityBadge;
        private static GUIStyle researchNumber;
        private static GUIStyle researchName;
        private static GUIStyle researchDetail;
        private static GUIStyle accordionClosed;
        private static GUIStyle accordionOpen;
        private static GUIStyle accordionTitle;
        private static GUIStyle accordionMeta;
        private static GUIStyle accordionArrow;
        private static GUIStyle accordionBody;

        internal static GUIStyle SelectedTab { get { return selectedTab; } }
        internal static GUIStyle PrimaryButton { get { return primaryButton; } }
        internal static GUIStyle SectionTitle { get { return sectionTitle; } }
        internal static GUIStyle CurrencyText { get { return currencyText; } }
        internal static GUIStyle Panel { get { return panel; } }
        internal static GUIStyle NavButton { get { return navButton; } }
        internal static GUIStyle Overline { get { return overline; } }
        internal static GUIStyle Subheading { get { return subheading; } }
        internal static GUIStyle SkillButton { get { return skillButton; } }
        internal static GUIStyle Explanation { get { return explanation; } }
        internal static GUIStyle AccordionBody { get { return accordionBody; } }
        internal const float ComfortableTapHeight = 56f;
        internal const float PrimaryTapHeight = 64f;
        internal static readonly Color Aqua = new Color(0.31f, 0.87f, 0.78f, 1f);

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

            Texture2D card = Plate(new Color(.09f,.15f,.24f),new Color(.035f,.065f,.13f),new Color(.38f,.46f,.52f));
            Texture2D button = Plate(new Color(.17f,.27f,.38f),new Color(.10f,.16f,.25f),new Color(.43f,.54f,.64f));
            Texture2D hover = Plate(new Color(.24f,.37f,.49f),new Color(.13f,.24f,.35f),new Color(.49f,.76f,.79f));
            Texture2D gold = Plate(new Color(.55f,.38f,.20f),new Color(.32f,.20f,.10f),new Color(.99f,.75f,.36f));
            Texture2D teal = Plate(new Color(.13f,.63f,.54f),new Color(.06f,.35f,.31f),new Color(.45f,.96f,.84f));
            Texture2D tealHover = Plate(new Color(.19f,.72f,.62f),new Color(.09f,.44f,.39f),new Color(.64f,1f,.88f));

            themedSkin.box = new GUIStyle(source.box);
            Background(themedSkin.box, card, card, Text);
            themedSkin.box.border = new RectOffset(9, 9, 9, 9);
            themedSkin.box.padding = new RectOffset(12, 12, 10, 10);
            themedSkin.box.fontSize = 17;

            themedSkin.button = new GUIStyle(source.button);
            Background(themedSkin.button, button, hover, Text);
            themedSkin.button.border = new RectOffset(9, 9, 9, 9);
            themedSkin.button.padding = new RectOffset(8, 8, 9, 9);
            themedSkin.button.fontStyle = FontStyle.Bold;
            themedSkin.button.fontSize = 19;
            themedSkin.button.alignment = TextAnchor.MiddleCenter;
            themedSkin.button.wordWrap = true;
            themedSkin.button.clipping = TextClipping.Clip;
            themedSkin.button.margin = new RectOffset(3, 3, 4, 4);

            themedSkin.label = new GUIStyle(source.label);
            themedSkin.label.normal.textColor = Text;
            themedSkin.label.fontSize = 18;
            themedSkin.label.wordWrap = true;
            themedSkin.label.richText = true;

            themedSkin.textField = new GUIStyle(source.textField);
            Background(themedSkin.textField, button, hover, Text);
            themedSkin.textField.border = new RectOffset(8, 8, 8, 8);
            themedSkin.textField.padding = new RectOffset(8, 8, 6, 6);
            themedSkin.textField.fontSize = 19;
            themedSkin.textArea = new GUIStyle(themedSkin.textField);

            selectedTab = new GUIStyle(themedSkin.button);
            Background(selectedTab, gold, gold, Color.white);
            selectedTab.fontStyle = FontStyle.Bold;

            primaryButton = new GUIStyle(themedSkin.button);
            Background(primaryButton, teal, tealHover, Color.white);
            primaryButton.fontSize = 20;

            sectionTitle = new GUIStyle(themedSkin.label);
            sectionTitle.normal.textColor = Gold;
            sectionTitle.fontSize = 23;
            sectionTitle.fontStyle = FontStyle.Bold;
            sectionTitle.alignment = TextAnchor.MiddleLeft;
            sectionTitle.wordWrap = true;

            currencyText = new GUIStyle(themedSkin.label);
            currencyText.normal.textColor = Muted;
            currencyText.fontStyle = FontStyle.Bold;
            currencyText.fontSize = 18;
            panel = new GUIStyle(themedSkin.box);
            panel.padding = new RectOffset(12,12,11,11);
            navButton = new GUIStyle(themedSkin.button);
            navButton.alignment = TextAnchor.MiddleCenter;
            navButton.padding = new RectOffset(8,8,7,7);
            navButton.wordWrap = false;
            overline = new GUIStyle(themedSkin.label);
            overline.fontSize = 13;
            overline.fontStyle = FontStyle.Bold;
            overline.wordWrap = false;
            overline.clipping = TextClipping.Clip;
            overline.normal.textColor = new Color(.61f,.78f,.87f,1f);
            subheading = new GUIStyle(themedSkin.label);
            subheading.fontSize = 21;
            subheading.fontStyle = FontStyle.Bold;
            subheading.normal.textColor = Gold;
            skillButton = new GUIStyle(themedSkin.button);
            skillButton.fontSize = 17;
            skillButton.wordWrap = true;
            skillButton.normal.textColor = Aqua;
            explanation = new GUIStyle(themedSkin.label);
            explanation.fontSize = 19;
            explanation.fontStyle = FontStyle.Normal;
            explanation.wordWrap = true;
            explanation.normal.textColor = new Color(0.92f,0.97f,1f,1f);
            facilityHeading = new GUIStyle(sectionTitle);
            facilityHeading.fontSize = 27;
            facilityHeading.normal.textColor = Color.white;
            facilityBadge = new GUIStyle(overline);
            facilityBadge.fontSize = 14;
            facilityBadge.alignment = TextAnchor.MiddleRight;
            facilityBadge.normal.textColor = Gold;
            researchNumber = new GUIStyle(subheading);
            researchNumber.fontSize = 19;
            researchNumber.alignment = TextAnchor.MiddleCenter;
            researchNumber.normal.textColor = Color.white;
            researchName = new GUIStyle(subheading);
            researchName.fontSize = 21;
            researchName.normal.textColor = Color.white;
            researchName.wordWrap = true;
            researchDetail = new GUIStyle(explanation);
            researchDetail.fontSize = 17;
            researchDetail.normal.textColor = Muted;
            Texture2D folded = Plate(new Color(.15f,.23f,.34f),
                new Color(.10f,.17f,.27f),new Color(.40f,.54f,.65f));
            Texture2D foldedHover = Plate(new Color(.21f,.32f,.44f),
                new Color(.13f,.23f,.34f),new Color(.56f,.76f,.83f));
            Texture2D unfolded = Plate(new Color(.22f,.30f,.40f),
                new Color(.14f,.21f,.31f),new Color(.95f,.72f,.36f));
            accordionClosed = new GUIStyle(themedSkin.button);
            Background(accordionClosed, folded, foldedHover, Color.white);
            accordionClosed.border = new RectOffset(9,9,9,9);
            accordionClosed.padding = new RectOffset(0,0,0,0);
            accordionClosed.margin = new RectOffset(4,4,5,5);
            accordionOpen = new GUIStyle(accordionClosed);
            Background(accordionOpen, unfolded, unfolded, Color.white);
            accordionTitle = new GUIStyle(subheading);
            accordionTitle.fontSize = 21;
            accordionTitle.wordWrap = false;
            accordionTitle.clipping = TextClipping.Clip;
            accordionTitle.normal.textColor = Color.white;
            accordionMeta = new GUIStyle(researchDetail);
            accordionMeta.fontSize = 16;
            accordionMeta.alignment = TextAnchor.MiddleRight;
            accordionMeta.wordWrap = false;
            accordionMeta.clipping = TextClipping.Clip;
            accordionArrow = new GUIStyle(accordionTitle);
            accordionArrow.fontSize = 20;
            accordionArrow.alignment = TextAnchor.MiddleCenter;
            accordionBody = new GUIStyle(themedSkin.box);
            accordionBody.margin = new RectOffset(7,7,0,12);
            accordionBody.padding = new RectOffset(14,14,14,16);
            themedSkin.toggle = new GUIStyle(source.toggle);
            themedSkin.toggle.fontSize = 18;
            themedSkin.toggle.wordWrap = true;
            themedSkin.toggle.normal.textColor = Text;
            themedSkin.toggle.onNormal.textColor = Text;
            themedSkin.verticalScrollbar = new GUIStyle(source.verticalScrollbar);
            themedSkin.verticalScrollbar.fixedWidth = 22f;
            themedSkin.verticalScrollbarThumb = new GUIStyle(source.verticalScrollbarThumb);
            themedSkin.verticalScrollbarThumb.fixedHeight = 0f;
            themedSkin.verticalScrollbarThumb.stretchHeight = true;
            themedSkin.scrollView = new GUIStyle(source.scrollView);
            themedSkin.scrollView.normal.background = null;
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

        // Visual-only drawing: overlays contain no input, targeting or hitbox.
        internal static void DrawMapCorners(Rect b, Color tint)
        {
            if (b.width < 40f || b.height < 40f) return;
            float n = Mathf.Min(20f, b.width * .09f, b.height * .16f);
            Stroke(new Rect(b.xMin,b.yMin,n,2f),tint);
            Stroke(new Rect(b.xMin,b.yMin,2f,n),tint);
            Stroke(new Rect(b.xMax-n,b.yMin,n,2f),tint);
            Stroke(new Rect(b.xMax-2f,b.yMin,2f,n),tint);
            Stroke(new Rect(b.xMin,b.yMax-2f,n,2f),tint);
            Stroke(new Rect(b.xMin,b.yMax-n,2f,n),tint);
            Stroke(new Rect(b.xMax-n,b.yMax-2f,n,2f),tint);
            Stroke(new Rect(b.xMax-2f,b.yMax-n,2f,n),tint);
        }

        internal static void DrawHeader(Rect area)
        {
            GUI.Box(area, GUIContent.none, Panel);
            Stroke(new Rect(area.x+3f,area.y+7f,3f,Mathf.Max(0f,area.height-14f)),Gold);
            Stroke(new Rect(area.x+12f,area.yMax-3f,
                Mathf.Max(0f,area.width-24f),1f),new Color(.52f,.75f,.79f,.62f));
        }

        internal static void DrawPanelAccent(Rect area, Color accent)
        {
            Stroke(new Rect(area.x+12f,area.y+4f,
                Mathf.Max(0f,area.width-24f),2f),accent);
        }

        // Decorative navigation marks only. The real button retains its full
        // rectangular hit area and existing selection/toggle semantics.
        internal static void DrawNavigationMark(Rect area, Color tint, bool selected)
        {
            if (area.width < 90f || area.height < 28f) return;
            Stroke(new Rect(area.x+3f,area.y+8f,3f,area.height-16f),tint);
            Stroke(new Rect(area.x+11f,area.center.y-3f,6f,6f),tint);
            if (selected)
                Stroke(new Rect(area.xMax-15f,area.center.y-2f,7f,4f),
                    new Color(1f,1f,1f,.85f));
        }

        // Source-backed facility pages share one game-like visual language.
        // These plates cannot purchase, mutate resources or claim progress.
        internal static void DrawFacilityBanner(Rect area, string eyebrow,
            string title, string description, string counter, Color tint)
        {
            GUI.Box(area, GUIContent.none, Panel);
            Stroke(new Rect(area.x+4f,area.y+10f,4f,area.height-20f),tint);
            Stroke(new Rect(area.x+17f,area.y+35f,
                Mathf.Max(0f,area.width-34f),1f),new Color(tint.r,tint.g,tint.b,.4f));
            Stroke(new Rect(area.xMax-39f,area.y+52f,17f,2f),tint);
            Stroke(new Rect(area.xMax-31f,area.y+45f,2f,16f),tint);
            float textWidth = Mathf.Max(60f,area.width-48f);
            GUI.Label(new Rect(area.x+18f,area.y+10f,
                Mathf.Max(50f,textWidth-110f),22f),eyebrow,Overline);
            GUI.Label(new Rect(area.xMax-114f,area.y+9f,96f,23f),counter,
                facilityBadge);
            GUI.Label(new Rect(area.x+18f,area.y+45f,textWidth,38f),title,
                facilityHeading);
            GUI.Label(new Rect(area.x+18f,area.y+88f,textWidth,42f),description,
                researchDetail);
        }

        // The whole 72px row is a touch target; labels are visual overlays.
        // Text and collapse state are supplied by the owner, never inferred
        // from gameplay data or used to execute an action.
        internal static bool DrawAccordionHeader(string title, string meta,
            bool expanded, Color tint, float height = 72f)
        {
            Rect area = GUILayoutUtility.GetRect(0f, height,
                GUILayout.ExpandWidth(true));
            bool pressed = GUI.Button(area, GUIContent.none,
                expanded ? accordionOpen : accordionClosed);
            if (Event.current.type != EventType.Repaint) return pressed;
            Stroke(new Rect(area.x+5f,area.y+12f,4f,
                Mathf.Max(0f,area.height-24f)),tint);
            GUI.Label(new Rect(area.x+14f,area.y+10f,34f,area.height-20f),
                expanded ? "▼" : "▶",accordionArrow);
            float available = Mathf.Max(100f,area.width-70f);
            float metaWidth = string.IsNullOrEmpty(meta) ? 0f :
                Mathf.Min(Mathf.Max(82f,available*.32f),170f);
            float titleWidth = Mathf.Max(40f,area.width-76f-metaWidth);
            GUI.Label(new Rect(area.x+52f,area.y+9f,titleWidth,area.height-18f),
                title,accordionTitle);
            if (metaWidth>0f)
                GUI.Label(new Rect(area.xMax-metaWidth-18f,area.y+9f,
                    metaWidth,area.height-18f),meta,accordionMeta);
            return pressed;
        }

        // A tappable operation row: one press is the action, never an expansion.
        // The caller owns eligibility, resource checks and the transaction.
        internal static bool DrawActionRow(string title, string meta,
            Color tint, float height = 72f)
        {
            Rect area = GUILayoutUtility.GetRect(0f, height,
                GUILayout.ExpandWidth(true));
            bool pressed = GUI.Button(area, GUIContent.none, accordionClosed);
            if (Event.current.type != EventType.Repaint) return pressed;
            Stroke(new Rect(area.x+5f,area.y+12f,4f,
                Mathf.Max(0f,area.height-24f)),tint);
            GUI.Label(new Rect(area.x+14f,area.y+10f,34f,area.height-20f),
                "▶",accordionArrow);
            float available = Mathf.Max(100f,area.width-70f);
            float metaWidth = string.IsNullOrEmpty(meta) ? 0f :
                Mathf.Min(Mathf.Max(82f,available*.32f),170f);
            float titleWidth = Mathf.Max(40f,area.width-76f-metaWidth);
            GUI.Label(new Rect(area.x+52f,area.y+9f,titleWidth,area.height-18f),
                title,accordionTitle);
            if (metaWidth>0f)
                GUI.Label(new Rect(area.xMax-metaWidth-18f,area.y+9f,
                    metaWidth,area.height-18f),meta,accordionMeta);
            return pressed;
        }

        internal static void DrawResearchPlate(Rect area, int ordinal,
            string title, string description, int maximumLevel, Color tint)
        {
            GUI.Box(area, GUIContent.none, Panel);
            Stroke(new Rect(area.x+4f,area.y+7f,3f,area.height-14f),tint);
            Stroke(new Rect(area.x+15f,area.y+13f,42f,42f),
                new Color(tint.r,tint.g,tint.b,.35f));
            GUI.Label(new Rect(area.x+15f,area.y+14f,42f,39f),
                ordinal.ToString("00"),researchNumber);
            GUI.Label(new Rect(area.x+67f,area.y+11f,
                Mathf.Max(40f,area.width-82f),48f),title,researchName);
            GUI.Label(new Rect(area.x+18f,area.y+63f,
                Mathf.Max(40f,area.width-36f),35f),description,researchDetail);
            Stroke(new Rect(area.x+18f,area.y+105f,
                Mathf.Max(0f,area.width-36f),1f),new Color(tint.r,tint.g,tint.b,.3f));
            GUI.Label(new Rect(area.x+18f,area.y+109f,
                Mathf.Max(40f,area.width-135f),22f),"구매 미연결",Overline);
            GUI.Label(new Rect(area.xMax-119f,area.y+108f,101f,22f),
                "최대 " + maximumLevel + "단계",facilityBadge);
        }

        internal static void ResourceChip(Rect slot, string type, string amount, Color tint)
        {
            // A single baseline: small resource marker, label and actual value.
            float icon = 8f;
            Stroke(new Rect(slot.x + 4f, slot.center.y - icon * .5f, icon, icon), tint);
            var key = new GUIStyle(GUI.skin.label) { fontSize = 13,
                alignment = TextAnchor.MiddleLeft, wordWrap = false,
                padding = new RectOffset(0, 0, 0, 0), clipping = TextClipping.Clip };
            key.normal.textColor = tint;
            float labelWidth = key.CalcSize(new GUIContent(type)).x;
            GUI.Label(new Rect(slot.x + 16f, slot.y, labelWidth, slot.height), type, key);
            var value = new GUIStyle(key) { fontSize = 18, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight };
            value.normal.textColor = Color.white;
            var number = new GUIContent(amount);
            var valueRect = new Rect(slot.x + 20f + labelWidth, slot.y,
                Mathf.Max(0f, slot.width - 26f - labelWidth), slot.height);
            while (value.fontSize > 9 && value.CalcSize(number).x > valueRect.width) value.fontSize--;
            GUI.Label(valueRect, number, value);
        }

        private static void Stroke(Rect area, Color tint)
        {
            if (area.width<=0f||area.height<=0f)return;
            Color before=GUI.color;
            GUI.color=tint;
            GUI.DrawTexture(area,Texture2D.whiteTexture);
            GUI.color=before;
        }

        // Tiny CPU-generated textured plates, drawn once and 9-sliced.
        private static Texture2D Plate(Color bright, Color dark, Color border)
        {
            const int size=32, radius=7;
            var tex=new Texture2D(size,size,TextureFormat.RGBA32,false);
            tex.filterMode=FilterMode.Bilinear;
            tex.wrapMode=TextureWrapMode.Clamp;
            tex.hideFlags=HideFlags.HideAndDontSave;
            for(int y=0;y<size;y++)
            for(int x=0;x<size;x++)
            {
                float px=x+.5f,py=y+.5f;
                float dx=Mathf.Max(0f,Mathf.Max(radius-px,px-(size-radius)));
                float dy=Mathf.Max(0f,Mathf.Max(radius-py,py-(size-radius)));
                float alpha=Mathf.Clamp01(radius+.5f-Mathf.Sqrt(dx*dx+dy*dy));
                float edge=Mathf.Min(Mathf.Min(px,size-px),Mathf.Min(py,size-py));
                Color color=Color.Lerp(bright,dark,y/(float)(size-1));
                color=Color.Lerp(color,border,edge<1.7f?.86f:0f);
                color.a=alpha;
                tex.SetPixel(x,y,color);
            }
            tex.Apply(false,true);
            return tex;
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
