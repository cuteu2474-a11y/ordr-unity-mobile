using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Core;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323AutomaticRareWispTests
    {
        private sealed class Script : IOriginalWispRandom
        {
            private readonly int roll;
            public int Calls;
            public Script(int fixedRoll) { roll=fixedRoll; }
            public int NextInclusive(int minimum,int maximum)
            {
                Calls++;
                Assert.That(roll,Is.InRange(minimum,maximum));
                return roll;
            }
        }
        private sealed class Native : IOriginal323NativeUnitPoolRandom
        {
            private readonly int index;
            public int Calls;
            public Native(int selectedIndex) { index=selectedIndex; }
            public int NextInclusive(int minimum,int maximum)
            {
                Calls++;
                Assert.That(index,Is.InRange(minimum,maximum));
                return index;
            }
        }
        private sealed class Position : IOriginalWispSpawnPositionPort
        {
            public int Calls;
            public bool TryGetPosition(string id,out double x,out double y)
            {
                Calls++;x=-3776d;y=4480d;
                return id==WispResourceIds.Rare;
            }
        }
        private sealed class Bonus : IOriginalWispBonusAbilityPort
        {
            public int Calls;
            public bool Succeeds=true;
            public bool TryApplyOriginalAbility(long instanceId,string code)
            {
                Calls++;return Succeeds && instanceId>0 && code=="A0YE";
            }
        }
        private static string Source() => File.ReadAllText(Path.Combine(
            Application.dataPath,"..","data","original-2323C-wisp-pools.json"));

        [Test]
        public void NoVerifiedSourceKeepsRareBlocked()
        {
            var basic=new Original323AutomaticUncommonWisps(Source(),new Script(0));
            Assert.IsFalse(basic.IsVerified(WispResourceIds.Rare));
            Assert.IsTrue(basic.IsVerified(WispResourceIds.Uncommon));
            Assert.IsTrue(basic.IsVerified(WispResourceIds.Special));
            Assert.IsFalse(basic.TryGetPosition(WispResourceIds.Rare,out _,out _));
            Assert.IsFalse(basic.TryApplyOriginalAbility(1,"A0YE"));
        }

        [Test]
        public void RareOnePercentGaimonIsEighthSpecialNativePoolChoice()
        {
            var branch=new Script(100);
            var native=new Native(7);
            var pos=new Position();
            var bonus=new Bonus();
            var roller=new Original323AutomaticUncommonWisps(
                Source(),branch,native,pos,bonus);
            Assert.IsTrue(roller.IsVerified(WispResourceIds.Rare));
            var result=roller.Roll(WispResourceIds.Rare);
            Assert.AreEqual("h0BG",result.UnitRawcode);
            Assert.IsNull(result.BonusAbilityRawcode);
            Assert.AreEqual(1,branch.Calls);
            Assert.AreEqual(1,native.Calls);
            Assert.IsTrue(roller.TryGetPosition(WispResourceIds.Rare,out var x,out var y));
            Assert.AreEqual(-3776d,x);
            Assert.AreEqual(4480d,y);
            Assert.AreEqual(1,pos.Calls);
            Assert.AreEqual(0,bonus.Calls);
        }

        [Test]
        public void RareNinetyNinePercentNeedsActualA0yeApplication()
        {
            var roller=new Original323AutomaticUncommonWisps(
                Source(),new Script(99),new Native(0),new Position(),
                new Bonus {Succeeds=false});
            var result=roller.Roll(WispResourceIds.Rare);
            Assert.AreEqual("h09X",result.UnitRawcode);
            Assert.AreEqual("A0YE",result.BonusAbilityRawcode);
            Assert.IsFalse(roller.TryApplyOriginalAbility(33,"A0YE"));
            Assert.IsFalse(roller.TryApplyOriginalAbility(33,"A1AA"));
        }

        [Test]
        public void SourceRngAndNativePoolCannotBeSameProvider()
        {
            var source=Source();var rng=new Native(0);
            Assert.Throws<ArgumentException>(()=>new Original323AutomaticUncommonWisps(
                source,rng,rng,new Position(),new Bonus()));
            Assert.Throws<ArgumentNullException>(()=>new Original323AutomaticUncommonWisps(
                source,new Script(99),new Native(0),new Position(),null));
        }
    }
}
