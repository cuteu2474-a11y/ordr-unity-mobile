using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323BountyTreasureWorldTests
    {
        private sealed class PositionRolls : IOriginalWispRandom
        {
            public int NextInclusive(int min,int max) => (max-min)/2+min;
        }
        private sealed class TreasureNative : IOriginal323TreasureNativeWorld
        {
            public bool TryConfirmOriginalTreasureCast(double x,double y,
                double halfExtent,bool collect,int round,out string evidence)
            { evidence=null; return false; }
            public bool TrySettleTreasureOnce(string id,
                Original323TreasureReward reward,string cast,string proof)
                => false;
        }
        private sealed class NativeRecord : IOriginal323OnePieceNativeAbilityWorld
        {
            public int Applications;
            public bool TryApplyOnePieceOriginalAbilitiesOnce(
                string receipt,string evidence)
            {
                Applications++;
                return true;
            }
        }
        private sealed class Open : IOriginal323BountyChestOpenWitness
        {
            public int Roll;
            public int Gold;
            public bool TryObserveOriginalCrwOpen(string id,int round,
                out int roll,out int nativeGold,out string proof)
            {
                roll=Roll;nativeGold=Gold;proof="observed original CRW chest";
                return true;
            }
        }
        private static GameSession Game(int round)
        {
            var game=new GameSession();
            game.Start();
            for(int i=1;i<=round;i++)game.BeginWave(i);
            return game;
        }
        [Test]
        public void FirstFourMapsAddThreeAndFifthGrantsOnePieceWithoutBerry()
        {
            var wallet=new EconomyWallet();
            var rng=new PositionRolls();
            var session=new Original323MobileTreasureSession(rng,
                () => false,() => false);
            var hunt=new Original323TreasureHunt(new TreasureNative(),rng);
            var bounty=new Original323BountyTreasureWorld(
                wallet,Game(10),hunt,session);
            Assert.AreEqual(5,bounty.MapsRemaining);
            for(int i=0;i<4;i++)
            {
                var result=bounty.ProcessObservedMapPrize(
                    "map"+i,10,4-i,"source observed CRW map prize");
                Assert.IsTrue(result.Succeeded);
                Assert.AreEqual(3*(i+1),hunt.SpawnCount);
                Assert.AreEqual(4-i,bounty.MapsRemaining);
            }
            var final=bounty.ProcessObservedMapPrize(
                "map4",10,0,"source observed CRW last map prize");
            Assert.IsTrue(final.Succeeded);
            Assert.AreEqual(Original323CrwOutcome.OnePiece,final.Outcome);
            Assert.AreEqual(12,hunt.SpawnCount);
            Assert.AreEqual(0,bounty.MapsRemaining);
            Assert.AreEqual(10,wallet.Balance(
                Original323BountyTreasureWorld.LineExplosionIncreaseId));
            Assert.AreEqual(10,wallet.Balance(
                Original323BountyTreasureWorld.LineMagicDefenseIncreaseId));
            Assert.AreEqual(10,
                Original323SourceItemPassiveRules.SourceLineExplosionLevelIncrease(wallet));
            Assert.AreEqual(10,
                Original323SourceItemPassiveRules.SourceLineMagicDefenseLevelIncrease(wallet));
            Assert.AreEqual(1,wallet.Balance(
                Original323BountyTreasureWorld.OnePieceRecordId));
            Assert.AreEqual(1,wallet.Balance(
                Original323BountyTreasureWorld.NativeAbilitiesPendingId));
            Assert.IsFalse(bounty.ProcessObservedMapPrize(
                "map4-again",10,0,"source observed CRW last map prize").Succeeded);
            Assert.AreEqual(0,wallet.Balance("berry"));
        }

        [Test]
        public void CrwOutcomeRangesAndNativeAbilitiesUseRealSourceWitness()
        {
            var wallet=new EconomyWallet();
            var game=Game(10);
            var rng=new PositionRolls();
            var session=new Original323MobileTreasureSession(rng,
                () => false,() => false);
            var source=new Open { Roll=20 };
            var native=new NativeRecord();
            var hunt=new Original323TreasureHunt(new TreasureNative(),rng);
            var bounty=new Original323BountyTreasureWorld(
                wallet,game,hunt,session,source,native);
            Assert.IsTrue(bounty.ObserveChestOpen("wood").Succeeded);
            Assert.AreEqual(1,wallet.Balance("wood"));
            source.Roll=40;
            Assert.IsTrue(bounty.ObserveChestOpen("wisp").Succeeded);
            Assert.AreEqual(1,wallet.Balance(WispResourceIds.Random));
            source.Roll=95;source.Gold=2000;
            Assert.IsTrue(bounty.ObserveChestOpen("gold-min").Succeeded);
            Assert.AreEqual(2000,wallet.Balance("gold"));
            source.Roll=41;source.Gold=5000;
            Assert.IsTrue(bounty.ObserveChestOpen("gold-max").Succeeded);
            Assert.AreEqual(7000,wallet.Balance("gold"));
            source.Roll=96;source.Gold=0;
            for(int i=0;i<5;i++)
                Assert.IsTrue(bounty.ObserveChestOpen("map"+i).Succeeded);
            Assert.IsTrue(bounty.OnePieceRecorded);
            Assert.AreEqual(1,native.Applications);
            Assert.IsFalse(bounty.NativeOnePiecePending);
            source.Roll=100;source.Gold=3456;
            Assert.AreEqual(Original323CrwOutcome.Gold,
                bounty.ObserveChestOpen("post-map").Outcome);
            Assert.AreEqual(10456,wallet.Balance("gold"));
        }
    }
}
