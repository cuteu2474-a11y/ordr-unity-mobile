using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Unity;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrBattlefieldArtTests
    {
        [TestCase("WideButton", "OrdrArt/Ui/WideButton", true)]
        [TestCase("SquareButton", "OrdrArt/Ui/SquareButton", true)]
        [TestCase("Popup", "OrdrArt/Ui/PopupPanel", false)]
        [TestCase("Speech", "OrdrArt/Ui/SpeechBubble", false)]
        public void SharedUiArtworkImportsAndBuildsCachedSlicedFaces(
            string faceName, string resource, bool interactive)
        {
            var raw = Resources.Load<Texture2D>(resource);
            Assert.IsNotNull(raw, "The original image must be in the player Resources build.");
            Assert.IsTrue(raw.isReadable, "One-time alpha trimming requires the checked-in import settings.");
            bool transparent = false, visible = false;
            foreach (var pixel in raw.GetPixels32())
            {
                transparent |= pixel.a == 0;
                visible |= pixel.a >= 32;
            }
            Assert.IsTrue(transparent, "Do not flatten the original transparent background.");
            Assert.IsTrue(visible, "A fully transparent import must not pass as applied artwork.");
            var type = typeof(OrdrMobileTouchPanel).Assembly.GetType("Ordr.Mobile.Unity.OrdrMobileUiArtwork");
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static;
            Assert.IsNotNull(type);
            var faceType = type.GetNestedType("Face", System.Reflection.BindingFlags.NonPublic);
            var apply = type.GetMethod("Apply", flags);
            Assert.IsNotNull(faceType);
            Assert.IsNotNull(apply);
            var face = System.Enum.Parse(faceType, faceName);
            var first = new GUIStyle();
            var second = new GUIStyle();
            apply.Invoke(null, new object[] { first, face, Color.white, interactive });
            apply.Invoke(null, new object[] { second, face, Color.white, interactive });
            Assert.IsNotNull(first.normal.background, "Fallback without an imported face is a failure here.");
            Assert.AreSame(first.normal.background, second.normal.background);
            Assert.IsFalse(first.normal.background.isReadable, "Tinted GPU faces should release CPU copies.");
            Assert.LessOrEqual(Mathf.Max(first.normal.background.width, first.normal.background.height),
                faceName == "SquareButton" ? 96 : 384);
            Assert.Greater(first.border.left, 0);
            Assert.Less(first.border.left + first.border.right, first.normal.background.width);
            Assert.Less(first.border.top + first.border.bottom, first.normal.background.height);
            if (interactive)
            {
                Assert.AreNotSame(first.normal.background, first.active.background);
                Assert.AreNotSame(first.normal.background, first.hover.background);
            }
            else Assert.AreSame(first.normal.background, first.active.background);
        }

        [Test]
        public void AuthoredAssetsAreCachedAndDontNeedNetworkOrPrivatePhotos()
        {
            Assert.IsNotNull(OrdrBattlefieldArt.BoardSprite);
            Assert.AreSame(OrdrBattlefieldArt.BoardSprite, OrdrBattlefieldArt.BoardSprite);
            Assert.IsNotNull(OrdrBattlefieldArt.StoryTexture);
            Assert.IsNotNull(OrdrBattlefieldArt.HeaderTexture);
            Assert.Greater(OrdrBattlefieldArt.StoryTexture.width, 0);
            Assert.AreSame(OrdrBattlefieldArt.UnitSprite("C-01"),
                OrdrBattlefieldArt.UnitSprite("C-01"));
            Assert.AreNotSame(OrdrBattlefieldArt.UnitSprite("C-01"),
                OrdrBattlefieldArt.UnitSprite("C-02"));
            Assert.AreEqual(1f, OrdrBattlefieldArt.UnitSprite("C-01").bounds.size.x, 0.0001f);
            Assert.IsNotNull(OrdrBattlefieldArt.GradeFrame);
            Assert.IsNotNull(OrdrBattlefieldArt.EnemySprite(true));
            Assert.AreNotSame(OrdrBattlefieldArt.EnemySprite(true),
                OrdrBattlefieldArt.EnemySprite(false));
            Assert.IsNotNull(OrdrBattlefieldArt.EnemyHaloSprite);
            Assert.IsNotNull(OrdrBattlefieldArt.SkillRingSprite);
            Assert.IsNotNull(OrdrBattlefieldArt.SkillSparkSprite);
            Assert.AreSame(OrdrBattlefieldArt.SkillRingSprite,
                OrdrBattlefieldArt.SkillRingSprite);
            Assert.IsNotNull(OrdrBattlefieldArt.EvolutionSprite);
        }

        [Test]
        public void ApprovedArtworkIsPackagedOfflineAndBothBattlefieldsShareOneTexture()
        {
            var ground = Resources.Load<Texture2D>(OrdrBattlefieldArt.GroundResource);
            Assert.IsNotNull(ground, "The approved floor must be in the player Resources build.");
            Assert.IsNotNull(Resources.Load<Texture2D>(OrdrBattlefieldArt.QuantityResource));
            Assert.IsNotNull(Resources.Load<Texture2D>(OrdrBattlefieldArt.FortressResource));
            Assert.AreSame(ground, OrdrBattlefieldArt.SharedGroundTexture);
            Assert.AreSame(ground, OrdrBattlefieldArt.BoardSprite.texture);
            Assert.AreSame(ground, OrdrBattlefieldArt.StoryTexture);
        }

        [TestCase(1200f, 800f)]
        [TestCase(800f, 1200f)]
        public void NonSquareArtworkStillCoversTheExactExistingWorldRectangle(float width, float height)
        {
            var sprite = OrdrBattlefieldArt.BoardSprite;
            var scale = OrdrBattlefieldArt.BoardScaleForBounds(new Vector2(width, height), sprite);
            Assert.AreEqual(width, sprite.bounds.size.x * scale.x, .001f);
            Assert.AreEqual(height, sprite.bounds.size.y * scale.y, .001f);
        }

        [TestCase(180f, 90f)]
        [TestCase(70f, 140f)]
        public void FortressKeepsItsCroppedAspectAndFitsAboveTheStoryFooter(float width, float height)
        {
            var source = OrdrBattlefieldArt.StoryFortressTexture;
            Assert.IsNotNull(source);
            Rect area = new Rect(25f, 42f, width, height);
            Rect image = OrdrBattlefieldArt.FitTexture(area, source, OrdrBattlefieldArt.StoryFortressUv);
            Assert.That(image.width / image.height, Is.EqualTo(1348f / 792f).Within(.01f));
            Assert.GreaterOrEqual(image.xMin, area.xMin - .001f);
            Assert.LessOrEqual(image.xMax, area.xMax + .001f);
            Assert.GreaterOrEqual(image.yMin, area.yMin - .001f);
            Assert.AreEqual(area.yMax, image.yMax, .001f);
        }

        [TestCase(1280f, 720f)]
        [TestCase(1920f, 1080f)]
        [TestCase(3120f, 1440f)]
        public void TallerStoryArtworkDoesNotOverlapMainCameraSkillRibbonOrRightControls(float width, float height)
        {
            var safe = new Rect(0f, 0f, width, height);
            var layout = MobileMapSkillLayout.Calculate(safe, height, MobileMapSkillLayout.UiScale(safe, 320f));
            Assert.Less(layout.Battle.yMax, layout.SkillMargin.yMin);
            Assert.Less(layout.SkillMargin.yMax, layout.Story.yMin);
            Assert.Less(layout.Story.height, layout.Battle.height);
            Assert.LessOrEqual(layout.Story.yMax, height);
            Assert.Less(layout.Battle.xMax, layout.RightDrawer.xMin);
            var viewport = layout.MainCameraViewport(width, height);
            Assert.AreEqual(layout.Battle.xMin, viewport.xMin * width, .001f);
            Assert.AreEqual(layout.Battle.yMin, (1f - viewport.yMax) * height, .001f);
            Assert.AreEqual(layout.Battle.height, viewport.height * height, .001f);
        }
    }
}
