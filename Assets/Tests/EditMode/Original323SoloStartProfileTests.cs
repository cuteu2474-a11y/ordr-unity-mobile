using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323SoloStartProfileTests
    {
        [Test]
        public void CuoAndCunSupply60GoldFiveRandomWispsAndEvolutionActor()
        {
            var profile = Original323SoloStartProfile.Create();
            Assert.IsTrue(profile.IsSourceVerified);
            Assert.AreEqual(60, profile.Resources.Get("gold"));
            Assert.AreEqual(5, profile.Resources.Get(WispResourceIds.Random));
            Assert.AreEqual(1, profile.StartingUnits.Count);
            Assert.AreEqual("e0HP", profile.StartingUnits[0].CatalogId);
            Assert.AreEqual(-3456, profile.StartingUnits[0].X);
            Assert.AreEqual(-9696, profile.StartingUnits[0].Y);

            var wallet = new EconomyWallet();
            var roster = new UnitRoster();
            var session = new SharedEconomySession(wallet, roster);
            session.ApplyVerifiedStart(profile, roster.Add, id => { roster.Remove(id); });
            Assert.AreEqual(60, wallet.Balance("gold"));
            Assert.AreEqual(5, wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(1, roster.Count);
            Assert.Throws<System.InvalidOperationException>(() =>
                session.ApplyVerifiedStart(profile, roster.Add, id => { roster.Remove(id); }));
        }
    }
}
