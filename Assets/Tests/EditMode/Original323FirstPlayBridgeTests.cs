using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Unity;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323FirstPlayBridgeTests
    {
        private static string Data(string name) => File.ReadAllText(
            Path.Combine(Application.dataPath, "..", "data", name));

        private sealed class Live
        {
            public CharacterCombatRuntime Combat;
            public SharedEconomySession Shared;
            public OriginalMapBaseStatCatalog OriginalUnits;
        }

        private static Live Create()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var original = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var profiles = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var rejected = new RejectUnverifiedDamageFormula();
            var roster = new UnitRoster();
            var combat = new CharacterCombatRuntime(units, profiles,
                rejected, rejected, new SeededNonOriginalProcRandom(11), roster);
            var shared = new SharedEconomySession(new EconomyWallet(), roster);
            combat.Start();
            combat.BeginWave(1);
            shared.Wallet.Grant(new ResourceBundle().Add(WispResourceIds.Random, 3));
            return new Live { Combat = combat, Shared = shared, OriginalUnits = original };
        }

        // Contract fixture only; there is deliberately no production adapter
        // providing a guessed cost, common-unit pool or random roll.
        private sealed class RecruitmentWorld : IOriginal323CommonWispRecruitmentWorld
        {
            public CharacterCombatRuntime Combat;
            public string Rawcode = "h003";
            public bool Reject;
            public bool ThrowOnCommit;
            public bool DirtyWallet;
            public SharedEconomySession Shared;
            public int OriginalRandomWispCost => 2;
            public string OriginalSourceEvidence =>
                "contract review reference " +
                Original323MonsterWaveIdentityCatalog.OriginalWar3mapJSha256;
            public IOriginal323CommonWispRecruitmentTransaction Begin() =>
                new RecruitmentTransaction(this);

            private sealed class RecruitmentTransaction : IOriginal323CommonWispRecruitmentTransaction
            {
                private readonly RecruitmentWorld owner;
                private long spawned;
                private bool committed;
                public RecruitmentTransaction(RecruitmentWorld world) { owner = world; }
                public bool TryCreate(out long id, out string rawcode)
                {
                    spawned = owner.Combat.SpawnCharacter("C-01", -3456, -9696);
                    id = spawned;
                    rawcode = owner.Rawcode;
                    if (owner.DirtyWallet)
                        owner.Shared.Wallet.Grant(new ResourceBundle().Add("gold", 1));
                    return !owner.Reject;
                }
                public void Commit()
                {
                    if (owner.ThrowOnCommit)
                        throw new InvalidOperationException("commit rejected");
                    committed = true;
                }
                public void Dispose()
                {
                    if (!committed && spawned > 0)
                        owner.Combat.RollbackUncommittedSpawn(spawned);
                }
            }
        }

        [Test]
        public void SourceBackedManualRecruitmentCreatesOneOriginalCommonAndSpendsSharedWisp()
        {
            var live = Create();
            var provider = new RecruitmentWorld { Combat = live.Combat, Shared = live.Shared };
            var service = new Original323CommonWispRecruitmentService(
                live.Combat, live.Shared, live.OriginalUnits, provider);

            Assert.IsTrue(service.CanRecruit);
            var result = service.RecruitOne();
            Assert.AreEqual("C-01", result.CatalogId);
            Assert.AreEqual(2, result.SpentRandomWisps);
            Assert.AreEqual(1, live.Combat.CharacterCount);
            Assert.AreEqual(1, live.Shared.Units.Count);
            Assert.AreEqual(result.InstanceId, live.Combat.MobileUnitSnapshot()[0].InstanceId);
            Assert.AreEqual(1, live.Shared.Wallet.Balance(WispResourceIds.Random));
        }

        [TestCase("h999", false, false)]
        [TestCase("h003", false, true)]
        [TestCase("h003", true, false)]
        public void RejectedRawcodeWalletMutationOrCommitRollsBackSpawnAndWisp(
            string rawcode, bool rejectCommit, bool dirtyWallet)
        {
            var live = Create();
            var provider = new RecruitmentWorld { Combat = live.Combat,
                Shared = live.Shared, Rawcode = rawcode,
                ThrowOnCommit = rejectCommit, DirtyWallet = dirtyWallet };
            var service = new Original323CommonWispRecruitmentService(
                live.Combat, live.Shared, live.OriginalUnits, provider);
            Assert.Throws<InvalidOperationException>(() => service.RecruitOne());
            Assert.AreEqual(0, live.Combat.CharacterCount);
            Assert.AreEqual(0, live.Shared.Units.Count);
            Assert.AreEqual(3, live.Shared.Wallet.Balance(WispResourceIds.Random));
            Assert.AreEqual(0, live.Shared.Wallet.Balance("gold"));
        }

        [Test]
        public void RejectedOriginalDrawCannotLeaveSideEffects()
        {
            var live = Create();
            var provider = new RecruitmentWorld { Combat = live.Combat,
                Shared = live.Shared, Reject = true };
            var service = new Original323CommonWispRecruitmentService(
                live.Combat, live.Shared, live.OriginalUnits, provider);
            Assert.Throws<InvalidOperationException>(() => service.RecruitOne());
            Assert.AreEqual(0, live.Combat.CharacterCount);
            Assert.AreEqual(3, live.Shared.Wallet.Balance(WispResourceIds.Random));
        }

        private sealed class ObservedPositions : IOriginal323ObservedMonsterPositionSource
        {
            public bool Approved = true;
            public bool ValidPosition = true;
            public bool DirtyPreflight;
            public bool DirtyProgress;
            public bool DirtyStatus;
            public string InvalidForEnemyId;
            public CharacterCombatRuntime Combat;
            public bool TryObserve(string id, double time, out double x,
                out double y, out double progress, out string evidence)
            {
                if (DirtyPreflight) Combat.TryMoveEnemy(id, 999, 0, 0);
                if (DirtyProgress) Combat.TryMoveEnemy(id, 20, 0, .6);
                if (DirtyStatus) Combat.StatusOf(id).Apply(
                    new CombatEffect("TEST_ONLY-observer-side-effect",
                        CombatEffectType.ArmorReduction,
                        CombatEffectOrigin.Aura, 5, 0));
                x = ValidPosition && id != InvalidForEnemyId
                    ? 210 : double.NaN;
                y = 15;
                progress = .3;
                evidence = Approved ? "observed native frame " +
                    Original323MonsterWaveIdentityCatalog.OriginalWar3mapJSha256 :
                    "unverified";
                return true;
            }
        }

        [Test]
        public void OriginalObservedPositionsUpdateTheSameEnemyThatThePresenterReads()
        {
            var live = Create();
            live.Combat.SpawnEnemy("mob", 300, CharacterEnemyKind.Line, 20, 0, 0);
            var observed = new ObservedPositions { Combat = live.Combat };
            var world = new Original323ObservedMonsterMovementWorld(observed);
            Assert.AreEqual(1, world.ApplyAtCurrentCombatTime(live.Combat));
            Assert.AreEqual(210, live.Combat.MobileEnemySnapshot()[0].X);
            Assert.AreEqual(15, live.Combat.MobileEnemySnapshot()[0].Y);
            Assert.AreEqual(.3, live.Combat.MobileEnemySnapshot()[0].PathProgress, 1e-9);
            Assert.AreEqual(300, live.Combat.MobileEnemySnapshot()[0].Health);
        }

        [Test]
        public void InvalidObservedPositionOrDirtyObserverCannotMoveEnemy()
        {
            var live = Create();
            live.Combat.SpawnEnemy("mob", 300, CharacterEnemyKind.Line, 20, 0, 0);
            var observed = new ObservedPositions { Combat = live.Combat,
                ValidPosition = false };
            var world = new Original323ObservedMonsterMovementWorld(observed);
            Assert.Throws<InvalidOperationException>(() =>
                world.ApplyAtCurrentCombatTime(live.Combat));
            Assert.AreEqual(20, live.Combat.MobileEnemySnapshot()[0].X);
            observed.ValidPosition = true;
            observed.Approved = false;
            Assert.Throws<InvalidOperationException>(() =>
                world.ApplyAtCurrentCombatTime(live.Combat));
            Assert.AreEqual(20, live.Combat.MobileEnemySnapshot()[0].X);
            observed.Approved = true;
            observed.DirtyPreflight = true;
            Assert.Throws<InvalidOperationException>(() =>
                world.ApplyAtCurrentCombatTime(live.Combat));
        }

        [Test]
        public void ObservedMonsterMovementRejectsPathProgressAndStatusTampering()
        {
            var live = Create();
            live.Combat.SpawnEnemy("mob", 300, CharacterEnemyKind.Line, 20, 0, 0);
            var observer = new ObservedPositions {
                Combat = live.Combat, DirtyProgress = true
            };
            var world = new Original323ObservedMonsterMovementWorld(observer);
            Assert.Throws<InvalidOperationException>(() =>
                world.ApplyAtCurrentCombatTime(live.Combat));
            Assert.AreEqual(20, live.Combat.MobileEnemySnapshot()[0].X);
            Assert.AreEqual(.6, live.Combat.MobileEnemySnapshot()[0].PathProgress,
                "An invalid observer is detected, not silently overwritten.");
            observer.DirtyProgress = false;
            observer.DirtyStatus = true;
            Assert.Throws<InvalidOperationException>(() =>
                world.ApplyAtCurrentCombatTime(live.Combat));
            Assert.AreEqual(20, live.Combat.MobileEnemySnapshot()[0].X);
        }

        [Test]
        public void ObservedMonsterBatchRejectsInvalidLaterEnemyWithoutPartialCommit()
        {
            var live = Create();
            live.Combat.SpawnEnemy("a", 300, CharacterEnemyKind.Line, 20, 0, 0);
            live.Combat.SpawnEnemy("b", 300, CharacterEnemyKind.Line, 30, 0, 0);
            var observer = new ObservedPositions { InvalidForEnemyId = "b" };
            var world = new Original323ObservedMonsterMovementWorld(observer);
            Assert.Throws<InvalidOperationException>(() =>
                world.ApplyAtCurrentCombatTime(live.Combat));
            Assert.AreEqual(20, live.Combat.MobileEnemySnapshot()[0].X);
            Assert.AreEqual(30, live.Combat.MobileEnemySnapshot()[1].X);
            observer.InvalidForEnemyId = null;
            Assert.AreEqual(2, world.ApplyAtCurrentCombatTime(live.Combat));
            Assert.AreEqual(210, live.Combat.MobileEnemySnapshot()[0].X);
            Assert.AreEqual(210, live.Combat.MobileEnemySnapshot()[1].X);
        }

        [Test]
        public void ObservedMonsterFrameRefreshesUnlimitedAuraOnceNotPerEnemy()
        {
            var live = Create();
            long caster = live.Combat.SpawnCharacter("C-01", 0, 0);
            live.Combat.SpawnEnemy("a", 300, CharacterEnemyKind.Line, 20, 0, 0);
            live.Combat.SpawnEnemy("b", 300, CharacterEnemyKind.Line, 30, 0, 0);
            live.Combat.AddVerifiedEnemyAura(new CombatAuraEmitter(
                caster, "TEST_ONLY-aura", CombatEffectType.ArmorReduction,
                5, 1, new[] { CharacterEnemyKind.Line },
                "TEST_ONLY verified target fixture"));
            long before = live.Combat.StatusOf("a").Revision;
            Assert.AreEqual(2, new Original323ObservedMonsterMovementWorld(
                new ObservedPositions()).ApplyAtCurrentCombatTime(live.Combat));
            // One refresh removes and reapplies one existing source.
            Assert.AreEqual(before + 2, live.Combat.StatusOf("a").Revision);
            Assert.AreEqual(1, live.Combat.StatusOf("a").ActiveCount);
            Assert.AreEqual(1, live.Combat.StatusOf("b").ActiveCount);
            before = live.Combat.StatusOf("a").Revision;
            Assert.AreEqual(2, new Original323ObservedMonsterMovementWorld(
                new ObservedPositions()).ApplyAtCurrentCombatTime(live.Combat));
            Assert.AreEqual(before, live.Combat.StatusOf("a").Revision,
                "Reobserving unchanged positions must not recompute auras.");
        }

        private sealed class ExitObserver : IOriginal323ObservedMonsterEscapeSource
        {
            public CharacterCombatRuntime Combat;
            public string ExitId = "leaver";
            public bool EvidenceApproved = true;
            public bool DirtyPosition;
            public int Calls;
            public bool TryObserveEscape(int spawnWave, string instanceId,
                double seconds, out string evidence)
            {
                Calls++;
                if (DirtyPosition)
                    Combat.TryMoveEnemy(instanceId, 999, 0, 0);
                evidence = EvidenceApproved ?
                    "original live exit for wave " + spawnWave + " " +
                    Original323MonsterWaveIdentityCatalog.OriginalWar3mapJSha256 :
                    "TEST_ONLY fake exit";
                return instanceId == ExitId;
            }
        }

        private static CharacterCombatRuntime ExitWorld()
        {
            var units = CharacterUnitCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[]}");
            var profiles = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var deferred = new RejectUnverifiedDamageFormula();
            var world = new CharacterCombatRuntime(
                units, profiles, deferred, deferred,
                new SeededNonOriginalProcRandom(12));
            world.Start();
            return world;
        }

        private static VerifiedWavePlan ExitPlan()
        {
            return new VerifiedWavePlan(1, new[] {
                new VerifiedWaveEnemy(0, "leaver", 300,
                    CharacterEnemyKind.Line, 20, 0, 0,
                    EnemyDefenseProfile.UnverifiedKind(CharacterEnemyKind.Line),
                    "o00I"),
                new VerifiedWaveEnemy(0, "survivor", 300,
                    CharacterEnemyKind.Line, 30, 0, 0,
                    EnemyDefenseProfile.UnverifiedKind(CharacterEnemyKind.Line),
                    "o00I")
            }, "TEST_ONLY: authoritative exit contract fixture");
        }

        [Test]
        public void ObservedNativeEscapeUpdatesOneWaveLedgerNotDeathOrGold()
        {
            var game = ExitWorld();
            var director = new CombatWaveDirector();
            director.Begin(game, ExitPlan());
            director.Step(game, 0);
            var source = new ExitObserver();
            var bridge = new Original323ObservedMonsterEscapeWorld(source);
            Assert.AreEqual(1, bridge.ApplyAtCurrentCombatTime(game, director));
            Assert.IsNull(game.EnemyOf("leaver"));
            Assert.AreEqual(300, game.EnemyOf("survivor").Health);
            Assert.AreEqual(1, director.CurrentProgress.Escaped);
            Assert.AreEqual(0, director.CurrentProgress.Defeated);
            Assert.AreEqual(1, game.Game.LeakedEnemies);
            Assert.AreEqual(0, bridge.ApplyAtCurrentCombatTime(game, director));
            Assert.AreEqual(1, game.Game.LeakedEnemies);
        }

        [Test]
        public void RejectedExitEvidenceDoesNotLeakAnything()
        {
            var game = ExitWorld();
            var director = new CombatWaveDirector();
            director.Begin(game, ExitPlan()); director.Step(game, 0);
            var source = new ExitObserver { EvidenceApproved = false };
            var bridge = new Original323ObservedMonsterEscapeWorld(source);
            Assert.Throws<InvalidOperationException>(() =>
                bridge.ApplyAtCurrentCombatTime(game, director));
            Assert.AreEqual(2, game.Game.AliveEnemies);
            Assert.AreEqual(0, director.CurrentProgress.Escaped);
        }

        [Test]
        public void EscapeWitnessMustNotModifyEnemyDuringReadOnlyInspection()
        {
            var game = ExitWorld();
            var director = new CombatWaveDirector();
            director.Begin(game, ExitPlan()); director.Step(game, 0);
            var source = new ExitObserver { Combat = game, DirtyPosition = true };
            var bridge = new Original323ObservedMonsterEscapeWorld(source);
            Assert.Throws<InvalidOperationException>(() =>
                bridge.ApplyAtCurrentCombatTime(game, director));
            Assert.AreEqual(2, game.Game.AliveEnemies);
            Assert.AreEqual(0, director.CurrentProgress.Escaped);
        }

        [Test]
        public void UnscheduledEnemyCannotReceiveVerifiedWaveExit()
        {
            var game = ExitWorld();
            var director = new CombatWaveDirector();
            director.Begin(game, ExitPlan()); director.Step(game, 0);
            game.SpawnEnemy("extra", 300, CharacterEnemyKind.Line, 0, 0, 0);
            var source = new ExitObserver { ExitId = "extra" };
            var bridge = new Original323ObservedMonsterEscapeWorld(source);
            Assert.AreEqual(0, bridge.ApplyAtCurrentCombatTime(game, director));
            Assert.AreEqual(2, source.Calls);
            Assert.AreEqual(3, game.Game.AliveEnemies);
        }

        // This is an isolated movement contract. Speeds and paths below are
        // TEST_ONLY numbers; they must never be installed as 2.323[C] data.
        private sealed class PathSpeedFixture : IOriginalEnemyMovementRules
        {
            public string InvalidFor;
            public bool DirtyPreflight;
            public CharacterCombatRuntime World;
            public bool TryResolveSpeed(CombatEnemy enemy,
                IReadOnlyList<CombatEffect> effects, out double speed)
            {
                if (DirtyPreflight) World.TryMoveEnemy(enemy.Id, 77, 0, .77);
                speed = enemy.Id == InvalidFor ? double.NaN : 10;
                return true;
            }
        }

        private static VerifiedCombatPath FixtureStraightPath()
        {
            return new VerifiedCombatPath(new[] {
                new CombatPathPoint(0, 0), new CombatPathPoint(100, 0)
            }, "TEST_ONLY: synthetic straight route, not original WC3");
        }

        [Test]
        public void PathAdvanceMovesMultipleEnemiesAtomicallyAndDoesNotInventLeaks()
        {
            var live = Create();
            live.Combat.SpawnEnemy("a", 300, CharacterEnemyKind.Line, 0, 0, 0);
            live.Combat.SpawnEnemy("b", 300, CharacterEnemyKind.Line, 0, 0, 0);
            var route = FixtureStraightPath();
            var driver = new CombatEnemyPathDriver();
            driver.Attach("a", route);
            driver.Attach("b", route);
            var speed = new PathSpeedFixture();
            Assert.AreEqual(0, driver.Advance(live.Combat, speed, 1).Count);
            foreach (var enemy in live.Combat.MobileEnemySnapshot())
            {
                Assert.AreEqual(10, enemy.X, 1e-9);
                Assert.AreEqual(.1, enemy.PathProgress, 1e-9);
            }
            var arrivals = driver.Advance(live.Combat, speed, 9);
            CollectionAssert.AreEqual(new[] { "a", "b" }, arrivals);
            Assert.AreEqual(2, live.Combat.Game.AliveEnemies);
            Assert.AreEqual(0, live.Combat.Game.LeakedEnemies);
            Assert.AreEqual(0, driver.Advance(live.Combat, speed, 1).Count,
                "A finished path is detached and not reported twice.");
            Assert.AreEqual(300, live.Combat.EnemyOf("a").Health);
        }

        [Test]
        public void InvalidLateSpeedDoesNotPartiallyMoveAnEarlierEnemy()
        {
            var live = Create();
            live.Combat.SpawnEnemy("a", 300, CharacterEnemyKind.Line, 0, 0, 0);
            live.Combat.SpawnEnemy("b", 300, CharacterEnemyKind.Line, 0, 0, 0);
            var route = FixtureStraightPath();
            var driver = new CombatEnemyPathDriver();
            driver.Attach("a", route);
            driver.Attach("b", route);
            var speed = new PathSpeedFixture { InvalidFor = "b" };
            Assert.Throws<InvalidOperationException>(() =>
                driver.Advance(live.Combat, speed, 1));
            foreach (var enemy in live.Combat.MobileEnemySnapshot())
            {
                Assert.AreEqual(0, enemy.X);
                Assert.AreEqual(0, enemy.PathProgress);
            }
            speed.InvalidFor = null;
            driver.Advance(live.Combat, speed, 1);
            Assert.AreEqual(10, live.Combat.MobileEnemySnapshot()[0].X);
            Assert.AreEqual(10, live.Combat.MobileEnemySnapshot()[1].X);
        }

        [Test]
        public void PathSpeedWitnessMustNotMutateCombatDuringPreflight()
        {
            var live = Create();
            live.Combat.SpawnEnemy("a", 300, CharacterEnemyKind.Line, 0, 0, 0);
            var driver = new CombatEnemyPathDriver();
            driver.Attach("a", FixtureStraightPath());
            var speed = new PathSpeedFixture {
                World = live.Combat, DirtyPreflight = true
            };
            Assert.Throws<InvalidOperationException>(() =>
                driver.Advance(live.Combat, speed, 1));
            // The bad policy is rejected rather than adding another movement
            // or incorrectly claiming to have rolled its mutation back.
            Assert.AreEqual(77, live.Combat.MobileEnemySnapshot()[0].X);
            speed.DirtyPreflight = false;
            driver.Advance(live.Combat, speed, 1);
            Assert.AreEqual(10, live.Combat.MobileEnemySnapshot()[0].X);
        }

        [Test]
        public void StunnedPathEnemyRemainsStationaryWhileOtherEnemyMoves()
        {
            var live = Create();
            live.Combat.SpawnEnemy("a", 300, CharacterEnemyKind.Line, 0, 0, 0);
            live.Combat.SpawnEnemy("b", 300, CharacterEnemyKind.Line, 0, 0, 0);
            var route = FixtureStraightPath();
            var driver = new CombatEnemyPathDriver();
            driver.Attach("a", route);
            driver.Attach("b", route);
            live.Combat.StatusOf("a").Apply(new CombatEffect(
                "TEST_ONLY-path-stun", CombatEffectType.Stun,
                CombatEffectOrigin.TriggeredSkill, 1, 3));
            driver.Advance(live.Combat, new PathSpeedFixture(), 2);
            Assert.AreEqual(0, live.Combat.MobileEnemySnapshot()[0].X);
            Assert.AreEqual(0, live.Combat.MobileEnemySnapshot()[0].PathProgress);
            Assert.AreEqual(20, live.Combat.MobileEnemySnapshot()[1].X);
            Assert.AreEqual(.2, live.Combat.MobileEnemySnapshot()[1].PathProgress);
        }

        [Test]
        public void EditorStyleSyntheticRoutesUseCombatElapsedTimeWithoutCreatingLeaks()
        {
            var live = Create();
            live.Combat.SpawnEnemy("first", 300, CharacterEnemyKind.Line, 0, 0, 0);
            var driver = new CombatEnemyPathDriver();
            driver.Attach("first", new VerifiedCombatPath(new[] {
                new CombatPathPoint(0, 0),
                new CombatPathPoint(100, 0)
            }, "TEST_ONLY: Editor-style synthetic first wave path"));
            var speed = new PathSpeedFixture();

            // An editor tick without an actual simulation step must not move.
            Assert.AreEqual(0, driver.Advance(live.Combat, speed, 0).Count);
            Assert.AreEqual(0, live.Combat.EnemyOf("first").X);
            Assert.AreEqual(0, live.Combat.Game.LeakedEnemies);

            driver.Advance(live.Combat, speed, 3);
            Assert.AreEqual(30, live.Combat.EnemyOf("first").X);
            Assert.AreEqual(.3, live.Combat.EnemyOf("first").PathProgress, 1e-9);

            // A subsequent spawn joins its own route without resetting the
            // first enemy's progression, even across a later practice wave.
            live.Combat.SpawnEnemy("second", 300, CharacterEnemyKind.Line, 20, 10, 0);
            driver.Attach("second", new VerifiedCombatPath(new[] {
                new CombatPathPoint(20, 10),
                new CombatPathPoint(120, 10)
            }, "TEST_ONLY: Editor-style synthetic second wave path"));
            var arrivals = driver.Advance(live.Combat, speed, 7);
            CollectionAssert.AreEqual(new[] { "first" }, arrivals);
            Assert.AreEqual(100, live.Combat.EnemyOf("first").X);
            Assert.AreEqual(90, live.Combat.EnemyOf("second").X);
            Assert.AreEqual(.7, live.Combat.EnemyOf("second").PathProgress, 1e-9);
            Assert.AreEqual(0, live.Combat.Game.LeakedEnemies,
                "The synthetic route endpoint is not an observed WC3 escape.");
            Assert.AreEqual(0, driver.Advance(live.Combat, speed, 0).Count);
        }

        [Test]
        public void OpeningCameraFramesOriginalWorldCoordinatesWithoutChangingThem()
        {
            var points = new List<Vector2> {
                new Vector2(-3456, -9696), new Vector2(-2100, -8550) };
            Vector2 center;
            float halfHeight;
            Assert.IsTrue(MobileBattlefieldCameraFraming.TryFrame(
                points, 16f / 9f, out center, out halfHeight));
            Assert.AreEqual(-2778, center.x, .1f);
            Assert.AreEqual(-9123, center.y, .1f);
            Assert.GreaterOrEqual(halfHeight, 420f);
            Assert.AreEqual(-3456, points[0].x);
            Assert.IsFalse(MobileBattlefieldCameraFraming.TryFrame(
                new List<Vector2>(), 1f, out center, out halfHeight));
        }

        [Test]
        public void OfflinePauseSeparatesManualChoiceFromAppFocusAndBackground()
        {
            var gate = new OfflineSessionRunGate();
            Assert.IsTrue(gate.CanAdvance);
            gate.SetManualPause(true);
            Assert.IsFalse(gate.CanAdvance);
            gate.SetApplicationPause(true);
            gate.SetManualPause(false);
            Assert.IsFalse(gate.CanAdvance);
            gate.SetApplicationPause(false);
            gate.SetFocus(false);
            Assert.IsFalse(gate.CanAdvance);
            gate.SetFocus(true);
            Assert.IsTrue(gate.CanAdvance);
        }

        [Test]
        public void BattlefieldPanAndAnchorZoomOnlyChangePresentationCoordinates()
        {
            Vector2 result;
            Assert.IsTrue(MobileBattlefieldCameraNavigation.TryPan(
                new Vector2(100, 200), new Vector2(100, -50), 500f,
                1000f, out result));
            Assert.AreEqual(0f, result.x, .001f);
            Assert.AreEqual(250f, result.y, .001f);

            float size;
            Assert.IsTrue(MobileBattlefieldCameraNavigation.TryZoom(
                new Vector2(100, 200), 500f, new Vector2(100, 50),
                1000f, .5f, 50f, 1200f, out result, out size));
            Assert.AreEqual(250f, size, .001f);
            Assert.AreEqual(150f, result.x, .001f);
            Assert.AreEqual(225f, result.y, .001f);
            Assert.IsFalse(MobileBattlefieldCameraNavigation.TryPan(
                Vector2.zero, Vector2.zero, 500f, 0f, out result));
            Assert.IsFalse(MobileBattlefieldCameraNavigation.TryZoom(
                Vector2.zero, 500f, Vector2.zero, 1000f, float.NaN,
                50f, 1200f, out result, out size));
        }

        [Test]
        public void OfflineErrorLogIsBoundedAndExportRemovesEmbeddedNewlines()
        {
            var diagnostics = new OfflineSessionDiagnostics();
            for (int index = 0; index < 70; index++)
                diagnostics.Record(1, index, "전투 기록", "항목 " + index);
            Assert.AreEqual(64, diagnostics.Count);
            string export = diagnostics.Export();
            StringAssert.Contains("nightmare-otherworld", export);
            StringAssert.DoesNotContain("항목 0\n", export);
            StringAssert.Contains("항목 69", export);
            diagnostics.Record(2, 1.5, "예외", "첫 줄\n둘째 줄");
            StringAssert.Contains("첫 줄 둘째 줄", diagnostics.Export());
        }

        [Test]
        public void AllMobileUnitsUseUnlimitedBasicAttackReachWithinAssignedBattlefield()
        {
            var live = Create();
            var field = typeof(CharacterCombatRuntime).GetField(
                "attackPipeline", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            var pipeline = (CombatAttackPipeline)field.GetValue(live.Combat);
            Assert.AreEqual(CombatAttackReachMode.UnlimitedWithinAssignedBattlefield,
                pipeline.ReachMode);
            Assert.IsTrue(pipeline.CanReach("unregistered-fixture", 1000000000000d));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                pipeline.CanReach("unregistered-fixture", -1d));
        }
    }
}
