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
            IOriginalJassLiveObjectReader
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
                formula, formula, new SeededNonOriginalProcRandom(19));
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

        private static MobileFusionPreview FindEt01(Fixture fixture)
        {
            foreach (var item in fixture.Port.AvailableRecipes(
                fixture.Combat.MobileUnitSnapshot()))
                if (item.RecipeId == "ET01") return item;
            Assert.Fail("비비 영원의 원본 등록식 ET01이 검색되지 않았습니다.");
            return null;
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
