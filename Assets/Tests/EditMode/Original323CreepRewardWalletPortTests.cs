using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323CreepRewardWalletPortTests
    {
        private sealed class Dice : ICombatProcRandom
        { public int Next(int upper) => 0; }
        private sealed class SharedRoll : IOriginalWispRandom
        {
            public int Branch = 1;
            public int Calls;
            public int NextInclusive(int min, int max)
            {
                Assert.AreEqual(1, min);
                Assert.AreEqual(2, max);
                Calls++;
                return Branch;
            }
        }
        private sealed class Source : IOriginal323CreepSourceSpawnPort
        {
            public bool TryResolveOriginalSpawn(int stage, string oldId,
                out Original323CreepSourceSpawn spawn)
            {
                spawn = new Original323CreepSourceSpawn(stage,
                    stage == 1 ? "n00E" : stage == 2 ? "n00F" : "n00G",
                    "source-w3x:observed CTs-CTq", stage + 30,
                    new EnemyDefenseProfile(CharacterEnemyKind.Creep,
                        0, null, "normal", "original observed creep final"),
                    Original323CreepSourceProgression.SinglePlayerCreepX,
                    Original323CreepSourceProgression.SinglePlayerCreepY);
                return true;
            }
        }
        private sealed class Ship : IOriginal323CreepPirateShipPlacement
        {
            public bool Available;
            public int Calls;
            public bool TryGetCTqPirateShipSpawn(string enemy,
                out double x, out double y, out string evidence)
            {
                Calls++;
                x = -400; y = 700;
                evidence = "source-w3x:CTq h060 observed";
                return Available;
            }
        }
        private sealed class Live
        {
            public CharacterCombatRuntime Combat;
            public SharedEconomySession Economy;
            public OriginalMapBaseStatCatalog Map;
            public CharacterUnitCatalog Units;
        }
        private static string Data(string file) =>
            File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", file));
        private static Live Create()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(
                Data("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var dice = new Dice();
            var rules = new SourceModelBasicCombatRules(map, dice);
            var combat = new CharacterCombatRuntime(
                units, skills, rules, rules, dice);
            var economy = new SharedEconomySession(
                new EconomyWallet(), combat.Roster);
            economy.ApplyVerifiedStart(Original323SoloStartProfile.Create(),
                (id, x, y) => economy.Units.Add(id, x, y),
                id => economy.Units.Remove(id));
            combat.Start();
            combat.BeginWave(1);
            return new Live { Units = units, Map = map,
                Combat = combat, Economy = economy };
        }
        private static void Kill(CharacterCombatRuntime combat, long id,
            Original323CreepEncounter encounter)
        {
            var r = (CombatResolution)typeof(CharacterCombatRuntime)
                .GetField("resolution", BindingFlags.NonPublic |
                    BindingFlags.Instance).GetValue(combat);
            long applied;
            Assert.IsTrue(r.TryApplySkillDamage(id.ToString(), encounter.EnemyId,
                1000, out applied));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void ThreeOriginalDeathReceiptsSpendOnlyOneSharedRollAndNoDuplicateRewards(
            int branch)
        {
            var game = Create();
            long actor = game.Combat.SpawnCharacter("C-01", 0, 0);
            var rng = new SharedRoll { Branch = branch };
            var ship = new Ship { Available = true };
            using (var creeps = new Original323CreepPracticeWorld(game.Combat,
                Original323CreepObjectCatalog.FromJson(
                    Data("original-2323C-monster-object-stats.json"))))
            {
                var rewards = new Original323CreepRewardWalletPort(
                    game.Combat, game.Economy, rng, game.Units, game.Map, ship);
                using (var flow = new Original323CreepSourceProgression(
                    game.Combat, creeps, new Source(), rewards))
                {
                    long gold = game.Economy.Wallet.Balance("gold");
                    long wood = game.Economy.Wallet.Balance("wood");
                    Assert.IsTrue(flow.ObserveOriginalFirstSpawn("original CTs/LB receipt"));
                    Kill(game.Combat, actor, creeps.Encounters.Last());
                    flow.AdvancePending();
                    Assert.AreEqual(gold + 1000, game.Economy.Wallet.Balance("gold"));
                    Assert.AreEqual(wood + 1, game.Economy.Wallet.Balance("wood"));
                    Assert.AreEqual(2, creeps.Encounters.Count);
                    Kill(game.Combat, actor, creeps.Encounters.Last());
                    flow.AdvancePending();
                    Assert.AreEqual(3, creeps.Encounters.Count);
                    Assert.AreEqual(1, rng.Calls);
                    Assert.AreEqual(wood + (branch == 1 ? 3 : 8),
                        game.Economy.Wallet.Balance("wood"));
                    Assert.AreEqual(branch == 1 ? 1 : 0,
                        game.Combat.MobileUnitSnapshot().Count(u =>
                            u.Unit.Id ==
                                Original323SourceMaterialRewardWorld.PirateShipCatalogId));
                    Kill(game.Combat, actor, creeps.Encounters.Last());
                    flow.AdvancePending();
                    flow.AdvancePending();
                    Assert.AreEqual(3, flow.SettledRewardCount);
                    Assert.AreEqual(gold + 6000, game.Economy.Wallet.Balance("gold"));
                    Assert.AreEqual(wood + (branch == 1 ? 4 : 9),
                        game.Economy.Wallet.Balance("wood"));
                    Assert.AreEqual(1, game.Economy.Wallet.Balance(
                        SourceModelItemExcavation.OrdinaryStacks));
                    Assert.AreEqual(1, rng.Calls);
                    Assert.IsFalse(flow.ObserveOriginalFirstSpawn("original CTs/LB receipt"));
                }
            }
        }

        [Test]
        public void MissingCTqShipPositionDefersSameBranchWithoutFreeReroll()
        {
            var game = Create();
            long actor = game.Combat.SpawnCharacter("C-01", 0, 0);
            var dice = new SharedRoll { Branch = 1 };
            var location = new Ship();
            using (var creeps = new Original323CreepPracticeWorld(game.Combat,
                Original323CreepObjectCatalog.FromJson(
                    Data("original-2323C-monster-object-stats.json"))))
            {
                var rewards = new Original323CreepRewardWalletPort(
                    game.Combat, game.Economy, dice, game.Units, game.Map, location);
                using (var flow = new Original323CreepSourceProgression(
                    game.Combat, creeps, new Source(), rewards))
                {
                    Assert.IsTrue(flow.ObserveOriginalFirstSpawn("original CTs/LB receipt"));
                    Kill(game.Combat, actor, creeps.Encounters.Last());
                    flow.AdvancePending();
                    Kill(game.Combat, actor, creeps.Encounters.Last());
                    long before = game.Economy.Wallet.Balance("wood");
                    flow.AdvancePending();
                    flow.AdvancePending();
                    Assert.AreEqual(1, dice.Calls);
                    Assert.AreEqual(2, creeps.Encounters.Count);
                    Assert.AreEqual(1, flow.PendingRewardCount);
                    Assert.AreEqual(before, game.Economy.Wallet.Balance("wood"));
                    location.Available = true;
                    flow.AdvancePending();
                    Assert.AreEqual(3, creeps.Encounters.Count);
                    Assert.AreEqual(before + 2, game.Economy.Wallet.Balance("wood"));
                    Assert.AreEqual(1, dice.Calls);
                    Assert.AreEqual(1, game.Combat.MobileUnitSnapshot().Count(u =>
                        u.Unit.Id == Original323SourceMaterialRewardWorld.PirateShipCatalogId));
                }
            }
        }
    }
}
