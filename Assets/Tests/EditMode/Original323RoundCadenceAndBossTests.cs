using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323RoundCadenceAndBossTests
    {
        private static Original323MonsterWaveIdentityCatalog Source()
        {
            return Original323MonsterWaveIdentityCatalog.FromJson(File.ReadAllText(
                Path.Combine(Application.dataPath, "..", "data",
                    "original-2323C-monster-wave-script-evidence.json")));
        }

        private static VerifiedWaveEnemy Spawn(double time, string id,
            CharacterEnemyKind kind, string rawcode)
        {
            return new VerifiedWaveEnemy(time, id, 100,
                kind, 0, 0, 0, EnemyDefenseProfile.UnverifiedKind(kind), rawcode);
        }

        private static VerifiedWavePlan Plan(int number, string line,
            string boss, bool extra = false, double bossTime = -1,
            double lineTimeShift = 0, double enragedTime = 7.5)
        {
            var spawns = new List<VerifiedWaveEnemy>();
            if (line != null)
                for (int i = 1; i <= 35; i++)
                    spawns.Add(Spawn(i * 0.5 + lineTimeShift,
                        "line-" + i, CharacterEnemyKind.Line, line));
            if (extra)
                spawns.Add(Spawn(enragedTime, "enraged",
                    CharacterEnemyKind.Enraged, line));
            if (boss != null)
                spawns.Add(Spawn(bossTime < 0 ? 0.5 :
                    bossTime, "boss", CharacterEnemyKind.Boss, boss));
            return new VerifiedWavePlan(number, spawns,
                "TEST_ONLY: timing source fixture");
        }

        [Test]
        public void EarlyLineAndBossOnlyRoundsFollowHalfSecondSpawnTimer()
        {
            var source = Source();
            Assert.IsTrue(source.MatchesSinglePlayerSpawnCadence(
                Plan(1, "o00I", null)));
            Assert.IsTrue(source.MatchesSinglePlayerSpawnCadence(
                Plan(10, null, "o01V")));
            Assert.IsFalse(source.MatchesSinglePlayerSpawnCadence(
                Plan(10, null, "o01V", false, 1)));
            Assert.IsFalse(source.MatchesSinglePlayerSpawnCadence(
                Plan(1, "o00I", null, false, -1, 0.5)));
        }

        [Test]
        public void LateBossAndFirstNormalMonsterShareTheFirstTimerTick()
        {
            var source = Source();
            Assert.IsTrue(source.MatchesSinglePlayerSpawnCadence(
                Plan(55, "o01S", "o001")));
            Assert.IsTrue(source.MatchesSinglePlayerSpawnCadence(
                Plan(65, "o02H", "o02B")));
            Assert.IsTrue(source.MatchesSinglePlayerSpawnCadence(
                Plan(55, "o01S", "o001", false, 0.5)));
            Assert.IsFalse(source.MatchesSinglePlayerSpawnCadence(
                Plan(55, "o01S", "o001", false, 18)));
            Assert.IsFalse(source.MatchesSinglePlayerSpawnCadence(
                Plan(55, "o01S", "o001", false, 17.5)));
        }

        [Test]
        public void OptionalEnragedSpawnOnlyAtTheFifteenthNormalTick()
        {
            var source = Source();
            Assert.IsTrue(source.MatchesSinglePlayerSpawnCadence(
                Plan(51, "o01U", null, true)));
            Assert.IsTrue(source.MatchesSinglePlayerSpawnCadence(
                Plan(51, "o01U", null)));
            Assert.IsFalse(source.MatchesSinglePlayerSpawnCadence(
                Plan(51, "o01U", null, true, -1, 0, 8)));
            Assert.IsFalse(source.MatchesSinglePlayerSpawnCadence(
                Plan(55, "o01S", "o001", true)));
        }

        [Test]
        public void SameCountButWrongOriginalMonsterCannotPassTimingAudit()
        {
            var source = Source();
            Assert.IsFalse(source.MatchesSinglePlayerSpawnCadence(
                Plan(1, "n001", null)));
            Assert.IsFalse(source.MatchesSinglePlayerSpawnCadence(
                Plan(1, "o00I", "o01V")));
        }

        private sealed class Witness : IOriginal323ObservedRoundStartWitness
        {
            public bool Approved = true;
            public string Evidence = "original verified live round-start signal";
            public bool TryConfirmNextRoundStart(int previousRound, int requestedRound,
                out string observedEvidence)
            {
                observedEvidence = Evidence;
                return Approved && previousRound == 1 && requestedRound == 2;
            }
        }

        [Test]
        public void NextRoundRequiresObservedSourceNotJustAllKills()
        {
            var session = new GameSession();
            session.Start();
            string evidence;
            Assert.IsTrue(Original323ObservedRoundStartGate.TryAuthorize(
                session, 1, null, out evidence));
            session.BeginWave(1);
            Assert.IsFalse(Original323ObservedRoundStartGate.TryAuthorize(
                session, 2, null, out evidence));
            Assert.IsTrue(Original323ObservedRoundStartGate.TryAuthorize(
                session, 2, new Witness(), out evidence));
            Assert.IsFalse(Original323ObservedRoundStartGate.TryAuthorize(
                session, 3, new Witness(), out evidence));
        }

        [Test]
        public void OriginalNextWaveProbeWaitsForScheduledSpawnsNotKills()
        {
            var world = World();
            var director = new CombatWaveDirector();
            var witness = new Witness();
            Assert.IsFalse(Original323ObservedRoundStartGate.MayObserveNext(
                world.Game, director, witness));
            director.Begin(world, new VerifiedWavePlan(1,
                new[] { Spawn(1, "pending", CharacterEnemyKind.Line, "o00I") },
                "TEST_ONLY: future native spawn"));
            Assert.IsFalse(Original323ObservedRoundStartGate.MayObserveNext(
                world.Game, director, witness));
            director.Step(world, 1);
            // Existing living monsters do not prevent a source-observed
            // round start after all scheduled spawns were issued.
            Assert.AreEqual(1, world.Game.AliveEnemies);
            Assert.IsTrue(Original323ObservedRoundStartGate.MayObserveNext(
                world.Game, director, witness));
            Assert.IsFalse(Original323ObservedRoundStartGate.MayObserveNext(
                world.Game, director, null));
            string evidence;
            Assert.IsTrue(Original323ObservedRoundStartGate.TryAuthorize(
                world.Game, 2, witness, out evidence));
            director.Begin(world, new VerifiedWavePlan(2,
                new[] { Spawn(0, "next", CharacterEnemyKind.Line, "o00I") },
                "TEST_ONLY: next native spawn"));
            Assert.IsFalse(Original323ObservedRoundStartGate.MayObserveNext(
                world.Game, director, witness));
            director.Step(world, 0);
            Assert.IsTrue(Original323ObservedRoundStartGate.MayObserveNext(
                world.Game, director, witness));
        }

        [Test]
        public void RejectedSyntheticOrUnconfirmedTransitionLeavesRoundUnchanged()
        {
            var session = new GameSession();
            session.Start(); session.BeginWave(1);
            var witness = new Witness { Approved = false };
            string evidence;
            Assert.IsFalse(Original323ObservedRoundStartGate.TryAuthorize(
                session, 2, witness, out evidence));
            witness.Approved = true;
            witness.Evidence = "TEST_ONLY: fabricated";
            Assert.IsFalse(Original323ObservedRoundStartGate.TryAuthorize(
                session, 2, witness, out evidence));
            Assert.IsNull(evidence);
            Assert.AreEqual(1, session.CurrentWave);
            Assert.IsFalse(Original323ObservedRoundStartGate.TryAuthorize(
                session, 66, witness, out evidence));
        }

        private static CharacterCombatRuntime World()
        {
            var units = CharacterUnitCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[]}");
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var unavailable = new RejectUnverifiedDamageFormula();
            var world = new CharacterCombatRuntime(units, skills,
                unavailable, unavailable, new SeededNonOriginalProcRandom(981));
            world.Start();
            return world;
        }

        private static void Kill(CharacterCombatRuntime world, string enemyId)
        {
            var field = typeof(CharacterCombatRuntime).GetField(
                "resolution", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            var resolution = (CombatResolution)field.GetValue(world);
            long dealt;
            Assert.IsTrue(resolution.TryApplySkillDamage("TEST_FIXTURE",
                enemyId, 100, out dealt));
            Assert.AreEqual(100, dealt);
        }

        [Test]
        public void BossOutcomeTracksVerifiedKillsWithoutAutoRewardsOrVictory()
        {
            var world = World();
            var wave = new VerifiedWavePlan(1,
                new[] { Spawn(0, "boss", CharacterEnemyKind.Boss, "o01V") },
                "TEST_ONLY: single boss accounting");
            var director = new CombatWaveDirector();
            director.Begin(world, wave);
            Assert.AreEqual(1, director.CurrentProgress.BossPlanned);
            Assert.IsFalse(director.CurrentProgress.AllBossesDefeated);
            director.Step(world, 0);
            Assert.AreEqual(1, director.CurrentProgress.BossSpawned);
            Assert.AreEqual(1, director.CurrentProgress.LivingBosses);
            Kill(world, "boss");
            Assert.AreEqual(1, director.CurrentProgress.BossDefeated);
            Assert.AreEqual(0, director.CurrentProgress.LivingBosses);
            Assert.IsTrue(director.CurrentProgress.AllBossesDefeated);
            string bossRawcode, monsterRawcode;
            Assert.IsTrue(director.TryGetDefeatedBossRawcode(
                1, "boss", out bossRawcode));
            Assert.IsTrue(director.TryGetDefeatedMonsterRawcode(
                1, "boss", out monsterRawcode));
            Assert.AreEqual("o01V", bossRawcode);
            Assert.AreEqual(bossRawcode, monsterRawcode);
            Assert.AreEqual(SessionPhase.Running, world.Game.Phase);
        }

        [Test]
        public void BossEscapeAndNoBossNeverCountAsBossClear()
        {
            var world = World();
            var director = new CombatWaveDirector();
            director.Begin(world, new VerifiedWavePlan(1,
                new [] { Spawn(0, "normal", CharacterEnemyKind.Line, "o00I") },
                "TEST_ONLY: no-boss fixture"));
            director.Step(world, 0);
            Kill(world, "normal");
            Assert.IsTrue(director.CurrentProgress.AllWaveEnemiesDefeated);
            Assert.IsFalse(director.CurrentProgress.AllBossesDefeated);
            director.Begin(world, new VerifiedWavePlan(2,
                new [] { Spawn(0, "boss", CharacterEnemyKind.Boss, "o01V") },
                "TEST_ONLY: escape fixture"));
            director.Step(world, 0);
            Assert.IsTrue(world.LeakEnemy("boss"));
            Assert.AreEqual(1, director.CurrentProgress.BossEscaped);
            Assert.IsFalse(director.CurrentProgress.AllBossesDefeated);
            Assert.AreEqual(1, world.Game.LeakedEnemies);
            Assert.AreEqual(SessionPhase.Running, world.Game.Phase);
        }
    }
}
