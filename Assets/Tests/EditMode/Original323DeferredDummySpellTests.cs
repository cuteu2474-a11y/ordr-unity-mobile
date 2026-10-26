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
    // Synthetic source observations only: no claim that Warcraft's native
    // missile physics / target filter has been fully recreated in Unity.
    public sealed class Original323DeferredDummySpellTests
    {
        private sealed class Dice : IOriginal323ScriptRandomDrawWitness
        {
            public bool TryConfirmOriginalDraw(long ordinal,
                Original323ScriptRandomPurpose purpose, int previous,
                int next, int min, int max, int result, out string proof)
            {
                proof = "source observed original shared dice stream";
                return true;
            }
        }

        private sealed class Attacked : IOriginal323ScriptAttackProcWitness
        {
            public bool TryConfirmOriginalAttackEvent(string function,
                string actor, string target, long ordinal, out string proof)
            {
                proof = "source observed original Warcraft attacked callback";
                return target == "primary" && ordinal == 1;
            }
        }

        private sealed class CastWitness : IOriginal323DummySpellCastWitness
        {
            public string CastId = "original-native-cast-1";
            public string Proof = "source observed native dummy IssuePointOrder";
            public int Calls;
            public bool Approved = true;
            public bool TryConfirmOriginalDummyCast(
                Original323AttackDummySpellCommand order,
                Original323NativeDummyAbilityFacts facts,
                CharacterCombatRuntime combat, string actor, string primary,
                double observedTime, out string castId, out string proof)
            {
                Calls++;
                castId = CastId; proof = Proof;
                return Approved && primary == "primary" &&
                    observedTime == combat.Time &&
                    order.OriginalAbilityRawcode == facts.AbilityRawcode;
            }
        }

        private sealed class ImpactWitness : IOriginal323DummySpellImpactWitness
        {
            public Original323NativeDummyImpact Current;
            public int Calls;
            public bool Approved = true;
            public bool TryConfirmOriginalDummyImpact(
                Original323ObservedDummyCast original,
                CharacterCombatRuntime combat, out Original323NativeDummyImpact impact)
            {
                Calls++;
                impact = Current;
                return Approved && original.CastId == "original-native-cast-1";
            }
        }

        private sealed class BasicDamage : IOriginalDamageResolver
        {
            public long ResolveBasicDamage(BasicDamageRequest request,
                CombatEnemy target)
            {
                return 1; // Fixture only; never original-world attack power.
            }
        }

        private sealed class Fixture
        {
            public CharacterCombatRuntime Combat;
            public CastWitness Cast;
            public ImpactWitness Impact;
            public Original323DeferredNativeDummySpellWorld Bridge;
            public long Actor;

            public void Enemy(string id, long hp = 1000,
                CharacterEnemyKind kind = CharacterEnemyKind.Line)
            {
                Combat.SpawnEnemyWithDefenses(id, hp, kind,
                    id == "primary" ? 1 : 50, 0, 0,
                    new EnemyDefenseProfile(kind, 0, 0, "normal",
                        "TEST_ONLY synthetic enemy defenses"));
            }

            public bool Deliver(string impactId, double time, bool finish,
                out long amount, params Original323NativeDummySpellHit[] hits)
            {
                Impact.Current = new Original323NativeDummyImpact(
                    impactId, time, finish, hits, "observed Warcraft native HP impact");
                return Bridge.TryApplyObservedImpact(Combat,
                    Cast.CastId, out amount);
            }
        }

        private static Fixture Create(string catalogId = "U-04",
            string rawcode = "h00M")
        {
            var unitJson = File.ReadAllText(Path.Combine(
                Application.dataPath, "..", "data", "units.json"));
            var units = CharacterUnitCatalog.FromJson(unitJson);
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var game = new CharacterCombatRuntime(units, skills,
                new BasicDamage(), new RejectUnverifiedDamageFormula(),
                new SeededNonOriginalProcRandom(42));
            var fixture = new Fixture {
                Combat = game,
                Cast = new CastWitness(),
                Impact = new ImpactWitness()
            };
            var originalRng = new Original323SharedBfiRandom(3,
                "source observed Warcraft native seed", new Dice());
            fixture.Bridge = new Original323DeferredNativeDummySpellWorld(
                fixture.Cast, fixture.Impact);
            game.ConfigureOriginal323ScriptAttackEffectWorld(
                new Original323ScriptAttackProcRolls(
                    originalRng, new Attacked()), fixture.Bridge);
            game.Start();
            game.BeginWave(1);
            fixture.Actor = game.SpawnCharacter(catalogId, 0, 0);
            game.BindVerifiedBasicAttack(fixture.Actor,
                new BasicAttackSpec(1, 1, 10, DamageSchool.Physical,
                    DamageTraits.None), 0, rawcode);
            return fixture;
        }

        private static Original323NativeDummySpellHit Hit(string id, long damage)
        {
            return new Original323NativeDummySpellHit(id, damage,
                "actual original Warcraft final HP loss on " + id);
        }

        [Test]
        public void DeclaringSwarmCastDoesNotPrematurelyApplyNativeDamage()
        {
            var game = Create();
            game.Enemy("primary"); game.Enemy("second");
            var events = game.Combat.Step(0);
            Assert.AreEqual(999, game.Combat.EnemyOf("primary").Health);
            Assert.AreEqual(1000, game.Combat.EnemyOf("second").Health);
            Assert.AreEqual(1, game.Combat.PendingOriginalDummySpellCount);
            Assert.AreEqual(1, game.Cast.Calls);
            Assert.AreEqual(0, game.Impact.Calls);
            Assert.IsTrue(ContainsEvent(events,
                CharacterCombatEventType.SkillActivated, "람각"));
        }

        [Test]
        public void DelayedSwarmContactsLandAtTwoSeparatelyObservedTimes()
        {
            var game = Create();
            game.Enemy("primary"); game.Enemy("second");
            game.Combat.Step(0);
            game.Combat.Step(0.25);
            long dealt;
            Assert.IsTrue(game.Deliver("native-impact-1", 0.25, false,
                out dealt, Hit("second", 30)));
            Assert.AreEqual(30, dealt);
            Assert.AreEqual(970, game.Combat.EnemyOf("second").Health);
            Assert.AreEqual(1, game.Combat.PendingOriginalDummySpellCount);

            game.Combat.Step(0.25);
            Assert.IsTrue(game.Deliver("native-impact-2", 0.50, true,
                out dealt, Hit("primary", 7)));
            Assert.AreEqual(7, dealt);
            Assert.AreEqual(992, game.Combat.EnemyOf("primary").Health);
            Assert.AreEqual(0, game.Combat.PendingOriginalDummySpellCount);
            Assert.IsFalse(game.Deliver("native-impact-replay", 0.5, true,
                out dealt, Hit("second", 1)));
            Assert.AreEqual(970, game.Combat.EnemyOf("second").Health);
            var pendingEvents = game.Combat.Step(0);
            Assert.IsTrue(ContainsEvent(pendingEvents,
                CharacterCombatEventType.SkillDamage, "람각"));
        }

        [Test]
        public void ProjectileMayHitAnotherEnemyAfterOriginalTargetDiedToBasic()
        {
            var game = Create();
            game.Enemy("primary", 1); game.Enemy("second");
            game.Combat.Step(0);
            Assert.IsNull(game.Combat.EnemyOf("primary"),
                "처치된 원본 기본공격 대상은 전투 원장에서 제거됩니다.");
            game.Combat.Step(0.25);
            long dealt;
            Assert.IsTrue(game.Deliver("native-impact-after-death", 0.25, true,
                out dealt, Hit("second", 49)));
            Assert.AreEqual(49, dealt);
            Assert.AreEqual(951, game.Combat.EnemyOf("second").Health);
        }

        [Test]
        public void FutureOrOldTimestampCannotApplyOrConsumeRealImpact()
        {
            var game = Create();
            game.Enemy("primary");
            game.Combat.Step(0);
            game.Combat.Step(0.5);
            long dealt;
            Assert.IsFalse(game.Deliver("native-impact-1", 0.25, true,
                out dealt, Hit("primary", 22)));
            Assert.IsFalse(game.Deliver("native-impact-1", 0.75, true,
                out dealt, Hit("primary", 22)));
            Assert.AreEqual(999, game.Combat.EnemyOf("primary").Health);
            Assert.AreEqual(1, game.Combat.PendingOriginalDummySpellCount);
            Assert.IsTrue(game.Deliver("native-impact-1", 0.5, true,
                out dealt, Hit("primary", 22)));
            Assert.AreEqual(977, game.Combat.EnemyOf("primary").Health);
        }

        [Test]
        public void StoryLaneOrDuplicateTargetsRejectWholeProjectileBatch()
        {
            foreach (bool duplicate in new[]{false,true})
            {
                var game = Create();
                game.Enemy("primary"); game.Enemy("second");
                if (!duplicate)
                    game.Enemy("story", 1000, CharacterEnemyKind.Story);
                game.Combat.Step(0);
                game.Combat.Step(0.25);
                long dealt;
                Assert.IsFalse(game.Deliver("impact-invalid", 0.25, true,
                    out dealt, Hit("second", 40),
                    Hit(duplicate ? "second" : "story", 30)));
                Assert.AreEqual(1000, game.Combat.EnemyOf("second").Health);
                Assert.AreEqual(1, game.Combat.PendingOriginalDummySpellCount);
                Assert.IsTrue(game.Deliver("impact-corrected", 0.25, true,
                    out dealt, Hit("second", 40)));
                Assert.AreEqual(960, game.Combat.EnemyOf("second").Health);
            }
        }

        [Test]
        public void SameTargetCannotReceiveTwoSwarmImpactReceipts()
        {
            var game = Create();
            game.Enemy("primary"); game.Enemy("second");
            game.Combat.Step(0);
            game.Combat.Step(0.25);
            long dealt;
            Assert.IsTrue(game.Deliver("impact-one",0.25,false,
                out dealt, Hit("second",10)));
            Assert.IsFalse(game.Deliver("impact-one",0.25,false,
                out dealt, Hit("primary",50)));
            Assert.IsFalse(game.Deliver("impact-two",0.25,false,
                out dealt, Hit("second",50)));
            Assert.AreEqual(990, game.Combat.EnemyOf("second").Health);
            Assert.AreEqual(999, game.Combat.EnemyOf("primary").Health);
            Assert.IsTrue(game.Deliver("impact-three",0.25,true,
                out dealt, Hit("primary",8)));
            Assert.AreEqual(991, game.Combat.EnemyOf("primary").Health);
        }

        [Test]
        public void ForkedLightningHasFourTargetLimitAcrossAllImpactReceipts()
        {
            var game = Create("S-18","h00Q");
            foreach (var id in new[]{"primary","a","b","c","d"})
                game.Enemy(id);
            game.Combat.Step(0);
            game.Combat.Step(0.25);
            long dealt;
            Assert.IsTrue(game.Deliver("fork-one",0.25,false,
                out dealt,Hit("primary",10),Hit("a",10)));
            game.Combat.Step(0.25);
            Assert.IsTrue(game.Deliver("fork-two",0.5,false,
                out dealt,Hit("b",10),Hit("c",10)));
            game.Combat.Step(0.25);
            Assert.IsFalse(game.Deliver("fork-fifth",0.75,true,
                out dealt,Hit("d",10)));
            Assert.AreEqual(1000,game.Combat.EnemyOf("d").Health);
            Assert.AreEqual(1,game.Combat.PendingOriginalDummySpellCount);
            Assert.IsTrue(game.Deliver("fork-end",0.75,true,out dealt));
            Assert.AreEqual(0,dealt);
            Assert.AreEqual(0,game.Combat.PendingOriginalDummySpellCount);
            Assert.AreEqual(990,game.Combat.EnemyOf("a").Health);
            Assert.AreEqual(989,game.Combat.EnemyOf("primary").Health);
        }

        [Test]
        public void UnobservedImpactAndInvalidProofLeaveCastPendingAndHpIntact()
        {
            var game = Create();
            game.Enemy("primary");
            game.Combat.Step(0);
            game.Combat.Step(0.25);
            game.Impact.Approved = false;
            long dealt;
            Assert.IsFalse(game.Deliver("impact-one",0.25,true,
                out dealt,Hit("primary",50)));
            game.Impact.Approved = true;
            game.Impact.Current = new Original323NativeDummyImpact(
                "impact-one",0.25,true,
                new[]{Hit("primary",50)},"MODEL_ONLY bogus hit proof");
            Assert.IsFalse(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out dealt));
            game.Impact.Current = new Original323NativeDummyImpact(
                "impact-one",0.25,true,
                new[]{new Original323NativeDummySpellHit("primary",50,
                    "TEST_FIXTURE fabricated HP")},"observed native projectile");
            Assert.IsFalse(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out dealt));
            Assert.AreEqual(999,game.Combat.EnemyOf("primary").Health);
            Assert.AreEqual(1,game.Combat.PendingOriginalDummySpellCount);
        }

        [Test]
        public void ObservedImmuneSpellCanEndWithoutAnyHpDamage()
        {
            var game = Create("S-18","h00Q");
            game.Enemy("primary");
            game.Combat.Step(0);
            game.Combat.Step(0.25);
            long dealt;
            Assert.IsTrue(game.Deliver("native-immune-end",0.25,true,out dealt));
            Assert.AreEqual(0,dealt);
            Assert.AreEqual(999,game.Combat.EnemyOf("primary").Health);
            Assert.AreEqual(0,game.Combat.PendingOriginalDummySpellCount);
        }

        [Test]
        public void UnverifiedCastSourceCannotCreatePendingImpact()
        {
            var game = Create();
            game.Cast.Proof="SYNTHETIC: guessed original order";
            game.Enemy("primary");
            var events=game.Combat.Step(0);
            Assert.AreEqual(0,game.Combat.PendingOriginalDummySpellCount);
            Assert.AreEqual(999,game.Combat.EnemyOf("primary").Health);
            Assert.IsTrue(ContainsEvent(events,
                CharacterCombatEventType.EffectDeferred,"람각"));
            long dealt;
            Assert.IsFalse(game.Bridge.TryApplyObservedImpact(
                game.Combat,"original-native-cast-1",out dealt));
            Assert.AreEqual(0,game.Impact.Calls);
        }

        [Test]
        public void NativeImpactConstructorValidatesTimestampAndHitSnapshot()
        {
            Assert.Throws<ArgumentException>(()=>
                new Original323NativeDummyImpact("impact",-1,false,
                    new Original323NativeDummySpellHit[0],"verified"));
            Assert.Throws<ArgumentException>(()=>
                new Original323NativeDummyImpact("impact",double.NaN,false,
                    new Original323NativeDummySpellHit[0],"verified"));
            Assert.Throws<ArgumentException>(()=>
                new Original323NativeDummyImpact("impact",0,false,
                    null,"verified"));
            var mutable = new List<Original323NativeDummySpellHit> {
                Hit("primary",4)
            };
            var snapshot=new Original323NativeDummyImpact(
                "impact",0,true,mutable,"verified observed");
            mutable.Clear();
            Assert.AreEqual(1,snapshot.Hits.Count);
        }

        private static bool ContainsEvent(
            IReadOnlyList<CharacterCombatEvent> events,
            CharacterCombatEventType type, string skill)
        {
            foreach (var e in events)
                if (e.Type==type && e.SkillName==skill) return true;
            return false;
        }
    }
}
