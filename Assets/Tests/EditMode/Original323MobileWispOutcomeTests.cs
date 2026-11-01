using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323MobileWispOutcomeTests
    {
        private sealed class Script : IOriginalWispRandom
        {
            public int Roll;
            public int Calls;
            public Script(int roll) { Roll = roll; }
            public int NextInclusive(int min, int max)
            {
                Calls++;
                Assert.That(Roll, Is.InRange(min, max));
                return Roll;
            }
        }

        private sealed class Native : IOriginal323NativeUnitPoolRandom
        {
            public int Index;
            public int Calls;
            public Native(int index) { Index = index; }
            public int NextInclusive(int min, int max)
            {
                Calls++;
                Assert.That(Index, Is.InRange(min, max));
                return Index;
            }
        }

        private static string Read(string name) => File.ReadAllText(
            Path.Combine(Application.dataPath, "..", "data", name));

        private sealed class World
        {
            public CharacterCombatRuntime Combat;
            public EconomyWallet Wallet;
            public Original323WispAssignedRosterMap Mapping;
        }

        private static World Create()
        {
            var units = CharacterUnitCatalog.FromJson(Read("units.json"));
            var objects = OriginalMapBaseStatCatalog.FromJson(
                Read("original-2323C-map-base-stats.json"), units);
            var map = Original323WispAssignedRosterMap.FromSourceJson(
                Read("original-2323C-wisp-character-identity-assignments.json"),
                Read("original-2323C-wisp-pools.json"), units, objects);
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var noFormula = new RejectUnverifiedDamageFormula();
            var combat = new CharacterCombatRuntime(units, skills,
                noFormula, noFormula, new SeededNonOriginalProcRandom(29),
                new UnitRoster());
            combat.Start();
            combat.BeginWave(1);
            return new World { Combat = combat, Wallet = new EconomyWallet(), Mapping = map };
        }

        [Test]
        public void FourAutomaticWispTiersSpawnRealRosterUnits()
        {
            var live = Create();
            var script = new Script(99);
            var native = new Native(0);
            var roller = new Original323MobileWispOutcomeRoller(
                Read("original-2323C-wisp-pools.json"), script, native);
            var deferred = new Original323WispDeferredAbilityRegistry(live.Combat);
            var rewardWorld = new OriginalWispCombatRewardWorld(
                live.Combat, live.Mapping, roller, roller, deferred);
            using (var service = new MobileWispService(live.Wallet, roller, rewardWorld))
            {
                foreach (string id in new[] { WispResourceIds.Uncommon,
                    WispResourceIds.Special, WispResourceIds.Rare,
                    WispResourceIds.Legendary })
                    service.Grant(id);

                Assert.AreEqual(4, live.Combat.CharacterCount);
                Assert.AreEqual(0, service.Balance(WispResourceIds.Uncommon));
                Assert.AreEqual(0, service.Balance(WispResourceIds.Special));
                Assert.AreEqual(0, service.Balance(WispResourceIds.Rare));
                Assert.AreEqual(0, service.Balance(WispResourceIds.Legendary));
                Assert.AreEqual(1, deferred.PendingCount,
                    "A0YE must be recorded as a pending native effect, not silently dropped.");
                Assert.AreEqual(1, script.Calls, "Only Rare uses the 99:1 branch roll.");
                Assert.AreEqual(4, native.Calls);
            }
        }

        [Test]
        public void RareOnePercentGaimonDoesNotCreateA0yeReceipt()
        {
            var live = Create();
            var script = new Script(100);
            var native = new Native(7);
            var roller = new Original323MobileWispOutcomeRoller(
                Read("original-2323C-wisp-pools.json"), script, native);
            var deferred = new Original323WispDeferredAbilityRegistry(live.Combat);
            using (var service = new MobileWispService(live.Wallet, roller,
                new OriginalWispCombatRewardWorld(
                    live.Combat, live.Mapping, roller, roller, deferred)))
            {
                service.Grant(WispResourceIds.Rare);
                Assert.AreEqual(0, deferred.PendingCount);
                Assert.AreEqual(1, live.Combat.CharacterCount);
                Assert.AreEqual("X-08", live.Combat.MobileUnitSnapshot()[0].Unit.Id);
                Assert.AreEqual(0, service.Balance(WispResourceIds.Rare));
            }
        }

        [Test]
        public void UncommittedRareShellRemovesSpawnAndDeferredAbility()
        {
            var live = Create();
            var roller = new Original323MobileWispOutcomeRoller(
                Read("original-2323C-wisp-pools.json"), new Script(99), new Native(0));
            var deferred = new Original323WispDeferredAbilityRegistry(live.Combat);
            var world = new OriginalWispCombatRewardWorld(
                live.Combat, live.Mapping, roller, roller, deferred);
            using (var transaction = world.Begin())
            {
                transaction.Apply(new WispReward(WispResourceIds.Rare, "h09X", "A0YE"));
                Assert.AreEqual(1, live.Combat.CharacterCount);
                Assert.AreEqual(1, deferred.PendingCount);
            }
            Assert.AreEqual(0, live.Combat.CharacterCount);
            Assert.AreEqual(0, deferred.PendingCount);
        }

        [Test]
        public void WrongTierOrMissingRareBonusCannotSpawnAUnit()
        {
            var live = Create();
            var roller = new Original323MobileWispOutcomeRoller(
                Read("original-2323C-wisp-pools.json"), new Script(99), new Native(0));
            var deferred = new Original323WispDeferredAbilityRegistry(live.Combat);
            var world = new OriginalWispCombatRewardWorld(
                live.Combat, live.Mapping, roller, roller, deferred);
            using (var transaction = world.Begin())
                Assert.Throws<InvalidOperationException>(() =>
                    transaction.Apply(new WispReward(WispResourceIds.Rare, "h03K")));
            using (var transaction = world.Begin())
                Assert.Throws<InvalidOperationException>(() =>
                    transaction.Apply(new WispReward(WispResourceIds.Rare, "h09X")));
            Assert.AreEqual(0, live.Combat.CharacterCount);
            Assert.AreEqual(0, deferred.PendingCount);
        }

        [Test]
        public void FourTierRollerCannotPretendToHaveNativeA0ye()
        {
            var roller = new Original323MobileWispOutcomeRoller(
                Read("original-2323C-wisp-pools.json"), new Script(99), new Native(0));
            Assert.IsTrue(roller.IsVerified(WispResourceIds.Legendary));
            Assert.IsTrue(roller.IsVerified(WispResourceIds.Rare));
            Assert.IsFalse(roller.TryApplyOriginalAbility(1, "A0YE"));
            Assert.IsTrue(roller.TryGetPosition(WispResourceIds.Rare, out double x, out double y));
            Assert.AreEqual(-3776d, x);
            Assert.AreEqual(4480d, y);
        }
    }
}
