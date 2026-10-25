using System;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    // All values are synthetic fixtures. They DO NOT permit production waves.
    public sealed class VerifiedWaveProgressTests
    {
        private static CharacterCombatRuntime CreateWorld()
        {
            var units = CharacterUnitCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[]}");
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var deferred = new RejectUnverifiedDamageFormula();
            var world = new CharacterCombatRuntime(units, skills,
                deferred, deferred, new SeededNonOriginalProcRandom(222));
            world.Start();
            return world;
        }

        private static VerifiedWaveEnemy Spawn(double at, string id)
        {
            return new VerifiedWaveEnemy(at, id, 100,
                CharacterEnemyKind.Line, 0, 0, 0,
                EnemyDefenseProfile.UnverifiedKind(CharacterEnemyKind.Line),
                "n001");
        }

        private static VerifiedWavePlan Plan(int wave, params VerifiedWaveEnemy[] enemies)
        {
            return new VerifiedWavePlan(wave, enemies, "TEST_ONLY: synthetic wave");
        }

        // Only test code accesses the canonical resolution to submit verified
        // synthetic HP damage; no arbitrary production damage API is added.
        private static void KillFromCanonicalDamage(CharacterCombatRuntime world, string id)
        {
            var field = typeof(CharacterCombatRuntime).GetField(
                "resolution", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            var combat = (CombatResolution)field.GetValue(world);
            long actual;
            Assert.IsTrue(combat.TryApplySkillDamage("TEST_FIXTURE", id, 200,
                out actual));
            Assert.AreEqual(100, actual);
        }

        [Test]
        public void EmptyBoardBeforeAllScheduledSpawnsIsNotCompletion()
        {
            var world = CreateWorld();
            var director = new CombatWaveDirector();
            director.Begin(world, Plan(1, Spawn(2, "late")));
            var before = director.CurrentProgress;
            Assert.AreEqual(1, before.PendingSpawns);
            Assert.AreEqual(0, before.LivingFromWave);
            Assert.IsFalse(before.AllSpawnsIssued);
            Assert.IsFalse(before.AllWaveEnemiesResolved);
            Assert.IsFalse(before.AllWaveEnemiesDefeated);
            director.Step(world, 1);
            Assert.IsFalse(director.CurrentProgress.AllWaveEnemiesResolved);
            director.Step(world, 1);
            Assert.AreEqual(1, director.CurrentProgress.LivingFromWave);
        }

        [Test]
        public void ActualKillClosesOnlyTheOwnedWaveAndUsesSessionAccounting()
        {
            var world = CreateWorld();
            var director = new CombatWaveDirector();
            director.Begin(world, Plan(1, Spawn(0, "first"), Spawn(0, "second")));
            director.Step(world, 0);
            Assert.AreEqual(2, director.CurrentProgress.Spawned);
            KillFromCanonicalDamage(world, "first");
            var progress = director.CurrentProgress;
            Assert.AreEqual(1, progress.Defeated);
            Assert.AreEqual(1, progress.LivingFromWave);
            Assert.IsFalse(progress.AllWaveEnemiesResolved);
            KillFromCanonicalDamage(world, "second");
            Assert.IsTrue(director.CurrentProgress.AllWaveEnemiesResolved);
            Assert.IsTrue(director.CurrentProgress.AllWaveEnemiesDefeated);
            Assert.AreEqual(2, world.Game.DefeatedEnemies);
            Assert.AreEqual(0, world.Game.AliveEnemies);
            Assert.IsFalse(world.LeakEnemy("first"));
            Assert.AreEqual(2, director.CurrentProgress.Defeated);
        }

        [Test]
        public void EscapedMonstersNeverCountAsDefeatedOrWinning()
        {
            var world = CreateWorld();
            var director = new CombatWaveDirector();
            director.Begin(world, Plan(1, Spawn(0, "escape"), Spawn(2, "alive")));
            director.Step(world, 0);
            Assert.IsTrue(world.LeakEnemy("escape"));
            Assert.IsFalse(world.LeakEnemy("escape"));
            Assert.AreEqual(1, director.CurrentProgress.Escaped);
            Assert.IsFalse(director.CurrentProgress.AllWaveEnemiesResolved);
            director.Step(world, 2);
            KillFromCanonicalDamage(world, "alive");
            Assert.IsTrue(director.CurrentProgress.AllWaveEnemiesResolved);
            Assert.IsFalse(director.CurrentProgress.AllWaveEnemiesDefeated);
            Assert.AreEqual(1, director.CurrentProgress.Defeated);
            Assert.AreEqual(1, director.CurrentProgress.Escaped);
            Assert.AreEqual(1, world.Game.LeakedEnemies);
            Assert.AreEqual(1, world.Game.DefeatedEnemies);
            Assert.AreEqual(SessionPhase.Running, world.Game.Phase);
        }

        [Test]
        public void SurvivingPreviousWaveIsAccountedSeparatelyAfterNextWaveStarts()
        {
            var world = CreateWorld();
            var director = new CombatWaveDirector();
            director.Begin(world, Plan(1, Spawn(0, "old")));
            director.Step(world, 0);
            director.Begin(world, Plan(2, Spawn(0, "new")));
            director.Step(world, 0);
            Assert.AreEqual(2, world.Game.AliveEnemies);
            KillFromCanonicalDamage(world, "old");
            VerifiedWaveProgress old, current;
            Assert.IsTrue(director.TryGetWaveProgress(1, out old));
            Assert.IsTrue(director.TryGetWaveProgress(2, out current));
            Assert.IsTrue(old.AllWaveEnemiesDefeated);
            Assert.AreEqual(0, current.Defeated);
            Assert.AreEqual(1, current.LivingFromWave);
            KillFromCanonicalDamage(world, "new");
            Assert.IsTrue(director.CurrentProgress.AllWaveEnemiesDefeated);
            Assert.AreEqual(2, world.Game.CurrentWave);
        }

        [Test]
        public void ReusedInstanceIdAfterExitBelongsOnlyToNewWave()
        {
            var world = CreateWorld();
            var director = new CombatWaveDirector();
            director.Begin(world, Plan(1, Spawn(0, "reused")));
            director.Step(world, 0);
            Assert.IsTrue(world.LeakEnemy("reused"));
            director.Begin(world, Plan(2, Spawn(0, "reused")));
            director.Step(world, 0);
            KillFromCanonicalDamage(world, "reused");
            VerifiedWaveProgress old;
            Assert.IsTrue(director.TryGetWaveProgress(1, out old));
            Assert.AreEqual(1, old.Escaped);
            Assert.AreEqual(0, old.Defeated);
            Assert.AreEqual(1, director.CurrentProgress.Defeated);
        }

        [Test]
        public void NoCrossSessionStepOrDuplicateWaveIsAllowed()
        {
            var world = CreateWorld();
            var director = new CombatWaveDirector();
            director.Begin(world, Plan(1, Spawn(0, "one")));
            Assert.Throws<InvalidOperationException>(() =>
                director.Begin(world, Plan(2, Spawn(0, "two"))));
            director.Step(world, 0);
            Assert.Throws<InvalidOperationException>(() =>
                director.Begin(world, Plan(1, Spawn(0, "again"))));
            var other = CreateWorld();
            Assert.Throws<InvalidOperationException>(() => director.Step(other, 0));
            Assert.Throws<InvalidOperationException>(() =>
                director.Begin(other, Plan(2, Spawn(0, "other"))));
            VerifiedWaveProgress missing;
            Assert.IsFalse(director.TryGetWaveProgress(2, out missing));
            Assert.IsNull(missing);
            Assert.AreEqual(1, world.Game.CurrentWave);
        }

        [Test]
        public void ProgressSnapshotDoesNotChangeWhenLaterKillsOccur()
        {
            var world = CreateWorld();
            var director = new CombatWaveDirector();
            director.Begin(world, Plan(1, Spawn(0, "a")));
            director.Step(world, 0);
            var old = director.CurrentProgress;
            KillFromCanonicalDamage(world, "a");
            Assert.AreEqual(1, old.LivingFromWave);
            Assert.AreEqual(0, old.Defeated);
            Assert.AreEqual(0, director.CurrentProgress.LivingFromWave);
            Assert.IsTrue(director.CurrentProgress.AllWaveEnemiesDefeated);
            Assert.AreEqual(SessionPhase.Running, world.Game.Phase);
        }
    }
}
