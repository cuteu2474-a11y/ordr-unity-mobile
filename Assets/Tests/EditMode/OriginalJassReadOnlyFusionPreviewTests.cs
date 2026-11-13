using System;
using System.Collections.Generic;
using System.IO;
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
                    "원본 제작 거래 미연결") ||
                    offer.OriginalMaterialsAndCost.Contains(
                        "원본 별도 획득 경로"),
                    "Distinguish ordinary unconnected fusion from native " +
                    "reward routes without approving either transaction.");
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
            Assert.IsTrue(offer.HasAllUnitMaterials,
                "원본 제작 권한은 잠겨도 보유 UNIT 재료는 별도로 확인합니다.");
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
        public void StoryDispatchRemainsOwnedButRequiresRecallBeforeCrafting()
        {
            var units = Units();
            var registrations = Registrations();
            var names = Names(registrations, units);
            var port = new OriginalJassReadOnlyFusionPort(registrations,
                names, id => id == 30);
            var offer = Find(port, new[] {
                Unit(units, 30, "C-01"), Unit(units, 31, "C-08")
            }, "A00D");
            Assert.IsFalse(offer.IsReady, "파견 재료를 자동 소비하지 않습니다.");
            Assert.IsTrue(offer.HasAllUnitMaterials,
                "스토리 파견 유닛도 현재 소유한 재료로 표시합니다.");
            CollectionAssert.AreEquivalent(new long[] { 31 },
                offer.SuggestedMaterialInstances);
            CollectionAssert.AreEquivalent(new long[] { 30 },
                offer.StoryMaterialInstances);
            bool recallExplained = false;
            foreach (var reason in offer.MissingMaterials)
                if (reason.Contains("복귀 필요")) recallExplained = true;
            Assert.IsTrue(recallExplained,
                "조합 조건의 실제 원인은 부족이 아니라 파견 상태여야 합니다.");
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

        private static OriginalMapBaseStatCatalog SourceObjects(CharacterUnitCatalog units)
        {
            return OriginalMapBaseStatCatalog.FromJson(File.ReadAllText(
                Path.Combine(Application.dataPath, "..", "data",
                    "original-2323C-map-base-stats.json")), units);
        }

        [Test]
        public void OriginalSixPickRegistrationsUseA800OwnedOnlyByFourteenRandomObjects()
        {
            var registrations = Registrations();
            int picks = 0;
            foreach (var recipe in registrations.All)
                foreach (var entry in recipe.OrderedConditions)
                    if (entry.Kind == "PICK")
                    {
                        picks++;
                        Assert.AreEqual("A800", entry.Rawcode);
                        Assert.AreEqual(1, entry.Count);
                    }
            Assert.AreEqual(6, picks);
            // Construction audits the original object ability lists, not web
            // assumptions about what the JASS picker ultimately consumes.
            var units = Units();
            var source = SourceObjects(units);
            Assert.DoesNotThrow(() =>
                new Original323MysticPickCandidatePreview(source, units));
        }

        [Test]
        public void PickCandidatePreviewReservesNamedRandomCasterAndStoryDispatch()
        {
            var units = Units();
            var preview = new Original323MysticPickCandidatePreview(
                SourceObjects(units), units);
            OriginalJassFusionInstruction pick = null;
            foreach (var condition in Registrations().FindByAbility("A19W").OrderedConditions)
                if (condition.Kind == "PICK") pick = condition;
            Assert.NotNull(pick);
            var owned = new[]
            {
                Unit(units, 100, "RN-01"), // fixed ingredient of A19W
                Unit(units, 101, "RN-02"), // dispatched from main battlefield
                Unit(units, 102, "C-01"),  // no original A800
                Unit(units, 103, "RN-03"), // only free A800-bearing candidate
                Unit(units, 104, "RN-04")
            };
            var used = new HashSet<long> { 100, 104 };
            IReadOnlyList<long> candidates;
            string label;
            Assert.IsTrue(preview.TryPreview(pick, owned, id => id == 101,
                used, out candidates, out label));
            CollectionAssert.AreEqual(new long[] { 103 }, candidates);
            StringAssert.Contains("A800", label);
            Assert.AreEqual(2, used.Count, "A preview must never mutate a paid-material reservation.");

            // Even with the source-known extra actor, A19W remains read-only.
            var registration = Registrations();
            var sourcePreview = new OriginalJassReadOnlyFusionPort(
                registration, Names(registration, units), id => false);
            Assert.IsFalse(sourcePreview.CanCommit);
            Assert.AreEqual(ManualActionState.NotImplemented,
                sourcePreview.ConfirmFusion("A19W", candidates).State);
        }

        [Test]
        public void PickPreviewNeverReusesAReservedActorOrAnUnavailableSourceCondition()
        {
            var units = Units();
            var source = new Original323MysticPickCandidatePreview(SourceObjects(units), units);
            OriginalJassFusionInstruction pick = null;
            foreach (var condition in Registrations().FindByAbility("A19W").OrderedConditions)
                if (condition.Kind == "PICK") pick = condition;
            IReadOnlyList<long> candidate;
            string title;
            Assert.IsTrue(source.TryPreview(pick, new[] { Unit(units, 5, "RN-01") },
                id => false, new HashSet<long> { 5 }, out candidate, out title));
            Assert.AreEqual(0, candidate.Count,
                "The fixed A19W caster cannot also be paid as its PICK slot.");
            Assert.IsFalse(source.TryPreview(null, new MobileUnitSnapshot[0],
                id => false, new HashSet<long>(), out candidate, out title));
        }
    }
}
