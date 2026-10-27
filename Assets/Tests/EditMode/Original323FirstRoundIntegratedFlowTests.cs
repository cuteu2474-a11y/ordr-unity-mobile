using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;
using Ordr.Mobile.Unity;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // Entire first-play acceptance using the *existing* production controller,
    // wave director, Wisp source and native attack binding. The two fake
    // approvals and fixture enemy HP below are NEVER production proof.
    public sealed class Original323FirstRoundIntegratedFlowTests
    {
        private sealed class FixtureDice : ICombatProcRandom
        {
            public int Next(int exclusiveUpper) { return 0; }
        }

        private sealed class FixtureQuantizer : IOrdr323VerifiedDamageQuantization
        {
            public long Quantize(double damage, Ordr323VerifiedDamageHit hit)
            {
                return (long)Math.Truncate(damage);
            }
        }

        private sealed class FixtureNativePath : IOriginalCommonNativeAttackVerification
        {
            public bool TryVerify(string unitId, string rawcode, out double delay,
                out DamageSchool school, out string evidence)
            {
                delay = .35;
                school = DamageSchool.Physical;
                evidence = "INTEGRATION_FIXTURE: not verified retail Warcraft combat";
                return unitId == "C-01" && rawcode == "h003";
            }
        }

        private sealed class FixtureRandom : IOriginalWispRandom
        {
            private readonly Queue<int> values = new Queue<int>(new[] { 10000, 0 });
            public int NextInclusive(int low, int high)
            {
                if (values.Count == 0)
                    throw new InvalidOperationException("Unexpected JASS Wisp draw");
                int value = values.Dequeue();
                if (value < low || value > high)
                    throw new InvalidOperationException("Wisp test fixture range mismatch");
                return value;
            }
        }

        private sealed class FixtureLiveApproval : IOriginal323LiveMonsterWaveEvidence
        {
            public bool Allow = true;
            public int Calls;
            public bool TryApprove(VerifiedWavePlan plan, out string proof)
            {
                Calls++;
                proof = "INTEGRATION_FIXTURE: external reviewer approves TEST RUN only";
                return Allow;
            }
        }

        // Synthetic source observer tests the *existing* post-kill wallet
        // wiring. It is not a genuine Warcraft event, native BFh seed,
        // player gold bonus or original RNG sequence.
        private sealed class FixtureGoldDeathWitness :
            IOriginal323MonsterGoldDeathWitness
        {
            public string ApprovedEnemyId;
            public int Calls;

            public bool TryConfirmOriginalGoldDeath(int wave, string enemyId,
                string rawcode, out int playerSlot, out int deathRound,
                out int roll, out float goldBonus, out int observedGold,
                out string proof)
            {
                Calls++;
                playerSlot = 0;
                deathRound = 1;
                roll = 3;
                goldBonus = 0f;
                observedGold = 2;
                proof = "original CVM proof supplied by an isolated integration test";
                return wave == 1 && enemyId == ApprovedEnemyId &&
                    rawcode == "o00I";
            }
        }

        private static OrdrCharacterCombatController CreateController(GameObject root,
            FixtureLiveApproval approval, FixtureGoldDeathWitness gold = null)
        {
            root.SetActive(false);
            var controller = root.AddComponent<OrdrCharacterCombatController>();
            var awake = typeof(OrdrCharacterCombatController).GetMethod(
                "Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(awake);
            awake.Invoke(controller, null);
            Assert.IsNotNull(controller.OriginalMonsterSpawnIdentities);
            Assert.IsNotNull(controller.OriginalMonsterObjectAbilities);
            controller.ConfigureRandomWispResourceRng(new FixtureRandom());
            controller.ConfigureVerifiedLiveMonsterWaves(approval);
            if (gold != null) controller.ConfigureOriginalMonsterGoldEvents(gold);
            controller.ConfigureOriginalCommonNativeDamageWhenVerified(
                new FixtureNativePath(), null, new FixtureQuantizer(),
                new FixtureDice());
            Assert.IsNotNull(controller.Combat);
            return controller;
        }

        private static VerifiedWavePlan MakeFixtureRoundOne(
            Original323MonsterWaveIdentityCatalog original, long fixtureHealth = 1000)
        {
            return MakeFixtureWave(original, 1, fixtureHealth);
        }

        private static VerifiedWavePlan MakeFixtureWave(
            Original323MonsterWaveIdentityCatalog original, int wave,
            long fixtureHealth = 1000)
        {
            var originalTimers = original.BuildSinglePlayerSpawnTemplates(wave);
            Assert.AreEqual(35, originalTimers.Count);
            var spawns = new List<VerifiedWaveEnemy>();
            foreach (var timer in originalTimers)
            {
                // Fixture 1,000 HP/10 armor/coordinates do NOT represent
                // actual Nightmare + Otherworld final live stats.
                spawns.Add(new VerifiedWaveEnemy(
                    timer.AtSeconds, timer.InstanceId, fixtureHealth, timer.Kind,
                    20, 0, 0, new EnemyDefenseProfile(
                        timer.Kind, 10, null, "normal",
                        "INTEGRATION_FIXTURE: not verified monster defense"),
                    timer.Rawcode));
            }
            return new VerifiedWavePlan(wave, spawns,
                "INTEGRATION_FIXTURE: source wave schedule verification only");
        }

        [Test]
        public void SourceWiredFirstRoundCanStartRecruitWispAndDamageRealCombatEnemy()
        {
            var root = new GameObject("ORDR isolated first-round integration fixture");
            try
            {
                var approval = new FixtureLiveApproval();
                var controller = CreateController(root, approval);
                var first = MakeFixtureRoundOne(controller.OriginalMonsterSpawnIdentities);
                controller.RegisterVerifiedLiveMonsterWavePlan(first);

                Assert.AreEqual(0, controller.GetFirstWaveBlockers().Count,
                    "Complete fixture wiring should clear structural blockers.");
                Assert.IsTrue(controller.CanBeginVerifiedFirstWave(out _));
                Assert.AreEqual(0, approval.Calls,
                    "GUI readiness must not consume the live original event.");

                controller.BeginVerifiedFirstWave();
                Assert.AreEqual(SessionPhase.Running, controller.Combat.Game.Phase);
                Assert.AreEqual(1, controller.Combat.Game.CurrentWave);
                Assert.AreEqual(60, controller.SharedEconomy.Wallet.Balance("gold"));
                Assert.AreEqual(5, controller.SharedEconomy.Wallet.Balance(
                    WispResourceIds.Random));
                Assert.AreEqual(1, controller.SharedEconomy.Units.Count,
                    "Pinned e0HP evolution actor is not an ordinary attacker.");
                Assert.AreEqual(0, controller.Combat.CharacterCount);
                Assert.Greater(approval.Calls, 0);

                controller.VerifiedWaveDirector.Step(controller.Combat, .5);
                Assert.AreEqual(1, controller.Combat.Game.AliveEnemies);
                Assert.AreEqual(34, controller.VerifiedWaveDirector.RemainingSpawns);
                var firstMonster = first.Spawns[0];
                Assert.AreEqual("o00I", firstMonster.OriginalRawcode);
                Assert.AreEqual(1000, controller.Combat.EnemyOf(firstMonster.EnemyId).Health);

                var draw = controller.RequestVerifiedCommonWispRecruitment();
                Assert.AreEqual(ManualActionState.Succeeded, draw.State);
                Assert.AreEqual(4, controller.SharedEconomy.Wallet.Balance(
                    WispResourceIds.Random));
                Assert.AreEqual(60, controller.SharedEconomy.Wallet.Balance("gold"));
                Assert.AreEqual(2, controller.SharedEconomy.Units.Count);
                Assert.AreEqual(1, controller.Combat.CharacterCount);
                Assert.AreEqual(1, controller.Combat.ReadyAttackers);
                Assert.AreEqual("C-01", controller.Combat.MobileUnitSnapshot()[0].Unit.Id);

                controller.VerifiedWaveDirector.Step(controller.Combat, .35);
                Assert.Less(controller.Combat.EnemyOf(firstMonster.EnemyId).Health, 1000,
                    "The existing unbounded native attack should affect live enemy HP.");
                Assert.AreEqual(1, controller.Combat.Game.CurrentWave);
                Assert.AreEqual(34, controller.VerifiedWaveDirector.RemainingSpawns);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ExistingFirstRoundKillCreditsSameWalletOnlyWithObservedDeath()
        {
            var root = new GameObject("ORDR first-round death and economy fixture");
            try
            {
                var approval = new FixtureLiveApproval();
                var observedGold = new FixtureGoldDeathWitness();
                var controller = CreateController(root, approval, observedGold);
                // Intentionally LOW, synthetic monster HP ensures two native
                // object attack events produce a real CombatResolution death.
                // This does NOT constitute real Nightmare mode balance.
                var plan = MakeFixtureRoundOne(
                    controller.OriginalMonsterSpawnIdentities, 30L);
                observedGold.ApprovedEnemyId = plan.Spawns[0].EnemyId;
                controller.RegisterVerifiedLiveMonsterWavePlan(plan);
                controller.BeginVerifiedFirstWave();
                controller.VerifiedWaveDirector.Step(controller.Combat, .5);

                Assert.AreEqual(60L,
                    controller.SharedEconomy.Wallet.Balance("gold"));
                Assert.AreEqual(0, observedGold.Calls,
                    "Monster spawn itself must not trigger a death-only gold roll.");
                var summoned = controller.RequestVerifiedCommonWispRecruitment();
                Assert.AreEqual(ManualActionState.Succeeded, summoned.State);
                Assert.AreEqual(4L, controller.SharedEconomy.Wallet.Balance(
                    WispResourceIds.Random));
                Assert.AreEqual(1, controller.Combat.ReadyAttackers);

                string originalRawcode;
                bool killed = false;
                for (int frame = 0; frame < 20; frame++)
                {
                    controller.VerifiedWaveDirector.Step(controller.Combat, .5);
                    if (controller.VerifiedWaveDirector.TryGetDefeatedMonsterRawcode(
                        1, observedGold.ApprovedEnemyId, out originalRawcode))
                    {
                        killed = true;
                        Assert.AreEqual("o00I", originalRawcode);
                        break;
                    }
                }

                Assert.IsTrue(killed,
                    "The actual auto-attacker must kill a scheduled enemy.");
                var progress = controller.VerifiedWaveDirector.CurrentProgress;
                Assert.GreaterOrEqual(progress.Spawned, 1);
                Assert.AreEqual(1, progress.Defeated);
                Assert.AreEqual(0, progress.Escaped);
                Assert.AreEqual(1, observedGold.Calls);
                Assert.AreEqual(62L,
                    controller.SharedEconomy.Wallet.Balance("gold"),
                    "Verified death must credit the same economy wallet that held starting gold.");
                Assert.AreEqual(4L, controller.SharedEconomy.Wallet.Balance(
                    WispResourceIds.Random));
                int duplicatePayout;
                Assert.IsFalse(controller.TryProcessObservedMonsterGold(
                    1, observedGold.ApprovedEnemyId, out duplicatePayout),
                    "A processed corpse cannot pay twice.");
                Assert.AreEqual(0, duplicatePayout);
                Assert.AreEqual(62L,
                    controller.SharedEconomy.Wallet.Balance("gold"));
                Assert.AreEqual(1, controller.Combat.Game.CurrentWave,
                    "A kill does not invent an original timer-driven next wave.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

#if UNITY_EDITOR
        [Test]
        public void EditorPracticeDoesNotBypassOriginalRoundObservationByDefault()
        {
            var root = new GameObject("ORDR original gate remains strict");
            root.SetActive(false);
            try
            {
                var controller = CreateController(root, new FixtureLiveApproval());
                controller.RegisterVerifiedLiveMonsterWavePlans(new[]
                {
                    MakeFixtureWave(controller.OriginalMonsterSpawnIdentities, 1),
                    MakeFixtureWave(controller.OriginalMonsterSpawnIdentities, 2)
                });
                controller.BeginVerifiedFirstWave();
                controller.VerifiedWaveDirector.Step(controller.Combat, 17.5);

                Assert.AreEqual(0, controller.VerifiedWaveDirector.RemainingSpawns);
                Assert.IsFalse(controller.TryBeginNextObservedMonsterWave(),
                    "A real round never advances without a source witness.");
                Assert.Throws<InvalidOperationException>(() =>
                    controller.BeginEditorPreviewWave(2),
                    "An ordinary session cannot enter the preview bypass.");
                Assert.Throws<InvalidOperationException>(() =>
                    controller.MarkEditorPreview(),
                    "Running games cannot be retroactively marked as practice.");
                Assert.AreEqual(1, controller.Combat.Game.CurrentWave);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PracticeSpeedIsAvailableOnlyAfterExplicitEditorSessionStart()
        {
            var root = new GameObject("Editor-only speed control fixture");
            root.SetActive(false);
            float originalScale = Time.timeScale;
            try
            {
                var controller = CreateController(root, new FixtureLiveApproval());
                Assert.Throws<InvalidOperationException>(() =>
                    controller.SetEditorPreviewSpeed(2),
                    "Ordinary sessions cannot enable fast-forward.");
                controller.RegisterVerifiedLiveMonsterWavePlan(
                    MakeFixtureRoundOne(controller.OriginalMonsterSpawnIdentities));
                controller.MarkEditorPreview();
                Assert.Throws<InvalidOperationException>(() =>
                    controller.SetEditorPreviewSpeed(2),
                    "Practice must enter a running session first.");
                controller.BeginVerifiedFirstWave();
                Assert.AreEqual(1, controller.EditorPreviewSpeed);
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    controller.SetEditorPreviewSpeed(3));
                Assert.AreEqual(1, controller.EditorPreviewSpeed);
                controller.SetEditorPreviewSpeed(4);
                Assert.AreEqual(4, controller.EditorPreviewSpeed);
                Assert.AreEqual(4f, Time.timeScale);
                controller.SetEditorPreviewSpeed(2);
                Assert.AreEqual(2f, Time.timeScale);
                controller.SetManualGameplayPause(true);
                Assert.IsTrue(controller.IsGameplayPaused);
                controller.SetEditorPreviewSpeed(1);
                Assert.AreEqual(1f, Time.timeScale);
                Assert.IsTrue(controller.IsGameplayPaused,
                    "Fast forward must not bypass the manual pause.");
            }
            finally
            {
                Time.timeScale = originalScale;
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ExplicitEditorPracticeAdvancesOnlyTwoSequentialCompletedSpawnQueues()
        {
            var root = new GameObject("ORDR 3-round Editor-only practice");
            root.SetActive(false);
            try
            {
                var controller = CreateController(root, new FixtureLiveApproval());
                controller.RegisterVerifiedLiveMonsterWavePlans(new[]
                {
                    MakeFixtureWave(controller.OriginalMonsterSpawnIdentities, 1),
                    MakeFixtureWave(controller.OriginalMonsterSpawnIdentities, 2),
                    MakeFixtureWave(controller.OriginalMonsterSpawnIdentities, 3)
                });
                controller.MarkEditorPreview();
                controller.BeginVerifiedFirstWave();
                Assert.Throws<InvalidOperationException>(() =>
                    controller.BeginEditorPreviewWave(2),
                    "Cannot start wave 2 while wave 1 is still spawning.");

                controller.VerifiedWaveDirector.Step(controller.Combat, 17.5);
                Assert.AreEqual(35, controller.VerifiedWaveDirector.CurrentProgress.Spawned);
                controller.BeginEditorPreviewWave(2);
                Assert.AreEqual(2, controller.Combat.Game.CurrentWave);
                Assert.AreEqual(35, controller.VerifiedWaveDirector.RemainingSpawns);
                Assert.AreEqual(35, controller.Combat.Game.AliveEnemies);
                Assert.Throws<InvalidOperationException>(() =>
                    controller.BeginEditorPreviewWave(3),
                    "Cannot skip unfinished wave-2 spawns.");

                controller.VerifiedWaveDirector.Step(controller.Combat, 17.5);
                controller.BeginEditorPreviewWave(3);
                Assert.AreEqual(3, controller.Combat.Game.CurrentWave);
                Assert.AreEqual(35, controller.VerifiedWaveDirector.RemainingSpawns);
                Assert.Throws<InvalidOperationException>(() =>
                    controller.BeginEditorPreviewWave(4),
                    "Editor preview must not fabricate a fourth round.");
                Assert.AreEqual(5L, controller.SharedEconomy.Wallet.Balance(
                    WispResourceIds.Random));
                Assert.AreEqual(60L, controller.SharedEconomy.Wallet.Balance("gold"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
#endif

        [Test]
        public void RejectedFinalStatSourceCannotSpendPinnedStartResources()
        {
            var root = new GameObject("ORDR rejected fixture first-round approval");
            try
            {
                var approval = new FixtureLiveApproval { Allow = false };
                var controller = CreateController(root, approval);
                controller.RegisterVerifiedLiveMonsterWavePlan(
                    MakeFixtureRoundOne(controller.OriginalMonsterSpawnIdentities));
                Assert.IsTrue(controller.CanBeginVerifiedFirstWave(out _),
                    "Preflight must not invoke potentially consuming approval.");

                Assert.Throws<InvalidOperationException>(() =>
                    controller.BeginVerifiedFirstWave());
                Assert.Greater(approval.Calls, 0);
                Assert.AreEqual(SessionPhase.NotStarted, controller.Combat.Game.Phase);
                Assert.AreEqual(0, controller.SharedEconomy.Units.Count);
                Assert.IsFalse(controller.SharedEconomy.HasStarted);
                Assert.AreEqual(0, controller.SharedEconomy.Wallet.Balance("gold"));
                Assert.AreEqual(0, controller.SharedEconomy.Wallet.Balance(
                    WispResourceIds.Random));
                Assert.IsNull(controller.VerifiedWaveDirector.CurrentProgress);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
