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

        private sealed class TestOnlyConditionalCreepAuthority :
            IOriginal323CreepConditionalDispatchAuthority
        {
            public int DirectStage;
            public int PreparedStage = 1;
            public bool Available = true;
            public bool Began;
            public bool BeganRejected;
            public bool Finished;
            public bool Failed;
            public int BeginCalls;
            public int ObserveCalls;
            public string CatalogWitness = "C-01";
            public bool ForgeWrongCatalogId;
            public bool ForgeWrongEnemyId;
            public bool TryResolveAttackPlacement(long id,
                Original323CreepEncounter target, out double x, out double y)
            {
                x = target.X + 1;
                y = target.Y;
                return Available && target.OriginalObject.Stage == DirectStage;
            }

            public bool TryPreviewPreparation(long id, string catalogId,
                Original323CreepEncounter target,
                out Original323CreepPreparationPlan plan)
            {
                plan = null;
                if (!Available || catalogId != CatalogWitness ||
                    target.OriginalObject.Stage != PreparedStage)
                    return false;
                plan = new Original323CreepPreparationPlan(id,
                    ForgeWrongCatalogId ? "C-02" : catalogId,
                    ForgeWrongEnemyId ? "nonexistent-creep" : target.EnemyId,
                    target.OriginalObject.Stage,
                    "FAKE", Original323CreepPreparationKind.MovementSkill,
                    "source-provided-fixture:complete-cast");
                return true;
            }

            public bool TryBeginPreparation(Original323CreepPreparationPlan plan)
            {
                BeginCalls++;
                if (BeganRejected) return false;
                Began = true;
                return true;
            }

            public Original323CreepPreparationProgress ObservePreparation(
                Original323CreepPreparationPlan plan)
            {
                ObserveCalls++;
                return Failed ? Original323CreepPreparationProgress.Failed :
                    Finished ? Original323CreepPreparationProgress.Completed :
                    Original323CreepPreparationProgress.Pending;
            }
        }

        private sealed class WitnessedBlink : IOriginal323CreepVerifiedBlinkRuntime
        {
            public bool AllowPreview = true;
            public bool AllowIssue = true;
            public bool Finished;
            public bool ThrowOnIssue;
            public bool ThrowOnObserve;
            public double X, Y;
            public int PreviewCalls, IssueCalls, ObserveCalls;
            public string LastRawcode;
            public double LastIssuedX, LastIssuedY;
            public bool TryPreviewOriginalBlink(long id, string raw,
                Original323CreepEncounter target, out double x,
                out double y, out string evidence)
            {
                PreviewCalls++;
                LastRawcode = raw;
                x = X; y = Y;
                evidence = "original-w3x:w3a-confirmed-blink-attack-site";
                return AllowPreview;
            }
            public bool TryIssueOriginalBlink(long id, string raw,
                string enemy, double x, double y, string evidence)
            {
                IssueCalls++;
                LastRawcode = raw;
                LastIssuedX = x; LastIssuedY = y;
                if (ThrowOnIssue)
                    throw new InvalidOperationException("source cast failed");
                return AllowIssue;
            }
            public Original323CreepPreparationProgress ObserveOriginalBlink(
                long id, string raw, string enemy)
            {
                ObserveCalls++;
                if (ThrowOnObserve)
                    throw new InvalidOperationException("native observer unavailable");
                return Finished
                    ? Original323CreepPreparationProgress.Completed
                    : Original323CreepPreparationProgress.Pending;
            }
        }

        private sealed class WitnessedAttackSite :
            IOriginal323CreepVerifiedAttackPosition
        {
            public bool AllowDirect;
            public bool AllowBlink = true;
            public double X, Y;
            public int DirectCalls, BlinkCalls;
            public bool TryConfirmDirectAttackPlacement(long id,
                string catalogId, Original323CreepEncounter target,
                out double x, out double y, out string evidence)
            {
                DirectCalls++;
                x = X; y = Y;
                evidence = "original-w3x:direct-reach-weapon-ground";
                return AllowDirect;
            }
            public bool TryConfirmOriginalBlinkLanding(long id,
                string catalogId, Original323CreepEncounter target,
                double x, double y, out string evidence)
            {
                BlinkCalls++;
                evidence = "original-w3x:ground-attack-after-blink";
                return AllowBlink && x == X && y == Y;
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
        public void OriginalBlinkCatalogContainsExactly21BoundOwnersAndActualW3ARanges()
        {
            Assert.AreEqual(21, Original323CreepBlinkAbilityCatalog.Count);
            Assert.AreEqual(21, Original323CreepBlinkAbilityCatalog.All
                .Select(row => row.CatalogId).Distinct().Count());
            Original323CreepBlinkAbility shortRange, longRange;
            Assert.IsTrue(Original323CreepBlinkAbilityCatalog.TryGet(
                "MY-05", out shortRange));
            Assert.AreEqual("A0W9", shortRange.Rawcode);
            Assert.AreEqual(800, shortRange.MaximumDistance);
            Assert.AreEqual(0.1, shortRange.OriginalCooldownSeconds);
            Assert.IsTrue(Original323CreepBlinkAbilityCatalog.TryGet(
                "L-08", out longRange));
            Assert.AreEqual("A05Q", longRange.Rawcode);
            Assert.AreEqual(99999, longRange.MaximumDistance);
            Assert.AreEqual(15, longRange.OriginalCooldownSeconds);
            Original323CreepBlinkAbility ignored;
            Assert.IsFalse(Original323CreepBlinkAbilityCatalog.TryGet(
                "C-01", out ignored),
                "A ground unit without the source AEbl ability is not a blink candidate.");
        }

        [Test]
        public void BlinkCapabilityAloneCannotSkipNativeCastOrGroundAttackProof()
        {
            var combat = EmptyCombat();
            long id = combat.SpawnCharacter("R-05", 0, 0);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                var target = creeps.SpawnPracticeStage(1, 500, 0);
                var native = new WitnessedBlink { X = 501, Y = 0 };
                var witness = new WitnessedAttackSite { X = 501, Y = 0 };
                var authority = new Original323CreepVerifiedMobilityAuthority(
                    combat, native, witness);
                combat.ConfigureVerifiedCreepDispatch(creeps, authority);
                double x, y;
                Assert.IsFalse(authority.TryResolveAttackPlacement(id, target,
                    out x, out y),
                    "A hypothetical reachable blink site cannot become free relocation.");
                Assert.IsTrue(combat.CanDispatchToCreep(id, 1));
                Assert.AreEqual(0, native.IssueCalls);
                Assert.IsFalse(combat.IsCreepDeployed(id));
                Assert.IsTrue(combat.TryDispatchToCreep(id, 1));
                Assert.IsTrue(combat.IsPreparingCreepDispatch(id));
                Assert.AreEqual(1, native.IssueCalls);
                Assert.AreEqual("A05C", native.LastRawcode);
                Assert.AreEqual(501, native.LastIssuedX);
                Assert.AreEqual(0, native.LastIssuedY);
                Assert.AreEqual(0, combat.CreepDeployedCount);
                combat.AdvancePendingCreepPreparations();
                Assert.AreEqual(0, combat.CreepDeployedCount);
                Assert.IsFalse(combat.TryDispatchToCreep(id, 1));
                // The completed native receipt itself authorizes ONE
                // approved landing without inventing a direct walking path.
                native.Finished = true;
                combat.AdvancePendingCreepPreparations();
                Assert.AreEqual(target.EnemyId, combat.CreepTargetOf(id));
                Assert.AreEqual(1, combat.CreepDeployedCount);
                Assert.AreEqual(1, native.IssueCalls);
                Assert.IsTrue(combat.TryRecallFromCreep(id));
                native.AllowPreview = false;
                Assert.IsFalse(combat.CanDispatchToCreep(id, 1),
                    "The completed ability receipt cannot be reused after recall.");
                Assert.AreEqual(1, native.IssueCalls);
            }
        }

        [Test]
        public void OriginalBlinkMaximumDistanceIsNotUniversalCreepRange()
        {
            var combat = EmptyCombat();
            long shortId = combat.SpawnCharacter("MY-05", -1000, 0);
            long longId = combat.SpawnCharacter("R-05", -1000, 0);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                creeps.SpawnPracticeStage(1, 0, 0);
                var native = new WitnessedBlink { X = 0, Y = 0 };
                var hit = new WitnessedAttackSite { X = 0, Y = 0 };
                combat.ConfigureVerifiedCreepDispatch(creeps,
                    new Original323CreepVerifiedMobilityAuthority(combat, native, hit));
                Assert.IsFalse(combat.CanDispatchToCreep(shortId, 1),
                    "MY-05 original 800 cannot reach 1000 without another proven hop.");
                Assert.IsTrue(combat.CanDispatchToCreep(longId, 1));
                Assert.AreEqual("A05C", native.LastRawcode);
                Assert.AreEqual(0, native.IssueCalls);
                hit.AllowBlink = false;
                Assert.IsFalse(combat.CanDispatchToCreep(longId, 1),
                    "A legal distance without ground-target damage is not valid.");
                Assert.IsFalse(combat.TryDispatchToCreep(longId, 1));
                Assert.AreEqual(0, native.IssueCalls);
                Assert.AreEqual(0, combat.CreepPreparationCount);
            }
        }

        [Test]
        public void NativeObserverErrorReleasesLockWithoutFreeTravelOrSecondCast()
        {
            var combat = EmptyCombat();
            long id = combat.SpawnCharacter("R-05", 0, 0);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                creeps.SpawnPracticeStage(1, 500, 0);
                var native = new WitnessedBlink {
                    X = 501, Y = 0, ThrowOnObserve = true
                };
                var site = new WitnessedAttackSite { X = 501, Y = 0 };
                combat.ConfigureVerifiedCreepDispatch(creeps,
                    new Original323CreepVerifiedMobilityAuthority(combat, native, site));
                int reported = 0;
                combat.CreepPreparationObserverFailed += ex => { reported++; };
                Assert.IsTrue(combat.TryDispatchToCreep(id, 1));
                combat.AdvancePendingCreepPreparations();
                Assert.AreEqual(1, reported);
                Assert.AreEqual(0, combat.CreepPreparationCount);
                Assert.IsFalse(combat.IsCreepDeployed(id));
                Assert.AreEqual(0, combat.Roster.Get(id).X);
                Assert.AreEqual(1, native.IssueCalls);
            }
        }

        [Test]
        public void ConfirmedDirectRouteNeverSpendsBlinkCooldown()
        {
            var combat = EmptyCombat();
            long id = combat.SpawnCharacter("R-05", 0, 0);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                var target = creeps.SpawnPracticeStage(1, 500, 0);
                var native = new WitnessedBlink { X = 501, Y = 0 };
                var site = new WitnessedAttackSite {
                    X = 501, Y = 0, AllowDirect = true
                };
                combat.ConfigureVerifiedCreepDispatch(creeps,
                    new Original323CreepVerifiedMobilityAuthority(combat, native, site));
                Assert.IsTrue(combat.TryDispatchToCreep(id, 1));
                Assert.AreEqual(0, native.IssueCalls);
                Assert.AreEqual(0, combat.CreepPreparationCount);
                Assert.AreEqual(target.EnemyId, combat.CreepTargetOf(id));
            }
        }

        [Test]
        public void NativeCastErrorReleasesCreepReservationForLaterRealRetry()
        {
            var combat = EmptyCombat();
            long id = combat.SpawnCharacter("R-05", 0, 0);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                creeps.SpawnPracticeStage(1, 500, 0);
                var native = new WitnessedBlink {
                    X = 501, Y = 0, ThrowOnIssue = true
                };
                var site = new WitnessedAttackSite { X = 501, Y = 0 };
                combat.ConfigureVerifiedCreepDispatch(creeps,
                    new Original323CreepVerifiedMobilityAuthority(combat, native, site));
                Assert.Throws<InvalidOperationException>(() =>
                    combat.TryDispatchToCreep(id, 1));
                Assert.IsFalse(combat.IsPreparingCreepDispatch(id));
                Assert.IsFalse(combat.IsCreepDeployed(id));
                Assert.AreEqual(0, combat.CreepPreparationCount);
                Assert.AreEqual(0, combat.Roster.Get(id).X);
                native.ThrowOnIssue = false;
                Assert.IsTrue(combat.TryDispatchToCreep(id, 1));
                Assert.AreEqual(2, native.IssueCalls);
                Assert.AreEqual(1, combat.CreepPreparationCount);
            }
        }

        [Test]
        public void ConditionalAuthorizationMustBindExactUnitAndExactLivingEnemy()
        {
            var combat = EmptyCombat();
            long id = combat.SpawnCharacter("C-01", 30, 50);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                creeps.SpawnPracticeStage(1, -100, 200);
                var authority = new TestOnlyConditionalCreepAuthority {
                    ForgeWrongCatalogId = true
                };
                combat.ConfigureVerifiedCreepDispatch(creeps, authority);
                Assert.IsFalse(combat.CanDispatchToCreep(id, 1));
                Assert.IsFalse(combat.TryDispatchToCreep(id, 1));
                authority.ForgeWrongCatalogId = false;
                authority.ForgeWrongEnemyId = true;
                Assert.IsFalse(combat.CanDispatchToCreep(id, 1));
                Assert.IsFalse(combat.TryDispatchToCreep(id, 1));
                Assert.AreEqual(0, authority.BeginCalls);
                Assert.AreEqual(0, combat.CreepPreparationCount);
                Assert.AreEqual(0, combat.CreepDeployedCount);
                Assert.AreEqual(30, combat.Roster.Get(id).X);
            }
        }

        [Test]
        public void ConditionalCreepDispatchStartsExactlyOneOriginalCastAndWaitsForCompletion()
        {
            var combat = EmptyCombat();
            long id = combat.SpawnCharacter("C-01", 30, 50);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                var enemy = creeps.SpawnPracticeStage(1, -100, 200);
                var authority = new TestOnlyConditionalCreepAuthority();
                combat.ConfigureVerifiedCreepDispatch(creeps, authority);
                Assert.IsTrue(combat.CanDispatchToCreep(id, 1));
                Assert.AreEqual(0, authority.BeginCalls,
                    "Read-only candidate rendering must not spend the original ability.");
                Assert.IsTrue(combat.TryDispatchToCreep(id, 1));
                Assert.IsTrue(combat.IsPreparingCreepDispatch(id));
                Assert.IsFalse(combat.IsCreepDeployed(id));
                Assert.AreEqual(1, combat.CreepPreparationCount);
                Assert.AreEqual(1, authority.BeginCalls);
                Assert.AreEqual(30, combat.Roster.Get(id).X);
                Assert.IsFalse(combat.CanDispatchToCreep(id, 1));
                Assert.IsFalse(combat.TryDispatchToCreep(id, 1));
                Assert.IsTrue(combat.IsLockedFromRosterTransactions(id));
                Assert.IsTrue(combat.IsLockedFromOwnedUnitConsumption(id));
                Assert.Throws<InvalidOperationException>(() =>
                    combat.BeginOriginalUnitRemoval(id),
                    "An unfinished movement cast must not be consumed mid-spell.");
                combat.AdvancePendingCreepPreparations();
                Assert.AreEqual(1, authority.ObserveCalls);
                Assert.AreEqual(1, combat.CreepPreparationCount);
                Assert.AreEqual(1, authority.BeginCalls);
                authority.DirectStage = 1;
                authority.Finished = true;
                combat.AdvancePendingCreepPreparations();
                Assert.AreEqual(0, combat.CreepPreparationCount);
                Assert.AreEqual(1, authority.BeginCalls);
                Assert.AreEqual(enemy.EnemyId, combat.CreepTargetOf(id));
                Assert.IsTrue(combat.IsCreepDeployed(id));
                Assert.IsTrue(CanAttackLane(combat, id, enemy.EnemyId));
                Assert.IsFalse(combat.IsLockedFromOwnedUnitConsumption(id));
                Assert.IsTrue(combat.TryRecallFromCreep(id));
                Assert.AreEqual(30, combat.Roster.Get(id).X);
                Assert.AreEqual(50, combat.Roster.Get(id).Y);
            }
        }

        [Test]
        public void InvalidOrInterruptedCreepPreparationCannotGrantMovementOrFreeRefund()
        {
            var combat = EmptyCombat();
            long id = combat.SpawnCharacter("C-01", 30, 50);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                var enemy = creeps.SpawnPracticeStage(1, -100, 200);
                var authority = new TestOnlyConditionalCreepAuthority { BeganRejected = true };
                combat.ConfigureVerifiedCreepDispatch(creeps, authority);
                Assert.IsFalse(combat.TryDispatchToCreep(id, 1));
                Assert.AreEqual(0, combat.CreepPreparationCount);
                Assert.AreEqual(1, authority.BeginCalls);
                authority.BeganRejected = false;
                Assert.IsTrue(combat.TryDispatchToCreep(id, 1));
                authority.Failed = true;
                combat.AdvancePendingCreepPreparations();
                Assert.IsFalse(combat.IsCreepDeployed(id));
                Assert.IsFalse(combat.IsPreparingCreepDispatch(id));
                Assert.AreEqual(30, combat.Roster.Get(id).X);
                Assert.AreEqual(50, combat.Roster.Get(id).Y);
                Assert.IsNull(combat.CreepTargetOf(id));
                // The planner doesn't mint returned mana/points or rewind
                // native cooldown; these remain inside the original bridge.
                Assert.AreEqual(2, authority.BeginCalls);
                Assert.Throws<ArgumentException>(() =>
                    new Original323CreepPreparationPlan(id, "C-01", enemy.EnemyId,
                        1, "FAKE", Original323CreepPreparationKind.MovementSkill,
                        "TEST_ONLY fabricated proof"));
            }
        }

        [Test]
        public void NextStageRetargetStartsOnePreparedMoveAndRequiresLiveReapproval()
        {
            var combat = EmptyCombat();
            long id = combat.SpawnCharacter("C-01", 30, 50);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                var first = creeps.SpawnPracticeStage(1, -100, 200);
                var authority = new TestOnlyConditionalCreepAuthority {
                    DirectStage = 1, PreparedStage = 2
                };
                combat.ConfigureVerifiedCreepDispatch(creeps, authority);
                Assert.IsTrue(combat.TryDispatchToCreep(id, 1));
                Assert.AreEqual(0, authority.BeginCalls);
                var resolver = (CombatResolution)typeof(CharacterCombatRuntime)
                    .GetField("resolution", BindingFlags.Instance |
                        BindingFlags.NonPublic).GetValue(combat);
                long applied;
                Assert.IsTrue(resolver.TryApplySkillDamage("TEST_ONLY",
                    first.EnemyId, first.OriginalObject.ObjectBaseHitPoints,
                    out applied));
                var next = creeps.SpawnPracticeStage(2, 500, 800);
                Assert.AreEqual(1, authority.BeginCalls);
                Assert.IsTrue(combat.IsPreparingCreepDispatch(id));
                Assert.IsNull(combat.CreepTargetOf(id));
                Assert.AreEqual(1, combat.CreepStageOf(id).Value);
                Assert.IsFalse(combat.TryRecallFromCreep(id),
                    "An in-flight form/teleport cannot be bypassed by recall.");
                Assert.IsFalse(combat.TryDispatchToCreep(id, 2));
                authority.Finished = true;
                combat.AdvancePendingCreepPreparations();
                Assert.IsFalse(combat.IsPreparingCreepDispatch(id));
                Assert.AreEqual(1, combat.CreepStageOf(id).Value,
                    "A completed cast is not proof of a reachable landing.");
                Assert.IsNull(combat.CreepTargetOf(id));
                // The actual original route must now approve the NEW attack
                // placement; no fabricated teleport or retroactive refund.
                authority.DirectStage = 2;
                Assert.IsTrue(combat.TryDispatchToCreep(id, 2));
                Assert.AreEqual(next.EnemyId, combat.CreepTargetOf(id));
                Assert.AreEqual(1, authority.BeginCalls);
                Assert.IsTrue(combat.TryRecallFromCreep(id));
            }
        }

        [Test]
        public void DeferredNextStageEligibilityRetriesAfterCastOrCooldownUnlock()
        {
            var combat = EmptyCombat();
            long id = combat.SpawnCharacter("C-01", 30, 50);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                var first = creeps.SpawnPracticeStage(1, -100, 200);
                var authority = new TestOnlyConditionalCreepAuthority {
                    DirectStage = 1, PreparedStage = 2
                };
                combat.ConfigureVerifiedCreepDispatch(creeps, authority);
                Assert.IsTrue(combat.TryDispatchToCreep(id, 1));
                var resolution = (CombatResolution)typeof(CharacterCombatRuntime)
                    .GetField("resolution", BindingFlags.Instance |
                        BindingFlags.NonPublic).GetValue(combat);
                long damage;
                Assert.IsTrue(resolution.TryApplySkillDamage("TEST_ONLY",
                    first.EnemyId, first.OriginalObject.ObjectBaseHitPoints,
                    out damage));
                authority.Available = false;
                var next = creeps.SpawnPracticeStage(2, 500, 800);
                Assert.AreEqual(0, authority.BeginCalls);
                Assert.IsNull(combat.CreepTargetOf(id));
                authority.Available = true;
                combat.AdvancePendingCreepPreparations();
                Assert.AreEqual(1, authority.BeginCalls,
                    "A source-approved next-stage action starts after the old cast is ready.");
                Assert.IsTrue(combat.IsPreparingCreepDispatch(id));
                authority.Finished = true;
                authority.DirectStage = 2;
                combat.AdvancePendingCreepPreparations();
                combat.AdvancePendingCreepPreparations();
                Assert.AreEqual(next.EnemyId, combat.CreepTargetOf(id));
                Assert.AreEqual(1, authority.BeginCalls);
                Assert.AreEqual(0, combat.CreepPreparationCount);
            }
        }

        [Test]
        public void AutoCastFailureCannotRetryAndRechargeEachFrame()
        {
            var combat = EmptyCombat();
            long id = combat.SpawnCharacter("C-01", 30, 50);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                var first = creeps.SpawnPracticeStage(1, -100, 200);
                var authority = new TestOnlyConditionalCreepAuthority {
                    DirectStage = 1, PreparedStage = 2
                };
                combat.ConfigureVerifiedCreepDispatch(creeps, authority);
                Assert.IsTrue(combat.TryDispatchToCreep(id, 1));
                var resolution = (CombatResolution)typeof(CharacterCombatRuntime)
                    .GetField("resolution", BindingFlags.Instance |
                        BindingFlags.NonPublic).GetValue(combat);
                long damage;
                Assert.IsTrue(resolution.TryApplySkillDamage("TEST_ONLY",
                    first.EnemyId, first.OriginalObject.ObjectBaseHitPoints,
                    out damage));
                creeps.SpawnPracticeStage(2, 500, 800);
                Assert.AreEqual(1, authority.BeginCalls);
                authority.Failed = true;
                combat.AdvancePendingCreepPreparations();
                combat.AdvancePendingCreepPreparations();
                combat.AdvancePendingCreepPreparations();
                Assert.AreEqual(1, authority.BeginCalls);
                Assert.IsFalse(combat.IsPreparingCreepDispatch(id));
                Assert.IsNull(combat.CreepTargetOf(id));
                Assert.IsTrue(combat.TryDispatchToCreep(id, 2),
                    "Only a new manual instruction may retry an original failed cast.");
                Assert.AreEqual(2, authority.BeginCalls);
            }
        }

        [Test]
        public void ConsumingTheActorDuringPendingCastCannotCreatePhantomCreepUnit()
        {
            var combat = EmptyCombat();
            long id = combat.SpawnCharacter("C-01", 30, 50);
            using (var creeps = new Original323CreepPracticeWorld(combat, SourceCatalog()))
            {
                creeps.SpawnPracticeStage(1, -100, 200);
                var authority = new TestOnlyConditionalCreepAuthority();
                combat.ConfigureVerifiedCreepDispatch(creeps, authority);
                Assert.IsTrue(combat.TryDispatchToCreep(id, 1));
                // Death/explicit combat cleanup may retire the actual actor
                // without a sale transaction, including its pending ticket.
                combat.RemoveCharacter(id);
                authority.DirectStage = 1;
                authority.Finished = true;
                combat.AdvancePendingCreepPreparations();
                Assert.IsFalse(combat.IsPreparingCreepDispatch(id));
                Assert.AreEqual(0, combat.CharacterCount);
                Assert.AreEqual(0, combat.CreepDeployedCount);
                Assert.AreEqual(0, combat.CreepPreparationCount);
                Assert.AreEqual(0, authority.ObserveCalls);
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
