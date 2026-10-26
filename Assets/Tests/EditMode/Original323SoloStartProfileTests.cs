using System;
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

        [Test]
        public void RosterEvolutionActorUsesTheSameSessionAndIsNotDuplicated()
        {
            var roster = new UnitRoster();
            var shared = new SharedEconomySession(new EconomyWallet(), roster);
            var evolution = new Original323RosterStartEvolutionWorld(roster);
            var source = Original323SoloStartProfile.Create();
            var original = source.StartingUnits[0];

            long id = evolution.SpawnAndTrack(shared, original.CatalogId,
                original.X, original.Y);
            Assert.AreEqual(1, roster.Count);
            Assert.AreEqual("e0HP", roster.Get(id).UnitId);
            Assert.Throws<InvalidOperationException>(() =>
                evolution.SpawnAndTrack(shared, original.CatalogId,
                    original.X, original.Y));
            Assert.Throws<InvalidOperationException>(() =>
                evolution.SpawnAndTrack(shared, "h003", original.X, original.Y));
            Assert.Throws<InvalidOperationException>(() =>
                evolution.RemoveAndUntrack(new SharedEconomySession(
                    new EconomyWallet(), new UnitRoster()), id));

            evolution.RemoveAndUntrack(shared, id);
            Assert.AreEqual(0, roster.Count);
            Assert.Throws<InvalidOperationException>(() =>
                evolution.RemoveAndUntrack(shared, id));
            Assert.Greater(evolution.SpawnAndTrack(shared, original.CatalogId,
                original.X, original.Y), id);
        }

        [Test]
        public void StartFailureRollsBackEvolutionAndWalletTogether()
        {
            var roster = new UnitRoster();
            var wallet = new EconomyWallet();
            var shared = new SharedEconomySession(wallet, roster);
            var evolution = new Original323RosterStartEvolutionWorld(roster);
            var profile = new VerifiedEconomyStartProfile("ORDR_S2_2.323[C]",
                GameSession.ModeId, "TEST_ONLY rollback sequence", true,
                new ResourceBundle().Add("gold", 60),
                new[] {
                    new VerifiedStartingUnit("e0HP",
                        Original323SoloStartProfile.EvolutionX,
                        Original323SoloStartProfile.EvolutionY),
                    new VerifiedStartingUnit("TEST_ONLY_second_actor", 0, 0)
                });
            Assert.Throws<InvalidOperationException>(() =>
                shared.ApplyVerifiedStart(profile,
                    (raw, x, y) => evolution.SpawnAndTrack(shared, raw, x, y),
                    id => evolution.RemoveAndUntrack(shared, id)));
            Assert.AreEqual(0, roster.Count);
            Assert.AreEqual(0, wallet.Balance("gold"));
            Assert.IsFalse(shared.HasStarted);
            // The rollback restored the adapter's actor tracking, not just
            // the wallet: the next genuine session can create e0HP once.
            shared.ApplyVerifiedStart(Original323SoloStartProfile.Create(),
                (raw, x, y) => evolution.SpawnAndTrack(shared, raw, x, y),
                id => evolution.RemoveAndUntrack(shared, id));
            Assert.AreEqual(60, wallet.Balance("gold"));
            Assert.AreEqual(1, roster.Count);
        }
    }
}
