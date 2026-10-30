using System;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // Development fixtures exercise wiring; they are NOT original-runtime witnesses.
    public sealed class Original323StoryBuildingPracticeWorldTests
    {
        private sealed class ZeroDice : ICombatProcRandom
        {
            public int Next(int upper) { return 0; }
        }

        private static string ReadData(string name) =>
            File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", name));

        private static Original323StoryBuildingCatalog Catalog() =>
            Original323StoryBuildingCatalog.FromJson(
                ReadData("original-2323C-monster-object-stats.json"));

        private static CharacterCombatRuntime World()
        {
            var units = CharacterUnitCatalog.FromJson(ReadData("units.json"));
            var skills = CharacterSkillCatalog.FromJson(
                ReadData("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                ReadData("original-2323C-map-base-stats.json"), units);
            var random = new ZeroDice();
            var rules = new SourceModelBasicCombatRules(map, random);
            var runtime = new CharacterCombatRuntime(
                units, skills, rules, rules, random);
            runtime.Start();
            runtime.BeginWave(1);
            return runtime;
        }

        private sealed class SourceStage : IOriginal323StoryBuildingSpawnAuthorization
        {
            public Original323StoryBuildingSpawnPlan Plan;
            public bool TryGetAuthorizedSpawn(int stage,
                out Original323StoryBuildingSpawnPlan plan)
            {
                plan = Plan;
                return plan != null && plan.Stage == stage;
            }
        }

        private sealed class SourceSpawnObserver : IOriginal323StoryBuildingSpawnWitness
        {
            public string Evidence = "observed native story building spawn";
            public bool Approved = true;
            public bool TryConfirmOriginalSpawn(Original323StoryBuildingSpawnPlan plan,
                out string sourceEvidence)
            {
                sourceEvidence = Evidence;
                return Approved;
            }
        }

        private sealed class SourceClearObserver : IOriginal323StoryBuildingClearWitness
        {
            public bool TryConfirmSettledOriginalClear(int stage, string rawcode,
                string enemyId, string spawnEventId, string settlementEventId,
                out string sourceEvidence)
            {
                sourceEvidence = "observed native original story settlement";
                return true;
            }
        }

        [Test]
        public void AllFourteenMapToOriginalFortifiedStoryBuildings()
        {
            var catalog = Catalog();
            Assert.AreEqual(14, catalog.Count);
            Assert.AreEqual("n000", catalog.Find(1).Rawcode);
            Assert.AreEqual("1. 로그타운", catalog.Find(1).Name);
            Assert.AreEqual(70200, catalog.Find(1).BaseHitPoints);
            Assert.AreEqual(10, catalog.Find(1).BaseArmor);
            Assert.AreEqual("n00A", catalog.Find(9).Rawcode);
            Assert.AreEqual("n001", catalog.Find(10).Rawcode);
            Assert.AreEqual("n009", catalog.Find(14).Rawcode);
            Assert.AreEqual(288800000, catalog.Find(14).BaseHitPoints);
            Assert.IsNull(catalog.Find(15));
            for (int stage = 1; stage <= 14; stage++)
            {
                var building = catalog.Find(stage);
                Assert.IsNotNull(building);
                Assert.AreEqual("fort", building.ArmorType);
                Assert.IsFalse(building.FinalModeStatsVerified);
            }
        }

        [Test]
        public void StaticStoryEncountersDoNotAlterWaveDeathsOrMovement()
        {
            var combat = World();
            using (var story = new Original323StoryBuildingPracticeWorld(combat, Catalog()))
            {
                var a = story.SpawnPracticeBuilding(1, 35, 70);
                var b = story.SpawnPracticeBuilding(14, -120, 440);
                Assert.AreEqual(CharacterEnemyKind.Story,
                    combat.MobileEnemySnapshot()[0].Kind);
                Assert.AreEqual(0, combat.Game.AliveEnemies);
                Assert.AreEqual(0, combat.Game.DefeatedEnemies);
                Assert.AreEqual(2, combat.EnemyCount);
                Assert.IsFalse(a.SourceVerifiedSpawn);
                Assert.IsFalse(a.RewardAwaitingSourceVerification);
                Assert.AreEqual("fort", combat.EnemyOf(b.EnemyId).Defenses.WarcraftArmorType);
                Assert.Throws<InvalidOperationException>(() =>
                    story.SpawnPracticeBuilding(1, 35, 70));
                Assert.Throws<InvalidOperationException>(() =>
                    combat.TryMoveEnemy(a.EnemyId, 15, 18, 0.3));
                Assert.Throws<InvalidOperationException>(() =>
                    combat.ForceMoveEnemy(a.EnemyId, 15, 18, 0.3));
                Assert.IsTrue(combat.LeakEnemy(a.EnemyId));
                Assert.AreEqual(Original323StoryBuildingState.Removed, a.State);
                Assert.AreEqual(0, combat.Game.LeakedEnemies);
                Assert.AreEqual(0, combat.Game.AliveEnemies);
                Assert.IsFalse(story.TryRecordSettledClear(
                    a.EnemyId, "settled-1", new SourceClearObserver()));
                var another = story.SpawnPracticeBuilding(1, 35, 70);
                Assert.AreNotEqual(a.EnemyId, another.EnemyId);
            }
        }

        [Test]
        public void SourceGateRequiresConsistentStageEventStatsAndIndependentEvidence()
        {
            var combat = World();
            using (var story = new Original323StoryBuildingPracticeWorld(combat, Catalog()))
            {
                var authorization = new SourceStage {
                    Plan = new Original323StoryBuildingSpawnPlan(
                        10, "n001", "source-spawn-10", 115000000L, 188,
                        "fort", 20, 30, null)
                };
                var observer = new SourceSpawnObserver { Evidence = "TEST_ONLY forged" };
                Assert.Throws<InvalidOperationException>(() =>
                    story.SpawnAuthorizedBuilding(10, authorization, observer));
                Assert.AreEqual(0, combat.EnemyCount);
                observer.Evidence = "source observed spawning operation";
                authorization.Plan = new Original323StoryBuildingSpawnPlan(
                    10, "n000", "wrong-stage-identity", 115000000, 188,
                    "fort", 20, 30, null);
                Assert.Throws<InvalidOperationException>(() =>
                    story.SpawnAuthorizedBuilding(10, authorization, observer));
                authorization.Plan = new Original323StoryBuildingSpawnPlan(
                    10, "n001", "source-spawn-10", 115000000, 188,
                    "fort", 20, 30, 10);
                var encounter = story.SpawnAuthorizedBuilding(10, authorization, observer);
                Assert.IsTrue(encounter.SourceVerifiedSpawn);
                Assert.AreEqual("source-spawn-10", encounter.SourceSpawnEventId);
                Assert.AreEqual(10, combat.EnemyOf(encounter.EnemyId)
                    .Defenses.MagicResistancePercent);
                Assert.AreEqual(0, combat.Game.AliveEnemies);
                Assert.IsFalse(story.TryRecordSettledClear(
                    encounter.EnemyId, "clear-10", new SourceClearObserver()));
                Assert.Throws<InvalidOperationException>(() =>
                    story.SpawnAuthorizedBuilding(10, authorization, observer));
                Assert.IsTrue(combat.LeakEnemy(encounter.EnemyId));
                Assert.IsFalse(story.TryRecordSettledClear(
                    encounter.EnemyId, "clear-10", new SourceClearObserver()));
                Assert.Throws<InvalidOperationException>(() =>
                    story.SpawnAuthorizedBuilding(10, authorization, observer));
            }
        }

        [Test]
        public void InvalidSourcePlanIsRejectedBeforeMutatingWorld()
        {
            Assert.Throws<ArgumentException>(() =>
                new Original323StoryBuildingSpawnPlan(0, "n000", "evt", 5,
                    10, "fort", 0, 0, null));
            Assert.Throws<ArgumentException>(() =>
                new Original323StoryBuildingSpawnPlan(1, "n000", "evt", 5,
                    10, "normal", 0, 0, null));
            Assert.Throws<ArgumentException>(() =>
                new Original323StoryBuildingSpawnPlan(1, "n000", "evt", 5,
                    10, "fort", double.NaN, 0, null));
            Assert.Throws<ArgumentException>(() =>
                new Original323StoryBuildingSpawnPlan(1, "n000", "evt", 5,
                    10, "fort", 0, 0, 120));
        }
    }
}
