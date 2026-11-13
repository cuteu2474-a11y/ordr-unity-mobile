using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Unity;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrCartoonUiArtworkTests
    {
        [TestCase("TealWide", "OrdrArt/CartoonUi/TealWide", true, 384)]
        [TestCase("OrangeWide", "OrdrArt/CartoonUi/OrangeWide", true, 384)]
        [TestCase("Square", "OrdrArt/CartoonUi/Square", true, 128)]
        [TestCase("Parchment", "OrdrArt/CartoonUi/Parchment", false, 512)]
        public void CartoonButtonsAndPaperUseCachedReadableNineSlicedFaces(
            string faceName, string resource, bool interactive, int maximumEdge)
        {
            var original = Resources.Load<Texture2D>(resource);
            Assert.IsNotNull(original, "Runtime PNG must be packaged in Unity Resources.");
            Assert.IsTrue(original.isReadable, "One-time transparent margin trimming must be supported.");

            var artwork = typeof(OrdrMobileTouchPanel).Assembly.GetType(
                "Ordr.Mobile.Unity.OrdrCartoonUiArtwork");
            Assert.IsNotNull(artwork);
            var faceType = artwork.GetNestedType("Face", BindingFlags.NonPublic);
            Assert.IsNotNull(faceType);
            var face = Enum.Parse(faceType, faceName);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var pathMethod = artwork.GetMethod("ResourcePath", flags);
            var applyMethod = artwork.GetMethod("Apply", flags);
            Assert.IsNotNull(pathMethod);
            Assert.IsNotNull(applyMethod);
            Assert.AreEqual(resource, pathMethod.Invoke(null, new object[] { face }));

            var first = new GUIStyle();
            var second = new GUIStyle();
            Assert.IsTrue((bool)applyMethod.Invoke(null,
                new object[] { first, face, interactive }));
            Assert.IsTrue((bool)applyMethod.Invoke(null,
                new object[] { second, face, interactive }));

            Assert.IsNotNull(first.normal.background);
            Assert.AreSame(first.normal.background, second.normal.background);
            Assert.IsFalse(first.normal.background.isReadable,
                "Compact GPU faces should not retain readable CPU copies.");
            Assert.LessOrEqual(Mathf.Max(first.normal.background.width,
                first.normal.background.height), maximumEdge);
            Assert.Greater(first.border.left, 0);
            Assert.Greater(first.border.top, 0);
            Assert.Less(first.border.left + first.border.right,
                first.normal.background.width);
            Assert.Less(first.border.top + first.border.bottom,
                first.normal.background.height);
            if (interactive)
            {
                Assert.AreNotSame(first.normal.background, first.hover.background);
                Assert.AreNotSame(first.normal.background, first.active.background);
                Assert.AreSame(first.normal.background, first.onNormal.background);
            }
            else
            {
                Assert.AreSame(first.normal.background, first.hover.background);
                Assert.AreSame(first.normal.background, first.active.background);
            }
        }

        [Test]
        public void ExistingBattlefieldResourcesRemainAvailable()
        {
            Assert.IsNotNull(Resources.Load<Texture2D>(OrdrBattlefieldArt.GroundResource));
            Assert.IsNotNull(Resources.Load<Texture2D>(OrdrBattlefieldArt.QuantityResource));
            Assert.IsNotNull(Resources.Load<Texture2D>(OrdrBattlefieldArt.FortressResource));
        }
    }
}
