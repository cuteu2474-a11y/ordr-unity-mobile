using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323HelperNavigationPotionTests
    {
        // TEST ONLY: pretends the original cast engine has *already* charged
        // a verified cost. The production port must resolve inherited AOsf
        // cost from original Warcraft data; this 10 is NOT a game constant.
        private sealed class TestCommittedCast : IOrdr323VerifiedManaPotionCastPort
        {
            public bool Allow = true;
            public int Calls;
            public int LastLevel;
            public bool TryCommitOriginalA0UYCast(
                Ordr323OriginalHelperCowActor actor, int abilityLevel)
            {
                Calls++;
                LastLevel = abilityLevel;
                if (!Allow || actor.CurrentMana < 10) return false;
                actor.SetMana(actor.CurrentMana - 10);
                return true;
            }
        }

        private static GameSession StartedAt(int round)
        {
            var game = new GameSession();
            game.Start();
            for (int wave = 1; wave <= round; wave++) game.BeginWave(wave);
            return game;
        }

        [Test]
        public void SourceNavigationIsExclusiveUnavailableBeforeRound21()
        {
            var game = StartedAt(20);
            var helper = new Ordr323OriginalHelperCowActor(0);
            var selection = new Ordr323OriginalHelperNavigation(game, helper);
            Assert.IsFalse(selection.CanChooseMaximumOutput);
            Assert.IsFalse(selection.TryChooseMaximumOutput());
            Assert.AreEqual(0, helper.GetAbilityLevel("A0UY"));
            game.BeginWave(21);
            Assert.IsTrue(selection.CanChooseMaximumOutput);
            Assert.IsTrue(selection.TryChooseMaximumOutput());
            Assert.IsFalse(selection.TryChooseMaximumOutput());
            Assert.IsTrue(helper.MaximumOutputNavigationChosen);
            Assert.AreEqual("최고의도움 :: 최대출력", selection.ChosenNavigation);
            Assert.AreEqual(3, helper.ManaPotionChargesRemaining);
            foreach (string rawcode in new[] { "A0BZ", "A0BY", "A07T", "A07X", "A07W" })
                Assert.AreEqual(2, helper.GetAbilityLevel(rawcode), rawcode);
        }

        [Test]
        public void PotionRequiresCommittedOriginalCastAndConsumesExactlyThreeCharges()
        {
            var game = StartedAt(21);
            var cow = new Ordr323OriginalHelperCowActor(0);
            var port = new TestCommittedCast();
            var potion = new Ordr323OriginalHelperManaPotion(game, cow, port);
            Assert.IsFalse(potion.CanAttempt);
            Assert.IsFalse(potion.TryUse());
            Assert.AreEqual(0, port.Calls);
            Assert.IsTrue(new Ordr323OriginalHelperNavigation(game, cow)
                .TryChooseMaximumOutput());
            port.Allow = false;
            Assert.IsFalse(potion.TryUse());
            Assert.AreEqual(15, cow.CurrentMana);
            Assert.AreEqual(3, cow.ManaPotionChargesRemaining);
            port.Allow = true;
            for (int level = 1; level <= 3; level++)
            {
                Assert.IsTrue(potion.TryUse());
                Assert.AreEqual(level, port.LastLevel);
                Assert.AreEqual(3 - level, cow.ManaPotionChargesRemaining);
                Assert.AreEqual(level == 3 ? 0 : level + 1,
                    cow.GetAbilityLevel("A0UY"));
            }
            Assert.AreEqual(885, cow.CurrentMana); // (15 - 10 + 300) x3
            Assert.IsFalse(potion.CanAttempt);
            Assert.IsFalse(potion.TryUse());
            Assert.AreEqual(4, port.Calls); // one rejected + three committed
        }

        [Test]
        public void PotionAndWispRechargeShareRealManaAndCapAtMaximum()
        {
            var game = StartedAt(21);
            var cow = new Ordr323OriginalHelperCowActor(0);
            Assert.IsTrue(new Ordr323OriginalHelperNavigation(game, cow)
                .TryChooseMaximumOutput());
            var port = new TestCommittedCast();
            var potion = new Ordr323OriginalHelperManaPotion(game, cow, port);
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add(WispResourceIds.Random, 1));
            var recharge = new OriginalHelperCowManaRecharge(wallet, game,
                new OriginalHelperCowManaWorld(() => cow));
            Assert.AreEqual(66, recharge.SpendOne().CurrentMana); // 15 + 20 + floor(21*1.5)
            Assert.AreEqual(0, wallet.Balance(WispResourceIds.Random));
            Assert.IsTrue(potion.TryUse());
            Assert.AreEqual(356, cow.CurrentMana);
            cow.SetMana(cow.MaximumMana);
            Assert.IsTrue(potion.TryUse());
            Assert.AreEqual(cow.MaximumMana, cow.CurrentMana);
            Assert.AreEqual(1, cow.ManaPotionChargesRemaining);
        }

        [Test]
        public void NoPortOrInvalidGameSessionCannotFakePotionCast()
        {
            var game = StartedAt(21);
            var cow = new Ordr323OriginalHelperCowActor(0);
            Assert.Throws<ArgumentNullException>(() =>
                new Ordr323OriginalHelperManaPotion(game, cow, null));
            var unstarted = new GameSession();
            var navigation = new Ordr323OriginalHelperNavigation(unstarted, cow);
            Assert.IsFalse(navigation.TryChooseMaximumOutput());
            var port = new TestCommittedCast();
            var potion = new Ordr323OriginalHelperManaPotion(unstarted, cow, port);
            Assert.IsFalse(potion.TryUse());
            Assert.AreEqual(0, port.Calls);
        }
    }
}
