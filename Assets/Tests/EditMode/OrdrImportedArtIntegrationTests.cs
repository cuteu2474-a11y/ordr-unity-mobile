using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Unity;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrImportedArtIntegrationTests
    {
        [Test]
        public void ApprovedFiveImagesAreBundledAsRuntimeResources()
        {
            AssertTexture("ImportedArt/enemy-pirate", 1254, 1254);
            AssertTexture("ImportedArt/buildings-sheet", 1254, 1254);
            AssertTexture("ImportedArt/ui-panel", 1536, 1024);
            AssertTexture("ImportedArt/units-sheet", 1983, 793);
            AssertTexture("ImportedArt/battle-floor", 1024, 683);
        }

        private static Texture2D AssertTexture(string path, int width, int height)
        {
            var image = Resources.Load<Texture2D>(path);
            Assert.IsNotNull(image, path + " must be included in player builds");
            Assert.AreEqual(width, image.width, path);
            Assert.AreEqual(height, image.height, path);
            return image;
        }

        [Test]
        public void RetiredPirateImageIsNotUsedForEveryLiveEnemyAndBoss()
        {
            var image = AssertTexture("ImportedArt/enemy-pirate", 1254, 1254);
            var normal = OrdrBattlefieldArt.EnemySprite(false);
            var boss = OrdrBattlefieldArt.EnemySprite(true);
            Assert.IsNotNull(normal);
            Assert.IsNotNull(boss);
            Assert.AreNotSame(normal, boss);
            Assert.AreNotSame(image, normal.texture);
            Assert.AreNotSame(image, boss.texture);
            Assert.AreSame(normal, OrdrBattlefieldArt.EnemySprite(false));
            Assert.AreSame(boss, OrdrBattlefieldArt.EnemySprite(true));
        }

        [Test]
        public void BlueGreenFloorAndGoldPanelReuseOriginalTextures()
        {
            var floor = AssertTexture("ImportedArt/battle-floor", 1024, 683);
            var panel = AssertTexture("ImportedArt/ui-panel", 1536, 1024);
            Assert.AreSame(floor, OrdrBattlefieldArt.MainGroundTexture);
            Assert.AreSame(floor, OrdrBattlefieldArt.StoryTexture);
            Assert.AreSame(panel, OrdrImportedArtAtlas.PanelBackground);
        }

        [Test]
        public void StoryBuildingsUseFourteenDistinctCellsInStageOrder()
        {
            var sheet = AssertTexture("ImportedArt/buildings-sheet", 1254, 1254);
            Assert.IsNull(OrdrImportedArtAtlas.StoryBuilding(0));
            Assert.IsNull(OrdrImportedArtAtlas.StoryBuilding(15));
            var first = OrdrImportedArtAtlas.StoryBuilding(1);
            var last = OrdrImportedArtAtlas.StoryBuilding(14);
            Assert.AreSame(sheet, first.texture);
            Assert.AreSame(sheet, last.texture);
            Assert.AreNotSame(first, last);
            Assert.AreSame(first, OrdrImportedArtAtlas.StoryBuilding(1));
            Assert.AreEqual(0f, first.rect.x, 1f);
            Assert.Greater(first.rect.y, last.rect.y);
            for (int i = 1; i <= 14; ++i)
            {
                var cell = OrdrImportedArtAtlas.StoryBuilding(i);
                Assert.IsNotNull(cell, "stage " + i);
                Assert.AreSame(sheet, cell.texture);
                var uv = OrdrImportedArtAtlas.TextureUv(cell);
                Assert.Greater(uv.width, 0f);
                Assert.Greater(uv.height, 0f);
                Assert.GreaterOrEqual(uv.xMin, 0f);
                Assert.LessOrEqual(uv.xMax, 1f);
                Assert.GreaterOrEqual(uv.yMin, 0f);
                Assert.LessOrEqual(uv.yMax, 1f);
            }
        }

        [Test]
        public void ConfirmedCharactersGetSeparateCellSpritesNotWholeSheet()
        {
            var sheet = AssertTexture("ImportedArt/units-sheet", 1983, 793);
            var ids = new[] {
                "LI-06", "C-02", "X-01", "C-07",
                "S-27", "U-12", "S-16"
            };
            foreach (string id in ids)
            {
                var cell = OrdrImportedArtAtlas.FindUnitPortrait(id);
                Assert.IsNotNull(cell, id);
                Assert.AreSame(sheet, cell.texture);
                Assert.Less(cell.rect.width, sheet.width / 2f, id);
                Assert.Less(cell.rect.height, sheet.height, id);
                Assert.AreSame(cell, OrdrImportedArtAtlas.FindUnitPortrait(id));
            }
            Assert.AreNotSame(OrdrImportedArtAtlas.FindUnitPortrait("C-02"),
                OrdrImportedArtAtlas.FindUnitPortrait("C-07"));
            Assert.IsNull(OrdrImportedArtAtlas.FindUnitPortrait("S-10"),
                "Never invent an unrelated sheet identity for Hawkins.");
        }
    }
}
