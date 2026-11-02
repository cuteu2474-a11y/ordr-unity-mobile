using System;
using NUnit.Framework;
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
    }
}
