using System;
using NUnit.Framework;
using Ordr.Mobile.Core;
namespace Ordr.Mobile.Tests
{
    public sealed class SharedEconomySessionTests
    {
        // All amounts and units in this class are TEST_ONLY, not original.
        private static VerifiedEconomyStartProfile Plan(bool verified = true,
            bool two = false)
        {
            return new VerifiedEconomyStartProfile("ORDR_S2_2.323[C]",
                GameSession.ModeId, "TEST_ONLY", verified,
                new ResourceBundle().Add("gold", 17).Add("wood", 3),
                two ? new[] { new VerifiedStartingUnit("C-01",1,2),
                    new VerifiedStartingUnit("C-02",3,4) } :
                    new[] { new VerifiedStartingUnit("C-01",1,2) });
        }
        [Test] public void SharesWalletAndRosterAndPreventsDuplicateStart()
        {
            var w = new EconomyWallet(); var r = new UnitRoster();
            var s = new SharedEconomySession(w,r);
            s.ApplyVerifiedStart(Plan(),r.Add,id => { r.Remove(id); });
            Assert.AreSame(w,s.Wallet); Assert.AreSame(r,s.Units);
            Assert.AreEqual(17,w.Balance("gold")); Assert.AreEqual(1,r.Count);
            Assert.AreEqual("C-01",r.Get(1).UnitId);
            Assert.Throws<InvalidOperationException>(() =>
                s.ApplyVerifiedStart(Plan(),r.Add,id => { r.Remove(id); }));
            Assert.AreEqual(17,w.Balance("gold"));
        }
        [Test] public void UnknownSourceBlocksAnyStartingGrant()
        {
            var w = new EconomyWallet(); var r = new UnitRoster();
            var s = new SharedEconomySession(w,r);
            Assert.Throws<InvalidOperationException>(() =>
                s.ApplyVerifiedStart(Plan(false),r.Add,id => { r.Remove(id); }));
            Assert.AreEqual(0,w.Balance("gold")); Assert.AreEqual(0,r.Count);
        }
        [Test] public void FailedSpawnRollsBackWalletAndAllCreatedUnits()
        {
            var w = new EconomyWallet(); var r = new UnitRoster();
            w.Grant(new ResourceBundle().Add("gold",2));
            var s = new SharedEconomySession(w,r);
            int calls = 0;
            Assert.Throws<InvalidOperationException>(() =>
                s.ApplyVerifiedStart(Plan(two:true),(id,x,y) => {
                    if (++calls == 2) throw new InvalidOperationException("TEST_ONLY");
                    return r.Add(id,x,y);
                }, id => { r.Remove(id); }));
            Assert.AreEqual(2,w.Balance("gold"));
            Assert.AreEqual(0,w.Balance("wood")); Assert.AreEqual(0,r.Count);
            Assert.IsFalse(s.HasStarted);
        }
        [Test] public void SubsequentPurchasesUseSameWalletAndPhysicalUnits()
        {
            var w = new EconomyWallet(); var r = new UnitRoster();
            var s = new SharedEconomySession(w,r);
            s.ApplyVerifiedStart(Plan(),r.Add,id => { r.Remove(id); });
            Assert.IsTrue(s.Wallet.TryExchange(new ResourceBundle().Add("gold",7),
                new ResourceBundle().Add("wood",2)));
            Assert.AreEqual(10,w.Balance("gold")); Assert.AreEqual(5,w.Balance("wood"));
            Assert.IsTrue(s.Units.Remove(1)); Assert.AreEqual(0,r.Count);
        }
    }
}
