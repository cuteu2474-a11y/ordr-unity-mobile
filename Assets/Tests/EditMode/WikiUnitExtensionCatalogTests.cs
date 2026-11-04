using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    public sealed class WikiUnitExtensionCatalogTests
    {
        private const string BaseJson =
            "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[" +
            "{\"id\":\"PS-01\",\"grade\":\"해적선\",\"name\":\"해적선\",\"roleTags\":[]}," +
            "{\"id\":\"TR-20\",\"grade\":\"초월함\",\"name\":\"조로\",\"roleTags\":[]}" +
            "]}";
        private const string Extension =
            "{\"format\":\"ordr-wiki-unit-extension-v1\",\"gameVersion\":\"S2 2.323[C]\"," +
            "\"sourceW3xSha256\":\"0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c\"," +
            "\"baseCatalogEntries\":2,\"expectedMergedEntries\":4," +
            "\"reclassifications\":[{\"id\":\"PS-01\",\"expectedGrade\":\"해적선\",\"grade\":\"히든\",\"gradeOrder\":23}]," +
            "\"entries\":[{\"id\":\"TR-31\",\"grade\":\"초월함\",\"name\":\"조로 염왕\",\"roleTags\":[],\"catalogEntryKind\":\"alternate-form\",\"variantOfUnitId\":\"TR-20\"}," +
            "{\"id\":\"R-44\",\"grade\":\"희귀함\",\"name\":\"고대의 배\",\"roleTags\":[],\"catalogEntryKind\":\"support-object\"}]}";

        [Test]
        public void SourceCatalogueAndAdditionalFormsStayDistinct()
        {
            var catalogue = CharacterUnitCatalog.FromJson(BaseJson);
            catalogue.ApplyWikiExtension(Extension);
            Assert.AreEqual(4, catalogue.Count);
            Assert.AreEqual("히든", catalogue.Require("PS-01").Grade);
            Assert.IsTrue(catalogue.Require("TR-31").IsAlternateForm);
            Assert.AreEqual("TR-20", catalogue.Require("TR-31").VariantOfUnitId);
            Assert.IsFalse(catalogue.Require("R-44").IsAlternateForm);
            Assert.IsNull(catalogue.Require("R-44").AttackPowerMin);
        }

        [Test]
        public void InvalidParentDoesNotPartiallyRegradeCatalog()
        {
            var catalogue = CharacterUnitCatalog.FromJson(BaseJson);
            string broken = Extension.Replace(
                "\"variantOfUnitId\":\"TR-20\"",
                "\"variantOfUnitId\":\"MISSING\"");
            Assert.Throws<FormatException>(() => catalogue.ApplyWikiExtension(broken));
            Assert.AreEqual(2, catalogue.Count);
            Assert.AreEqual("해적선", catalogue.Require("PS-01").Grade);
        }
        private static string Data(string name) =>
            File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", name));

        [Test]
        public void FullSourceOverlayInheritsOnlyReadOnlyTooltipMetadata()
        {
            var catalog = CharacterUnitCatalog.FromJson(Data("units.json"));
            catalog.ApplyWikiExtension(Data("wiki-unit-catalog-extension.json"));
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var definitions = UnitImplementationRegistry.Combine(catalog, skills, null);
            Assert.AreEqual(287, definitions.Count);
            Assert.AreEqual(200, definitions.DetailedProfiles);
            Assert.AreEqual(554, definitions.CollectedSkills);
            Assert.AreEqual(295, definitions.CollectedPassives);
            Assert.AreEqual("히든", definitions.Require("PS-01").Grade);
            Assert.AreEqual("희귀함", definitions.Require("R-44").Grade);
            Assert.AreEqual("재료", definitions.Require("SM-01").Grade);
            Assert.IsNull(definitions.Require("SM-01").Profile);
            Assert.AreEqual("전설", definitions.Require("L-40").Grade);
            Assert.AreEqual("신비함", definitions.Require("MY-14").Grade);
            foreach (string form in new[] { "TR-31", "TR-32", "TR-33", "TR-34", "TR-35" })
            {
                var owner = definitions.Require(form);
                Assert.IsFalse(owner.CanSpawnIntoRoster, form);
                Assert.IsTrue(owner.HasCollectedDetailedSkills, form);
                Assert.AreEqual(owner.VariantOfUnitId, owner.Profile.UnitId, form);
                Assert.IsFalse(owner.HasOriginalObjectStats, form);
            }
            Assert.IsNull(definitions.Require("MY-14").Profile);
        }

        [Test]
        public void WrongSourceFingerprintCannotRegradeOrAppend()
        {
            var catalog = CharacterUnitCatalog.FromJson(BaseJson);
            var corrupted = Extension.Replace(
                "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c",
                "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5d");
            Assert.Throws<FormatException>(() => catalog.ApplyWikiExtension(corrupted));
            Assert.AreEqual(2, catalog.Count);
            Assert.AreEqual("해적선", catalog.Require("PS-01").Grade);
        }

    }
}
