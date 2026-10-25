using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // Test-only spawn HP/armor. The real 65-wave JASS membership is read from
    // the pinned extraction, never inferred from these synthetic enemy stats.
    public sealed class Original323MonsterWaveIdentityCatalogTests
    {
        private static string OriginalJson() => File.ReadAllText(Path.Combine(
            Application.dataPath, "..", "data",
            "original-2323C-monster-wave-script-evidence.json"));

        private static Original323MonsterWaveIdentityCatalog Catalog() =>
            Original323MonsterWaveIdentityCatalog.FromJson(OriginalJson());

        private static VerifiedWavePlan Plan(int wave, string lineRawcode,
            int lineCount, string bossRawcode, int bossCount,
            int enragedCount = 0)
        {
            var spawns = new List<VerifiedWaveEnemy>();
            Action<string, CharacterEnemyKind, int> add = (rawcode, kind, count) =>
            {
                for (int i = 0; i < count; i++)
                {
                    string id = kind + ":" + i;
                    spawns.Add(new VerifiedWaveEnemy(i * 0.5, id, 1000,
                        kind, i, 0, 0, new EnemyDefenseProfile(
                            kind, 0, null, "normal", "TEST_ONLY: synthetic armor"),
                        rawcode));
                }
            };
            if (lineCount != 0) add(lineRawcode, CharacterEnemyKind.Line, lineCount);
            if (bossCount != 0) add(bossRawcode, CharacterEnemyKind.Boss, bossCount);
            if (enragedCount != 0) add(lineRawcode, CharacterEnemyKind.Enraged, enragedCount);
            return new VerifiedWavePlan(wave, spawns, "TEST_ONLY: synthetic positions");
        }

        [Test]
        public void PinnedOriginalJassSourceHasAllSixtyFiveWaves()
        {
            Assert.AreEqual(65, Catalog().Count);
        }

        [Test]
        public void FullSinglePlayerSourceIdentitiesMatchEarlyAndLateBossWaves()
        {
            var source = Catalog();
            Assert.IsTrue(source.MatchesSinglePlayerWave(
                Plan(1, "o00I", 35, null, 0)));
            Assert.IsTrue(source.MatchesSinglePlayerWave(
                Plan(10, null, 0, "o01V", 1)));
            Assert.IsTrue(source.MatchesSinglePlayerWave(
                Plan(55, "o01S", 35, "o001", 1)));
            Assert.IsTrue(source.MatchesSinglePlayerWave(
                Plan(65, "o02H", 35, "o02B", 1)));
        }

        [Test]
        public void UnknownRawcodesRolesAndShortenedRoundsNeverMatch()
        {
            var source = Catalog();
            Assert.IsFalse(source.MatchesSinglePlayerWave(
                Plan(1, "n001", 35, null, 0)));
            Assert.IsFalse(source.MatchesSinglePlayerWave(
                Plan(1, "o00I", 34, null, 0)));
            Assert.IsFalse(source.MatchesSinglePlayerWave(
                Plan(1, null, 0, "o00I", 1)));
            Assert.IsFalse(source.MatchesSinglePlayerWave(
                Plan(55, "o01S", 35, "o01V", 1)));
            Assert.IsFalse(source.MatchesSinglePlayerWave(
                Plan(65, "o02H", 35, null, 0)));
        }

        [Test]
        public void ConditionalEnragedCannotBeInventedOnUnrelatedWaves()
        {
            var source = Catalog();
            Assert.IsTrue(source.MatchesSinglePlayerWave(
                Plan(51, "o01U", 35, null, 0, 0)));
            Assert.IsTrue(source.MatchesSinglePlayerWave(
                Plan(51, "o01U", 35, null, 0, 1)));
            Assert.IsFalse(source.MatchesSinglePlayerWave(
                Plan(51, "o01U", 35, null, 0, 2)));
            Assert.IsFalse(source.MatchesSinglePlayerWave(
                Plan(55, "o01S", 35, "o001", 1, 1)));
        }

        [Test]
        public void ReferenceOnlySourceCannotBeRebrandedAsVerifiedFinalStats()
        {
            string original = OriginalJson();
            Assert.Throws<FormatException>(() =>
                Original323MonsterWaveIdentityCatalog.FromJson(original.Replace(
                    "\"safeForVerifiedMonsterWaveRuntime\": false",
                    "\"safeForVerifiedMonsterWaveRuntime\": true")));
            Assert.Throws<FormatException>(() =>
                Original323MonsterWaveIdentityCatalog.FromJson(original.Replace(
                    "\"sourceWar3mapJSha256\": \"96201e80",
                    "\"sourceWar3mapJSha256\": \"00000000")));
            Assert.Throws<FormatException>(() =>
                Original323MonsterWaveIdentityCatalog.FromJson(original.Replace(
                    "\"waveNumber\": 65", "\"waveNumber\": 66")));
            var testPlan = Plan(1, "o00I", 35, null, 0);
            Assert.IsTrue(Catalog().MatchesSinglePlayerWave(testPlan));
            string proof;
            Assert.IsFalse(Original323VerifiedMonsterWaveCatalog.TryAuthorizeForLive(
                testPlan, null, out proof));
        }
    }
}
