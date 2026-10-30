using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323CreepPracticeWorldTests
    {
        private sealed class MinimumDice : ICombatProcRandom
        {
            public int Next(int upper) { return 0; }
        }

        private static string Data(string file)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", file));
        }

        private static Original323CreepObjectCatalog SourceCatalog()
        {
            return Original323CreepObjectCatalog.FromJson(
                Data("original-2323C-monster-object-stats.json"));
        }

        private static CharacterCombatRuntime EmptyCombat()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var dice = new MinimumDice();
            var rules = new SourceModelBasicCombatRules(map, dice);
            var world = new CharacterCombatRuntime(units, skills, rules, rules, dice);
            world.Start();
            world.BeginWave(1);
            return world;
        }

        [Test]
        public void ThreeCreepStagesMatchSourceObjectBaseStatsButNotFinalStats()
        {
            var catalog = SourceCatalog();
            Assert.AreEqual(3, catalog.Count);
            Assert.AreEqual("n00E", catalog.Find(1).Rawcode);
            Assert.AreEqual("1단계 크립 듀공", catalog.Find(1).OriginalMapName);
            Assert.AreEqual(360000, catalog.Find(1).ObjectBaseHitPoints);
            Assert.AreEqual(27, catalog.Find(1).ObjectBaseArmor);
            Assert.AreEqual("n00F", catalog.Find(2).Rawcode);
            Assert.AreEqual(1000000, catalog.Find(2).ObjectBaseHitPoints);
            Assert.AreEqual(125, catalog.Find(2).ObjectBaseArmor);
            Assert.AreEqual("n00G", catalog.Find(3).Rawcode);
            Assert.AreEqual(1200000, catalog.Find(3).ObjectBaseHitPoints);
            Assert.AreEqual(150, catalog.Find(3).ObjectBaseArmor);
            Assert.IsNull(catalog.Find(0));
            Assert.IsNull(catalog.Find(4));
            foreach (int stage in new[] { 1, 2, 3 })
            {
                var record = catalog.Find(stage);
                Assert.IsFalse(record.FinalNightmareStatsVerified);
                var defenses = record.CreateObjectBasePracticeDefenses();
                Assert.AreEqual(CharacterEnemyKind.Creep, defenses.Kind);
                Assert.AreEqual(record.ObjectBaseArmor, defenses.BaseArmor);
                Assert.IsNull(defenses.MagicResistancePercent);
                Assert.IsNull(defenses.MagicAbilityLevel);
                StringAssert.Contains("OBJECT BASE", defenses.Source);
            }
        }

        [Test]
        public void SourceTamperingAndFakeFinalApprovalsAreRejected()
        {
            string original = Data("original-2323C-monster-object-stats.json");
            Assert.Throws<FormatException>(() =>
                Original323CreepObjectCatalog.FromJson(original.Replace(
                    "\"safeForVerifiedMonsterWaveRuntime\": false",
                    "\"safeForVerifiedMonsterWaveRuntime\": true")));
            Assert.Throws<FormatException>(() =>
                Original323CreepObjectCatalog.FromJson(original.Replace(
                    "\"nightmareOtherworldFinalStatsVerified\": false",
                    "\"nightmareOtherworldFinalStatsVerified\": true")));
            Assert.Throws<FormatException>(() =>
                Original323CreepObjectCatalog.FromJson(original.Replace(
                    Original323CreepObjectCatalog.OriginalUnitBalanceSha256,
                    new string('0', 64))));
            Assert.Throws<FormatException>(() =>
                Original323CreepObjectCatalog.FromJson(original.Replace(
                    "\"rawcode\": \"n00F\"",
                    "\"rawcode\": \"n999\"")));
        }

        [Test]
        public void CreepPlacementIsExplicitAndDoesNotAffectNormalWaveCounts()
        {
            var combat = EmptyCombat();
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                var first = creeps.SpawnPracticeStage(1, -100, 200);
                var second = creeps.SpawnPracticeStage(2, 500, 800);
                Assert.AreEqual(0, combat.Game.AliveEnemies);
                Assert.AreEqual(0, combat.Game.DefeatedEnemies);
                Assert.AreEqual(2, combat.EnemyCount);
                Assert.AreEqual(2, combat.MobileEnemySnapshot()
                    .Count(x => x.Kind == CharacterEnemyKind.Creep));
                Assert.AreEqual(first.EnemyId, creeps.FindEncounter(first.EnemyId).EnemyId);
                Assert.IsNull(creeps.FindEncounter("unknown"));
                Assert.AreNotEqual(first.EnemyId, second.EnemyId);
                Assert.AreEqual(Original323CreepEncounterState.Alive, first.State);
                Assert.IsFalse(first.RewardAwaitingSourceVerification);
                Assert.Throws<InvalidOperationException>(() =>
                    creeps.SpawnPracticeStage(1, 0, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    creeps.SpawnPracticeStage(4, 0, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    creeps.SpawnPracticeStage(3, double.NaN, 0));

                Assert.IsTrue(combat.LeakEnemy(first.EnemyId));
                Assert.AreEqual(Original323CreepEncounterState.Escaped, first.State);
                Assert.AreEqual(1, creeps.EscapedCount);
                Assert.AreEqual(0, creeps.DefeatedCount);
                Assert.AreEqual(0, combat.Game.LeakedEnemies);
                Assert.AreEqual(0, combat.Game.AliveEnemies);
                Assert.IsFalse(combat.LeakEnemy(first.EnemyId));
                var respawn = creeps.SpawnPracticeStage(1, -100, 200);
                Assert.AreNotEqual(first.EnemyId, respawn.EnemyId);
                Assert.AreEqual(3, creeps.Encounters.Count);
            }
        }

        [Test]
        public void WaveAndCreepDamageAreAccountedSeparatelyInCombatResolution()
        {
            var game = new GameSession();
            game.Start();
            game.BeginWave(1);
            var combat = new CombatResolution(game, new CombatScheduler());
            int defeated = 0, escaped = 0;
            combat.EnemyExited += (id, reason) =>
            {
                if (reason == CombatEnemyExitReason.Defeated) defeated++;
                else escaped++;
            };
            combat.Spawn(new CombatEnemy("wave", 10));
            combat.Spawn(new CombatEnemy("creep-a", 20), countsForWave: false);
            combat.Spawn(new CombatEnemy("creep-b", 30), countsForWave: false);
            Assert.AreEqual(1, game.AliveEnemies);
            long dealt;
            Assert.IsTrue(combat.TryApplySkillDamage("unit", "creep-a", 20, out dealt));
            Assert.AreEqual(20, dealt);
            Assert.AreEqual(1, defeated);
            Assert.AreEqual(1, game.AliveEnemies);
            Assert.AreEqual(0, game.DefeatedEnemies);
            Assert.IsTrue(combat.TryLeak("creep-b"));
            Assert.AreEqual(1, escaped);
            Assert.AreEqual(0, game.LeakedEnemies);
            Assert.IsTrue(combat.TryApplySkillDamage("unit", "wave", 10, out dealt));
            Assert.AreEqual(2, defeated);
            Assert.AreEqual(0, game.AliveEnemies);
            Assert.AreEqual(1, game.DefeatedEnemies);
            Assert.IsFalse(combat.TryLeak("creep-a"));
        }
    }
}
