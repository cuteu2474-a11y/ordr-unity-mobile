using System;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323MonsterGoldTests
    {
        // All monster positions, health and observed dice are TEST FIXTURES.
        private static CharacterCombatRuntime World()
        {
            var units = CharacterUnitCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[]}");
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var deferred = new RejectUnverifiedDamageFormula();
            var world = new CharacterCombatRuntime(units, skills,
                deferred, deferred, new SeededNonOriginalProcRandom(110));
            world.Start();
            return world;
        }

        private static CombatWaveDirector SpawnWave(CharacterCombatRuntime world,
            params VerifiedWaveEnemy[] enemies)
        {
            var director = new CombatWaveDirector();
            director.Begin(world, new VerifiedWavePlan(1, enemies,
                "TEST_ONLY: synthetic spawn data"));
            director.Step(world, 0);
            return director;
        }

        private static VerifiedWaveEnemy Spawn(string id,
            CharacterEnemyKind kind = CharacterEnemyKind.Line,
            string rawcode = "o00I")
        {
            return new VerifiedWaveEnemy(0, id, 100, kind,
                0, 0, 0, EnemyDefenseProfile.UnverifiedKind(kind), rawcode);
        }

        private static void Kill(CharacterCombatRuntime world, string id)
        {
            var f = typeof(CharacterCombatRuntime).GetField("resolution",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f);
            var combat = (CombatResolution)f.GetValue(world);
            long dealt;
            Assert.IsTrue(combat.TryApplySkillDamage("TEST_FIXTURE", id,
                150, out dealt));
            Assert.AreEqual(100, dealt);
        }

        private sealed class Witness : IOriginal323MonsterGoldDeathWitness
        {
            public bool Approved = true;
            public int Player;
            public int DeathRound = 1;
            public int Roll = 3;
            public float Bonus;
            public int ObservedGold = 2;
            public string Evidence = "original observed CVM death and BFh dice stream";
            public int Calls;

            public bool TryConfirmOriginalGoldDeath(int spawnWave, string id,
                string originalRawcode, out int playerSlot, out int deathRound,
                out int roll, out float goldBonus, out int observedGold,
                out string sourceEvidence)
            {
                Calls++;
                playerSlot = Player;
                deathRound = DeathRound;
                roll = Roll;
                goldBonus = Bonus;
                observedGold = ObservedGold;
                sourceEvidence = Evidence;
                return Approved && spawnWave == 1 &&
                    (originalRawcode == "o00I" ||
                     originalRawcode == "o01V");
            }
        }

        [Test]
        public void OriginalGoldUsesTruncationAndCurrentRound()
        {
            int gold;
            Assert.IsTrue(Original323MonsterGoldRules.TryComputeAward(1, 0, out gold));
            Assert.AreEqual(2, gold);
            Assert.IsTrue(Original323MonsterGoldRules.TryComputeAward(10, 0, out gold));
            Assert.AreEqual(18, gold);
            Assert.IsTrue(Original323MonsterGoldRules.TryComputeAward(55, 0, out gold));
            Assert.AreEqual(90, gold);
            Assert.IsTrue(Original323MonsterGoldRules.TryComputeAward(65, 0.6f, out gold));
            Assert.AreEqual(169, gold);
            Assert.IsFalse(Original323MonsterGoldRules.TryComputeAward(0, 0, out gold));
            Assert.IsFalse(Original323MonsterGoldRules.TryComputeAward(66, 0, out gold));
            Assert.IsFalse(Original323MonsterGoldRules.TryComputeAward(10, -0.2f, out gold));
            Assert.IsFalse(Original323MonsterGoldRules.TryComputeAward(
                10, float.NaN, out gold));
        }

        [Test]
        public void OnlyObservedRollThreePaysOutGold()
        {
            for (int roll = 1; roll <= 4; roll++)
            {
                int awarded;
                Assert.IsTrue(Original323MonsterGoldRules.TryEvaluateObservedRoll(
                    1, roll, 0, out awarded));
                Assert.AreEqual(roll == 3 ? 2 : 0, awarded);
            }
            int missing;
            Assert.IsFalse(Original323MonsterGoldRules.TryEvaluateObservedRoll(
                1, 0, 0, out missing));
            Assert.IsFalse(Original323MonsterGoldRules.TryEvaluateObservedRoll(
                1, 5, 0, out missing));
        }

        [Test]
        public void CanonicalDeathCreditsSameSharedWalletOnlyOnce()
        {
            var world = World();
            var waves = SpawnWave(world, Spawn("mob"));
            var wallet = new EconomyWallet();
            var witness = new Witness();
            var service = new Original323MonsterGoldService(
                waves, world.Game, wallet, 0, witness);
            int paid;
            Assert.IsFalse(service.TryProcessObservedDeath(1, "mob", out paid));
            Assert.AreEqual(0, witness.Calls);
            Kill(world, "mob");
            Assert.IsTrue(service.TryProcessObservedDeath(1, "mob", out paid));
            Assert.AreEqual(2, paid);
            Assert.AreEqual(2L, wallet.Balance("gold"));
            Assert.IsFalse(service.TryProcessObservedDeath(1, "mob", out paid));
            Assert.AreEqual(1, witness.Calls);
            Assert.AreEqual(0, paid);
        }

        [Test]
        public void OriginalGoldCanBeCreditedAutomaticallyAfterVerifiedDeathOnly()
        {
            var world = World();
            var waves = SpawnWave(world, Spawn("mob"), Spawn("escape"));
            var wallet = new EconomyWallet();
            var witness = new Witness();
            var service = new Original323MonsterGoldService(
                waves, world.Game, wallet, 0, witness);
            waves.VerifiedMonsterDefeated += (wave, id) =>
            {
                int awarded;
                service.TryProcessObservedDeath(wave, id, out awarded);
            };
            Assert.IsTrue(world.LeakEnemy("escape"));
            Assert.AreEqual(0, wallet.Balance("gold"));
            Kill(world, "mob");
            Assert.AreEqual(2, wallet.Balance("gold"));
            Assert.AreEqual(1, witness.Calls);
            int duplicate;
            Assert.IsFalse(service.TryProcessObservedDeath(1, "mob", out duplicate));
        }

        [Test]
        public void NonWinningRollIsPersistedAsProcessedWithoutAnyGold()
        {
            var world = World();
            var waves = SpawnWave(world, Spawn("mob"));
            Kill(world, "mob");
            var wallet = new EconomyWallet();
            var witness = new Witness { Roll = 2, ObservedGold = 0 };
            var service = new Original323MonsterGoldService(
                waves, world.Game, wallet, 0, witness);
            int paid;
            Assert.IsTrue(service.TryProcessObservedDeath(1, "mob", out paid));
            Assert.AreEqual(0, paid);
            Assert.AreEqual(0, wallet.Balance("gold"));
            var state = wallet.CaptureState();
            Assert.AreEqual(1, state.CompletedRewards.Count);
            Assert.IsFalse(service.TryProcessObservedDeath(1, "mob", out paid));
            var restored = new EconomyWallet();
            restored.RestoreState(state);
            var another = new Original323MonsterGoldService(
                waves, world.Game, restored, 0, witness);
            Assert.IsFalse(another.TryProcessObservedDeath(1, "mob", out paid));
        }

        [Test]
        public void EscapeAndMissingOrInvalidDeathCannotEarnGold()
        {
            var world = World();
            var waves = SpawnWave(world, Spawn("escaped"));
            var witness = new Witness();
            var wallet = new EconomyWallet();
            var service = new Original323MonsterGoldService(
                waves, world.Game, wallet, 0, witness);
            int paid;
            Assert.IsFalse(service.TryProcessObservedDeath(1, "escaped", out paid));
            Assert.IsTrue(world.LeakEnemy("escaped"));
            Assert.IsFalse(service.TryProcessObservedDeath(1, "escaped", out paid));
            Assert.IsFalse(service.TryProcessObservedDeath(1, "missing", out paid));
            Assert.IsFalse(service.TryProcessObservedDeath(2, "escaped", out paid));
            Assert.AreEqual(0, witness.Calls);
        }

        [Test]
        public void BossAndEnragedMonsterDeathUseSameOriginalGoldPath()
        {
            var world = World();
            var waves = SpawnWave(world,
                Spawn("boss", CharacterEnemyKind.Boss, "o01V"),
                Spawn("enraged", CharacterEnemyKind.Enraged, "o00I"));
            Kill(world, "boss"); Kill(world, "enraged");
            var wallet = new EconomyWallet();
            var witness = new Witness();
            var service = new Original323MonsterGoldService(
                waves, world.Game, wallet, 0, witness);
            int paid;
            Assert.IsTrue(service.TryProcessObservedDeath(1, "boss", out paid));
            Assert.IsTrue(service.TryProcessObservedDeath(1, "enraged", out paid));
            Assert.AreEqual(4, wallet.Balance("gold"));
            Assert.AreEqual(2, world.Game.DefeatedEnemies);
        }

        [Test]
        public void InvalidObservedRandomPlayerRoundEvidenceOrAmountFailsClosed()
        {
            var world = World();
            var waves = SpawnWave(world, Spawn("mob"));
            Kill(world, "mob");
            var wallet = new EconomyWallet();
            var witness = new Witness();
            var service = new Original323MonsterGoldService(
                waves, world.Game, wallet, 0, witness);
            int paid;
            witness.Approved = false;
            Assert.IsFalse(service.TryProcessObservedDeath(1, "mob", out paid));
            witness.Approved = true;
            witness.Roll = 5;
            Assert.IsFalse(service.TryProcessObservedDeath(1, "mob", out paid));
            witness.Roll = 3;
            witness.Player = 1;
            Assert.IsFalse(service.TryProcessObservedDeath(1, "mob", out paid));
            witness.Player = 0;
            witness.DeathRound = 2;
            Assert.IsFalse(service.TryProcessObservedDeath(1, "mob", out paid));
            witness.DeathRound = 1;
            witness.ObservedGold = 4;
            Assert.IsFalse(service.TryProcessObservedDeath(1, "mob", out paid));
            witness.ObservedGold = 2;
            witness.Evidence = "MODEL_ONLY: simulated roll";
            Assert.IsFalse(service.TryProcessObservedDeath(1, "mob", out paid));
            witness.Evidence = "observed Warcraft dice result";
            Assert.IsTrue(service.TryProcessObservedDeath(1, "mob", out paid));
            Assert.AreEqual(2, wallet.Balance("gold"));
        }

        [Test]
        public void DeathInLaterRoundUsesDeathRoundNotSpawnRound()
        {
            var world = World();
            var waves = SpawnWave(world, Spawn("old"));
            world.BeginWave(2);
            Kill(world, "old");
            var wallet = new EconomyWallet();
            var witness = new Witness { DeathRound = 2, ObservedGold = 4 };
            var service = new Original323MonsterGoldService(
                waves, world.Game, wallet, 0, witness);
            int paid;
            Assert.IsTrue(service.TryProcessObservedDeath(1, "old", out paid));
            Assert.AreEqual(4, paid);
            Assert.AreEqual(4, wallet.Balance("gold"));
        }

        [Test]
        public void WalletOverflowDoesNotConsumeDeathGoldAward()
        {
            var world = World();
            var waves = SpawnWave(world, Spawn("mob"));
            Kill(world, "mob");
            var wallet = new EconomyWallet();
            var baseline = wallet.CaptureState();
            wallet.Grant(new ResourceBundle().Add("gold", long.MaxValue));
            var witness = new Witness();
            var service = new Original323MonsterGoldService(
                waves, world.Game, wallet, 0, witness);
            int paid;
            Assert.IsFalse(service.TryProcessObservedDeath(1, "mob", out paid));
            Assert.AreEqual(long.MaxValue, wallet.Balance("gold"));
            Assert.AreEqual(0, wallet.CaptureState().CompletedRewards.Count);
            wallet.RestoreState(baseline);
            Assert.IsTrue(service.TryProcessObservedDeath(1, "mob", out paid));
            Assert.AreEqual(2, paid);
        }
    }
}
