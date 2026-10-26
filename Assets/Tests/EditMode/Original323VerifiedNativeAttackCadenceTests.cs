using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323VerifiedNativeAttackCadenceTests
    {
        private sealed class ZeroRandom : ICombatProcRandom
        {
            public int Calls;
            public int Next(int exclusive) { Calls++; return 0; }
        }

        // TEST_ONLY constant output. These intervals are NOT the actual
        // WC3 AOae stacking, attack-speed cap, or attack-phase equation.
        private sealed class ObservedTiming : IOriginal323VerifiedNativeAttackCadence
        {
            public double Seconds = 0.5;
            public bool Approved = true;
            public bool HasProof = true;
            public int Calls;
            public double LastOriginalInterval;
            public int LastEffectCount;

            public bool TryResolveNextInterval(string id, BasicAttackSpec stats,
                IReadOnlyList<CombatEffect> effects, double now,
                out double next, out string evidence)
            {
                Calls++;
                LastOriginalInterval = stats.IntervalSeconds;
                LastEffectCount = effects.Count;
                next = Seconds;
                evidence = HasProof ? "TEST_ONLY independent original native cadence" : null;
                return Approved;
            }
        }

        private sealed class Damage : IOriginalDamageResolver
        {
            public int Calls;
            public long ResolveBasicDamage(BasicDamageRequest request,
                CombatEnemy target)
            {
                Calls++;
                return 10; // TEST_ONLY already verified final basic damage
            }
        }

        private sealed class State
        {
            public CombatScheduler Clock;
            public CombatResolution Resolution;
            public CombatAttackPipeline Attacks;
            public Damage Formula;
            public ZeroRandom Random;
            public List<CombatEffect> Effects;
            public int AttackDeclared;
        }

        private static State Setup(bool speedBuff)
        {
            var c = new State();
            var game = new GameSession();
            game.Start();
            game.BeginWave(1);
            c.Clock = new CombatScheduler();
            c.Resolution = new CombatResolution(game, c.Clock);
            c.Random = new ZeroRandom();
            c.Formula = new Damage();
            c.Attacks = new CombatAttackPipeline(c.Clock, c.Resolution,
                new CombatSkillTriggerEngine(c.Random), c.Formula);
            c.Effects = new List<CombatEffect>();
            if (speedBuff)
                c.Effects.Add(new CombatEffect("original-A0FT",
                    CombatEffectType.AttackSpeedBuff, CombatEffectOrigin.Aura,
                    10, 0));
            c.Attacks.ConfigureAttackerEffectLookup(id => c.Effects.AsReadOnly());
            c.Attacks.ConfigureVerifiedAttackDeclared((id, target) => c.AttackDeclared++);
            c.Resolution.Spawn(new CombatEnemy("enemy", 500));
            c.Attacks.RegisterUnit("attacker", new UnitCombatSpec(
                "h01N", new BasicAttackSpec(10, 1, 500,
                    DamageSchool.Physical, DamageTraits.None),
                new CombatSkillSpec[0], "TEST_ONLY source object"), 0);
            return c;
        }

        private static bool Swing(State c)
        {
            BasicAttackResult result;
            return c.Attacks.TryBasicAttack("attacker",
                new[] { new TargetCandidate("enemy", 100, 0, true) },
                out result);
        }

        [Test]
        public void VerifiedNativeBuffActuallyChangesNextAttackSchedulerTime()
        {
            var c = Setup(true);
            var native = new ObservedTiming();
            c.Attacks.ConfigureVerifiedNativeAttackCadence(native);
            Assert.IsFalse(c.Attacks.HasUnresolvedNativeAttackSpeed("attacker"));
            Assert.IsTrue(Swing(c));
            Assert.AreEqual(0.5, c.Attacks.NextAttackAt("attacker").Value, 1e-9);
            Assert.AreEqual(1.0, native.LastOriginalInterval, 1e-9);
            Assert.AreEqual(1, native.LastEffectCount);
            c.Attacks.Advance(0.49);
            Assert.IsFalse(Swing(c));
            c.Attacks.Advance(0.01);
            Assert.IsTrue(Swing(c));
            Assert.AreEqual(1.0, c.Attacks.NextAttackAt("attacker").Value, 1e-9);
            Assert.AreEqual(480, c.Resolution.GetEnemy("enemy").Health);
            Assert.AreEqual(2, native.Calls);
            Assert.AreEqual(2, c.AttackDeclared);
        }

        [Test]
        public void NoOriginalNativeCadenceDoesNotRollDamageOrSpendAttackCooldown()
        {
            var c = Setup(true);
            Assert.IsTrue(c.Attacks.HasUnresolvedNativeAttackSpeed("attacker"));
            Assert.IsFalse(Swing(c));
            Assert.AreEqual(0, c.Formula.Calls);
            Assert.AreEqual(0, c.AttackDeclared);
            Assert.AreEqual(0, c.Random.Calls);
            Assert.AreEqual(500, c.Resolution.GetEnemy("enemy").Health);
            Assert.AreEqual(0, c.Attacks.NextAttackAt("attacker").Value, 1e-9);
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void MissingApprovalOrIndependentEvidenceRejectsNativeAttack(
            bool approved, bool proof)
        {
            var c = Setup(true);
            c.Attacks.ConfigureVerifiedNativeAttackCadence(
                new ObservedTiming { Approved = approved, HasProof = proof });
            Assert.IsFalse(Swing(c));
            Assert.AreEqual(0, c.Formula.Calls);
            Assert.AreEqual(0, c.AttackDeclared);
            Assert.AreEqual(500, c.Resolution.GetEnemy("enemy").Health);
        }

        [Test]
        public void BuffExpirationRestoresExactUnmodifiedNativeCooldown()
        {
            var c = Setup(true);
            var native = new ObservedTiming();
            c.Attacks.ConfigureVerifiedNativeAttackCadence(native);
            Assert.IsTrue(Swing(c));
            c.Effects.Clear(); // source-observed original buff loss
            c.Attacks.Advance(0.5);
            Assert.IsTrue(Swing(c));
            Assert.AreEqual(1.5, c.Attacks.NextAttackAt("attacker").Value, 1e-9);
            Assert.AreEqual(1, native.Calls);
            Assert.AreEqual(480, c.Resolution.GetEnemy("enemy").Health);
        }

        [TestCase(0)]
        [TestCase(-0.1)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NaN)]
        public void InvalidProofOfCadenceNeverSpendsNativeDamage(double interval)
        {
            var c = Setup(true);
            c.Attacks.ConfigureVerifiedNativeAttackCadence(
                new ObservedTiming { Seconds = interval });
            Assert.Throws<InvalidOperationException>(() => Swing(c));
            Assert.AreEqual(500, c.Resolution.GetEnemy("enemy").Health);
            Assert.AreEqual(0, c.Formula.Calls);
            Assert.AreEqual(0, c.AttackDeclared);
        }

        [Test]
        public void BasicAttackWithoutSpeedBuffPreservesPreviousSchedulerBehavior()
        {
            var c = Setup(false);
            Assert.IsTrue(Swing(c));
            Assert.AreEqual(1, c.Attacks.NextAttackAt("attacker").Value, 1e-9);
            c.Attacks.Advance(1);
            Assert.IsTrue(Swing(c));
            Assert.AreEqual(2, c.Attacks.NextAttackAt("attacker").Value, 1e-9);
            Assert.AreEqual(480, c.Resolution.GetEnemy("enemy").Health);
        }
    }
}
