using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;
using Ordr.Mobile.Unity;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileOwnedUnitFormationTests
    {
        private static MobileUnitSnapshot[] Owned()
        {
            var catalog = CharacterUnitCatalog.FromJson(File.ReadAllText(
                Path.Combine(Application.dataPath, "..", "data", "units.json")));
            return new[]
            {
                new MobileUnitSnapshot(30, catalog.Require("C-01"), null, new UnitState(30, "C-01", -9000, -4000)),
                new MobileUnitSnapshot(8, catalog.Require("TR-08"), null, new UnitState(8, "TR-08", 123, 456)),
                new MobileUnitSnapshot(12, catalog.Require("C-01"), null, new UnitState(12, "C-01", 9100, 2200)),
                new MobileUnitSnapshot(50, catalog.Require("C-02"), null, new UnitState(50, "C-02", 7000, 8000))
            };
        }

        [Test]
        public void ActualRosterIsGroupedByCatalogIdentityAndNeverMoves()
        {
            var owned = Owned();
            var groups = MobileOwnedUnitFormation.Group(owned, id => id != 50);
            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual("루피", groups[0].Unit.Name);
            Assert.AreEqual("루피", groups[1].Unit.Name);
            Assert.AreEqual("흔함", groups[0].Unit.Grade);
            Assert.AreEqual("초월함", groups[1].Unit.Grade);
            CollectionAssert.AreEqual(new long[] {12, 30}, groups[0].InstanceIds);
            CollectionAssert.AreEqual(new long[] {8}, groups[1].InstanceIds);
            Assert.AreEqual(30, owned[0].InstanceId);
            Assert.AreEqual(-9000, owned[0].Position.X);
            Assert.AreEqual(-4000, owned[0].Position.Y);
            Assert.AreEqual(9100, owned[2].Position.X);
            Assert.AreEqual(7000, owned[3].Position.X);
        }

        [Test]
        public void GroupSelectionCyclesOnlyLiveOwnedInstancesAndRefreshRemovesSoldUnits()
        {
            var owned = Owned();
            var group = MobileOwnedUnitFormation.Group(owned, id => id != 50)[0];
            Assert.AreEqual(12, group.NextSelection(null));
            Assert.AreEqual(30, group.NextSelection(12));
            Assert.AreEqual(12, group.NextSelection(30));
            Assert.IsFalse(group.Contains(8));
            var after = MobileOwnedUnitFormation.Group(new[] { owned[1], owned[2] }, id => true);
            CollectionAssert.AreEqual(new long[] {12}, after[0].InstanceIds);
            Assert.AreEqual(12, after[0].NextSelection(30));
            Assert.AreEqual(0, MobileOwnedUnitFormation.Group(new MobileUnitSnapshot[0], id => true).Count);
        }

        [TestCase(756f, 390f, 460f)]
        [TestCase(1920f, 1080f, 440f)]
        [TestCase(2288f, 1080f, 440f)]
        [TestCase(1152f, 700f, 320f)]
        public void AllPagesStayCenteredInsideTheMainBattleWithoutOverlap(float width, float height, float dpi)
        {
            var safe = new Rect(18, 0, width - 36, height);
            float scale = MobileMapSkillLayout.UiScale(safe, dpi);
            var battle = MobileMapSkillLayout.Calculate(safe, height, scale).Battle;
            const int groups = 17;
            var first = MobileOwnedFormationLayout.Calculate(battle, scale, groups, 0);
            var covered = new HashSet<int>();
            for (int p = 0; p < first.Pages; p++)
            {
                var page = MobileOwnedFormationLayout.Calculate(battle, scale, groups, p);
                for (int i = 0; i < page.Cells.Length; i++)
                {
                    var cell = page.Cells[i];
                    Assert.GreaterOrEqual(cell.width / scale, 60f - .01f);
                    Assert.AreEqual(cell.width, cell.height, .01f);
                    Assert.GreaterOrEqual(cell.xMin, battle.xMin);
                    Assert.LessOrEqual(cell.xMax, battle.xMax);
                    Assert.GreaterOrEqual(cell.yMin, battle.yMin + 37f * scale - .01f);
                    Assert.LessOrEqual(cell.yMax, page.Footer.yMin);
                    Assert.IsTrue(covered.Add(page.Start + i));
                    float rowMin = cell.xMin, rowMax = cell.xMax;
                    for (int j = 0; j < page.Cells.Length; j++)
                    {
                        if (i != j) Assert.IsFalse(cell.Overlaps(page.Cells[j]));
                        if (Math.Abs(cell.y - page.Cells[j].y) < .01f)
                        { rowMin = Math.Min(rowMin, page.Cells[j].xMin); rowMax = Math.Max(rowMax, page.Cells[j].xMax); }
                    }
                    Assert.AreEqual(battle.center.x, (rowMin + rowMax) * .5f, .01f);
                }
            }
            Assert.AreEqual(groups, covered.Count);
            Assert.AreEqual(first.Pages - 1,
                MobileOwnedFormationLayout.Calculate(battle, scale, groups, int.MaxValue).Page);
            Assert.AreEqual(0, MobileOwnedFormationLayout.Calculate(battle, scale, groups, -9).Page);
        }

        [Test]
        public void CompactPortraitsReserveReadableNameAndQuantityAreas()
        {
            var battle = new Rect(0f, 0f, 460f, 300f);
            var layout = MobileOwnedFormationLayout.Calculate(battle, 1f, 10, 0);
            Assert.AreEqual(3, layout.Capacity);
            Assert.Greater(layout.Pages, 1);
            foreach (var cell in layout.Cells)
            {
                Assert.AreEqual(132f, cell.width, .01f);
                float portrait = MobileOwnedFormationLayout.PortraitDiameterPixels(cell, 1f);
                Assert.LessOrEqual(portrait, 94f);
                var art = MobileOwnedFormationLayout.PortraitRect(cell, 1f);
                var name = MobileOwnedFormationLayout.NameRect(cell, 1f);
                var quantity = MobileOwnedFormationLayout.QuantityRect(cell, 1f);
                Assert.AreEqual(portrait, art.width, .01f);
                Assert.Less(art.yMax, name.yMin);
                Assert.Greater(name.height, 30f);
                Assert.Greater(quantity.height, 20f);
                Assert.GreaterOrEqual(quantity.xMin, cell.xMin);
                Assert.LessOrEqual(quantity.xMax, cell.xMax);
                Assert.LessOrEqual(quantity.yMax, name.yMin);
            }
            var narrow = new Rect(0f, 0f, 110f, 85f);
            var narrowArt = MobileOwnedFormationLayout.PortraitRect(narrow, 1f);
            var narrowName = MobileOwnedFormationLayout.NameRect(narrow, 1f);
            var narrowCount = MobileOwnedFormationLayout.QuantityRect(narrow, 1f);
            Assert.Less(narrowArt.yMax, narrowName.yMin);
            Assert.LessOrEqual(narrowCount.xMax, narrow.xMax);
            Assert.Greater(narrowName.height, 25f);
        }

        [TestCase(756f, 390f, 460f)]
        [TestCase(1152f, 700f, 320f)]
        [TestCase(1920f, 1080f, 440f)]
        public void TextAndQuantityStayInsideEveryCompactFormationSlot(
            float width, float height, float dpi)
        {
            var safe = new Rect(18f, 0f, width - 36f, height);
            float scale = MobileMapSkillLayout.UiScale(safe, dpi);
            Rect battle = MobileMapSkillLayout.Calculate(safe, height, scale).Battle;
            var layout = MobileOwnedFormationLayout.Calculate(battle, scale, 27, 0);
            Assert.Greater(layout.Capacity, 0);
            foreach (var cell in layout.Cells)
            {
                var art = MobileOwnedFormationLayout.PortraitRect(cell, scale);
                var name = MobileOwnedFormationLayout.NameRect(cell, scale);
                var quantity = MobileOwnedFormationLayout.QuantityRect(cell, scale);
                Assert.LessOrEqual(art.width, 94f * scale + .01f);
                Assert.LessOrEqual(art.yMax, name.yMin,
                    "Name must never cover the character portrait");
                Assert.Greater(name.height, 30f,
                    "At least one full readable line must remain");
                Assert.GreaterOrEqual(name.xMin, cell.xMin);
                Assert.LessOrEqual(name.xMax, cell.xMax);
                Assert.LessOrEqual(name.yMax, cell.yMax);
                Assert.Greater(quantity.height, 0f);
                Assert.GreaterOrEqual(quantity.yMin, cell.yMin);
                Assert.LessOrEqual(quantity.yMax, cell.yMax);
                Assert.GreaterOrEqual(quantity.xMin, cell.xMin);
                Assert.LessOrEqual(quantity.xMax, cell.xMax);
            }
        }

        [TestCase(100f, 50f)]
        [TestCase(50f, 100f)]
        [TestCase(2000f, 800f)]
        [TestCase(72f, 72f)]
        public void EveryPortraitAspectFitsInsideBothMainFormationAndMenuRow(
            float sourceWidth, float sourceHeight)
        {
            var cell = new Rect(36f, 40f, 130f, 130f);
            var frame = MobileOwnedFormationLayout.PortraitRect(cell, 1f);
            var inside = MobileSpritePortraitDisplay.Inset(frame, 4f);
            var image = MobileSpritePortraitDisplay.FitInside(
                inside, new Vector2(sourceWidth, sourceHeight));
            Assert.Greater(image.width, 0f);
            Assert.Greater(image.height, 0f);
            Assert.GreaterOrEqual(image.xMin, frame.xMin);
            Assert.GreaterOrEqual(image.yMin, frame.yMin);
            Assert.LessOrEqual(image.xMax, frame.xMax);
            Assert.LessOrEqual(image.yMax, frame.yMax);
            Assert.LessOrEqual(image.yMax,
                MobileOwnedFormationLayout.NameRect(cell, 1f).yMin);
            Assert.That(image.width / image.height,
                Is.EqualTo(sourceWidth / sourceHeight).Within(.001f));

            var button = new Rect(635f, 190f, 310f, 72f);
            var thumb = new Rect(button.x + 7f, button.y + 10f, 52f, 52f);
            var listPortrait = MobileSpritePortraitDisplay.FitInside(
                MobileSpritePortraitDisplay.Inset(thumb, 3f),
                new Vector2(sourceWidth, sourceHeight));
            Assert.GreaterOrEqual(listPortrait.xMin, button.xMin);
            Assert.LessOrEqual(listPortrait.xMax, button.xMax);
            Assert.GreaterOrEqual(listPortrait.yMin, button.yMin);
            Assert.LessOrEqual(listPortrait.yMax, button.yMax);
        }

        [Test]
        public void EmptyOrShrinkingRosterClampsItsPageWithoutInventingCharacters()
        {
            var area = new Rect(200, 80, 320, 210);
            var empty = MobileOwnedFormationLayout.Calculate(area, 1, 0, 100);
            Assert.AreEqual(0, empty.Cells.Length);
            Assert.AreEqual(0, empty.Page);
            var single = MobileOwnedFormationLayout.Calculate(area, 1, 1, 100);
            Assert.AreEqual(1, single.Cells.Length);
            Assert.AreEqual(area.center.x, single.Cells[0].center.x, .01f);
        }
        [Test]
        public void CameraMovementAndZoomNeverExposeOutsideTheFixedBattlefield()
        {
            var board = new Rect(-100f, -60f, 200f, 120f);
            Assert.AreEqual(60f, MobileBattlefieldCameraNavigation
                .MaximumContainedHalfHeight(board, 1.5f), .001f);
            Assert.AreEqual(50f, MobileBattlefieldCameraNavigation
                .MaximumContainedHalfHeight(board, 2f), .001f);
            Vector2 center;
            Assert.IsTrue(MobileBattlefieldCameraNavigation.TryClampToWorldBounds(
                new Vector2(999f, 999f), 30f, 1.5f, board, out center));
            Assert.AreEqual(55f, center.x, .001f);
            Assert.AreEqual(30f, center.y, .001f);
            Assert.IsTrue(MobileBattlefieldCameraNavigation.TryClampToWorldBounds(
                new Vector2(-999f, -999f), 30f, 1.5f, board, out center));
            Assert.AreEqual(-55f, center.x, .001f);
            Assert.AreEqual(-30f, center.y, .001f);
            Assert.IsTrue(MobileBattlefieldCameraNavigation.TryClampToWorldBounds(
                new Vector2(40f, -20f), 60f, 1.5f, board, out center));
            // At the vertical zoom limit the camera is locked to y=0,
            // but a horizontal margin of 10 still exists (100 - 60*1.5).
            Assert.AreEqual(new Vector2(10f, 0f), center);
            Assert.IsTrue(MobileBattlefieldCameraNavigation.TryClampToWorldBounds(
                new Vector2(-40f, 20f), 60f, 1.5f, board, out center));
            Assert.AreEqual(new Vector2(-10f, 0f), center);
            Assert.IsTrue(MobileBattlefieldCameraNavigation.TryClampToWorldBounds(
                new Vector2(40f, -20f), 50f, 2f, board, out center));
            Assert.AreEqual(new Vector2(0f, -10f), center);
            Assert.IsFalse(MobileBattlefieldCameraNavigation.TryClampToWorldBounds(
                Vector2.zero, 61f, 1.5f, board, out center));
        }

        [Test]
        public void CameraMathRejectsInvalidBoardWithoutChangingCombatState()
        {
            var invalid = new Rect(0f, 0f, 0f, 20f);
            Assert.AreEqual(0f, MobileBattlefieldCameraNavigation
                .MaximumContainedHalfHeight(invalid, 1.3f));
            Vector2 center;
            Assert.IsFalse(MobileBattlefieldCameraNavigation.TryClampToWorldBounds(
                new Vector2(3f, 4f), 10f, 1.3f, invalid, out center));
            Assert.AreEqual(new Vector2(3f, 4f), center);
        }

    }
}
