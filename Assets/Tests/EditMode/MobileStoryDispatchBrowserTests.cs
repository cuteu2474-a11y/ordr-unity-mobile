using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileStoryDispatchBrowserTests
    {
        private static MobileUnitSnapshot[] Owned()
        {
            var units = CharacterUnitCatalog.FromJson(File.ReadAllText(
                Path.Combine(Application.dataPath, "..", "data", "units.json")));
            return new[] {
                new MobileUnitSnapshot(11, units.Require("C-01"), null,
                    new UnitState(11, "C-01", 0, 0)),
                new MobileUnitSnapshot(12, units.Require("S-02"), null,
                    new UnitState(12, "S-02", 0, 0)),
                new MobileUnitSnapshot(13, units.Require("C-08"), null,
                    new UnitState(13, "C-08", 0, 0))
            };
        }

        [Test]
        public void OnlyRealSourceEligibleUnitsEnterDispatchViews()
        {
            int deployed, ready, recall, count, pages, page;
            var owned = Owned();
            var all = MobileStoryDispatchBrowser.Select(owned,
                id => id == 11, id => id == 12, id => id == 11,
                MobileStoryRosterFilter.All, 0, 8,
                out deployed, out ready, out recall, out count,
                out pages, out page);
            Assert.AreEqual(2, count,
                "All shows already deployed units and currently eligible candidates only.");
            Assert.AreEqual(1, deployed);
            Assert.AreEqual(1, ready);
            Assert.AreEqual(1, recall);
            Assert.AreEqual(11, all[0].Unit.InstanceId);
            Assert.AreEqual(12, all[1].Unit.InstanceId);
            Assert.IsTrue(all[0].CanRecall);
            Assert.IsFalse(all[0].CanDispatch);
            Assert.IsTrue(all[1].CanDispatch);
            Assert.IsFalse(all[0].CanDispatch);
            Assert.IsFalse(all[1].IsDeployed);
            Assert.IsFalse(all.Count == 3,
                "An ineligible third owned unit must remain hidden.");

            var readyOnly = MobileStoryDispatchBrowser.Select(owned,
                id => id == 11, id => id == 12, id => id == 11,
                MobileStoryRosterFilter.ReadyToDispatch, 0, 8,
                out deployed, out ready, out recall, out count,
                out pages, out page);
            Assert.AreEqual(1, count);
            Assert.AreEqual(12, readyOnly[0].Unit.InstanceId);
            Assert.AreEqual(3, owned.Length, "읽기 전용 UI가 보유 유닛을 바꾸면 안 됩니다.");

            var deployedOnly = MobileStoryDispatchBrowser.Select(owned,
                id => id == 11, id => id == 12, id => id == 11,
                MobileStoryRosterFilter.Deployed, 9, 8,
                out deployed, out ready, out recall, out count,
                out pages, out page);
            Assert.AreEqual(0, page);
            Assert.AreEqual(11, deployedOnly[0].Unit.InstanceId);
        }

        [Test]
        public void StoryDispatchGroupsOnlyMatchingRealStateAndActsOnOneInstance()
        {
            var catalog = CharacterUnitCatalog.FromJson(File.ReadAllText(
                Path.Combine(Application.dataPath, "..", "data", "units.json")));
            var owned = new[] {
                new MobileUnitSnapshot(20, catalog.Require("C-01"), null,
                    new UnitState(20, "C-01", 0, 0)),
                new MobileUnitSnapshot(21, catalog.Require("C-01"), null,
                    new UnitState(21, "C-01", 0, 0)),
                new MobileUnitSnapshot(22, catalog.Require("C-01"), null,
                    new UnitState(22, "C-01", 0, 0)),
                new MobileUnitSnapshot(23, catalog.Require("C-01"), null,
                    new UnitState(23, "C-01", 0, 0)),
                new MobileUnitSnapshot(24, catalog.Require("C-08"), null,
                    new UnitState(24, "C-08", 0, 0))
            };
            bool firstDenied = false;
            int deployed, ready, recall, units, groups, pages, page;
            var shown = MobileStoryDispatchBrowser.Group(owned,
                id => id == 23,
                id => id == 20 || id == 21 || id == 22 || id == 24,
                id => id == 23,
                actor => actor.InstanceId == 22 ? "special-buff" : "base",
                MobileStoryRosterFilter.All, 0, 8,
                out deployed, out ready, out recall, out units,
                out groups, out pages, out page);
            Assert.AreEqual(1, deployed);
            Assert.AreEqual(4, ready);
            Assert.AreEqual(5, units);
            Assert.AreEqual(4, groups,
                "Two common units share only exact dispatch and status keys.");
            Assert.AreEqual(2, shown[1].Count);
            Assert.AreEqual(20, shown[1].First.Unit.InstanceId);
            Assert.AreEqual(23, shown[0].First.Unit.InstanceId);
            Assert.IsTrue(shown[0].IsDeployed);
            Assert.IsFalse(shown[1].IsDeployed);
            Assert.AreEqual(21, shown[1].FirstCurrentlyActionable(
                id => id == 21).Value,
                "When the first actor loses eligibility, one real eligible member is used.");
            firstDenied = true;
            Assert.IsNull(shown[1].FirstCurrentlyActionable(id => !firstDenied));
            Assert.AreEqual(5, owned.Length,
                "Read-only grouping must not remove/copy roster instances.");

            var page2 = MobileStoryDispatchBrowser.Group(owned,
                id => id == 23,
                id => id == 20 || id == 21 || id == 22 || id == 24,
                id => id == 23,
                actor => actor.InstanceId == 22 ? "special-buff" : "base",
                MobileStoryRosterFilter.All, 9, 2,
                out deployed, out ready, out recall, out units,
                out groups, out pages, out page);
            Assert.AreEqual(2, pages);
            Assert.AreEqual(1, page);
            Assert.AreEqual(2, page2.Count);
        }

        [Test]
        public void StoryDispatchGroupingRespectsSelectedFilterAndRejectsMissingState()
        {
            var owned = Owned();
            int deployed, ready, recall, units, groups, pages, page;
            var readyOnly = MobileStoryDispatchBrowser.Group(owned,
                id => id == 11,
                id => id == 12,
                id => id == 11,
                actor => "normal",
                MobileStoryRosterFilter.ReadyToDispatch, 0, 8,
                out deployed, out ready, out recall, out units,
                out groups, out pages, out page);
            Assert.AreEqual(1, units);
            Assert.AreEqual(1, groups);
            Assert.AreEqual(12, readyOnly[0].First.Unit.InstanceId);
            var deployedOnly = MobileStoryDispatchBrowser.Group(owned,
                id => id == 11,
                id => id == 12,
                id => id == 11,
                actor => "normal",
                MobileStoryRosterFilter.Deployed, 0, 8,
                out deployed, out ready, out recall, out units,
                out groups, out pages, out page);
            Assert.AreEqual(1, groups);
            Assert.AreEqual(11, deployedOnly[0].First.Unit.InstanceId);
            Assert.Throws<ArgumentException>(() => MobileStoryDispatchBrowser.Group(owned,
                id => false, id => true, id => false, actor => "",
                MobileStoryRosterFilter.All, 0, 8,
                out deployed, out ready, out recall, out units,
                out groups, out pages, out page));
        }

        [Test]
        public void StoryRecallBatchPreflightsAndReportsPartialWithoutSkippingOriginalPort()
        {
            int calls = 0;
            var denied = MobileManualBatchActions.RecallStoryUnits(
                new long[] { 11, 12 }, id => id == 11, id =>
                {
                    calls++;
                    return new ManualActionResult(ManualActionState.Succeeded, "복귀");
                });
            Assert.AreEqual(ManualActionState.Rejected, denied.State);
            Assert.AreEqual(0, calls, "먼저 모든 대상의 원본 복귀 조건을 검사해야 합니다.");

            var incomplete = MobileManualBatchActions.RecallStoryUnits(
                new long[] { 11, 12 }, id => true, id =>
                {
                    calls++;
                    return new ManualActionResult(id == 11 ?
                        ManualActionState.Succeeded : ManualActionState.Rejected,
                        id == 11 ? "복귀" : "복귀 불가");
                });
            Assert.AreEqual(ManualActionState.Succeeded, incomplete.State);
            StringAssert.Contains("1/2", incomplete.Message);
            Assert.AreEqual(2, calls);
            Assert.Throws<ArgumentNullException>(() =>
                MobileManualBatchActions.RecallStoryUnits(null, id => true, id =>
                    new ManualActionResult(ManualActionState.Succeeded, "복귀")));
        }

        [Test]
        public void StoryRewardPreviewUsesTheActualBasePayoutWithoutFakeMvp()
        {
            var first = Original323SoloStoryScenario.BaseRewardSummary(1);
            StringAssert.Contains("골드 180", first);
            StringAssert.Contains("선택위습 3", first);
            StringAssert.DoesNotContain("500", first, "MVP 보너스는 기본 보상이 아닙니다.");
            StringAssert.Contains("초월위습 1회",
                Original323SoloStoryScenario.BaseRewardSummary(10));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Original323SoloStoryScenario.BaseRewardSummary(0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Original323SoloStoryScenario.BaseRewardSummary(15));
        }
    }
}
