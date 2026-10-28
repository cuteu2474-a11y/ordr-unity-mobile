using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323GaimonRevealPortTests
    {
        private sealed class Rng : IOriginalWispRandom
        {
            public int Calls;
            public int NextInclusive(int min,int max)
            {
                Calls++;
                throw new InvalidOperationException(
                    "CCQ reveal must never consume BeI random");
            }
        }
        private sealed class World : IOriginal323TreasureNativeWorld
        {
            public int Casts,Payments;
            public bool Approved=true;
            public bool LastCollect;
            public double LastRange;
            public bool TryConfirmOriginalTreasureCast(double x,double y,
                double range,bool collect,int round,out string evidence)
            {
                Casts++;LastCollect=collect;LastRange=range;
                evidence="original CCQ observed BeN(false) cast";
                return Approved;
            }
            public bool TrySettleTreasureOnce(string id,
                Original323TreasureReward reward,string cast,string spawn)
            {
                Payments++;throw new InvalidOperationException(
                    "Reveal cannot settle a chest");
            }
        }
        private sealed class Ccq : IOriginal323GaimonCcqEventWitness
        {
            public bool Approved=true;
            public int Calls;
            public double X=100,Y=100;
            public string Evidence="original Warcraft verified CCQ R cast";
            public bool TryObserveOriginalCcq(string id,int round,
                out double x,out double y,out string proof)
            {
                Calls++;x=X;y=Y;proof=Evidence;
                return Approved;
            }
        }
        private static Original323TreasureHunt Hunt(World world,Rng rng)
        {
            var h=new Original323TreasureHunt(world,rng);
            h.RegisterObservedSpawn("first",0,100,100,
                "observed source treasure map BeM event");
            h.RegisterObservedSpawn("second",0,300,200,
                "observed source treasure map BeM event");
            h.RegisterObservedSpawn("future",20,150,150,
                "observed source round 20 BeM event");
            return h;
        }

        [Test]
        public void OriginalCcqRevealsButNeverCollectsOrConsumesRewardRandom()
        {
            var world=new World();var rng=new Rng();
            var hunt=Hunt(world,rng);
            var port=new Original323GaimonRevealPort(hunt,new Ccq());
            var result=port.ObserveOriginalCcq("ccq-1",10,true,true);
            Assert.IsTrue(result.Authorized);
            Assert.AreEqual(2,result.RevealedCount);
            Assert.AreEqual(10,result.OriginalRound);
            Assert.AreEqual(2,hunt.UncollectedCountAtOriginalRound(10));
            Assert.AreEqual(3,hunt.UncollectedCountAtOriginalRound(20));
            Assert.AreEqual(0,hunt.CollectedCount);
            Assert.AreEqual(0,world.Payments);
            Assert.AreEqual(0,rng.Calls);
            Assert.AreEqual(2000d,world.LastRange);
            Assert.IsFalse(world.LastCollect);
            Assert.AreSame(result,port.LastAuthorizedReveal);
        }

        [Test]
        public void ReplayingCcqEventNeverRecastsOrRevealsFutureChest()
        {
            var world=new World();var rng=new Rng();
            var witness=new Ccq();var hunt=Hunt(world,rng);
            var port=new Original323GaimonRevealPort(hunt,witness);
            Assert.AreEqual(2,
                port.ObserveOriginalCcq("ccq-1",10,true,true).RevealedCount);
            var replay=port.ObserveOriginalCcq("ccq-1",20,true,true);
            Assert.IsFalse(replay.Authorized);
            Assert.AreEqual(0,replay.RevealedCount);
            Assert.AreEqual(1,port.ObservedEventCount);
            Assert.AreEqual(1,witness.Calls);
            Assert.AreEqual(1,world.Casts);
            Assert.AreEqual(3,
                port.ObserveOriginalCcq("ccq-2",20,true,true).RevealedCount);
        }

        [Test]
        public void NoOwnershipDreamOrSourceWitnessRejectsWithoutCasting()
        {
            var w=new World();var source=new Ccq();
            var port=new Original323GaimonRevealPort(Hunt(w,new Rng()),source);
            Assert.IsFalse(port.ObserveOriginalCcq("x",10,false,true).Authorized);
            Assert.IsFalse(port.ObserveOriginalCcq("x",10,true,false).Authorized);
            Assert.IsFalse(port.ObserveOriginalCcq("",10,true,true).Authorized);
            Assert.AreEqual(0,source.Calls);
            Assert.AreEqual(0,w.Casts);
            source.Approved=false;
            Assert.IsFalse(port.ObserveOriginalCcq("x",10,true,true).Authorized);
            source.Approved=true;source.Evidence="TEST_ONLY synthetic";
            Assert.IsFalse(port.ObserveOriginalCcq("x",10,true,true).Authorized);
            source.Evidence="original CCQ observed event";
            Assert.IsTrue(port.ObserveOriginalCcq("x",10,true,true).Authorized);
        }

        [Test]
        public void NativeDenialConsumesObservedCcqButPaysNothing()
        {
            var w=new World {Approved=false};var rng=new Rng();
            var port=new Original323GaimonRevealPort(Hunt(w,rng),new Ccq());
            Assert.IsFalse(port.ObserveOriginalCcq("failed",10,true,true).Authorized);
            w.Approved=true;
            Assert.IsFalse(port.ObserveOriginalCcq("failed",10,true,true).Authorized);
            Assert.AreEqual(1,w.Casts);
            Assert.AreEqual(0,w.Payments);
            Assert.AreEqual(0,rng.Calls);
        }
    }
}
