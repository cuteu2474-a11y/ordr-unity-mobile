using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalJassSimpleFusionTransactionTests
    {
        // TEST_ONLY original-object mappings from the source cross-reference.
        // This map is NOT a production reviewed-objects approval file.
        private sealed class TestMap : IVerifiedUnitRawcodeMap
        {
            public static readonly string[] Inputs = {
                "h05F", "h05W", "h01X", "h01S", "h00S", "h01G"
            };
            public static readonly string[] Ids = {
                "LI-09", "T-02", "R-06", "R-17", "S-04", "S-32"
            };
            public bool TryResolveUnit(string rawcode, out string catalogId)
            {
                if (rawcode == "h057") { catalogId = "E-06"; return true; }
                for (int i = 0; i < Inputs.Length; i++)
                    if (Inputs[i] == rawcode)
                    { catalogId = Ids[i]; return true; }
                catalogId = null;
                return false;
            }
        }

        // This is an isolated *test* world. In production the atomic
        // implementation must use the game's actual live roster, actual
        // original unit object IDs, wallet, CHu/CHx effects and transaction.
        private sealed class TestWorld : IOriginalJassSimpleFusionTransactionalWorld,
            IOriginalJassFusionResourceReader, IOriginalJassFusionCastPolicy
        {
            public readonly CharacterUnitCatalog Catalog;
            public List<MobileUnitSnapshot> Units =
                new List<MobileUnitSnapshot>();
            public Dictionary<long, string> Rawcodes =
                new Dictionary<long, string>();
            public int Gold;
            public int Lumber = 5;
            public int Commits;
            public int Rollbacks;
            public bool FailAfterStaging;
            public bool WrongCreatedRawcode;
            public bool InvalidLockedMaterial;
            private bool busy;

            public TestWorld(CharacterUnitCatalog catalog)
            {
                Catalog = catalog;
                for (int i = 0; i < TestMap.Inputs.Length; i++)
                {
                    long id = i + 1;
                    Units.Add(new MobileUnitSnapshot(id,
                        catalog.Require(TestMap.Ids[i]), null,
                        new UnitState(id, TestMap.Ids[i], 100 + i, 200)));
                    Rawcodes.Add(id, TestMap.Inputs[i]);
                }
            }

            public bool TryReadBalances(out int gold, out int lumber)
            { gold = Gold; lumber = Lumber; return true; }

            public bool TryChooseCastTarget(string raw, IReadOnlyList<long> ids,
                out long target, out string reason)
            {
                target = ids.Count == 0 ? 0 : ids[0];
                reason = target > 0 ? null : "재료 대상 없음";
                return target > 0;
            }

            public IOriginalJassSimpleFusionLockedTransaction BeginExclusiveFusion()
            {
                if (busy) throw new InvalidOperationException("이중 거래 잠금");
                busy = true;
                return new Locked(this);
            }

            private sealed class Locked : IOriginalJassSimpleFusionLockedTransaction
            {
                private readonly TestWorld world;
                private readonly List<MobileUnitSnapshot> units;
                private readonly Dictionary<long, string> raws;
                private int gold;
                private int lumber;
                private bool committed;

                public Locked(TestWorld world)
                {
                    this.world = world;
                    units = new List<MobileUnitSnapshot>(world.Units);
                    raws = new Dictionary<long, string>(world.Rawcodes);
                    gold = world.Gold;
                    lumber = world.Lumber;
                    if (world.InvalidLockedMaterial)
                        raws[TestMap.Inputs.Length] = "xxxx";
                }

                public IReadOnlyList<MobileUnitSnapshot> OwnedUnits()
                { return units.AsReadOnly(); }

                public bool TryReadBalances(out int outGold, out int outWood)
                { outGold = gold; outWood = lumber; return true; }

                public bool TryReadOriginalUnitRawcode(long id, out string code)
                { return raws.TryGetValue(id, out code); }

                public bool TryChooseCastTarget(string raw,
                    IReadOnlyList<long> ids, out long target, out string reason)
                {
                    target = ids.Count == 0 ? 0 : ids[0];
                    reason = target == 0 ? "원본 대상 미확인" : null;
                    return target != 0;
                }

                public bool TryStageOriginalFusion(OriginalJassSimpleFusionCommand cmd,
                    out long spawned, out string reason)
                {
                    spawned = 0;
                    reason = null;
                    if (cmd.AbilityRawcode != "ET01" ||
                        cmd.ResultUnitId != "E-06" || cmd.ResultRawcode != "h057" ||
                        cmd.MaterialInstanceIds.Count != 6 ||
                        cmd.Gold != 0 || cmd.Lumber != 5 ||
                        lumber < cmd.Lumber)
                    {
                        reason = "원본 주문과 다름";
                        return false;
                    }
                    var seen = new HashSet<long>();
                    for (int i = 0; i < cmd.MaterialInstanceIds.Count; ++i)
                    {
                        long id = cmd.MaterialInstanceIds[i];
                        string actual;
                        if (!seen.Add(id) || !raws.TryGetValue(id, out actual) ||
                            actual != cmd.MaterialRawcodes[i])
                        {
                            reason = "원본 재료 객체 불일치";
                            return false;
                        }
                    }
                    foreach (long id in cmd.MaterialInstanceIds)
                    {
                        units.RemoveAll(u => u.InstanceId == id);
                        raws.Remove(id);
                    }
                    gold -= cmd.Gold;
                    lumber -= cmd.Lumber;
                    spawned = 100;
                    units.Add(new MobileUnitSnapshot(spawned,
                        world.Catalog.Require("E-06"), null,
                        new UnitState(spawned, "E-06", 111, 222)));
                    raws.Add(spawned,
                        world.WrongCreatedRawcode ? "xxxx" : cmd.ResultRawcode);
                    if (world.FailAfterStaging)
                    {
                        reason = "원본 후속 처리 실패";
                        return false;
                    }
                    return true;
                }

                public void Commit()
                {
                    if (committed) throw new InvalidOperationException("중복 커밋");
                    world.Units = units;
                    world.Rawcodes = raws;
                    world.Gold = gold;
                    world.Lumber = lumber;
                    world.Commits++;
                    committed = true;
                }

                public void Dispose()
                {
                    if (!committed) world.Rollbacks++;
                    world.busy = false;
                }
            }
        }

        private sealed class Setup
        {
            public TestWorld World;
            public OriginalJassFusionPort Port;
            public OriginalJassSimpleOrderedSessionFactory Factory;
        }

        private static Setup Create()
        {
            var unitsJson = Resources.Load<TextAsset>("OrdrData/units");
            var sourceJson = Resources.Load<TextAsset>(
                "OrdrData/original-jass-fusion-registry");
            var labelsJson = Resources.Load<TextAsset>(
                "OrdrData/original-unit-rawcode-label-links");
            Assert.IsNotNull(unitsJson);
            Assert.IsNotNull(sourceJson);
            Assert.IsNotNull(labelsJson);
            var units = CharacterUnitCatalog.FromJson(unitsJson.text);
            var source = OriginalJassFusionCatalog.FromJson(sourceJson.text);
            var labels = OriginalUnitRawcodeLabelCatalog.FromJson(
                labelsJson.text, units, source);
            var world = new TestWorld(units);
            var planner = new OriginalJassFusionPlanner(source, labels,
                id => false, new OriginalJassNoSpecialConditions(), units);
            planner.SetVerifiedObjectMap(new TestMap());
            var factory = new OriginalJassSimpleOrderedSessionFactory(source, world);
            var adapter = new OriginalJassFusionOrderedWorldAdapter(factory);
            var coordinator = new OriginalJassFusionAtomicCoordinator(planner, adapter);
            var port = new OriginalJassFusionPort(planner,
                () => world.Units.AsReadOnly(), world, coordinator, world);
            return new Setup { World = world, Port = port, Factory = factory };
        }

        private static MobileFusionPreview Preview(Setup ctx)
        {
            foreach (var offer in ctx.Port.AvailableRecipes(ctx.World.Units))
                if (offer.RecipeId == "ET01") return offer;
            Assert.Fail("원본 ET01 비비 영원 조합이 없습니다.");
            return null;
        }

        [Test]
        public void RealEt01OrderCommitsSixExactMaterialsFiveWoodAndOneVivianResult()
        {
            var game = Create();
            var offer = Preview(game);
            Assert.IsTrue(game.Port.CanCommitRecipe("ET01"));
            Assert.IsTrue(offer.IsReady);
            CollectionAssert.AreEquivalent(new long[] { 1, 2, 3, 4, 5, 6 },
                offer.SuggestedMaterialInstances);
            Assert.AreEqual(ManualActionState.Succeeded,
                game.Port.ConfirmFusion("ET01", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(1, game.World.Commits);
            Assert.AreEqual(1, game.World.Units.Count);
            Assert.AreEqual("E-06", game.World.Units[0].Unit.Id);
            Assert.AreEqual("h057", game.World.Rawcodes[100]);
            Assert.AreEqual(0, game.World.Gold);
            Assert.AreEqual(0, game.World.Lumber);
            Assert.AreEqual(ManualActionState.Rejected,
                game.Port.ConfirmFusion("ET01", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(1, game.World.Commits);
        }

        [Test]
        public void SimpleScopeNeverApprovesExtraUpunOrOtherOriginalConditions()
        {
            var game = Create();
            var source = Resources.Load<TextAsset>(
                "OrdrData/original-jass-fusion-registry");
            var registry = OriginalJassFusionCatalog.FromJson(source.text);
            int supported = 0;
            foreach (var recipe in registry.All)
                if (game.Factory.CanCommitRecipe(recipe.AbilityRawcode))
                    supported++;
            Assert.AreEqual(5, supported);
            Assert.IsFalse(game.Port.CanCommitRecipe("A00D"));
            Assert.IsFalse(game.Port.CanCommitRecipe("XXXX"));
            Assert.AreEqual(ManualActionState.NotImplemented,
                game.Port.ConfirmFusion("A00D", new long[] { 1, 2 }).State);
            Assert.AreEqual(0, game.World.Commits);
        }

        [Test]
        public void FailureAfterStagingRollsBackMaterialAndWoodCompletely()
        {
            var game = Create();
            var offer = Preview(game);
            game.World.FailAfterStaging = true;
            Assert.AreEqual(ManualActionState.Rejected,
                game.Port.ConfirmFusion("ET01", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(0, game.World.Commits);
            Assert.Greater(game.World.Rollbacks, 0);
            Assert.AreEqual(6, game.World.Units.Count);
            Assert.AreEqual(5, game.World.Lumber);
            Assert.IsFalse(game.World.Rawcodes.ContainsKey(100));
        }

        [Test]
        public void WrongOriginalObjectOrForgedResultCannotCommit()
        {
            var game = Create();
            var offer = Preview(game);
            game.World.WrongCreatedRawcode = true;
            Assert.AreEqual(ManualActionState.Rejected,
                game.Port.ConfirmFusion("ET01", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(0, game.World.Commits);
            Assert.AreEqual(6, game.World.Units.Count);

            var lockedMismatch = Create();
            var originalPreview = Preview(lockedMismatch);
            lockedMismatch.World.InvalidLockedMaterial = true;
            Assert.AreEqual(ManualActionState.Rejected,
                lockedMismatch.Port.ConfirmFusion("ET01",
                    originalPreview.SuggestedMaterialInstances).State);
            Assert.AreEqual(0, lockedMismatch.World.Commits);
            Assert.AreEqual(5, lockedMismatch.World.Lumber);
        }
    }
}
