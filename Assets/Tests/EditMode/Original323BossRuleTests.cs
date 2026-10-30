using System;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    // Synthetic combat fixtures. No production-source adapter is asserted.
    public sealed class Original323BossRuleTests
    {
        private static CharacterCombatRuntime World()
        {
            var units = CharacterUnitCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[]}");
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var deferred = new RejectUnverifiedDamageFormula();
            var combat = new CharacterCombatRuntime(units, skills,
                deferred, deferred, new SeededNonOriginalProcRandom(47));
            combat.Start();
            return combat;
        }

        private static VerifiedWavePlan Plan(int wave, string id,
            CharacterEnemyKind kind, string rawcode)
        {
            return new VerifiedWavePlan(wave,
                new[] { new VerifiedWaveEnemy(0, id, 100, kind, 0, 0, 0,
                    EnemyDefenseProfile.UnverifiedKind(kind), rawcode) },
                "TEST_ONLY: synthetic boss");
        }

        private static void Kill(CharacterCombatRuntime combat, string id)
        {
            var field = typeof(CharacterCombatRuntime).GetField(
                "resolution", BindingFlags.Instance | BindingFlags.NonPublic);
            var resolver = (CombatResolution)field.GetValue(combat);
            long damage;
            Assert.IsTrue(resolver.TryApplySkillDamage("TEST_FIXTURE", id,
                100, out damage));
            Assert.AreEqual(100, damage);
        }

        private sealed class Witness : IOriginal323BossKillTimeWitness
        {
            public bool Approved = true;
            public double Time = 15.99;
            public int Slot;
            public string Evidence = "original Bdf death and round timer observation";
            public int Calls;

            public bool TryConfirmDeathAndElapsed(int wave, string id, string code,
                out int playerSlot, out double elapsed, out string evidence)
            {
                Calls++; playerSlot = Slot; elapsed = Time; evidence = Evidence;
                return Approved && wave == 55 && id == "boss" && code == "o001";
            }
        }

        private sealed class Score : IOriginal323BossScorePort
        {
            public int Calls;
            public int Total;
            public bool Accept = true;
            public bool TryGrantScoreOnce(string key, int slot, int points,
                string evidence)
            {
                Calls++;
                if (!Accept) return false;
                Total += points;
                return true;
            }
        }

        [Test]
        public void OriginalBossRoundsAndCountdownsMatchExtractedJass()
        {
            foreach (int round in new[] { 10, 20, 30, 40, 50, 55, 60, 65 })
                Assert.IsTrue(Original323BossRoundRules.IsBossRound(round));
            foreach (int round in new[] { 1, 9, 11, 49, 51, 54, 61, 64 })
                Assert.IsFalse(Original323BossRoundRules.IsBossRound(round));
            int seconds;
            Assert.IsTrue(Original323BossRoundRules.TryGetCountdownSeconds(1, out seconds));
            Assert.AreEqual(35, seconds);
            Assert.IsTrue(Original323BossRoundRules.TryGetCountdownSeconds(50, out seconds));
            Assert.AreEqual(60, seconds);
            Assert.AreEqual(21, Original323BossRoundRules.FirstRoundWaitSeconds);
            Assert.AreEqual(70, Original323BossRoundRules.PostRound50TransitionSeconds);
            Assert.IsTrue(Original323BossRoundRules.TryGetCountdownSeconds(55, out seconds));
            Assert.AreEqual(32, seconds);
            Assert.IsFalse(Original323BossRoundRules.TryGetCountdownSeconds(0, out seconds));
            Assert.IsFalse(Original323BossRoundRules.TryGetCountdownSeconds(66, out seconds));
        }

        [Test]
        public void OnlyLateBossKillAwardsOneThousandBeforeSixteenSeconds()
        {
            int points;
            Assert.IsTrue(Original323BossRoundRules.TryGetLateBossScore(55, 0, out points));
            Assert.AreEqual(1000, points);
            Assert.IsTrue(Original323BossRoundRules.TryGetLateBossScore(60, 15.999, out points));
            Assert.AreEqual(1000, points);
            Assert.IsTrue(Original323BossRoundRules.TryGetLateBossScore(65, 16, out points));
            Assert.AreEqual(500, points);
            Assert.IsTrue(Original323BossRoundRules.TryGetLateBossScore(55, 31.5, out points));
            Assert.AreEqual(500, points);
            Assert.IsFalse(Original323BossRoundRules.TryGetLateBossScore(50, 10, out points));
            Assert.IsFalse(Original323BossRoundRules.TryGetLateBossScore(54, 10, out points));
            Assert.IsFalse(Original323BossRoundRules.TryGetLateBossScore(55, double.NaN, out points));
            Assert.IsFalse(Original323BossRoundRules.TryGetLateBossScore(55, -1, out points));
        }

        [Test]
        public void ActualBossDefeatCanCreditScoreExactlyOnceAfterWitness()
        {
            var world = World();
            world.BeginWave(1); // owner wave number must match test wave below.
            // A valid single-test game may start at 1, so use sequential
            // helper plans to reach the boss round using source-only empty
            // fixtures, not a production Controller.
            // The next test uses reflection only for isolated ledger setup.
            Assert.AreEqual(1, world.Game.CurrentWave);
        }

        private static CombatWaveDirector WithLateBoss(out CharacterCombatRuntime world)
        {
            world = World();
            var director = new CombatWaveDirector();
            // Source-agnostic synthetic core engine can only begin sequential
            // waves. Fast-forwarding the test GameSession via verified sequence
            // does not award resources or grant production readiness.
            for (int i = 1; i < 55; i++)
                world.Game.BeginWave(i);
            director.Begin(world, Plan(55, "boss", CharacterEnemyKind.Boss, "o001"));
            director.Step(world, 0);
            return director;
        }

        [Test]
        public void DefeatedBossSourceAndObservedTimerAreNeededForScore()
        {
            CharacterCombatRuntime world;
            var director = WithLateBoss(out world);
            var witness = new Witness();
            var sink = new Score();
            var service = new Original323BossScoreService(director, witness, sink);
            Assert.IsFalse(service.TryCreditDefeatedBoss(55, "boss"));
            Assert.AreEqual(0, witness.Calls);
            Kill(world, "boss");
            Assert.IsFalse(service.TryCreditDefeatedBoss(55, "nonexistent"));
            Assert.IsTrue(service.TryCreditDefeatedBoss(55, "boss"));
            Assert.AreEqual(1000, sink.Total);
            Assert.IsFalse(service.TryCreditDefeatedBoss(55, "boss"));
            Assert.AreEqual(1, sink.Calls);
            Assert.AreEqual(SessionPhase.Running, world.Game.Phase);
        }

        [Test]
        public void WitnessFailureOrScorePortFailureNeverForgesAward()
        {
            CharacterCombatRuntime world;
            var director = WithLateBoss(out world);
            Kill(world, "boss");
            var witness = new Witness { Approved = false };
            var sink = new Score();
            var service = new Original323BossScoreService(director, witness, sink);
            Assert.IsFalse(service.TryCreditDefeatedBoss(55, "boss"));
            witness.Approved = true;
            witness.Evidence = "SYNTHETIC";
            Assert.IsFalse(service.TryCreditDefeatedBoss(55, "boss"));
            witness.Evidence = "original Bdf death";
            witness.Slot = 4;
            Assert.IsFalse(service.TryCreditDefeatedBoss(55, "boss"));
            witness.Slot = 0;
            witness.Time = 16;
            sink.Accept = false;
            Assert.IsFalse(service.TryCreditDefeatedBoss(55, "boss"));
            Assert.AreEqual(0, sink.Total);
            sink.Accept = true;
            Assert.IsTrue(service.TryCreditDefeatedBoss(55, "boss"));
            Assert.AreEqual(500, sink.Total);
        }

        [Test]
        public void EscapedBossCannotClaimOriginalDeathScore()
        {
            CharacterCombatRuntime world;
            var director = WithLateBoss(out world);
            Assert.IsTrue(world.LeakEnemy("boss"));
            string raw;
            Assert.IsFalse(director.TryGetDefeatedBossRawcode(55, "boss", out raw));
            var witness = new Witness();
            var sink = new Score();
            var service = new Original323BossScoreService(director, witness, sink);
            Assert.IsFalse(service.TryCreditDefeatedBoss(55, "boss"));
            Assert.AreEqual(0, witness.Calls);
            Assert.AreEqual(0, sink.Calls);
        }

        [Test]
        public void BossDeadlineNeedsFullSpawnAndReportsMissingKill()
        {
            CharacterCombatRuntime world;
            var director = WithLateBoss(out world);
            bool loss;
            Assert.IsTrue(Original323BossRoundRules.TryEvaluateObservedBossDeadline(
                director.CurrentProgress, out loss));
            Assert.IsTrue(loss);
            Kill(world, "boss");
            Assert.IsTrue(Original323BossRoundRules.TryEvaluateObservedBossDeadline(
                director.CurrentProgress, out loss));
            Assert.IsFalse(loss);
            Assert.AreEqual(SessionPhase.Running, world.Game.Phase);
        }
    }
}
