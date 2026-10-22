using System;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // These are synthetically reviewed JSON fixtures for the loader contract,
    // not real gameplay-approved object records.
    public sealed class OriginalVerifiedUnitObjectMapConflictTests
    {
        private static string ProjectFile(string name)
        {
            return File.ReadAllText(Path.Combine(
                Application.dataPath, "..", "data", name));
        }

        private static CharacterUnitCatalog Units()
        {
            return CharacterUnitCatalog.FromJson(ProjectFile("units.json"));
        }

        private static OriginalUnitRawcodeLabelCatalog Labels(CharacterUnitCatalog units)
        {
            return OriginalUnitRawcodeLabelCatalog.FromJson(
                ProjectFile("original-unit-rawcode-label-links.json"), units);
        }

        private static string Fixture(string rawcode, string unitId)
        {
            return "{\"gameVersion\":\"S2 2.323[C]\"," +
                "\"sourceJassSha256\":\"" +
                OriginalVerifiedUnitObjectMap.ExpectedJassSha256 + "\"," +
                "\"mappingEvidenceLevel\":\"manually-reviewed-original-object\"," +
                "\"approvedMappings\":[{\"rawcode\":\"" + rawcode + "\"," +
                "\"catalogUnitId\":\"" + unitId + "\"," +
                "\"sourceObjectFile\":\"Units/UnitData.slk\"," +
                "\"sourceObjectSha256\":\"" + new string('a', 64) + "\"," +
                "\"resolvedOriginalObjectName\":\"TEST_ONLY: synthetic fixture\"," +
                "\"manuallyReviewed\":true}]}";
        }

        [Test]
        public void E03CatalogRawcodeCannotBypassConflictingJassResultRawcode()
        {
            var units = Units();
            var labels = Labels(units);
            Assert.AreEqual("h0CJ", labels.FindUnit("E-03").Rawcode);
            Assert.Throws<FormatException>(() =>
                OriginalVerifiedUnitObjectMap.FromReviewedJson(
                    Fixture("h0C7", "E-03"), units, labels));
        }

        [Test]
        public void MatchingJassResultIdentityCanPassOnlyLoaderIdentityCheck()
        {
            var units = Units();
            var labels = Labels(units);
            var matched = OriginalVerifiedUnitObjectMap.FromReviewedJson(
                Fixture("h0CJ", "E-03"), units, labels);
            string linked;
            Assert.IsTrue(matched.TryResolveUnit("h0CJ", out linked));
            Assert.AreEqual("E-03", linked);
            Assert.IsFalse(matched.TryResolveUnit("h0C7", out linked));
            // This fixture does NOT certify any source-file SHA or real-world cast.
        }

        [Test]
        public void UnlabeledCommonCharacterDoesNotCreateFalseJassConflict()
        {
            var units = Units();
            var labels = Labels(units);
            Assert.IsNull(labels.FindUnit("C-01"));
            var matched = OriginalVerifiedUnitObjectMap.FromReviewedJson(
                Fixture("h003", "C-01"), units, labels);
            string linked;
            Assert.IsTrue(matched.TryResolveUnit("h003", out linked));
            Assert.AreEqual("C-01", linked);
        }
    }
}
