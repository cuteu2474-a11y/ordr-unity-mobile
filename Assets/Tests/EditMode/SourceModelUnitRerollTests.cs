using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceModelUnitRerollTests
    {
        private sealed class Die : ICombatProcRandom { public int Next(int upper) => 0; }
        private sealed class Rolls : IOriginalWispRandom
        {
            private readonly Queue<int> values;
            public int Calls;
            public Rolls(params int[] rolls) { values = new Queue<int>(rolls); }
            public int NextInclusive(int min, int max)
            { Calls++; int value = values.Dequeue(); Assert.That(value, Is.InRange(min, max)); return value; }
        }
        private static string Data(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", name));
        private sealed class Setup
        {
            public readonly CharacterCombatRuntime Combat;
            public readonly EconomyWallet Wallet = new EconomyWallet();
            public readonly Original323WispDeferredAbilityRegistry Registry;
            public readonly Original323UnitGamblingWorld World;
            public Setup()
            {
                var units = CharacterUnitCatalog.FromJson(Data("units.json"));
                var map = OriginalMapBaseStatCatalog.FromJson(Data("original-2323C-map-base-stats.json"), units);
                var die = new Die(); var rules = new SourceModelBasicCombatRules(map, die);
                Combat = new CharacterCombatRuntime(units,
                    CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json")), rules, rules, die);
                Combat.Start(); Registry = new Original323WispDeferredAbilityRegistry(Combat);
                World = new Original323UnitGamblingWorld(Combat, units, map,
                    Original323WispAssignedRosterMap.FromSourceJson(Data("original-2323C-wisp-character-identity-assignments.json"),
                        Data("original-2323C-wisp-pools.json"), units, map),
                    new Original323CwrAcquisitionRoutes(OriginalJassFusionCatalog.FromJson(Data("original-jass-fusion-registry.json")), units, map), Registry);
            }
            public long Actor(string raw, string ability = null)
            {
                long id = Combat.SpawnVerifiedOriginalPositionCharacter(World.CatalogId(raw), -3500, 4300);
                if (ability != null) Assert.IsTrue(Registry.TryDeferOriginalAbility(id, ability));
                return id;
            }
            public SourceModelUnitReroll Service(Rolls rolls, Original323GamblingNavigation nav = Original323GamblingNavigation.None) =>
                new SourceModelUnitReroll(Combat, Wallet, World, Registry, rolls, () => true, () => nav);
        }
        [Test]
        public void RandomRerollRejectsSameTypeConsumesOnePhysicalTokenAndDoesNotReAddA0YZ()
        {
            var s = new Setup(); long actor = s.Actor("h070", "A0YZ"); long token = s.Actor("h06G");
            var rolls = new Rolls(2, 3); var service = s.Service(rolls);
            Assert.AreEqual(ManualActionState.Succeeded, service.TryReroll(actor).State);
            Assert.Throws<KeyNotFoundException>(() => s.Combat.Roster.Get(actor));
            Assert.Throws<KeyNotFoundException>(() => s.Combat.Roster.Get(token));
            Assert.AreEqual(2, rolls.Calls);
            long result = s.Combat.Roster.SelectedId.Value;
            var selected = s.Combat.Roster.Get(result);
            Assert.AreEqual(s.World.CatalogId("h06T"), selected.UnitId);
            Assert.AreEqual(-3500, selected.X); Assert.AreEqual(4300, selected.Y);
            Assert.IsFalse(service.HasRerollAbility(result)); Assert.AreEqual(0, s.Registry.PendingCount);
        }
        [Test]
        public void RareRerollUsesSharedLNAndTwoWoodWhileRiskHedgeWaivesOnlyWood()
        {
            foreach (var nav in new[] { Original323GamblingNavigation.None, Original323GamblingNavigation.RiskHedge })
            {
                var s = new Setup(); long actor = s.Actor("h09X", "A0YE");
                s.Wallet.Grant(new ResourceBundle().Add("wood", 2).Add(SourceModelGamblingNavigation.RareRerollsResource, 1));
                var service = s.Service(new Rolls(0, 1), nav);
                Assert.AreEqual(ManualActionState.Succeeded, service.TryReroll(actor).State);
                long result = s.Combat.Roster.SelectedId.Value;
                Assert.AreEqual(s.World.CatalogId("h01Q"), s.Combat.Roster.Get(result).UnitId);
                Assert.IsTrue(s.Registry.HasAbility(result, "A0YE"));
                Assert.AreEqual(0, s.Wallet.Balance(SourceModelGamblingNavigation.RareRerollsResource));
                Assert.AreEqual(nav == Original323GamblingNavigation.None ? 0 : 2, s.Wallet.Balance("wood"));
                Assert.IsFalse(service.CanReroll(result));
            }
        }
        [Test]
        public void MissingTokenOrUnmarkedRandomUnitNeverDrawsOrDeletesSource()
        {
            var s = new Setup(); long actor = s.Actor("h070", "A0YZ"); var rolls = new Rolls();
            var service = s.Service(rolls);
            Assert.AreEqual(ManualActionState.Rejected, service.TryReroll(actor).State);
            Assert.AreEqual(0, rolls.Calls); Assert.IsTrue(service.HasRerollAbility(actor));
            long ordinary = s.Actor("h06T"); s.Actor("h06G");
            Assert.AreEqual(ManualActionState.Rejected, service.TryReroll(ordinary).State);
            Assert.AreEqual(0, rolls.Calls); Assert.AreEqual(3, s.Combat.Roster.Count);
        }
        [Test]
        public void RandomRiskRerollNeedsNoTokenAndPostCommitObserverFailureDoesNotUndoReplacement()
        {
            var s = new Setup(); long actor = s.Actor("h070", "A0YZ");
            s.Wallet.Granted += _ => throw new Exception("UI observer");
            var service = s.Service(new Rolls(3), Original323GamblingNavigation.RiskHedge);
            Assert.AreEqual(ManualActionState.Succeeded, service.TryReroll(actor).State);
            Assert.AreEqual(1, s.Combat.Roster.Count);
            Assert.AreEqual(1, s.Wallet.Balance("source:reroll:CRl:uses"));
            Assert.IsFalse(service.HasPendingDraw);
        }
    }
}
