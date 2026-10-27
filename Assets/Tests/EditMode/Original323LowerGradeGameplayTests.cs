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
            Assert.AreEqual(1, notifications);
            Assert.AreEqual(ManualActionState.Rejected, game.Fusion.ConfirmFusion(ability, consumed).State);
            Assert.AreEqual(1, notifications);
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
            Assert.AreEqual(44, game.Fusion.ExecutableRecipeCount);
            var fresh = Offer(game, "A012");
            Assert.AreEqual(ManualActionState.Succeeded,
                game.Fusion.ConfirmFusion("A012", fresh.SuggestedMaterialInstances).State);
            Assert.AreEqual("S-07", game.Combat.MobileUnitSnapshot().Single().Unit.Id);
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
