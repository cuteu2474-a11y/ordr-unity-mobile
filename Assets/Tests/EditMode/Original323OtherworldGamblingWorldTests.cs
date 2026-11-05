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
    public sealed class Original323OtherworldGamblingWorldTests
    {
        private sealed class Die : ICombatProcRandom
        { public int Next(int upper) { return 0; } }
        private static string Data(string name) => File.ReadAllText(
            Path.Combine(Application.dataPath, "..", "data", name));

        [TestCase(true, "h070", "RN-09", "A0YZ")]
        [TestCase(false, "h06G", "LT-01", null)]
        [TestCase(false, "h05K", "R-41", "A0YE")]
        public void RealRosterSpawnUsesExactSourceIdentityAndRollsBackWithAbilityMetadata(
            bool won, string rawcode, string expectedId, string ability)
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(Data("original-2323C-map-base-stats.json"), units);
            var die = new Die(); var rules = new SourceModelBasicCombatRules(map, die);
            var combat = new CharacterCombatRuntime(units,
                CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json")), rules, rules, die);
            combat.Start();
            var registry = new Original323WispDeferredAbilityRegistry(combat);
            var routes = new Original323CwrAcquisitionRoutes(
                OriginalJassFusionCatalog.FromJson(Data("original-jass-fusion-registry.json")), units, map);
            var assignments = Original323WispAssignedRosterMap.FromSourceJson(
                Data("original-2323C-wisp-character-identity-assignments.json"),
                Data("original-2323C-wisp-pools.json"), units, map);
            var world = new Original323OtherworldGamblingWorld(combat, units, map, routes, assignments, registry);
            var outcome = new Original323OtherworldGamblingOutcome(won, rawcode,
                won || rawcode == "h06G" ? Original323GamblingNavigation.None : Original323GamblingNavigation.Casino);
            Assert.AreEqual(ability, outcome.BonusAbility);
            long actor;
            using (var spawn = world.Begin(outcome))
            {
                string name;
                Assert.IsTrue(spawn.TrySpawn(out actor, out name));
                Assert.IsNotEmpty(name);
                Assert.AreEqual(expectedId, combat.Roster.Get(actor).UnitId);
                Assert.AreEqual(ability == null ? 0 : 1, registry.PendingCount);
            }
            Assert.Throws<KeyNotFoundException>(() => combat.Roster.Get(actor));
            Assert.AreEqual(0, registry.PendingCount);
            using (var spawn = world.Begin(outcome))
            {
                string name;
                Assert.IsTrue(spawn.TrySpawn(out actor, out name));
                spawn.Commit();
            }
            Assert.AreEqual(expectedId, combat.Roster.Get(actor).UnitId);
        }
    }
}
