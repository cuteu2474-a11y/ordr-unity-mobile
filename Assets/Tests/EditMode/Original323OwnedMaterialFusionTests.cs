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
            return game.Registry.All.Where(r => game.Fusion.CanCommitRecipe(r.AbilityRawcode) &&
                    !r.OrderedConditions.Any(c => c.Kind == "RMAX"))
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
            long traits = recipe.OrderedConditions.Where(c => c.Kind == "SKPT").Sum(c => (long)c.Count);
            long greenBlood = recipe.OrderedConditions.Where(c => c.Kind == "GREN").Sum(c => (long)c.Count);
            var funds = new ResourceBundle();
            if (gold > 0) funds.Add("gold", gold);
            if (wood > 0) funds.Add("wood", wood);
            if (traits > 0) funds.Add(OriginalMissionWalletRewards.TraitPointsId, traits);
            if (greenBlood > 0) funds.Add(Original323StoryMobileProgression.GreenBloodUseResourceId, greenBlood);
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
            Assert.AreEqual(0, game.Economy.Wallet.Balance(OriginalMissionWalletRewards.TraitPointsId));
            Assert.AreEqual(0, game.Economy.Wallet.Balance(Original323StoryMobileProgression.GreenBloodUseResourceId));
            Assert.AreEqual(recipe.OrderedConditions.Any(x => x.Kind == "CHCT") ? 1 : 0,
                game.Economy.Wallet.Balance(Original323FusionCountLedger.ChangeUseId));
            Assert.AreEqual(recipe.OrderedConditions.Any(x => x.Kind == "IFCT") ? 1 : 0,
                game.Economy.Wallet.Balance(Original323FusionCountLedger.ImmortalUseId));
            Assert.AreEqual(ManualActionState.Rejected,
                game.Fusion.ConfirmFusion(ability, offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(1, game.Combat.CharacterCount);
        }
        // Batch 38: these CWR records are permitted to craft the verified
        // source result actor. Their separate JASS post-creation callbacks are
        // deliberately NOT represented as fully verified mobile behavior.
        [Test]
        public void TwentyTwoTranscendMobileResultsAreSourceMappedAndVegaPunkRemainsLocked()
        {
            var game = Create();
            var newlyEnabled = new[] {
                "TR02", "TR03", "TR04", "TR05", "TR06", "TR07", "TR08",
                "TR11", "TR12", "TR13", "TR14", "TR15", "TR16", "TR17",
                "TR18", "TR19", "TR20", "TR22", "TR24", "TR25", "TR26", "TR29"
            };
            Assert.AreEqual(22, game.Fusion.ExecutableMobileOutcomeTranscendCount);
            Assert.AreEqual(22, newlyEnabled.Distinct(StringComparer.Ordinal).Count());
            foreach (var ability in newlyEnabled)
            {
                Assert.IsTrue(game.Fusion.CanCommitRecipe(ability), ability);
                var recipe = game.Registry.FindByAbility(ability);
                Assert.IsNotNull(recipe, ability);
                Assert.AreEqual(1, recipe.Results.Count, ability);
                Assert.AreEqual("UNIT", recipe.Results[0].Kind, ability);
                Assert.AreEqual(1, recipe.Results[0].Count, ability);
                Assert.IsTrue(recipe.OrderedConditions.All(c =>
                    c.Kind == "UNIT" || c.Kind == "UPUN" ||
                    c.Kind == "UBAN" || c.Kind == "KING"), ability);
                Assert.IsTrue(recipe.RequiredUnitRawcodes().Any(), ability);
                foreach (var material in recipe.RequiredUnitRawcodes())
                {
                    string id;
                    Assert.IsTrue(game.Fusion.TryResolveUnit(material.Key, out id),
                        ability + "/" + material.Key);
                    Assert.IsNotNull(game.Map.Find(id), ability + "/" + material.Key);
                }
                string resultId;
                Assert.IsTrue(game.Fusion.TryResolveUnit(recipe.Results[0].Rawcode, out resultId), ability);
                Assert.AreEqual("초월함", game.Units.Require(resultId).Grade, ability);
                Assert.IsNotNull(game.Map.Find(resultId), ability);
            }
            Assert.IsFalse(game.Fusion.CanCommitRecipe("TR30"),
                "VegaPunk still needs a verified h0AN material identity.");
        }

        // Batch 39: source-linked mobile crafting outcomes only. These
        // records do not claim parity for the original native callbacks.
        [Test]
        public void TwentyOneAdditionalMobileOutcomesKeepSourceResultsAndDeferredEffectsLocked()
        {
            var game = Create();
            var abilities = new[] {
                "A00O", "A025", "A0UG", "A02G",
                "YY01", "YY15", "YY20",
                "A0S0", "A0UH", "A1A5", "A02O",
                "IM05", "IM06", "IM07", "IM08", "IM09", "IM11", "IM12",
                "ET05", "ET06", "ET10"
            };
            Assert.AreEqual(21, abilities.Length);
            Assert.AreEqual(abilities.Length, abilities.Distinct(StringComparer.Ordinal).Count());
            Assert.AreEqual(4, game.Fusion.ExecutableMobileOutcomeLegendaryCount);
            Assert.AreEqual(3, game.Fusion.ExecutableMobileOutcomeHiddenCount);
            Assert.AreEqual(16, game.Fusion.ExecutableMobileOutcomeUpperTierCount); // + 41차 알비다 SKPT4
            foreach (var ability in abilities)
            {
                Assert.IsTrue(game.Fusion.CanCommitRecipe(ability), ability);
                var recipe = game.Registry.FindByAbility(ability);
                Assert.IsNotNull(recipe, ability);
                Assert.AreEqual(1, recipe.Results.Count, ability);
                Assert.AreEqual("UNIT", recipe.Results[0].Kind, ability);
                Assert.AreEqual(1, recipe.Results[0].Count, ability);
                Assert.IsTrue(recipe.OrderedConditions.All(c =>
                    c.Kind == "UNIT" || c.Kind == "GOLD" || c.Kind == "WOOD" ||
                    c.Kind == "UPUN" || c.Kind == "UBAN" || c.Kind == "KING"), ability);
                Assert.IsTrue(recipe.RequiredUnitRawcodes().Any(), ability);
                foreach (var input in recipe.RequiredUnitRawcodes())
                {
                    string catalogId;
                    Assert.IsTrue(game.Fusion.TryResolveUnit(input.Key, out catalogId),
                        ability + "/" + input.Key);
                    Assert.IsNotNull(game.Map.Find(catalogId), ability + "/" + input.Key);
                }
                string resultId;
                Assert.IsTrue(game.Fusion.TryResolveUnit(recipe.Results[0].Rawcode, out resultId),
                    ability + "/" + recipe.Results[0].Rawcode);
                Assert.IsNotNull(game.Map.Find(resultId), ability + "/" + resultId);
                var offer = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot())
                    .Single(x => x.RecipeId == ability);
                Assert.IsTrue(offer.Title.Contains("모바일 제작"), ability);
                Assert.IsTrue(offer.OriginalMaterialsAndCost.Contains("원본 부가효과 미검증"), ability);
            }
            // Known native gameplay side effects and unresolved ingredient
            // identities remain blocked rather than silently discarded.
            foreach (var blocked in new[] {
                "A007", "IM01", "IM04", "ET04", "ET12",
                "A02Q", "A10A", "TR30"
            })
                Assert.IsFalse(game.Fusion.CanCommitRecipe(blocked), blocked);
        }

        [Test]
        public void Batch40AOsfMobileResultsAndRoundGatedRedfieldPreserveSourceCosts()
        {
            var game = Create();
            var aOsf = new[] { "A19Z", "A1A1", "A1A0" };
            Assert.AreEqual(3, game.Fusion.ExecutableMobileOutcomeAOsfCount);
            Assert.AreEqual(229, game.Fusion.ExecutableRecipeCount);
            foreach (var ability in aOsf)
            {
                Assert.IsTrue(game.Fusion.CanCommitRecipe(ability), ability);
                var recipe = game.Registry.FindByAbility(ability);
                Assert.IsTrue(recipe.RequiredUnitRawcodes().Any(), ability);
                Assert.IsTrue(recipe.OrderedConditions.All(c =>
                    c.Kind == "UNIT" || c.Kind == "UPUN"), ability);
                bool hasCaster = false;
                foreach (var material in recipe.RequiredUnitRawcodes())
                {
                    string id;
                    Assert.IsTrue(game.Fusion.TryResolveUnit(material.Key, out id), ability);
                    foreach (var originalAbility in game.Map.Find(id).OriginalAbilityRawcodes)
                        if (originalAbility == ability) hasCaster = true;
                }
                Assert.IsTrue(hasCaster, ability);
                var preview = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot())
                    .Single(r => r.RecipeId == ability);
                StringAssert.Contains("모바일 제작", preview.Title);
            }
            const string redfield = "A1A6";
            Assert.IsTrue(game.Fusion.CanCommitRecipe(redfield));
            var redfieldRecipe = game.Registry.FindByAbility(redfield);
            var limit = redfieldRecipe.OrderedConditions.Single(c => c.Kind == "RMAX");
            Assert.AreEqual("h05G", limit.Rawcode);
            Assert.AreEqual(40, limit.Count);
            foreach (var material in redfieldRecipe.RequiredUnitRawcodes())
            {
                string id;
                Assert.IsTrue(game.Fusion.TryResolveUnit(material.Key, out id));
                for (int i = 0; i < material.Value; i++)
                    game.Combat.SpawnCharacter(id, 1, 2);
            }
            game.Economy.Wallet.Grant(new ResourceBundle().Add("gold", 5000).Add("wood", 5));
            var before = game.Combat.MobileUnitSnapshot().Select(x => x.InstanceId).ToArray();
            var offer = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot())
                .Single(r => r.RecipeId == redfield);
            Assert.IsFalse(offer.IsReady,
                "RMAX cannot be satisfied without an independently observed source round.");
            Assert.AreEqual(ManualActionState.Rejected,
                game.Fusion.ConfirmFusion(redfield, offer.SuggestedMaterialInstances).State);
            CollectionAssert.AreEquivalent(before,
                game.Combat.MobileUnitSnapshot().Select(x => x.InstanceId).ToArray());
            Assert.AreEqual(5000, game.Economy.Wallet.Balance("gold"));
            Assert.AreEqual(5, game.Economy.Wallet.Balance("wood"));
        }

        [Test]
        public void Batch40NonCraftSourceResultsNeverBecomeFreeFusionButtons()
        {
            var game = Create();
            foreach (string ability in new[] { "UU01", "YY26", "SP01", "SP08", "IS01", "IS14" })
            {
                Assert.IsFalse(game.Fusion.CanCommitRecipe(ability), ability);
                var preview = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot())
                    .Single(x => x.RecipeId == ability);
                StringAssert.Contains("조합 아님", preview.Title);
                Assert.IsFalse(preview.IsReady);
            }
            foreach (string ability in new[] { "A02Q", "A10A", "TR30",
                "A007", "A011", "IM01", "IM04", "ET04", "ET07", "ET12" })
                Assert.IsFalse(game.Fusion.CanCommitRecipe(ability), ability);
        }

        [Test]
        public void Batch41AlvidaRequiresAndAtomicallyDebitsFourRealTraitPoints()
        {
            var game = Create();
            const string ability = "A18P";
            Assert.IsTrue(game.Fusion.CanCommitRecipe(ability));
            var recipe = game.Registry.FindByAbility(ability);
            Assert.AreEqual("h08Q", recipe.Results[0].Rawcode);
            var originalCost = recipe.OrderedConditions.Single(c => c.Kind == "SKPT");
            Assert.AreEqual("SKPT", originalCost.Rawcode);
            Assert.AreEqual(4, originalCost.Count);
            foreach (var material in recipe.RequiredUnitRawcodes())
            {
                string id;
                Assert.IsTrue(game.Fusion.TryResolveUnit(material.Key, out id));
                for (int i = 0; i < material.Value; i++)
                    game.Combat.SpawnCharacter(id, 10, 20);
            }
            game.Economy.Wallet.Grant(new ResourceBundle()
                .Add("gold", 5000).Add("wood", 5)
                .Add(OriginalMissionWalletRewards.TraitPointsId, 3));
            var before = game.Combat.MobileUnitSnapshot().Select(x => x.InstanceId).ToArray();
            var insufficient = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot())
                .Single(x => x.RecipeId == ability);
            Assert.IsFalse(insufficient.IsReady);
            StringAssert.Contains("특성 포인트 4", insufficient.OriginalMaterialsAndCost);
            StringAssert.Contains("보유 3", insufficient.OriginalMaterialsAndCost);
            Assert.AreEqual(ManualActionState.Rejected,
                game.Fusion.ConfirmFusion(ability, insufficient.SuggestedMaterialInstances).State);
            CollectionAssert.AreEquivalent(before,
                game.Combat.MobileUnitSnapshot().Select(x => x.InstanceId).ToArray());
            Assert.AreEqual(5000, game.Economy.Wallet.Balance("gold"));
            Assert.AreEqual(5, game.Economy.Wallet.Balance("wood"));
            Assert.AreEqual(3, game.Economy.Wallet.Balance(OriginalMissionWalletRewards.TraitPointsId));

            game.Economy.Wallet.Grant(new ResourceBundle()
                .Add(OriginalMissionWalletRewards.TraitPointsId, 1));
            var ready = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot())
                .Single(x => x.RecipeId == ability);
            Assert.IsTrue(ready.IsReady, string.Join("; ", ready.MissingMaterials));
            // An obsolete offer cannot spend points that were consumed elsewhere.
            Assert.IsTrue(game.Economy.Wallet.TryExchange(
                new ResourceBundle().Add(OriginalMissionWalletRewards.TraitPointsId, 1),
                new ResourceBundle()));
            Assert.AreEqual(ManualActionState.Rejected,
                game.Fusion.ConfirmFusion(ability, ready.SuggestedMaterialInstances).State);
            CollectionAssert.AreEquivalent(before,
                game.Combat.MobileUnitSnapshot().Select(x => x.InstanceId).ToArray());
            Assert.AreEqual(5000, game.Economy.Wallet.Balance("gold"));
            Assert.AreEqual(5, game.Economy.Wallet.Balance("wood"));

            game.Economy.Wallet.Grant(new ResourceBundle()
                .Add(OriginalMissionWalletRewards.TraitPointsId, 1));
            Assert.AreEqual(ManualActionState.Succeeded,
                game.Fusion.ConfirmFusion(ability, ready.SuggestedMaterialInstances).State);
            Assert.AreEqual(1, game.Combat.CharacterCount);
            Assert.AreEqual("LI-06", game.Combat.MobileUnitSnapshot().Single().Unit.Id);
            Assert.AreEqual(60, game.Economy.Wallet.Balance("gold"));
            Assert.AreEqual(0, game.Economy.Wallet.Balance("wood"));
            Assert.AreEqual(0, game.Economy.Wallet.Balance(OriginalMissionWalletRewards.TraitPointsId));
        }

        [Test]
        public void Batch41EggheadNativeRewardIsAOneUseRealSharedWalletEntitlement()
        {
            var game = Create();
            var sourceRewardQueue = new Original323StoryNativeRewardQueue();
            sourceRewardQueue.RecordCommittedClear(14);
            var progression = new Original323StoryMobileProgression(game.Economy.Wallet);
            Assert.AreEqual(1, sourceRewardQueue.ApplyPending(progression),
                "Only the A13A cast is settled; unimplemented XP remains pending.");
            Assert.AreEqual(1, game.Economy.Wallet.Balance(
                Original323StoryMobileProgression.GreenBloodUseResourceId));
            Assert.AreEqual(0, sourceRewardQueue.ApplyPending(progression));
            Assert.AreEqual(1, game.Economy.Wallet.Balance(
                Original323StoryMobileProgression.GreenBloodUseResourceId));

            foreach (string ability in new[] { "SPH1", "SPH2", "SPH4" })
            {
                Assert.IsTrue(game.Fusion.CanCommitRecipe(ability), ability);
                var recipe = game.Registry.FindByAbility(ability);
                Assert.AreEqual("GREN", recipe.OrderedConditions.Single(c => c.Kind == "GREN").Rawcode);
                var material = recipe.RequiredUnitRawcodes().Single();
                Assert.AreEqual(1, material.Value);
                Original323GreenBloodPlan plan;
                Assert.IsTrue(Original323StoryGreenBloodRules.TryPlan(
                    material.Key, true, false, false, out plan));
                Assert.AreEqual(recipe.Results.Single().Rawcode, plan.ReplacementRawcode);
                Assert.IsFalse(plan.ExecuteOriginalCENAfterTransformation);
            }
            Assert.AreEqual(3, game.Fusion.ExecutableMobileGreenBloodSeraphimCount);
            Assert.IsFalse(game.Fusion.CanCommitRecipe("SPH3"),
                "h03G requires native CEN following its transformation.");
        }

        [Test]
        public void Batch41GreenBloodConsumesExactlyOneCastAndOneOwnedTarget()
        {
            var game = Create();
            var queue = new Original323StoryNativeRewardQueue();
            queue.RecordCommittedClear(14);
            var progression = new Original323StoryMobileProgression(game.Economy.Wallet);
            Assert.AreEqual(1, queue.ApplyPending(progression));
            var recipe = game.Registry.FindByAbility("SPH1");
            string unitId;
            Assert.IsTrue(game.Fusion.TryResolveUnit(recipe.RequiredUnitRawcodes().Single().Key,
                out unitId));
            long source = game.Combat.SpawnCharacter(unitId, 91, 127);
            var available = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot())
                .Single(x => x.RecipeId == "SPH1");
            Assert.IsTrue(available.IsReady, string.Join("; ", available.MissingMaterials));
            StringAssert.Contains("그린블러드 1", available.OriginalMaterialsAndCost);
            Assert.AreEqual(ManualActionState.Succeeded,
                game.Fusion.ConfirmFusion("SPH1", available.SuggestedMaterialInstances).State);
            Assert.AreEqual(0, game.Economy.Wallet.Balance(
                Original323StoryMobileProgression.GreenBloodUseResourceId));
            var transformed = game.Combat.MobileUnitSnapshot().Single();
            Assert.AreNotEqual(source, transformed.InstanceId);
            Assert.AreEqual("SR-01", transformed.Unit.Id);
            Assert.AreEqual(91, transformed.Position.X);
            Assert.AreEqual(127, transformed.Position.Y);

            string secondId;
            Assert.IsTrue(game.Fusion.TryResolveUnit("h043", out secondId));
            game.Combat.SpawnCharacter(secondId, 22, 33);
            var blocked = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot())
                .Single(x => x.RecipeId == "SPH2");
            Assert.IsFalse(blocked.IsReady);
            Assert.AreEqual(ManualActionState.Rejected,
                game.Fusion.ConfirmFusion("SPH2", blocked.SuggestedMaterialInstances).State);
            Assert.AreEqual(2, game.Combat.CharacterCount);
        }

        [Test]
        public void Batch42AllFiveChangesAndSixDistortedOutcomesUseExactSourceCounterTags()
        {
            var game = Create();
            var changes = new[] { "A0K2", "A0K3", "A0K4", "A0K6", "A0K7" };
            var distorted = new[] { "IF01", "IF02", "IF03", "IF04", "IF05", "IF06" };
            Assert.AreEqual(5, game.Fusion.ExecutableMobileOutcomeChangesCount);
            Assert.AreEqual(6, game.Fusion.ExecutableMobileOutcomeDistortedCount);
            foreach (var ability in changes.Concat(distorted))
            {
                Assert.IsTrue(game.Fusion.CanCommitRecipe(ability), ability);
                var recipe = game.Registry.FindByAbility(ability);
                var counter = recipe.OrderedConditions.Single(x =>
                    x.Kind == "CHCT" || x.Kind == "IFCT");
                Assert.AreEqual(changes.Contains(ability) ? "CHCT" : "IFCT", counter.Kind);
                Assert.AreEqual(counter.Kind, counter.Rawcode);
                Assert.AreEqual(changes.Contains(ability) ? 1 : 3, counter.Count);
                Assert.IsTrue(recipe.RequiredUnitRawcodes().Any(), ability);
                foreach (var material in recipe.RequiredUnitRawcodes())
                {
                    string id;
                    Assert.IsTrue(game.Fusion.TryResolveUnit(material.Key, out id), ability);
                    Assert.IsNotNull(game.Map.Find(id));
                }
                string output;
                Assert.IsTrue(game.Fusion.TryResolveUnit(recipe.Results.Single().Rawcode, out output));
                Assert.AreEqual(changes.Contains(ability) ? "변화" : "왜곡됨",
                    game.Units.Require(output).Grade);
                Assert.AreEqual(changes.Contains(ability) ? 10 : 0,
                    recipe.OrderedConditions.Where(x => x.Kind == "WOOD").Sum(x => x.Count));
            }
        }

        [Test]
        public void Batch42ChangeConsumesOneSharedSlotAndSecondChangeIsBlockedWithoutSpending()
        {
            var game = Create();
            foreach (var ability in new[] { "A0K2", "A0K3" })
            {
                var recipe = game.Registry.FindByAbility(ability);
                foreach (var material in recipe.RequiredUnitRawcodes())
                {
                    string id;
                    Assert.IsTrue(game.Fusion.TryResolveUnit(material.Key, out id));
                    for (int i = 0; i < material.Value; i++)
                        game.Combat.SpawnCharacter(id, 12, 34);
                }
            }
            game.Economy.Wallet.Grant(new ResourceBundle().Add("wood", 20));
            var first = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot())
                .Single(x => x.RecipeId == "A0K2");
            Assert.IsTrue(first.IsReady, string.Join("; ", first.MissingMaterials));
            StringAssert.Contains("변화 제작 횟수 0/1", first.OriginalMaterialsAndCost);
            Assert.AreEqual(ManualActionState.Succeeded,
                game.Fusion.ConfirmFusion("A0K2", first.SuggestedMaterialInstances).State);
            Assert.AreEqual(1, game.Economy.Wallet.Balance(Original323FusionCountLedger.ChangeUseId));
            Assert.AreEqual(10, game.Economy.Wallet.Balance("wood"));
            var next = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot())
                .Single(x => x.RecipeId == "A0K3");
            Assert.IsFalse(next.IsReady);
            StringAssert.Contains("변화 제작 횟수 1/1", next.OriginalMaterialsAndCost);
            var before = game.Combat.MobileUnitSnapshot().Select(x => x.InstanceId).ToArray();
            Assert.AreEqual(ManualActionState.Rejected,
                game.Fusion.ConfirmFusion("A0K3", next.SuggestedMaterialInstances).State);
            CollectionAssert.AreEquivalent(before,
                game.Combat.MobileUnitSnapshot().Select(x => x.InstanceId).ToArray());
            Assert.AreEqual(10, game.Economy.Wallet.Balance("wood"));
            Assert.AreEqual(1, game.Economy.Wallet.Balance(Original323FusionCountLedger.ChangeUseId));
        }

        [Test]
        public void Batch42DistortedAllowsThreeSuccessfulCraftsThenRejectsFourth()
        {
            var game = Create();
            const string ability = "IF01";
            var recipe = game.Registry.FindByAbility(ability);
            for (int turn = 0; turn < 4; turn++)
            {
                foreach (var material in recipe.RequiredUnitRawcodes())
                {
                    string id;
                    Assert.IsTrue(game.Fusion.TryResolveUnit(material.Key, out id));
                    for (int i = 0; i < material.Value; i++)
                        game.Combat.SpawnCharacter(id, turn * 10, turn * 20);
                }
                var offer = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot())
                    .Single(x => x.RecipeId == ability);
                if (turn < 3)
                {
                    Assert.IsTrue(offer.IsReady, string.Join("; ", offer.MissingMaterials));
                    Assert.AreEqual(ManualActionState.Succeeded,
                        game.Fusion.ConfirmFusion(ability, offer.SuggestedMaterialInstances).State);
                    Assert.AreEqual(turn + 1,
                        game.Economy.Wallet.Balance(Original323FusionCountLedger.ImmortalUseId));
                }
                else
                {
                    Assert.IsFalse(offer.IsReady);
                    StringAssert.Contains("왜곡 제작 횟수 3/3", offer.OriginalMaterialsAndCost);
                    var before = game.Combat.MobileUnitSnapshot().Select(x => x.InstanceId).ToArray();
                    Assert.AreEqual(ManualActionState.Rejected,
                        game.Fusion.ConfirmFusion(ability, offer.SuggestedMaterialInstances).State);
                    CollectionAssert.AreEquivalent(before,
                        game.Combat.MobileUnitSnapshot().Select(x => x.InstanceId).ToArray());
                    Assert.AreEqual(3,
                        game.Economy.Wallet.Balance(Original323FusionCountLedger.ImmortalUseId));
                }
            }
            var stored = game.Economy.Wallet.CaptureState();
            var restored = new EconomyWallet();
            restored.RestoreState(stored);
            Assert.AreEqual(3, restored.Balance(Original323FusionCountLedger.ImmortalUseId));
            Assert.AreEqual(0, restored.Balance(Original323FusionCountLedger.ChangeUseId));
        }

        [Test]
        public void Batch42MissingWoodNeverConsumesChangeSlotOrMaterials()
        {
            var game = Create();
            var recipe = game.Registry.FindByAbility("A0K7");
            foreach (var material in recipe.RequiredUnitRawcodes())
            {
                string id;
                Assert.IsTrue(game.Fusion.TryResolveUnit(material.Key, out id));
                game.Combat.SpawnCharacter(id, 2, 4);
            }
            var before = game.Combat.MobileUnitSnapshot().Select(x => x.InstanceId).ToArray();
            var preview = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot())
                .Single(x => x.RecipeId == "A0K7");
            Assert.IsFalse(preview.IsReady);
            Assert.AreEqual(ManualActionState.Rejected,
                game.Fusion.ConfirmFusion("A0K7", preview.SuggestedMaterialInstances).State);
            CollectionAssert.AreEquivalent(before,
                game.Combat.MobileUnitSnapshot().Select(x => x.InstanceId).ToArray());
            Assert.AreEqual(0, game.Economy.Wallet.Balance(Original323FusionCountLedger.ChangeUseId));
            game.Economy.Wallet.Grant(new ResourceBundle().Add("wood", 10));
            var ready = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot())
                .Single(x => x.RecipeId == "A0K7");
            Assert.IsTrue(ready.IsReady);
            Assert.AreEqual(ManualActionState.Succeeded,
                game.Fusion.ConfirmFusion("A0K7", ready.SuggestedMaterialInstances).State);
            Assert.AreEqual(1, game.Economy.Wallet.Balance(Original323FusionCountLedger.ChangeUseId));
        }

        [Test]
        public void Batch39HighTierFusionNeverConsumesMaterialsWhenWoodIsMissing()
        {
            var game = Create();
            var recipe = game.Registry.FindByAbility("IM05");
            foreach (var input in recipe.RequiredUnitRawcodes())
            {
                string catalogId;
                Assert.IsTrue(game.Fusion.TryResolveUnit(input.Key, out catalogId));
                for (int i = 0; i < input.Value; i++)
                    game.Combat.SpawnCharacter(catalogId, 10, 20);
            }
            var before = game.Combat.MobileUnitSnapshot().Select(x => x.InstanceId).ToArray();
            var offer = game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot())
                .Single(x => x.RecipeId == "IM05");
            Assert.IsFalse(offer.IsReady, "IM05 requires 10 source wood.");
            Assert.AreEqual(ManualActionState.Rejected,
                game.Fusion.ConfirmFusion("IM05", offer.SuggestedMaterialInstances).State);
            CollectionAssert.AreEquivalent(before,
                game.Combat.MobileUnitSnapshot().Select(x => x.InstanceId).ToArray());
            Assert.AreEqual(0, game.Economy.Wallet.Balance("wood"));
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
