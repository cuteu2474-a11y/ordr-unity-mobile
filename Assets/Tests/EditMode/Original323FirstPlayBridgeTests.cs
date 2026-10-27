using System;
using System.Collections.Generic;
using System.IO;
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
    }
}
