using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Core;
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
                new MobileUnitSnapshot(1, units.Require("RN-01"), null, new UnitState(1, "RN-01", 0, 0)),
                new MobileUnitSnapshot(2, units.Require("D-02"), null, new UnitState(2, "D-02", 0, 0)),
                new MobileUnitSnapshot(3, units.Require("RN-02"), null, new UnitState(3, "RN-02", 0, 0)),
                new MobileUnitSnapshot(4, units.Require("RN-02"), null, new UnitState(4, "RN-02", 0, 0))
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
                new MobileUnitSnapshot(1, units.Require("RN-01"), null, new UnitState(1, "RN-01", 0, 0)),
                new MobileUnitSnapshot(2, units.Require("D-02"), null, new UnitState(2, "D-02", 0, 0))
            })[0];
            CollectionAssert.AreEquivalent(new long[] { 1, 2 }, preview.SuggestedMaterialInstances);
            CollectionAssert.Contains(preview.MissingMaterials, "랜덤 등급 유닛 1기");
            Assert.IsFalse(preview.IsReady);
        }

        private static MobileUnitSnapshot Unit(CharacterUnitCatalog units, long id, string type)
        {
            return new MobileUnitSnapshot(id, units.Require(type), null,
                new UnitState(id, type, 0, 0));
        }

        private static FakeMysticWorld World(int gold = 10000, int lumber = 7)
        {
            var units = CharacterUnitCatalog.FromJson(UnitsJson);
            return new FakeMysticWorld(units, new[]
            {
                Unit(units, 11, "RN-01"),
                Unit(units, 12, "RN-02"),
                Unit(units, 13, "D-02")
            }, gold, lumber);
        }

        private static OtherworldMysticFusionAtomicCoordinator Coordinator(
            OtherworldMysticFusionCatalog catalog, FakeMysticWorld world)
        {
            return new OtherworldMysticFusionAtomicCoordinator(catalog, world);
        }

        [Test]
        public void VerifiedAtomicWorldExecutesUserConfirmedFusionExactlyOnce()
        {
            var catalog = Read();
            var world = World();
            world.AllowOriginalVerification = true;
            var port = new OtherworldMysticFusionPreviewPort(catalog, id => false,
                Coordinator(catalog, world));
            var manual = new MobileManualInteraction();
            manual.Configure(null, port, null);
            Assert.IsTrue(manual.HasExecutableFusionService);
            var preview = manual.PreviewFusion(world.Owned)[0];
            Assert.IsTrue(preview.IsReady);
            CollectionAssert.AreEquivalent(new long[] { 11, 12, 13 },
                preview.SuggestedMaterialInstances);

            Assert.AreEqual(ManualActionState.Succeeded, manual.ConfirmFusion(preview).State);
            Assert.AreEqual(1, world.Commits);
            Assert.AreEqual(0, world.Gold);
            Assert.AreEqual(0, world.Lumber);
            Assert.AreEqual(1, world.Owned.Count);
            Assert.AreEqual("MY-01", world.Owned[0].Unit.Id);
            Assert.AreEqual(ManualActionState.Rejected, manual.ConfirmFusion(preview).State);
            Assert.AreEqual(1, world.Commits);
        }

        [Test]
        public void OriginalSpecialItemAndCastVerificationIsMandatoryEvenIfPreviewIsReady()
        {
            var catalog = Read();
            var world = World();
            var port = new OtherworldMysticFusionPreviewPort(catalog, id => false,
                Coordinator(catalog, world));
            var preview = port.AvailableRecipes(world.Owned)[0];
            Assert.IsTrue(preview.IsReady);
            Assert.AreEqual(ManualActionState.Rejected,
                port.ConfirmFusion("MY-01", preview.SuggestedMaterialInstances).State);
            Assert.AreEqual(0, world.Commits);
            Assert.AreEqual(10000, world.Gold);
            Assert.AreEqual(7, world.Lumber);
            Assert.AreEqual(3, world.Owned.Count);
        }

        [Test]
        public void OriginalApplyFailureAfterStagedCostsRollsBackEverything()
        {
            var catalog = Read();
            var world = World();
            world.AllowOriginalVerification = true;
            world.FailAfterStagedMutation = true;
            var executor = Coordinator(catalog, world);

            Assert.AreEqual(ManualActionState.Rejected,
                executor.TryCommit(catalog.Find("MY-01"), new long[] { 11, 12, 13 }).State);
            Assert.AreEqual(0, world.Commits);
            Assert.Greater(world.Rollbacks, 0);
            Assert.AreEqual(10000, world.Gold);
            Assert.AreEqual(7, world.Lumber);
            Assert.AreEqual(3, world.Owned.Count);
        }

        [Test]
        public void DuplicateStaleOrWrongGradeMaterialsNeverSpendAnything()
        {
            var catalog = Read();
            var world = World();
            world.AllowOriginalVerification = true;
            var executor = Coordinator(catalog, world);
            var recipe = catalog.Find("MY-01");
            Assert.AreEqual(ManualActionState.Rejected,
                executor.TryCommit(recipe, new long[] { 11, 11, 13 }).State);
            Assert.AreEqual(ManualActionState.Rejected,
                executor.TryCommit(recipe, new long[] { 11, 999, 13 }).State);
            Assert.AreEqual(ManualActionState.Rejected,
                executor.TryCommit(recipe, new long[] { 11, 13, 13 }).State);
            Assert.AreEqual(ManualActionState.Rejected,
                executor.TryCommit(recipe, new long[] { 11, 13 }).State);
            Assert.AreEqual(0, world.Commits);
            Assert.AreEqual(3, world.Owned.Count);
        }

        [Test]
        public void StoryDispatchedUnitOrInsufficientBalanceBlocksCommit()
        {
            var catalog = Read();
            var world = World(9999, 7);
            world.AllowOriginalVerification = true;
            var executor = Coordinator(catalog, world);
            var ids = new long[] { 11, 12, 13 };
            Assert.AreEqual(ManualActionState.Rejected,
                executor.TryCommit(catalog.Find("MY-01"), ids).State);
            world.Gold = 10000;
            world.Lumber = 6;
            Assert.AreEqual(ManualActionState.Rejected,
                executor.TryCommit(catalog.Find("MY-01"), ids).State);
            world.Lumber = 7;
            world.Deployed.Add(12);
            Assert.AreEqual(ManualActionState.Rejected,
                executor.TryCommit(catalog.Find("MY-01"), ids).State);
            Assert.AreEqual(0, world.Commits);
            Assert.AreEqual(3, world.Owned.Count);
        }

        // Test-only world: proving the atomic call/rollback integration does NOT
        // mean that the game's original JASS item/rawcode rules have been recovered.
        private sealed class FakeMysticWorld : IOriginalOtherworldMysticTransactionalWorld
        {
            private readonly CharacterUnitCatalog units;
            public readonly HashSet<long> Deployed = new HashSet<long>();
            public List<MobileUnitSnapshot> Owned;
            public int Gold;
            public int Lumber;
            public int Commits;
            public int Rollbacks;
            public bool AllowOriginalVerification;
            public bool FailAfterStagedMutation;

            public FakeMysticWorld(CharacterUnitCatalog catalog,
                IEnumerable<MobileUnitSnapshot> startingUnits, int gold, int lumber)
            {
                units = catalog;
                Owned = new List<MobileUnitSnapshot>(startingUnits);
                Gold = gold;
                Lumber = lumber;
            }

            public IOriginalOtherworldMysticFusionTransaction BeginMysticFusionTransaction()
            {
                return new StagedTransaction(this);
            }

            private sealed class StagedTransaction : IOriginalOtherworldMysticFusionTransaction
            {
                private readonly FakeMysticWorld world;
                private readonly List<MobileUnitSnapshot> stagedUnits;
                private int gold;
                private int lumber;
                private bool committed;

                public StagedTransaction(FakeMysticWorld world)
                {
                    this.world = world;
                    stagedUnits = new List<MobileUnitSnapshot>(world.Owned);
                    gold = world.Gold;
                    lumber = world.Lumber;
                }

                public IReadOnlyList<MobileUnitSnapshot> OwnedUnits() { return stagedUnits; }
                public bool IsStoryDeployed(long instanceId) { return world.Deployed.Contains(instanceId); }
                public bool TryReadBalances(out int balanceGold, out int balanceLumber)
                {
                    balanceGold = gold;
                    balanceLumber = lumber;
                    return true;
                }

                public bool TryVerifyOriginalConditions(OtherworldMysticRecipe recipe,
                    IReadOnlyList<long> materialIds, out string reason)
                {
                    reason = world.AllowOriginalVerification ? null : "원본 아이템/트리거 규칙 미검증";
                    return world.AllowOriginalVerification;
                }

                public bool TryApplyOriginalFusion(OtherworldMysticRecipe recipe,
                    IReadOnlyList<long> materialIds, out string reason)
                {
                    foreach (long id in materialIds)
                        stagedUnits.RemoveAll(u => u.InstanceId == id);
                    gold -= recipe.Gold;
                    lumber -= recipe.Lumber;
                    reason = world.FailAfterStagedMutation ? "원본 후처리 실패" : null;
                    if (world.FailAfterStagedMutation) return false;
                    stagedUnits.Add(Unit(world.units, 100, recipe.ResultUnitId));
                    return true;
                }

                public void Commit()
                {
                    world.Owned = stagedUnits;
                    world.Gold = gold;
                    world.Lumber = lumber;
                    world.Commits++;
                    committed = true;
                }

                public void Dispose()
                {
                    if (!committed) world.Rollbacks++;
                }
            }
        }
    }
}
