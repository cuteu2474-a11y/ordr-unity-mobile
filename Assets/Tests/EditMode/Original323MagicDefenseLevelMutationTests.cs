using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    // Observed source callbacks below are deliberately synthetic fixtures.
    // The original map JASS caller/line/delta catalog itself is pinned.
    public sealed class Original323MagicDefenseLevelMutationTests
    {
        private sealed class Calls : IOriginal323MagicDefenseLevelMutationWitness
        {
            public int CallsObserved;
            public bool Approved = true;
            public string Evidence = "original verified ordered Bd4/Bd5 native callbacks";
            public bool TryConfirmOrderedOriginalLevelMutations(
                string id, int before, int after,
                IReadOnlyList<Original323ObservedMagicDefenseLevelMutation> ops,
                out string evidence)
            {
                CallsObserved++;
                evidence = Evidence;
                return Approved && id == "line-a" && before == 16 &&
                    after == 15 && ops.Count == 2;
            }
        }

        private sealed class Native : IOriginal323LiveMonsterA04QWitness
        {
            public bool Approved = true;
            public int Calls;
            public bool TryConfirm(string id, int before, int after,
                out string evidence)
            {
                Calls++;
                evidence = "original Warcraft native current monster A04Q";
                return Approved && id == "line-a" && before == 16 && after == 15;
            }
        }

        private static CombatEnemy Monster()
        {
            return new CombatEnemy("line-a", 3000,
                Ordr323VerifiedDefenseAbilities.FromVerifiedMagicAbilityLevel(
                    CharacterEnemyKind.Line, 42, "normal", 16,
                    "original 2.323C A04Q known live monster"));
        }

        private static Original323ObservedMagicDefenseLevelMutation Set(
            string caller = "CVX", int line = 59504, int operand = 5,
            string eventId = "original-event-set")
        {
            return new Original323ObservedMagicDefenseLevelMutation(
                caller,line,
                Original323MagicDefenseLevelMutationKind.SetToOriginalRoundLevel,
                operand,eventId,"source callback observed on this enemy");
        }

        private static Original323ObservedMagicDefenseLevelMutation Add(
            string caller = "BoM", int line = 15139, int operand = 10,
            string eventId = "original-event-add")
        {
            return new Original323ObservedMagicDefenseLevelMutation(
                caller,line,
                Original323MagicDefenseLevelMutationKind.IncreaseFromOriginalSkill,
                operand,eventId,"original per-monster skill trigger confirmed");
        }

        [Test]
        public void AllEightOriginalSetCallsitesArePinnedToCorrectOperands()
        {
            var lines = new Dictionary<string,int> {
                {"Bvi",20698},{"CKz",50860},{"CNI",52131},
                {"CVX",59504},{"CVY",59518},{"CVZ",59552},
                {"CVo",59681},{"CVp",59694}
            };
            foreach (var item in lines)
            {
                int actual; string argument;
                Assert.IsTrue(
                    Original323MagicDefenseLevelMutationRules
                        .TryGetVerifiedSetCaller(item.Key,
                            out actual,out argument));
                Assert.AreEqual(item.Value,actual);
                Assert.AreEqual(item.Key=="CNI" ? "NJ" : "LK[JG]",argument);
            }
            int unknown; string operand;
            Assert.IsFalse(Original323MagicDefenseLevelMutationRules
                .TryGetVerifiedSetCaller("made-up",out unknown,out operand));
        }

        [Test]
        public void SetThenSkillIncreaseUsesFifteenNotTwentyOneOrTwentySix()
        {
            int level;
            Assert.IsTrue(Original323MagicDefenseLevelMutationRules
                .TryPlanOrderedLevelMutations(16,
                    new[]{Set(),Add()},out level));
            Assert.AreEqual(15,level);
            // Reversing event order changes the answer because Bd4 SETS.
            Assert.IsTrue(Original323MagicDefenseLevelMutationRules
                .TryPlanOrderedLevelMutations(16,
                    new[]{Add(eventId:"first-add"),Set(eventId:"last-set")},
                    out level));
            Assert.AreEqual(5,level);
        }

        [Test]
        public void VerifiedOrderedMutationsChangeOnlyLiveMagicDefense()
        {
            var enemy=Monster();
            var caller=new Calls();
            var native=new Native();
            Assert.IsTrue(Original323MagicDefenseLevelMutationRules
                .TryApplyObservedLevelMutations(enemy,16,15,
                    new[]{Set(),Add()},caller,native));
            Assert.AreEqual(1,caller.CallsObserved);
            Assert.AreEqual(1,native.Calls);
            Assert.AreEqual(15,enemy.Defenses.MagicAbilityLevel.Value);
            Assert.AreEqual(3000,enemy.Health);
            Assert.AreEqual(3000,enemy.MaxHealth);
            Assert.AreEqual(42,enemy.Defenses.BaseArmor.Value);
            Assert.AreEqual("normal",enemy.Defenses.WarcraftArmorType);
        }

        [Test]
        public void InvalidSourceAndStaleNativeEventsDoNotAlterTheMonster()
        {
            var enemy=Monster();
            var caller=new Calls();
            var native=new Native();
            var invalid=new [] {Set("CVX",59504,5),
                Add("BoM",15139,11)};
            Assert.IsFalse(Original323MagicDefenseLevelMutationRules
                .TryApplyObservedLevelMutations(enemy,16,16,
                    invalid,caller,native));
            Assert.AreEqual(0,caller.CallsObserved);
            Assert.IsFalse(Original323MagicDefenseLevelMutationRules
                .TryApplyObservedLevelMutations(enemy,15,15,
                    new[]{Set(),Add()},caller,native));
            Assert.IsFalse(Original323MagicDefenseLevelMutationRules
                .TryApplyObservedLevelMutations(enemy,16,14,
                    new[]{Set(),Add()},caller,native));
            Assert.AreEqual(16,enemy.Defenses.MagicAbilityLevel.Value);
        }

        [Test]
        public void DuplicateEventsAndBeyondObjectDataCannotBeApproved()
        {
            int level;
            Assert.IsFalse(Original323MagicDefenseLevelMutationRules
                .TryPlanOrderedLevelMutations(16,
                    new[]{Set(eventId:"same"),Add(eventId:"same")},
                    out level));
            Assert.IsFalse(Original323MagicDefenseLevelMutationRules
                .TryPlanOrderedLevelMutations(16,
                    new[]{Set(operand:40),Add()},out level));
            Assert.IsFalse(Original323MagicDefenseLevelMutationRules
                .TryPlanOrderedLevelMutations(16,
                    new[]{Set(operand:0)},out level));
            Assert.IsFalse(Original323MagicDefenseLevelMutationRules
                .TryPlanOrderedLevelMutations(16,
                    new[]{Set(line:59518)},out level));
            Assert.IsFalse(Original323MagicDefenseLevelMutationRules
                .TryPlanOrderedLevelMutations(16,
                    new[]{new Original323ObservedMagicDefenseLevelMutation(
                        "CVX",59504,
                        Original323MagicDefenseLevelMutationKind.SetToOriginalRoundLevel,
                        5,"MODEL_ONLY fake event","verified")},out level));
        }

        [Test]
        public void MissingRealSequenceOrNativeLevelEvidenceFailsClosed()
        {
            var enemy=Monster();
            var calls=new Calls {Approved=false};
            var native=new Native();
            Assert.IsFalse(Original323MagicDefenseLevelMutationRules
                .TryApplyObservedLevelMutations(enemy,16,15,
                    new[]{Set(),Add()},calls,native));
            Assert.AreEqual(0,native.Calls);
            calls.Approved=true;
            calls.Evidence="SYNTHETIC invented ordering";
            Assert.IsFalse(Original323MagicDefenseLevelMutationRules
                .TryApplyObservedLevelMutations(enemy,16,15,
                    new[]{Set(),Add()},calls,native));
            calls.Evidence="original native ordering source";
            native.Approved=false;
            Assert.IsFalse(Original323MagicDefenseLevelMutationRules
                .TryApplyObservedLevelMutations(enemy,16,15,
                    new[]{Set(),Add()},calls,native));
            Assert.AreEqual(16,enemy.Defenses.MagicAbilityLevel.Value);
            native.Approved=true;
            Assert.IsTrue(Original323MagicDefenseLevelMutationRules
                .TryApplyObservedLevelMutations(enemy,16,15,
                    new[]{Set(),Add()},calls,native));
            Assert.IsFalse(Original323MagicDefenseLevelMutationRules
                .TryApplyObservedLevelMutations(enemy,16,15,
                    new[]{Set(),Add()},calls,native));
        }
    }
}
