using System;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class CombatEnemyResumeSnapshotTests
    {
        private static SourceCombatEnemyCheckpoint Snapshot()
        {
            var status = new CombatStatusEngine();
            status.Apply(new CombatEffect("TEST_ONLY_STUN", CombatEffectType.Stun,
                CombatEffectOrigin.TriggeredSkill, 1, 10));
            status.Advance(2.5);
            return new SourceCombatEnemyCheckpoint {
                EnemyId = "test:enemy:original", Kind = CharacterEnemyKind.Boss,
                MaximumHealth = 500, Health = 370,
                X = 12.5, Y = 44, PathProgress = 0.32,
                ArmorBreakStacks = 2, ProvenA04MStacks = 1,
                ProvenA04PTotals = 3,
                Defenses = SourceCombatDefenseCheckpoint.Capture(
                    EnemyDefenseProfile.UnverifiedKind(CharacterEnemyKind.Boss)),
                Status = status.CaptureState()
            };
        }

        [Test]
        public void AttachesSavedEnemyWithoutDoubleCountingOrHealing()
        {
            var game = new GameSession();
            game.Start(); game.BeginWave(1); game.SpawnEnemy();
            var restoredSession = GameSession.Restore(game.CaptureState());
            var scheduler = new CombatScheduler();
            var resolution = new CombatResolution(restoredSession, scheduler);
            var snapshot = Snapshot();
            var enemy = CombatEnemy.RestoreSnapshot(snapshot);
            resolution.AttachRestoredEnemy(enemy, true);
            Assert.AreEqual(1, restoredSession.AliveEnemies);
            Assert.AreEqual(370L, resolution.GetEnemy(snapshot.EnemyId).Health);
            Assert.IsTrue(enemy.Status.IsStunned());
            Assert.AreEqual(2.5, enemy.Status.CurrentTime);
            Assert.Throws<InvalidOperationException>(() =>
                resolution.AttachRestoredEnemy(enemy, true));
            Assert.AreEqual(1, restoredSession.AliveEnemies);
            long dealt;
            Assert.IsTrue(resolution.TryApplySkillDamage(snapshot.EnemyId, 370, out dealt));
            Assert.AreEqual(370, dealt);
            Assert.AreEqual(0, restoredSession.AliveEnemies);
            Assert.AreEqual(1, restoredSession.DefeatedEnemies);
        }

        [Test]
        public void InvalidSnapshotIsRejectedWithoutTouchingSession()
        {
            var snapshot = Snapshot();
            snapshot.NativePassiveTargetRevision = 1;
            Assert.Throws<FormatException>(() => CombatEnemy.RestoreSnapshot(snapshot));
            snapshot.NativePassiveTargetRevision = 0;
            snapshot.Health = 501;
            Assert.Throws<FormatException>(() => CombatEnemy.RestoreSnapshot(snapshot));
            snapshot.Health = 370;
            snapshot.HasOriginalA04RLevel = true;
            snapshot.OriginalA04RLevel = 2;
            Assert.Throws<FormatException>(() => CombatEnemy.RestoreSnapshot(snapshot));
        }

        [Test]
        public void NativeTargetMaskAndRevisionSurviveSourcePinnedRoundTrip()
        {
            var native = new Original323NativePassiveTargetProfile("h060", true,
                new [] { "A001", "A0YE" },
                Original323UncommonAbilityCatalog.SourceW3xSha256,
                "TEST_ONLY_original_native_target_sample");
            var original = native.CaptureState();
            var restored = Original323NativePassiveTargetProfile.Restore(original);
            Assert.AreEqual("h060", restored.OriginalUnitRawcode);
            Assert.IsTrue(restored.WarcraftHero);
            Assert.IsTrue(restored.Allows("A001"));
            Assert.IsFalse(restored.Allows("A002"));
            original.EligibleAbilities.Add("A001");
            Assert.Throws<FormatException>(() =>
                Original323NativePassiveTargetProfile.Restore(original));
        }
    }
}
