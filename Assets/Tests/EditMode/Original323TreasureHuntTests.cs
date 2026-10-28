using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323TreasureHuntTests
    {
        private sealed class Rolls : IOriginalWispRandom
        {
            private readonly Queue<int> rolls;
            public readonly List<string> Asked=new List<string>();
            public Rolls(params int[] values){rolls=new Queue<int>(values);}
            public int NextInclusive(int min,int max)
            {
                Asked.Add(min+":"+max);
                if(rolls.Count==0)throw new InvalidOperationException("no roll");
                return rolls.Dequeue();
            }
        }
        private sealed class World : IOriginal323TreasureNativeWorld
        {
            public bool AllowCast=true;
            public bool AllowSettle=true;
            public int Casts;
            public int Payments;
            public readonly List<string> Received=new List<string>();
            public bool TryConfirmOriginalTreasureCast(double x,double y,
                double size,bool collect,int wave,out string evidence)
            {
                Casts++;
                evidence="Warcraft S2 2.323[C] observed BeN native cast";
                return AllowCast;
            }
            public bool TrySettleTreasureOnce(string id,
                Original323TreasureReward value,
                string cast,string spawn)
            {
                Payments++;
                if(!AllowSettle)return false;
                Received.Add(id+":"+value.WispResourceId+":"+
                    value.WispCount+":"+value.BonusBerry);
                return true;
            }
        }
        private static Original323TreasureHunt Session(World world,Rolls rng)
        {
            var treasure=new Original323TreasureHunt(world,rng);
            treasure.RegisterObservedSpawn("extra-1",0,100,200,
                "Warcraft original BFj(x,y) source observation");
            treasure.RegisterObservedSpawn("extra-2",0,250,300,
                "Warcraft original BFj(x,y) source observation");
            return treasure;
        }
        [Test]
        public void SpawnCadenceAndMapBoundsArePinnedToOriginalJass()
        {
            Assert.IsTrue(Original323TreasureRules.IsScheduledSpawnRound(10));
            Assert.IsTrue(Original323TreasureRules.IsScheduledSpawnRound(50));
            Assert.IsFalse(Original323TreasureRules.IsScheduledSpawnRound(51));
            Assert.IsFalse(Original323TreasureRules.IsScheduledSpawnRound(0));
            Assert.AreEqual(850,Original323TreasureRules.SearchHalfExtent(false,false));
            Assert.AreEqual(978,Original323TreasureRules.SearchHalfExtent(true,false));
            Assert.AreEqual(1125,Original323TreasureRules.SearchHalfExtent(true,true));
            var s=Session(new World(),new Rolls());
            Assert.Throws<InvalidOperationException>(()=>
                s.RegisterObservedSpawn("bad",11,100,200,"Warcraft event"));
            Assert.Throws<InvalidOperationException>(()=>
                s.RegisterObservedSpawn("bad",10,-9999,200,"Warcraft event"));
            Assert.Throws<InvalidOperationException>(()=>
                s.RegisterObservedSpawn("extra-1",0,100,200,"Warcraft event"));
        }

        [Test]
        public void SquareChecksEdgesAndDoesNotTreatZeroedChestAsReal()
        {
            Assert.IsTrue(Original323TreasureRules.Contains(850,850,0,0,850));
            Assert.IsFalse(Original323TreasureRules.Contains(851,0,0,0,850));
            Assert.IsFalse(Original323TreasureRules.Contains(0,0,0,0,850));
            Assert.IsFalse(Original323TreasureRules.Contains(0,5,0,0,-1));
        }

        [Test]
        public void BeforeRound50BranchDrawPrecedesQuantityAndBerry()
        {
            var rng=new Rolls(2,1,25);
            var value=Original323TreasureRules.RollOriginalReward(20,
                false,true,false,rng);
            Assert.AreEqual(WispResourceIds.Uncommon,value.WispResourceId);
            Assert.AreEqual(1,value.WispCount);
            Assert.IsTrue(value.BonusBerry);
            CollectionAssert.AreEqual(new[]{"1:2","1:2","1:1000"},rng.Asked);
        }

        [Test]
        public void GaimonOverridesQuantityButConsumesTheOptionalSourceRoll()
        {
            var rng=new Rolls(1,1,26);
            var value=Original323TreasureRules.RollOriginalReward(40,
                true,true,false,rng);
            Assert.AreEqual(WispResourceIds.Choice,value.WispResourceId);
            Assert.AreEqual(2,value.WispCount);
            Assert.IsFalse(value.BonusBerry);
            Assert.AreEqual(3,rng.Asked.Count);
        }

        [Test]
        public void Round50AndLaterOnlyRandomWispWithoutFirstRoll()
        {
            var rng=new Rolls(1,999);
            var value=Original323TreasureRules.RollOriginalReward(50,
                true,true,false,rng);
            Assert.AreEqual(WispResourceIds.Random,value.WispResourceId);
            Assert.AreEqual(2,value.WispCount);
            Assert.IsFalse(value.BonusBerry);
            CollectionAssert.AreEqual(new[]{"1:2","1:1000"},rng.Asked);
        }

        [Test]
        public void RevealDoesNotConsumeTreasureOrRng()
        {
            var rng=new Rolls(1,400);
            var world=new World();
            var s=Session(world,rng);
            var r=s.SearchObserved(10,100,200,false,false,false,false,true);
            Assert.IsTrue(r.WasAuthorized);
            Assert.AreEqual(2,r.Matched);
            Assert.AreEqual(0,r.Collected);
            Assert.AreEqual(0,rng.Asked.Count);
            Assert.AreEqual(2,s.UncollectedCount);
        }

        [Test]
        public void CollectionIsOnceOnlyAndRefusesUnauthorizedCast()
        {
            var rng=new Rolls(1,400,2,400);
            var world=new World { AllowCast=false };
            var s=Session(world,rng);
            var denied=s.SearchObserved(10,100,200,false,false,false,false,false);
            Assert.IsFalse(denied.WasAuthorized);
            Assert.AreEqual(0,rng.Asked.Count);
            world.AllowCast=true;
            var yes=s.SearchObserved(10,100,200,false,false,false,false,false);
            Assert.AreEqual(2,yes.Collected);
            Assert.AreEqual(2,s.CollectedCount);
            Assert.AreEqual(2,world.Payments);
            Assert.AreEqual(0,s.Preview(100,200,850).Count);
            s.SearchObserved(10,100,200,false,false,false,false,false);
            Assert.AreEqual(2,world.Payments);
        }

        [Test]
        public void FailedSettlementPreservesSameRewardAndDoesNotReroll()
        {
            var rng=new Rolls(2,999,1,999);
            var world=new World { AllowSettle=false };
            var s=Session(world,rng);
            var first=s.SearchObserved(10,100,200,false,false,false,false,false);
            Assert.AreEqual(0,first.Collected);
            Assert.AreEqual(2,rng.Asked.Count);
            world.AllowSettle=true;
            // The same pending first chest is settled, then the second chest
            // would need further rolls. Place it outside a narrow search.
            Assert.AreEqual(1,s.Preview(100,200,1).Count);
            var again=s.SearchObserved(10,100,200,false,false,false,false,false);
            Assert.AreEqual(2,again.Collected);
            Assert.AreEqual(4,rng.Asked.Count);
            Assert.AreEqual(2,world.Received.Count);
        }
    }
}
