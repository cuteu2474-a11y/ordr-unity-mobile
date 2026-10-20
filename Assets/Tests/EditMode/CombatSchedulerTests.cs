using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public class CombatSchedulerTests
    {
        [Test]
        public void AttackCooldownUsesExplicitTime()
        {
            var scheduler = new CombatScheduler();
            scheduler.Register("test-unit", 0.5);
            Assert.IsFalse(scheduler.IsReady("test-unit"));
            scheduler.Advance(0.5);
            Assert.IsTrue(scheduler.TryCommitAttack("test-unit", 1.0));
            Assert.IsFalse(scheduler.TryCommitAttack("test-unit", 1.0));
            scheduler.Advance(1.0);
            Assert.IsTrue(scheduler.IsReady("test-unit"));
        }

        [Test]
        public void TargetingFiltersDeadAndOutOfRange()
        {
            var targets = new[] {
                new TargetCandidate("a", 4, 1, true),
                new TargetCandidate("b", 1, 2, false),
                new TargetCandidate("c", 100, 3, true)
            };
            Assert.AreEqual("a", CombatTargeting.Select(targets, 3, TargetPriority.Nearest));
            Assert.IsNull(CombatTargeting.Select(targets, 1, TargetPriority.Nearest));
        }

        [Test]
        public void TargetPriorityMustBeExplicit()
        {
            var targets = new[] {
                new TargetCandidate("a", 4, 0.9, true),
                new TargetCandidate("b", 1, 0.1, true)
            };
            Assert.AreEqual("b", CombatTargeting.Select(targets, 5, TargetPriority.Nearest));
            Assert.AreEqual("a", CombatTargeting.Select(targets, 5, TargetPriority.FurthestAlongPath));
        }

        [Test]
        public void RejectsInvalidAttackDelay()
        {
            var scheduler = new CombatScheduler();
            scheduler.Register("test-unit", 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.TryCommitAttack("test-unit", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Advance(-1));
        }
    }
}
