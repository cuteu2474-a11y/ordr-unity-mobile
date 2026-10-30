using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323SoloStoryScenarioTests
    {
        private sealed class FirstDie : ICombatProcRandom
        {
            public int Next(int upper) { return 0; }
        }

        private static string Source(string name) =>
            File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", name));

        private static CharacterCombatRuntime Combat(out OriginalMapBaseStatCatalog map)
        {
            var units = CharacterUnitCatalog.FromJson(Source("units.json"));
            var skills = CharacterSkillCatalog.FromJson(Source("character-skill-profiles.json"));
            map = OriginalMapBaseStatCatalog.FromJson(
                Source("original-2323C-map-base-stats.json"), units);
            var random = new FirstDie();
            var rules = new SourceModelBasicCombatRules(map, random);
            var combat = new CharacterCombatRuntime(units, skills, rules, rules, random);
            combat.Start();
            return combat;
        }

        [Test]
        public void LoguetownSpawnsBeforeFirstWaveAndNeverConsumesWaveCapacity()
        {
            OriginalMapBaseStatCatalog map;
            var combat = Combat(out map);
            var catalog = Original323StoryBuildingCatalog.FromJson(
                Source("original-2323C-monster-object-stats.json"));
            using (var buildings = new Original323StoryBuildingPracticeWorld(combat, catalog))
            using (var story = new Original323SoloStoryScenario(combat,
                buildings, new SharedEconomySession(new EconomyWallet(), combat.Roster)))
            {
                story.Start();
                Assert.AreEqual(0, combat.Game.CurrentWave);
                Assert.AreEqual(1, story.ActiveStage);
                Assert.AreEqual(0, story.HighestDestroyedStage);
                Assert.AreEqual(0, combat.Game.AliveEnemies);
                var target = combat.MobileEnemySnapshot().Single();
                Assert.AreEqual(CharacterEnemyKind.Story, target.Kind);
                Assert.AreEqual(Original323SoloStoryScenario.BuildingX, target.X);
                Assert.AreEqual(Original323SoloStoryScenario.BuildingY, target.Y);
                Assert.AreEqual("n000", buildings.Encounters[0].Building.Rawcode);
                Assert.Throws<InvalidOperationException>(() => story.Start());
                Assert.Throws<InvalidOperationException>(() =>
                    combat.TryMoveEnemy(target.Id, 0, 0, 0));
            }
        }

        [Test]
        public void RoundThirtyFiveChecksTheDestroyedWanoNotNextSpawn()
        {
            OriginalMapBaseStatCatalog map;
            var combat = Combat(out map);
            using (var buildings = new Original323StoryBuildingPracticeWorld(combat,
                Original323StoryBuildingCatalog.FromJson(
                    Source("original-2323C-monster-object-stats.json"))))
            using (var story = new Original323SoloStoryScenario(combat, buildings,
                new SharedEconomySession(new EconomyWallet(), combat.Roster)))
            {
                story.Start();
                for (int round = 1; round < 35; round++)
                {
                    combat.BeginWave(round);
                    story.OnRoundStarted();
                    Assert.AreEqual(SessionPhase.Running, combat.Game.Phase);
                }
                combat.BeginWave(35);
                story.OnRoundStarted();
                Assert.AreEqual(SessionPhase.Lost, combat.Game.Phase);
                Assert.IsTrue(story.LostByStory);
                StringAssert.Contains("와노쿠니", story.LastNotice);
                Assert.AreEqual(0, combat.Game.DefeatedEnemies);
            }
        }

        [Test]
        public void OriginalJassManualEntryAndReturnUseFixedSoloPositions()
        {
            OriginalMapBaseStatCatalog map;
            var combat = Combat(out map);
            Assert.IsNotNull(map.Find("C-01").Attack1);
            var actor = combat.SpawnCharacter("C-01", -3776, 4480);
            var movement = new Original323SoloStoryMovement(combat, map);
            var dispatch = new Original323StoryManualDispatch(combat, movement);
            combat.ConfigureStoryDeployment(dispatch);
            Assert.AreEqual(ManualActionState.Succeeded,
                dispatch.Dispatch(actor).State);
            Assert.IsTrue(combat.IsStoryDeployed(actor));
            Assert.AreEqual(Original323SoloStoryMovement.SoloStoryX,
                combat.Roster.Get(actor).X);
            Assert.AreEqual(Original323SoloStoryMovement.SoloStoryY,
                combat.Roster.Get(actor).Y);
            Assert.AreEqual(ManualActionState.Rejected,
                dispatch.Dispatch(actor).State);
            Assert.AreEqual(ManualActionState.Succeeded,
                dispatch.Recall(actor).State);
            Assert.IsFalse(combat.IsStoryDeployed(actor));
            Assert.AreEqual(Original323SoloStoryMovement.SoloStartX,
                combat.Roster.Get(actor).X);
            Assert.AreEqual(Original323SoloStoryMovement.SoloStartY,
                combat.Roster.Get(actor).Y);
        }

        private sealed class TestOnlyHugeDamage : IOriginalDamageResolver
        {
            // TEST FIXTURE ONLY: never used in the shipping game.
            public long ResolveBasicDamage(BasicDamageRequest request, CombatEnemy target)
            {
                return 400000000L;
            }
        }

        private sealed class TestOnlyStoryAttacker : IOriginalVerifiedUnitAttackProvider
        {
            public bool TryResolve(CatalogUnit unit, CharacterSkillProfile detailed,
                out OriginalVerifiedAttackBinding original)
            {
                original = unit != null && unit.Id == "C-01"
                    ? new OriginalVerifiedAttackBinding(
                        new BasicAttackSpec(1, 1, DamageSchool.Physical, DamageTraits.None),
                        0, null, "TEST_FIXTURE: source model scenario integration")
                    : null;
                return original != null;
            }
        }

        [Test]
        public void FullFourteenStagePracticeUsesActualCombatKillsAndSinglePaymentReceipts()
        {
            var units = CharacterUnitCatalog.FromJson(Source("units.json"));
            var skills = CharacterSkillCatalog.FromJson(Source("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Source("original-2323C-map-base-stats.json"), units);
            var dummy = new SourceModelBasicCombatRules(map, new FirstDie());
            var combat = new CharacterCombatRuntime(
                units, skills, new TestOnlyHugeDamage(), dummy, new FirstDie());
            combat.ConfigureVerifiedUnitAttackProvider(new TestOnlyStoryAttacker());
            combat.Start();
            var actor = combat.SpawnCharacter("C-01", 0, 0);
            Assert.AreEqual(1, combat.ReadyAttackers);
            Assert.AreEqual(0, combat.BindExistingVerifiedAttacks());
            var movement = new Original323SoloStoryMovement(combat, map);
            var dispatch = new Original323StoryManualDispatch(combat, movement);
            combat.ConfigureStoryDeployment(dispatch);
            Assert.AreEqual(ManualActionState.Succeeded, dispatch.Dispatch(actor).State);

            var wallet = new EconomyWallet();
            using (var buildings = new Original323StoryBuildingPracticeWorld(combat,
                Original323StoryBuildingCatalog.FromJson(
                    Source("original-2323C-monster-object-stats.json"))))
            using (var story = new Original323SoloStoryScenario(combat, buildings,
                new SharedEconomySession(wallet, combat.Roster)))
            {
                story.Start();
                combat.BeginWave(1);
                for (int stage = 1; stage <= 13; stage++)
                {
                    Assert.AreEqual(stage, story.ActiveStage);
                    combat.Step(1.1);
                    story.Advance();
                    Assert.AreEqual(stage, story.HighestDestroyedStage);
                    Assert.AreEqual(0, combat.Game.AliveEnemies,
                        "Story targets must not affect wave overflow counts");
                }
                Assert.IsFalse(story.CompletedAllBuildings);
                Assert.AreEqual(13, buildings.DestroyedCount);
                Assert.AreEqual(13, buildings.Encounters.Count);
                double wait = story.EggheadWaitRemaining;
                Assert.Greater(wait, 119.0);
                combat.Step(wait - 0.1);
                story.Advance();
                Assert.AreEqual(13, story.ActiveStage);
                combat.Step(0.2);
                story.Advance();
                Assert.AreEqual(14, story.ActiveStage);
                Assert.Greater(story.EggheadDeadlineRemaining, 239);
                combat.Step(1.1);
                story.Advance();
                Assert.IsTrue(story.CompletedAllBuildings);
                Assert.AreEqual(14, story.HighestDestroyedStage);
                Assert.AreEqual(14, buildings.DestroyedCount);
                Assert.IsFalse(story.LostByStory);
                Assert.AreEqual(0, combat.Game.DefeatedEnemies);
                Assert.AreEqual(SessionPhase.Running, combat.Game.Phase,
                    "Story clear is not the 65R game victory");
                Assert.Greater(wallet.Balance("gold"), 0);
                Assert.Greater(wallet.Balance("wood"), 0);
                Assert.AreEqual(1, wallet.Balance(
                    Original323SoloStoryScenario.PracticeTranscendWisp));
                var oldGold = wallet.Balance("gold");
                story.Advance();
                Assert.AreEqual(oldGold, wallet.Balance("gold"),
                    "Completion cannot duplicate source-mode rewards");
            }
        }

        [Test]
        public void OriginalJassCwlBlocksEntryWithBossButNeverBlocksRecall()
        {
            OriginalMapBaseStatCatalog map;
            var combat = Combat(out map);
            var actor = combat.SpawnCharacter("C-01", -3776, 4480);
            var movement = new Original323SoloStoryMovement(combat, map);
            var dispatch = new Original323StoryManualDispatch(combat, movement);
            combat.ConfigureStoryDeployment(dispatch);
            Assert.AreEqual(ManualActionState.Succeeded,
                dispatch.Dispatch(actor).State);
            combat.BeginWave(1);
            combat.SpawnEnemy("boss:test", 100, CharacterEnemyKind.Boss, 1, 1, 0);
            Assert.IsFalse(movement.CanDispatch(actor));
            Assert.AreEqual(ManualActionState.Succeeded,
                dispatch.Recall(actor).State);
            Assert.AreEqual(ManualActionState.Rejected,
                dispatch.Dispatch(actor).State);
            Assert.IsTrue(combat.LeakEnemy("boss:test"));
            Assert.AreEqual(ManualActionState.Succeeded,
                dispatch.Dispatch(actor).State);
            combat.RemoveCharacter(actor);
            Assert.IsFalse(dispatch.IsDeployed(actor));
            Assert.AreEqual(0, dispatch.DeployedCount);
        }
    }
}
