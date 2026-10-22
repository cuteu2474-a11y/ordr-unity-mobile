using System;
using NUnit.Framework;
using Ordr.Mobile.Mobile;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalMysticJassCandidateCatalogTests
    {
        private const string OriginalFingerprint =
            "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c";

        private static string CandidateJson(string verifiedCount = "0", string permitted = "false",
            string verifiedResult = "null")
        {
            return "{\"gameVersion\":\"S2 2.323[C]\",\"mode\":\"악몽 난이도 + 이세계모드 고정\"," +
                "\"sourceJassSha256\":\"" +
                OriginalFingerprint + "\",\"verifiedObjectRawcodeMappings\":" + verifiedCount +
                ",\"authorizedForUnitConsumption\":" + permitted +
                ",\"webRecipeCount\":1,\"rows\":[{" +
                "\"resultUnitId\":\"MY-01\",\"matchStatus\":\"positive-label-correlation\"," +
                "\"verifiedResultRawcode\":" + verifiedResult + "," +
                "\"authorizedForUnitConsumption\":false," +
                "\"strongestLabelEvidenceCandidateAbilities\":[\"A19W\"]," +
                "\"compatibleCandidatesUnderHypotheses\":[{" +
                "\"abilityRawcode\":\"A19W\",\"resultRawcode\":\"h09M\"," +
                "\"labelMatchedIngredients\":[{" +
                "\"catalogUnitId\":\"D-02\",\"count\":1},{" +
                "\"catalogUnitId\":\"H-17\",\"count\":1}]}]}]}";
        }

        [Test]
        public void RealThirteenRecipeEvidenceLoadsFromEditorSyncedResources()
        {
            var asset = Resources.Load<TextAsset>(
                "OrdrData/original-jass-mystic-candidate-reconciliation");
            Assert.IsNotNull(asset, "편집기 동기화 목록에 신비 JASS 후보 자료가 있어야 합니다.");
            var catalog = OriginalMysticJassCandidateCatalog.FromJson(asset.text);
            Assert.AreEqual(13, catalog.Count);
            Assert.IsFalse(catalog.AuthorizedForUnitConsumption);
            Assert.AreEqual("A19W", catalog.Find("MY-01")
                .StrongestEvidenceAbilities[0]);
        }

        [Test]
        public void CorrelatedAbilityIsVisibleButNeverRunnable()
        {
            var catalog = OriginalMysticJassCandidateCatalog.FromJson(CandidateJson());
            Assert.AreEqual(1, catalog.Count);
            Assert.IsFalse(catalog.AuthorizedForUnitConsumption);
            var result = catalog.Find("MY-01");
            Assert.IsNotNull(result);
            Assert.IsFalse(result.AuthorizedForUnitConsumption);
            CollectionAssert.AreEqual(new[] { "A19W" }, result.StrongestEvidenceAbilities);
            Assert.AreEqual("h09M", result.CompatibleCandidates[0].ResultRawcodeCandidate);
            Assert.AreEqual(2, result.CompatibleCandidates[0].CorrelatedIngredientCount);
            Assert.IsNull(catalog.Find("MY-99"));
        }

        [Test]
        public void GuessDataCannotGrantExecutionOrVerifiedMapping()
        {
            Assert.Throws<FormatException>(() =>
                OriginalMysticJassCandidateCatalog.FromJson(CandidateJson("1")));
            Assert.Throws<FormatException>(() =>
                OriginalMysticJassCandidateCatalog.FromJson(CandidateJson("0", "true")));
            Assert.Throws<FormatException>(() =>
                OriginalMysticJassCandidateCatalog.FromJson(CandidateJson("0", "false", "\"h09M\"")));
        }

        [Test]
        public void WrongFingerprintOrEvidenceCandidateFailsClosed()
        {
            string json = CandidateJson();
            Assert.Throws<FormatException>(() =>
                OriginalMysticJassCandidateCatalog.FromJson(json.Replace(
                    OriginalFingerprint, "0000000000000000000000000000000000000000000000000000000000000000")));
            Assert.Throws<FormatException>(() =>
                OriginalMysticJassCandidateCatalog.FromJson(json.Replace(
                    "\"strongestLabelEvidenceCandidateAbilities\":[\"A19W\"]",
                    "\"strongestLabelEvidenceCandidateAbilities\":[\"XXXX\"]")));
        }
    }
}
