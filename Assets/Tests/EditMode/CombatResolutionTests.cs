using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public class CombatResolutionTests
    {
        // 아래 체력·피해·쿨타임은 엔진 테스트용 가상 값이다.
        [Test]
        public void AttackDamagesAndDefeatUpdatesSessionOnce()
        {
            var session = new GameSession();
            session.Start();
            session.BeginWave(1);
            var scheduler = new CombatScheduler();
            scheduler.Register("test-attacker", 0);
            var combat = new CombatResolution(session, scheduler);
            combat.Spawn(new CombatEnemy("test-enemy", 100));
            long dealt;
            Assert.IsTrue(combat.TryAttack("test-attacker", "test-enemy", 1, 40, out dealt));
            Assert.AreEqual(40, dealt);
            Assert.AreEqual(60, combat.GetEnemy("test-enemy").Health);
            Assert.IsFalse(combat.TryAttack("test-attacker", "test-enemy", 1, 60, out dealt));
            scheduler.Advance(1);
            Assert.IsTrue(combat.TryAttack("test-attacker", "test-enemy", 1, 100, out dealt));
            Assert.AreEqual(60, dealt);
            Assert.AreEqual(0, session.AliveEnemies);
            Assert.AreEqual(1, session.DefeatedEnemies);
            Assert.IsFalse(combat.TryLeak("test-enemy"));
        }

        [Test]
        public void LeakAndDefeatAreMutuallyExclusive()
        {
            var session = new GameSession();
            session.Start();
            session.BeginWave(1);
            var scheduler = new CombatScheduler();
            var combat = new CombatResolution(session, scheduler);
            combat.Spawn(new CombatEnemy("test-enemy", 5));
            Assert.IsTrue(combat.TryLeak("test-enemy"));
            Assert.AreEqual(1, session.LeakedEnemies);
            Assert.AreEqual(0, session.DefeatedEnemies);
            Assert.IsFalse(combat.TryLeak("test-enemy"));
        }

        [Test]
        public void NegativeDamageCannotChangeHealthOrCooldown()
        {
            var session = new GameSession();
            session.Start();
            session.BeginWave(1);
            var scheduler = new CombatScheduler();
            scheduler.Register("test-attacker", 0);
            var combat = new CombatResolution(session, scheduler);
            combat.Spawn(new CombatEnemy("test-enemy", 20));
            long dealt;
            Assert.Throws<ArgumentOutOfRangeException>(() => {
                long ignored;
                combat.TryAttack("test-attacker", "test-enemy", 1, -1, out ignored);
            });
            Assert.AreEqual(20, combat.GetEnemy("test-enemy").Health);
            Assert.IsTrue(scheduler.IsReady("test-attacker"));
        }
    }
}
