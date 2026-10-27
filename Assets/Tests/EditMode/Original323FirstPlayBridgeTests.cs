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
            public CharacterCombatRuntime Combat;
            public bool TryObserve(string id, double time, out double x,
                out double y, out double progress, out string evidence)
            {
                if (DirtyPreflight) Combat.TryMoveEnemy(id, 999, 0, 0);
                x = ValidPosition ? 210 : double.NaN;
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
                "{\\"gameVersion\\":\\"S2 2.323[C]\\",\\"entries\\":[]}");
            var profiles = CharacterSkillCatalog.FromJson(
                "{\\"gameVersion\\":\\"S2 2.323[C]\\",\\"profiles\\":[]}");
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
