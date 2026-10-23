using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalLuffyCommonSaleCoordinatorTests
    {
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
                public bool TryRollOriginalBonusWood(out int result, out string reason)
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
    }
}
