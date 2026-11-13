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

        // S-09's Black Arrow uses an effect-only attacker. The permanent
        // U-14 from Absalom's separate original acquisition remains a roster unit.
        private static void TriggerMoriaBlackArrowModel(
            CharacterCombatRuntime world, long moriaId, string victimId)
        {
            const BindingFlags privateField = BindingFlags.Instance | BindingFlags.NonPublic;
            var characters = (System.Collections.IDictionary)typeof(CharacterCombatRuntime)
                .GetField("characters", privateField).GetValue(world);
            var enemies = (System.Collections.IDictionary)typeof(CharacterCombatRuntime)
                .GetField("enemies", privateField).GetValue(world);
            var method = typeof(CharacterCombatRuntime).GetMethod(
                "SpawnSourceModelMoriaZombie", privateField);
            Assert.IsNotNull(method);
            method.Invoke(world, new[] { characters[moriaId], enemies[victimId] });
        }

        private static System.Collections.Generic.List<SourceModelSummonState> MoriaModelEffects(
            CharacterCombatRuntime world)
        {
            var field = typeof(CharacterCombatRuntime).GetField(
                "sourceTemporarySummons", BindingFlags.Instance | BindingFlags.NonPublic);
            return (System.Collections.Generic.List<SourceModelSummonState>)field.GetValue(world);
        }

        [Test]
        public void MoriaKillEffectNeverCreatesARealZombieOrChangesPermanentAbsalomZombie()
        {
            using (var game = Create())
            {
                long moria = game.Combat.SpawnCharacter("S-09", 0, 0);
                long permanent = game.Combat.SpawnCharacter("U-14", 10, 0);
                // This is a controlled test target, not a verified wave spawn.
                // Basic attacks must reject unknown armor rather than invent
                // zero armor; provide an explicit TEST_ONLY defense fixture.
                game.Combat.SpawnEnemyWithDefenses("victim", 1000000000000,
                    CharacterEnemyKind.Line, 10, 10, 0,
                    new EnemyDefenseProfile(CharacterEnemyKind.Line, 0, 0,
                        "medium", "TEST_ONLY_MORIA_BLACK_ARROW_DEFENSE"));
                int before = game.Combat.CharacterCount;

                TriggerMoriaBlackArrowModel(game.Combat, moria, "victim");

                Assert.AreEqual(before, game.Combat.CharacterCount);
                Assert.AreEqual("U-14", game.Combat.Roster.Get(permanent).UnitId);
                Assert.AreEqual(0, game.Combat.PracticeSummonsOf(moria).Count,
                    "The transient Black Arrow is an effect, not a selectable summon.");
                var transient = MoriaModelEffects(game.Combat)
                    .Single(x => x.OwnerId == moria && x.IsVirtualAttackEffect);
                Assert.AreEqual("h00H", transient.SourceUnitRawcode);
                Assert.AreEqual(10, transient.ExpiresAt - game.Combat.Time, 0.00001);
                Assert.IsTrue(transient.HasIndependentCombat);
                double projectileX, projectileY;
                Assert.IsTrue(game.Combat.TryGetSourceMoriaBlackArrowProjectileOrigin(
                    "practice-summon:" + transient.Id,
                    out projectileX, out projectileY));
                Assert.AreEqual(10, projectileX);
                Assert.AreEqual(10, projectileY);
                Assert.IsFalse(game.Combat.TryGetSourceMoriaBlackArrowProjectileOrigin(
                    permanent.ToString(), out projectileX, out projectileY));
                var attacks = game.Combat.Step(5);
                Assert.IsTrue(attacks.Any(x => x.Type == CharacterCombatEventType.Attack &&
                    x.ActorId.StartsWith("practice-summon:", StringComparison.Ordinal)),
                    "The effect must deal ordinary source-backed attack damage.");
                Assert.AreEqual(before, game.Combat.CharacterCount);
                Assert.AreEqual("U-14", game.Combat.Roster.Get(permanent).UnitId);

                game.Combat.Step(6);
                Assert.IsFalse(game.Combat.TryGetSourceMoriaBlackArrowProjectileOrigin(
                    "practice-summon:" + transient.Id,
                    out projectileX, out projectileY));
                Assert.IsFalse(MoriaModelEffects(game.Combat)
                    .Any(x => x.OwnerId == moria && x.IsVirtualAttackEffect));
                Assert.AreEqual(before, game.Combat.CharacterCount);
                Assert.AreEqual("U-14", game.Combat.Roster.Get(permanent).UnitId);
            }
        }

        [Test]
        public void MoriaTransientAttackEndsWhenOwnerIsRemovedOrStartsDecoding()
        {
            using (var game = Create())
            {
                long moria = game.Combat.SpawnCharacter("S-09", 0, 0);
                game.Combat.SpawnEnemy("victim", 1000000000000,
                    CharacterEnemyKind.Line, 10, 10, 0);
                TriggerMoriaBlackArrowModel(game.Combat, moria, "victim");
                Assert.AreEqual(1, MoriaModelEffects(game.Combat)
                    .Count(x => x.IsVirtualAttackEffect));

                game.Combat.SetOriginalPoneglyphChanneling(moria, true);
                Assert.IsFalse(MoriaModelEffects(game.Combat)
                    .Any(x => x.IsVirtualAttackEffect),
                    "A paused/decoding owner must not keep an earlier lane's effect.");
                game.Combat.SetOriginalPoneglyphChanneling(moria, false);
                TriggerMoriaBlackArrowModel(game.Combat, moria, "victim");
                game.Combat.RemoveCharacter(moria);
                Assert.IsFalse(MoriaModelEffects(game.Combat)
                    .Any(x => x.IsVirtualAttackEffect),
                    "Removing the actual owner must cancel the transient combat effect.");
            }
        }

        // The production pipeline calls this immediately AFTER a
        // confirmed basic-hit receipt. CombatResolution sends DamageApplied
        // earlier, so these fixtures exercise both post-hit marking and
        // a subsequent different attacker's lethal receipt without Unity runs.
        private static void ApplyConfirmedMoriaMark(
            CharacterCombatRuntime world, long moriaId, string targetId)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var characters = (System.Collections.IDictionary)typeof(CharacterCombatRuntime)
                .GetField("characters", flags).GetValue(world);
            var enemies = (System.Collections.IDictionary)typeof(CharacterCombatRuntime)
                .GetField("enemies", flags).GetValue(world);
            var method = typeof(CharacterCombatRuntime).GetMethod(
                "ApplySourceMoriaBlackArrowAfterBasicHit", flags);
            Assert.IsNotNull(method);
            method.Invoke(world, new[] { characters[moriaId], enemies[targetId] });
        }

        private static long DealModelDamage(
            CharacterCombatRuntime world, long attacker, string victim, long amount)
        {
            var f = typeof(CharacterCombatRuntime).GetField(
                "resolution", BindingFlags.Instance | BindingFlags.NonPublic);
            var resolution = (CombatResolution)f.GetValue(world);
            long actual;
            Assert.IsTrue(resolution.TryApplySkillDamage(
                attacker.ToString(), victim, amount, out actual));
            return actual;
        }

        [Test]
        public void BlackArrowNativeMarkConvertsOnDifferentUnitKillDuringPointZeroFiveSeconds()
        {
            using (var game = Create())
            {
                long moria = game.Combat.SpawnCharacter("S-09", 0, 0);
                long permanent = game.Combat.SpawnCharacter("U-14", 10, 0);
                game.Combat.SpawnEnemy("marked", 1000000,
                    CharacterEnemyKind.Line, 10, 10, 0);
                int initialRoster = game.Combat.CharacterCount;
                Assert.AreEqual(10, DealModelDamage(game.Combat, moria, "marked", 10));
                ApplyConfirmedMoriaMark(game.Combat, moria, "marked");
                Assert.IsTrue(game.Combat.StatusOf("marked").Snapshot()
                    .Any(x => x.Type == CombatEffectType.SourceBlackArrowDeathMark &&
                              x.DurationSeconds == .05));
                game.Combat.Step(.02);
                Assert.AreEqual(999990, DealModelDamage(
                    game.Combat, permanent, "marked", 999990));
                var effect = MoriaModelEffects(game.Combat)
                    .Single(x => x.OwnerId == moria && x.IsVirtualAttackEffect);
                Assert.AreEqual("h00H", effect.SourceUnitRawcode);
                Assert.AreEqual(1, MoriaModelEffects(game.Combat)
                    .Count(x => x.IsVirtualAttackEffect));
                Assert.AreEqual(initialRoster, game.Combat.CharacterCount);
                Assert.AreEqual("U-14", game.Combat.Roster.Get(permanent).UnitId);
                Assert.IsFalse(game.Combat.StatusOf("marked").Snapshot()
                    .Any(x => x.Type == CombatEffectType.SourceBlackArrowDeathMark));
            }
        }

        [Test]
        public void BlackArrowExpiresBeforeLaterKillAndNeverCreatesAPermanentMaterial()
        {
            using (var game = Create())
            {
                long moria = game.Combat.SpawnCharacter("S-09", 0, 0);
                long ally = game.Combat.SpawnCharacter("U-14", 10, 0);
                game.Combat.SpawnEnemy("marked", 1000000,
                    CharacterEnemyKind.Line, 10, 10, 0);
                DealModelDamage(game.Combat, moria, "marked", 1);
                ApplyConfirmedMoriaMark(game.Combat, moria, "marked");
                game.Combat.Step(.051);
                Assert.IsFalse(game.Combat.StatusOf("marked").Snapshot()
                    .Any(x => x.Type == CombatEffectType.SourceBlackArrowDeathMark));
                DealModelDamage(game.Combat, ally, "marked", 999999);
                Assert.IsFalse(MoriaModelEffects(game.Combat)
                    .Any(x => x.IsVirtualAttackEffect));
                Assert.AreEqual(2, game.Combat.CharacterCount);
                Assert.AreEqual(0, game.Combat.MoriaZombieMaterials);
            }
        }

        [Test]
        public void BlackArrowMarkIsClearedOnOwnerRemovalAndDecodingLaneChange()
        {
            using (var game = Create())
            {
                long removedMoria = game.Combat.SpawnCharacter("S-09", 0, 0);
                long survivor = game.Combat.SpawnCharacter("U-14", 10, 0);
                game.Combat.SpawnEnemy("removed-owner", 1000,
                    CharacterEnemyKind.Line, 10, 10, 0);
                DealModelDamage(game.Combat, removedMoria, "removed-owner", 1);
                ApplyConfirmedMoriaMark(game.Combat, removedMoria, "removed-owner");
                game.Combat.RemoveCharacter(removedMoria);
                Assert.IsFalse(game.Combat.StatusOf("removed-owner").Snapshot()
                    .Any(x => x.Type == CombatEffectType.SourceBlackArrowDeathMark));
                DealModelDamage(game.Combat, survivor, "removed-owner", 999);
                Assert.IsFalse(MoriaModelEffects(game.Combat)
                    .Any(x => x.IsVirtualAttackEffect));

                long decoding = game.Combat.SpawnCharacter("S-09", 0, 0);
                game.Combat.SpawnEnemy("decode-owner", 1000,
                    CharacterEnemyKind.Line, 10, 10, 0);
                DealModelDamage(game.Combat, decoding, "decode-owner", 1);
                ApplyConfirmedMoriaMark(game.Combat, decoding, "decode-owner");
                game.Combat.SetOriginalPoneglyphChanneling(decoding, true);
                Assert.IsFalse(game.Combat.StatusOf("decode-owner").Snapshot()
                    .Any(x => x.Type == CombatEffectType.SourceBlackArrowDeathMark));
                DealModelDamage(game.Combat, survivor, "decode-owner", 999);
                Assert.IsFalse(MoriaModelEffects(game.Combat)
                    .Any(x => x.IsVirtualAttackEffect));
            }
        }

        [Test]
        public void BlackArrowLethalBasicHitConvertsOnceAndBossDoesNotReceiveMark()
        {
            using (var game = Create())
            {
                long moria = game.Combat.SpawnCharacter("S-09", 0, 0);
                game.Combat.SpawnEnemy("boss", 1000,
                    CharacterEnemyKind.Boss, 10, 10, 0);
                DealModelDamage(game.Combat, moria, "boss", 1);
                ApplyConfirmedMoriaMark(game.Combat, moria, "boss");
                Assert.IsFalse(game.Combat.StatusOf("boss").Snapshot()
                    .Any(x => x.Type == CombatEffectType.SourceBlackArrowDeathMark));
                DealModelDamage(game.Combat, moria, "boss", 999);
                Assert.IsFalse(MoriaModelEffects(game.Combat)
                    .Any(x => x.IsVirtualAttackEffect));

                game.Combat.SpawnEnemy("lethal", 1000,
                    CharacterEnemyKind.Line, 10, 10, 0);
                DealModelDamage(game.Combat, moria, "lethal", 1000);
                ApplyConfirmedMoriaMark(game.Combat, moria, "lethal");
                Assert.AreEqual(1, MoriaModelEffects(game.Combat)
                    .Count(x => x.IsVirtualAttackEffect));
                ApplyConfirmedMoriaMark(game.Combat, moria, "lethal");
                Assert.AreEqual(1, MoriaModelEffects(game.Combat)
                    .Count(x => x.IsVirtualAttackEffect));
                Assert.AreEqual(1, game.Combat.CharacterCount);
            }
        }

        [Test]
        public void DispellingBlackArrowBeforeDeathPreventsConversion()
        {
            using (var game = Create())
            {
                long moria = game.Combat.SpawnCharacter("S-09", 0, 0);
                long ally = game.Combat.SpawnCharacter("U-14", 10, 0);
                game.Combat.SpawnEnemy("dispelled", 1000,
                    CharacterEnemyKind.Line, 10, 10, 0);
                DealModelDamage(game.Combat, moria, "dispelled", 1);
                ApplyConfirmedMoriaMark(game.Combat, moria, "dispelled");
                game.Combat.StatusOf("dispelled").RemoveSource(
                    "practice-native:A087:BNba:" + moria);
                DealModelDamage(game.Combat, ally, "dispelled", 999);
                Assert.IsFalse(MoriaModelEffects(game.Combat)
                    .Any(x => x.IsVirtualAttackEffect));
            }
        }

        [Test]
        public void IndependentCreepsDoNotJoinWavePathOrLineLossCounter()
        {
            using (var game = Create(5))
            using (var creeps = new Original323CreepPracticeWorld(game.Combat,
                Original323CreepObjectCatalog.FromJson(
                    Data("original-2323C-monster-object-stats.json"))))
            {
                var creep = creeps.SpawnPracticeStage(1, -8000, -8000);
                Assert.AreEqual(0, game.Combat.Game.AliveEnemies);
                var before = game.Combat.MobileEnemySnapshot()
                    .Single(x => x.Id == creep.EnemyId);
                Assert.AreEqual(360000, before.Health);
                Assert.DoesNotThrow(() => game.Battle.Advance(2));
                var after = game.Combat.MobileEnemySnapshot()
                    .Single(x => x.Id == creep.EnemyId);
                Assert.AreEqual(before.X, after.X);
                Assert.AreEqual(before.Y, after.Y);
                Assert.AreEqual(360000, after.Health,
                    "Without the user's creep hunter list, even ready attackers must not target creeps.");
                Assert.AreEqual(game.Combat.MobileEnemySnapshot()
                    .Count(x => x.Kind != CharacterEnemyKind.Creep),
                    game.Combat.Game.AliveEnemies);
                Assert.AreEqual(0, creeps.DefeatedCount);
                Assert.AreEqual(0, creeps.EscapedCount);
                Assert.IsTrue(game.Combat.LeakEnemy(creep.EnemyId));
                Assert.AreEqual(0, game.Combat.Game.LeakedEnemies);
                Assert.AreEqual(Original323CreepEncounterState.Escaped, creep.State);
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
        public void MovingMonstersExposeRealRouteProgressForFrontTargetPriority()
        {
            using (var game = Create())
            {
                game.Battle.Advance(.5);
                var born = game.Combat.MobileEnemySnapshot().Single();
                Assert.AreEqual(0, born.PathProgress, 1e-12);
                game.Battle.Advance(.1);
                var moving = game.Combat.MobileEnemySnapshot().Single();
                Assert.Greater(moving.PathProgress, born.PathProgress);
                Assert.Less(moving.PathProgress, 1);
                Assert.AreEqual(moving.Id, CombatTargeting.Select(
                    new[] {
                        new TargetCandidate("back", 0, born.PathProgress, true),
                        new TargetCandidate(moving.Id, 100, moving.PathProgress, true)
                    }, 0, TargetPriority.FurthestAlongPath, true));
                Assert.AreEqual(0, game.Combat.PendingSimulationSeconds);
            }
            var catalog = new SourceModelPracticeCatalog(
                Data("mobile-practice-battle-model.json"));
            var corners = catalog.RouteCorners;
            double lap = 0;
            for (int i = 0; i < corners.Count; i++)
            {
                var from = corners[i];
                var to = corners[(i + 1) % corners.Count];
                double dx = to.X - from.X, dy = to.Y - from.Y;
                lap += Math.Sqrt(dx * dx + dy * dy);
            }
            Assert.AreEqual(.25, catalog.PathProgress(lap * .25), 1e-9);
            Assert.AreEqual(.25, catalog.PathProgress(lap * 1.25), 1e-9);
            Assert.AreEqual(0, catalog.PathProgress(lap), 1e-9);
            Assert.Throws<ArgumentOutOfRangeException>(() => catalog.PathProgress(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                catalog.PathProgress(double.NaN));
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
        public void HigherGradeUnitsJoinTheSamePracticeAttackPipelineWithOnlyRegisteredSkills(string unitId)
        {
            using (var game = Create())
            {
                long id = game.Combat.SpawnCharacter(unitId, -3760, 3904);
                Assert.AreEqual(1, game.Combat.ReadyAttackers);
                Assert.IsFalse(game.Combat.HasVerifiedAttack(id));
                var events = game.Battle.Advance(3);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.Attack));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.EnemyDefeated));
                // S-09, R-06 (original Boo/Bon) and L-29 acquired source-backed
                // attack callbacks; only S-01 here still has no registered skill.
                if (unitId == "S-01")
                    Assert.IsFalse(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated ||
                        x.Type == CharacterCombatEventType.SkillDamage));
                if (unitId == "R-06")
                    Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated ||
                        x.Type == CharacterCombatEventType.SkillDamage),
                        "R-06's registered Boo/Bon attack callback must reach the practice combat ledger");
                Assert.IsFalse(events.Any(x => x.Type == CharacterCombatEventType.ZombieMaterialEarned));
                Assert.AreEqual(0, game.Combat.MoriaZombieMaterials);
                Assert.AreEqual(game.Combat.Game.DefeatedEnemies,
                    events.Count(x => x.Type == CharacterCombatEventType.EnemyDefeated));
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
                owner.BeginStandaloneBattle();
                owner.PracticeBattle.Advance(21.1);
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
                owner.BeginStandaloneBattle();
                owner.PracticeBattle.Advance(21.1);
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
                Assert.AreEqual(0, owner.Combat.Game.CurrentWave);
                Assert.AreEqual(wisps, owner.WispBalance(WispResourceIds.Random));
                owner.BeginStandaloneBattle();
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
                owner.BeginStandaloneBattle();
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
                Assert.Throws<InvalidOperationException>(() => owner.BeginStandaloneBattle());
                Assert.Throws<InvalidOperationException>(() => owner.BeginVerifiedFirstWave());
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
