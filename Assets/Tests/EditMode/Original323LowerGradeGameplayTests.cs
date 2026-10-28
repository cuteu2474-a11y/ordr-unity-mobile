using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;
using Ordr.Mobile.Unity;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323LowerGradeGameplayTests
    {
        private sealed class Rolls : IOriginalWispRandom
        {
            private readonly Queue<int> values;
            public int Calls;
            public Rolls(params int[] sequence) { values = new Queue<int>(sequence); }
            public int NextInclusive(int min, int max)
            {
                Calls++;
                if (values.Count == 0) throw new InvalidOperationException("Unexpected random draw");
                int value = values.Dequeue();
                Assert.That(value, Is.InRange(min, max));
                return value;
            }
        }
        private sealed class DifferentPlacement : IOriginalPlacementRules
        {
            public bool TryChoosePosition(CatalogUnit unit, UnitState original, out double x, out double y)
            { x = 999; y = 888; return true; }
            public bool IsLegalPosition(CatalogUnit unit, UnitState original, double x, double y) => true;
        }
        private sealed class Live
        {
            public CharacterCombatRuntime Combat;
            public SharedEconomySession Economy;
            public OriginalMapBaseStatCatalog Map;
            public OriginalJassFusionCatalog Registry;
            public Original323CommonFusionPort Fusion;
        }
        private static string Data(string name) => File.ReadAllText(
            Path.Combine(Application.dataPath, "..", "data", name));
        private static Live Create()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(Data("original-2323C-map-base-stats.json"), units);
            var reject = new RejectUnverifiedDamageFormula();
            var world = new CharacterCombatRuntime(units, skills, reject, reject,
                new SeededNonOriginalProcRandom(2323));
            var shared = new SharedEconomySession(new EconomyWallet(), world.Roster);
            shared.ApplyVerifiedStart(Original323SoloStartProfile.Create(),
                (id, x, y) => shared.Units.Add(id, x, y), id => shared.Units.Remove(id));
            world.Start();
            var registry = OriginalJassFusionCatalog.FromJson(Data("original-jass-fusion-registry.json"));
            var labels = OriginalUnitRawcodeLabelCatalog.FromJson(
                Data("original-unit-rawcode-label-links.json"), units, registry);
            var preview = OriginalFusionObjectPreviewCatalog.FromJson(
                Data("original-fusion-object-reconciliation.json"), registry, units);
            var fallback = new OriginalJassReadOnlyFusionPort(registry, preview, id => world.IsStoryDeployed(id));
            return new Live { Combat = world, Economy = shared, Map = map, Registry = registry,
                Fusion = new Original323CommonFusionPort(world, shared, units, map, registry, labels, fallback) };
        }
        private static MobileFusionPreview Offer(Live game, string ability) => game.Fusion
            .AvailableRecipes(game.Combat.MobileUnitSnapshot()).Single(row => row.RecipeId == ability);

        [TestCase("A006", "U-01")]
        [TestCase("A00E", "U-02")]
        [TestCase("A00W", "U-03")]
        [TestCase("A0N5", "U-04")]
        [TestCase("A00D", "U-05")]
        [TestCase("A00P", "U-06")]
        [TestCase("A00V", "U-07")]
        [TestCase("A02I", "U-08")]
        [TestCase("A01R", "U-09")]
        [TestCase("A00L", "U-10")]
        [TestCase("A01H", "U-11")]
        [TestCase("A01L", "U-12")]
        [TestCase("A0N7", "U-13")]
        [TestCase("A012", "S-07")]
        [TestCase("A015", "S-03")]
        [TestCase("A032", "S-22")]
        [TestCase("A018", "S-11")]
        [TestCase("A01B", "S-13")]
        [TestCase("A024", "S-08")]
        [TestCase("A01A", "S-24")]
        [TestCase("A0NA", "S-02")]
        [TestCase("A01O", "S-06")]
        [TestCase("A03E", "S-04")]
        [TestCase("A01X", "S-05")]
        [TestCase("A017", "S-28")]
        [TestCase("A00K", "S-09")]
        [TestCase("A019", "S-33")]
        [TestCase("A00Z", "S-21")]
        [TestCase("A01I", "S-12")]
        [TestCase("A01D", "S-18")]
        [TestCase("A01K", "S-20")]
        [TestCase("A02B", "S-01")]
        [TestCase("A01C", "S-31")]
        [TestCase("A00B", "S-25")]
        [TestCase("A014", "S-26")]
        [TestCase("A030", "S-30")]
        [TestCase("A01N", "S-10")]
        [TestCase("A00S", "S-19")]
        [TestCase("A01E", "S-23")]
        [TestCase("A02Y", "S-32")]
        [TestCase("A01Q", "S-29")]
        [TestCase("A0N8", "S-27")]
        [TestCase("A01P", "S-16")]
        [TestCase("A029", "S-17")]
        [TestCase("A0XP", "R-01")]
        [TestCase("A02W", "R-02")]
        [TestCase("A016", "R-07")]
        [TestCase("A02M", "R-04")]
        [TestCase("A02R", "R-33")]
        [TestCase("A00I", "R-08")]
        [TestCase("A01F", "R-03")]
        [TestCase("A028", "R-28")]
        [TestCase("A01M", "R-09")]
        [TestCase("A02K", "R-10")]
        [TestCase("A00F", "R-06")]
        [TestCase("A02F", "R-12")]
        [TestCase("A013", "R-13")]
        [TestCase("A0IO", "R-14")]
        [TestCase("A03G", "R-15")]
        [TestCase("A00A", "R-16")]
        [TestCase("A00Q", "R-17")]
        [TestCase("A0N6", "R-39")]
        [TestCase("A0ND", "R-42")]
        [TestCase("A02J", "R-19")]
        [TestCase("A00X", "R-20")]
        [TestCase("A00R", "R-27")]
        [TestCase("A0NF", "R-21")]
        [TestCase("A03B", "R-22")]
        [TestCase("A02V", "R-23")]
        [TestCase("A02L", "R-24")]
        [TestCase("A039", "R-25")]
        [TestCase("A01J", "R-31")]
        [TestCase("A0NB", "R-29")]
        [TestCase("A01W", "R-30")]
        [TestCase("A00U", "R-37")]
        [TestCase("A008", "R-32")]
        [TestCase("A02A", "R-34")]
        [TestCase("A02T", "R-11")]
        [TestCase("A00C", "R-36")]
        [TestCase("A0NC", "R-26")]
        [TestCase("A02N", "R-38")]
        [TestCase("A02S", "R-40")]
        [TestCase("A010", "R-35")]
        [TestCase("A02Z", "R-05")]
        [TestCase("A01V", "R-41")]
        [TestCase("A037", "L-29")]
        [TestCase("A01G", "L-12")]
        [TestCase("A00N", "L-24")]
        [TestCase("A0XS", "L-03")]
        [TestCase("A03C", "L-05")]
        [TestCase("A02P", "L-09")]
        [TestCase("A0NH", "L-27")]
        [TestCase("A036", "L-01")]
        [TestCase("A00T", "L-10")]
        [TestCase("A03D", "L-15")]
        [TestCase("A00Y", "L-04")]
        [TestCase("A00J", "L-13")]
        [TestCase("A027", "L-32")]
        [TestCase("A0N9", "L-36")]
        [TestCase("A00G", "L-06")]
        [TestCase("A01U", "L-16")]
        [TestCase("A02C", "L-33")]
        [TestCase("A00M", "L-18")]
        [TestCase("A0NI", "L-19")]
        [TestCase("A03H", "L-20")]
        [TestCase("A009", "L-22")]
        [TestCase("A02X", "L-23")]
        [TestCase("A005", "L-25")]
        [TestCase("A0NG", "L-26")]
        [TestCase("A0NE", "L-28")]
        [TestCase("A026", "L-30")]
        [TestCase("A01T", "L-31")]
        [TestCase("A108", "L-34")]
        [TestCase("A02H", "L-08")]
        [TestCase("A00H", "L-37")]
        [TestCase("A1CC", "L-39")]
        public void AllReviewedRecipesConsumeActualMaterialsAndCreateOneResult(string ability, string result)
        {
            var game = Create();
            foreach (var requirement in game.Registry.FindByAbility(ability).RequiredUnitRawcodes())
            {
                string id;
                Assert.IsTrue(game.Fusion.TryResolveUnit(requirement.Key, out id));
                for (int i = 0; i < requirement.Value; i++) game.Combat.SpawnCharacter(id, 125, 340);
            }
            long unrelated = game.Combat.SpawnCharacter("C-07", 888, 999);
            bool legendary = result.StartsWith("L-", StringComparison.Ordinal);
            if (legendary) game.Economy.Wallet.Grant(new ResourceBundle().Add("wood", 3));
            var offer = Offer(game, ability);
            Assert.IsTrue(offer.IsReady, string.Join(",", offer.MissingMaterials));
            long[] consumed = offer.SuggestedMaterialInstances.ToArray();
            long gold = game.Economy.Wallet.Balance("gold");
            int notifications = 0;
            game.Fusion.FusionCommitted += (id, catalog, raw) => notifications++;
            Assert.AreEqual(ManualActionState.Succeeded,
                game.Fusion.ConfirmFusion(ability, consumed).State);
            var now = game.Combat.MobileUnitSnapshot();
            var made = now.Single(row => row.Unit.Id == result);
            Assert.AreEqual(125, made.Position.X);
            Assert.AreEqual(340, made.Position.Y);
            Assert.IsFalse(now.Any(row => consumed.Contains(row.InstanceId)));
            Assert.IsTrue(now.Any(row => row.InstanceId == unrelated));
            Assert.AreEqual(2, now.Count);
            Assert.AreEqual(gold, game.Economy.Wallet.Balance("gold"));
            Assert.AreEqual(0, game.Economy.Wallet.Balance("wood"));
            Assert.AreEqual(1, notifications);
            Assert.AreEqual(ManualActionState.Rejected, game.Fusion.ConfirmFusion(ability, consumed).State);
            Assert.AreEqual(1, notifications);
        }

        [Test]
        public void RepeatFusionConsumesFreshSourceApprovedMaterialsForEachCopy()
        {
            var game = Create();
            for (int i = 0; i < 6; i++)
                game.Combat.SpawnCharacter("C-01", 100 + i, 200);
            int commits = 0;
            var result = MobileFusionProductionAssistant.CraftRepeated("A012", 2,
                game.Fusion.CanCommitRecipe,
                () => game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot()),
                offer =>
                {
                    commits++;
                    return game.Fusion.ConfirmFusion(offer.RecipeId,
                        offer.SuggestedMaterialInstances);
                });
            Assert.AreEqual(ManualActionState.Succeeded, result.State);
            Assert.AreEqual(2, commits);
            Assert.AreEqual(2, game.Combat.MobileUnitSnapshot().Count);
            Assert.IsTrue(game.Combat.MobileUnitSnapshot().All(
                u => u.Unit.Id == "S-07"));
            Assert.AreEqual(0, game.Combat.MobileUnitSnapshot().Count(
                u => u.Unit.Id == "C-01"));
        }

        [Test]
        public void RepeatFusionStopsAfterRealMaterialsAreExhausted()
        {
            var game = Create();
            for (int i = 0; i < 3; i++)
                game.Combat.SpawnCharacter("C-01", 100 + i, 200);
            int commits = 0;
            var result = MobileFusionProductionAssistant.CraftRepeated("A012", 5,
                game.Fusion.CanCommitRecipe,
                () => game.Fusion.AvailableRecipes(game.Combat.MobileUnitSnapshot()),
                offer =>
                {
                    commits++;
                    return game.Fusion.ConfirmFusion(offer.RecipeId,
                        offer.SuggestedMaterialInstances);
                });
            Assert.AreEqual(ManualActionState.Succeeded, result.State);
            StringAssert.Contains("1/5", result.Message);
            Assert.AreEqual(1, commits);
            Assert.AreEqual("S-07", game.Combat.MobileUnitSnapshot().Single().Unit.Id);
        }

        [Test]
        public void GaimonRawcodeMapsToLiveRosterButDoesNotUnlockUnreviewedFusion()
        {
            var game = Create();
            Assert.AreEqual(273, game.Map.Count);
            Assert.AreEqual("h0BG", game.Map.Find(SpecialGradeMobilePolicy.Gaimon).OriginalRawcode);
            Assert.AreEqual("가이몬 - 특수", game.Map.Find(SpecialGradeMobilePolicy.Gaimon).MapName);
            string id;
            Assert.IsTrue(game.Fusion.TryResolveUnit("h0BG", out id));
            Assert.AreEqual(SpecialGradeMobilePolicy.Gaimon, id);
            Assert.IsFalse(game.Fusion.CanCommitRecipe("SP08"));
            // Spawning is for runtime identity testing. A test spawn is
            // never an original verified player reward.
            long actor = game.Combat.SpawnCharacter(id, -3776, 4480);
            Assert.IsTrue(game.Combat.MobileUnitSnapshot().Any(
                u => u.InstanceId == actor && u.Unit.Id == "X-08"));
            game.Combat.RemoveCharacter(actor);
            Assert.IsFalse(game.Combat.MobileUnitSnapshot().Any(
                u => u.Unit.Id == "X-08"));
        }

        [Test]
        public void SpecialFusionRechecksExactMaterialsAndDoesNotAuthorizeUnreviewedCasts()
        {
            var game = Create();
            for (int i = 0; i < 3; i++) game.Combat.SpawnCharacter("C-01", 10 + i, 20);
            var offer = Offer(game, "A012"); // Three Luffy -> Gear Second.
            Assert.IsTrue(offer.IsReady);
            long removed = offer.SuggestedMaterialInstances[0];
            game.Combat.RemoveCharacter(removed);
            long replacement = game.Combat.SpawnCharacter("C-01", 99, 100);
            Assert.AreEqual(ManualActionState.Rejected,
                game.Fusion.ConfirmFusion("A012", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(3, game.Combat.CharacterCount);
            Assert.IsTrue(game.Combat.MobileUnitSnapshot().Any(x => x.InstanceId == replacement));
            Assert.IsFalse(game.Fusion.CanCommitRecipe("A19Z"));
            Assert.IsFalse(game.Fusion.CanCommitRecipe("A1A1"));
            Assert.AreEqual(116, game.Fusion.ExecutableRecipeCount);
            Assert.IsFalse(game.Fusion.CanCommitRecipe("A1A0"));
            var fresh = Offer(game, "A012");
            Assert.AreEqual(ManualActionState.Succeeded,
                game.Fusion.ConfirmFusion("A012", fresh.SuggestedMaterialInstances).State);
            Assert.AreEqual("S-07", game.Combat.MobileUnitSnapshot().Single().Unit.Id);
        }

        [Test]
        public void LegendaryWoodIsRecheckedBeforeAnyMaterialIsConsumed()
        {
            var game = Create();
            game.Combat.SpawnCharacter("R-06", 10, 20);
            game.Combat.SpawnCharacter("R-36", 30, 40);
            game.Combat.SpawnCharacter("R-01", 50, 60);
            var insufficient = Offer(game, "A03D");
            Assert.IsFalse(insufficient.IsReady);
            game.Economy.Wallet.Grant(new ResourceBundle().Add("wood", 3));
            var ready = Offer(game, "A03D");
            Assert.IsTrue(ready.IsReady);
            Assert.IsTrue(game.Economy.Wallet.TryExchange(new ResourceBundle().Add("wood", 1), new ResourceBundle()));
            Assert.AreEqual(ManualActionState.Rejected,
                game.Fusion.ConfirmFusion("A03D", ready.SuggestedMaterialInstances).State);
            Assert.AreEqual(3, game.Combat.CharacterCount);
            Assert.AreEqual(2, game.Economy.Wallet.Balance("wood"));
            game.Economy.Wallet.Grant(new ResourceBundle().Add("wood", 1));
            Assert.AreEqual(ManualActionState.Succeeded,
                game.Fusion.ConfirmFusion("A03D", ready.SuggestedMaterialInstances).State);
            var result = game.Combat.MobileUnitSnapshot().Single();
            Assert.AreEqual("L-15", result.Unit.Id);
            Assert.AreEqual(10, result.Position.X);
            Assert.AreEqual(20, result.Position.Y);
            Assert.AreEqual(0, game.Economy.Wallet.Balance("wood"));
            Assert.AreEqual(ManualActionState.Rejected,
                game.Fusion.ConfirmFusion("A03D", ready.SuggestedMaterialInstances).State);
            Assert.AreEqual(0, game.Economy.Wallet.Balance("wood"));
            Assert.IsFalse(game.Fusion.CanCommitRecipe("A00O"));
            Assert.IsFalse(game.Fusion.CanCommitRecipe("A01S"));
        }

        [Test]
        public void StaleAndDuplicateFusionMaterialsNeverConsumeOtherUnits()
        {
            var game = Create();
            long luffy = game.Combat.SpawnCharacter("C-01", 13, 24);
            long gunner = game.Combat.SpawnCharacter("C-08", 99, 100);
            var offer = Offer(game, "A00D");
            Assert.AreEqual(ManualActionState.Rejected,
                game.Fusion.ConfirmFusion("A00D", new[] { luffy, luffy }).State);
            game.Combat.RemoveCharacter(gunner);
            long replacement = game.Combat.SpawnCharacter("C-08", 99, 100);
            Assert.AreEqual(ManualActionState.Rejected,
                game.Fusion.ConfirmFusion("A00D", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(2, game.Combat.CharacterCount);
            Assert.AreEqual("C-01", game.Combat.Roster.Get(luffy).UnitId);
            Assert.AreEqual("C-08", game.Combat.Roster.Get(replacement).UnitId);
            Assert.IsFalse(game.Fusion.CanCommitRecipe("ET01"));
        }

        [Test]
        public void FusionUsesAbilityOwningCasterPositionAndObserverFailureDoesNotUndoCommit()
        {
            var game = Create();
            game.Combat.SpawnCharacter("C-06", 900, 200);
            game.Combat.SpawnCharacter("C-01", 40, 50);
            var offer = Offer(game, "A00E"); // C-06 owns A00E, not C-01.
            game.Fusion.FusionCommitted += (id, catalog, raw) => { throw new Exception("observer"); };
            Assert.AreEqual(ManualActionState.Succeeded,
                game.Fusion.ConfirmFusion("A00E", offer.SuggestedMaterialInstances).State);
            var result = game.Combat.MobileUnitSnapshot().Single();
            Assert.AreEqual("U-02", result.Unit.Id);
            Assert.AreEqual(900, result.Position.X);
            Assert.AreEqual(200, result.Position.Y);
        }

        [Test]
        public void ThirdCommonSaleOverflowPreservesActorAndReusesSameWoodRoll()
        {
            var game = Create();
            var rng = new Rolls(35);
            var sale = new Original323LowerGradeSaleService(game.Combat, game.Economy, game.Map, rng);
            var ids = new List<long>();
            for (int i = 0; i < 3; i++) ids.Add(game.Combat.SpawnCharacter("C-01", i, 0));
            Assert.AreEqual(ManualActionState.Succeeded, sale.Sell(ids[0]).State);
            Assert.AreEqual(ManualActionState.Succeeded, sale.Sell(ids[1]).State);
            game.Combat.Roster.Select(ids[2]);
            game.Economy.Wallet.Grant(new ResourceBundle().Add(WispResourceIds.Random,
                long.MaxValue - game.Economy.Wallet.Balance(WispResourceIds.Random)));
            Assert.AreEqual(ManualActionState.Rejected, sale.Sell(ids[2]).State);
            Assert.AreEqual(2, sale.CommonSold);
            Assert.AreEqual(ids[2], game.Combat.Roster.SelectedId);
            Assert.AreEqual(1, game.Combat.CharacterCount);
            Assert.AreEqual(0, game.Economy.Wallet.Balance("wood"));
            Assert.AreEqual(1, rng.Calls);
            Assert.IsTrue(game.Economy.Wallet.TryExchange(
                new ResourceBundle().Add(WispResourceIds.Random, 1), new ResourceBundle()));
            Assert.AreEqual(ManualActionState.Succeeded, sale.Sell(ids[2]).State);
            Assert.AreEqual(3, sale.CommonSold);
            Assert.AreEqual(1, game.Economy.Wallet.Balance("wood"));
            Assert.AreEqual(1, rng.Calls);
            Assert.AreEqual(ManualActionState.Rejected, sale.Sell(ids[2]).State);
        }

        [TestCase("U-01", 50, 20, 1, 1, 2)]
        [TestCase("U-01", 51, 21, 0, 0, 2)]
        [TestCase("S-01", 35, 0, 1, 1, 1)]
        [TestCase("S-01", 36, 0, 1, 0, 1)]
        [TestCase("R-01", 0, 0, 2, 1, 0)]
        public void GradeSalesRespectIndependentSourceRollsAndExactRewards(
            string unit, int first, int second, int wisps, int wood, int calls)
        {
            var game = Create();
            var rng = calls == 0 ? new Rolls() : calls == 1 ? new Rolls(first) : new Rolls(first, second);
            var sale = new Original323LowerGradeSaleService(game.Combat, game.Economy, game.Map, rng);
            long id = game.Combat.SpawnCharacter(unit, 2, 3);
            Assert.AreEqual(ManualActionState.Succeeded, sale.Sell(id).State);
            Assert.AreEqual(5 + wisps, game.Economy.Wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(wood, game.Economy.Wallet.Balance("wood"));
            Assert.AreEqual(calls, rng.Calls);
            Assert.AreEqual(0, sale.CommonSold);
            Assert.AreEqual(0, game.Combat.CharacterCount);
        }

        [Test]
        public void FailedRewardSubscriberRestoresUnitSelectionWalletAndSaleCounter()
        {
            var game = Create();
            var sale = new Original323LowerGradeSaleService(game.Combat, game.Economy, game.Map, new Rolls());
            long id = game.Combat.SpawnCharacter("R-01", 2, 3);
            game.Combat.Roster.Select(id);
            Action<ResourceBundle> broken = reward => { game.Combat.Step(1); };
            game.Economy.Wallet.Granted += broken;
            Assert.AreEqual(ManualActionState.Rejected, sale.Sell(id).State);
            Assert.AreEqual(id, game.Combat.Roster.SelectedId);
            Assert.AreEqual(5, game.Economy.Wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(0, game.Economy.Wallet.Balance("wood"));
            Assert.AreEqual(0, game.Combat.Time);
            game.Economy.Wallet.Granted -= broken;
            Assert.AreEqual(ManualActionState.Succeeded, sale.Sell(id).State);
            Assert.AreEqual(0, game.Combat.CharacterCount);
        }

        [Test]
        public void AwardedUncommonWispsBecomeRealUnitsWhileOtherGradesStayQueued()
        {
            var game = Create();
            var rng = new Rolls(0, 12);
            var source = new Original323AutomaticUncommonWisps(Data("original-2323C-wisp-pools.json"), rng);
            using (var wisps = new MobileWispService(game.Economy.Wallet, source,
                new OriginalWispCombatRewardWorld(game.Combat, game.Fusion, source, source)))
            {
                game.Economy.Wallet.Grant(new ResourceBundle().Add(WispResourceIds.Uncommon, 2)
                    .Add(WispResourceIds.Rare, 1));
                Assert.AreEqual(2, game.Combat.CharacterCount);
                Assert.AreEqual(0, game.Economy.Wallet.Balance(WispResourceIds.Uncommon));
                Assert.AreEqual(1, game.Economy.Wallet.Balance(WispResourceIds.Rare));
                foreach (var unit in game.Combat.MobileUnitSnapshot())
                {
                    Assert.AreEqual("안흔함", unit.Unit.Grade);
                    Assert.AreEqual(-3760, unit.Position.X);
                    Assert.AreEqual(3904, unit.Position.Y);
                }
                Assert.AreEqual(0, wisps.FlushAutomatic());
                Assert.AreEqual(2, rng.Calls);
            }
            game.Economy.Wallet.Grant(new ResourceBundle().Add(WispResourceIds.Uncommon, 1));
            Assert.AreEqual(2, game.Combat.CharacterCount, "Disposed service must unsubscribe.");
        }

        [TestCase(0, "S-01")]
        [TestCase(1, "S-18")]
        [TestCase(2, "S-09")]
        [TestCase(3, "S-03")]
        [TestCase(4, "S-04")]
        [TestCase(5, "S-06")]
        [TestCase(6, "S-22")]
        [TestCase(7, "S-08")]
        [TestCase(8, "S-07")]
        [TestCase(9, "S-28")]
        [TestCase(10, "S-10")]
        [TestCase(11, "S-11")]
        [TestCase(12, "S-12")]
        [TestCase(13, "S-13")]
        [TestCase(14, "S-14")]
        [TestCase(15, "S-15")]
        [TestCase(16, "S-16")]
        [TestCase(17, "S-17")]
        [TestCase(18, "S-20")]
        [TestCase(19, "S-30")]
        [TestCase(20, "S-21")]
        [TestCase(21, "S-23")]
        [TestCase(22, "S-24")]
        [TestCase(23, "S-02")]
        [TestCase(24, "S-27")]
        [TestCase(25, "S-29")]
        [TestCase(26, "S-31")]
        [TestCase(27, "S-25")]
        [TestCase(28, "S-26")]
        [TestCase(29, "S-05")]
        [TestCase(30, "S-32")]
        [TestCase(31, "S-19")]
        [TestCase(32, "S-33")]
        public void EverySpecialWispOutcomeCreatesTheActualSourceUnitAtPlayerStart(int roll, string result)
        {
            var game = Create();
            game.Combat.MobileAutomation.Configure(new DifferentPlacement(), null, null);
            var rng = new Rolls(roll);
            var source = new Original323AutomaticUncommonWisps(Data("original-2323C-wisp-pools.json"), rng);
            using (var wisps = new MobileWispService(game.Economy.Wallet, source,
                new OriginalWispCombatRewardWorld(game.Combat, game.Fusion, source, source)))
            {
                game.Economy.Wallet.Grant(new ResourceBundle().Add(WispResourceIds.Special, 1));
                var unit = game.Combat.MobileUnitSnapshot().Single();
                Assert.AreEqual(result, unit.Unit.Id);
                Assert.AreEqual(-3776, unit.Position.X);
                Assert.AreEqual(4480, unit.Position.Y);
                Assert.AreEqual(0, wisps.Balance(WispResourceIds.Special));
                Assert.AreEqual(1, rng.Calls);
                Assert.AreEqual(0, game.Combat.Time);
                Assert.AreEqual(5, wisps.Balance(WispResourceIds.Random));
            }
        }


        private sealed class RareNativePool : IOriginal323NativeUnitPoolRandom
        {
            private readonly int index;
            public int Calls;
            public RareNativePool(int selectedIndex) { index=selectedIndex; }
            public int NextInclusive(int min,int max)
            {
                Calls++;
                Assert.That(index,Is.InRange(min,max));
                return index;
            }
        }
        private sealed class RareEffectWorld : IOriginalWispSpawnPositionPort,
            IOriginalWispBonusAbilityPort
        {
            public bool ApproveBonus;
            public int AbilityCalls;
            public bool TryGetPosition(string source,out double x,out double y)
            {
                x=-3776;y=4480;
                return source==WispResourceIds.Rare;
            }
            public bool TryApplyOriginalAbility(long instanceId,string ability)
            {
                AbilityCalls++;
                return ApproveBonus && instanceId>0 && ability=="A0YE";
            }
        }

        [Test]
        public void RareWispSpecialGaimonGrantsOneRealUnitWithoutA0ye()
        {
            var game=Create();
            var branch=new Rolls(100);
            var native=new RareNativePool(7);
            var effect=new RareEffectWorld();
            var source=new Original323AutomaticUncommonWisps(
                Data("original-2323C-wisp-pools.json"),branch,native,effect,effect);
            game.Economy.Wallet.Grant(new ResourceBundle().Add(WispResourceIds.Rare,1));
            using(var wisps=new MobileWispService(game.Economy.Wallet,source,
                new OriginalWispCombatRewardWorld(game.Combat,game.Fusion,source,source)))
            {
                Assert.AreEqual(1,wisps.FlushAutomatic());
                Assert.AreEqual(1,game.Combat.CharacterCount);
                var unit=game.Combat.MobileUnitSnapshot().Single();
                Assert.AreEqual(SpecialGradeMobilePolicy.Gaimon,unit.Unit.Id);
                Assert.AreEqual(-3776,unit.Position.X);
                Assert.AreEqual(4480,unit.Position.Y);
                Assert.AreEqual(0,wisps.Balance(WispResourceIds.Rare));
                Assert.AreEqual(0,effect.AbilityCalls);
                Assert.AreEqual(1,branch.Calls);
                Assert.AreEqual(1,native.Calls);
            }
        }

        [Test]
        public void RareWispFailedA0yePreservesOriginalRollAndWalletForRetry()
        {
            var game=Create();
            var branch=new Rolls(99);
            var native=new RareNativePool(0);
            var effect=new RareEffectWorld {ApproveBonus=false};
            var source=new Original323AutomaticUncommonWisps(
                Data("original-2323C-wisp-pools.json"),branch,native,effect,effect);
            game.Economy.Wallet.Grant(new ResourceBundle().Add(WispResourceIds.Rare,1));
            using(var wisps=new MobileWispService(game.Economy.Wallet,source,
                new OriginalWispCombatRewardWorld(game.Combat,game.Fusion,source,source)))
            {
                Assert.Throws<InvalidOperationException>(()=>wisps.FlushAutomatic());
                Assert.AreEqual(0,game.Combat.CharacterCount);
                Assert.AreEqual(1,wisps.Balance(WispResourceIds.Rare));
                Assert.AreEqual(1,branch.Calls);
                Assert.AreEqual(1,native.Calls);
                effect.ApproveBonus=true;
                Assert.AreEqual(1,wisps.FlushAutomatic());
                Assert.AreEqual(1,game.Combat.CharacterCount);
                Assert.AreEqual("R-01",game.Combat.MobileUnitSnapshot().Single().Unit.Id);
                Assert.AreEqual(0,wisps.Balance(WispResourceIds.Rare));
                Assert.AreEqual(1,branch.Calls,
                    "실패 후 원본 희귀위습 보상을 다시 뽑으면 안 됩니다.");
                Assert.AreEqual(1,native.Calls);
                Assert.AreEqual(2,effect.AbilityCalls);
            }
        }

        [Test]
        public void RareFusionUsesActualCasterPositionAndNeverAuthorizesLegendaryRecipes()
        {
            var game = Create();
            game.Combat.SpawnCharacter("S-07", 11, 22);
            game.Combat.SpawnCharacter("S-12", 33, 44);
            game.Combat.SpawnCharacter("S-18", 55, 66);
            var offer = Offer(game, "A00F"); // S-07 owns Gear Third fusion.
            Assert.IsTrue(offer.IsReady);
            Assert.AreEqual(ManualActionState.Succeeded,
                game.Fusion.ConfirmFusion("A00F", offer.SuggestedMaterialInstances).State);
            var result = game.Combat.MobileUnitSnapshot().Single();
            Assert.AreEqual("R-06", result.Unit.Id);
            Assert.AreEqual(11, result.Position.X);
            Assert.AreEqual(22, result.Position.Y);
            Assert.IsFalse(game.Fusion.CanCommitRecipe("A1A0"));
            Assert.IsFalse(game.Fusion.CanCommitRecipe("ET01"));
        }

        [Test]
        public void DefaultControllerPreparationSupportsBatchSummonsWithoutStartingFakeBattle()
        {
            var root = new GameObject("ORDR unit preparation integration");
            root.SetActive(false);
            try
            {
                var owner = root.AddComponent<OrdrCharacterCombatController>();
                typeof(OrdrCharacterCombatController).GetMethod("Awake",
                    BindingFlags.NonPublic | BindingFlags.Instance).Invoke(owner, null);
                owner.ConfigureRandomWispResourceRng(new Rolls(
                    10000, 0, 10000, 1, 2, 10000, 3, 10000, 4, 5, 10000, 6));
                owner.BeginUnitPreparation();
                Assert.IsTrue(owner.IsUnitPreparation);
                Assert.IsFalse(owner.HasSourceVerifiedDamageRuntime);
                Assert.AreEqual(13, owner.ExecutableCommonFusionCount);
                Assert.AreEqual(31, owner.ExecutableSpecialFusionCount);
                Assert.AreEqual(41, owner.ExecutableRareFusionCount);
                Assert.AreEqual(31, owner.ExecutableLegendaryFusionCount);
                Assert.AreEqual(ManualActionState.Succeeded, owner.RequestCommonWispBatch(5).State);
                Assert.AreEqual(5, owner.Combat.CharacterCount);
                Assert.AreEqual(6, owner.SharedEconomy.Units.Count); // Plus non-combat evolution.
                Assert.AreEqual(0, owner.WispBalance(WispResourceIds.Random));
                Assert.AreEqual(0, owner.Combat.Game.CurrentWave);
                typeof(OrdrCharacterCombatController).GetMethod("Update",
                    BindingFlags.NonPublic | BindingFlags.Instance).Invoke(owner, null);
                Assert.AreEqual(0, owner.Combat.Time);
                Assert.AreEqual(0, owner.Combat.EnemyCount);
                Assert.Throws<InvalidOperationException>(() => owner.BeginUnitPreparation());
                Assert.AreEqual(60, owner.SharedEconomy.Wallet.Balance("gold"));
                string blocked;
                Assert.IsFalse(owner.CanBeginVerifiedFirstWave(out blocked));
                Assert.Throws<InvalidOperationException>(() => owner.BeginVerifiedFirstWave());
                Assert.AreEqual(5, owner.Combat.CharacterCount);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
