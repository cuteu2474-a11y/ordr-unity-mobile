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

        private sealed class NativeMagic : IOriginal323NativeMagicImpactWitness
        {
            public Original323ObservedNativeMagicImpact Next;
            public int Calls;
            public bool Approved = true;

            public bool TryConfirmOriginalNativeMagicImpact(
                Original323ObservedDummyCast cast,
                CharacterCombatRuntime combat,
                out Original323ObservedNativeMagicImpact observed)
            {
                Calls++;
                observed = Next;
                return Approved;
            }
        }

        // Test-only rounding. Production must supply native-engine-backed
        // integer HP conversion (NOT assume nearest whole number).
        private sealed class TestHpConversion : IOriginal323NativeMagicHpConversion
        {
            public bool Approved = true;
            public int Calls;
            public bool TryConvertNativeSpellDamage(float postDefend,
                string originalAbility, out long amount)
            {
                Calls++;
                amount = (long)Math.Round(postDefend,
                    MidpointRounding.AwayFromZero);
                return Approved;
            }
        }

        private sealed class LiveMagicLevel : IOriginal323LiveMonsterA04QWitness
        {
            public string VerifiedEnemyId = "primary";
            public int Calls;
            public bool Approved = true;
            public bool TryConfirm(string id, int previousLevel, int newLevel,
                out string evidence)
            {
                Calls++;
                evidence = "source observed original per-monster Bd5 update";
                return Approved && id == VerifiedEnemyId &&
                    previousLevel == 16 && newLevel == 1;
            }
        }

        private sealed class CurrentNativeModifiers :
            IOriginal323NativeMagicModifierWitness
        {
            public int Calls;
            public bool Approved = true;
            public string Evidence = "observed complete native buff pipeline at hit time";
            public int ExpectedCount = 2;
            public int ExpectedMagicReductions = 1;
            public int ExpectedMagicAmplifiers = 1;
            public string ObservedTargetId = "primary";

            public bool TryConfirmNativeModifiersAtImpact(
                CombatEnemy target, string spell,
                Original323ObservedNativeMagicHit hit,
                IReadOnlyList<CombatEffect> effects, out string source)
            {
                Calls++;
                source = Evidence;
                if (target.Id != ObservedTargetId ||
                    hit.TargetId != target.Id ||
                    effects.Count != ExpectedCount ||
                    (spell != "A086" && spell != "A0VS"))
                    return false;
                int reductions = 0, amplifiers = 0;
                foreach (var effect in effects)
                {
                    if (effect.Type == CombatEffectType.MagicResistanceReduction)
                    {
                        if (effect.SourceId != "source-resist-down" ||
                            effect.Amount != 5) return false;
                        reductions++;
                    }
                    else if (effect.Type == CombatEffectType.MagicDamageAmplification)
                    {
                        if (effect.SourceId != "source-magic-amp" ||
                            effect.Amount != 0.2) return false;
                        amplifiers++;
                    }
                    else return false;
                }
                return Approved && reductions == ExpectedMagicReductions &&
                    amplifiers == ExpectedMagicAmplifiers;
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
                CharacterEnemyKind kind = CharacterEnemyKind.Line,
                int? verifiedMagicDefenseLevel = null)
            {
                var defense = verifiedMagicDefenseLevel.HasValue ?
                    Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                        kind, 0, "normal",
                        verifiedMagicDefenseLevel.Value,
                        "source-backed test monster profile") :
                    new EnemyDefenseProfile(kind, 0, 0, "normal",
                        "TEST_ONLY synthetic enemy defenses");
                Combat.SpawnEnemyWithDefenses(id, hp, kind,
                    id == "primary" ? 1 : 50, 0, 0, defense);
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
            string rawcode = "h00M",
            IOriginal323DummySpellImpactWitness observedImpactWorld = null)
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
                fixture.Cast, observedImpactWorld ?? fixture.Impact);
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
            game.Impact.Current=new Original323NativeDummyImpact(
                "fork-one",0.25,false,
                new[]{Hit("primary",10),Hit("a",10)},
                new[]{Selected("primary"),Selected("a")},
                "observed first native fork selection");
            Assert.IsTrue(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out dealt));
            game.Combat.Step(0.25);
            game.Impact.Current=new Original323NativeDummyImpact(
                "fork-two",0.5,false,
                new[]{Hit("b",10),Hit("c",10)},
                new[]{Selected("b"),Selected("c")},
                "observed second native fork selection");
            Assert.IsTrue(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out dealt));
            game.Combat.Step(0.25);
            game.Impact.Current=new Original323NativeDummyImpact(
                "fork-fifth",0.75,true,
                new[]{Hit("d",10)},new[]{Selected("d")},
                "observed fifth native fork target");
            Assert.IsFalse(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out dealt));
            Assert.AreEqual(1000,game.Combat.EnemyOf("d").Health);
            Assert.AreEqual(1,game.Combat.PendingOriginalDummySpellCount);
            game.Impact.Current=new Original323NativeDummyImpact(
                "fork-end",0.75,true,
                new Original323NativeDummySpellHit[0],
                new Original323NativeSelectedSpellTarget[0],
                "observed original native lightning completion");
            Assert.IsTrue(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out dealt));
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
            game.Impact.Current=new Original323NativeDummyImpact(
                "native-immune-end",0.25,true,
                new Original323NativeDummySpellHit[0],
                new[]{Selected("primary",true)},
                "observed original native magic immunity");
            Assert.IsTrue(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out dealt));
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

        private static Original323NativeSelectedSpellTarget Selected(
            string id, bool immune = false)
        {
            return new Original323NativeSelectedSpellTarget(id, immune,
                "source confirmed native selection and immunity state");
        }

        [Test]
        public void ForkSelectionCountsImmuneTargetsTowardFourTargetLimit()
        {
            var game = Create("S-18", "h00Q");
            foreach (var id in new[] { "primary", "a", "b", "c", "d" })
                game.Enemy(id);
            game.Combat.Step(0);
            game.Combat.Step(0.25);
            long actual;
            game.Impact.Current = new Original323NativeDummyImpact(
                "fork-choice-first",0.25,false,
                new[] { Hit("primary",20) },
                new[] { Selected("primary"),Selected("a",true),
                    Selected("b",true) },
                "observed native forked lightning target choice");
            Assert.IsTrue(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out actual));
            Assert.AreEqual(20, actual);
            int selectedCount, immuneCount;
            Assert.IsTrue(game.Combat.TryGetOriginalDummyTargetSelectionProgress(
                game.Cast.CastId,out selectedCount,out immuneCount));
            Assert.AreEqual(3,selectedCount);
            Assert.AreEqual(2,immuneCount);
            Assert.AreEqual(1000,game.Combat.EnemyOf("a").Health);
            Assert.AreEqual(1000,game.Combat.EnemyOf("b").Health);
            game.Combat.Step(0.25);
            game.Impact.Current = new Original323NativeDummyImpact(
                "fork-choice-overflow",0.5,true,
                new[] { Hit("c",10),Hit("d",10) },
                new[] { Selected("c"),Selected("d") },
                "observed original native multi-target overflow");
            Assert.IsFalse(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out actual));
            Assert.AreEqual(1000,game.Combat.EnemyOf("c").Health);
            Assert.AreEqual(1000,game.Combat.EnemyOf("d").Health);
            game.Impact.Current = new Original323NativeDummyImpact(
                "fork-choice-last",0.5,true,
                new[] { Hit("c",10) },new[] { Selected("c") },
                "observed original forked lightning final selection");
            Assert.IsTrue(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out actual));
            Assert.AreEqual(10,actual);
            Assert.AreEqual(0,game.Combat.PendingOriginalDummySpellCount);
        }

        [Test]
        public void ForkCannotAssumeHitListIsCompleteOriginalTargetSelection()
        {
            var game = Create("S-18","h00Q");
            game.Enemy("primary");
            game.Combat.Step(0);
            game.Combat.Step(0.25);
            long actual;
            Assert.IsFalse(game.Deliver("fork-legacy",0.25,true,
                out actual,Hit("primary",100)));
            Assert.AreEqual(999,game.Combat.EnemyOf("primary").Health);
            // No hits does NOT imply no selected targets: even that case
            // requires an explicit original native selection receipt.
            Assert.IsFalse(game.Deliver("fork-no-selection",0.25,true,
                out actual));
            game.Impact.Current = new Original323NativeDummyImpact(
                "fork-explicit-zero",0.25,true,
                new Original323NativeDummySpellHit[0],
                new[]{Selected("primary",true)},
                "original native immunity prevented final HP damage");
            Assert.IsTrue(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out actual));
            Assert.AreEqual(0,actual);
            Assert.AreEqual(999,game.Combat.EnemyOf("primary").Health);
        }

        [Test]
        public void UnselectedOrImmuneTargetCanNeverTakeConfirmedForkHpDamage()
        {
            foreach (bool immune in new[]{false,true})
            {
                var game=Create("S-18","h00Q");
                game.Enemy("primary");
                game.Enemy("a");
                game.Combat.Step(0);
                game.Combat.Step(0.25);
                long actual;
                game.Impact.Current=new Original323NativeDummyImpact(
                    "fork-invalid-selection",0.25,true,
                    new[]{Hit("primary",55)},
                    new[]{Selected(immune?"primary":"a",immune)},
                    "observed original native fork target discrepancy");
                Assert.IsFalse(game.Bridge.TryApplyObservedImpact(
                    game.Combat,game.Cast.CastId,out actual));
                Assert.AreEqual(999,game.Combat.EnemyOf("primary").Health);
                Assert.AreEqual(1,game.Combat.PendingOriginalDummySpellCount);
            }
        }

        [Test]
        public void RepeatedSelectionAndSyntheticEvidenceFailWithoutConsumption()
        {
            var game=Create("S-18","h00Q");
            game.Enemy("primary");
            game.Enemy("a");
            game.Combat.Step(0);
            game.Combat.Step(0.25);
            game.Impact.Current=new Original323NativeDummyImpact(
                "fork-1",0.25,false,
                new[]{Hit("primary",3)},
                new[]{Selected("primary")},
                "observed native fork target");
            long actual;
            Assert.IsTrue(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out actual));
            Assert.AreEqual(3,actual);
            game.Combat.Step(0.25);
            game.Impact.Current=new Original323NativeDummyImpact(
                "fork-2",0.5,true,
                new Original323NativeDummySpellHit[0],
                new[]{Selected("primary")},
                "observed native duplicate target");
            Assert.IsFalse(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out actual));
            game.Impact.Current=new Original323NativeDummyImpact(
                "fork-3",0.5,true,
                new Original323NativeDummySpellHit[0],
                new[]{new Original323NativeSelectedSpellTarget(
                    "a",true,"TEST_ONLY guessed immunity")},
                "observed native fake selection proof");
            Assert.IsFalse(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out actual));
            Assert.AreEqual(0,actual);
            Assert.AreEqual(1000,game.Combat.EnemyOf("a").Health);
            Assert.AreEqual(1,game.Combat.PendingOriginalDummySpellCount);
        }

        private static Original323ObservedNativeMagicHit MagicHit(
            string id, float nativeBeforeDefend, int level,
            long observedHp, bool applied = true,
            string evidence = "source observed native pre-Defend damage and HP")
        {
            return new Original323ObservedNativeMagicHit(
                id, nativeBeforeDefend, level, applied,
                observedHp, evidence);
        }

        private static Original323ObservedNativeMagicImpact MagicImpact(
            string castId, string impactId, double at, bool completed,
            IReadOnlyList<Original323NativeSelectedSpellTarget> selected,
            params Original323ObservedNativeMagicHit[] hit)
        {
            return new Original323ObservedNativeMagicImpact(
                castId, impactId, at, completed,
                selected, hit, "source observed Warcraft magic impact");
        }

        [Test]
        public void VerifiedDefendFactorIsTakenFromExactCurrentMonsterLevel()
        {
            var quantizer = new TestHpConversion();
            Original323NativeDummySpellHit converted;
            var nightmare = new CombatEnemy("target",2000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line,0,"normal",16,
                    "original 2.323 nightmare source level"));
            Assert.IsTrue(Original323NativeMagicDamageReconciler.TryReconcile(
                nightmare,"A086",MagicHit("target",1000f,16,900),
                quantizer,out converted));
            Assert.AreEqual(900,converted.FinalHpDamage);
            var stronger = new CombatEnemy("target",2000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line,0,"normal",1,
                    "original dynamically observed magic defense"));
            Assert.IsTrue(Original323NativeMagicDamageReconciler.TryReconcile(
                stronger,"A0VS",MagicHit("target",1000f,1,750),
                quantizer,out converted));
            Assert.AreEqual(750,converted.FinalHpDamage);
            var weakened = new CombatEnemy("target",2000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line,0,"normal",41,
                    "original dynamically observed magic vulnerability"));
            Assert.IsTrue(Original323NativeMagicDamageReconciler.TryReconcile(
                weakened,"A086",MagicHit("target",1000f,41,1150),
                quantizer,out converted));
            Assert.AreEqual(1150,converted.FinalHpDamage);
        }

        [Test]
        public void StaleUnknownLevelOrMismatchedOriginalHpFailsClosed()
        {
            var quantizer = new TestHpConversion();
            Original323NativeDummySpellHit hit;
            var monster = new CombatEnemy("enemy",2000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line,0,"normal",16,"original level"));
            Assert.IsFalse(Original323NativeMagicDamageReconciler.TryReconcile(
                monster,"A086",MagicHit("enemy",1000f,1,750),
                quantizer,out hit));
            Assert.IsFalse(Original323NativeMagicDamageReconciler.TryReconcile(
                monster,"A086",MagicHit("enemy",1000f,16,899),
                quantizer,out hit));
            Assert.IsFalse(Original323NativeMagicDamageReconciler.TryReconcile(
                monster,"A086",MagicHit("enemy",1000f,16,900,true,
                    "MODEL_ONLY guessed native base"),quantizer,out hit));
            var unknown = new CombatEnemy("enemy",2000,
                new EnemyDefenseProfile(CharacterEnemyKind.Line,0,null,
                    "normal","original raw defense not verified"));
            Assert.IsFalse(Original323NativeMagicDamageReconciler.TryReconcile(
                unknown,"A086",MagicHit("enemy",1000f,16,900),
                quantizer,out hit));
            Assert.IsNull(hit);
        }

        [Test]
        public void NativeHpConversionIsMandatoryAndClampsToActualRemainingHp()
        {
            var monster = new CombatEnemy("target",500,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line,0,"normal",16,
                    "original dynamic ability level"));
            Original323NativeDummySpellHit hit;
            Assert.IsFalse(Original323NativeMagicDamageReconciler.TryReconcile(
                monster,"A0VS",MagicHit("target",1000f,16,500),
                null,out hit));
            var quantizer = new TestHpConversion { Approved = false };
            Assert.IsFalse(Original323NativeMagicDamageReconciler.TryReconcile(
                monster,"A0VS",MagicHit("target",1000f,16,500),
                quantizer,out hit));
            quantizer.Approved = true;
            Assert.IsTrue(Original323NativeMagicDamageReconciler.TryReconcile(
                monster,"A0VS",MagicHit("target",1000f,16,500),
                quantizer,out hit));
            Assert.AreEqual(500,hit.FinalHpDamage);
            Assert.IsFalse(Original323NativeMagicDamageReconciler.TryReconcile(
                monster,"A0VS",MagicHit("target",1000f,16,900),
                quantizer,out hit));
        }

        [Test]
        public void VerifiedMagicImpactAdapterCommitsOnlyReconciledDamage()
        {
            var source = new NativeMagic();
            var quantizer = new TestHpConversion();
            var magic = new Original323VerifiedNativeMagicImpactAdapter(
                source,quantizer);
            var game = Create("S-18","h00Q",magic);
            game.Enemy("primary",2000,CharacterEnemyKind.Line,16);
            game.Enemy("another",2000,CharacterEnemyKind.Line,1);
            game.Combat.Step(0);
            Assert.AreEqual(1999,game.Combat.EnemyOf("primary").Health);
            game.Combat.Step(0.25);
            source.Next = MagicImpact(game.Cast.CastId,"impact-native-magic",
                0.25,true,
                new[]{Selected("primary"),Selected("another")},
                MagicHit("primary",1000f,16,900),
                MagicHit("another",1000f,1,750));
            long damage;
            Assert.IsTrue(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out damage));
            Assert.AreEqual(1650,damage);
            Assert.AreEqual(1099,game.Combat.EnemyOf("primary").Health);
            Assert.AreEqual(1250,game.Combat.EnemyOf("another").Health);
            Assert.AreEqual(0,game.Combat.PendingOriginalDummySpellCount);
        }

        [Test]
        public void NativeMagicAdapterDoesNotCommitPartiallyVerifiedBatch()
        {
            var source = new NativeMagic();
            var magic = new Original323VerifiedNativeMagicImpactAdapter(
                source,new TestHpConversion());
            var game = Create("S-18","h00Q",magic);
            game.Enemy("primary",2000,CharacterEnemyKind.Line,16);
            game.Enemy("another",2000,CharacterEnemyKind.Line,1);
            game.Combat.Step(0);
            game.Combat.Step(0.25);
            source.Next = MagicImpact(game.Cast.CastId,"native-invalid",
                0.25,true,
                new[]{Selected("primary"),Selected("another")},
                MagicHit("primary",1000f,16,900),
                MagicHit("another",1000f,1,751));
            long amount;
            Assert.IsFalse(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out amount));
            Assert.AreEqual(0,amount);
            Assert.AreEqual(1999,game.Combat.EnemyOf("primary").Health);
            Assert.AreEqual(2000,game.Combat.EnemyOf("another").Health);
            Assert.AreEqual(1,game.Combat.PendingOriginalDummySpellCount);
            source.Next = MagicImpact(game.Cast.CastId,"native-correct",
                0.25,true,
                new[]{Selected("primary"),Selected("another")},
                MagicHit("primary",1000f,16,900),
                MagicHit("another",1000f,1,750));
            Assert.IsTrue(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out amount));
            Assert.AreEqual(1650,amount);
        }

        [Test]
        public void ImmuneSelectionExcludesAllNativeMagicDamage()
        {
            var source = new NativeMagic();
            var magic = new Original323VerifiedNativeMagicImpactAdapter(
                source,new TestHpConversion());
            var game = Create("S-18","h00Q",magic);
            game.Enemy("primary",2000,CharacterEnemyKind.Line,16);
            game.Enemy("immune",2000,CharacterEnemyKind.Line,16);
            game.Combat.Step(0);
            game.Combat.Step(0.25);
            source.Next = MagicImpact(game.Cast.CastId,"native-immune",
                0.25,true,
                new[]{Selected("primary"),Selected("immune",true)},
                MagicHit("primary",1000f,16,900),
                MagicHit("immune",1000f,16,900));
            long amount;
            Assert.IsFalse(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out amount));
            Assert.AreEqual(1999,game.Combat.EnemyOf("primary").Health);
            Assert.AreEqual(2000,game.Combat.EnemyOf("immune").Health);
            source.Next = MagicImpact(game.Cast.CastId,"native-immune-ok",
                0.25,true,
                new[]{Selected("primary"),Selected("immune",true)},
                MagicHit("primary",1000f,16,900));
            Assert.IsTrue(game.Bridge.TryApplyObservedImpact(
                game.Combat,game.Cast.CastId,out amount));
            Assert.AreEqual(900,amount);
        }

        [Test]
        public void DefenseUpdatedAfterCastUsesImpactTimeLevelNotCastLevel()
        {
            var source = new NativeMagic();
            var adapter = new Original323VerifiedNativeMagicImpactAdapter(
                source, new TestHpConversion());
            var world = Create("S-18", "h00Q", adapter);
            world.Enemy("primary", 2000, CharacterEnemyKind.Line, 16);
            world.Combat.Step(0);
            Assert.AreEqual(16,
                world.Combat.EnemyOf("primary").Defenses.MagicAbilityLevel.Value);
            Assert.AreEqual(1, world.Combat.PendingOriginalDummySpellCount);
            var observedLevel = new LiveMagicLevel();
            Assert.IsTrue(world.Combat.TryApplyObservedEnemyA04QLevel(
                "primary", 16, 1, observedLevel));
            Assert.AreEqual(1, observedLevel.Calls);
            world.Combat.Step(0.25);
            source.Next = MagicImpact(world.Cast.CastId, "native-after-change",
                0.25, true, new[] { Selected("primary") },
                MagicHit("primary", 1000f, 1, 750));
            long actual;
            Assert.IsTrue(world.Bridge.TryApplyObservedImpact(
                world.Combat, world.Cast.CastId, out actual));
            Assert.AreEqual(750, actual);
            Assert.AreEqual(1249, world.Combat.EnemyOf("primary").Health);
            Assert.AreEqual(0, world.Combat.PendingOriginalDummySpellCount);
        }

        [Test]
        public void StaleCastTimeResistanceRejectedAndCorrectionCanRetry()
        {
            var source = new NativeMagic();
            var adapter = new Original323VerifiedNativeMagicImpactAdapter(
                source, new TestHpConversion());
            var world = Create("U-04", "h00M", adapter);
            world.Enemy("primary", 2000, CharacterEnemyKind.Line, 16);
            world.Combat.Step(0);
            Assert.IsTrue(world.Combat.TryApplyObservedEnemyA04QLevel(
                "primary", 16, 1, new LiveMagicLevel()));
            world.Combat.Step(0.25);
            source.Next = MagicImpact(world.Cast.CastId, "stale-native-hit",
                0.25, true, new[] { Selected("primary") },
                MagicHit("primary", 1000f, 16, 900));
            long actual;
            Assert.IsFalse(world.Bridge.TryApplyObservedImpact(
                world.Combat, world.Cast.CastId, out actual));
            Assert.AreEqual(1999, world.Combat.EnemyOf("primary").Health);
            Assert.AreEqual(1, world.Combat.PendingOriginalDummySpellCount);
            source.Next = MagicImpact(world.Cast.CastId, "correct-native-hit",
                0.25, true, new[] { Selected("primary") },
                MagicHit("primary", 1000f, 1, 750));
            Assert.IsTrue(world.Bridge.TryApplyObservedImpact(
                world.Combat, world.Cast.CastId, out actual));
            Assert.AreEqual(750, actual);
            Assert.AreEqual(1249, world.Combat.EnemyOf("primary").Health);
        }

        [Test]
        public void OneChangedMonsterDoesNotChangeOtherTargetsMagicDefense()
        {
            var source = new NativeMagic();
            var bridge = new Original323VerifiedNativeMagicImpactAdapter(
                source, new TestHpConversion());
            var world = Create("S-18", "h00Q", bridge);
            world.Enemy("primary", 2000, CharacterEnemyKind.Line, 16);
            world.Enemy("second", 2000, CharacterEnemyKind.Line, 16);
            world.Combat.Step(0);
            Assert.IsTrue(world.Combat.TryApplyObservedEnemyA04QLevel(
                "primary", 16, 1, new LiveMagicLevel()));
            world.Combat.Step(0.25);
            source.Next = MagicImpact(world.Cast.CastId, "mixed-stale",
                0.25, true,
                new[] { Selected("primary"), Selected("second") },
                MagicHit("primary", 1000f, 1, 750),
                MagicHit("second", 1000f, 1, 750));
            long actual;
            Assert.IsFalse(world.Bridge.TryApplyObservedImpact(
                world.Combat, world.Cast.CastId, out actual));
            Assert.AreEqual(1999, world.Combat.EnemyOf("primary").Health);
            Assert.AreEqual(2000, world.Combat.EnemyOf("second").Health);
            Assert.AreEqual(1, world.Combat.PendingOriginalDummySpellCount);
            source.Next = MagicImpact(world.Cast.CastId, "mixed-correct",
                0.25, true,
                new[] { Selected("primary"), Selected("second") },
                MagicHit("primary", 1000f, 1, 750),
                MagicHit("second", 1000f, 16, 900));
            Assert.IsTrue(world.Bridge.TryApplyObservedImpact(
                world.Combat, world.Cast.CastId, out actual));
            Assert.AreEqual(1650, actual);
            Assert.AreEqual(1249, world.Combat.EnemyOf("primary").Health);
            Assert.AreEqual(1100, world.Combat.EnemyOf("second").Health);
        }

        [Test]
        public void SimultaneousResistanceDebuffAndMagicAmplificationNeedWitness()
        {
            var monster = new CombatEnemy("primary", 2000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line, 0, "normal", 16,
                    "source observed real A04Q baseline"));
            monster.Status.Apply(new CombatEffect("source-resist-down",
                CombatEffectType.MagicResistanceReduction,
                CombatEffectOrigin.TriggeredSkill, 5, 1.0));
            monster.Status.Apply(new CombatEffect("source-magic-amp",
                CombatEffectType.MagicDamageAmplification,
                CombatEffectOrigin.TriggeredSkill, 0.2, 1.0));
            var hit = MagicHit("primary", 1000f, 16, 900);
            var quantizer = new TestHpConversion();
            Original323NativeDummySpellHit actual;
            Assert.IsFalse(Original323NativeMagicDamageReconciler.TryReconcile(
                monster, "A086", hit, quantizer, out actual));
            var modifiers = new CurrentNativeModifiers();
            Assert.IsTrue(Original323NativeMagicDamageReconciler.TryReconcile(
                monster, "A086", hit, quantizer, modifiers, out actual));
            Assert.AreEqual(900, actual.FinalHpDamage);
            Assert.IsTrue(actual.OriginalNativeHitEvidence.Contains(
                "original native modifier stage"));
            Assert.AreEqual(1, modifiers.Calls);
            // This does NOT assert any original stacking multiplier:
            // native 1000 pre-Defend was an independently observed value.
        }

        [Test]
        public void IncompleteBuffWitnessOrSyntheticEvidenceNeverAcceptsDamage()
        {
            var monster = new CombatEnemy("primary", 2000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line, 0, "normal", 16,
                    "observed original baseline"));
            monster.Status.Apply(new CombatEffect("source-resist-down",
                CombatEffectType.MagicResistanceReduction,
                CombatEffectOrigin.TriggeredSkill, 5, 1));
            monster.Status.Apply(new CombatEffect("source-magic-amp",
                CombatEffectType.MagicDamageAmplification,
                CombatEffectOrigin.TriggeredSkill, 0.2, 1));
            var hit = MagicHit("primary", 1000f, 16, 900);
            var policy = new TestHpConversion();
            var modifiers = new CurrentNativeModifiers();
            Original323NativeDummySpellHit confirmed;
            modifiers.ExpectedCount = 1;
            Assert.IsFalse(Original323NativeMagicDamageReconciler.TryReconcile(
                monster, "A0VS", hit, policy, modifiers, out confirmed));
            modifiers.ExpectedCount = 2;
            modifiers.Evidence = "MODEL_ONLY guessed stacking policy";
            Assert.IsFalse(Original323NativeMagicDamageReconciler.TryReconcile(
                monster, "A0VS", hit, policy, modifiers, out confirmed));
            modifiers.Evidence = "observed original complete native modifier state";
            modifiers.Approved = false;
            Assert.IsFalse(Original323NativeMagicDamageReconciler.TryReconcile(
                monster, "A0VS", hit, policy, modifiers, out confirmed));
            Assert.IsNull(confirmed);
            modifiers.Approved = true;
            Assert.IsTrue(Original323NativeMagicDamageReconciler.TryReconcile(
                monster, "A0VS", hit, policy, modifiers, out confirmed));
        }

        private sealed class MutatingMagicModifiers :
            IOriginal323NativeMagicModifierWitness
        {
            public Action<CombatEnemy> Change;
            public bool TryConfirmNativeModifiersAtImpact(
                CombatEnemy monster, string ability,
                Original323ObservedNativeMagicHit hit,
                IReadOnlyList<CombatEffect> active,
                out string evidence)
            {
                var original = new CurrentNativeModifiers();
                bool approved = original.TryConfirmNativeModifiersAtImpact(
                    monster, ability, hit, active, out evidence);
                if (approved && Change != null) Change(monster);
                return approved;
            }
        }

        private sealed class MutatingHpConverter :
            IOriginal323NativeMagicHpConversion
        {
            public CombatEnemy Monster;
            public bool TryConvertNativeSpellDamage(float postDefend,
                string originalAbility, out long amount)
            {
                amount = (long)Math.Round(postDefend,
                    MidpointRounding.AwayFromZero);
                Monster.Status.Apply(new CombatEffect("late-all-damage",
                    CombatEffectType.AllDamageAmplification,
                    CombatEffectOrigin.TriggeredSkill, 0.1, 1));
                return true;
            }
        }

        [Test]
        public void SameValueRefreshNewStackAndElapsedTimeCannotReuseWitness()
        {
            var changes = new Action<CombatEnemy>[] {
                // Same values and same source ID, but a different instance.
                monster => {
                    monster.Status.RemoveSource("source-magic-amp");
                    monster.Status.Apply(new CombatEffect("source-magic-amp",
                        CombatEffectType.MagicDamageAmplification,
                        CombatEffectOrigin.TriggeredSkill, 0.2, 1));
                },
                monster => monster.Status.Apply(new CombatEffect(
                    "new-source", CombatEffectType.AllDamageAmplification,
                    CombatEffectOrigin.TriggeredSkill, 0.1, 1)),
                // Both effects stay active, but the observed time is stale.
                monster => monster.Status.Advance(0.1)
            };
            foreach (var change in changes)
            {
                var monster = new CombatEnemy("primary", 2000,
                    Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                        CharacterEnemyKind.Line, 0, "normal", 16,
                        "source observed original magic defense"));
                monster.Status.Apply(new CombatEffect("source-resist-down",
                    CombatEffectType.MagicResistanceReduction,
                    CombatEffectOrigin.TriggeredSkill, 5, 1));
                monster.Status.Apply(new CombatEffect("source-magic-amp",
                    CombatEffectType.MagicDamageAmplification,
                    CombatEffectOrigin.TriggeredSkill, 0.2, 1));
                var observer = new MutatingMagicModifiers { Change = change };
                Original323NativeDummySpellHit confirmed;
                Assert.IsFalse(Original323NativeMagicDamageReconciler.TryReconcile(
                    monster, "A086", MagicHit("primary", 1000f, 16, 900),
                    new TestHpConversion(), observer, out confirmed));
                Assert.IsNull(confirmed);
                Assert.AreEqual(2000, monster.Health);
            }
        }

        [Test]
        public void HpConversionCannotCertifyAnImpactAfterModifiersChange()
        {
            var monster = new CombatEnemy("primary", 2000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line, 0, "normal", 16,
                    "source observed original magic defense"));
            Original323NativeDummySpellHit confirmed;
            Assert.IsFalse(Original323NativeMagicDamageReconciler.TryReconcile(
                monster, "A086", MagicHit("primary", 1000f, 16, 900),
                new MutatingHpConverter { Monster = monster }, out confirmed));
            Assert.IsNull(confirmed);
            Assert.AreEqual(2000, monster.Health);
            Assert.AreEqual(1, monster.Status.ActiveCount);
        }

        [Test]
        public void ExpiredMagicModifierNoLongerRequiresCurrentStackWitness()
        {
            var monster = new CombatEnemy("primary", 2000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line, 0, "normal", 16,
                    "observed original baseline"));
            monster.Status.Apply(new CombatEffect("source-resist-down",
                CombatEffectType.MagicResistanceReduction,
                CombatEffectOrigin.TriggeredSkill, 5, 0.25));
            monster.Status.Apply(new CombatEffect("source-magic-amp",
                CombatEffectType.MagicDamageAmplification,
                CombatEffectOrigin.TriggeredSkill, 0.2, 0.25));
            var policy = new TestHpConversion();
            Original323NativeDummySpellHit hit;
            Assert.IsFalse(Original323NativeMagicDamageReconciler.TryReconcile(
                monster, "A086", MagicHit("primary", 1000f, 16, 900),
                policy, out hit));
            monster.Status.Advance(0.25);
            Assert.AreEqual(0, monster.Status.ActiveCount);
            Assert.IsTrue(Original323NativeMagicDamageReconciler.TryReconcile(
                monster, "A086", MagicHit("primary", 1000f, 16, 900),
                policy, out hit));
            Assert.AreEqual(900, hit.FinalHpDamage);
        }

        [Test]
        public void WitnessMayConfirmNativeDefendDidNotApplyToThisSpell()
        {
            var monster = new CombatEnemy("primary", 2000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line, 0, "normal", 1,
                    "original observed magic defense level"));
            var quantizer = new TestHpConversion();
            Original323NativeDummySpellHit result;
            Assert.IsFalse(Original323NativeMagicDamageReconciler.TryReconcile(
                monster, "A086", MagicHit("primary", 1000f, 1, 750, false),
                quantizer, out result));
            Assert.IsTrue(Original323NativeMagicDamageReconciler.TryReconcile(
                monster, "A086", MagicHit("primary", 1000f, 1, 1000, false),
                quantizer, out result));
            Assert.AreEqual(1000, result.FinalHpDamage);
        }

        private sealed class OrderedMagicReductionWitness :
            IOriginal323MagicDefenseStackSequenceWitness
        {
            public int Calls;
            public bool Approved = true;
            public string Evidence =
                "original native ordered Bd5 callbacks for this living enemy";
            public string ExpectedEnemy = "primary";

            public bool TryConfirmOriginalOrderedStackCalls(string enemy,
                int original, int finalLevel,
                IReadOnlyList<Original323ObservedMagicDefenseStackCall> calls,
                out string proof)
            {
                Calls++;
                proof = Evidence;
                return Approved && enemy == ExpectedEnemy &&
                    calls != null && calls.Count >= 1;
            }
        }

        private static Original323ObservedMagicDefenseStackCall Bd5Call(
            string name, string eventId)
        {
            int line, delta;
            Assert.IsTrue(Original323MagicDefenseStackRules.TryGetSourceIncrease(
                name, out line, out delta));
            return new Original323ObservedMagicDefenseStackCall(
                name, line, delta, eventId,
                "observed original JASS callback and native target");
        }

        [Test]
        public void OriginalNineJassMagicDefenseCallbacksHaveExactPinnedIncrements()
        {
            var fixedNames = new[] { "BoM", "Br6", "Bur", "BvG", "B46",
                "B7I", "CEa", "CFI", "CGm" };
            var fixedLines = new[] {15139,17604,19761,20134,32897,
                35379,43508,44588,46939};
            var fixedDeltas = new[] {10,10,1,18,5,10,1,3,10};
            for (int i=0;i<fixedNames.Length;i++)
            {
                int line,delta;
                Assert.IsTrue(Original323MagicDefenseStackRules.TryGetSourceIncrease(
                    fixedNames[i],out line,out delta));
                Assert.AreEqual(fixedLines[i],line);
                Assert.AreEqual(fixedDeltas[i],delta);
            }
            int unknownLine,unknownDelta;
            Assert.IsFalse(Original323MagicDefenseStackRules.TryGetSourceIncrease(
                "unverified-skill",out unknownLine,out unknownDelta));
        }

        [Test]
        public void DifferentOriginalMagicReductionHitsAccumulateAbilityLevels()
        {
            var monster = new CombatEnemy("primary",2000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line, 50, "normal", 16,
                    "original monster magic defense"));
            var events = new[] {
                Bd5Call("Bur","original-hit-1"),
                Bd5Call("CFI","original-hit-2"),
                Bd5Call("B46","original-hit-3")
            };
            int planned;
            Assert.IsTrue(Original323MagicDefenseStackRules.TryPlanObservedStack(
                16,events,out planned));
            Assert.AreEqual(25,planned);
            var callbacks = new OrderedMagicReductionWitness();
            var native = new LiveMagicLevel { VerifiedEnemyId="primary" };
            // Native observer for this test confirms 16 -> 25, while the
            // earlier LiveMagicLevel fixture confirms 16 -> 1 only.
            var finalLevel = new GenericNativeA04QLevel { OldLevel=16,NewLevel=25 };
            Assert.IsTrue(Original323MagicDefenseStackRules.TryApplyObservedStack(
                monster,16,25,events,callbacks,finalLevel));
            Assert.AreEqual(25,monster.Defenses.MagicAbilityLevel.Value);
            Assert.AreEqual(1.0,monster.Defenses.MagicResistancePercent.Value,0.0001);
            Assert.AreEqual(50,monster.Defenses.BaseArmor.Value);
            Assert.AreEqual(2000,monster.Health);
            Assert.AreEqual(1,callbacks.Calls);
            Assert.AreEqual(1,finalLevel.Calls);
        }

        private sealed class GenericNativeA04QLevel :
            IOriginal323LiveMonsterA04QWitness
        {
            public int OldLevel = 16;
            public int NewLevel = 25;
            public string EnemyId = "primary";
            public bool Approved = true;
            public int Calls;
            public string Evidence = "original native observed A04Q level";

            public bool TryConfirm(string enemyId,int oldLevel,int newLevel,
                out string source)
            {
                Calls++;
                source = Evidence;
                return Approved && enemyId == EnemyId &&
                    oldLevel == OldLevel && newLevel == NewLevel;
            }
        }

        [Test]
        public void RepeatedSourceCanStackOnlyWithDistinctOriginalNativeEvents()
        {
            var two = new[] {
                Bd5Call("Bur","source-hit-a"),
                Bd5Call("Bur","source-hit-b")
            };
            int next;
            Assert.IsTrue(Original323MagicDefenseStackRules.TryPlanObservedStack(
                16,two,out next));
            Assert.AreEqual(18,next);
            var duplicate = new[] {
                Bd5Call("Bur","source-hit-a"),
                Bd5Call("Bur","source-hit-a")
            };
            Assert.IsFalse(Original323MagicDefenseStackRules.TryPlanObservedStack(
                16,duplicate,out next));
            Assert.AreEqual(0,next);
        }

        [Test]
        public void UnsupportedLevelsAndWrongCallbackPayloadCannotMutateEnemy()
        {
            var monster = new CombatEnemy("primary",2000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line, 0, "normal", 16,
                    "original observed monster magic defense"));
            var native = new GenericNativeA04QLevel();
            var observer = new OrderedMagicReductionWitness();
            int planned;
            Assert.IsFalse(Original323MagicDefenseStackRules.TryPlanObservedStack(
                16,new[]{Bd5Call("Br6","first"),
                    Bd5Call("BvG","second")},out planned));
            Assert.IsFalse(Original323MagicDefenseStackRules.TryPlanObservedStack(
                16,new[]{new Original323ObservedMagicDefenseStackCall(
                    "Bur",19761,10,"incorrect-increment",
                    "source observed JASS damage callback")},out planned));
            Assert.IsFalse(Original323MagicDefenseStackRules.TryPlanObservedStack(
                16,new[]{new Original323ObservedMagicDefenseStackCall(
                    "Bur",19762,1,"wrong-source-line",
                    "source observed JASS damage callback")},out planned));
            Assert.IsFalse(Original323MagicDefenseStackRules.TryPlanObservedStack(
                16,new[]{Bd5Call("Bur","TEST_ONLY: duplicate-like-ID")},
                out planned));
            Assert.IsFalse(Original323MagicDefenseStackRules.TryApplyObservedStack(
                monster,16,25,new[]{Bd5Call("Bur","good-hit")},
                observer,native));
            Assert.AreEqual(16,monster.Defenses.MagicAbilityLevel.Value);
            Assert.AreEqual(0,native.Calls);
        }

        [Test]
        public void MissingSourceOrNativeApprovalKeepsLevelAndHpUntouched()
        {
            var monster = new CombatEnemy("primary",2000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line,0,"normal",16,
                    "original monster defense"));
            var calls = new[]{Bd5Call("Bur","event-one")};
            var callbacks = new OrderedMagicReductionWitness();
            var native = new GenericNativeA04QLevel { NewLevel=17 };
            Assert.IsFalse(Original323MagicDefenseStackRules.TryApplyObservedStack(
                monster,16,17,calls,null,native));
            callbacks.Evidence = "MODEL_ONLY invented callback";
            Assert.IsFalse(Original323MagicDefenseStackRules.TryApplyObservedStack(
                monster,16,17,calls,callbacks,native));
            callbacks.Evidence = "observed actual callback sequence";
            native.Approved=false;
            Assert.IsFalse(Original323MagicDefenseStackRules.TryApplyObservedStack(
                monster,16,17,calls,callbacks,native));
            Assert.AreEqual(16,monster.Defenses.MagicAbilityLevel.Value);
            Assert.AreEqual(2000,monster.Health);
            native.Approved=true;
            Assert.IsTrue(Original323MagicDefenseStackRules.TryApplyObservedStack(
                monster,16,17,calls,callbacks,native));
            Assert.IsFalse(Original323MagicDefenseStackRules.TryApplyObservedStack(
                monster,16,17,calls,callbacks,native));
            Assert.AreEqual(17,monster.Defenses.MagicAbilityLevel.Value);
        }

        [Test]
        public void DuplicateImportedNativeLevelStatusCannotBeCountedAgain()
        {
            var monster = new CombatEnemy("primary",2000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line,0,"normal",16,
                    "original monster defense"));
            monster.Status.Apply(new CombatEffect("jass:A04Q:original-hit",
                CombatEffectType.OriginalA04QLevelIncrease,
                CombatEffectOrigin.Aura,1,0));
            var native = new GenericNativeA04QLevel {NewLevel=17};
            Assert.IsFalse(Original323MagicDefenseStackRules.TryApplyObservedStack(
                monster,16,17,new[]{Bd5Call("Bur","event-one")},
                new OrderedMagicReductionWitness(),native));
            Assert.AreEqual(0,native.Calls);
            Assert.AreEqual(16,monster.Defenses.MagicAbilityLevel.Value);
        }

        [Test]
        public void StackedMagicDefenseImmediatelyFeedsPendingLightningImpact()
        {
            var impactSource = new NativeMagic();
            var magic = new Original323VerifiedNativeMagicImpactAdapter(
                impactSource,new TestHpConversion());
            var world = Create("S-18","h00Q",magic);
            world.Enemy("primary",2000,CharacterEnemyKind.Line,16);
            world.Combat.Step(0);
            Assert.AreEqual(1,world.Combat.PendingOriginalDummySpellCount);
            var events=new[]{Bd5Call("Bur","impact-pre-1"),
                Bd5Call("CFI","impact-pre-2")};
            var callback = new OrderedMagicReductionWitness();
            var native = new GenericNativeA04QLevel {NewLevel=20};
            Assert.IsTrue(world.Combat.TryApplyObservedEnemyMagicDefenseStack(
                "primary",16,20,events,callback,native));
            world.Combat.Step(0.25);
            impactSource.Next=MagicImpact(world.Cast.CastId,
                "native-stacked-lightning",0.25,true,
                new[]{Selected("primary")},
                MagicHit("primary",1000f,20,940));
            long actual;
            Assert.IsTrue(world.Bridge.TryApplyObservedImpact(
                world.Combat,world.Cast.CastId,out actual));
            Assert.AreEqual(940,actual);
            Assert.AreEqual(1059,world.Combat.EnemyOf("primary").Health);
            Assert.AreEqual(0,world.Combat.PendingOriginalDummySpellCount);
        }

        [Test]
        public void StaleStackAmountDuringPendingSpellRejectsWholeImpact()
        {
            var impactSource=new NativeMagic();
            var world=Create("S-18","h00Q",
                new Original323VerifiedNativeMagicImpactAdapter(
                    impactSource,new TestHpConversion()));
            world.Enemy("primary",2000,CharacterEnemyKind.Line,16);
            world.Combat.Step(0);
            Assert.IsTrue(world.Combat.TryApplyObservedEnemyMagicDefenseStack(
                "primary",16,17,new[]{Bd5Call("Bur","pre-impact-event")},
                new OrderedMagicReductionWitness(),
                new GenericNativeA04QLevel {NewLevel=17}));
            world.Combat.Step(0.25);
            impactSource.Next=MagicImpact(world.Cast.CastId,
                "stale-stacked",0.25,true,new[]{Selected("primary")},
                MagicHit("primary",1000f,16,900));
            long actual;
            Assert.IsFalse(world.Bridge.TryApplyObservedImpact(
                world.Combat,world.Cast.CastId,out actual));
            Assert.AreEqual(1999,world.Combat.EnemyOf("primary").Health);
            impactSource.Next=MagicImpact(world.Cast.CastId,
                "correct-stacked",0.25,true,new[]{Selected("primary")},
                MagicHit("primary",1000f,17,910));
            Assert.IsTrue(world.Bridge.TryApplyObservedImpact(
                world.Combat,world.Cast.CastId,out actual));
            Assert.AreEqual(910,actual);
            Assert.AreEqual(1089,world.Combat.EnemyOf("primary").Health);
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
