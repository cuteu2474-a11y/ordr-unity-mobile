using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Unity;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceModelPracticeBattleTests
    {
        private sealed class MinimumDice : ICombatProcRandom
        { public int Next(int upper) { return 0; } }
        private sealed class WinningGold : IOriginalWispRandom
        { public int NextInclusive(int min, int max) { Assert.AreEqual(1, min); Assert.AreEqual(4, max); return 3; } }
        private sealed class Live : IDisposable
        {
            public CharacterCombatRuntime Combat;
            public SharedEconomySession Economy;
            public SourceModelPracticeBattle Battle;
            public void Dispose() { Battle.Dispose(); }
        }
        private static string Data(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", file));
        private static Live Create(int attackers = 0, int lastRound = 10,
            string attackerUnitId = "U-02", bool mobileLane = false)
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(Data("original-2323C-map-base-stats.json"), units);
            var dice = new MinimumDice();
            var rules = new SourceModelBasicCombatRules(map, dice);
            var combat = new CharacterCombatRuntime(units, skills, rules, rules, dice);
            var shared = new SharedEconomySession(new EconomyWallet(), combat.Roster);
            shared.ApplyVerifiedStart(Original323SoloStartProfile.Create(),
                (id, x, y) => shared.Units.Add(id, x, y), id => shared.Units.Remove(id));
            combat.Start();
            rules.Enable();
            combat.ConfigureSourceModelBasicAttacks(rules);
            for (int i = 0; i < attackers; i++) combat.SpawnCharacter(attackerUnitId, -4000 + 20 * i, 4000);
            var identity = Original323MonsterWaveIdentityCatalog.FromJson(Data("original-2323C-monster-wave-script-evidence.json"));
            var battle = new SourceModelPracticeBattle(combat, new CombatWaveDirector(), shared,
                new SourceModelPracticeCatalog(Data(lastRound == 65
                    ? "mobile-standalone-enemy-model.json" : "mobile-practice-battle-model.json"), lastRound), identity,
                rules, new WinningGold(), null, mobileMainLane: mobileLane);
            battle.Start();
            return new Live { Combat = combat, Economy = shared, Battle = battle };
        }

        [Test]
        public void BulletStanceCheckpointPreservesExpiryAndLaneWithoutRecasting()
        {
            // Directly inject the already-activated stance storage to isolate
            // serialization from attack probability and source RNG.
            using (var live = Create(1, attackerUnitId: "I-05"))
            {
                long owner = live.Combat.MobileUnitSnapshot()[0].InstanceId;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var until = (System.Collections.Generic.Dictionary<long, double>)
                    typeof(CharacterCombatRuntime).GetField(
                        "sourceModelBulletRokushikiUntil", flags).GetValue(live.Combat);
                var lanes = (System.Collections.Generic.Dictionary<long, long>)
                    typeof(CharacterCombatRuntime).GetField(
                        "sourceModelBulletRokushikiLane", flags).GetValue(live.Combat);
                var before = live.Combat.CaptureRuntimeDraft();
                long lane = before.Actors[0].SourceLaneRevision;
                double expiry = live.Combat.Time + 2.25;
                until.Add(owner, expiry);
                lanes.Add(owner, lane);
                var draft = live.Combat.CaptureRuntimeDraft();
                Assert.AreEqual(1, draft.BulletStances.Count);
                Assert.AreEqual(expiry, draft.BulletStances[0].ExpiresAt);

                var roster = UnitRoster.Restore(live.Combat.Roster.CaptureState());
                var session = GameSession.Restore(live.Combat.Game.CaptureState());
                var units = CharacterUnitCatalog.FromJson(Data("units.json"));
                var skills = CharacterSkillCatalog.FromJson(
                    Data("character-skill-profiles.json"));
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"), units);
                var dice = new MinimumDice();
                var rules = new SourceModelBasicCombatRules(map, dice);
                rules.Enable();
                var recovered = new CharacterCombatRuntime(units, skills,
                    rules, rules, dice, roster, session);
                recovered.ConfigureSourceModelBasicAttacksForRestore(rules);
                recovered.RestoreEnemyWorldDraft(draft);
                recovered.RestoreActorWorldDraft(draft);
                var again = recovered.CaptureRuntimeDraft();
                Assert.AreEqual(1, again.BulletStances.Count);
                Assert.AreEqual(owner, again.BulletStances[0].OwnerId);
                Assert.AreEqual(expiry, again.BulletStances[0].ExpiresAt);
                Assert.AreEqual(draft.BulletStances[0].LaneRevision,
                    again.BulletStances[0].LaneRevision);
                // An expired stale dictionary entry no longer grants a
                // resumable stance, even before its lazy combat cleanup.
                until[owner] = live.Combat.Time;
                Assert.AreEqual(0, live.Combat.CaptureRuntimeDraft()
                    .BulletStances.Count);
            }
        }

        [Test]
        public void SixtyFiveRoundMobileLayoutOptInKeepsFiniteRangeWithoutChangingFixtures()
        {
            using (var practice = Create(1, 65))
            {
                Assert.IsFalse(practice.Battle.MobileMainLaneEnabled);
                Assert.AreEqual(-4000, practice.Combat.MobileUnitSnapshot()[0].Position.X);
            }
            using (var mobile = Create(1, 65, mobileLane: true))
            {
                Assert.IsTrue(mobile.Battle.MobileMainLaneEnabled);
                var actor = mobile.Combat.MobileUnitSnapshot()[0];
                Assert.Less(actor.Position.X, -4992);
                Assert.Greater(actor.Position.Y, 3456);
                Assert.Less(actor.Position.Y, 5632);
                // A manually spawned line target has no wave/death rewards.
                // The ranged check must use the original source object's
                // positive range; distant enemies cannot be chosen.
                mobile.Combat.SpawnEnemy("test-distant", 10000,
                    CharacterEnemyKind.Line, 0, 0, 0);
                var far = mobile.Combat.InspectBasicAttack(actor.InstanceId);
                Assert.AreEqual(BasicAttackReadinessKind.OutOfRange, far.Kind,
                    "Out-of-range is not an unverified monster target.");
                mobile.Combat.SpawnEnemy("test-adjacent", 10000,
                    CharacterEnemyKind.Line, actor.Position.X + 1, actor.Position.Y, 0);
                var near = mobile.Combat.InspectBasicAttack(actor.InstanceId);
                Assert.AreNotEqual(BasicAttackReadinessKind.UnverifiedTarget, near.Kind);
                Assert.AreNotEqual(BasicAttackReadinessKind.OutOfRange, near.Kind);
            }
        }

        [Test]
        public void MobileFirstFiveCommonAttackersUseCentralRallyAndCanAcquireLaneTarget()
        {
            // Production-style 65R mobile lane, not the permissive legacy
            // practice fixture. Characters are created before lane activation.
            using (var mobile = Create(5, 65, "C-01", mobileLane: true))
            {
                var lane = new SourceModelMainLaneGeometry(mobile.Battle.RouteCorners);
                var actors = mobile.Combat.MobileUnitSnapshot();
                Assert.AreEqual(5, actors.Count);
                foreach (var actor in actors)
                {
                    var expected = lane.UnitPosition(actor.InstanceId);
                    Assert.AreEqual(expected.X, actor.Position.X, 1e-9);
                    Assert.AreEqual(expected.Y, actor.Position.Y, 1e-9);
                    Assert.LessOrEqual(Math.Abs(actor.Position.Y - lane.RallyPoint.Y), 64d);
                }

                mobile.Combat.SpawnEnemy("rally-lane-target", 10000,
                    CharacterEnemyKind.Line, lane.LeftX, lane.RallyPoint.Y, 0);
                foreach (var actor in actors)
                {
                    var readiness = mobile.Combat.InspectBasicAttack(actor.InstanceId);
                    Assert.AreNotEqual(BasicAttackReadinessKind.UnverifiedSource,
                        readiness.Kind);
                    Assert.AreNotEqual(BasicAttackReadinessKind.UnverifiedTarget,
                        readiness.Kind, "A valid first-round lane target must be in source range.");
                }
            }
        }

        [Test]
        public void FullMixedOpeningRosterProducesFirstFourRoundPressureTrace()
        {
            // Regression fixture for the user-reported early loss:
            // Hachi + five named common units in the real finite-range 65R
            // layout, independent of the currently visible roster page.
            // Direct test spawns do not represent paid Wisp recruitment.
            using (var game = Create(0, 65, mobileLane: true))
            {
                string[] lineup = { "U-12", "C-01", "C-02", "C-04", "C-05", "C-08" };
                foreach (string id in lineup) game.Combat.SpawnCharacter(id, 0, 0);
                var atStart = game.Battle.CaptureCombatPressure();
                Assert.AreEqual(lineup.Length, atStart.Units.Count);
                CollectionAssert.AreEquivalent(lineup,
                    atStart.Units.Select(x => x.CatalogId).ToArray());
                int previousWave = game.Combat.Game.CurrentWave;
                long lastObservedDamage = 0;
                for (int tick = 0; tick < 360 &&
                     game.Combat.Game.Phase == SessionPhase.Running; tick++)
                {
                    // The production director itself integrates its 0.1s
                    // spawn/attack/movement slices; no mocked damage.
                    game.Battle.Advance(.5);
                    var pressure = game.Battle.CaptureCombatPressure();
                    Assert.AreEqual(lineup.Length, pressure.Units.Count,
                        "A roster page or wave transition must not hide attackers.");
                    Assert.AreEqual(lineup.Length,
                        pressure.Ready + pressure.Cooldown +
                        pressure.UnverifiedSource + pressure.UnverifiedTarget +
                        pressure.OutOfRange + pressure.FormulaDeferred +
                        pressure.NoBattlefieldTarget);
                    Assert.AreEqual(pressure.ObservedWaveHpDamageThisRound,
                        pressure.OriginWaves.Sum(x => x.AppliedHpDamageThisRound),
                        "HP damage must remain attributed to its spawn wave.");
                    Assert.AreEqual(pressure.ObservedDamageEventsThisRound,
                        pressure.OriginWaves.Sum(x => x.DamageEventsThisRound));
                    Assert.GreaterOrEqual(pressure.ObservedWaveHpDamageSinceAttach,
                        lastObservedDamage, "Damage observation must be monotonic.");
                    lastObservedDamage = pressure.ObservedWaveHpDamageSinceAttach;
                    if (tick % 20 == 0 || pressure.Wave != previousWave)
                        TestContext.WriteLine(
                            "t=" + game.Combat.Time.ToString("F1") +
                            " R=" + pressure.Wave +
                            " alive=" + pressure.AliveEnemies +
                            " kills=" + game.Combat.Game.DefeatedEnemies +
                            " ready=" + pressure.Ready +
                            " cooling=" + pressure.Cooldown +
                            " noTarget=" + pressure.NoBattlefieldTarget +
                            " outOfRange=" + pressure.OutOfRange +
                            " rejected=" + pressure.UnverifiedTarget +
                            " unknown=" + pressure.UnverifiedSource +
                            " formula=" + pressure.FormulaDeferred +
                            " hpDamage=" + pressure.ObservedWaveHpDamageSinceAttach +
                            " thisRoundDamage=" + pressure.ObservedWaveHpDamageThisRound +
                            " hitEvents=" + pressure.ObservedDamageEventsThisRound +
                            " damagingActors=" + pressure.ObservedDamagingActorsThisRound +
                            " actorDamage=" + string.Join(",",
                                pressure.ActorDamageThisRound.Select(x =>
                                    x.ActorId + ":" + x.AppliedHpDamage).ToArray()) +
                            " origin=" + string.Join(",",
                                pressure.OriginWaves.Select(x =>
                                    x.SpawnWave + ":alive=" + x.AliveEnemies +
                                    "/hp=" + x.AppliedHpDamageThisRound +
                                    "/kills=" + x.DefeatedThisRound).ToArray()));
                    previousWave = pressure.Wave;
                    if (pressure.Wave >= 5) break;
                }
                Assert.GreaterOrEqual(game.Combat.Game.CurrentWave, 1);
                Assert.Greater(lastObservedDamage, 0,
                    "Early waves must produce actual HP damage, not merely Ready labels.");
                // The trace must not fabricate a source-parity result from
                // the number of test units or the model-only monster stats.
            }
        }

        [Test]
        public void CombatPressureAttributesOldSurvivorDamageToItsActualSpawnWave()
        {
            // A past-round survivor cannot be counted as a new-round hit.
            // Direct damage in this fixture tests the existing resolution
            // observer and is NOT a shortcut enabled in normal gameplay.
            using (var game = Create(0, 65, mobileLane: true))
            {
                // Stop at the exact 21s + 35s boundary. At 56.1s the
                // first 2R enemy already exists, invalidating a 35-only
                // carry-over fixture before its separate 2R spawn check.
                game.Battle.Advance(56);
                Assert.AreEqual(2, game.Combat.Game.CurrentWave);
                var before = game.Battle.CaptureCombatPressure();
                Assert.AreEqual(35, before.TrackedWaveAliveEnemies);
                Assert.AreEqual(0, before.UntrackedMainLaneEnemies);
                Assert.AreEqual(1, before.OriginWaves.Single().SpawnWave);
                Assert.AreEqual(35, before.OriginWaves.Single().AliveEnemies);
                Assert.AreEqual(0, before.OriginWaves.Single().AppliedHpDamageThisRound);

                long actor = game.Combat.SpawnCharacter("C-01", 0, 0);
                var first = game.Combat.MobileEnemySnapshot()
                    .First(x => x.Kind == CharacterEnemyKind.Line);
                Assert.AreEqual(10, DealModelDamage(game.Combat, actor, first.Id, 10));
                var damaged = game.Battle.CaptureCombatPressure();
                Assert.AreEqual(2, damaged.Wave);
                var old = damaged.OriginWaves.Single(x => x.SpawnWave == 1);
                Assert.AreEqual(10, old.AppliedHpDamageThisRound);
                Assert.AreEqual(1, old.DamageEventsThisRound);
                Assert.AreEqual(0, old.DefeatedThisRound);
                Assert.AreEqual(10,
                    damaged.ActorDamageThisRound.Single().AppliedHpDamage);

                Assert.AreEqual(first.Health - 10,
                    DealModelDamage(game.Combat, actor, first.Id, first.Health - 10));
                var killed = game.Battle.CaptureCombatPressure();
                old = killed.OriginWaves.Single(x => x.SpawnWave == 1);
                Assert.AreEqual(1, old.DefeatedThisRound);
                Assert.AreEqual(34, old.AliveEnemies);
                Assert.AreEqual(first.Health, old.AppliedHpDamageThisRound);
                Assert.AreEqual(0, killed.UntrackedMainLaneEnemies);
                Assert.AreEqual(killed.ObservedWaveHpDamageThisRound,
                    killed.OriginWaves.Sum(x => x.AppliedHpDamageThisRound));

                game.Battle.Advance(.5);
                var mixed = game.Battle.CaptureCombatPressure();
                Assert.AreEqual(34,
                    mixed.OriginWaves.Single(x => x.SpawnWave == 1).AliveEnemies);
                Assert.GreaterOrEqual(
                    mixed.OriginWaves.Single(x => x.SpawnWave == 2).AliveEnemies, 1);
            }
        }

        [Test]
        public void ExpiredTemporaryAttackFormulaBlockDoesNotMisleadHudOrTargetQuery()
        {
            using (var game = Create(0, 65, mobileLane: true))
            {
                long id = game.Combat.SpawnCharacter("C-01", 0, 0);
                var actor = game.Combat.MobileUnitSnapshot().Single();
                game.Combat.SpawnEnemy("diagnostic-reachable", 10000,
                    CharacterEnemyKind.Line, actor.Position.X + 1,
                    actor.Position.Y, 0);
                Assert.AreNotEqual(BasicAttackReadinessKind.UnverifiedSource,
                    game.Combat.InspectBasicAttack(id).Kind);

                var characters = (System.Collections.IDictionary)
                    typeof(CharacterCombatRuntime).GetField(
                        "characters", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(game.Combat);
                object instance = characters[id];
                var type = instance.GetType();
                type.GetField("AttackBlockedByMissingFormula").SetValue(instance, true);
                type.GetField("AttackBlockedStatusSourceId").SetValue(
                    instance, "TEST_ONLY_EXPIRED_FORMULA_STATUS");
                type.GetField("DeferredAttackReason").SetValue(
                    instance, "TEST_ONLY expired temporary effect");
                Assert.AreNotEqual(BasicAttackReadinessKind.FormulaDeferred,
                    game.Combat.InspectBasicAttack(id).Kind);
                Assert.AreEqual("diagnostic-reachable",
                    game.Combat.CurrentAttackTargetOf(id));
                Assert.IsTrue((bool)type.GetField("AttackBlockedByMissingFormula")
                    .GetValue(instance), "Reading the diagnostic must not clear combat state.");

                type.GetField("AttackBlockedStatusSourceId").SetValue(instance, null);
                Assert.AreEqual(BasicAttackReadinessKind.FormulaDeferred,
                    game.Combat.InspectBasicAttack(id).Kind,
                    "A missing formula without an expiring status remains blocked.");
                Assert.IsNull(game.Combat.CurrentAttackTargetOf(id));
            }
        }

        [Test]
        public void DeferredBasicDamageDoesNotSuppressIndependentWeatherPeriodic()
        {
            // TR-02's existing, source-model-only weather packet is periodic
            // and does not require its ordinary basic hit to succeed. Model
            // a deferred BASIC formula without approving any new ability.
            using (var game = Create())
            {
                long nami = game.Combat.SpawnCharacter("TR-02", -4900, 5400);
                var characters = (System.Collections.IDictionary)typeof(CharacterCombatRuntime)
                    .GetField("characters", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(game.Combat);
                object actor = characters[nami];
                Assert.IsNotNull(actor);
                actor.GetType().GetField("AttackBlockedByMissingFormula")
                    .SetValue(actor, true);
                actor.GetType().GetField("DeferredAttackReason")
                    .SetValue(actor, "TEST_ONLY: independent basic formula deferred");
                Assert.AreEqual(BasicAttackReadinessKind.FormulaDeferred,
                    game.Combat.InspectBasicAttack(nami).Kind);

                var before = game.Battle.Advance(3.8);
                Assert.IsFalse(before.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "날씨조작술"),
                    "The independent 3.5-second period must not fire early.");
                Assert.IsFalse(before.Any(e =>
                    e.Type == CharacterCombatEventType.Attack &&
                    e.ActorId == nami.ToString()),
                    "A deferred basic hit remains blocked.");

                var after = game.Battle.Advance(.8);
                Assert.IsTrue(after.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "날씨조작술" && e.ActorId == nami.ToString() &&
                    e.Amount > 0),
                    "The independent periodic should survive a basic formula block.");
                Assert.IsFalse(after.Any(e =>
                    e.Type == CharacterCombatEventType.Attack &&
                    e.ActorId == nami.ToString()));
                Assert.AreEqual(BasicAttackReadinessKind.FormulaDeferred,
                    game.Combat.InspectBasicAttack(nami).Kind);
                Assert.Greater(game.Battle.CaptureCombatPressure()
                    .ObservedWaveHpDamageSinceAttach, 0);
            }
        }

        [Test]
        public void OutOfRangePeriodicDoesNotRegisterOrSpendSlotsWhenOtherUnitsAttack()
        {
            // In the 65R finite-range mode, same-lane enemies can exist
            // while this periodic owner cannot hit any of them. Ordinary
            // attackers must not silently consume that owner's 3.5s timer.
            using (var game = Create(0, 65, mobileLane: true))
            {
                long periodicOwner = game.Combat.SpawnCharacter("TR-02", 0, 0);
                game.Combat.MoveCharacter(periodicOwner, 10000, 10000);
                game.Combat.SpawnCharacter("C-01", 0, 0);
                var events = game.Battle.Advance(26);
                Assert.IsTrue(game.Combat.Game.AliveEnemies > 0);
                Assert.IsFalse(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.ActorId == periodicOwner.ToString() &&
                    e.SkillName == "날씨조작술"));
                var privateField = typeof(CharacterCombatRuntime).GetField(
                    "additionalAutoReadyAt", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(privateField);
                var ready = (System.Collections.IDictionary)privateField.GetValue(game.Combat);
                string key = periodicOwner + ":TR-02:skill:0";
                Assert.IsFalse(ready.Contains(key),
                    "An out-of-range periodic must not schedule or burn its clock.");

                var slot = new SourceModelMainLaneGeometry(game.Battle.RouteCorners)
                    .UnitPosition(periodicOwner);
                game.Combat.MoveCharacter(periodicOwner, slot.X, slot.Y);
                game.Battle.Advance(.1);
                Assert.IsTrue(ready.Contains(key),
                    "The same live owner must start scheduling when a target enters reach.");
                Assert.Greater((double)ready[key], game.Combat.Time);
            }
        }

        [Test]
        public void CombatPressureReadOnlySamplesDoNotAdvanceDamageOrAttackClocks()
        {
            using (var game = Create(5, 65, "C-01", mobileLane: true))
            {
                game.Battle.Advance(26);
                var before = game.Battle.CaptureCombatPressure();
                var time = game.Combat.Time;
                var wallet = game.Economy.Wallet.Balance("gold");
                var again = game.Battle.CaptureCombatPressure();
                Assert.AreEqual(before.ObservedWaveHpDamageSinceAttach,
                    again.ObservedWaveHpDamageSinceAttach);
                Assert.AreEqual(before.ObservedDamageEventsThisRound,
                    again.ObservedDamageEventsThisRound);
                Assert.AreEqual(time, game.Combat.Time);
                Assert.AreEqual(wallet, game.Economy.Wallet.Balance("gold"));
                Assert.AreEqual(before.OutOfRange, again.OutOfRange);
                Assert.AreEqual(before.Ready, again.Ready);
                Assert.AreEqual(before.Cooldown, again.Cooldown);
            }
        }

        [Test]
        public void MobileBossPatrolIsExplicitAndDoesNotChangeLegacyPracticeWavePlans()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var identity = Original323MonsterWaveIdentityCatalog.FromJson(
                Data("original-2323C-monster-wave-script-evidence.json"));
            var model = new SourceModelPracticeCatalog(
                Data("mobile-standalone-enemy-model.json"), 65);
            var legacy = model.BuildPracticePlan(55, identity);
            var mobile = model.BuildPracticePlan(55, identity, mobileBossPatrol: true);
            var bossBefore = legacy.Spawns.Single(x => x.Kind == CharacterEnemyKind.Boss);
            var bossMobile = mobile.Spawns.Single(x => x.Kind == CharacterEnemyKind.Boss);
            Assert.AreEqual(model.Spawn.X, bossBefore.X);
            Assert.AreEqual(model.BossSpawn.X, bossMobile.X);
            Assert.AreEqual(model.BossSpawn.Y, bossMobile.Y);
            Assert.AreEqual(bossBefore.EnemyId, bossMobile.EnemyId);
            Assert.AreEqual(bossBefore.Health, bossMobile.Health);
        }

        [Test]
        public void PracticeAttacksDealDamageAndPayActualKillGoldWithoutRetailApproval()
        {
            using (var game = Create(10))
            {
                var first = game.Combat.MobileUnitSnapshot()[0];
                Assert.IsTrue(game.Combat.UsesSourceModelBasicAttacks);
                Assert.IsFalse(game.Combat.HasVerifiedAttack(first.InstanceId));
                var events = game.Battle.Advance(10);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.Attack));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.EnemyDefeated));
                var actual = game.Battle.CaptureCombatPressure();
                Assert.Greater(actual.ObservedWaveHpDamageSinceAttach, 0);
                Assert.Greater(actual.ObservedDamageEventsThisRound, 0);
                Assert.Greater(actual.ObservedDamagingActorsThisRound, 0);
                Assert.AreEqual(actual.ObservedWaveHpDamageThisRound,
                    actual.OriginWaves.Sum(x => x.AppliedHpDamageThisRound));
                Assert.Greater(actual.OriginWaves.Sum(x => x.DefeatedThisRound), 0);
                Assert.AreEqual(actual.ObservedDamagingActorsThisRound,
                    actual.ActorDamageThisRound.Count);
                Assert.AreEqual(actual.ObservedWaveHpDamageThisRound,
                    actual.ActorDamageThisRound.Sum(x => x.AppliedHpDamage));
                Assert.IsTrue(actual.ObservationIncludesRoundStart);
                Assert.Greater(game.Combat.Game.DefeatedEnemies, 0);
                Assert.Greater(game.Battle.GoldAwarded, 0);
                Assert.AreEqual(60 + game.Battle.GoldAwarded, game.Economy.Wallet.Balance("gold"));
                Assert.AreEqual(0, game.Battle.BossScoreAwarded);
                Assert.AreEqual(0, game.Combat.Game.LeakedEnemies);
            }
        }

        // S-09's Black Arrow uses an effect-only attacker. The permanent
        // U-14 from Absalom's separate original acquisition remains a roster unit.
        private static void TriggerMoriaBlackArrowModel(
            CharacterCombatRuntime world, long moriaId, string victimId)
        {
            const BindingFlags privateField = BindingFlags.Instance | BindingFlags.NonPublic;
            var characters = (System.Collections.IDictionary)typeof(CharacterCombatRuntime)
                .GetField("characters", privateField).GetValue(world);
            var enemies = (System.Collections.IDictionary)typeof(CharacterCombatRuntime)
                .GetField("enemies", privateField).GetValue(world);
            var method = typeof(CharacterCombatRuntime).GetMethod(
                "SpawnSourceModelMoriaZombie", privateField);
            Assert.IsNotNull(method);
            method.Invoke(world, new[] { characters[moriaId], enemies[victimId] });
        }

        private static System.Collections.Generic.List<SourceModelSummonState> MoriaModelEffects(
            CharacterCombatRuntime world)
        {
            var field = typeof(CharacterCombatRuntime).GetField(
                "sourceTemporarySummons", BindingFlags.Instance | BindingFlags.NonPublic);
            return (System.Collections.Generic.List<SourceModelSummonState>)field.GetValue(world);
        }

        [Test]
        public void MoriaKillEffectNeverCreatesARealZombieOrChangesPermanentAbsalomZombie()
        {
            using (var game = Create())
            {
                long moria = game.Combat.SpawnCharacter("S-09", 0, 0);
                long permanent = game.Combat.SpawnCharacter("U-14", 10, 0);
                // This is a controlled test target, not a verified wave spawn.
                // Basic attacks must reject unknown armor rather than invent
                // zero armor; provide an explicit TEST_ONLY defense fixture.
                game.Combat.SpawnEnemyWithDefenses("victim", 1000000000000,
                    CharacterEnemyKind.Line, 10, 10, 0,
                    new EnemyDefenseProfile(CharacterEnemyKind.Line, 0, 0,
                        "medium", "TEST_ONLY_MORIA_BLACK_ARROW_DEFENSE"));
                int before = game.Combat.CharacterCount;

                TriggerMoriaBlackArrowModel(game.Combat, moria, "victim");

                Assert.AreEqual(before, game.Combat.CharacterCount);
                Assert.AreEqual("U-14", game.Combat.Roster.Get(permanent).UnitId);
                Assert.AreEqual(0, game.Combat.PracticeSummonsOf(moria).Count,
                    "The transient Black Arrow is an effect, not a selectable summon.");
                var transient = MoriaModelEffects(game.Combat)
                    .Single(x => x.OwnerId == moria && x.IsVirtualAttackEffect);
                Assert.AreEqual("h00H", transient.SourceUnitRawcode);
                Assert.AreEqual(10, transient.ExpiresAt - game.Combat.Time, 0.00001);
                Assert.IsTrue(transient.HasIndependentCombat);
                double projectileX, projectileY;
                Assert.IsTrue(game.Combat.TryGetSourceMoriaBlackArrowProjectileOrigin(
                    "practice-summon:" + transient.Id,
                    out projectileX, out projectileY));
                Assert.AreEqual(10, projectileX);
                Assert.AreEqual(10, projectileY);
                Assert.IsFalse(game.Combat.TryGetSourceMoriaBlackArrowProjectileOrigin(
                    permanent.ToString(), out projectileX, out projectileY));
                var attacks = game.Combat.Step(5);
                Assert.IsTrue(attacks.Any(x => x.Type == CharacterCombatEventType.Attack &&
                    x.ActorId.StartsWith("practice-summon:", StringComparison.Ordinal)),
                    "The effect must deal ordinary source-backed attack damage.");
                Assert.AreEqual(before, game.Combat.CharacterCount);
                Assert.AreEqual("U-14", game.Combat.Roster.Get(permanent).UnitId);

                game.Combat.Step(6);
                Assert.IsFalse(game.Combat.TryGetSourceMoriaBlackArrowProjectileOrigin(
                    "practice-summon:" + transient.Id,
                    out projectileX, out projectileY));
                Assert.IsFalse(MoriaModelEffects(game.Combat)
                    .Any(x => x.OwnerId == moria && x.IsVirtualAttackEffect));
                Assert.AreEqual(before, game.Combat.CharacterCount);
                Assert.AreEqual("U-14", game.Combat.Roster.Get(permanent).UnitId);
            }
        }

        [Test]
        public void MoriaTransientAttackEndsWhenOwnerIsRemovedOrStartsDecoding()
        {
            using (var game = Create())
            {
                long moria = game.Combat.SpawnCharacter("S-09", 0, 0);
                game.Combat.SpawnEnemy("victim", 1000000000000,
                    CharacterEnemyKind.Line, 10, 10, 0);
                TriggerMoriaBlackArrowModel(game.Combat, moria, "victim");
                Assert.AreEqual(1, MoriaModelEffects(game.Combat)
                    .Count(x => x.IsVirtualAttackEffect));

                game.Combat.SetOriginalPoneglyphChanneling(moria, true);
                Assert.IsFalse(MoriaModelEffects(game.Combat)
                    .Any(x => x.IsVirtualAttackEffect),
                    "A paused/decoding owner must not keep an earlier lane's effect.");
                game.Combat.SetOriginalPoneglyphChanneling(moria, false);
                TriggerMoriaBlackArrowModel(game.Combat, moria, "victim");
                game.Combat.RemoveCharacter(moria);
                Assert.IsFalse(MoriaModelEffects(game.Combat)
                    .Any(x => x.IsVirtualAttackEffect),
                    "Removing the actual owner must cancel the transient combat effect.");
            }
        }

        // No manual Black Arrow mark is required. The shared damage ledger
        // records Moria's positive HP contributions and resolves them once
        // when that SAME line target eventually dies.

        private static long DealModelDamage(
            CharacterCombatRuntime world, long attacker, string victim, long amount)
        {
            var f = typeof(CharacterCombatRuntime).GetField(
                "resolution", BindingFlags.Instance | BindingFlags.NonPublic);
            var resolution = (CombatResolution)f.GetValue(world);
            long actual;
            Assert.IsTrue(resolution.TryApplySkillDamage(
                attacker.ToString(), victim, amount, out actual));
            return actual;
        }

        [Test]
        public void BlackArrowParticipationConvertsOnDifferentUnitKillAfterPositiveDamage()
        {
            using (var game = Create())
            {
                long moria = game.Combat.SpawnCharacter("S-09", 0, 0);
                long permanent = game.Combat.SpawnCharacter("U-14", 10, 0);
                game.Combat.SpawnEnemy("marked", 1000000,
                    CharacterEnemyKind.Line, 10, 10, 0);
                int initialRoster = game.Combat.CharacterCount;
                Assert.AreEqual(10, DealModelDamage(game.Combat, moria, "marked", 10));


                Assert.IsFalse(game.Combat.StatusOf("marked").Snapshot()
                    .Any(x => x.Type == CombatEffectType.SourceBlackArrowDeathMark),
                    "Contribution is recorded on committed HP damage, not a 0.05s mark.");
                game.Combat.Step(.02);
                Assert.AreEqual(999990, DealModelDamage(
                    game.Combat, permanent, "marked", 999990));
                var effect = MoriaModelEffects(game.Combat)
                    .Single(x => x.OwnerId == moria && x.IsVirtualAttackEffect);
                Assert.AreEqual("h00H", effect.SourceUnitRawcode);
                Assert.AreEqual(1, MoriaModelEffects(game.Combat)
                    .Count(x => x.IsVirtualAttackEffect));
                Assert.AreEqual(initialRoster, game.Combat.CharacterCount);
                Assert.AreEqual("U-14", game.Combat.Roster.Get(permanent).UnitId);
                Assert.IsFalse(game.Combat.StatusOf("marked").Snapshot()
                    .Any(x => x.Type == CombatEffectType.SourceBlackArrowDeathMark));
            }
        }

        [Test]
        public void BlackArrowParticipationPersistsAfterNativeMarkWindowAndNeverAddsRosterUnit()
        {
            using (var game = Create())
            {
                long moria = game.Combat.SpawnCharacter("S-09", 0, 0);
                long ally = game.Combat.SpawnCharacter("U-14", 10, 0);
                game.Combat.SpawnEnemy("marked", 1000000,
                    CharacterEnemyKind.Line, 10, 10, 0);
                DealModelDamage(game.Combat, moria, "marked", 1);


                game.Combat.Step(.051);
                Assert.IsFalse(game.Combat.StatusOf("marked").Snapshot()
                    .Any(x => x.Type == CombatEffectType.SourceBlackArrowDeathMark));
                DealModelDamage(game.Combat, ally, "marked", 999999);
                Assert.AreEqual(1, MoriaModelEffects(game.Combat)
                    .Count(x => x.IsVirtualAttackEffect),
                    "Any positive contribution survives the old 0.05s mark window.");
                Assert.AreEqual(2, game.Combat.CharacterCount);
                Assert.AreEqual(0, game.Combat.MoriaZombieMaterials);
            }
        }

        [Test]
        public void BlackArrowContributionIsClearedOnOwnerRemovalAndDecodingLaneChange()
        {
            using (var game = Create())
            {
                long removedMoria = game.Combat.SpawnCharacter("S-09", 0, 0);
                long survivor = game.Combat.SpawnCharacter("U-14", 10, 0);
                game.Combat.SpawnEnemy("removed-owner", 1000,
                    CharacterEnemyKind.Line, 10, 10, 0);
                DealModelDamage(game.Combat, removedMoria, "removed-owner", 1);


                game.Combat.RemoveCharacter(removedMoria);
                Assert.IsFalse(game.Combat.StatusOf("removed-owner").Snapshot()
                    .Any(x => x.Type == CombatEffectType.SourceBlackArrowDeathMark));
                DealModelDamage(game.Combat, survivor, "removed-owner", 999);
                Assert.IsFalse(MoriaModelEffects(game.Combat)
                    .Any(x => x.IsVirtualAttackEffect));

                long decoding = game.Combat.SpawnCharacter("S-09", 0, 0);
                game.Combat.SpawnEnemy("decode-owner", 1000,
                    CharacterEnemyKind.Line, 10, 10, 0);
                DealModelDamage(game.Combat, decoding, "decode-owner", 1);


                game.Combat.SetOriginalPoneglyphChanneling(decoding, true);
                Assert.IsFalse(game.Combat.StatusOf("decode-owner").Snapshot()
                    .Any(x => x.Type == CombatEffectType.SourceBlackArrowDeathMark));
                DealModelDamage(game.Combat, survivor, "decode-owner", 999);
                Assert.IsFalse(MoriaModelEffects(game.Combat)
                    .Any(x => x.IsVirtualAttackEffect));
            }
        }

        [Test]
        public void BlackArrowLethalDamageConvertsOnceAndBossDoesNotContribute()
        {
            using (var game = Create())
            {
                long moria = game.Combat.SpawnCharacter("S-09", 0, 0);
                game.Combat.SpawnEnemy("boss", 1000,
                    CharacterEnemyKind.Boss, 10, 10, 0);
                DealModelDamage(game.Combat, moria, "boss", 1);


                Assert.IsFalse(game.Combat.StatusOf("boss").Snapshot()
                    .Any(x => x.Type == CombatEffectType.SourceBlackArrowDeathMark));
                DealModelDamage(game.Combat, moria, "boss", 999);
                Assert.IsFalse(MoriaModelEffects(game.Combat)
                    .Any(x => x.IsVirtualAttackEffect));

                game.Combat.SpawnEnemy("lethal", 1000,
                    CharacterEnemyKind.Line, 10, 10, 0);
                DealModelDamage(game.Combat, moria, "lethal", 1000);


                Assert.AreEqual(1, MoriaModelEffects(game.Combat)
                    .Count(x => x.IsVirtualAttackEffect));


                Assert.AreEqual(1, MoriaModelEffects(game.Combat)
                    .Count(x => x.IsVirtualAttackEffect));
                Assert.AreEqual(1, game.Combat.CharacterCount);
            }
        }

        [Test]
        public void RemovingObsoleteNativeMarkDoesNotEraseRecordedMoriaContribution()
        {
            using (var game = Create())
            {
                long moria = game.Combat.SpawnCharacter("S-09", 0, 0);
                long ally = game.Combat.SpawnCharacter("U-14", 10, 0);
                game.Combat.SpawnEnemy("dispelled", 1000,
                    CharacterEnemyKind.Line, 10, 10, 0);
                DealModelDamage(game.Combat, moria, "dispelled", 1);


                game.Combat.StatusOf("dispelled").RemoveSource(
                    "practice-native:A087:BNba:" + moria);
                DealModelDamage(game.Combat, ally, "dispelled", 999);
                Assert.AreEqual(1, MoriaModelEffects(game.Combat)
                    .Count(x => x.IsVirtualAttackEffect),
                    "The current contribution rule is independent of a native debuff.");
            }
        }

        [Test]
        public void IndependentCreepsDoNotJoinWavePathOrLineLossCounter()
        {
            using (var game = Create(5))
            using (var creeps = new Original323CreepPracticeWorld(game.Combat,
                Original323CreepObjectCatalog.FromJson(
                    Data("original-2323C-monster-object-stats.json"))))
            {
                var creep = creeps.SpawnPracticeStage(1, -8000, -8000);
                Assert.AreEqual(0, game.Combat.Game.AliveEnemies);
                var before = game.Combat.MobileEnemySnapshot()
                    .Single(x => x.Id == creep.EnemyId);
                Assert.AreEqual(360000, before.Health);
                Assert.DoesNotThrow(() => game.Battle.Advance(2));
                var after = game.Combat.MobileEnemySnapshot()
                    .Single(x => x.Id == creep.EnemyId);
                Assert.AreEqual(before.X, after.X);
                Assert.AreEqual(before.Y, after.Y);
                Assert.AreEqual(360000, after.Health,
                    "Without the user's creep hunter list, even ready attackers must not target creeps.");
                Assert.AreEqual(game.Combat.MobileEnemySnapshot()
                    .Count(x => x.Kind != CharacterEnemyKind.Creep),
                    game.Combat.Game.AliveEnemies);
                Assert.AreEqual(0, creeps.DefeatedCount);
                Assert.AreEqual(0, creeps.EscapedCount);
                Assert.IsTrue(game.Combat.LeakEnemy(creep.EnemyId));
                Assert.AreEqual(0, game.Combat.Game.LeakedEnemies);
                Assert.AreEqual(Original323CreepEncounterState.Escaped, creep.State);
            }
        }

        [Test]
        public void EmptyBoardAdvancesOnSourceTimerAndGrantsTwoWispsExactlyOnce()
        {
            using (var game = Create())
            {
                game.Battle.Advance(35);
                Assert.AreEqual(2, game.Combat.Game.CurrentWave);
                Assert.AreEqual(35, game.Combat.Game.AliveEnemies);
                Assert.AreEqual(7, game.Economy.Wallet.Balance(WispResourceIds.Random));
                Assert.AreEqual(2, game.Battle.RoundWispsAwarded);
                Assert.AreEqual(1, game.Battle.RoundHistory.Count);
                Assert.AreEqual(1, game.Battle.RoundHistory[0].Wave);
                Assert.AreEqual(35, game.Battle.RoundHistory[0].ElapsedSeconds, 1e-6);
                Assert.AreEqual(35, game.Battle.RoundHistory[0].AliveEnemies);
                game.Battle.Advance(0);
                game.Battle.Advance(.1);
                Assert.AreEqual(7, game.Economy.Wallet.Balance(WispResourceIds.Random));
                Assert.AreEqual(0, game.Combat.CharacterCount);
                Assert.AreEqual(SessionPhase.Running, game.Combat.Game.Phase);
            }
        }

        [Test]
        public void SeventyAccumulatedMonstersLoseWithoutInventedEscapesOrRewards()
        {
            using (var game = Create())
            {
                game.Battle.Advance(100);
                Assert.AreEqual(SessionPhase.Lost, game.Combat.Game.Phase);
                Assert.GreaterOrEqual(game.Combat.Game.AliveEnemies, 70,
                    "More waves can spawn during the 20-second grace period.");
                Assert.AreEqual(0, game.Combat.Game.LeakedEnemies);
                Assert.AreEqual(0, game.Combat.Game.DefeatedEnemies);
                Assert.AreEqual(60, game.Economy.Wallet.Balance("gold"));
                StringAssert.Contains("70", game.Battle.ResultMessage);
                long gold = game.Economy.Wallet.Balance("gold");
                game.Battle.Advance(100);
                Assert.AreEqual(gold, game.Economy.Wallet.Balance("gold"));
            }
        }

        [Test]
        public void DeathCountdownWaitsTwentyActualCombatSecondsBeforeLosing()
        {
            using (var game = Create())
            {
                game.Battle.Advance(52.6);
                Assert.AreEqual(70, game.Combat.Game.AliveEnemies);
                Assert.AreEqual(SessionPhase.Running, game.Combat.Game.Phase);
                Assert.IsTrue(game.Battle.IsDeathCountdownActive);
                Assert.Greater(game.Battle.DeathCountdownSecondsRemaining, 19.8);
                game.Battle.Advance(19.0);
                Assert.AreEqual(SessionPhase.Running, game.Combat.Game.Phase);
                Assert.IsTrue(game.Battle.IsDeathCountdownActive);
                game.Battle.Advance(1.2);
                Assert.AreEqual(SessionPhase.Lost, game.Combat.Game.Phase);
                Assert.GreaterOrEqual(game.Combat.Game.AliveEnemies, 70,
                    "More waves can spawn during the 20-second grace period.");
                Assert.AreEqual(0, game.Combat.Game.LeakedEnemies);
                StringAssert.Contains("20초", game.Battle.ResultMessage);
                Assert.AreEqual(0, game.Battle.DeathCountdownSecondsRemaining, 1e-7);
            }
        }

        [Test]
        public void DeathCountdownCancelsBelowLimitAndStartsFreshOnNextBreach()
        {
            using (var game = Create())
            {
                game.Battle.Advance(53);
                Assert.IsTrue(game.Battle.IsDeathCountdownActive);
                var enemy = game.Combat.MobileEnemySnapshot().First();
                Assert.IsTrue(game.Combat.LeakEnemy(enemy.Id));
                game.Battle.Advance(.1);
                Assert.AreEqual(69, game.Combat.Game.AliveEnemies);
                Assert.IsFalse(game.Battle.IsDeathCountdownActive);
                Assert.AreEqual(0d, game.Battle.DeathCountdownSecondsRemaining);
                game.Battle.Advance(17.6); // Third round starts, one more spawn.
                Assert.AreEqual(SessionPhase.Running, game.Combat.Game.Phase);
                Assert.GreaterOrEqual(game.Combat.Game.AliveEnemies, 70);
                Assert.IsTrue(game.Battle.IsDeathCountdownActive);
                Assert.Greater(game.Battle.DeathCountdownSecondsRemaining, 19d);
            }
        }

        [Test]
        public void DeathCountdownObservesBriefBelowLimitReboundWithinOneWaveFrame()
        {
            using (var game = Create())
            {
                game.Battle.Advance(53);
                Assert.AreEqual(70, game.Combat.Game.AliveEnemies);
                double firstStart = game.Battle.CaptureState().DeathCountStartedAt;
                game.Battle.Advance(1.0);
                Assert.Greater(game.Battle.DeathCountdownSecondsRemaining, 18d);
                Assert.Less(game.Battle.DeathCountdownSecondsRemaining, 20d);

                // Both changes occur at one combat timestamp. Frame-only
                // polling would see 70->70 and incorrectly retain the old
                // deadline even though the limit was momentarily broken.
                var victim = game.Combat.MobileEnemySnapshot().First();
                Assert.IsTrue(game.Combat.LeakEnemy(victim.Id));
                Assert.AreEqual(69, game.Combat.Game.AliveEnemies);
                Assert.IsFalse(game.Battle.IsDeathCountdownActive);
                game.Combat.SpawnEnemy("count-rebound-fixture", 100,
                    CharacterEnemyKind.Line, 0, 0, 0);
                Assert.AreEqual(70, game.Combat.Game.AliveEnemies);
                Assert.IsTrue(game.Battle.IsDeathCountdownActive);
                Assert.Greater(game.Battle.CaptureState().DeathCountStartedAt, firstStart);
                Assert.AreEqual(20d, game.Battle.DeathCountdownSecondsRemaining, 1e-7);

                // Only main-lane enemies affect the death count. An
                // independent story building must not restart the clock.
                game.Combat.SpawnEnemy("story-count-fixture", 100,
                    CharacterEnemyKind.Story, 0, 0, 0);
                Assert.AreEqual(70, game.Combat.Game.AliveEnemies);
                Assert.AreEqual(20d, game.Battle.DeathCountdownSecondsRemaining, 1e-7);

                game.Battle.Advance(.1);
                Assert.Greater(game.Battle.DeathCountdownSecondsRemaining, 19.8);
            }
        }

        [Test]
        public void DeathCountdownUsesCombatClockAndPreservesCheckpointState()
        {
            var timer = new MonsterCountDeathCountdown(70);
            Assert.IsFalse(timer.Observe(70, 100));
            Assert.IsTrue(timer.IsActive);
            Assert.AreEqual(20d, timer.RemainingSeconds(100), 1e-9);
            Assert.IsFalse(timer.Observe(70, 105));
            Assert.AreEqual(15d, timer.RemainingSeconds(105), 1e-9);
            var restored = new MonsterCountDeathCountdown(70);
            restored.Restore(true, timer.StartedAt, 70, 108);
            Assert.AreEqual(12d, restored.RemainingSeconds(108), 1e-9);
            Assert.IsFalse(restored.Observe(69, 108));
            Assert.IsFalse(restored.IsActive);
            Assert.IsFalse(restored.Observe(70, 109));
            Assert.AreEqual(20d, restored.RemainingSeconds(109), 1e-9);
            Assert.IsFalse(restored.Observe(70, 128.9));
            Assert.IsTrue(restored.Observe(70, 129));
            Assert.Throws<FormatException>(() => restored.Restore(true, 0, 69, 20));
            Assert.Throws<FormatException>(() => restored.Restore(true, 500, 70, 20));
            Assert.Throws<FormatException>(() => restored.Restore(true, 1, 70, 22));

            using (var game = Create())
            {
                game.Battle.Advance(53);
                var snapshot = game.Battle.CaptureState();
                Assert.IsTrue(snapshot.DeathCountActive);
                Assert.AreEqual(20d, game.Battle.DeathCountdownGraceSeconds);
                SourceModelPracticeBattle.ValidateState(snapshot, 10,
                    game.Combat.Game.CurrentWave, game.Combat.Time);
                snapshot.DeathCountStartedAt = game.Combat.Time + 1;
                Assert.Throws<FormatException>(() =>
                    SourceModelPracticeBattle.ValidateState(snapshot, 10,
                        game.Combat.Game.CurrentWave, game.Combat.Time));
            }
        }

        [Test]
        public void SourceRouteMovesLivingEnemiesAndDoesNotKillAtLapEnd()
        {
            using (var game = Create())
            {
                game.Battle.Advance(.5);
                var before = game.Combat.MobileEnemySnapshot().Single();
                game.Battle.Advance(.1);
                var after = game.Combat.MobileEnemySnapshot().Single();
                Assert.AreEqual(before.Id, after.Id);
                Assert.AreNotEqual(before.Y, after.Y);
                Assert.AreEqual(before.Health, after.Health);
                Assert.AreEqual(0, game.Combat.Game.LeakedEnemies);
            }
        }

        [Test]
        public void MovingMonstersExposeRealRouteProgressForFrontTargetPriority()
        {
            using (var game = Create())
            {
                game.Battle.Advance(.5);
                var born = game.Combat.MobileEnemySnapshot().Single();
                Assert.AreEqual(0, born.PathProgress, 1e-12);
                game.Battle.Advance(.1);
                var moving = game.Combat.MobileEnemySnapshot().Single();
                Assert.Greater(moving.PathProgress, born.PathProgress);
                Assert.Less(moving.PathProgress, 1);
                Assert.AreEqual(moving.Id, CombatTargeting.Select(
                    new[] {
                        new TargetCandidate("back", 0, born.PathProgress, true),
                        new TargetCandidate(moving.Id, 100, moving.PathProgress, true)
                    }, 0, TargetPriority.FurthestAlongPath, true));
                Assert.AreEqual(0, game.Combat.PendingSimulationSeconds);
            }
            var catalog = new SourceModelPracticeCatalog(
                Data("mobile-practice-battle-model.json"));
            var corners = catalog.RouteCorners;
            double lap = 0;
            for (int i = 0; i < corners.Count; i++)
            {
                var from = corners[i];
                var to = corners[(i + 1) % corners.Count];
                double dx = to.X - from.X, dy = to.Y - from.Y;
                lap += Math.Sqrt(dx * dx + dy * dy);
            }
            Assert.AreEqual(.25, catalog.PathProgress(lap * .25), 1e-9);
            Assert.AreEqual(.25, catalog.PathProgress(lap * 1.25), 1e-9);
            Assert.AreEqual(0, catalog.PathProgress(lap), 1e-9);
            Assert.Throws<ArgumentOutOfRangeException>(() => catalog.PathProgress(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                catalog.PathProgress(double.NaN));
        }

        [Test]
        public void TenRoundPracticeCanBeCompletedUsingActualObjectAttackStats()
        {
            // Strong TEST formation skips player recruitment, but every hit
            // still runs the actual source-object dice/cadence/armor model.
            using (var game = Create(35))
            {
                for (int i = 0; i < 800 && game.Combat.Game.Phase == SessionPhase.Running; i++)
                    game.Battle.Advance(.5);
                Assert.AreEqual(SessionPhase.Won, game.Combat.Game.Phase, game.Battle.ResultMessage);
                Assert.AreEqual(10, game.Combat.Game.CurrentWave);
                Assert.AreEqual(316, game.Combat.Game.DefeatedEnemies);
                Assert.AreEqual(0, game.Combat.Game.AliveEnemies);
                Assert.AreEqual(0, game.Combat.Game.LeakedEnemies);
                Assert.AreEqual(18, game.Battle.RoundWispsAwarded);
                Assert.AreEqual(23, game.Economy.Wallet.Balance(WispResourceIds.Random));
                Assert.AreEqual("10라운드 연습 완료", game.Battle.ResultMessage);
                Assert.AreEqual(10, game.Battle.RoundHistory.Count);
                Assert.AreEqual(316, game.Battle.RoundHistory.Sum(x => x.Defeated));
                Assert.AreEqual(game.Battle.GoldAwarded, game.Battle.RoundHistory.Sum(x => x.GoldAwarded));
                CollectionAssert.AreEqual(Enumerable.Range(1, 10), game.Battle.RoundHistory.Select(x => x.Wave));
                long paid = game.Economy.Wallet.Balance("gold");
                game.Battle.Advance(60);
                Assert.AreEqual(paid, game.Economy.Wallet.Balance("gold"));
                Assert.AreEqual(10, game.Battle.RoundHistory.Count);
            }
        }

        [TestCase(10, "U-02")]
        [TestCase(20, "L-29")]
        public void UnkilledFinalPracticeBossFailsAtItsSourceDeadline(int lastRound, string attackerUnitId)
        {
            using (var game = Create(35, lastRound, attackerUnitId))
            {
                for (int i = 0; i < 1800 && game.Combat.Game.CurrentWave < lastRound; i++)
                    game.Battle.Advance(.5);
                Assert.AreEqual(lastRound, game.Combat.Game.CurrentWave);
                foreach (var unit in game.Combat.MobileUnitSnapshot()) game.Combat.RemoveCharacter(unit.InstanceId);
                game.Battle.Advance(60);
                Assert.AreEqual(SessionPhase.Lost, game.Combat.Game.Phase);
                StringAssert.Contains("보스 제한시간", game.Battle.ResultMessage);
                Assert.AreEqual(0, game.Combat.Game.LeakedEnemies);
            }
        }

        [TestCase("S-01")]
        [TestCase("S-09")]
        [TestCase("R-06")]
        [TestCase("L-29")]
        public void HigherGradeUnitsJoinTheSamePracticeAttackPipelineWithOnlyRegisteredSkills(string unitId)
        {
            using (var game = Create())
            {
                long id = game.Combat.SpawnCharacter(unitId, -3760, 3904);
                Assert.AreEqual(1, game.Combat.ReadyAttackers);
                Assert.IsFalse(game.Combat.HasVerifiedAttack(id));
                var events = game.Battle.Advance(3);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.Attack));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.EnemyDefeated));
                // S-09, R-06 (original Boo/Bon) and L-29 acquired source-backed
                // attack callbacks; only S-01 here still has no registered skill.
                if (unitId == "S-01")
                    Assert.IsFalse(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated ||
                        x.Type == CharacterCombatEventType.SkillDamage));
                if (unitId == "R-06")
                    Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated ||
                        x.Type == CharacterCombatEventType.SkillDamage),
                        "R-06's registered Boo/Bon attack callback must reach the practice combat ledger");
                Assert.IsFalse(events.Any(x => x.Type == CharacterCombatEventType.ZombieMaterialEarned));
                Assert.AreEqual(0, game.Combat.MoriaZombieMaterials);
                Assert.AreEqual(game.Combat.Game.DefeatedEnemies,
                    events.Count(x => x.Type == CharacterCombatEventType.EnemyDefeated));
            }
        }

        // A newly crafted unit has finite native range on the real phone lane.
        // Place a clearly labeled TEST_ONLY enemy within that same real
        // reach instead of assuming arbitrary wave monsters pass within
        // 525 units during a fixed three-second window.
        private static void PlaceReachableFusionTarget(
            CharacterCombatRuntime combat, string enemyId, long ownerId)
        {
            var pos = combat.Roster.Get(ownerId);
            combat.SpawnEnemyWithDefenses(enemyId, 1000000000000,
                CharacterEnemyKind.Line, pos.X + 2, pos.Y, 0,
                new EnemyDefenseProfile(CharacterEnemyKind.Line, 0, 0,
                    "medium", "TEST_ONLY_FINITE_REACH_FUSION_TARGET"));
        }

        [Test]
        public void FusionDuringPracticeReplacesActiveAttackersAndContinuesCombat()
        {
            var root = new GameObject("ORDR live Special fusion");
            root.SetActive(false);
            try
            {
                var owner = root.AddComponent<OrdrCharacterCombatController>();
                typeof(OrdrCharacterCombatController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(owner, null);
                owner.BeginUnitPreparation();
                for (int i = 0; i < 3; i++) owner.Combat.SpawnCharacter("C-01", -4000, 4000);
                owner.BeginStandaloneBattle();
                owner.PracticeBattle.Advance(21.1);
                var offer = owner.ManualActions.PreviewFusion(owner.Combat.MobileUnitSnapshot())
                    .Single(x => x.RecipeId == "A012");
                Assert.AreEqual(Ordr.Mobile.Mobile.ManualActionState.Succeeded,
                    owner.ManualActions.ConfirmFusion(offer).State);
                Assert.AreEqual(1, owner.Combat.CharacterCount);
                Assert.AreEqual(1, owner.Combat.ReadyAttackers);
                var fused = owner.Combat.MobileUnitSnapshot().Single();
                Assert.AreEqual("S-07", fused.Unit.Id);
                PlaceReachableFusionTarget(owner.Combat, "fixture:fusion-special", fused.InstanceId);
                var events = owner.PracticeBattle.Advance(3);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.Attack &&
                    x.EnemyId == "fixture:fusion-special"), 
                    "Fusion must preserve a registered native attack and hit a verified in-reach target.");
                Assert.AreEqual(0, owner.Combat.Game.LeakedEnemies);
                Assert.IsFalse(owner.HasSourceVerifiedDamageRuntime);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void CraftingAndSellingRareDuringPracticeUsesOneRosterAndWallet()
        {
            var root = new GameObject("ORDR live Rare growth");
            root.SetActive(false);
            try
            {
                var owner = root.AddComponent<OrdrCharacterCombatController>();
                typeof(OrdrCharacterCombatController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(owner, null);
                owner.BeginUnitPreparation();
                owner.Combat.SpawnCharacter("S-07", -4000, 4000);
                owner.Combat.SpawnCharacter("S-12", -4000, 4000);
                owner.Combat.SpawnCharacter("S-18", -4000, 4000);
                owner.BeginStandaloneBattle();
                owner.PracticeBattle.Advance(21.1);
                var offer = owner.ManualActions.PreviewFusion(owner.Combat.MobileUnitSnapshot())
                    .Single(x => x.RecipeId == "A00F");
                Assert.AreEqual(Ordr.Mobile.Mobile.ManualActionState.Succeeded,
                    owner.ManualActions.ConfirmFusion(offer).State);
                var made = owner.Combat.MobileUnitSnapshot().Single();
                Assert.AreEqual("R-06", made.Unit.Id);
                Assert.AreEqual(1, owner.Combat.ReadyAttackers);
                PlaceReachableFusionTarget(owner.Combat, "fixture:fusion-rare", made.InstanceId);
                Assert.IsTrue(owner.PracticeBattle.Advance(3).Any(x =>
                    x.Type == CharacterCombatEventType.Attack &&
                    x.EnemyId == "fixture:fusion-rare"));
                long before = owner.WispBalance(WispResourceIds.Random);
                long wood = owner.SharedEconomy.Wallet.Balance("wood");
                Assert.AreEqual(Ordr.Mobile.Mobile.ManualActionState.Succeeded,
                    owner.RequestLowerGradeSale(made.InstanceId).State);
                Assert.AreEqual(before + 2, owner.WispBalance(WispResourceIds.Random));
                Assert.AreEqual(wood + 1, owner.SharedEconomy.Wallet.Balance("wood"));
                Assert.AreEqual(0, owner.Combat.CharacterCount);
                Assert.AreEqual(0, owner.Combat.ReadyAttackers);
                Assert.IsFalse(owner.HasSourceVerifiedDamageRuntime);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void TwentyRoundPracticeCompletesWithTheSourceSpawnsAndOneTimeRoundRewards()
        {
            // Test-only formation. No free units are added to normal gameplay.
            using (var game = Create(35, 20, "L-29"))
            {
                for (int i = 0; i < 2000 && game.Combat.Game.Phase == SessionPhase.Running; i++)
                    game.Battle.Advance(.5);
                Assert.AreEqual(SessionPhase.Won, game.Combat.Game.Phase, game.Battle.ResultMessage);
                Assert.AreEqual(20, game.Combat.Game.CurrentWave);
                Assert.AreEqual(632, game.Combat.Game.DefeatedEnemies);
                Assert.AreEqual(38, game.Battle.RoundWispsAwarded);
                Assert.AreEqual(43, game.Economy.Wallet.Balance(WispResourceIds.Random));
                Assert.AreEqual(20, game.Battle.RoundHistory.Count);
                Assert.AreEqual(632, game.Battle.RoundHistory.Sum(x => x.Defeated));
                Assert.AreEqual(game.Battle.GoldAwarded, game.Battle.RoundHistory.Sum(x => x.GoldAwarded));
                Assert.AreEqual("20라운드 연습 완료", game.Battle.ResultMessage);
                long gold = game.Economy.Wallet.Balance("gold");
                game.Battle.Advance(100);
                Assert.AreEqual(gold, game.Economy.Wallet.Balance("gold"));
                Assert.AreEqual(20, game.Battle.RoundHistory.Count);
            }
        }

        [Test]
        public void PracticeLengthIsExplicitAndUnsupportedLengthsFailBeforeCombatMutation()
        {
            string json = Data("mobile-practice-battle-model.json");
            string standalone = Data("mobile-standalone-enemy-model.json");
            Assert.AreEqual(10, new SourceModelPracticeCatalog(json).LastRound);
            Assert.AreEqual(20, new SourceModelPracticeCatalog(json, 20).LastRound);
            Assert.Throws<ArgumentOutOfRangeException>(() => new SourceModelPracticeCatalog(json, 65));
            Assert.AreEqual(65, new SourceModelPracticeCatalog(standalone, 65).LastRound);
            Assert.Throws<ArgumentOutOfRangeException>(() => new SourceModelPracticeCatalog(standalone, 66));
            var root = new GameObject("ORDR practice length");
            root.SetActive(false);
            try
            {
                var owner = root.AddComponent<OrdrCharacterCombatController>();
                typeof(OrdrCharacterCombatController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(owner, null);
                owner.BeginUnitPreparation();
                owner.Combat.SpawnCharacter("C-01", -4000, 4000);
                long wisps = owner.WispBalance(WispResourceIds.Random);
                Assert.AreEqual(0, owner.Combat.Game.CurrentWave);
                Assert.AreEqual(wisps, owner.WispBalance(WispResourceIds.Random));
                owner.BeginStandaloneBattle();
                Assert.AreEqual(65, owner.PracticeBattle.LastRound);
                Assert.AreEqual(1, owner.Combat.Game.CurrentWave);
                Assert.IsFalse(owner.HasSourceVerifiedDamageRuntime);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void PracticeModelCannotBeRelabelledAsRetailVerified()
        {
            foreach (int length in new[] { 20, 65 })
            {
                string json = Data(length == 65
                    ? "mobile-standalone-enemy-model.json" : "mobile-practice-battle-model.json");
                Assert.Throws<FormatException>(() => new SourceModelPracticeCatalog(
                    json.Replace("\"retailParityVerified\": false", "\"retailParityVerified\": true"), length));
                Assert.Throws<FormatException>(() => new SourceModelPracticeCatalog(
                    json.Replace("\"safeForVerifiedMonsterWaveRuntime\": false", "\"safeForVerifiedMonsterWaveRuntime\": true"), length));
            }
        }

        [Test]
        public void AllSixtyFiveSourceWavesBuildWithoutAnExternalLiveWitness()
        {
            var identity = Original323MonsterWaveIdentityCatalog.FromJson(
                Data("original-2323C-monster-wave-script-evidence.json"));
            var catalog = new SourceModelPracticeCatalog(Data("mobile-standalone-enemy-model.json"), 65);
            double expectedMagicResistance;
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetNightmareBaselineResistance(
                out expectedMagicResistance));
            int monsters = 0;
            for (int wave = 1; wave <= 65; wave++)
            {
                var plan = catalog.BuildPracticePlan(wave, identity);
                Assert.IsTrue(identity.MatchesSinglePlayerSpawnCadence(plan), "wave " + wave);
                Assert.IsTrue(plan.Spawns.All(x => x.Health > 0), "wave " + wave);
                foreach (var enemy in plan.Spawns)
                {
                    Assert.AreEqual(Original323NightmareA04QSpawnState.NightmareInitialLevel,
                        enemy.Defenses.MagicAbilityLevel, "wave " + wave);
                    Assert.AreEqual(expectedMagicResistance,
                        enemy.Defenses.MagicResistancePercent.Value, 0.0001,
                        "wave " + wave);
                }
                monsters += plan.Spawns.Count;
                if (wave == 55 || wave == 60 || wave == 65)
                {
                    Assert.AreEqual(36, plan.Spawns.Count);
                    Assert.AreEqual(1, plan.Spawns.Count(x => x.Kind == CharacterEnemyKind.Boss));
                    Assert.AreEqual(35, plan.Spawns.Count(x => x.Kind == CharacterEnemyKind.Line));
                }
            }
            Assert.AreEqual(65 * 35 - 5 * 34 + 3, monsters);
            using (var game = Create(0, 65))
            {
                Assert.AreEqual(65, game.Battle.LastRound);
                Assert.AreEqual(1, game.Combat.Game.CurrentWave);
                Assert.AreEqual(21, game.Battle.PreparationSecondsRemaining, 1e-6);
                Assert.AreEqual(56, game.Battle.SecondsRemaining, 1e-6);
                game.Battle.Advance(.5);
                Assert.AreEqual(0, game.Combat.Game.AliveEnemies);
                Assert.AreEqual(20.5, game.Battle.PreparationSecondsRemaining, 1e-6);
                game.Battle.Advance(21.1);
                Assert.AreEqual(0, game.Battle.PreparationSecondsRemaining, 1e-6);
                Assert.AreEqual(1, game.Combat.Game.AliveEnemies);
                Assert.AreEqual(338, game.Combat.MobileEnemySnapshot().Single().Health);
                Assert.AreEqual(0, game.Combat.Game.LeakedEnemies);
            }
        }

        [Test]
        public void OnlyStandaloneSixtyFiveRoundUsesSourceVerifiedPreparationAndTransition()
        {
            var standalone = new SourceModelPracticeCatalog(
                Data("mobile-standalone-enemy-model.json"), 65);
            Assert.AreEqual(21, standalone.ModelLeadInSeconds(1));
            Assert.AreEqual(0, standalone.ModelLeadInSeconds(2));
            Assert.AreEqual(0, standalone.ModelLeadInSeconds(50));
            Assert.AreEqual(70, standalone.ModelLeadInSeconds(51));
            Assert.AreEqual(0, standalone.ModelLeadInSeconds(55));
            Assert.AreEqual(0, standalone.ModelLeadInSeconds(65));
            Assert.Throws<ArgumentOutOfRangeException>(() => standalone.ModelLeadInSeconds(66));

            var training = new SourceModelPracticeCatalog(Data("mobile-practice-battle-model.json"));
            Assert.AreEqual(0, training.ModelLeadInSeconds(1));
        }

        [Test]
        public void StandaloneMagicDefenseLevelCannotDriftOrBecomeRetailApproval()
        {
            string standalone = Data("mobile-standalone-enemy-model.json");
            Assert.Throws<FormatException>(() => new SourceModelPracticeCatalog(
                standalone.Replace("\"initialMagicAbilityLevel\": 16",
                    "\"initialMagicAbilityLevel\": 15"), 65));
            var oldPractice = new SourceModelPracticeCatalog(
                Data("mobile-practice-battle-model.json"));
            var identity = Original323MonsterWaveIdentityCatalog.FromJson(
                Data("original-2323C-monster-wave-script-evidence.json"));
            Assert.IsFalse(oldPractice.BuildPracticePlan(1, identity).Spawns[0]
                .Defenses.MagicResistancePercent.HasValue);
        }

        [Test]
        public void ControllerCanContinuePreparedRosterIntoPracticeWhileLiveStartStaysBlocked()
        {
            var root = new GameObject("ORDR practice integration");
            root.SetActive(false);
            try
            {
                var owner = root.AddComponent<OrdrCharacterCombatController>();
                typeof(OrdrCharacterCombatController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(owner, null);
                owner.BeginUnitPreparation();
                long id = owner.Combat.SpawnCharacter("C-01", -4768, 3232);
                long balance = owner.WispBalance(WispResourceIds.Random);
                owner.BeginStandaloneBattle();
                Assert.IsNotNull(owner.PracticeBattle);
                Assert.IsFalse(owner.HasSourceVerifiedDamageRuntime);
                Assert.IsFalse(owner.IsUnitPreparation);
                Assert.AreEqual(1, owner.Combat.Game.CurrentWave);
                Assert.AreEqual("C-01", owner.Combat.Roster.Get(id).UnitId);
                Assert.AreEqual(balance, owner.WispBalance(WispResourceIds.Random));
                Assert.AreEqual(1, owner.Combat.ReadyAttackers);
                owner.SetManualGameplayPause(true);
                typeof(OrdrCharacterCombatController).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(owner, null);
                Assert.AreEqual(0, owner.Combat.Time);
                Assert.Throws<InvalidOperationException>(() => owner.BeginStandaloneBattle());
                Assert.Throws<InvalidOperationException>(() => owner.BeginVerifiedFirstWave());
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
