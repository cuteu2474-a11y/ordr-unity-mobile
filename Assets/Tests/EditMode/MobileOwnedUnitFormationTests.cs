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
                    Assert.GreaterOrEqual(cell.width / scale, 100f - .01f);
                    Assert.GreaterOrEqual(cell.height / scale, 44f - .01f);
                    Assert.GreaterOrEqual(cell.xMin, battle.xMin);
                    Assert.LessOrEqual(cell.xMax, battle.xMax);
                    Assert.GreaterOrEqual(cell.yMin, battle.yMin + 25f * scale - .01f);
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
    }
}
