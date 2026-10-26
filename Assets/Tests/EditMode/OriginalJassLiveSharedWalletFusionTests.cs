using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalJassLiveSharedWalletFusionTests
    {
        private sealed class Et01ObjectMap : IVerifiedUnitRawcodeMap,
            IOriginalJassVerifiedFusionSwapRules
        {
            public static readonly string[] Rawcodes = {
                "h05F", "h05W", "h01X", "h01S", "h00S", "h01G"
            };
            public static readonly string[] CatalogIds = {
                "LI-09", "T-02", "R-06", "R-17", "S-04", "S-32"
            };
            public readonly Dictionary<long, string> Actual =
                new Dictionary<long, string>();

            public bool TryResolveUnit(string rawcode, out string unitId)
            {
                if (rawcode == "h057")
                { unitId = "E-06"; return true; }
                for (int i = 0; i < Rawcodes.Length; ++i)
                    if (Rawcodes[i] == rawcode)
                    { unitId = CatalogIds[i]; return true; }
                unitId = null;
                return false;
            }

            public bool TryReadOriginalRawcode(long instanceId, out string rawcode)
            { return Actual.TryGetValue(instanceId, out rawcode); }

            public bool ApproveOriginalFollowUps;
            public bool RejectBinding;

            // TEST ONLY: the production implementation MUST be independently
            // checked against the actual original CHu/CHx event/placement.
            public bool TryResolveCompleteOriginalFusion(
                OriginalJassSimpleFusionCommand order,
                IReadOnlyList<MobileUnitSnapshot> inputs, out double x,
                out double y, out string reason)
            {
                x = 1000; y = 2000;
                bool valid = ApproveOriginalFollowUps &&
                    order.AbilityRawcode == "ET01" &&
                    order.ResultRawcode == "h057" &&
                    order.ResultUnitId == "E-06" &&
                    order.Lumber == 5 && order.Gold == 0 &&
                    inputs.Count == 6;
                reason = valid ? null :
                    "TEST_ONLY: native CHu/CHx placement/follow-ups not approved";
                return valid;
            }

            public bool TryBindStagedResult(long id,
                OriginalJassSimpleFusionCommand order, out string reason)
            {
                if (RejectBinding)
                {
                    reason = "TEST_ONLY: result identity rejected";
                    return false;
                }
                if (Actual.ContainsKey(id))
                {
                    reason = "이미 사용 중인 원본 객체 인스턴스 ID";
                    return false;
                }
                Actual.Add(id, order.ResultRawcode);
                reason = null;
                return true;
            }

            public void DiscardStagedResult(long id)
            { Actual.Remove(id); }
        }

        private sealed class NoBasicDamage : IOriginalDamageResolver
        {
            public long ResolveBasicDamage(BasicDamageRequest request,
                CombatEnemy target)
            { throw new NotSupportedException("TEST_ONLY basic damage unavailable"); }
        }

        private sealed class TestOriginalCast : IOriginalJassFusionCastPolicy
        {
            public bool TryChooseCastTarget(string rawcode,
                IReadOnlyList<long> ids, out long selected, out string reason)
            {
                selected = ids.Count > 0 ? ids[0] : 0;
                reason = selected <= 0 ? "검증용 대상 없음" : null;
                return selected > 0;
            }
        }

        private sealed class FailingNativeUnits : IOriginalJassReversibleUnitFusionWorld
        {
            public int BeginCount;
            public int StageCount;
            public int Rollbacks;
            public int Commits;
            public long WoodObservedDuringStage;
            public bool ThrowsDuringStage;

            public IOriginalJassReversibleUnitFusionTransaction BeginExclusiveUnitFusion(
                CharacterCombatRuntime combat, SharedEconomySession economy)
            {
                BeginCount++;
                return new Locked(this, economy.Wallet);
            }

            private sealed class Locked : IOriginalJassReversibleUnitFusionTransaction
            {
                private readonly FailingNativeUnits parent;
                private readonly EconomyWallet wallet;
                private bool disposed;
                private bool committed;

                public Locked(FailingNativeUnits source, EconomyWallet real)
                {
                    parent = source;
                    wallet = real;
                }

                public bool TryStageOriginalUnitFusion(
                    OriginalJassSimpleFusionCommand command,
                    out long created, out string reason)
                {
                    parent.StageCount++;
                    parent.WoodObservedDuringStage = wallet.Balance("wood");
                    created = 0;
                    reason = "TEST_ONLY native unit creation deliberately refused";
                    if (parent.ThrowsDuringStage)
                        throw new InvalidOperationException("TEST_ONLY native callback exception");
                    return false;
                }

                public void Commit()
                {
                    committed = true;
                    parent.Commits++;
                    throw new InvalidOperationException("never authorize test native commit");
                }

                public void Dispose()
                {
                    if (disposed) return;
                    disposed = true;
                    if (!committed) parent.Rollbacks++;
                }
            }
        }

        private sealed class Fixture
        {
            public CharacterCombatRuntime Combat;
            public EconomyWallet Wallet;
            public OriginalJassFusionPort Port;
            public Et01ObjectMap Map;
            public FailingNativeUnits Native;
            public long[] OriginalIds;
            public OriginalJassLiveCombatFusionWorld LiveWorld;
        }

        private static Fixture Create()
        {
            var unitAsset = Resources.Load<TextAsset>("OrdrData/units");
            var jassAsset = Resources.Load<TextAsset>(
                "OrdrData/original-jass-fusion-registry");
            var labelAsset = Resources.Load<TextAsset>(
                "OrdrData/original-unit-rawcode-label-links");
            Assert.IsNotNull(unitAsset);
            Assert.IsNotNull(jassAsset);
            Assert.IsNotNull(labelAsset);
            var units = CharacterUnitCatalog.FromJson(unitAsset.text);
            var original = OriginalJassFusionCatalog.FromJson(jassAsset.text);
            var labels = OriginalUnitRawcodeLabelCatalog.FromJson(
                labelAsset.text, units, original);
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var formula = new RejectUnverifiedDamageFormula();
            var combat = new CharacterCombatRuntime(units, skills,
                new NoBasicDamage(), formula, new SeededNonOriginalProcRandom(19));
            var map = new Et01ObjectMap();
            var ids = new long[6];
            for (int i = 0; i < 6; ++i)
            {
                ids[i] = combat.SpawnCharacter(
                    Et01ObjectMap.CatalogIds[i], 100 + i, 200);
                map.Actual.Add(ids[i], Et01ObjectMap.Rawcodes[i]);
            }
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 60).Add("wood", 5));
            var economy = new SharedEconomySession(wallet, combat.Roster);
            var native = new FailingNativeUnits();
            var world = new OriginalJassLiveCombatFusionWorld(
                combat, economy, map, new TestOriginalCast(),
                new OriginalJassSharedWalletFusionEffects(native));
            var planner = new OriginalJassFusionPlanner(
                original, labels, id => combat.IsStoryDeployed(id),
                new OriginalJassNoSpecialConditions(), units);
            planner.SetVerifiedObjectMap(map);
            var factory = new OriginalJassSimpleOrderedSessionFactory(
                original, world);
            var adapter = new OriginalJassFusionOrderedWorldAdapter(factory);
            var coordinator = new OriginalJassFusionAtomicCoordinator(
                planner, adapter);
            var port = new OriginalJassFusionPort(
                planner, () => combat.MobileUnitSnapshot(), world,
                coordinator, world);
            return new Fixture {
                Combat = combat, Wallet = wallet, Port = port,
                Map = map, Native = native, OriginalIds = ids
            };
        }

        private static Fixture CreateWithRealCombatUnitSwap()
        {
            var fixture = Create();
            var sourceAsset = Resources.Load<TextAsset>(
                "OrdrData/original-jass-fusion-registry");
            var labelsAsset = Resources.Load<TextAsset>(
                "OrdrData/original-unit-rawcode-label-links");
            var unitAsset = Resources.Load<TextAsset>("OrdrData/units");
            var source = OriginalJassFusionCatalog.FromJson(sourceAsset.text);
            var units = CharacterUnitCatalog.FromJson(unitAsset.text);
            var labels = OriginalUnitRawcodeLabelCatalog.FromJson(
                labelsAsset.text, units, source);
            fixture.Map.ApproveOriginalFollowUps = true;

            var economy = new SharedEconomySession(
                fixture.Wallet, fixture.Combat.Roster);
            var real = new OriginalJassLiveCombatFusionWorld(
                fixture.Combat, economy, fixture.Map, new TestOriginalCast(),
                new OriginalJassSharedWalletFusionEffects(
                    new OriginalJassCombatUnitFusionWorld(fixture.Map)));
            fixture.LiveWorld = real;
            var planner = new OriginalJassFusionPlanner(source, labels,
                id => fixture.Combat.IsStoryDeployed(id),
                new OriginalJassNoSpecialConditions(), units);
            planner.SetVerifiedObjectMap(fixture.Map);
            var originalCoordinator = new OriginalJassFusionAtomicCoordinator(
                planner, new OriginalJassFusionOrderedWorldAdapter(
                    new OriginalJassSimpleOrderedSessionFactory(source, real)));
            fixture.Port = new OriginalJassFusionPort(
                planner, () => fixture.Combat.MobileUnitSnapshot(),
                real, originalCoordinator, real);
            return fixture;
        }

        [Test]
        public void MissionFusionNotificationFollowsCommitAndReleasedWorldLock()
        {
            var fixture = CreateWithRealCombatUnitSwap();
            int observed = 0;
            Exception notificationFailure = null;
            fixture.LiveWorld.PostCommitObserverFailed += error => notificationFailure = error;
            fixture.LiveWorld.FusionCommitted += (id, catalog, rawcode) => {
                observed++;
                Assert.AreEqual("E-06", catalog);
                Assert.AreEqual("h057", rawcode);
                Assert.AreEqual(catalog, fixture.Combat.Roster.Get(id).UnitId);
                Assert.AreEqual(0, fixture.Wallet.Balance("wood"));
                using (var read = fixture.LiveWorld.BeginExclusiveFusion())
                    Assert.IsNotNull(read, "notification must follow lock release");
            };
            var offer = FindEt01(fixture);
            Assert.AreEqual(ManualActionState.Succeeded,
                fixture.Port.ConfirmFusion("ET01", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(1, observed);
            Assert.IsNull(notificationFailure);
            Assert.AreEqual(ManualActionState.Rejected,
                fixture.Port.ConfirmFusion("ET01", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(1, observed);
        }

        [Test]
        public void RejectedNativeFusionNeverRaisesMissionNotification()
        {
            var fixture = CreateWithRealCombatUnitSwap();
            fixture.Map.RejectBinding = true;
            int observed = 0;
            fixture.LiveWorld.FusionCommitted += (_, __, ___) => observed++;
            var offer = FindEt01(fixture);
            Assert.AreEqual(ManualActionState.Rejected,
                fixture.Port.ConfirmFusion("ET01", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(0, observed);
            Assert.AreEqual(5, fixture.Wallet.Balance("wood"));
            Assert.AreEqual(6, fixture.Combat.CharacterCount);
        }

        [Test]
        public void MissionObserverFailureCannotRejectCommittedFusion()
        {
            var fixture = CreateWithRealCombatUnitSwap();
            int laterObserver = 0;
            fixture.LiveWorld.FusionCommitted += (_, __, ___) => {
                throw new InvalidOperationException("TEST_ONLY mission observer failure");
            };
            fixture.LiveWorld.FusionCommitted += (_, __, ___) => laterObserver++;
            fixture.LiveWorld.PostCommitObserverFailed += _ => {
                throw new InvalidOperationException("TEST_ONLY diagnostic failure");
            };
            var offer = FindEt01(fixture);
            Assert.AreEqual(ManualActionState.Succeeded,
                fixture.Port.ConfirmFusion("ET01", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(1, laterObserver);
            Assert.AreEqual(1, fixture.Combat.CharacterCount);
            Assert.AreEqual(0, fixture.Wallet.Balance("wood"));
        }

        [Test]
        public void Et01ProducesRealCombatVivianAndRemovesSixActualUnitsAtomically()
        {
            var fixture = CreateWithRealCombatUnitSwap();
            var offer = FindEt01(fixture);
            Assert.IsTrue(offer.IsReady);
            Assert.AreEqual(ManualActionState.Succeeded,
                fixture.Port.ConfirmFusion(
                    "ET01", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(1, fixture.Combat.CharacterCount);
            Assert.AreEqual(1, fixture.Combat.Roster.Count);
            Assert.AreEqual(1, fixture.Combat.MobileUnitSnapshot().Count);
            var produced = fixture.Combat.MobileUnitSnapshot()[0];
            Assert.AreEqual("E-06", produced.Unit.Id);
            Assert.AreEqual("h057", fixture.Map.Actual[produced.InstanceId]);
            Assert.AreEqual(1000, fixture.Combat.Roster.Get(produced.InstanceId).X);
            Assert.AreEqual(2000, fixture.Combat.Roster.Get(produced.InstanceId).Y);
            Assert.AreEqual(0, fixture.Wallet.Balance("wood"));
            Assert.AreEqual(60, fixture.Wallet.Balance("gold"));
            foreach (long id in fixture.OriginalIds)
            {
                Assert.Throws<KeyNotFoundException>(() => fixture.Combat.Roster.Get(id));
            }
            // Old 6-instance confirmation cannot create a second result.
            Assert.AreEqual(ManualActionState.Rejected,
                fixture.Port.ConfirmFusion(
                    "ET01", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(1, fixture.Combat.CharacterCount);
        }

        [Test]
        public void ResultIdentityRejectedAfterSpawnRollsBackActualCombatActorsAndWallet()
        {
            var fixture = CreateWithRealCombatUnitSwap();
            var offer = FindEt01(fixture);
            fixture.Map.RejectBinding = true;
            double originalTime = fixture.Combat.Time;
            Assert.AreEqual(ManualActionState.Rejected,
                fixture.Port.ConfirmFusion(
                    "ET01", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(6, fixture.Combat.CharacterCount);
            Assert.AreEqual(6, fixture.Combat.Roster.Count);
            Assert.AreEqual(6, fixture.Combat.MobileUnitSnapshot().Count);
            Assert.AreEqual(originalTime, fixture.Combat.Time);
            Assert.AreEqual(5, fixture.Wallet.Balance("wood"));
            Assert.AreEqual(60, fixture.Wallet.Balance("gold"));
            foreach (long id in fixture.OriginalIds)
                Assert.IsTrue(fixture.Map.Actual.ContainsKey(id));
            Assert.AreEqual(6, fixture.Map.Actual.Count);
        }

        [Test]
        public void MissingCompleteOriginalNativeWitnessFailsWithoutUnitSwap()
        {
            var fixture = CreateWithRealCombatUnitSwap();
            var offer = FindEt01(fixture);
            fixture.Map.ApproveOriginalFollowUps = false;
            Assert.AreEqual(ManualActionState.Rejected,
                fixture.Port.ConfirmFusion(
                    "ET01", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(6, fixture.Combat.CharacterCount);
            Assert.AreEqual(6, fixture.Combat.MobileUnitSnapshot().Count);
            Assert.AreEqual(5, fixture.Wallet.Balance("wood"));
            Assert.AreEqual(6, fixture.Map.Actual.Count);
        }

        private static MobileFusionPreview FindEt01(Fixture fixture)
        {
            foreach (var item in fixture.Port.AvailableRecipes(
                fixture.Combat.MobileUnitSnapshot()))
                if (item.RecipeId == "ET01") return item;
            Assert.Fail("비비 영원의 원본 등록식 ET01이 검색되지 않았습니다.");
            return null;
        }

        [Test]
        public void StagedSwapKeepsInputsAliveUntilCommitAndRestoresOnAbort()
        {
            var fixture = CreateWithRealCombatUnitSwap();
            using (var pending = fixture.Combat.BeginOriginalFusionUnitSwap(
                fixture.OriginalIds, "E-06", 1000, 2000))
            {
                Assert.AreEqual(7, fixture.Combat.CharacterCount);
                Assert.AreEqual(1, fixture.Combat.MobileUnitSnapshot().Count);
                Assert.AreEqual("E-06",
                    fixture.Combat.MobileUnitSnapshot()[0].Unit.Id);
                // Source instances are still real, with their preexisting
                // skill cooldowns and runtime effects not torn down.
                foreach (long id in fixture.OriginalIds)
                    Assert.AreEqual(200, fixture.Combat.Roster.Get(id).Y);
                Assert.Throws<InvalidOperationException>(() =>
                    fixture.Combat.MoveCharacter(fixture.OriginalIds[0], 600, 700));
                Assert.Throws<InvalidOperationException>(() =>
                    fixture.Combat.RemoveCharacter(fixture.OriginalIds[0]));
                Assert.Throws<InvalidOperationException>(() =>
                    fixture.Combat.SpawnCharacter("C-01", 2, 2));
            }
            Assert.AreEqual(6, fixture.Combat.CharacterCount);
            Assert.AreEqual(6, fixture.Combat.Roster.Count);
            Assert.AreEqual(6, fixture.Combat.MobileUnitSnapshot().Count);
        }

        [Test]
        public void StagedFusionCannotCommitAfterSourceRosterMovedUnexpectedly()
        {
            var fixture = CreateWithRealCombatUnitSwap();
            using (var pending = fixture.Combat.BeginOriginalFusionUnitSwap(
                fixture.OriginalIds, "E-06", 1000, 2000))
            {
                // This low-level roster modification intentionally bypasses
                // normal live-combat mutation guards. The final ticket still
                // detects it and refuses to destroy source instances.
                fixture.Combat.Roster.Move(fixture.OriginalIds[0], 400, 500);
                Assert.Throws<InvalidOperationException>(() => pending.Commit());
            }
            Assert.AreEqual(6, fixture.Combat.CharacterCount);
            Assert.AreEqual(6, fixture.Combat.MobileUnitSnapshot().Count);
            Assert.AreEqual(5, fixture.Wallet.Balance("wood"));
        }

        [Test]
        public void StagedFusionDoesNotRunCombatOrLeakAnAbortedSpawnCue()
        {
            var fixture = CreateWithRealCombatUnitSwap();
            fixture.Combat.Start();
            fixture.Combat.BeginWave(1);
            fixture.Combat.Step(0); // drain the six existing spawn cues

            long sourceId = fixture.OriginalIds[0];
            long originalRevision = fixture.Combat.UnitStatusOf(sourceId).Revision;
            using (var pending = fixture.Combat.BeginOriginalFusionUnitSwap(
                fixture.OriginalIds, "E-06", 1000, 2000))
            {
                Assert.Throws<InvalidOperationException>(() =>
                    fixture.Combat.Step(0.25));
                Assert.AreEqual(0, fixture.Combat.PendingSimulationSeconds);
                Assert.Throws<InvalidOperationException>(() =>
                    fixture.Combat.ApplyVerifiedUnitEffect(sourceId,
                        new CombatEffect("blocked-fusion-effect",
                            CombatEffectType.MagicDamageAmplification,
                            CombatEffectOrigin.TriggeredSkill, 0.3, 1)));
                Assert.Throws<InvalidOperationException>(() =>
                    fixture.Combat.SpawnEnemy("forbidden", 100,
                        CharacterEnemyKind.Line, 0, 0, 0));
                Assert.AreEqual(originalRevision,
                    fixture.Combat.UnitStatusOf(sourceId).Revision);
                Assert.AreEqual(0, fixture.Combat.EnemyCount);
            }

            Assert.AreEqual(6, fixture.Combat.CharacterCount);
            foreach (var evt in fixture.Combat.Step(0))
                Assert.IsFalse(evt.Type == CharacterCombatEventType.UnitSpawned ||
                    evt.Type == CharacterCombatEventType.UnitPlaced,
                    "Uncommitted native fusion must never publish a spawn.");
        }

        [Test]
        public void AbortedFusionDoesNotReapplyOrRemoveExistingEnemyAuras()
        {
            var fixture = CreateWithRealCombatUnitSwap();
            fixture.Combat.Start();
            fixture.Combat.BeginWave(1);
            fixture.Combat.SpawnEnemy("aura-target", 1000,
                CharacterEnemyKind.Line, 0, 0, 0);
            fixture.Combat.AddVerifiedEnemyAura(new CombatAuraEmitter(
                fixture.OriginalIds[0], "original-aura-witness",
                CombatEffectType.ArmorReduction, 3, 500,
                new[] { CharacterEnemyKind.Line }, "source-observed test aura"));
            long before = fixture.Combat.StatusOf("aura-target").Revision;

            using (fixture.Combat.BeginOriginalFusionUnitSwap(
                fixture.OriginalIds, "E-06", 1000, 2000))
            {
                Assert.AreEqual(before,
                    fixture.Combat.StatusOf("aura-target").Revision);
            }
            Assert.AreEqual(before,
                fixture.Combat.StatusOf("aura-target").Revision);
            Assert.AreEqual(6, fixture.Combat.CharacterCount);
            Assert.AreEqual(5, fixture.Wallet.Balance("wood"));
        }

        [Test]
        public void CommittedOriginalFusionPublishesExactlyOneSpawnAndPlacement()
        {
            var fixture = CreateWithRealCombatUnitSwap();
            fixture.Combat.Start();
            fixture.Combat.BeginWave(1);
            fixture.Combat.Step(0); // drain the six initial spawn cues
            var offer = FindEt01(fixture);

            Assert.AreEqual(ManualActionState.Succeeded,
                fixture.Port.ConfirmFusion(
                    "ET01", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(1, fixture.Combat.CharacterCount);
            var produced = fixture.Combat.MobileUnitSnapshot()[0];
            Assert.AreEqual(1000, fixture.Combat.Roster.Get(produced.InstanceId).X);
            Assert.AreEqual(2000, fixture.Combat.Roster.Get(produced.InstanceId).Y);
            int spawned = 0, placed = 0;
            foreach (var evt in fixture.Combat.Step(0))
            {
                if (evt.Type == CharacterCombatEventType.UnitSpawned)
                    spawned++;
                if (evt.Type == CharacterCombatEventType.UnitPlaced)
                    placed++;
            }
            Assert.AreEqual(1, spawned);
            Assert.AreEqual(1, placed);
            Assert.AreEqual(0, fixture.Wallet.Balance("wood"));
        }

        [Test]
        public void NativeFusionFailureRestoresRealSharedWoodAndAllCombatUnits()
        {
            var fixture = Create();
            var offer = FindEt01(fixture);
            Assert.IsTrue(offer.IsReady);
            Assert.IsTrue(fixture.Port.CanCommitRecipe("ET01"));
            Assert.AreEqual(6, offer.SuggestedMaterialInstances.Count);
            Assert.AreEqual(ManualActionState.Rejected,
                fixture.Port.ConfirmFusion(
                    "ET01", offer.SuggestedMaterialInstances).State);
            // Gold 60 has no original ET01 cost; original wood 5 is
            // temporarily charged and MUST be refunded on native rejection.
            Assert.AreEqual(0, fixture.Native.WoodObservedDuringStage);
            Assert.AreEqual(5, fixture.Wallet.Balance("wood"));
            Assert.AreEqual(60, fixture.Wallet.Balance("gold"));
            Assert.AreEqual(6, fixture.Combat.CharacterCount);
            Assert.AreEqual(1, fixture.Native.StageCount);
            Assert.AreEqual(1, fixture.Native.Rollbacks);
            Assert.AreEqual(0, fixture.Native.Commits);
            foreach (var id in fixture.OriginalIds)
                Assert.IsTrue(fixture.Map.Actual.ContainsKey(id));
        }

        [Test]
        public void ExceptionAfterRealWalletDebitAlsoRefundsAndCannotCreatePhantomResult()
        {
            var fixture = Create();
            var offer = FindEt01(fixture);
            fixture.Native.ThrowsDuringStage = true;
            Assert.AreEqual(ManualActionState.Rejected,
                fixture.Port.ConfirmFusion(
                    "ET01", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(0, fixture.Native.WoodObservedDuringStage);
            Assert.AreEqual(5, fixture.Wallet.Balance("wood"));
            Assert.AreEqual(6, fixture.Combat.CharacterCount);
            Assert.AreEqual(1, fixture.Native.Rollbacks);
            Assert.AreEqual(0, fixture.Native.Commits);
        }

        [Test]
        public void OriginalRawcodeMismatchBlocksLiveWalletDebitBeforeNativeEffects()
        {
            var fixture = Create();
            var offer = FindEt01(fixture);
            fixture.Map.Actual[fixture.OriginalIds[0]] = "xxxx";
            Assert.AreEqual(ManualActionState.Rejected,
                fixture.Port.ConfirmFusion(
                    "ET01", offer.SuggestedMaterialInstances).State);
            Assert.AreEqual(0, fixture.Native.StageCount);
            Assert.AreEqual(5, fixture.Wallet.Balance("wood"));
            Assert.AreEqual(6, fixture.Combat.CharacterCount);
        }
    }
}
