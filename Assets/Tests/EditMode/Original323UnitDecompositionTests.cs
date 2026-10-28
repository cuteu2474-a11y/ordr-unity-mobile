using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323UnitDecompositionTests
    {
        private sealed class Rolls : IOriginalWispRandom
        {
            public int Count = 2;
            public int Calls;
            public Action DuringDraw;
            public int NextInclusive(int min, int max)
            {
                Assert.AreEqual(2, min); Assert.AreEqual(3, max);
                Calls++; DuringDraw?.Invoke(); return Count;
            }
        }
        private sealed class FailSpawn : IOriginalVerifiedUnitAttackProvider
        {
            public string FailId;
            public int OnOccurrence = 1;
            private int occurrences;
            public bool TryResolve(CatalogUnit unit, CharacterSkillProfile profile, out OriginalVerifiedAttackBinding binding)
            {
                binding = null;
                if (unit.Id == FailId && ++occurrences == OnOccurrence) throw new InvalidOperationException("injected spawn failure");
                return false;
            }
        }
        private sealed class Live : IDisposable
        {
            public CharacterCombatRuntime Combat;
            public SharedEconomySession Economy;
            public OriginalMapBaseStatCatalog Map;
            public Original323UnitDecompositionService Service;
            public void Dispose() { Service.Dispose(); }
        }
        private static string Data(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", file));
        private static Live Create(IOriginalWispRandom random = null)
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(Data("original-2323C-map-base-stats.json"), units);
            var dice = new SeededNonOriginalProcRandom(1);
            var rules = new SourceModelBasicCombatRules(map, dice);
            var combat = new CharacterCombatRuntime(units, skills, rules, rules, dice);
            var shared = new SharedEconomySession(new EconomyWallet(), combat.Roster);
            shared.ApplyVerifiedStart(Original323SoloStartProfile.Create(),
                (id,x,y) => shared.Units.Add(id,x,y), id => shared.Units.Remove(id));
            combat.Start(); rules.Enable(); combat.ConfigureSourceModelBasicAttacks(rules);
            return new Live { Combat = combat, Economy = shared, Map = map,
                Service = new Original323UnitDecompositionService(combat, shared, map, random ?? new Rolls()) };
        }
        [Test]
        public void CaponeWaitsFiveSecondsThenAtomicallyCreatesThreeExactSourceResults()
        {
            var rng = new Rolls();
            using (var game = Create(rng))
            {
                long id = game.Combat.SpawnCharacter("S-02", 11, 22);
                long gold = game.Economy.Wallet.Balance("gold"), wood = game.Economy.Wallet.Balance("wood");
                Assert.AreEqual(ManualActionState.Succeeded, game.Service.Request(id).State);
                game.Service.Advance(4.999);
                Assert.AreEqual("S-02", game.Combat.Roster.Get(id).UnitId);
                Assert.AreEqual(1, game.Combat.CharacterCount);
                game.Service.Advance(.001);
                var rows = game.Combat.MobileUnitSnapshot();
                Assert.AreEqual(3, rows.Count);
                CollectionAssert.AreEquivalent(new[] { "U-13", "C-07", "C-08" }, rows.Select(r => r.Unit.Id));
                var fukuro = rows.Single(r => r.Unit.Id == "U-13");
                Assert.AreEqual(-3760, fukuro.Position.X); Assert.AreEqual(3904, fukuro.Position.Y);
                Assert.AreEqual(-3232, rows.Single(r => r.Unit.Id == "C-07").Position.X);
                Assert.AreEqual(-2976, rows.Single(r => r.Unit.Id == "C-08").Position.X);
                Assert.AreEqual(0, rng.Calls);
                Assert.AreEqual(gold, game.Economy.Wallet.Balance("gold"));
                Assert.AreEqual(wood, game.Economy.Wallet.Balance("wood"));
                game.Service.Advance(100);
                Assert.AreEqual(3, game.Combat.CharacterCount);
            }
        }
        [TestCase(2)]
        [TestCase(3)]
        public void BuggyUsesOneOriginalTwoToThreeDrawOnlyAfterTheTwoSecondCast(int count)
        {
            var rng = new Rolls { Count = count };
            using (var game = Create(rng))
            {
                long id = game.Combat.SpawnCharacter("S-11", 0, 0);
                game.Service.Request(id); game.Service.Advance(1.99);
                Assert.AreEqual(0, rng.Calls);
                Assert.AreEqual(1, game.Combat.CharacterCount);
                game.Service.Advance(.01);
                Assert.AreEqual(count, game.Combat.CharacterCount);
                Assert.AreEqual(1, rng.Calls);
                Assert.IsTrue(game.Combat.MobileUnitSnapshot().All(r => r.Unit.Id == "C-07" && r.Position.X == -3232 && r.Position.Y == 3232));
            }
        }
        [Test]
        public void CancelKeepsTheActorAndDoesNotDrawOrProduceSalePoints()
        {
            var rng = new Rolls();
            using (var game = Create(rng))
            {
                long id = game.Combat.SpawnCharacter("S-11", 2, 3);
                game.Combat.Roster.Select(id);
                game.Service.Request(id); game.Service.Advance(1);
                Assert.AreEqual(ManualActionState.Succeeded, game.Service.Cancel(id).State);
                game.Service.Advance(10);
                Assert.AreEqual("S-11", game.Combat.Roster.Get(id).UnitId);
                Assert.AreEqual(id, game.Combat.Roster.SelectedId);
                Assert.IsFalse(game.Combat.IsOriginalUnitCasting(id));
                Assert.AreEqual(0, rng.Calls);
            }
        }
        [Test]
        public void CastStopsOnlyItsOwnAttacksAndCancelRestoresTargeting()
        {
            using (var game = Create())
            {
                long caster = game.Combat.SpawnCharacter("S-11", 0, 0);
                long other = game.Combat.SpawnCharacter("C-01", 0, 0);
                game.Combat.BeginWave(1);
                game.Combat.SpawnEnemyWithDefenses("enemy", 100000, CharacterEnemyKind.Line, 1, 1, 0,
                    new EnemyDefenseProfile(CharacterEnemyKind.Line, 0, null, "normal", "model test"));
                game.Service.Request(caster);
                var hits = game.Combat.Step(.6);
                Assert.IsTrue(hits.Any(e => e.Type == CharacterCombatEventType.Attack && e.ActorId == other.ToString()));
                Assert.IsFalse(hits.Any(e => e.Type == CharacterCombatEventType.Attack && e.ActorId == caster.ToString()));
                game.Service.Cancel(caster);
                Assert.IsTrue(game.Combat.Step(.1).Any(e => e.Type == CharacterCombatEventType.Attack && e.ActorId == caster.ToString()));
            }
        }
        [Test]
        public void MovingAwayAndBackInterruptsTheSameCastWithoutSpawningAnything()
        {
            using (var game = Create())
            {
                long id = game.Combat.SpawnCharacter("S-02", 1, 2);
                game.Service.Request(id);
                game.Combat.MoveCharacter(id, 3, 4); game.Combat.MoveCharacter(id, 1, 2);
                game.Service.Advance(5);
                Assert.AreEqual(1, game.Combat.CharacterCount);
                Assert.AreEqual("S-02", game.Combat.Roster.Get(id).UnitId);
                Assert.AreEqual(0, game.Service.PendingCount);
            }
        }
        [Test]
        public void UnsupportedSaleKeepsTheCastButRemovingTheActorCancelsWithoutAnyReward()
        {
            var rng = new MobileRollbackWispRandom(42);
            using (var game = Create(rng))
            {
                long id = game.Combat.SpawnCharacter("S-11", 1, 2);
                var sales = new Original323LowerGradeSaleService(game.Combat, game.Economy, game.Map, rng);
                game.Service.Request(id);
                Assert.AreEqual(ManualActionState.Rejected, sales.Sell(id).State);
                Assert.IsTrue(game.Combat.IsOriginalUnitCasting(id));
                game.Combat.RemoveCharacter(id);
                int afterSale = rng.CaptureCheckpoint();
                game.Service.Advance(2);
                Assert.AreEqual(0, game.Combat.CharacterCount);
                Assert.AreEqual(afterSale, rng.CaptureCheckpoint());
                Assert.AreEqual(0, sales.CommonSold);
            }
        }
        [Test]
        public void FailureOnThirdOutputRemovesAllStagedResultsAndKeepsOriginalSelection()
        {
            using (var game = Create())
            {
                long id = game.Combat.SpawnCharacter("S-02", 1, 2);
                game.Combat.Roster.Select(id); game.Combat.Step(0);
                game.Combat.ConfigureVerifiedUnitAttackProvider(new FailSpawn { FailId = "C-08" });
                game.Service.Request(id); game.Service.Advance(5);
                Assert.AreEqual(1, game.Combat.CharacterCount);
                Assert.AreEqual(id, game.Combat.Roster.SelectedId);
                Assert.AreEqual("S-02", game.Combat.Roster.Get(id).UnitId);
                Assert.IsFalse(game.Combat.Step(0).Any(e => e.Type == CharacterCombatEventType.UnitSpawned));
                Assert.IsFalse(game.Combat.IsOriginalUnitCasting(id));
            }
        }
        [Test]
        public void FailedRandomConversionRewindsMobileStreamBeforeAFullRetry()
        {
            var rng = new MobileRollbackWispRandom(42);
            int expected = new MobileRollbackWispRandom(42).NextInclusive(2,3);
            using (var game = Create(rng))
            {
                long id = game.Combat.SpawnCharacter("S-11", 1, 2);
                game.Combat.ConfigureVerifiedUnitAttackProvider(new FailSpawn { FailId = "C-07", OnOccurrence = 2 });
                game.Service.Request(id); game.Service.Advance(2);
                Assert.AreEqual(0, rng.CaptureCheckpoint());
                Assert.AreEqual(1, game.Combat.CharacterCount);
                game.Combat.ConfigureVerifiedUnitAttackProvider(null);
                game.Service.Request(id); game.Service.Advance(2);
                Assert.AreEqual(expected, game.Combat.CharacterCount);
                Assert.AreEqual(1, rng.CaptureCheckpoint());
            }
        }
        [Test]
        public void DrawRunsUnderTheExclusiveUnitTransactionAndDuplicateRequestsDoNotMultiply()
        {
            var rng = new Rolls();
            using (var game = Create(rng))
            {
                long id = game.Combat.SpawnCharacter("S-11", 1, 2);
                rng.DuringDraw = () => {
                    Assert.Throws<InvalidOperationException>(() => game.Combat.Step(.1));
                    Assert.Throws<InvalidOperationException>(() => game.Combat.RemoveCharacter(id));
                };
                game.Service.Request(id);
                Assert.AreEqual(ManualActionState.Rejected, game.Service.Request(id).State);
                game.Service.Advance(2);
                Assert.AreEqual(2, game.Combat.CharacterCount);
                Assert.AreEqual(0, game.Combat.Time);
                Assert.AreEqual(1, rng.Calls);
            }
        }
        [Test]
        public void AbortedFusionPreservesTheCastAndCommittedFusionCancelsIt()
        {
            using (var game = Create())
            {
                long id = game.Combat.SpawnCharacter("S-02", 1, 2);
                game.Service.Request(id);
                using (var swap = game.Combat.BeginOriginalFusionUnitSwap(new[] { id }, "U-01", 1, 2)) { }
                Assert.IsTrue(game.Combat.IsOriginalUnitCasting(id));
                game.Service.Advance(5);
                Assert.AreEqual(3, game.Combat.CharacterCount);
            }
            using (var game = Create())
            {
                long id = game.Combat.SpawnCharacter("S-02", 1, 2);
                game.Service.Request(id);
                using (var swap = game.Combat.BeginOriginalFusionUnitSwap(new[] { id }, "U-01", 1, 2)) swap.Commit();
                game.Service.Advance(5);
                Assert.AreEqual(1, game.Combat.CharacterCount);
                Assert.AreEqual("U-01", game.Combat.MobileUnitSnapshot().Single().Unit.Id);
                Assert.AreEqual(0, game.Service.PendingCount);
            }
        }
        [Test]
        public void GameEndOrServiceDisposalCancelsThePendingCast()
        {
            using (var game = Create())
            {
                long id = game.Combat.SpawnCharacter("S-02", 0, 0);
                game.Service.Request(id); game.Combat.Game.Finish(false); game.Service.Advance(0);
                Assert.AreEqual(0, game.Service.PendingCount);
                Assert.IsFalse(game.Combat.IsOriginalUnitCasting(id));
                Assert.AreEqual(1, game.Combat.CharacterCount);
            }
            using (var game = Create())
            {
                long id = game.Combat.SpawnCharacter("S-02", 0, 0);
                game.Service.Request(id); game.Service.Dispose();
                Assert.IsFalse(game.Combat.IsOriginalUnitCasting(id));
                Assert.AreEqual(1, game.Combat.CharacterCount);
            }
        }
    }
}
