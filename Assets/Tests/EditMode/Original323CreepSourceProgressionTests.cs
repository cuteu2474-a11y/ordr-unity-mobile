using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // This is an injected TEST-only native source port. Its values never
    // reach the standalone game's production configuration by default.
    public sealed class Original323CreepSourceProgressionTests
    {
        private sealed class MinimumDice : ICombatProcRandom
        { public int Next(int exclusiveUpperBound) => 0; }

        private sealed class Witness : IOriginal323CreepSourceSpawnPort
        {
            public bool Allow = true;
            public int Calls;
            public int InvalidStage;
            public bool InvalidPosition;
            public bool InvalidEvidence;
            public bool TryResolveOriginalSpawn(int stage, string previousId,
                out Original323CreepSourceSpawn spawn)
            {
                Calls++;
                spawn = null;
                if (!Allow || (stage > 1 && string.IsNullOrWhiteSpace(previousId)))
                    return false;
                string raw = stage == 1 ? "n00E" : stage == 2 ? "n00F" : "n00G";
                spawn = new Original323CreepSourceSpawn(
                    stage == InvalidStage ? 99 : stage, raw,
                    InvalidEvidence ? "TEST_ONLY" : "source-w3x:JASS CTs/CTp/CTq observed",
                    100 + stage, new EnemyDefenseProfile(
                        CharacterEnemyKind.Creep, 10, null, "normal",
                        "original observed creep source final stats"),
                    Original323CreepSourceProgression.SinglePlayerCreepX +
                        (InvalidPosition ? 1 : 0),
                    Original323CreepSourceProgression.SinglePlayerCreepY);
                return true;
            }
        }

        private sealed class Reward : IOriginal323CreepSourceRewardPort
        {
            public bool Allow = true;
            public readonly List<Original323CreepSourceRewardClaim> Applied =
                new List<Original323CreepSourceRewardClaim>();
            public bool TryCommitOriginalDeathReward(
                Original323CreepSourceRewardClaim claim)
            {
                if (!Allow) return false;
                if (!Applied.Any(x => x.ReceiptId == claim.ReceiptId))
                    Applied.Add(claim);
                return true;
            }
        }

        private sealed class Reach : IOriginal323CreepDispatchAuthority
        {
            public bool TryResolveAttackPlacement(long actor,
                Original323CreepEncounter encounter, out double x, out double y)
            { x = encounter.X + 1; y = encounter.Y; return true; }
        }

        private static string Data(string file) =>
            File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", file));

        private static CharacterCombatRuntime EmptyCombat()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(
                Data("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var random = new MinimumDice();
            var rules = new SourceModelBasicCombatRules(map, random);
            var combat = new CharacterCombatRuntime(
                units, skills, rules, rules, random);
            combat.Start();
            combat.BeginWave(1);
            return combat;
        }

        private static void Defeat(CharacterCombatRuntime combat, long actor,
            Original323CreepEncounter encounter)
        {
            var resolver = (CombatResolution)typeof(CharacterCombatRuntime)
                .GetField("resolution", BindingFlags.NonPublic |
                    BindingFlags.Instance).GetValue(combat);
            long damage;
            Assert.IsTrue(resolver.TryApplySkillDamage(actor.ToString(),
                encounter.EnemyId, 999999, out damage));
            Assert.AreEqual(Original323CreepEncounterState.Defeated,
                encounter.State);
        }

        [Test]
        public void SpawnRequiresActualCTsEventAndFinalSourceEvidence()
        {
            var combat = EmptyCombat();
            using (var creeps = new Original323CreepPracticeWorld(
                combat, Original323CreepObjectCatalog.FromJson(
                    Data("original-2323C-monster-object-stats.json"))))
            {
                var witness = new Witness { Allow = false };
                using (var progress = new Original323CreepSourceProgression(
                    combat, creeps, witness, new Reward()))
                {
                    Assert.IsFalse(progress.ObserveOriginalFirstSpawn("source:LB:observed"));
                    Assert.AreEqual(0, creeps.Encounters.Count);
                    witness.Allow = true;
                    witness.InvalidEvidence = true;
                    Assert.IsFalse(progress.ObserveOriginalFirstSpawn("source:LB:observed"));
                    witness.InvalidEvidence = false;
                    witness.InvalidPosition = true;
                    Assert.IsFalse(progress.ObserveOriginalFirstSpawn("source:LB:observed"));
                    witness.InvalidPosition = false;
                    Assert.IsTrue(progress.ObserveOriginalFirstSpawn("source:LB:observed"));
                    Assert.IsFalse(progress.ObserveOriginalFirstSpawn("source:LB:observed"));
                    Assert.AreEqual(1, creeps.Encounters.Count);
                    Assert.IsTrue(creeps.Encounters[0].SourceConfirmed);
                    Assert.AreEqual(101, combat.EnemyOf(
                        creeps.Encounters[0].EnemyId).MaxHealth);
                    Assert.Throws<NotSupportedException>(() =>
                        creeps.CaptureState(),
                        "v1 checkpoints cannot silently reload native creeps as base practice.");
                }
            }
        }

        [Test]
        public void DeathStagesRequireSettledClaimThenAutoRetargetAndManualRecall()
        {
            var combat = EmptyCombat();
            long actor = combat.SpawnCharacter("C-01", 4, 8);
            using (var creeps = new Original323CreepPracticeWorld(
                combat, Original323CreepObjectCatalog.FromJson(
                    Data("original-2323C-monster-object-stats.json"))))
            {
                var rewards = new Reward { Allow = false };
                using (var progress = new Original323CreepSourceProgression(
                    combat, creeps, new Witness(), rewards))
                {
                    combat.ConfigureVerifiedCreepDispatch(creeps, new Reach());
                    Assert.IsTrue(progress.ObserveOriginalFirstSpawn("source:LB:observed"));
                    var first = creeps.Encounters.Single();
                    Assert.IsTrue(combat.TryDispatchToCreep(actor, 1));
                    Defeat(combat, actor, first);
                    Assert.AreEqual(1, progress.PendingRewardCount);
                    progress.AdvancePending();
                    Assert.AreEqual(1, creeps.Encounters.Count,
                        "No next stage until original reward is committed.");
                    Assert.IsTrue(combat.IsCreepDeployed(actor));
                    rewards.Allow = true;
                    progress.AdvancePending();
                    Assert.AreEqual(2, creeps.Encounters.Count);
                    Assert.AreEqual(1, progress.SettledRewardCount);
                    Assert.IsTrue(first.RewardSettled);
                    var second = creeps.Encounters.Last();
                    Assert.AreEqual(2, second.OriginalObject.Stage);
                    Assert.AreEqual(second.EnemyId, combat.CreepTargetOf(actor));
                    Assert.AreEqual(0, combat.Game.DefeatedEnemies);
                    Defeat(combat, actor, second);
                    progress.AdvancePending();
                    var third = creeps.Encounters.Last();
                    Assert.AreEqual(3, third.OriginalObject.Stage);
                    Assert.AreEqual(third.EnemyId, combat.CreepTargetOf(actor));
                    Defeat(combat, actor, third);
                    progress.AdvancePending();
                    progress.AdvancePending();
                    Assert.AreEqual(3, rewards.Applied.Count);
                    Assert.AreEqual(3, progress.SettledRewardCount);
                    Assert.AreEqual(3, creeps.Encounters.Count);
                    Assert.IsNull(combat.CreepTargetOf(actor));
                    Assert.IsTrue(combat.IsCreepDeployed(actor),
                        "The final kill never auto-recalls a real actor.");
                    Assert.IsTrue(combat.TryRecallFromCreep(actor));
                    Assert.AreEqual(4, combat.Roster.Get(actor).X);
                    Assert.AreEqual(8, combat.Roster.Get(actor).Y);
                    Assert.AreEqual(0, combat.Game.DefeatedEnemies);
                    Assert.AreEqual(actor.ToString(),
                        rewards.Applied[1].LastHitActorId);
                }
            }
        }

        [Test]
        public void ObjectBasePracticeDeathNeverTriggersSourceSettlementOrChain()
        {
            var combat = EmptyCombat();
            using (var creeps = new Original323CreepPracticeWorld(
                combat, Original323CreepObjectCatalog.FromJson(
                    Data("original-2323C-monster-object-stats.json"))))
            {
                var witness = new Witness();
                var rewards = new Reward();
                using (var source = new Original323CreepSourceProgression(
                    combat, creeps, witness, rewards))
                {
                    var practice = creeps.SpawnPracticeStage(1, 3, 4);
                    Defeat(combat, 88, practice);
                    source.AdvancePending();
                    Assert.AreEqual(0, rewards.Applied.Count);
                    Assert.AreEqual(0, witness.Calls);
                    Assert.AreEqual(1, creeps.Encounters.Count);
                }
            }
        }
    }
}
