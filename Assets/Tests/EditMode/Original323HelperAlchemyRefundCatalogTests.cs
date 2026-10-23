using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323HelperAlchemyRefundCatalogTests
    {
        private static string OriginalAlchemyJson()
        {
            var asset = Resources.Load<TextAsset>(
                "OrdrData/original-2323C-helper-alchemy-recipes");
            Assert.IsNotNull(asset, "원본 CJY 재료 환급표가 Unity Resources에 동기화되어야 합니다.");
            return asset.text;
        }

        [Test]
        public void OriginalCJYUnitOnlyRefundCatalogIsAvailable()
        {
            var catalog = Ordr323OriginalAlchemyRefundCatalog.FromJson(OriginalAlchemyJson());
            Assert.AreEqual(265, catalog.Count);
            Assert.AreEqual(143, catalog.ExistingFusionArchiveInterleavingMismatches);
            var recipe = catalog.Find("h00O");
            Assert.IsNotNull(recipe);
            CollectionAssert.AreEqual(new[] { "A006" }, recipe.FusionAbilities);
            Assert.AreEqual(2, recipe.RefundUnits.Count);
            Assert.AreEqual("h001", recipe.RefundUnits[0].Rawcode);
            Assert.AreEqual(1, recipe.RefundUnits[0].Count);
            Assert.AreEqual("h004", recipe.RefundUnits[1].Rawcode);
            Assert.Greater(recipe.IgnoredNonUnitConditions, 0);
            // A rawcode not registered by Bi8 has no R3 entries in original CJY.
            Assert.IsNull(catalog.Find("XXXX"));
        }

        [Test]
        public void TamperedSourceAndExecutionAuthorizationAreRejected()
        {
            var json = OriginalAlchemyJson();
            Assert.Throws<FormatException>(() =>
                Ordr323OriginalAlchemyRefundCatalog.FromJson(json.Replace(
                    Ordr323OriginalAlchemyRefundCatalog.ExpectedJassSha256,
                    new string('0', 64))));
            Assert.Throws<FormatException>(() =>
                Ordr323OriginalAlchemyRefundCatalog.FromJson(json.Replace(
                    "\"includesTargetConsumptionAndManaTransaction\": false",
                    "\"includesTargetConsumptionAndManaTransaction\": true")));
            Assert.Throws<FormatException>(() =>
                Ordr323OriginalAlchemyRefundCatalog.FromJson(json.Replace(
                    "\"targetCount\": 265", "\"targetCount\": 264")));
        }

        [Test]
        public void AlchemyNavigationIsManualAndExclusiveWithMaximumOutput()
        {
            var game = new GameSession();
            var cow = new Ordr323OriginalHelperCowActor(0);
            var navigation = new Ordr323OriginalHelperNavigation(game, cow);
            Assert.IsFalse(navigation.TryChooseAlchemy());
            game.Start();
            for (int i = 1; i <= 20; i++) game.BeginWave(i);
            Assert.IsFalse(navigation.TryChooseAlchemy());
            game.BeginWave(21);
            Assert.IsTrue(navigation.CanChooseAlchemy);
            Assert.IsTrue(navigation.TryChooseAlchemy());
            Assert.IsFalse(navigation.TryChooseAlchemy());
            Assert.IsFalse(navigation.TryChooseMaximumOutput());
            Assert.IsTrue(cow.AlchemyNavigationChosen);
            Assert.IsFalse(cow.MaximumOutputNavigationChosen);
            Assert.AreEqual("최고의도움 :: 연금술", navigation.ChosenNavigation);
            Assert.AreEqual(1, cow.GetAbilityLevel("A0J2"));
            Assert.AreEqual(1, cow.HighestHelpResearchLevel);
            Assert.AreEqual(1, cow.GetAbilityLevel("A0BZ"));
            Assert.AreEqual(0, cow.GetAbilityLevel("A0UY"));
        }
    }
}
