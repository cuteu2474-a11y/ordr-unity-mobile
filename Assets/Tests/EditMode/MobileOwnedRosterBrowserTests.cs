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
