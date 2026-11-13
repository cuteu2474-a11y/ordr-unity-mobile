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
        private static GUIStyle borderlessPanel;
        private static GUIStyle closeButton;
        private static GUIStyle navButton;
        private static GUIStyle overline;
        private static GUIStyle subheading;
        private static GUIStyle skillButton;
        private static GUIStyle explanation;
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
        private static GUIStyle wispCountCaption;
        private static GUIStyle wispCountValue;
        private static GUIStyle statusValue;
        private static GUIStyle scoreCardCaption;
        private static GUIStyle scoreCardValue;
        private static GUIStyle scoreCardFooter;
        private static GUIStyle fusionReadyChip;
        private static GUIStyle fusionMissingChip;
        private static GUIStyle fusionNeutralChip;
        private static GUIStyle fusionSelectedCard;
        private static GUIStyle fusionIngredientName;
        private static GUIStyle fusionListName;
        private static GUIStyle smallButton;
        private static GUIStyle confirmationPanel;
        private static GUIStyle helpBubble;
        private static GUIStyle statusCard;

        internal static GUIStyle SelectedTab { get { return selectedTab; } }
        internal static GUIStyle PrimaryButton { get { return primaryButton; } }
        internal static GUIStyle SectionTitle { get { return sectionTitle; } }
        internal static GUIStyle CurrencyText { get { return currencyText; } }
        internal static GUIStyle Panel { get { return panel; } }
        internal static GUIStyle BorderlessPanel { get { return borderlessPanel; } }
        internal static GUIStyle CloseButton { get { return closeButton; } }
        internal static GUIStyle NavButton { get { return navButton; } }
        internal static GUIStyle Overline { get { return overline; } }
        internal static GUIStyle Subheading { get { return subheading; } }
        internal static GUIStyle SkillButton { get { return skillButton; } }
        internal static GUIStyle Explanation { get { return explanation; } }
        internal static GUIStyle AccordionBody { get { return accordionBody; } }
        internal static GUIStyle FusionReadyChip { get { return fusionReadyChip; } }
        internal static GUIStyle FusionMissingChip { get { return fusionMissingChip; } }
        internal static GUIStyle FusionNeutralChip { get { return fusionNeutralChip; } }
        internal static GUIStyle FusionSelectedCard { get { return fusionSelectedCard; } }
        internal static GUIStyle FusionIngredientName { get { return fusionIngredientName; } }
        internal static GUIStyle FusionListName { get { return fusionListName; } }
        internal static GUIStyle SmallButton { get { return smallButton; } }
        internal static GUIStyle ConfirmationPanel { get { return confirmationPanel; } }
        internal static GUIStyle HelpBubble { get { return helpBubble; } }
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
            closeButton = new GUIStyle(themedSkin.button);
            Texture2D closeFace = Plate(new Color(.25f,.28f,.32f),
                new Color(.11f,.14f,.18f),new Color(.63f,.68f,.72f));
            Background(closeButton, closeFace, hover, Color.white);
            closeButton.fontSize = 28;
            closeButton.padding = new RectOffset(0,0,0,0);
            closeButton.wordWrap = false;

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
            borderlessPanel = new GUIStyle(panel);
            Texture2D drawerBackground = Flat(new Color(.035f,.055f,.085f,1f));
            Background(borderlessPanel, drawerBackground, drawerBackground, Text);
            borderlessPanel.border = new RectOffset(0,0,0,0);
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
            skillButton.padding = new RectOffset(8,8,4,4);
            skillButton.normal.textColor = Aqua;
            explanation = new GUIStyle(themedSkin.label);
            explanation.fontSize = 19;
            explanation.fontStyle = FontStyle.Normal;
            explanation.wordWrap = true;
            explanation.normal.textColor = new Color(0.92f,0.97f,1f,1f);
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

            // The fusion strip uses game-themed, readable materials without
            // changing any source-backed readiness or consumption rules.
            Texture2D fusionReadyPlate = Plate(new Color(.08f,.30f,.27f),
                new Color(.04f,.18f,.16f), new Color(.34f,.77f,.60f));
            Texture2D fusionMissingPlate = Plate(new Color(.40f,.17f,.20f),
                new Color(.24f,.09f,.12f), new Color(.90f,.42f,.46f));
            Texture2D fusionNeutralPlate = Plate(new Color(.18f,.26f,.37f),
                new Color(.09f,.15f,.25f), new Color(.50f,.62f,.72f));
            Texture2D fusionSelectedPlate = Plate(new Color(.20f,.25f,.35f),
                new Color(.09f,.14f,.24f), new Color(.95f,.72f,.36f));
            fusionReadyChip = CreateFusionChip(fusionReadyPlate, new Color(.72f,1f,.85f));
            fusionMissingChip = CreateFusionChip(fusionMissingPlate, new Color(1f,.78f,.79f));
            fusionNeutralChip = CreateFusionChip(fusionNeutralPlate, Text);
            fusionSelectedCard = new GUIStyle(themedSkin.box);
            Background(fusionSelectedCard, fusionSelectedPlate, fusionSelectedPlate, Text);
            fusionSelectedCard.border = new RectOffset(9,9,9,9);
            fusionSelectedCard.padding = new RectOffset(9,9,7,7);
            fusionIngredientName = new GUIStyle(themedSkin.label);
            fusionIngredientName.fontSize = 17;
            fusionIngredientName.fontStyle = FontStyle.Bold;
            fusionIngredientName.wordWrap = true;
            fusionIngredientName.alignment = TextAnchor.MiddleCenter;
            fusionIngredientName.normal.textColor = Color.white;
            fusionListName = new GUIStyle(navButton);
            fusionListName.fontSize = 20;
            fusionListName.fontStyle = FontStyle.Bold;
            fusionListName.alignment = TextAnchor.MiddleLeft;
            fusionListName.padding = new RectOffset(12,8,4,4);
            fusionListName.clipping = TextClipping.Clip;
            wispCountCaption = new GUIStyle(accordionMeta);
            wispCountCaption.fontSize = 13;
            wispCountCaption.fontStyle = FontStyle.Bold;
            wispCountCaption.alignment = TextAnchor.MiddleCenter;
            wispCountCaption.normal.textColor = Muted;
            wispCountValue = new GUIStyle(accordionTitle);
            wispCountValue.fontSize = 27;
            wispCountValue.fontStyle = FontStyle.Bold;
            wispCountValue.alignment = TextAnchor.MiddleCenter;
            wispCountValue.normal.textColor = Color.white;
            statusValue = new GUIStyle(accordionMeta);
            statusValue.fontSize = 21;
            statusValue.fontStyle = FontStyle.Bold;
            statusValue.normal.textColor = Color.white;
            scoreCardCaption = new GUIStyle(accordionMeta);
            scoreCardCaption.fontSize = 15;
            scoreCardCaption.alignment = TextAnchor.MiddleCenter;
            scoreCardCaption.normal.textColor = Muted;
            scoreCardValue = new GUIStyle(statusValue);
            scoreCardValue.fontSize = 30;
            scoreCardValue.alignment = TextAnchor.MiddleCenter;
            scoreCardFooter = new GUIStyle(scoreCardCaption);
            scoreCardFooter.fontSize = 12;
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
            InitializeAuxiliaryStyles();
        }

        private static void InitializeAuxiliaryStyles()
        {
            // Keep controls usable without legacy image dependencies.
            statusCard = new GUIStyle(accordionClosed);
            smallButton = new GUIStyle(themedSkin.button);
            smallButton.padding = new RectOffset(4,4,6,6);
            smallButton.wordWrap = false;
            confirmationPanel = new GUIStyle(accordionBody);
            confirmationPanel.padding = new RectOffset(16,16,14,14);
            helpBubble = new GUIStyle(accordionBody);
            helpBubble.padding = new RectOffset(22,18,16,32);
            // Cartoon pirate styling uses the original shared IMGUI controls;
            // no new hitboxes, click handlers, resource transactions or screens.
            // Teal is the default action, orange marks selection/confirmation.
            OrdrCartoonUiArtwork.Apply(themedSkin.button, OrdrCartoonUiArtwork.Face.TealWide, true);
            OrdrCartoonUiArtwork.Apply(navButton, OrdrCartoonUiArtwork.Face.TealWide, true);
            OrdrCartoonUiArtwork.Apply(fusionListName, OrdrCartoonUiArtwork.Face.TealWide, true);
            OrdrCartoonUiArtwork.Apply(selectedTab, OrdrCartoonUiArtwork.Face.OrangeWide, true);
            OrdrCartoonUiArtwork.Apply(primaryButton, OrdrCartoonUiArtwork.Face.OrangeWide, true);
            OrdrCartoonUiArtwork.Apply(skillButton, OrdrCartoonUiArtwork.Face.TealWide, true);
            OrdrCartoonUiArtwork.Apply(accordionClosed, OrdrCartoonUiArtwork.Face.TealWide, true);
            OrdrCartoonUiArtwork.Apply(accordionOpen, OrdrCartoonUiArtwork.Face.OrangeWide, true);
            OrdrCartoonUiArtwork.Apply(closeButton, OrdrCartoonUiArtwork.Face.Square, true);
            OrdrCartoonUiArtwork.Apply(smallButton, OrdrCartoonUiArtwork.Face.Square, true);
            OrdrCartoonUiArtwork.Apply(confirmationPanel, OrdrCartoonUiArtwork.Face.Parchment, false);
            OrdrCartoonUiArtwork.Apply(helpBubble, OrdrCartoonUiArtwork.Face.Parchment, false);
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

        // Large, read-only battle metrics without a decorative section title.
        internal static void DrawScoreHeroCard(string label, string value,
            string footer, Color tint, float height = 112f)
        {
            Rect area = GUILayoutUtility.GetRect(0f, height,
                GUILayout.ExpandWidth(true));
            GUI.Box(area, GUIContent.none, statusCard);
            if (Event.current.type != EventType.Repaint) return;
            Stroke(new Rect(area.x + 8f, area.y + 6f,
                Mathf.Max(0f, area.width - 16f), 3f), tint);
            GUI.Label(new Rect(area.x + 5f, area.y + 15f,
                area.width - 10f, 22f), label, scoreCardCaption);
            Color previous = scoreCardValue.normal.textColor;
            scoreCardValue.normal.textColor = tint;
            int font = scoreCardValue.fontSize;
            var content = new GUIContent(value);
            while (scoreCardValue.fontSize > 18 &&
                scoreCardValue.CalcSize(content).x > area.width - 12f)
                scoreCardValue.fontSize--;
            GUI.Label(new Rect(area.x + 5f, area.y + 37f,
                area.width - 10f, 43f), content, scoreCardValue);
            scoreCardValue.fontSize = font;
            scoreCardValue.normal.textColor = previous;
            if (!string.IsNullOrEmpty(footer))
                GUI.Label(new Rect(area.x + 5f, area.yMax - 27f,
                    area.width - 10f, 20f), footer, scoreCardFooter);
        }

        // Read-only game data uses the same compact card language as actions.
        internal static void DrawStatusRow(string title, string value,
            Color tint, float height = 64f)
        {
            Rect area = GUILayoutUtility.GetRect(0f, height,
                GUILayout.ExpandWidth(true));
            GUI.Box(area, GUIContent.none, statusCard);
            if (Event.current.type != EventType.Repaint) return;
            Stroke(new Rect(area.x+5f,area.y+12f,4f,
                Mathf.Max(0f,area.height-24f)),tint);
            float valueWidth = Mathf.Min(154f,Mathf.Max(100f,area.width*.36f));
            float titleWidth = Mathf.Max(40f,area.width-valueWidth-42f);
            int originalTitleSize = accordionTitle.fontSize;
            var titleText = new GUIContent(title);
            while (accordionTitle.fontSize > 14 &&
                accordionTitle.CalcSize(titleText).x > titleWidth)
                accordionTitle.fontSize--;
            GUI.Label(new Rect(area.x+18f,area.y+8f,titleWidth,area.height-16f),
                titleText,accordionTitle);
            accordionTitle.fontSize = originalTitleSize;
            int originalValueSize = statusValue.fontSize;
            var valueText = new GUIContent(value);
            while (statusValue.fontSize > 14 &&
                statusValue.CalcSize(valueText).x > valueWidth-10f)
                statusValue.fontSize--;
            GUI.Label(new Rect(area.xMax-valueWidth-14f,area.y+8f,
                valueWidth,area.height-16f),valueText,statusValue);
            statusValue.fontSize = originalValueSize;
        }

        // The whole 72px row is a touch target; labels are visual overlays.
        // Text and collapse state are supplied by the owner, never inferred
        // from gameplay data or used to execute an action.
        internal static bool DrawAccordionHeader(string title, string meta,
            bool expanded, Color tint, float height = 72f, bool countBadge = false,
            long remainingCount = 0)
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
                countBadge ? 118f :
                Mathf.Min(Mathf.Max(82f,available*.32f),170f);
            float titleWidth = Mathf.Max(40f,area.width-76f-metaWidth);
            GUI.Label(new Rect(area.x+52f,area.y+9f,titleWidth,area.height-18f),
                title,accordionTitle);
            if (metaWidth>0f)
            {
                if (countBadge) DrawWispCountBadge(area, meta, remainingCount);
                else GUI.Label(new Rect(area.xMax-metaWidth-18f,area.y+9f,
                    metaWidth,area.height-18f),meta,accordionMeta);
            }
            return pressed;
        }

        // Ordinary rows act on a single tap. Gambling rows distinguish a
        // short tap from a hold without changing their shared presentation.
        internal static bool DrawActionRow(string title, string meta,
            Color tint, float height = 72f, bool countBadge = false, long remainingCount = 0)
        {
            Rect area = GUILayoutUtility.GetRect(0f, height,
                GUILayout.ExpandWidth(true));
            bool pressed = GUI.Button(area, GUIContent.none, accordionClosed);
            if (Event.current.type == EventType.Repaint)
                DrawActionRowFace(area, title, meta, tint, countBadge, remainingCount);
            return pressed;
        }

        internal static MobileLongPressResult DrawHoldActionRow(string key,
            string title, string meta, Color tint, MobileLongPressGesture gesture,
            float height = 72f, bool compact = false)
        {
            Rect area = GUILayoutUtility.GetRect(0f, height,
                GUILayout.ExpandWidth(true));
            Event input = Event.current;
            EventType inputType = input.type;
            if (inputType == EventType.MouseDown && input.button == 0 &&
                GUI.enabled && area.Contains(input.mousePosition))
                gesture.Begin(key, Time.unscaledTime, input.mousePosition);
            if (inputType == EventType.MouseDrag && input.button == 0 &&
                gesture.Move(key, input.mousePosition))
                GUIUtility.hotControl = 0;
            bool pressed = GUI.Button(area, GUIContent.none, accordionClosed);
            MobileLongPressResult result = MobileLongPressResult.None;
            if (pressed)
                result = gesture.Complete(key, Time.unscaledTime);
            else if (inputType == EventType.MouseUp && gesture.IsTracking(key))
                gesture.Cancel();
            if (Event.current.type == EventType.Repaint)
            {
                if (compact)
                    DrawCompactGamblingRowFace(area, title, meta, tint);
                else
                    DrawActionRowFace(area, title, meta, tint, false);
            }
            return result;
        }

        // One large touch target: short tap executes once, long press reveals
        // details. The cost is always visible; no second hidden action button.
        private static void DrawCompactGamblingRowFace(Rect area,
            string title, string meta, Color tint)
        {
            Stroke(new Rect(area.x + 5f, area.y + 10f, 4f,
                Mathf.Max(0f, area.height - 20f)), tint);
            float costWidth = Mathf.Min(Mathf.Max(107f, area.width * .45f),
                Mathf.Max(75f, area.width - 109f));
            float titleWidth = Mathf.Max(55f, area.width - costWidth - 37f);
            var label = new GUIStyle(accordionTitle)
            {
                fontSize = 20, alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip, wordWrap = false
            };
            var cost = new GUIStyle(statusValue)
            {
                fontSize = 18, alignment = TextAnchor.MiddleRight,
                clipping = TextClipping.Clip, wordWrap = false
            };
            GUI.Label(new Rect(area.x + 15f, area.y + 6f, titleWidth,
                area.height - 12f), title, label);
            GUI.Label(new Rect(area.xMax - costWidth - 28f, area.y + 6f,
                costWidth, area.height - 12f), meta, cost);
            GUI.Label(new Rect(area.xMax - 30f, area.y + 6f,
                24f, area.height - 12f), "⋯", accordionArrow);
        }

        private static void DrawActionRowFace(Rect area, string title,
            string meta, Color tint, bool countBadge, long remainingCount = 0)
        {
            Stroke(new Rect(area.x+5f,area.y+12f,4f,
                Mathf.Max(0f,area.height-24f)),tint);
            GUI.Label(new Rect(area.x+14f,area.y+10f,34f,area.height-20f),
                "▶",accordionArrow);
            float available = Mathf.Max(100f,area.width-70f);
            float metaWidth = string.IsNullOrEmpty(meta) ? 0f :
                countBadge ? 118f :
                Mathf.Min(Mathf.Max(82f,available*.32f),170f);
            float titleWidth = Mathf.Max(40f,area.width-76f-metaWidth);
            GUI.Label(new Rect(area.x+52f,area.y+9f,titleWidth,area.height-18f),
                title,accordionTitle);
            if (metaWidth>0f)
            {
                if (countBadge) DrawWispCountBadge(area, meta, remainingCount);
                else GUI.Label(new Rect(area.xMax-metaWidth-18f,area.y+9f,
                    metaWidth,area.height-18f),meta,accordionMeta);
            }
        }

        // Button face only: the parent header/action is still the single
        // original touch target. Do not create a second purchase/retry command.
        private static void DrawWispCountBadge(Rect area, string count, long remaining)
        {
            float width = Mathf.Min(112f, area.width * .38f);
            float height = Mathf.Min(60f, area.height - 10f);
            Rect badge = new Rect(area.xMax - width - 12f,
                area.y + (area.height - height) * .5f, width, height);
            bool wasEnabled = GUI.enabled;
            Color before = GUI.color;
            try
            {
                // Keep zero/blocked quantities readable, but leave input to
                // the original parent control and its existing enabled gate.
                GUI.enabled = true;
                Color tint = remaining > 0 && wasEnabled ? Gold : new Color(.66f,.70f,.75f,1f);
                var texture = OrdrBattlefieldArt.QuantityButtonTexture;
                if (texture != null)
                    DrawQuantitySlices(badge, texture, OrdrBattlefieldArt.QuantityButtonUv, tint);
                else
                {
                    GUI.color = before * tint;
                    GUI.Box(badge, GUIContent.none, accordionClosed);
                }
                GUI.color = before;
                GUI.Label(new Rect(badge.x+6f,badge.y+3f,badge.width-12f,18f),
                    "남은 수량", wispCountCaption);
                var value = new GUIStyle(wispCountValue);
                var content = new GUIContent(count);
                while (value.fontSize > 10 && value.CalcSize(content).x > badge.width-12f)
                    value.fontSize--;
                GUI.Label(new Rect(badge.x+6f,badge.y+20f,
                    badge.width-12f,badge.height-23f),content,value);
            }
            finally { GUI.color = before; GUI.enabled = wasEnabled; }
        }

        // GPU-only nine-slice of the trimmed sprite UV, so transparent
        // generation margins never shrink the face. No readable texture copy.
        private static void DrawQuantitySlices(Rect area, Texture2D texture, Rect uv, Color tint)
        {
            float corner = Mathf.Min(10f, Mathf.Min(area.width * .25f, area.height * .25f));
            Color before = GUI.color;
            try
            {
                GUI.color = before * tint;
                for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                {
                    float x = column == 0 ? area.xMin : column == 1 ?
                        area.xMin + corner : area.xMax - corner;
                    float y = row == 0 ? area.yMin : row == 1 ?
                        area.yMin + corner : area.yMax - corner;
                    float width = column == 1 ? area.width - corner * 2f : corner;
                    float height = row == 1 ? area.height - corner * 2f : corner;
                    float u = column == 0 ? uv.xMin : column == 1 ?
                        uv.xMin + uv.width * .12f : uv.xMax - uv.width * .12f;
                    float v = row == 0 ? uv.yMax - uv.height * .20f :
                        row == 1 ? uv.yMin + uv.height * .20f : uv.yMin;
                    GUI.DrawTextureWithTexCoords(new Rect(x,y,width,height), texture,
                        new Rect(u,v,uv.width * (column == 1 ? .76f : .12f),
                            uv.height * (row == 1 ? .60f : .20f)));
                }
            }
            finally { GUI.color = before; }
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
                Mathf.Max(40f,area.width-135f),22f),"구매 준비 중",Overline);
            GUI.Label(new Rect(area.xMax-119f,area.y+108f,101f,22f),
                "최대 " + maximumLevel + "단계",facilityBadge);
        }

        internal static void ResourceChip(Rect slot, string type, string amount, Color tint)
        {
            // Keep the exact balance. Narrow slots put the value below its
            // caption rather than shrinking a long number beside Korean text.
            float icon = 8f;
            Stroke(new Rect(slot.x + 4f, slot.center.y - icon * .5f, icon, icon), tint);
            var key = new GUIStyle(GUI.skin.label) { fontSize = 13,
                alignment = TextAnchor.MiddleLeft, wordWrap = false,
                padding = new RectOffset(0, 0, 0, 0), clipping = TextClipping.Clip };
            key.normal.textColor = tint;
            float labelWidth = key.CalcSize(new GUIContent(type)).x;
            var value = new GUIStyle(key) { fontSize = 18, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight };
            value.normal.textColor = Color.white;
            var number = new GUIContent(amount);
            bool stacked = labelWidth + value.CalcSize(number).x + 32f > slot.width;
            var labelRect = stacked
                ? new Rect(slot.x + 16f, slot.y + 2f, Mathf.Max(0f, slot.width - 20f), 18f)
                : new Rect(slot.x + 16f, slot.y, labelWidth, slot.height);
            GUI.Label(labelRect, type, key);
            var valueRect = new Rect(slot.x + 20f + labelWidth, slot.y,
                Mathf.Max(0f, slot.width - 26f - labelWidth), slot.height);
            if (stacked)
                valueRect = new Rect(slot.x + 4f, slot.y + 21f,
                    Mathf.Max(0f, slot.width - 10f), Mathf.Max(0f, slot.height - 23f));
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

        private static Texture2D Flat(Color color)
        {
            var texture = new Texture2D(1,1,TextureFormat.RGBA32,false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixel(0,0,color);
            texture.Apply(false,true);
            return texture;
        }

        // Tiny CPU-generated textured plates, drawn once and 9-sliced.
        private static GUIStyle CreateFusionChip(Texture2D background,
            Color textColor)
        {
            GUIStyle chip = new GUIStyle(themedSkin.box);
            Background(chip, background, background, textColor);
            chip.border = new RectOffset(9,9,9,9);
            chip.padding = new RectOffset(9,9,6,6);
            chip.margin = new RectOffset(2,2,1,1);
            chip.fontSize = 18;
            chip.fontStyle = FontStyle.Bold;
            chip.alignment = TextAnchor.MiddleCenter;
            chip.wordWrap = false;
            chip.clipping = TextClipping.Clip;
            return chip;
        }

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


