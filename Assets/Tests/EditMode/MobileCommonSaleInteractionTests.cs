using NUnit.Framework;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileCommonSaleInteractionTests
    {
        private sealed class SalePort : IOriginalCommonSalePort
        {
            public int Calls;
            public long LastUnit;
            public ManualActionResult ConfirmSale(long id)
            {
                Calls++;
                LastUnit = id;
                return new ManualActionResult(ManualActionState.Succeeded,
                    "TEST_ONLY: preconfirmed source-backed transaction");
            }
        }

        [Test]
        public void SaleIsUnavailableUntilRealGameWorldIsConnected()
        {
            var mobile = new MobileManualInteraction();
            Assert.IsFalse(mobile.HasOriginalCommonSaleService);
            Assert.AreEqual(ManualActionState.NotImplemented,
                mobile.RequestCommonSale(1).State);
            Assert.AreEqual(ManualActionState.Rejected,
                mobile.RequestCommonSale(0).State);
        }

        [Test]
        public void ManualTapDoesNotDuplicateOrAutomaticallyExecuteSale()
        {
            var mobile = new MobileManualInteraction();
            var world = new SalePort();
            mobile.ConfigureOriginalCommonSale(world);
            Assert.IsTrue(mobile.HasOriginalCommonSaleService);
            Assert.AreEqual(0, world.Calls);
            Assert.AreEqual(ManualActionState.Succeeded,
                mobile.RequestCommonSale(42).State);
            Assert.AreEqual(1, world.Calls);
            Assert.AreEqual(42, world.LastUnit);
        }
    }
}
