using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class OtherworldMysticFusionCatalogTests
    {
        private const string UnitsJson =
            "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[" +
            "{\"id\":\"MY-01\",\"grade\":\"신비함\",\"name\":\"고죠 사토루\",\"roleTags\":[]}," +
            "{\"id\":\"RN-01\",\"grade\":\"랜덤\",\"name\":\"K\",\"roleTags\":[]}," +
            "{\"id\":\"RN-02\",\"grade\":\"랜덤\",\"name\":\"나루토\",\"roleTags\":[]}," +
            "{\"id\":\"D-02\",\"grade\":\"왜곡됨\",\"name\":\"코알라\",\"roleTags\":[]}]}";

        private const string RecipeJson =
            "{\"format\":\"ordr-otherworld-mystic-web-recipes-v1\"," +
            "\"gameVersion\":\"S2 2.323[C]\",\"mode\":\"nightmare-otherworld\"," +
            "\"sourceVersion\":\"2.323\",\"catalogGrade\":\"신비함\"," +
            "\"authorizedForConsumption\":false,\"originalJassConditionsVerified\":false," +
            "\"recipeCount\":1,\"recipes\":[{" +
            "\"resultUnitId\":\"MY-01\",\"resultName\":\"고죠 사토루\"," +
            "\"sourceUrl\":\"https://ordr.kr/character/MY1\"," +
            "\"gold\":10000,\"lumber\":7," +
            "\"specialItems\":[\"해적선\",\"행운의토큰\"]," +
            "\"ingredients\":[{\"kind\":\"unit\",\"unitId\":\"RN-01\",\"count\":1}," +
            "{\"kind\":\"any-random\",\"count\":1}," +
            "{\"kind\":\"unit\",\"unitId\":\"D-02\",\"count\":1}]}]}";

        private static OtherworldMysticFusionCatalog Read(string json = null)
        {
            return OtherworldMysticFusionCatalog.FromJson(json ?? RecipeJson,
                CharacterUnitCatalog.FromJson(UnitsJson));
        }

        [Test]
        public void WebEvidencePreservesNamedSpecialRequirementsAndPreventsConsumption()
        {
            var catalog = Read();
            Assert.AreEqual(1, catalog.Count);
            var recipe = catalog.Find("MY-01");
            Assert.AreEqual("고죠 사토루", recipe.ResultName);
            Assert.AreEqual(10000, recipe.Gold);
            Assert.AreEqual(7, recipe.Lumber);
            CollectionAssert.AreEqual(new[] { "해적선", "행운의토큰" }, recipe.SpecialRequirements);
            Assert.IsFalse(recipe.AuthorizedForConsumption);
            Assert.IsFalse(catalog.Preview("MY-01",
                new Dictionary<string, int> { { "RN-01", 2 }, { "D-02", 1 } }).CanExecute);
        }

        [Test]
        public void WildcardMustUseSeparateRandomUnitAfterFixedMaterial()
        {
            var catalog = Read();
            var onlyOne = catalog.Preview("MY-01",
                new Dictionary<string, int> { { "RN-01", 1 }, { "D-02", 1 } });
            Assert.IsFalse(onlyOne.HasAllUnitMaterials);
            CollectionAssert.AreEqual(new[] { "랜덤 등급 유닛 1기" }, onlyOne.MissingUnitMaterials);

            Assert.IsTrue(catalog.Preview("MY-01", new Dictionary<string, int>
                { { "RN-01", 2 }, { "D-02", 1 } }).HasAllUnitMaterials);
            Assert.IsTrue(catalog.Preview("MY-01", new Dictionary<string, int>
                { { "RN-01", 1 }, { "RN-02", 1 }, { "D-02", 1 } }).HasAllUnitMaterials);
        }

        [Test]
        public void MissingFixedIngredientCannotBeReplacedByWildcard()
        {
            var preview = Read().Preview("MY-01",
                new Dictionary<string, int> { { "RN-01", 2 } });
            Assert.IsFalse(preview.HasAllUnitMaterials);
            CollectionAssert.AreEqual(new[] { "코알라 1기" }, preview.MissingUnitMaterials);
        }

        [Test]
        public void ImportRejectsFalseAuthorizationAndUnknownOriginalConditions()
        {
            Assert.Throws<FormatException>(() => Read(RecipeJson.Replace(
                "\"authorizedForConsumption\":false", "\"authorizedForConsumption\":true")));
            Assert.Throws<FormatException>(() => Read(RecipeJson.Replace(
                "\"originalJassConditionsVerified\":false",
                "\"originalJassConditionsVerified\":true")));
            Assert.Throws<FormatException>(() => Read(RecipeJson.Replace(
                "\"mode\":\"nightmare-otherworld\"", "\"mode\":\"nightmare\"")));
            Assert.Throws<FormatException>(() => Read(RecipeJson.Replace(
                "\"kind\":\"any-random\",\"count\":1",
                "\"kind\":\"any-random\",\"unitId\":\"RN-02\",\"count\":1")));
        }

        [Test]
        public void InvalidInventoryFailsClosed()
        {
            Assert.Throws<ArgumentException>(() => Read().Preview("MY-01",
                new Dictionary<string, int> { { "RN-01", -1 } }));
            Assert.Throws<ArgumentException>(() => Read().Preview("MY-01",
                new Dictionary<string, int> { { "RN-999", 1 } }));
        }

        [Test]
        public void MobilePreviewAllocatesDistinctInstancesAndExcludesStoryDispatch()
        {
            var units = CharacterUnitCatalog.FromJson(UnitsJson);
            var port = new OtherworldMysticFusionPreviewPort(Read(), id => id == 4);
            var preview = port.AvailableRecipes(new[]
            {
                new MobileUnitSnapshot(1, units.Require("RN-01"), null, null),
                new MobileUnitSnapshot(2, units.Require("D-02"), null, null),
                new MobileUnitSnapshot(3, units.Require("RN-02"), null, null),
                new MobileUnitSnapshot(4, units.Require("RN-02"), null, null)
            });
            Assert.AreEqual(1, preview.Count);
            CollectionAssert.AreEquivalent(new long[] { 1, 2, 3 }, preview[0].SuggestedMaterialInstances);
            Assert.IsFalse(preview[0].IsReady);
            Assert.IsFalse(new MobileManualInteraction().HasFusionService);

            var manual = new MobileManualInteraction();
            manual.Configure(null, port, null);
            Assert.IsTrue(manual.HasFusionService);
            Assert.IsFalse(manual.HasExecutableFusionService);
            Assert.AreEqual(ManualActionState.NotImplemented,
                port.ConfirmFusion("MY-01", preview[0].SuggestedMaterialInstances).State);
        }

        [Test]
        public void MobilePreviewDoesNotReuseOneRandomInstanceTwice()
        {
            var units = CharacterUnitCatalog.FromJson(UnitsJson);
            var port = new OtherworldMysticFusionPreviewPort(Read(), id => false);
            var preview = port.AvailableRecipes(new[]
            {
                new MobileUnitSnapshot(1, units.Require("RN-01"), null, null),
                new MobileUnitSnapshot(2, units.Require("D-02"), null, null)
            })[0];
            CollectionAssert.AreEquivalent(new long[] { 1, 2 }, preview.SuggestedMaterialInstances);
            CollectionAssert.Contains(preview.MissingMaterials, "랜덤 등급 유닛 1기");
            Assert.IsFalse(preview.IsReady);
        }
    }
}
