using System;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalJassReadOnlyFusionPreviewTests
    {
        private static CharacterUnitCatalog Units()
        {
            var asset = Resources.Load<TextAsset>("OrdrData/units");
            Assert.IsNotNull(asset);
            return CharacterUnitCatalog.FromJson(asset.text);
        }

        private static OriginalJassFusionCatalog Registrations()
        {
            var asset = Resources.Load<TextAsset>(
                "OrdrData/original-jass-fusion-registry");
            Assert.IsNotNull(asset);
            return OriginalJassFusionCatalog.FromJson(asset.text);
        }

        private static OriginalFusionObjectPreviewCatalog Names(
            OriginalJassFusionCatalog registrations, CharacterUnitCatalog units)
        {
            var asset = Resources.Load<TextAsset>(
                "OrdrData/original-fusion-object-reconciliation");
            Assert.IsNotNull(asset,
                "Unity 데이터 동기화에서 읽기 전용 원본 객체 매핑이 누락됐습니다.");
            return OriginalFusionObjectPreviewCatalog.FromJson(
                asset.text, registrations, units);
        }

        private static MobileUnitSnapshot Unit(CharacterUnitCatalog catalog,
            long id, string code)
        {
            return new MobileUnitSnapshot(id, catalog.Require(code), null,
                new UnitState(id, code, 0, 0));
        }

        private static MobileFusionPreview Find(
            OriginalJassReadOnlyFusionPort port,
            MobileUnitSnapshot[] inventory, string ability)
        {
            foreach (var recipe in port.AvailableRecipes(inventory))
                if (recipe.RecipeId == ability) return recipe;
            Assert.Fail("원본 CWR 등록식 누락: " + ability);
            return null;
        }

        [Test]
        public void All265OriginalRegistrationRecipesShowInMobileCatalog()
        {
            var registrations = Registrations();
            var units = Units();
            var names = Names(registrations, units);
            Assert.AreEqual(280, names.Count);
            Assert.AreEqual(265, registrations.Count);
            var port = new OriginalJassReadOnlyFusionPort(
                registrations, names, id => false);
            var offers = port.AvailableRecipes(new MobileUnitSnapshot[0]);
            Assert.AreEqual(registrations.Count, offers.Count);
            Assert.IsFalse(port.CanCommit);
            foreach (var offer in offers)
                Assert.IsTrue(offer.OriginalMaterialsAndCost.Contains(
                    "원본 제작 거래 미연결"));
        }

        [Test]
        public void ActualCommonMaterialsShowOnlyUnitReadinessWithoutGrantingFusion()
        {
            var units = Units();
            var registrations = Registrations();
            var port = new OriginalJassReadOnlyFusionPort(registrations,
                Names(registrations, units), id => false);
            var offer = Find(port, new[] {
                Unit(units, 30, "C-01"), Unit(units, 31, "C-08")
            }, "A00D");
            Assert.IsTrue(offer.Title.Contains("안흔함"));
            Assert.IsTrue(offer.OriginalMaterialsAndCost.Contains("루피"));
            Assert.IsTrue(offer.OriginalMaterialsAndCost.Contains("총병"));
            Assert.IsTrue(offer.OriginalMaterialsAndCost.Contains("특수 조건 3개"));
            CollectionAssert.AreEquivalent(new long[] { 30, 31 },
                offer.SuggestedMaterialInstances);
            Assert.IsFalse(offer.IsReady,
                "읽기 전용 재료가 모두 있어도 미승인 거래는 제작 준비로 표시하지 않습니다.");
            Assert.AreEqual(1, offer.MissingMaterials.Count,
                "실제 재료 부족 없이 제작 미승인 사유만 남아야 합니다.");
            Assert.AreEqual(ManualActionState.NotImplemented,
                port.ConfirmFusion("A00D", offer.SuggestedMaterialInstances).State);
            var manual = new MobileManualInteraction();
            manual.Configure(null, port, null);
            Assert.IsFalse(manual.HasExecutableFusionService);
            Assert.IsFalse(manual.CanExecuteFusionRecipe("A00D"));
        }

        [Test]
        public void StoryDispatchAndUnknownOriginalObjectBlockMaterialReadiness()
        {
            var units = Units();
            var registrations = Registrations();
            var names = Names(registrations, units);
            var port = new OriginalJassReadOnlyFusionPort(registrations,
                names, id => id == 30);
            var offer = Find(port, new[] {
                Unit(units, 30, "C-01"), Unit(units, 31, "C-08")
            }, "A00D");
            Assert.IsFalse(offer.IsReady);
            Assert.IsTrue(offer.MissingMaterials.Count > 0);
            Assert.AreEqual("KM-01", names.Find("h04S").CatalogUnitId,
                "쿠마 객체 매핑과 실제 획득 경로의 구현은 별개입니다.");
            var unknown = names.Find("h05Y");
            Assert.IsNull(unknown.CatalogUnitId);
            Assert.IsFalse(port.CanCommit);
        }

        [Test]
        public void DuplicateUnitInstanceOrPromotedObjectCandidateFailsClosed()
        {
            var units = Units();
            var registrations = Registrations();
            var names = Names(registrations, units);
            var port = new OriginalJassReadOnlyFusionPort(
                registrations, names, id => false);
            Assert.Throws<ArgumentException>(() =>
                port.AvailableRecipes(new[] {
                    Unit(units, 1, "C-01"), Unit(units, 1, "C-08")
                }));
            var asset = Resources.Load<TextAsset>(
                "OrdrData/original-fusion-object-reconciliation");
            Assert.Throws<FormatException>(() =>
                OriginalFusionObjectPreviewCatalog.FromJson(
                    asset.text.Replace(
                        "\"authorizedForFusionExecution\": false",
                        "\"authorizedForFusionExecution\": true"),
                    registrations, units));
            Assert.Throws<FormatException>(() =>
                OriginalFusionObjectPreviewCatalog.FromJson(
                    asset.text.Replace(registrations.SourceSha256,
                        new string('0', 64)),
                    registrations, units));
        }
    }
}
