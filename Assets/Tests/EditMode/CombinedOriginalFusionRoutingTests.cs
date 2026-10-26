using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class CombinedOriginalFusionRoutingTests
    {
        // Routing-only stand-ins. These do not authorize any source JASS
        // recipe or assert that Warcraft's original crafting rules are done.
        private sealed class Route : IOriginalFusionPort, IOriginalFusionCommitCapability
        {
            public readonly string RecipeId;
            public bool CanCommit { get; set; }
            public int CommitCalls;
            public bool ReturnNull;
            public bool SuppressOffer;

            public Route(string recipeId, bool executable)
            {
                RecipeId = recipeId;
                CanCommit = executable;
            }

            public IReadOnlyList<MobileFusionPreview> AvailableRecipes(
                IReadOnlyList<MobileUnitSnapshot> owned)
            {
                return SuppressOffer
                    ? new MobileFusionPreview[0]
                    : new[] { new MobileFusionPreview(RecipeId,
                        "원본 조합 " + RecipeId, "원본 재료·비용",
                        new long[] { 101 }) };
            }

            public ManualActionResult ConfirmFusion(string id,
                IReadOnlyList<long> selectedIds)
            {
                CommitCalls++;
                if (ReturnNull) return null;
                if (id != RecipeId || selectedIds.Count != 1 ||
                    selectedIds[0] != 101)
                    return new ManualActionResult(ManualActionState.Rejected,
                        "선택한 재료가 다릅니다.");
                return new ManualActionResult(ManualActionState.Succeeded,
                    "검증된 거래 제공자가 성공을 보고함");
            }
        }

        [Test]
        public void ThreeSourcesStayVisibleAndJassConfirmationRoutesOnlyToJass()
        {
            var regular = new Route("REG-01", false);
            var jass = new Route("A09G", true);
            var mystic = new Route("MY-01", false);
            var combined = new CombinedOriginalFusionPort(regular, jass, mystic);
            var manual = new MobileManualInteraction();
            manual.Configure(null, combined, null);

            var shown = manual.PreviewFusion(new MobileUnitSnapshot[0]);
            CollectionAssert.AreEqual(new[] { "A09G", "MY-01", "REG-01" },
                new[] { shown[0].RecipeId, shown[1].RecipeId, shown[2].RecipeId });
            Assert.IsTrue(manual.HasExecutableFusionService);
            Assert.IsTrue(manual.CanExecuteFusionRecipe("A09G"));
            Assert.IsFalse(manual.CanExecuteFusionRecipe("REG-01"));
            Assert.IsFalse(manual.CanExecuteFusionRecipe("MY-01"));
            Assert.IsFalse(manual.CanExecuteFusionRecipe("missing"));
            Assert.AreEqual(ManualActionState.NotImplemented,
                combined.ConfirmFusion("MY-01", new long[] { 101 }).State);
            Assert.AreEqual(0, mystic.CommitCalls);

            Assert.AreEqual(ManualActionState.Succeeded,
                manual.ConfirmFusion(shown[0]).State);
            Assert.AreEqual(1, jass.CommitCalls);
            Assert.AreEqual(0, regular.CommitCalls);
            Assert.AreEqual(0, mystic.CommitCalls);
        }

        [Test]
        public void RebuiltPreviewRejectsStaleRecipeAndKeepsIndependentCapabilities()
        {
            var regular = new Route("REG-01", false);
            var jass = new Route("A09G", true);
            var mystic = new Route("MY-01", false);
            var combined = new CombinedOriginalFusionPort(regular, jass, mystic);
            var manual = new MobileManualInteraction();
            manual.Configure(null, combined, null);
            Assert.AreEqual(3, manual.PreviewFusion(
                new MobileUnitSnapshot[0]).Count);

            jass.SuppressOffer = true;
            Assert.AreEqual(2, manual.PreviewFusion(
                new MobileUnitSnapshot[0]).Count);
            Assert.IsFalse(manual.CanExecuteFusionRecipe("A09G"));
            Assert.AreEqual(ManualActionState.Rejected,
                combined.ConfirmFusion("A09G", new long[] { 101 }).State);
            Assert.AreEqual(0, jass.CommitCalls);
            Assert.IsTrue(manual.HasExecutableFusionService);
            jass.CanCommit = false;
            Assert.IsFalse(manual.HasExecutableFusionService);
            Assert.IsFalse(manual.CanExecuteFusionRecipe("REG-01"));
        }

        [Test]
        public void MissingDuplicateOrNullProvidersAndRecipesFailClosed()
        {
            var a = new Route("DUP", true);
            var b = new Route("DUP", true);
            Assert.Throws<ArgumentException>(() =>
                new CombinedOriginalFusionPort(a));
            Assert.Throws<ArgumentException>(() =>
                new CombinedOriginalFusionPort(a, a));
            Assert.Throws<ArgumentException>(() =>
                new CombinedOriginalFusionPort(a, (IOriginalFusionPort)null));
            Assert.Throws<InvalidOperationException>(() =>
                new CombinedOriginalFusionPort(a, b)
                    .AvailableRecipes(new MobileUnitSnapshot[0]));
            Assert.AreEqual(0, a.CommitCalls);
            Assert.AreEqual(0, b.CommitCalls);
        }

        [Test]
        public void OriginalTransactionFailureOrMissingReceiptCannotBecomeSuccess()
        {
            var normal = new Route("REG-01", false);
            var jass = new Route("A09G", true) { ReturnNull = true };
            var mystic = new Route("MY-01", false);
            var combined = new CombinedOriginalFusionPort(normal, jass, mystic);
            combined.AvailableRecipes(new MobileUnitSnapshot[0]);
            Assert.AreEqual(ManualActionState.Rejected,
                combined.ConfirmFusion("A09G", new long[] { 101 }).State);
            Assert.AreEqual(1, jass.CommitCalls);
            Assert.AreEqual(0, normal.CommitCalls);
            Assert.AreEqual(0, mystic.CommitCalls);
        }
    }
}
