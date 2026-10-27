using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
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

        private static OrdrCharacterCombatController CreateController(GameObject root,
            FixtureLiveApproval approval)
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
            controller.ConfigureOriginalCommonNativeDamageWhenVerified(
                new FixtureNativePath(), null, new FixtureQuantizer(),
                new FixtureDice());
            Assert.IsNotNull(controller.Combat);
            return controller;
        }

        private static VerifiedWavePlan MakeFixtureRoundOne(
            Original323MonsterWaveIdentityCatalog original)
        {
            var originalTimers = original.BuildSinglePlayerSpawnTemplates(1);
            Assert.AreEqual(35, originalTimers.Count);
            var spawns = new List<VerifiedWaveEnemy>();
            foreach (var timer in originalTimers)
            {
                // Fixture 1,000 HP/10 armor/coordinates do NOT represent
                // actual Nightmare + Otherworld final live stats.
                spawns.Add(new VerifiedWaveEnemy(
                    timer.AtSeconds, timer.InstanceId, 1000L, timer.Kind,
                    20, 0, 0, new EnemyDefenseProfile(
                        timer.Kind, 10, null, "normal",
                        "INTEGRATION_FIXTURE: not verified monster defense"),
                    timer.Rawcode));
            }
            return new VerifiedWavePlan(1, spawns,
                "INTEGRATION_FIXTURE: first-wave schedule verification only");
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
