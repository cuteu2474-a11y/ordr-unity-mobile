using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323MonsterObjectAbilityCatalogTests
    {
        private static string SourceJson() => File.ReadAllText(Path.Combine(
            Application.dataPath, "..", "data",
            "original-2323C-monster-object-abilities.json"));

        private static Original323MonsterObjectAbilityCatalog Catalog() =>
            Original323MonsterObjectAbilityCatalog.FromJson(SourceJson());

        private static VerifiedWavePlan Wave(string rawcode, CharacterEnemyKind kind)
        {
            return new VerifiedWavePlan(1, new List<VerifiedWaveEnemy> {
                new VerifiedWaveEnemy(0, "source-probe", 100,
                    kind, 0, 0, 0, new EnemyDefenseProfile(
                        kind, 0, null, "normal", "TEST_ONLY"), rawcode)
            }, "TEST_ONLY: no actual wave can be authorized");
        }

        [Test]
        public void ExtractedMonsterObjectsIncludeAllWaveRawcodes()
        {
            var catalog = Catalog();
            Assert.AreEqual(100, catalog.Count);
            var waveRoot = OrdrJson.Object(OrdrJson.Parse(File.ReadAllText(
                Path.Combine(Application.dataPath, "..", "data",
                    "original-2323C-monster-wave-script-evidence.json"))), "waves");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in OrdrJson.Array(OrdrJson.Value(waveRoot, "waves"), "waves"))
            {
                var round = OrdrJson.Object(item, "round");
                foreach (object enemy in OrdrJson.Array(
                    OrdrJson.Value(round, "normalAndBossSpawns"), "normalAndBossSpawns"))
                {
                    var spawn = OrdrJson.Object(enemy, "spawn");
                    string rawcode = OrdrJson.RequiredString(spawn, "rawcode");
                    string role = OrdrJson.RequiredString(spawn, "role");
                    Assert.IsTrue(catalog.HasObject(rawcode));
                    Assert.IsTrue(catalog.HasObjectBaseAbility(rawcode, "A04Q"));
                    Assert.IsTrue(catalog.HasObjectBaseAbility(rawcode, "A04M"));
                    Assert.IsTrue(catalog.HasObjectBaseAbility(rawcode, "A04R"));
                    Assert.AreEqual(role == "line",
                        catalog.HasObjectBaseAbility(rawcode, "Aeth"),
                        "Aeth native object membership must not be guessed.");
                    Assert.IsTrue(catalog.MatchesWaveObjectRoles(Wave(rawcode,
                        role == "boss" ? CharacterEnemyKind.Boss : CharacterEnemyKind.Line)));
                    seen.Add(rawcode);
                }
            }
            Assert.AreEqual(68, seen.Count);
        }

        [Test]
        public void StoryStunResistanceCreepMagicImmunityAndOtherInvulnerabilityStaySeparate()
        {
            var catalog = Catalog();
            Assert.IsTrue(catalog.HasObjectBaseAbility("n000", "A08Q"));
            Assert.IsTrue(catalog.HasObjectBaseAbility("n00E", "Amim"));
            Assert.IsTrue(catalog.HasObjectBaseAbility("o02E", "Avul"));
            Assert.IsFalse(catalog.HasObjectBaseAbility("o00I", "Amim"));
            Assert.IsFalse(catalog.HasObjectBaseAbility("o01V", "Aeth"));
            Assert.IsFalse(catalog.HasObjectBaseAbility("o00I", "Avul"));
            Assert.IsFalse(catalog.HasObject("xxxx"));
            Assert.IsFalse(catalog.HasObjectBaseAbility("xxxx", "Aeth"));
        }

        [Test]
        public void RawcodeRoleMismatchCannotEnterOriginalWaveMembership()
        {
            var catalog = Catalog();
            Assert.IsFalse(catalog.MatchesWaveObjectRoles(
                Wave("o01V", CharacterEnemyKind.Line)));
            Assert.IsFalse(catalog.MatchesWaveObjectRoles(
                Wave("o00I", CharacterEnemyKind.Boss)));
            Assert.IsFalse(catalog.MatchesWaveObjectRoles(
                Wave("n000", CharacterEnemyKind.Line)));
            Assert.IsFalse(catalog.MatchesWaveObjectRoles(
                Wave("xxxx", CharacterEnemyKind.Boss)));
            Assert.IsTrue(catalog.MatchesWaveObjectRoles(
                Wave("o00I", CharacterEnemyKind.Enraged)));
        }

        [Test]
        public void UnverifiedRuntimeClaimsAndSourceTamperingAreRejected()
        {
            string original = SourceJson();
            Assert.Throws<FormatException>(() =>
                Original323MonsterObjectAbilityCatalog.FromJson(original.Replace(
                    "\"safeForVerifiedMonsterWaveRuntime\": false",
                    "\"safeForVerifiedMonsterWaveRuntime\": true")));
            Assert.Throws<FormatException>(() =>
                Original323MonsterObjectAbilityCatalog.FromJson(original.Replace(
                    "\"liveImmunityFlagsVerified\": false",
                    "\"liveImmunityFlagsVerified\": true")));
            Assert.Throws<FormatException>(() =>
                Original323MonsterObjectAbilityCatalog.FromJson(original.Replace(
                    Original323MonsterObjectAbilityCatalog.OriginalUnitAbilitiesSha256,
                    new string('0', 64))));
            Assert.Throws<FormatException>(() =>
                Original323MonsterObjectAbilityCatalog.FromJson(original.Replace(
                    "\"rawcode\": \"o00I\"", "\"rawcode\": \"o004\"")));
        }
    }
}
