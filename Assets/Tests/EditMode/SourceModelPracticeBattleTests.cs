using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Unity;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceModelPracticeBattleTests
    {
        private sealed class MinimumDice : ICombatProcRandom
        { public int Next(int upper) { return 0; } }
        private sealed class WinningGold : IOriginalWispRandom
        { public int NextInclusive(int min, int max) { Assert.AreEqual(1, min); Assert.AreEqual(4, max); return 3; } }
        private sealed class Live : IDisposable
        {
            public CharacterCombatRuntime Combat;
            public SharedEconomySession Economy;
            public SourceModelPracticeBattle Battle;
            public void Dispose() { Battle.Dispose(); }
        }
        private static string Data(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", file));
        private static Live Create(int attackers = 0, int lastRound = 10, string attackerUnitId = "U-02")
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(Data("original-2323C-map-base-stats.json"), units);
            var dice = new MinimumDice();
            var rules = new SourceModelBasicCombatRules(map, dice);
            var combat = new CharacterCombatRuntime(units, skills, rules, rules, dice);
            var shared = new SharedEconomySession(new EconomyWallet(), combat.Roster);
            shared.ApplyVerifiedStart(Original323SoloStartProfile.Create(),
                (id, x, y) => shared.Units.Add(id, x, y), id => shared.Units.Remove(id));
            combat.Start();
            rules.Enable();
            combat.ConfigureSourceModelBasicAttacks(rules);
            for (int i = 0; i < attackers; i++) combat.SpawnCharacter(attackerUnitId, -4000 + 20 * i, 4000);
            var identity = Original323MonsterWaveIdentityCatalog.FromJson(Data("original-2323C-monster-wave-script-evidence.json"));
            var battle = new SourceModelPracticeBattle(combat, new CombatWaveDirector(), shared,
                new SourceModelPracticeCatalog(Data(lastRound == 65
                    ? "mobile-standalone-enemy-model.json" : "mobile-practice-battle-model.json"), lastRound), identity,
                rules, new WinningGold(), null);
            battle.Start();
            return new Live { Combat = combat, Economy = shared, Battle = battle };
        }

        [Test]
        public void PracticeAttacksDealDamageAndPayActualKillGoldWithoutRetailApproval()
        {
            using (var game = Create(10))
            {
                var first = game.Combat.MobileUnitSnapshot()[0];
                Assert.IsTrue(game.Combat.UsesSourceModelBasicAttacks);
                Assert.IsFalse(game.Combat.HasVerifiedAttack(first.InstanceId));
                var events = game.Battle.Advance(10);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.Attack));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.EnemyDefeated));
                Assert.Greater(game.Combat.Game.DefeatedEnemies, 0);
                Assert.Greater(game.Battle.GoldAwarded, 0);
                Assert.AreEqual(60 + game.Battle.GoldAwarded, game.Economy.Wallet.Balance("gold"));
                Assert.AreEqual(0, game.Battle.BossScoreAwarded);
                Assert.AreEqual(0, game.Combat.Game.LeakedEnemies);
            }
        }

        [Test]
        public void EmptyBoardAdvancesOnSourceTimerAndGrantsTwoWispsExactlyOnce()
        {
            using (var game = Create())
            {
                game.Battle.Advance(35);
                Assert.AreEqual(2, game.Combat.Game.CurrentWave);
                Assert.AreEqual(35, game.Combat.Game.AliveEnemies);
                Assert.AreEqual(7, game.Economy.Wallet.Balance(WispResourceIds.Random));
                Assert.AreEqual(2, game.Battle.RoundWispsAwarded);
                Assert.AreEqual(1, game.Battle.RoundHistory.Count);
                Assert.AreEqual(1, game.Battle.RoundHistory[0].Wave);
                Assert.AreEqual(35, game.Battle.RoundHistory[0].ElapsedSeconds, 1e-6);
                Assert.AreEqual(35, game.Battle.RoundHistory[0].AliveEnemies);
                game.Battle.Advance(0);
                game.Battle.Advance(.1);
                Assert.AreEqual(7, game.Economy.Wallet.Balance(WispResourceIds.Random));
                Assert.AreEqual(0, game.Combat.CharacterCount);
                Assert.AreEqual(SessionPhase.Running, game.Combat.Game.Phase);
            }
        }

        [Test]
        public void SeventyAccumulatedMonstersLoseWithoutInventedEscapesOrRewards()
        {
            using (var game = Create())
            {
                game.Battle.Advance(100);
                Assert.AreEqual(SessionPhase.Lost, game.Combat.Game.Phase);
                Assert.AreEqual(70, game.Combat.Game.AliveEnemies);
                Assert.AreEqual(0, game.Combat.Game.LeakedEnemies);
                Assert.AreEqual(0, game.Combat.Game.DefeatedEnemies);
                Assert.AreEqual(60, game.Economy.Wallet.Balance("gold"));
                StringAssert.Contains("70", game.Battle.ResultMessage);
                long gold = game.Economy.Wallet.Balance("gold");
                game.Battle.Advance(100);
                Assert.AreEqual(gold, game.Economy.Wallet.Balance("gold"));
            }
        }

        [Test]
        public void SourceRouteMovesLivingEnemiesAndDoesNotKillAtLapEnd()
        {
            using (var game = Create())
            {
                game.Battle.Advance(.5);
                var before = game.Combat.MobileEnemySnapshot().Single();
                game.Battle.Advance(.1);
                var after = game.Combat.MobileEnemySnapshot().Single();
                Assert.AreEqual(before.Id, after.Id);
                Assert.AreNotEqual(before.Y, after.Y);
                Assert.AreEqual(before.Health, after.Health);
                Assert.AreEqual(0, game.Combat.Game.LeakedEnemies);
            }
        }

        [Test]
        public void TenRoundPracticeCanBeCompletedUsingActualObjectAttackStats()
        {
            // Strong TEST formation skips player recruitment, but every hit
            // still runs the actual source-object dice/cadence/armor model.
            using (var game = Create(35))
            {
                for (int i = 0; i < 800 && game.Combat.Game.Phase == SessionPhase.Running; i++)
                    game.Battle.Advance(.5);
                Assert.AreEqual(SessionPhase.Won, game.Combat.Game.Phase, game.Battle.ResultMessage);
                Assert.AreEqual(10, game.Combat.Game.CurrentWave);
                Assert.AreEqual(316, game.Combat.Game.DefeatedEnemies);
                Assert.AreEqual(0, game.Combat.Game.AliveEnemies);
                Assert.AreEqual(0, game.Combat.Game.LeakedEnemies);
                Assert.AreEqual(18, game.Battle.RoundWispsAwarded);
                Assert.AreEqual(23, game.Economy.Wallet.Balance(WispResourceIds.Random));
                Assert.AreEqual("10라운드 연습 완료", game.Battle.ResultMessage);
                Assert.AreEqual(10, game.Battle.RoundHistory.Count);
                Assert.AreEqual(316, game.Battle.RoundHistory.Sum(x => x.Defeated));
                Assert.AreEqual(game.Battle.GoldAwarded, game.Battle.RoundHistory.Sum(x => x.GoldAwarded));
                CollectionAssert.AreEqual(Enumerable.Range(1, 10), game.Battle.RoundHistory.Select(x => x.Wave));
                long paid = game.Economy.Wallet.Balance("gold");
                game.Battle.Advance(60);
                Assert.AreEqual(paid, game.Economy.Wallet.Balance("gold"));
                Assert.AreEqual(10, game.Battle.RoundHistory.Count);
            }
        }

        [TestCase(10, "U-02")]
        [TestCase(20, "L-29")]
        public void UnkilledFinalPracticeBossFailsAtItsSourceDeadline(int lastRound, string attackerUnitId)
        {
            using (var game = Create(35, lastRound, attackerUnitId))
            {
                for (int i = 0; i < 1800 && game.Combat.Game.CurrentWave < lastRound; i++)
                    game.Battle.Advance(.5);
                Assert.AreEqual(lastRound, game.Combat.Game.CurrentWave);
                foreach (var unit in game.Combat.MobileUnitSnapshot()) game.Combat.RemoveCharacter(unit.InstanceId);
                game.Battle.Advance(60);
                Assert.AreEqual(SessionPhase.Lost, game.Combat.Game.Phase);
                StringAssert.Contains("보스 제한시간", game.Battle.ResultMessage);
                Assert.AreEqual(0, game.Combat.Game.LeakedEnemies);
            }
        }

        [TestCase("S-01")]
        [TestCase("S-09")]
        [TestCase("R-06")]
        [TestCase("L-29")]
        public void HigherGradeUnitsJoinTheSamePracticeAttackPipelineWithoutEnablingSkills(string unitId)
        {
            using (var game = Create())
            {
                long id = game.Combat.SpawnCharacter(unitId, -3760, 3904);
                Assert.AreEqual(1, game.Combat.ReadyAttackers);
                Assert.IsFalse(game.Combat.HasVerifiedAttack(id));
                var events = game.Battle.Advance(3);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.Attack));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.EnemyDefeated));
                Assert.IsFalse(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated ||
                    x.Type == CharacterCombatEventType.SkillDamage || x.Type == CharacterCombatEventType.ZombieMaterialEarned));
                Assert.AreEqual(0, game.Combat.MoriaZombieMaterials);
            }
        }

        [Test]
        public void FusionDuringPracticeReplacesActiveAttackersAndContinuesCombat()
        {
            var root = new GameObject("ORDR live Special fusion");
            root.SetActive(false);
            try
            {
                var owner = root.AddComponent<OrdrCharacterCombatController>();
                typeof(OrdrCharacterCombatController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(owner, null);
                owner.BeginUnitPreparation();
                for (int i = 0; i < 3; i++) owner.Combat.SpawnCharacter("C-01", -4000, 4000);
                owner.BeginPracticeBattle();
                owner.PracticeBattle.Advance(.1);
                var offer = owner.ManualActions.PreviewFusion(owner.Combat.MobileUnitSnapshot())
                    .Single(x => x.RecipeId == "A012");
                Assert.AreEqual(Ordr.Mobile.Mobile.ManualActionState.Succeeded,
                    owner.ManualActions.ConfirmFusion(offer).State);
                Assert.AreEqual(1, owner.Combat.CharacterCount);
                Assert.AreEqual(1, owner.Combat.ReadyAttackers);
                Assert.AreEqual("S-07", owner.Combat.MobileUnitSnapshot().Single().Unit.Id);
                var events = owner.PracticeBattle.Advance(3);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.Attack));
                Assert.AreEqual(0, owner.Combat.Game.LeakedEnemies);
                Assert.IsFalse(owner.HasSourceVerifiedDamageRuntime);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void CraftingAndSellingRareDuringPracticeUsesOneRosterAndWallet()
        {
            var root = new GameObject("ORDR live Rare growth");
            root.SetActive(false);
            try
            {
                var owner = root.AddComponent<OrdrCharacterCombatController>();
                typeof(OrdrCharacterCombatController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(owner, null);
                owner.BeginUnitPreparation();
                owner.Combat.SpawnCharacter("S-07", -4000, 4000);
                owner.Combat.SpawnCharacter("S-12", -4000, 4000);
                owner.Combat.SpawnCharacter("S-18", -4000, 4000);
                owner.BeginPracticeBattle();
                owner.PracticeBattle.Advance(.1);
                var offer = owner.ManualActions.PreviewFusion(owner.Combat.MobileUnitSnapshot())
                    .Single(x => x.RecipeId == "A00F");
                Assert.AreEqual(Ordr.Mobile.Mobile.ManualActionState.Succeeded,
                    owner.ManualActions.ConfirmFusion(offer).State);
                var made = owner.Combat.MobileUnitSnapshot().Single();
                Assert.AreEqual("R-06", made.Unit.Id);
                Assert.AreEqual(1, owner.Combat.ReadyAttackers);
                Assert.IsTrue(owner.PracticeBattle.Advance(3).Any(x => x.Type == CharacterCombatEventType.Attack));
                long before = owner.WispBalance(WispResourceIds.Random);
                long wood = owner.SharedEconomy.Wallet.Balance("wood");
                Assert.AreEqual(Ordr.Mobile.Mobile.ManualActionState.Succeeded,
                    owner.RequestLowerGradeSale(made.InstanceId).State);
                Assert.AreEqual(before + 2, owner.WispBalance(WispResourceIds.Random));
                Assert.AreEqual(wood + 1, owner.SharedEconomy.Wallet.Balance("wood"));
                Assert.AreEqual(0, owner.Combat.CharacterCount);
                Assert.AreEqual(0, owner.Combat.ReadyAttackers);
                Assert.IsFalse(owner.HasSourceVerifiedDamageRuntime);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void TwentyRoundPracticeCompletesWithTheSourceSpawnsAndOneTimeRoundRewards()
        {
            // Test-only formation. No free units are added to normal gameplay.
            using (var game = Create(35, 20, "L-29"))
            {
                for (int i = 0; i < 2000 && game.Combat.Game.Phase == SessionPhase.Running; i++)
                    game.Battle.Advance(.5);
                Assert.AreEqual(SessionPhase.Won, game.Combat.Game.Phase, game.Battle.ResultMessage);
                Assert.AreEqual(20, game.Combat.Game.CurrentWave);
                Assert.AreEqual(632, game.Combat.Game.DefeatedEnemies);
                Assert.AreEqual(38, game.Battle.RoundWispsAwarded);
                Assert.AreEqual(43, game.Economy.Wallet.Balance(WispResourceIds.Random));
                Assert.AreEqual(20, game.Battle.RoundHistory.Count);
                Assert.AreEqual(632, game.Battle.RoundHistory.Sum(x => x.Defeated));
                Assert.AreEqual(game.Battle.GoldAwarded, game.Battle.RoundHistory.Sum(x => x.GoldAwarded));
                Assert.AreEqual("20라운드 연습 완료", game.Battle.ResultMessage);
                long gold = game.Economy.Wallet.Balance("gold");
                game.Battle.Advance(100);
                Assert.AreEqual(gold, game.Economy.Wallet.Balance("gold"));
                Assert.AreEqual(20, game.Battle.RoundHistory.Count);
            }
        }

        [Test]
        public void PracticeLengthIsExplicitAndUnsupportedLengthsFailBeforeCombatMutation()
        {
            string json = Data("mobile-practice-battle-model.json");
            string standalone = Data("mobile-standalone-enemy-model.json");
            Assert.AreEqual(10, new SourceModelPracticeCatalog(json).LastRound);
            Assert.AreEqual(20, new SourceModelPracticeCatalog(json, 20).LastRound);
            Assert.Throws<ArgumentOutOfRangeException>(() => new SourceModelPracticeCatalog(json, 65));
            Assert.AreEqual(65, new SourceModelPracticeCatalog(standalone, 65).LastRound);
            Assert.Throws<ArgumentOutOfRangeException>(() => new SourceModelPracticeCatalog(standalone, 66));
            var root = new GameObject("ORDR practice length");
            root.SetActive(false);
            try
            {
                var owner = root.AddComponent<OrdrCharacterCombatController>();
                typeof(OrdrCharacterCombatController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(owner, null);
                owner.BeginUnitPreparation();
                owner.Combat.SpawnCharacter("C-01", -4000, 4000);
                long wisps = owner.WispBalance(WispResourceIds.Random);
                Assert.Throws<ArgumentOutOfRangeException>(() => owner.BeginPracticeBattle(42));
                Assert.AreEqual(0, owner.Combat.Game.CurrentWave);
                Assert.AreEqual(wisps, owner.WispBalance(WispResourceIds.Random));
                owner.BeginPracticeBattle(65);
                Assert.AreEqual(65, owner.PracticeBattle.LastRound);
                Assert.AreEqual(1, owner.Combat.Game.CurrentWave);
                Assert.IsFalse(owner.HasSourceVerifiedDamageRuntime);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void PracticeModelCannotBeRelabelledAsRetailVerified()
        {
            foreach (int length in new[] { 20, 65 })
            {
                string json = Data(length == 65
                    ? "mobile-standalone-enemy-model.json" : "mobile-practice-battle-model.json");
                Assert.Throws<FormatException>(() => new SourceModelPracticeCatalog(
                    json.Replace("\"retailParityVerified\": false", "\"retailParityVerified\": true"), length));
                Assert.Throws<FormatException>(() => new SourceModelPracticeCatalog(
                    json.Replace("\"safeForVerifiedMonsterWaveRuntime\": false", "\"safeForVerifiedMonsterWaveRuntime\": true"), length));
            }
        }

        [Test]
        public void AllSixtyFiveSourceWavesBuildWithoutAnExternalLiveWitness()
        {
            var identity = Original323MonsterWaveIdentityCatalog.FromJson(
                Data("original-2323C-monster-wave-script-evidence.json"));
            var catalog = new SourceModelPracticeCatalog(Data("mobile-standalone-enemy-model.json"), 65);
            double expectedMagicResistance;
            Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetNightmareBaselineResistance(
                out expectedMagicResistance));
            int monsters = 0;
            for (int wave = 1; wave <= 65; wave++)
            {
                var plan = catalog.BuildPracticePlan(wave, identity);
                Assert.IsTrue(identity.MatchesSinglePlayerSpawnCadence(plan), "wave " + wave);
                Assert.IsTrue(plan.Spawns.All(x => x.Health > 0), "wave " + wave);
                foreach (var enemy in plan.Spawns)
                {
                    Assert.AreEqual(Original323NightmareA04QSpawnState.NightmareInitialLevel,
                        enemy.Defenses.MagicAbilityLevel, "wave " + wave);
                    Assert.AreEqual(expectedMagicResistance,
                        enemy.Defenses.MagicResistancePercent.Value, 0.0001,
                        "wave " + wave);
                }
                monsters += plan.Spawns.Count;
                if (wave == 55 || wave == 60 || wave == 65)
                {
                    Assert.AreEqual(36, plan.Spawns.Count);
                    Assert.AreEqual(1, plan.Spawns.Count(x => x.Kind == CharacterEnemyKind.Boss));
                    Assert.AreEqual(35, plan.Spawns.Count(x => x.Kind == CharacterEnemyKind.Line));
                }
            }
            Assert.AreEqual(65 * 35 - 5 * 34 + 3, monsters);
            using (var game = Create(0, 65))
            {
                Assert.AreEqual(65, game.Battle.LastRound);
                Assert.AreEqual(1, game.Combat.Game.CurrentWave);
                Assert.AreEqual(21, game.Battle.PreparationSecondsRemaining, 1e-6);
                Assert.AreEqual(56, game.Battle.SecondsRemaining, 1e-6);
                game.Battle.Advance(.5);
                Assert.AreEqual(0, game.Combat.Game.AliveEnemies);
                Assert.AreEqual(20.5, game.Battle.PreparationSecondsRemaining, 1e-6);
                game.Battle.Advance(21.1);
                Assert.AreEqual(0, game.Battle.PreparationSecondsRemaining, 1e-6);
                Assert.AreEqual(1, game.Combat.Game.AliveEnemies);
                Assert.AreEqual(338, game.Combat.MobileEnemySnapshot().Single().Health);
                Assert.AreEqual(0, game.Combat.Game.LeakedEnemies);
            }
        }

        [Test]
        public void OnlyStandaloneSixtyFiveRoundUsesSourceVerifiedPreparationAndTransition()
        {
            var standalone = new SourceModelPracticeCatalog(
                Data("mobile-standalone-enemy-model.json"), 65);
            Assert.AreEqual(21, standalone.ModelLeadInSeconds(1));
            Assert.AreEqual(0, standalone.ModelLeadInSeconds(2));
            Assert.AreEqual(0, standalone.ModelLeadInSeconds(50));
            Assert.AreEqual(70, standalone.ModelLeadInSeconds(51));
            Assert.AreEqual(0, standalone.ModelLeadInSeconds(55));
            Assert.AreEqual(0, standalone.ModelLeadInSeconds(65));
            Assert.Throws<ArgumentOutOfRangeException>(() => standalone.ModelLeadInSeconds(66));

            var training = new SourceModelPracticeCatalog(Data("mobile-practice-battle-model.json"));
            Assert.AreEqual(0, training.ModelLeadInSeconds(1));
        }

        [Test]
        public void StandaloneMagicDefenseLevelCannotDriftOrBecomeRetailApproval()
        {
            string standalone = Data("mobile-standalone-enemy-model.json");
            Assert.Throws<FormatException>(() => new SourceModelPracticeCatalog(
                standalone.Replace("\"initialMagicAbilityLevel\": 16",
                    "\"initialMagicAbilityLevel\": 15"), 65));
            var oldPractice = new SourceModelPracticeCatalog(
                Data("mobile-practice-battle-model.json"));
            var identity = Original323MonsterWaveIdentityCatalog.FromJson(
                Data("original-2323C-monster-wave-script-evidence.json"));
            Assert.IsFalse(oldPractice.BuildPracticePlan(1, identity).Spawns[0]
                .Defenses.MagicResistancePercent.HasValue);
        }

        [Test]
        public void ControllerCanContinuePreparedRosterIntoPracticeWhileLiveStartStaysBlocked()
        {
            var root = new GameObject("ORDR practice integration");
            root.SetActive(false);
            try
            {
                var owner = root.AddComponent<OrdrCharacterCombatController>();
                typeof(OrdrCharacterCombatController).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(owner, null);
                owner.BeginUnitPreparation();
                long id = owner.Combat.SpawnCharacter("C-01", -4768, 3232);
                long balance = owner.WispBalance(WispResourceIds.Random);
                owner.BeginPracticeBattle();
                Assert.IsNotNull(owner.PracticeBattle);
                Assert.IsFalse(owner.HasSourceVerifiedDamageRuntime);
                Assert.IsFalse(owner.IsUnitPreparation);
                Assert.AreEqual(1, owner.Combat.Game.CurrentWave);
                Assert.AreEqual("C-01", owner.Combat.Roster.Get(id).UnitId);
                Assert.AreEqual(balance, owner.WispBalance(WispResourceIds.Random));
                Assert.AreEqual(1, owner.Combat.ReadyAttackers);
                owner.SetManualGameplayPause(true);
                typeof(OrdrCharacterCombatController).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(owner, null);
                Assert.AreEqual(0, owner.Combat.Time);
                Assert.Throws<InvalidOperationException>(() => owner.BeginPracticeBattle());
                Assert.Throws<InvalidOperationException>(() => owner.BeginVerifiedFirstWave());
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
