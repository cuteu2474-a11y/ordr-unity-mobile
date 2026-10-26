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
    public sealed class OriginalJassLiveCombatFusionWorldTests
    {
        private sealed class Objects : IOriginalJassLiveObjectReader
        {
            public readonly Dictionary<long, string> Rawcodes =
                new Dictionary<long, string>();

            public bool TryReadOriginalRawcode(long id, out string rawcode)
            { return Rawcodes.TryGetValue(id, out rawcode); }
        }

        private sealed class Cast : IOriginalJassFusionCastPolicy
        {
            public bool TryChooseCastTarget(string ability,
                IReadOnlyList<long> materials, out long id, out string reason)
            {
                id = materials.Count == 0 ? 0 : materials[0];
                reason = id <= 0 ? "원본 대상 없음" : null;
                return id > 0;
            }
        }

        private sealed class NativeEffects : IOriginalJassLiveEffectsFactory
        {
            public bool ReturnNull;
            public int Begun;
            public int Disposed;
            public int Committed;
            public int StageCalls;

            public IOriginalJassLiveEffectsTransaction BeginExclusiveNativeFusion(
                CharacterCombatRuntime combat, SharedEconomySession economy)
            {
                Begun++;
                if (ReturnNull) return null;
                return new Native(this);
            }

            private sealed class Native : IOriginalJassLiveEffectsTransaction
            {
                private readonly NativeEffects parent;
                private bool disposed;

                public Native(NativeEffects parent) { this.parent = parent; }

                public bool TryStageOriginalFusion(OriginalJassSimpleFusionCommand command,
                    out long created, out string reason)
                {
                    // No simulated world mutation may masquerade as CHu/CHx.
                    parent.StageCalls++;
                    created = 0;
                    reason = "실제 원본 CHu/CHx 네이티브 생성이 미연결";
                    return false;
                }

                public void Commit()
                {
                    parent.Committed++;
                    throw new InvalidOperationException(
                        "미검증 네이티브 월드에서 커밋 불가");
                }

                public void Dispose()
                {
                    if (disposed) return;
                    disposed = true;
                    parent.Disposed++;
                }
            }
        }

        private sealed class Fixture
        {
            public CharacterCombatRuntime Combat;
            public SharedEconomySession Economy;
            public OriginalJassLiveCombatFusionWorld World;
            public Objects Objects;
            public NativeEffects Native;
            public long Unit;
        }

        private static Fixture Create()
        {
            var catalogAsset = Resources.Load<TextAsset>("OrdrData/units");
            Assert.IsNotNull(catalogAsset);
            var units = CharacterUnitCatalog.FromJson(catalogAsset.text);
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var formulaUnavailable = new RejectUnverifiedDamageFormula();
            var combat = new CharacterCombatRuntime(units, skills,
                formulaUnavailable, formulaUnavailable,
                new SeededNonOriginalProcRandom(13));
            long id = combat.SpawnCharacter("C-01", 12.0, 24.0);
            var wallet = new EconomyWallet();
            wallet.Grant(new ResourceBundle().Add("gold", 60).Add("wood", 5));
            var economy = new SharedEconomySession(wallet, combat.Roster);
            var objects = new Objects();
            objects.Rawcodes.Add(id, "h003");
            var native = new NativeEffects();
            return new Fixture {
                Combat = combat,
                Economy = economy,
                Objects = objects,
                Native = native,
                Unit = id,
                World = new OriginalJassLiveCombatFusionWorld(combat, economy,
                    objects, new Cast(), native)
            };
        }

        [Test]
        public void ReadsActualCharacterInventoryAndSharedWalletWithoutFabricatedUnits()
        {
            var fixture = Create();
            int gold, lumber;
            Assert.IsTrue(fixture.World.TryReadBalances(out gold, out lumber));
            Assert.AreEqual(60, gold);
            Assert.AreEqual(5, lumber);
            using (var lockScope = fixture.World.BeginExclusiveFusion())
            {
                Assert.IsNotNull(lockScope);
                var actual = lockScope.OwnedUnits();
                Assert.AreEqual(fixture.Combat.CharacterCount, actual.Count);
                Assert.AreEqual(1, actual.Count);
                Assert.AreEqual(fixture.Unit, actual[0].InstanceId);
                Assert.AreEqual("C-01", actual[0].Unit.Id);
                string rawcode;
                Assert.IsTrue(lockScope.TryReadOriginalUnitRawcode(
                    fixture.Unit, out rawcode));
                Assert.AreEqual("h003", rawcode);
                Assert.IsFalse(lockScope.TryReadOriginalUnitRawcode(
                    999999, out rawcode));
            }
            Assert.AreEqual(1, fixture.Native.Disposed);
            Assert.AreEqual(0, fixture.Native.Committed);
            Assert.AreEqual(1, fixture.Combat.CharacterCount);
        }

        [Test]
        public void NestedTransactionAndMissingNativeLockNeverSpendResources()
        {
            var fixture = Create();
            using (var locked = fixture.World.BeginExclusiveFusion())
            {
                Assert.Throws<InvalidOperationException>(() =>
                    fixture.World.BeginExclusiveFusion());
            }
            Assert.AreEqual(1, fixture.Native.Disposed);
            fixture.Native.ReturnNull = true;
            Assert.IsNull(fixture.World.BeginExclusiveFusion());
            fixture.Native.ReturnNull = false;
            using (var again = fixture.World.BeginExclusiveFusion())
                Assert.IsNotNull(again);
            Assert.AreEqual(2, fixture.Native.Disposed);
            Assert.AreEqual(5, fixture.Economy.Wallet.Balance("wood"));
            Assert.AreEqual(1, fixture.Combat.CharacterCount);
        }

        [Test]
        public void WalletChangedAfterNativeLockInvalidatesOriginalTargetEvidence()
        {
            var fixture = Create();
            using (var locked = fixture.World.BeginExclusiveFusion())
            {
                fixture.Economy.Wallet.Grant(
                    new ResourceBundle().Add("gold", 1));
                long selected;
                string reason;
                Assert.IsFalse(locked.TryChooseCastTarget("ET01",
                    new long[] { fixture.Unit }, out selected, out reason));
                Assert.AreEqual(0, selected);
                Assert.IsTrue(reason.Contains("변경"));
            }
            Assert.AreEqual(0, fixture.Native.Committed);
            Assert.AreEqual(0, fixture.Native.StageCalls);
            Assert.AreEqual(1, fixture.Combat.CharacterCount);
        }

        [Test]
        public void LiveRosterMovementDuringLockedFusionIsNotStaleSafe()
        {
            var fixture = Create();
            using (var locked = fixture.World.BeginExclusiveFusion())
            {
                fixture.Combat.MoveCharacter(fixture.Unit, 45, 25);
                long chosen;
                string reason;
                Assert.IsFalse(locked.TryChooseCastTarget(
                    "ET01", new long[] { fixture.Unit }, out chosen, out reason));
                Assert.AreEqual(0, chosen);
                Assert.IsTrue(reason.Contains("변경"));
            }
            Assert.AreEqual(1, fixture.Native.Disposed);
            Assert.AreEqual(0, fixture.Native.Committed);
        }

        [Test]
        public void RawcodesCannotBeInferredFromKoreanNameOrOwnedCatalogId()
        {
            var fixture = Create();
            using (var locked = fixture.World.BeginExclusiveFusion())
            {
                fixture.Objects.Rawcodes.Clear();
                string rawcode;
                Assert.IsFalse(locked.TryReadOriginalUnitRawcode(
                    fixture.Unit, out rawcode));
                // Existence of a live C-01 Luffy is not proof of the W3X h003
                // object. The external original-object reader is authoritative.
                Assert.IsFalse(locked.TryReadOriginalUnitRawcode(
                    fixture.Unit + 1000, out rawcode));
            }
            Assert.AreEqual(0, fixture.Native.Committed);
            Assert.AreEqual(5, fixture.Economy.Wallet.Balance("wood"));
        }
    }
}
