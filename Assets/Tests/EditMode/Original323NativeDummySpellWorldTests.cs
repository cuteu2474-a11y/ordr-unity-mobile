using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // Synthetic source witnesses only. The real Warcraft native hit/target
    // calculation remains an independent required production adapter.
    public sealed class Original323NativeDummySpellWorldTests
    {
        private sealed class ScriptDrawWitness : IOriginal323ScriptRandomDrawWitness
        {
            public int Calls;
            public bool TryConfirmOriginalDraw(long sequence,
                Original323ScriptRandomPurpose purpose, int oldValue,
                int newValue, int min, int max, int expected, out string proof)
            {
                Calls++;
                proof = "observed original attack script random event";
                return true;
            }
        }
        private sealed class Attacked : IOriginal323ScriptAttackProcWitness
        {
            public bool TryConfirmOriginalAttackEvent(string function,
                string attacker, string target, long index, out string proof)
            {
                proof = "source observed Warcraft attacked event";
                return index == 1 && target == "primary";
            }
        }
        private sealed class Hits : IOriginal323NativeDummySpellHitWitness
        {
            public bool Confirmed = true;
            public bool ProofApproved = true;
            public int Calls;
            public Original323NativeDummyAbilityFacts LastFacts;
            public readonly List<Original323NativeDummySpellHit> List =
                new List<Original323NativeDummySpellHit>();
            public bool TryResolveObservedNativeSpell(
                Original323AttackDummySpellCommand order,
                Original323NativeDummyAbilityFacts facts,
                CharacterCombatRuntime world, string actor,
                string primary,
                out IReadOnlyList<Original323NativeDummySpellHit> confirmed,
                out string proof)
            {
                Calls++; LastFacts = facts;
                confirmed = Confirmed ? List.AsReadOnly() : null;
                proof = ProofApproved ?
                    "original Warcraft completed dummy spell and verified final HP" :
                    "MODEL_ONLY simulated native spell";
                return Confirmed;
            }
        }
        private sealed class Formula : IOriginalDamageResolver
        {
            public long ResolveBasicDamage(BasicDamageRequest request,
                CombatEnemy target)
            {
                return 1; // Synthetic basic attack fixture, not original HP.
            }
        }

        private static CharacterCombatRuntime World(string actorCatalogId,
            string originalRawcode, Hits hits, out long actor)
        {
            var units = CharacterUnitCatalog.FromJson(File.ReadAllText(
                Path.Combine(Application.dataPath, "..", "data", "units.json")));
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var combat = new CharacterCombatRuntime(units, skills,
                new Formula(), new RejectUnverifiedDamageFormula(),
                new SeededNonOriginalProcRandom(66));
            var sourceRng = new Original323SharedBfiRandom(3,
                "observed original native Warcraft seed", new ScriptDrawWitness());
            var scriptRolls = new Original323ScriptAttackProcRolls(
                sourceRng, new Attacked());
            combat.ConfigureOriginal323ScriptAttackEffectWorld(scriptRolls,
                new Original323ObservedAttackDummySpellWorld(hits));
            combat.Start();
            combat.BeginWave(1);
            actor = combat.SpawnCharacter(actorCatalogId, 0, 0);
            combat.BindVerifiedBasicAttack(actor,
                new BasicAttackSpec(1, 1, 10, DamageSchool.Physical,
                    DamageTraits.None), 0, originalRawcode);
            return combat;
        }

        private static void Enemy(CharacterCombatRuntime combat, string name,
            CharacterEnemyKind kind = CharacterEnemyKind.Line, long hp = 1000)
        {
            combat.SpawnEnemyWithDefenses(name, hp, kind, 1, 0, 0,
                new EnemyDefenseProfile(kind, 0, 0,
                    "normal", "synthetic test enemy stat fixture"));
        }

        [Test]
        public void PinnedOriginalNativeAbilityFieldsAreNotInterpretedAsFinalHp()
        {
            Original323AttackDummySpellCommand blue, fuk, enel;
            Original323NativeDummyAbilityFacts swarm, swarmFuk, fork;
            Assert.IsTrue(Original323ScriptAttackEffectCoordinator
                .TryCreateOriginalSpell("h00M", out blue));
            Assert.IsTrue(Original323ScriptAttackEffectCoordinator
                .TryCreateOriginalSpell("h00A", out fuk));
            Assert.IsTrue(Original323ScriptAttackEffectCoordinator
                .TryCreateOriginalSpell("h00Q", out enel));
            Assert.IsTrue(Original323NativeDummyAbilityCatalog
                .TryGet(blue, out swarm));
            Assert.IsTrue(Original323NativeDummyAbilityCatalog
                .TryGet(fuk, out swarmFuk));
            Assert.IsTrue(Original323NativeDummyAbilityCatalog
                .TryGet(enel, out fork));
            Assert.AreEqual("e0KD",swarm.DummyUnitRawcode);
            Assert.AreEqual("AUcs",swarm.ParentWarcraftAbility);
            Assert.AreEqual(200,swarm.Area1);
            Assert.AreEqual(1700,swarm.CastRange1);
            Assert.AreEqual(150,swarm.DataA1);
            Assert.AreEqual(70000000,swarm.DataB1);
            Assert.AreEqual(800,swarm.DataC1);
            Assert.AreEqual(250,swarm.DataD1);
            Assert.IsNull(swarm.MaxNativeTargets);
            Assert.AreEqual(swarm.AbilityRawcode,swarmFuk.AbilityRawcode);
            Assert.AreEqual("ANfl",fork.ParentWarcraftAbility);
            Assert.AreEqual("forkedlightning",fork.Order);
            Assert.AreEqual(125,fork.Area1);
            Assert.AreEqual(1750,fork.CastRange1);
            Assert.AreEqual(1500,fork.DataA1);
            Assert.AreEqual(4,fork.DataB1);
            Assert.AreEqual(1700,fork.DataC1);
            Assert.AreEqual(500,fork.DataD1);
            Assert.AreEqual(4,fork.MaxNativeTargets.Value);
        }

        [Test]
        public void VerifiedBluenoNativeLineHitsEnterCanonicalDamageLedger()
        {
            var hits = new Hits();
            hits.List.Add(new Original323NativeDummySpellHit(
                "primary", 12, "verified source primary final HP"));
            hits.List.Add(new Original323NativeDummySpellHit(
                "second", 17, "verified source secondary final HP"));
            long actor;
            var world = World("U-04","h00M",hits,out actor);
            Enemy(world,"primary"); Enemy(world,"second");
            var events = world.Step(0);
            Assert.AreEqual(987,world.EnemyOf("primary").Health);
            Assert.AreEqual(983,world.EnemyOf("second").Health);
            Assert.AreEqual(1,hits.Calls);
            Assert.AreEqual("A0VS",hits.LastFacts.AbilityRawcode);
            bool skillHit = false;
            foreach (var e in events)
                if (e.Type == CharacterCombatEventType.SkillDamage &&
                    e.EnemyId == "second" && e.SkillName == "람각" &&
                    e.Amount == 17) skillHit = true;
            Assert.IsTrue(skillHit);
        }

        [Test]
        public void VerifiedEnelForkedLightningCanAffectAtMostFourDistinctTargets()
        {
            var hits = new Hits();
            foreach (var id in new[]{"primary","a","b","c"})
                hits.List.Add(new Original323NativeDummySpellHit(
                    id, 10, "native forked lightning final HP for " + id));
            long actor;
            var world = World("S-18","h00Q",hits,out actor);
            foreach (var id in new[]{"primary","a","b","c"}) Enemy(world,id);
            world.Step(0);
            Assert.AreEqual(989,world.EnemyOf("primary").Health);
            foreach (var id in new[]{"a","b","c"})
                Assert.AreEqual(990,world.EnemyOf(id).Health);
            Assert.AreEqual(4,hits.LastFacts.MaxNativeTargets.Value);
        }

        [Test]
        public void UnverifiedFifthForkTargetBlocksEntireNativeBatch()
        {
            var hits = new Hits();
            foreach (var id in new[]{"primary","a","b","c","d"})
                hits.List.Add(new Original323NativeDummySpellHit(
                    id, 50, "native target final HP proof"));
            long actor;
            var world = World("S-18","h00Q",hits,out actor);
            foreach (var id in new[]{"primary","a","b","c","d"}) Enemy(world,id);
            var events = world.Step(0);
            Assert.AreEqual(999,world.EnemyOf("primary").Health);
            foreach (var id in new[]{"a","b","c","d"})
                Assert.AreEqual(1000,world.EnemyOf(id).Health);
            Assert.IsTrue(ContainsDeferred(events, "갈래 번개"));
        }

        [Test]
        public void OutOfLaneAndDuplicateTargetsNeverCausePartialAreaDamage()
        {
            foreach (bool duplicate in new[]{false,true})
            {
                var hits = new Hits();
                hits.List.Add(new Original323NativeDummySpellHit(
                    "primary", 20, "verified native target result"));
                hits.List.Add(new Original323NativeDummySpellHit(
                    duplicate ? "primary" : "story", 20,
                    "verified native target result"));
                long actor;
                var world = World("U-04","h00M",hits,out actor);
                Enemy(world,"primary");
                if (!duplicate) Enemy(world,"story",CharacterEnemyKind.Story);
                var events = world.Step(0);
                Assert.AreEqual(999,world.EnemyOf("primary").Health);
                if (!duplicate)
                    Assert.AreEqual(1000,world.EnemyOf("story").Health);
                Assert.IsTrue(ContainsDeferred(events, "람각"));
            }
        }

        [Test]
        public void NativeMagicImmunityCanYieldApprovedCastWithNoHpDamage()
        {
            var hits = new Hits(); // Source has observed zero valid HP hits.
            long actor;
            var world = World("S-18","h00Q",hits,out actor);
            Enemy(world,"primary");
            var events = world.Step(0);
            Assert.AreEqual(999,world.EnemyOf("primary").Health);
            Assert.IsTrue(ContainsActivated(events,"갈래 번개"));
            Assert.AreEqual(1,hits.Calls);
        }

        [Test]
        public void MissingOrSyntheticNativeImpactSourceLeavesHpUnchanged()
        {
            foreach (bool noConfirmation in new[]{false,true})
            {
                var hits = new Hits {
                    Confirmed = !noConfirmation,
                    ProofApproved = noConfirmation
                };
                hits.List.Add(new Original323NativeDummySpellHit(
                    "primary", 100, "real verified native attack damage"));
                long actor;
                var world = World("U-04","h00M",hits,out actor);
                Enemy(world,"primary");
                var events = world.Step(0);
                Assert.AreEqual(999,world.EnemyOf("primary").Health);
                Assert.IsTrue(ContainsDeferred(events, "람각"));
            }
        }

        [Test]
        public void SyntheticHitEvidenceRejectedBeforeAnyTargetIsDamaged()
        {
            var hits = new Hits();
            hits.List.Add(new Original323NativeDummySpellHit(
                "primary", 100, "TEST_ONLY guessed damage"));
            long actor;
            var world = World("U-13","h00A",hits,out actor);
            Enemy(world,"primary");
            var events = world.Step(0);
            Assert.AreEqual(999,world.EnemyOf("primary").Health);
            Assert.IsTrue(ContainsDeferred(events,"람각"));
        }

        private static bool ContainsDeferred(
            IReadOnlyList<CharacterCombatEvent> events, string skill)
        {
            foreach(var e in events)
                if(e.Type==CharacterCombatEventType.EffectDeferred &&
                   e.SkillName==skill) return true;
            return false;
        }

        private static bool ContainsActivated(
            IReadOnlyList<CharacterCombatEvent> events, string skill)
        {
            foreach(var e in events)
                if(e.Type==CharacterCombatEventType.SkillActivated &&
                   e.SkillName==skill) return true;
            return false;
        }
    }
}
