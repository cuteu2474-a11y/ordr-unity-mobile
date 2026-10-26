using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    // All starting seeds and purchase events here are synthetic test fixtures.
    public sealed class Original323MoneyGamblingAndScriptProcTests
    {
        private sealed class Draws : IOriginal323ScriptRandomDrawWitness
        {
            public bool Approved = true;
            public int Calls;
            public bool TryConfirmOriginalDraw(long ordinal,
                Original323ScriptRandomPurpose purpose, int previousState,
                int nextState, int minimum, int maximum,
                int originalResult, out string evidence)
            {
                evidence = "verified original draw event at this ordinal";
                Calls++;
                return Approved;
            }
        }

        private sealed class Purchase : IOriginal323MoneyGamblingPurchaseWitness
        {
            public bool Approved = true;
            public int Player = 0;
            public int First = 33;
            public int Second = 30;
            public int Gold = 30;
            public int Calls;
            public string Evidence = "observed original purchased shop item";
            public bool TryConfirmOriginalPurchase(string ticket,
                Original323MoneyGamblingTier tier, out int player,
                out int rollA, out int rollB, out int paidGold,
                out string proof)
            {
                Calls++;
                player = Player;
                rollA = First; rollB = Second;
                paidGold = Gold; proof = Evidence;
                return Approved && ticket == "purchased:1";
            }
        }

        private sealed class Settlement : IOriginal323MoneyGamblingSettlement
        {
            public int Calls;
            public int Balance;
            public bool Accept = true;
            public bool TrySettleVerifiedPurchaseOnce(string id,
                Original323MoneyGamblingTier tier, int player,
                int amount, string evidence)
            {
                Calls++;
                if (!Accept || id != "purchased:1") return false;
                Balance += amount;
                return true;
            }
        }

        private sealed class AttackEvent : IOriginal323ScriptAttackProcWitness
        {
            public bool Approved = true;
            public string Evidence = "original attacker event in Bnm callback";
            public int Calls;
            public bool TryConfirmOriginalAttackEvent(string function,
                string attacker, string target, long ordinal,
                out string proof)
            {
                Calls++;
                proof = Evidence;
                return Approved && attacker == "attacker" &&
                    target == "enemy" && ordinal > 0;
            }
        }

        private static Original323SharedBfiRandom Shared(int seed, Draws witness)
        {
            return new Original323SharedBfiRandom(seed,
                "observed original initial native seed", witness);
        }

        [Test]
        public void OriginalMoneyGamblingBranchesAndReturnRanges()
        {
            bool high; int payout;
            Assert.IsTrue(Original323MoneyGamblingRules.TryEvaluate(
                Original323MoneyGamblingTier.Regular, 25, 5, out high, out payout));
            Assert.IsFalse(high);
            Assert.AreEqual(5, payout);
            Assert.IsTrue(Original323MoneyGamblingRules.TryEvaluate(
                Original323MoneyGamblingTier.Regular, 26, 10, out high, out payout));
            Assert.IsTrue(high);
            Assert.AreEqual(10, payout);
            Assert.IsTrue(Original323MoneyGamblingRules.TryEvaluate(
                Original323MoneyGamblingTier.HighGrade, 30, 400, out high, out payout));
            Assert.IsFalse(high);
            Assert.IsTrue(Original323MoneyGamblingRules.TryEvaluate(
                Original323MoneyGamblingTier.HighGrade, 31, 4500, out high, out payout));
            Assert.IsTrue(high);
            Assert.AreEqual(4500, payout);
            Assert.IsFalse(Original323MoneyGamblingRules.TryEvaluate(
                Original323MoneyGamblingTier.HighGrade, 31, 401, out high, out payout));
            Assert.IsFalse(Original323MoneyGamblingRules.TryEvaluate(
                (Original323MoneyGamblingTier)100, 40, 300, out high, out payout));
        }

        [Test]
        public void PurchasedRegularSuccessConsumesExactlyTwoOriginalBfiCalls()
        {
            var draws = new Draws();
            var shared = Shared(10000, draws);
            var purchase = new Purchase();
            var settlement = new Settlement();
            var service = new Original323MoneyGamblingService(
                shared, purchase, settlement);
            Original323MoneyGamblingOutcome outcome;
            Assert.IsTrue(service.TrySettle("purchased:1",
                Original323MoneyGamblingTier.Regular, out outcome));
            Assert.IsTrue(outcome.HighPayout);
            Assert.AreEqual(30, outcome.GoldReturned);
            Assert.AreEqual(30, settlement.Balance);
            Assert.AreEqual(2, shared.DrawCount);
            Assert.AreEqual(2, draws.Calls);
            Assert.IsFalse(service.TrySettle("purchased:1",
                Original323MoneyGamblingTier.Regular, out outcome));
            Assert.AreEqual(2, shared.DrawCount);
            Assert.AreEqual(1, purchase.Calls);
            Assert.AreEqual(1, settlement.Calls);
        }

        [Test]
        public void AdvancedFailedBranchReturnsOriginalThreeHundredToFourHundred()
        {
            var shared = Shared(3, new Draws());
            var purchase = new Purchase { First = 1, Second = 367, Gold = 367 };
            var settlement = new Settlement();
            var service = new Original323MoneyGamblingService(
                shared, purchase, settlement);
            Original323MoneyGamblingOutcome outcome;
            Assert.IsTrue(service.TrySettle("purchased:1",
                Original323MoneyGamblingTier.HighGrade, out outcome));
            Assert.IsFalse(outcome.HighPayout);
            Assert.AreEqual(367, outcome.GoldReturned);
            Assert.AreEqual(2, shared.DrawCount);
        }

        [Test]
        public void InvalidOriginalGambleReceiptDoesNotConsumeSharedRandom()
        {
            var shared = Shared(10000, new Draws());
            var purchase = new Purchase { Gold = 31 };
            var service = new Original323MoneyGamblingService(shared,
                purchase, new Settlement());
            Original323MoneyGamblingOutcome outcome;
            Assert.IsFalse(service.TrySettle("purchased:1",
                Original323MoneyGamblingTier.Regular, out outcome));
            Assert.AreEqual(0, shared.DrawCount);
            purchase.Gold = 30; purchase.Second = 31;
            Assert.IsFalse(service.TrySettle("purchased:1",
                Original323MoneyGamblingTier.Regular, out outcome));
            Assert.AreEqual(0, shared.DrawCount);
            purchase.Second = 30; purchase.Player = 4;
            Assert.IsFalse(service.TrySettle("purchased:1",
                Original323MoneyGamblingTier.Regular, out outcome));
            purchase.Player = 0; purchase.Evidence = "MODEL_ONLY: invented";
            Assert.IsFalse(service.TrySettle("purchased:1",
                Original323MoneyGamblingTier.Regular, out outcome));
            Assert.AreEqual(0, shared.DrawCount);
        }

        [Test]
        public void FailedNativePurchaseSettlementNeverRerollsSameReceipt()
        {
            var shared = Shared(10000, new Draws());
            var purchase = new Purchase();
            var sink = new Settlement { Accept = false };
            var service = new Original323MoneyGamblingService(
                shared, purchase, sink);
            Original323MoneyGamblingOutcome outcome;
            Assert.IsFalse(service.TrySettle("purchased:1",
                Original323MoneyGamblingTier.Regular, out outcome));
            Assert.AreEqual(2, shared.DrawCount);
            sink.Accept = true;
            Assert.IsTrue(service.TrySettle("purchased:1",
                Original323MoneyGamblingTier.Regular, out outcome));
            Assert.AreEqual(2, shared.DrawCount);
            Assert.AreEqual(1, purchase.Calls);
            Assert.AreEqual(2, sink.Calls);
            Assert.AreEqual(30, sink.Balance);
        }

        [Test]
        public void ScriptAttackEventConsumesOneBfiEvenWhenNoSkillProc()
        {
            var shared = Shared(3, new Draws());
            var attack = new AttackEvent();
            var service = new Original323ScriptAttackProcRolls(shared, attack);
            bool triggered;
            Assert.IsTrue(service.TryObserveAndRoll("Bnm", "attacker", "enemy",
                1, out triggered));
            Assert.IsTrue(triggered); // seed 3: first BFi(1,100)=1
            Assert.IsFalse(service.TryObserveAndRoll("Bnm", "attacker", "enemy",
                1, out triggered));
            Assert.IsTrue(service.TryObserveAndRoll("Bnp", "attacker", "enemy",
                2, out triggered));
            Assert.IsFalse(triggered); // second BFi(1,100)=67
            Assert.AreEqual(2, shared.DrawCount);
        }

        [Test]
        public void NativeProcAndUnverifiedSkillEventRemainSeparate()
        {
            var shared = Shared(3, new Draws());
            var witness = new AttackEvent { Approved = false };
            var service = new Original323ScriptAttackProcRolls(shared, witness);
            bool triggered;
            Assert.IsFalse(service.TryObserveAndRoll("A07I:critical",
                "attacker", "enemy", 1, out triggered));
            Assert.IsFalse(service.TryObserveAndRoll("Bnr",
                "attacker", "enemy", 1, out triggered));
            witness.Approved = true;
            witness.Evidence = "TEST_ONLY";
            Assert.IsFalse(service.TryObserveAndRoll("Bnr",
                "attacker", "enemy", 1, out triggered));
            Assert.AreEqual(0, shared.DrawCount);
            witness.Evidence = "actual original Bnr attacked trigger";
            Assert.IsTrue(service.TryObserveAndRoll("Bnr",
                "attacker", "enemy", 1, out triggered));
            Assert.AreEqual(1, shared.DrawCount);
        }

        [Test]
        public void GamblingAndAttackSkillMaintainSingleGlobalCallOrder()
        {
            var drawWitness = new Draws();
            var shared = Shared(3, drawWitness);
            var proc = new Original323ScriptAttackProcRolls(
                shared, new AttackEvent());
            bool triggered;
            Assert.IsTrue(proc.TryObserveAndRoll("Bnm", "attacker", "enemy",
                1, out triggered));
            // First BFi(1,100) at seed 3 consumed. Next gambling now
            // starts from the SAME stream, not a separate seeded gamble RNG.
            int first, intermediate;
            Assert.IsTrue(shared.TryPreviewNext(1, 100,
                out first, out intermediate));
            Assert.AreEqual(67, first);
            int lower, upper; bool high;
            Assert.IsTrue(Original323MoneyGamblingRules.TryGetAmountBounds(
                Original323MoneyGamblingTier.Regular, first,
                out high, out lower, out upper));
            int second, last;
            Assert.IsTrue(Original323SharedBfiRandom.TryPreviewFromState(
                intermediate, lower, upper, out second, out last));
            var purchase = new Purchase {
                First = first, Second = second, Gold = second
            };
            var sink = new Settlement();
            var gambling = new Original323MoneyGamblingService(
                shared, purchase, sink);
            Original323MoneyGamblingOutcome result;
            Assert.IsTrue(gambling.TrySettle("purchased:1",
                Original323MoneyGamblingTier.Regular, out result));
            Assert.AreEqual(3, shared.DrawCount);
            Assert.AreEqual(second, sink.Balance);
        }
    }
}
