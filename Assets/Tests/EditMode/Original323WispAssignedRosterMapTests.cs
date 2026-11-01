using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323WispAssignedRosterMapTests
    {
        private static string Read(string file) => File.ReadAllText(
            Path.Combine(Application.dataPath, "..", "data", file));

        private static Original323WispAssignedRosterMap Create(string identityJson)
        {
            var units = CharacterUnitCatalog.FromJson(Read("units.json"));
            var objects = OriginalMapBaseStatCatalog.FromJson(
                Read("original-2323C-map-base-stats.json"), units);
            return Original323WispAssignedRosterMap.FromSourceJson(
                identityJson, Read("original-2323C-wisp-pools.json"), units, objects);
        }

        [Test]
        public void All156SourceRewardsMapToExistingCharacters()
        {
            var map = Create(Read("original-2323C-wisp-character-identity-assignments.json"));
            Assert.AreEqual(156, map.Count);
            Assert.IsTrue(map.TryResolveUnit("h00M", out var uncommon));
            Assert.AreEqual("U-04", uncommon);
            Assert.IsTrue(map.TryResolveUnit("h0BG", out var gaimon));
            Assert.AreEqual("X-08", gaimon);
            Assert.IsFalse(map.TryResolveUnit("h060", out _));
            Assert.IsFalse(map.TryResolveUnit("h04S", out _));
        }

        [Test]
        public void TamperedCatalogIdIsRejectedBeforeAnyWispIsConsumed()
        {
            string identity = Read("original-2323C-wisp-character-identity-assignments.json");
            string changed = identity.Replace(
                "\"catalogUnitId\": \"U-04\"", "\"catalogUnitId\": \"U-05\"");
            Assert.AreNotEqual(identity, changed);
            Assert.Throws<FormatException>(() => Create(changed));
        }

        [Test]
        public void LegendaryUsesOriginalSixtyUnitPoolAndSourceStartPosition()
        {
            var roller = new Original323AutomaticUncommonWisps(
                Read("original-2323C-wisp-pools.json"), new MinimumRoll());
            Assert.IsTrue(roller.IsVerified(WispResourceIds.Legendary));
            var result = roller.Roll(WispResourceIds.Legendary);
            Assert.AreEqual("h03K", result.UnitRawcode);
            Assert.IsNull(result.BonusAbilityRawcode);
            Assert.IsTrue(roller.TryGetPosition(
                WispResourceIds.Legendary, out var x, out var y));
            Assert.AreEqual(-3776d, x);
            Assert.AreEqual(4480d, y);
            Assert.IsFalse(roller.IsVerified(WispResourceIds.Rare));
        }

        private sealed class MinimumRoll : IOriginalWispRandom
        {
            public int NextInclusive(int min, int max) { return min; }
        }
    }
}
