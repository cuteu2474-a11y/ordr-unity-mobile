using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalLuffyCommonSaleCoordinatorTests
    {
        private static OriginalMapBaseStatCatalog OriginalMap()
        {
            string data = Path.Combine(Application.dataPath, "..", "data");
            var units = CharacterUnitCatalog.FromJson(
                File.ReadAllText(Path.Combine(data, "units.json")));
            return OriginalMapBaseStatCatalog.FromJson(
                File.ReadAllText(Path.Combine(data, "original-2323C-map-base-stats.json")),
                units);
        }

        // Explicitly a simulated atomic world: original runtime worlds must
        // implement this transaction before exposing sale on a device.
        private sealed class FakeWorld : IOriginalCommonSaleTransactionalWorld
        {
            public string Catalog = "C-01";
            public string Rawcode = "h003";
            public bool HasUnit = true;
            public bool Deployed;
            public int Sold;
            public int Wisps;
            public int Wood;
            public int Roll = 35;
            public int RngCalls;
            public bool RejectApply;
            public bool ThrowOnApply;
            public bool ThrowOnCommit;
            public bool Readable = true;
            public int BeginCalls;
            public int Rollbacks;

            public IOriginalCommonSaleTransaction BeginCommonSale()
            {
                BeginCalls++;
                return new Tx(this);
            }

            private sealed class Tx : IOriginalCommonSaleTransaction
            {
                private readonly FakeWorld world;
                private readonly bool snapshotUnit;
                private readonly int sold, wisps, wood, rngCalls;
                private bool committed;
                private bool disposed;
                public Tx(FakeWorld world)
                {
                    this.world = world;
                    snapshotUnit = world.HasUnit;
                    sold = world.Sold; wisps = world.Wisps;
                    wood = world.Wood; rngCalls = world.RngCalls;
                }
                public bool TryReadUnit(long id, out string catalog,
                    out string rawcode, out bool storyDeployed, out string reason)
                {
                    catalog = world.Catalog;
                    rawcode = world.Rawcode;
                    storyDeployed = world.Deployed;
                    reason = null;
                    return world.HasUnit && id == 3;
                }
                public bool TryReadSaleCount(out int count, out string reason)
                {
                    count = world.Sold;
                    reason = null;
                    return world.Readable;
                }
                public bool TryRollOriginalSharedBFiOneToHundred(out int result, out string reason)
                {
                    world.RngCalls++;
                    result = world.Roll;
                    reason = null;
                    return true;
                }
                public bool TryApplySale(long id, int count, string wispRaw,
                    double wispSpawnX, double wispSpawnY,
                    int wisps, int wood, out string reason)
                {
                    reason = world.RejectApply ? "TEST_ONLY: original unit spawn unavailable" : null;
                    if (world.RejectApply) return false;
                    if (!world.HasUnit || id != 3 || wispRaw != "e0IX" ||
                        wispSpawnX != -3456.0 || wispSpawnY != -9696.0 ||
                        count != world.Sold + 1)
                        throw new InvalidOperationException("TEST_ONLY: stale locked unit");
                    // Stage all mutations before the optional injected error.
                    world.HasUnit = false; world.Sold = count;
                    world.Wisps += wisps; world.Wood += wood;
                    if (world.ThrowOnApply) throw new InvalidOperationException("TEST_ONLY: partial apply");
                    return true;
                }
                public void Commit()
                {
                    if (world.ThrowOnCommit) throw new InvalidOperationException("TEST_ONLY: commit failure");
                    committed = true;
                }
                public void Dispose()
                {
                    if (disposed) return;
                    disposed = true;
                    if (committed) return;
                    world.HasUnit = snapshotUnit;
                    world.Sold = sold; world.Wisps = wisps;
                    world.Wood = wood; world.RngCalls = rngCalls;
                    world.Rollbacks++;
                }
            }
        }

        [Test]
        public void ExactLuffyObjectIsSoldOnlyOnce()
        {
            var state = new FakeWorld();
            var service = new OriginalLuffyCommonSaleCoordinator(state);
            Assert.AreEqual(ManualActionState.Succeeded, service.ConfirmSale(3).State);
            Assert.IsFalse(state.HasUnit);
            Assert.AreEqual(1, state.Sold);
            Assert.AreEqual(0, state.Wisps);
            Assert.AreEqual(0, state.Wood);
            Assert.AreEqual(0, state.RngCalls);
            Assert.AreEqual(ManualActionState.Rejected, service.ConfirmSale(3).State);
            Assert.AreEqual(1, state.Sold);
            Assert.AreEqual(0, state.Wisps);
        }

        [Test]
        public void ThirdSaleSpawnsOriginalWispAndUsesInclusive35Chance()
        {
            var state = new FakeWorld { Sold = 2, Roll = 35 };
            var service = new OriginalLuffyCommonSaleCoordinator(state);
            Assert.AreEqual(ManualActionState.Succeeded, service.ConfirmSale(3).State);
            Assert.AreEqual(3, state.Sold);
            Assert.AreEqual(1, state.Wisps);
            Assert.AreEqual(1, state.Wood);
            Assert.AreEqual(1, state.RngCalls);
            var next = new FakeWorld { Sold = 5, Roll = 36 };
            Assert.AreEqual(ManualActionState.Succeeded,
                new OriginalLuffyCommonSaleCoordinator(next).ConfirmSale(3).State);
            Assert.AreEqual(6, next.Sold);
            Assert.AreEqual(1, next.Wisps);
            Assert.AreEqual(0, next.Wood);
        }

        [Test]
        public void DifferentObjectAndDeployedOrUnavailableUnitCannotBeSold()
        {
            foreach (var broken in new[] {
                new FakeWorld { Catalog = "C-02" },
                new FakeWorld { Rawcode = "h002" },
                new FakeWorld { Deployed = true },
                new FakeWorld { HasUnit = false }
            })
            {
                Assert.AreEqual(ManualActionState.Rejected,
                    new OriginalLuffyCommonSaleCoordinator(broken).ConfirmSale(3).State);
                Assert.AreEqual(0, broken.Sold);
                Assert.IsTrue(broken.Wisps == 0 && broken.Wood == 0);
            }
            Assert.AreEqual(ManualActionState.Rejected,
                new OriginalLuffyCommonSaleCoordinator(new FakeWorld()).ConfirmSale(0).State);
        }

        [Test]
        public void AllLateFailuresRollbackUnitSaleRewardsAndRandomState()
        {
            foreach (bool onCommit in new[] { false, true })
            {
                var state = new FakeWorld {
                    Sold = 2, Roll = 35,
                    ThrowOnApply = !onCommit, ThrowOnCommit = onCommit
                };
                Assert.AreEqual(ManualActionState.Rejected,
                    new OriginalLuffyCommonSaleCoordinator(state).ConfirmSale(3).State);
                Assert.IsTrue(state.HasUnit);
                Assert.AreEqual(2, state.Sold);
                Assert.AreEqual(0, state.Wisps);
                Assert.AreEqual(0, state.Wood);
                Assert.AreEqual(0, state.RngCalls);
                Assert.AreEqual(1, state.Rollbacks);
            }
        }

        [Test]
        public void InvalidRandomAndFailedWispCreationDoNotConsumeUnit()
        {
            var invalid = new FakeWorld { Sold = 2, Roll = 101 };
            Assert.AreEqual(ManualActionState.Rejected,
                new OriginalLuffyCommonSaleCoordinator(invalid).ConfirmSale(3).State);
            Assert.IsTrue(invalid.HasUnit);
            Assert.AreEqual(0, invalid.RngCalls);
            var noWorld = new FakeWorld { Sold = 2, RejectApply = true };
            Assert.AreEqual(ManualActionState.Rejected,
                new OriginalLuffyCommonSaleCoordinator(noWorld).ConfirmSale(3).State);
            Assert.IsTrue(noWorld.HasUnit);
            Assert.AreEqual(0, noWorld.RngCalls);
        }

        [Test]
        public void MissingSourceCounterCannotGrantOrConsumeAnything()
        {
            var world = new FakeWorld { Readable = false, Sold = 2 };
            Assert.AreEqual(ManualActionState.Rejected,
                new OriginalLuffyCommonSaleCoordinator(world).ConfirmSale(3).State);
            Assert.IsTrue(world.HasUnit);
            Assert.AreEqual(0, world.RngCalls);
            Assert.AreEqual(0, world.Wisps);
        }
        [Test]
        public void AllNineCommonsShareOneSaleCounterAndOriginalWispMilestones()
        {
            var map = OriginalMap();
            var state = new FakeWorld();
            var service = new OriginalCommonSaleCoordinator(state, map);
            for (int index = 1; index <= 9; index++)
            {
                string id = "C-" + index.ToString("D2");
                state.Catalog = id;
                state.Rawcode = map.Find(id).OriginalRawcode;
                state.HasUnit = true; // TEST_ONLY: different owned unit in each transaction.
                Assert.AreEqual(ManualActionState.Succeeded,
                    service.ConfirmSale(3).State, id);
                Assert.AreEqual(index, state.Sold, id);
                Assert.AreEqual(index / 3, state.Wisps, id);
                Assert.AreEqual(index / 3, state.Wood, id);
                Assert.AreEqual(index / 3, state.RngCalls, id);
            }
        }

        [Test]
        public void AllCommonCoordinatorRejectsSpoofedRawcodeAndDifferentGrade()
        {
            var state = new FakeWorld { Catalog = "C-02", Rawcode = "h003" };
            var service = new OriginalCommonSaleCoordinator(state, OriginalMap());
            Assert.AreEqual(ManualActionState.Rejected, service.ConfirmSale(3).State);
            Assert.IsTrue(state.HasUnit);
            Assert.AreEqual(0, state.Sold);

            state.Catalog = "U-01";
            state.Rawcode = "h002";
            Assert.AreEqual(ManualActionState.Rejected, service.ConfirmSale(3).State);
            Assert.AreEqual(0, state.Sold);
        }

        [Test]
        public void AllCommonCoordinatorRollsBackThirdSaleAcrossUnitKinds()
        {
            var state = new FakeWorld {
                Catalog = "C-02", Rawcode = "h002", Sold = 2,
                Roll = 35, ThrowOnCommit = true
            };
            var service = new OriginalCommonSaleCoordinator(state, OriginalMap());
            Assert.AreEqual(ManualActionState.Rejected, service.ConfirmSale(3).State);
            Assert.IsTrue(state.HasUnit);
            Assert.AreEqual(2, state.Sold);
            Assert.AreEqual(0, state.Wisps);
            Assert.AreEqual(0, state.Wood);
            Assert.AreEqual(0, state.RngCalls);
            Assert.AreEqual(1, state.Rollbacks);
        }
    }
}
