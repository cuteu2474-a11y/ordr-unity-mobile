using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // Acquisition is deliberately outside this fixture. These are actual
    // roster/wallet transactions starting with owned source materials.
    public sealed class Original323OwnedMaterialFusionTests
    {
        private sealed class Live
        {
            public CharacterCombatRuntime Combat;
            public SharedEconomySession Economy;
            public CharacterUnitCatalog Units;
            public OriginalMapBaseStatCatalog Map;
            public OriginalJassFusionCatalog Registry;
            public OriginalUnitRawcodeLabelCatalog Labels;
            public Original323CommonFusionPort Fusion;
        }
        private static string Data(string file) => File.ReadAllText(
            Path.Combine(Application.dataPath, "..", "data", file));
        private static Live Create()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(Data("original-2323C-map-base-stats.json"), units);
            var reject = new RejectUnverifiedDamageFormula();
            var combat = new CharacterCombatRuntime(units, skills, reject, reject,
                new SeededNonOriginalProcRandom(2323));
            var economy = new SharedEconomySession(new EconomyWallet(), combat.Roster);
            economy.ApplyVerifiedStart(Original323SoloStartProfile.Create(),
                (id, x, y) => economy.Units.Add(id, x, y), id => economy.Units.Remove(id));
            combat.Start();
            var registry = OriginalJassFusionCatalog.FromJson(Data("original-jass-fusion-registry.json"));
            var labels = OriginalUnitRawcodeLabelCatalog.FromJson(Data("original-unit-rawcode-label-links.json"), units, registry);
            var preview = OriginalFusionObjectPreviewCatalog.FromJson(Data("original-fusion-object-reconciliation.json"), registry, units);
            var fallback = new OriginalJassReadOnlyFusionPort(registry, preview, id => combat.IsStoryDeployed(id));
            return new Live { Combat = combat, Economy = economy, Units = units, Map = map,
                Registry = registry, Labels = labels,
                Fusion = new Original323CommonFusionPort(combat, economy, units, map, registry, labels, fallback) };
        }
        public static IEnumerable<string> ExecutableRecipes()
        {
            var game = Create();
            return game.Registry.All.Where(r => game.Fusion.CanCommitRecipe(r.AbilityRawcode))
                .Select(r => r.AbilityRawcode).ToArray();
        }
        [TestCaseSource(nameof(ExecutableRecipes))]
        public void OwnedMaterialsBecomeExactlyOneRealResultWithExactCosts(string ability)
        {
            var game = Create();
            var recipe = game.Registry.FindByAbility(ability);
            var originalIds = new HashSet<long>();
            foreach (var material in recipe.RequiredUnitRawcodes())
            {
                string catalogId;
                Assert.IsTrue(game.Fusion.TryResolveUnit(material.Key, out catalogId), ability);
                for (int i = 0; i < material.Value; i++)
                    originalIds.Add(game.Combat.SpawnCharacter(catalogId, 100, 200));
            }
            long gold = recipe.OrderedConditions.Where(c => c.Kind == "GOLD").Sum(c => (long)c.Count);
            long wood = recipe.OrderedConditions.Where(c => c.Kind == "WOOD").Sum(c => (long)c.Count);
            var funds = new ResourceBundle();
            if (gold > 0) funds.Add("gold", gold);
            if (wood > 0) funds.Add("wood", wood);
            game.Economy.Wallet.Grant(funds);
            var offer = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot())
                .Single(r => r.RecipeId == ability);
            Assert.IsTrue(offer.IsReady, string.Join("; ", offer.MissingMaterials));
            CollectionAssert.AreEquivalent(originalIds, offer.SuggestedMaterialInstances);
            Assert.AreEqual(ManualActionState.Succeeded,
                game.Fusion.ConfirmFusion(ability, offer.SuggestedMaterialInstances).State);
            var result = game.Combat.MobileUnitSnapshot().Single();
            Assert.AreEqual(recipe.Results[0].Rawcode, game.Map.Find(result.Unit.Id).OriginalRawcode);
            Assert.IsFalse(originalIds.Contains(result.InstanceId));
            var basic = new SourceModelBasicCombatRules(game.Map, new SeededNonOriginalProcRandom(1));
            basic.Enable();
            OriginalVerifiedAttackBinding binding;
            Assert.IsTrue(basic.TryResolve(result.Unit, null, out binding),
                "The crafted actor must have the source-backed base attack definition.");
            Assert.IsFalse(game.Combat.HasVerifiedAttack(result.InstanceId),
                "Object-model coverage is not full native combat verification.");
            Assert.AreEqual(60, game.Economy.Wallet.Balance("gold"));
            Assert.AreEqual(0, game.Economy.Wallet.Balance("wood"));
            Assert.AreEqual(ManualActionState.Rejected,
                game.Fusion.ConfirmFusion(ability, offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(1, game.Combat.CharacterCount);
        }
        [TestCase("TR-08")]
        [TestCase("TR-14")]
        public void LuffyMaterialBucketAcceptsEitherRealFormWithoutChangingResultIdentity(string id)
        {
            var game = Create();
            long actor = game.Combat.SpawnCharacter(id, 1, 2);
            var planner = new OriginalJassFusionPlanner(game.Registry, game.Labels,
                u => game.Combat.IsStoryDeployed(u), null, game.Units);
            planner.SetVerifiedObjectMap(game.Fusion);
            planner.SetVerifiedMaterialGroups(new Original323FusionMaterialGroups(game.Map));
            var ready = planner.Assess("ET02", game.Combat.MobileUnitSnapshot(), 60, 5);
            var group = ready.Materials.Single(m => m.Rawcode == "H08T");
            Assert.AreEqual(1, group.OwnedCount);
            Assert.IsNull(group.CatalogUnitId, "A material group is not another actor identity.");
            CollectionAssert.AreEqual(new[] { actor }, ready.MaterialBindings.Single(m => m.Rawcode == "H08T").InstanceIds);
            string resolved;
            Assert.IsFalse(game.Fusion.TryResolveUnit("H08T", out resolved));
            Assert.IsFalse(game.Fusion.CanCommitRecipe("ET02"), "The original ITEM gate is still required.");
            Assert.AreEqual(1, game.Combat.CharacterCount);
            var preview = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot()).Single(r => r.RecipeId == "ET02");
            Assert.IsFalse(preview.IsReady);
            Assert.IsFalse(preview.MissingMaterials.Any(m => m.Contains("H08T")),
                "A source-backed owned material group is resolved even while ITEM/creation effects stay locked.");
        }
        [Test]
        public void BothLuffyFormsDoNotSatisfyOrConsumeTheSameOneUnitBucketTwice()
        {
            var game = Create();
            long first = game.Combat.SpawnCharacter("TR-14", 1, 2);
            game.Combat.SpawnCharacter("TR-08", 1, 2);
            var planner = new OriginalJassFusionPlanner(game.Registry, game.Labels, u => false, null, game.Units);
            planner.SetVerifiedObjectMap(game.Fusion);
            planner.SetVerifiedMaterialGroups(new Original323FusionMaterialGroups(game.Map));
            var ready = planner.Assess("ET02", game.Combat.MobileUnitSnapshot(), 60, 5);
            CollectionAssert.AreEqual(new[] { first }, ready.MaterialBindings.Single(m => m.Rawcode == "H08T").InstanceIds);
            Assert.AreEqual(2, game.Combat.CharacterCount);
        }
        [Test]
        public void TatsumakiRechecksTenThousandGoldAndSevenWoodWithoutEatingMaterials()
        {
            var game = Create();
            foreach (var material in game.Registry.FindByAbility("A12C").RequiredUnitRawcodes())
            {
                string id;
                Assert.IsTrue(game.Fusion.TryResolveUnit(material.Key, out id));
                for (int i = 0; i < material.Value; i++) game.Combat.SpawnCharacter(id, 1, 2);
            }
            game.Economy.Wallet.Grant(new ResourceBundle().Add("gold", 9940).Add("wood", 7));
            var offer = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot()).Single(r => r.RecipeId == "A12C");
            Assert.IsTrue(offer.IsReady);
            Assert.IsTrue(game.Economy.Wallet.TryExchange(new ResourceBundle().Add("gold", 1), new ResourceBundle()));
            Assert.AreEqual(ManualActionState.Rejected,
                game.Fusion.ConfirmFusion("A12C", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(4, game.Combat.CharacterCount);
            Assert.AreEqual(9999, game.Economy.Wallet.Balance("gold"));
            Assert.AreEqual(7, game.Economy.Wallet.Balance("wood"));
        }
    }
}
