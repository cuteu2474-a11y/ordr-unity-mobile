using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Reflection;
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

        private sealed class TestOnlyCreepAuthority : IOriginal323CreepDispatchAuthority
        {
            public long ExcludeOnStageTwo;
            public bool RestrictStageTwo;
            public int Calls;
            public bool TryResolveAttackPlacement(long id,
                Original323CreepEncounter target, out double x, out double y)
            {
                Calls++;
                x = target.X + 1; y = target.Y;
                return !(RestrictStageTwo &&
                    target.OriginalObject.Stage == 2 && id == ExcludeOnStageTwo);
            }
        }

        // Inspects the SAME private lane predicate used by basic attacks,
        // skill targeting and native enemy auras. No damage is fabricated.
        private static bool CanAttackLane(CharacterCombatRuntime combat,
            long ownerId, string enemyId)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var units = (IDictionary)typeof(CharacterCombatRuntime)
                .GetField("characters", flags).GetValue(combat);
            var enemies = (IDictionary)typeof(CharacterCombatRuntime)
                .GetField("enemies", flags).GetValue(combat);
            var fn = typeof(CharacterCombatRuntime).GetMethod(
                "CanAttackInThisLane", flags);
            return (bool)fn.Invoke(combat, new[] { units[ownerId], enemies[enemyId] });
        }

        private static CharacterCombatRuntime EmptyCombat(bool sourceAttacks = false)
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var dice = new MinimumDice();
            var rules = new SourceModelBasicCombatRules(map, dice);
            var world = new CharacterCombatRuntime(units, skills, rules, rules, dice);
            world.Start();
            if (sourceAttacks)
            {
                rules.Enable();
                world.ConfigureSourceModelBasicAttacks(rules);
            }
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
        public void CreepDispatchIsFailClosedUntilARealEligibilityAuthorityIsConnected()
        {
            var combat = EmptyCombat(sourceAttacks: true);
            long actor = combat.SpawnCharacter("C-01", -100, 200);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                var first = creeps.SpawnPracticeStage(1, -100, 200);
                combat.SpawnEnemy("main", 100000, CharacterEnemyKind.Line,
                    -99, 200, 0);
                Assert.IsFalse(combat.CanDispatchToCreep(actor, 1));
                Assert.IsFalse(combat.TryDispatchToCreep(actor, 1));
                Assert.IsNull(combat.CreepTargetOf(actor));
                Assert.IsTrue(CanAttackLane(combat, actor, "main"));
                Assert.IsFalse(CanAttackLane(combat, actor, first.EnemyId));
                Assert.AreEqual(0, combat.CreepDeployedCount);
            }
        }

        [Test]
        public void CreepDispatchPartitionsAllLanesAndRecallPreservesSameRealUnit()
        {
            var combat = EmptyCombat(sourceAttacks: true);
            long creepActor = combat.SpawnCharacter("C-01", -300, 20);
            long mainActor = combat.SpawnCharacter("C-02", -290, 20);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                var first = creeps.SpawnPracticeStage(1, -100, 200);
                combat.SpawnEnemy("main", 100000, CharacterEnemyKind.Line,
                    -99, 200, 0);
                var approved = new TestOnlyCreepAuthority();
                combat.ConfigureVerifiedCreepDispatch(creeps, approved);
                Assert.IsTrue(combat.TryDispatchToCreep(creepActor, 1));
                Assert.IsFalse(combat.TryDispatchToCreep(creepActor, 1));
                Assert.IsFalse(combat.CanDispatchToCreep(creepActor, 4));
                Assert.AreEqual(2, combat.CharacterCount);
                Assert.AreEqual(first.EnemyId, combat.CreepTargetOf(creepActor));
                Assert.IsTrue(combat.IsCreepDeployed(creepActor));
                Assert.IsTrue(combat.IsLockedFromRosterTransactions(creepActor));
                Assert.IsFalse(combat.IsLockedFromOwnedUnitConsumption(creepActor),
                    "Confirmed user sale/fusion may consume but not auto-recall.");
                Assert.IsTrue(CanAttackLane(combat, creepActor, first.EnemyId));
                Assert.IsFalse(CanAttackLane(combat, creepActor, "main"));
                Assert.IsFalse(CanAttackLane(combat, mainActor, first.EnemyId));
                Assert.IsTrue(CanAttackLane(combat, mainActor, "main"));
                Assert.Throws<InvalidOperationException>(() =>
                    combat.MoveCharacter(creepActor, 0, 0));
                Assert.Throws<InvalidOperationException>(() =>
                    combat.SetOriginalPoneglyphChanneling(creepActor, true));
                Assert.AreEqual(1, combat.CreepDeployedCount);

                using (var staged = combat.BeginOriginalUnitRemoval(creepActor)) { }
                Assert.AreEqual(first.EnemyId, combat.CreepTargetOf(creepActor),
                    "Aborted commerce must not recall a deployed unit.");
                Assert.IsTrue(combat.TryRecallFromCreep(creepActor));
                Assert.IsFalse(combat.TryRecallFromCreep(creepActor));
                Assert.IsFalse(combat.IsCreepDeployed(creepActor));
                Assert.AreEqual(-300, combat.Roster.Get(creepActor).X);
                Assert.AreEqual(20, combat.Roster.Get(creepActor).Y);
                Assert.IsTrue(CanAttackLane(combat, creepActor, "main"));
                Assert.IsFalse(CanAttackLane(combat, creepActor, first.EnemyId));
            }
        }

        [Test]
        public void OnlyDefeatedPreviousStageCanAutoTransferToEligibleNextCreep()
        {
            var combat = EmptyCombat();
            long follows = combat.SpawnCharacter("C-01", -300, 20);
            long waits = combat.SpawnCharacter("C-02", -290, 20);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                var first = creeps.SpawnPracticeStage(1, -100, 200);
                var policy = new TestOnlyCreepAuthority {
                    ExcludeOnStageTwo = waits, RestrictStageTwo = true
                };
                combat.ConfigureVerifiedCreepDispatch(creeps, policy);
                Assert.IsTrue(combat.TryDispatchToCreep(follows, 1));
                Assert.IsTrue(combat.TryDispatchToCreep(waits, 1));

                // The stage change is an observed physical defeat in the
                // shared resolution, not an invented stage-clear timer.
                var resolver = (CombatResolution)typeof(CharacterCombatRuntime)
                    .GetField("resolution", BindingFlags.Instance |
                        BindingFlags.NonPublic).GetValue(combat);
                long damage;
                Assert.IsTrue(resolver.TryApplySkillDamage("TEST_ONLY", first.EnemyId,
                    first.OriginalObject.ObjectBaseHitPoints, out damage));
                Assert.AreEqual(Original323CreepEncounterState.Defeated, first.State);
                Assert.IsNull(combat.CreepTargetOf(follows));
                var second = creeps.SpawnPracticeStage(2, 500, 800);
                Assert.AreEqual(second.EnemyId, combat.CreepTargetOf(follows));
                Assert.AreEqual(2, combat.CreepStageOf(follows).Value);
                Assert.IsNull(combat.CreepTargetOf(waits),
                    "A failed next-stage authorization leaves the actor deployed and idle.");
                Assert.AreEqual(1, combat.CreepStageOf(waits).Value);
                Assert.AreEqual(2, combat.CreepDeployedCount);
                Assert.IsFalse(combat.CanDispatchToCreep(waits, 2));
                policy.RestrictStageTwo = false;
                Assert.IsTrue(combat.TryDispatchToCreep(waits, 2));
                Assert.AreEqual(second.EnemyId, combat.CreepTargetOf(waits));

                Assert.IsTrue(resolver.TryApplySkillDamage("TEST_ONLY", second.EnemyId,
                    second.OriginalObject.ObjectBaseHitPoints, out damage));
                var third = creeps.SpawnPracticeStage(3, 500, 800);
                Assert.AreEqual(third.EnemyId, combat.CreepTargetOf(follows));
                Assert.AreEqual(third.EnemyId, combat.CreepTargetOf(waits));
                Assert.IsTrue(resolver.TryApplySkillDamage("TEST_ONLY", third.EnemyId,
                    third.OriginalObject.ObjectBaseHitPoints, out damage));
                Assert.IsNull(combat.CreepTargetOf(follows));
                Assert.IsNull(combat.CreepTargetOf(waits));
                Assert.AreEqual(2, combat.CreepDeployedCount,
                    "Defeating Shu Shu must not automatically recall anyone.");
                Assert.IsTrue(combat.TryRecallFromCreep(follows));
                Assert.IsTrue(combat.TryRecallFromCreep(waits));
                Assert.AreEqual(0, combat.CreepDeployedCount);
                Assert.AreEqual(0, combat.Game.DefeatedEnemies,
                    "Creep kills must not count as standard wave kills.");
            }
        }

        [Test]
        public void CommittedConsumptionForgetsCreepAssignmentWithoutLeakingToMain()
        {
            var combat = EmptyCombat();
            long id = combat.SpawnCharacter("C-01", 11, 22);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                var first = creeps.SpawnPracticeStage(1, -100, 200);
                combat.ConfigureVerifiedCreepDispatch(creeps,
                    new TestOnlyCreepAuthority());
                Assert.IsTrue(combat.TryDispatchToCreep(id, 1));
                using (var removal = combat.BeginOriginalUnitRemoval(id))
                {
                    Assert.IsTrue(combat.IsCreepDeployed(id));
                    Assert.AreEqual(first.EnemyId, combat.CreepTargetOf(id));
                    removal.Commit();
                }
                Assert.IsFalse(combat.IsCreepDeployed(id));
                Assert.IsNull(combat.CreepTargetOf(id));
                Assert.AreEqual(0, combat.CharacterCount);
                Assert.AreEqual(0, combat.CreepDeployedCount);
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
