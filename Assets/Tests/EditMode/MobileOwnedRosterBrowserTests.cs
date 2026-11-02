using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileOwnedRosterBrowserTests
    {
        private static MobileUnitSnapshot[] Sample()
        {
            var units = CharacterUnitCatalog.FromJson(File.ReadAllText(
                Path.Combine(Application.dataPath, "..", "data", "units.json")));
            return new[] {
                new MobileUnitSnapshot(12, units.Require("C-01"), null,
                    new UnitState(12, "C-01", 0, 0)),
                new MobileUnitSnapshot(13, units.Require("S-02"), null,
                    new UnitState(13, "S-02", 0, 0)),
                new MobileUnitSnapshot(14, units.Require("C-08"), null,
                    new UnitState(14, "C-08", 0, 0)),
                new MobileUnitSnapshot(15, units.Require("S-11"), null,
                    new UnitState(15, "S-11", 0, 0))
            };
        }
        private static System.Collections.Generic.IReadOnlyList<MobileUnitSnapshot> Browse(
            MobileUnitSnapshot[] entries, string search, OwnedRosterFilter filter,
            int page, out int count, out int pages, out int resolved)
        {
            return MobileOwnedRosterBrowser.Select(entries, search, filter,
                e => e.Unit.Id == "C-01" || e.Unit.Id == "C-08",
                page, 2, out count, out pages, out resolved);
        }

        [Test]
        public void NameIdGradeAndInstanceSearchUseOnlyLiveRoster()
        {
            var all = Sample(); int count, pages, resolved;
            Assert.AreEqual("C-08", Browse(all, "총병", OwnedRosterFilter.All,
                0, out count, out pages, out resolved)[0].Unit.Id);
            Assert.AreEqual(15, Browse(all, "s-11", OwnedRosterFilter.All,
                0, out count, out pages, out resolved)[0].InstanceId);
            Assert.AreEqual(12, Browse(all, "12", OwnedRosterFilter.All,
                0, out count, out pages, out resolved)[0].InstanceId);
            Browse(all, "흔함", OwnedRosterFilter.All,
                0, out count, out pages, out resolved);
            Assert.AreEqual(2, count);
            Assert.AreEqual(4, all.Length);
        }

        [Test]
        public void SaleAndDecompositionFiltersNeverInventEligibleUnits()
        {
            var all = Sample(); int count, pages, resolved;
            var sale = Browse(all, "", OwnedRosterFilter.Saleable,
                0, out count, out pages, out resolved);
            Assert.AreEqual(2, count);
            Assert.AreEqual("C-01", sale[0].Unit.Id);
            var dec = Browse(all, "", OwnedRosterFilter.Decomposable,
                0, out count, out pages, out resolved);
            Assert.AreEqual(2, count);
            Assert.AreEqual("S-02", dec[0].Unit.Id);
            Assert.AreEqual("S-11", dec[1].Unit.Id);
        }

        [Test]
        public void IdenticalCatalogIdsAreGroupedAndInstanceSelectionCycles()
        {
            var sample = Sample();
            var repeated = new[] { sample[0], sample[1], sample[2],
                new MobileUnitSnapshot(16, sample[0].Unit, null,
                    new UnitState(16, "C-01", 0, 0)) };
            int units, groups, pages, resolved;
            var shown = MobileOwnedRosterBrowser.Group(repeated,
                OwnedRosterFilter.All, x => true, 0, 8,
                out units, out groups, out pages, out resolved);
            Assert.AreEqual(4, units);
            Assert.AreEqual(3, groups);
            Assert.AreEqual(2, shown[0].Count);
            Assert.AreEqual(12L, shown[0].NextSelectable(null));
            Assert.AreEqual(16L, shown[0].NextSelectable(12));
            Assert.AreEqual(12L, shown[0].NextSelectable(16));
            Assert.IsTrue(shown[0].Contains(16));
            Assert.IsFalse(shown[0].Contains(13));
        }

        [Test]
        public void GroupFiltersPrecedePaginationAndEmptyResultsClamp()
        {
            var original = Sample();
            var repeated = new[] { original[0], original[1], original[2],
                new MobileUnitSnapshot(16, original[0].Unit, null,
                    new UnitState(16, "C-01", 0, 0)) };
            int units, groups, pages, resolved;
            var first = MobileOwnedRosterBrowser.Group(repeated,
                OwnedRosterFilter.Saleable, x => x.Unit.Id == "C-01" || x.Unit.Id == "C-08",
                0, 1, out units, out groups, out pages, out resolved);
            Assert.AreEqual(3, units);
            Assert.AreEqual(2, groups);
            Assert.AreEqual(2, pages);
            Assert.AreEqual(2, first[0].Count);
            var second = MobileOwnedRosterBrowser.Group(repeated,
                OwnedRosterFilter.Saleable, x => x.Unit.Id == "C-01" || x.Unit.Id == "C-08",
                5, 1, out units, out groups, out pages, out resolved);
            Assert.AreEqual(1, resolved);
            Assert.AreEqual("C-08", second[0].UnitId);
            var empty = MobileOwnedRosterBrowser.Group(
                Array.Empty<MobileUnitSnapshot>(), OwnedRosterFilter.All, x => true,
                5, 8, out units, out groups, out pages, out resolved);
            Assert.AreEqual(0, empty.Count);
            Assert.AreEqual(0, groups);
            Assert.AreEqual(1, pages);
            Assert.AreEqual(0, resolved);
        }

        [Test]
        public void PaginationClampsAfterFusionOrSaleAndHandlesEmptyResults()
        {
            var all = Sample(); int count, pages, resolved;
            var second = Browse(all, "", OwnedRosterFilter.All,
                1, out count, out pages, out resolved);
            Assert.AreEqual(14, second[0].InstanceId);
            var after = Browse(new[] { all[0] }, "", OwnedRosterFilter.All,
                500, out count, out pages, out resolved);
            Assert.AreEqual(0, resolved);
            Assert.AreEqual(1, pages);
            Assert.AreEqual(12, after[0].InstanceId);
            var empty = Browse(all, "없음", OwnedRosterFilter.All,
                5, out count, out pages, out resolved);
            Assert.AreEqual(0, empty.Count);
            Assert.AreEqual(0, resolved);
        }
    }
}
