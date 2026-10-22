using System;
using NUnit.Framework;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalMapBaseStatCatalogTests
    {
        private const string UnitFixture =
            "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[" +
            "{\"id\":\"C-01\",\"grade\":\"흔함\",\"name\":\"루피\",\"roleTags\":[]}," +
            "{\"id\":\"C-02\",\"grade\":\"흔함\",\"name\":\"조로\",\"roleTags\":[]}" +
            "]}";

        private const string MapHeader =
            "{\"gameVersion\":\"S2 2.323[C]\",\"format\":\"ordr-source-map-base-stats-v1\"," +
            "\"sourceW3xSha256\":\"0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c\"," +
            "\"catalogEntries\":[";

        private const string Luffy =
            "{\"catalogId\":\"C-01\",\"catalogName\":\"루피\",\"grade\":\"흔함\"," +
            "\"mappingStatus\":\"verified-common-map-identity\",\"originalRawcode\":\"h003\"," +
            "\"sourceMapName\":\"몽키.D.루피 - 흔함\",\"combatParityVerified\":false," +
            "\"attack1\":{\"minimumBaseDamage\":29,\"maximumBaseDamage\":31," +
            "\"attackCooldownSeconds\":1.02,\"originalRange\":500}," +
            "\"attack2\":null,\"baseStats\":{\"armor\":0,\"hitPoints\":10}}";

        private const string Unmatched =
            "{\"catalogId\":\"C-02\",\"catalogName\":\"조로\",\"grade\":\"흔함\"," +
            "\"mappingStatus\":\"unmatched\",\"originalRawcode\":null," +
            "\"sourceMapName\":null,\"attack1\":null,\"baseStats\":null," +
            "\"combatParityVerified\":false}";

        private const string JassReconciled =
            "{\"catalogId\":\"C-02\",\"catalogName\":\"조로\",\"grade\":\"흔함\"," +
            "\"mappingStatus\":\"verified-jass-fusion-result\",\"originalRawcode\":\"h002\"," +
            "\"sourceMapName\":\"조로 - 흔함\",\"combatParityVerified\":false," +
            "\"sourceJassFusionAbilityRawcodes\":[\"A011\"]," +
            "\"alternateRawcodes\":[\"h0XX\"],\"attack1\":null," +
            "\"baseStats\":{\"armor\":0,\"hitPoints\":10}}";

        private static readonly string JassLabelled = JassReconciled.Replace(
            "verified-jass-fusion-result", "verified-jass-label-and-fusion-result")
            .Replace("\"alternateRawcodes\":[\"h0XX\"],", "");

        [Test]
        public void OriginalMapLuffyIsReadableWithoutEnablingCombat()
        {
            var units = CharacterUnitCatalog.FromJson(UnitFixture);
            var catalog = OriginalMapBaseStatCatalog.FromJson(
                MapHeader + Luffy + "," + Unmatched + "]}", units);
            Assert.AreEqual(2, catalog.Count);
            Assert.AreEqual(1, catalog.LinkedCount);
            Assert.AreEqual("h003", catalog.Find("C-01").OriginalRawcode);
            Assert.AreEqual(29.0, catalog.Find("C-01").Attack1.MinimumBaseDamage);
            Assert.AreEqual(31.0, catalog.Find("C-01").Attack1.MaximumBaseDamage);
            Assert.AreEqual(1.02, catalog.Find("C-01").Attack1.AttackCooldownSeconds);
            Assert.AreEqual(1.0 / 1.02,
                catalog.Find("C-01").Attack1.BaseAttacksPerSecond.Value, 1e-9);
            Assert.IsFalse(catalog.Find("C-01").CombatParityVerified);
            Assert.IsNull(catalog.Find("C-02").Attack1);
        }

        [Test]
        public void CatalogRejectsFalseCombatParityClaim()
        {
            var units = CharacterUnitCatalog.FromJson(UnitFixture);
            var bad = Luffy.Replace("\"combatParityVerified\":false",
                                    "\"combatParityVerified\":true");
            Assert.Throws<FormatException>(() => OriginalMapBaseStatCatalog.FromJson(
                MapHeader + bad + "," + Unmatched + "]}", units));
        }

        [Test]
        public void JassObjectEvidencePreservesAlternatesWithoutClaimingCombatParity()
        {
            var units = CharacterUnitCatalog.FromJson(UnitFixture);
            var catalog = OriginalMapBaseStatCatalog.FromJson(
                MapHeader + Luffy + "," + JassReconciled + "]}", units);
            var row = catalog.Find("C-02");
            Assert.AreEqual("h002", row.OriginalRawcode);
            CollectionAssert.AreEqual(new[] { "h0XX" }, row.AlternateRawcodes);
            CollectionAssert.AreEqual(new[] { "A011" }, row.SourceJassFusionAbilityRawcodes);
            Assert.IsFalse(row.CombatParityVerified);
            Assert.Throws<FormatException>(() => OriginalMapBaseStatCatalog.FromJson(
                MapHeader + Luffy + "," + JassReconciled.Replace(
                    "\"sourceJassFusionAbilityRawcodes\":[\"A011\"]",
                    "\"sourceJassFusionAbilityRawcodes\":[]") + "]}", units));
        }

        [Test]
        public void JassLabelAndFusionEvidenceLinksAnUnmatchedObjectWithoutInventingForms()
        {
            var units = CharacterUnitCatalog.FromJson(UnitFixture);
            var catalog = OriginalMapBaseStatCatalog.FromJson(
                MapHeader + Luffy + "," + JassLabelled + "]}", units);
            var row = catalog.Find("C-02");
            Assert.AreEqual(2, catalog.LinkedCount);
            Assert.AreEqual("h002", row.OriginalRawcode);
            CollectionAssert.AreEqual(new[] { "A011" }, row.SourceJassFusionAbilityRawcodes);
            Assert.AreEqual(0, row.AlternateRawcodes.Count);
            Assert.IsFalse(row.CombatParityVerified);
            Assert.Throws<FormatException>(() => OriginalMapBaseStatCatalog.FromJson(
                MapHeader + Luffy + "," + JassLabelled.Replace(
                    "\"sourceJassFusionAbilityRawcodes\":[\"A011\"]",
                    "\"sourceJassFusionAbilityRawcodes\":[]") + "]}", units));
            Assert.Throws<FormatException>(() => OriginalMapBaseStatCatalog.FromJson(
                MapHeader + Luffy + "," + JassLabelled.Replace(
                    "\"attack1\":null", "\"alternateRawcodes\":[\"h0XX\"],\"attack1\":null") +
                    "]}", units));
        }
    }
}
