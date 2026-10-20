using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public class CombatDataAndSkillTriggerTests
    {
        // 테스트 값은 임의의 가상 스펙이다. 원작 유닛 스탯/스킬 확률이 아니다.
        private sealed class SequenceRandom : ICombatProcRandom
        {
            public int Calls { get; private set; }
            private readonly Queue<int> sequence;
            public SequenceRandom(params int[] values) { sequence = new Queue<int>(values); }
            public int Next(int max)
            {
                Calls++;
                return sequence.Count > 0 ? sequence.Dequeue() : 0;
            }
        }

        private static CombatSkillSpec Skill(string rawCode, SkillTriggerKind trigger,
            int chance, double cooldown = 0, double period = 0)
        {
            return new CombatSkillSpec(rawCode, trigger, chance, cooldown, period,
                SkillTargetArea.Single, 0,
                new[] { new SkillEffectSpec(SkillEffectKind.Damage, 10, 0,
                    DamageSchool.Magical, DamageTraits.Explosive) }, "TEST_FIXTURE");
        }

        private static UnitCombatSpec Unit(params CombatSkillSpec[] skills)
        {
            return new UnitCombatSpec("TEST", new BasicAttackSpec(10, 1, 5,
                DamageSchool.Physical, DamageTraits.None), skills, "TEST_FIXTURE");
        }

        [Test]
        public void ExplosionIsIndependentOfDamageSchool()
        {
            var physicalExplosion = new SkillEffectSpec(SkillEffectKind.Damage, 100, 0,
                DamageSchool.Physical, DamageTraits.Explosive);
            var magicalExplosion = new SkillEffectSpec(SkillEffectKind.Damage, 100, 0,
                DamageSchool.Magical, DamageTraits.Explosive);
            Assert.AreEqual(DamageSchool.Physical, physicalExplosion.DamageSchool);
            Assert.AreEqual(DamageSchool.Magical, magicalExplosion.DamageSchool);
            Assert.AreEqual(DamageTraits.Explosive, physicalExplosion.Traits);
            Assert.AreEqual(DamageTraits.Explosive, magicalExplosion.Traits);
        }

        [Test]
        public void InvalidSkillChanceRadiusAndPeriodAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Skill("x", SkillTriggerKind.OnHit, 1000001));
            Assert.Throws<ArgumentException>(() =>
                Skill("x", SkillTriggerKind.Periodic, 1000000, 0, 0));
            Assert.Throws<ArgumentException>(() =>
                new CombatSkillSpec("x", SkillTriggerKind.OnHit, 500000, 0, 0,
                    SkillTargetArea.CircleAroundTarget, 0,
                    new[] { new SkillEffectSpec(SkillEffectKind.Stun, 1, 2,
                        DamageSchool.ScriptDefined, DamageTraits.None) }, "TEST_FIXTURE"));
        }

        [Test]
        public void DuplicateSkillRawCodeIsRejected()
        {
            Assert.Throws<ArgumentException>(() => Unit(
                Skill("same", SkillTriggerKind.OnHit, 1000000),
                Skill("same", SkillTriggerKind.OnAttack, 1000000)));
        }

        [Test]
        public void ChanceBoundariesAndRepeatedEventAreHandled()
        {
            var random = new SequenceRandom(499999, 500000);
            var engine = new CombatSkillTriggerEngine(random);
            engine.RegisterUnit("unit-1", Unit(Skill("chance", SkillTriggerKind.OnHit, 500000)));
            Assert.IsTrue(engine.TryActivate("unit-1", "chance", SkillTriggerKind.OnHit, 1));
            Assert.IsFalse(engine.TryActivate("unit-1", "chance", SkillTriggerKind.OnHit, 1));
            Assert.IsFalse(engine.TryActivate("unit-1", "chance", SkillTriggerKind.OnHit, 2));
            Assert.AreEqual(2, random.Calls);
        }

        [Test]
        public void CooldownsAreTrackedPerUnitInstance()
        {
            var random = new SequenceRandom();
            var engine = new CombatSkillTriggerEngine(random);
            var profile = Unit(Skill("cooldown", SkillTriggerKind.OnAttack, 1000000, 2));
            engine.RegisterUnit("first", profile);
            engine.RegisterUnit("second", profile);
            Assert.IsTrue(engine.TryActivate("first", "cooldown", SkillTriggerKind.OnAttack, 1));
            Assert.IsFalse(engine.TryActivate("first", "cooldown", SkillTriggerKind.OnAttack, 2));
            Assert.IsTrue(engine.TryActivate("second", "cooldown", SkillTriggerKind.OnAttack, 1));
            engine.Advance(2);
            Assert.IsTrue(engine.TryActivate("first", "cooldown", SkillTriggerKind.OnAttack, 3));
            Assert.AreEqual(0, random.Calls);
        }

        [Test]
        public void FailedProcDoesNotConsumeEventCooldown()
        {
            var random = new SequenceRandom(900000, 100);
            var engine = new CombatSkillTriggerEngine(random);
            engine.RegisterUnit("unit", Unit(Skill("chance", SkillTriggerKind.OnHit, 500000, 3)));
            Assert.IsFalse(engine.TryActivate("unit", "chance", SkillTriggerKind.OnHit, 1));
            Assert.IsTrue(engine.TryActivate("unit", "chance", SkillTriggerKind.OnHit, 2));
            Assert.IsFalse(engine.TryActivate("unit", "chance", SkillTriggerKind.OnHit, 3));
        }

        [Test]
        public void PeriodicSkillWaitsUntilItsFirstInterval()
        {
            var engine = new CombatSkillTriggerEngine(new SequenceRandom());
            engine.RegisterUnit("unit", Unit(Skill("pulse", SkillTriggerKind.Periodic, 1000000, 0, 2)));
            Assert.IsFalse(engine.TryActivate("unit", "pulse", SkillTriggerKind.Periodic, 1));
            engine.Advance(2);
            Assert.IsTrue(engine.TryActivate("unit", "pulse", SkillTriggerKind.Periodic, 2));
            Assert.IsFalse(engine.TryActivate("unit", "pulse", SkillTriggerKind.Periodic, 3));
            engine.Advance(2);
            Assert.IsTrue(engine.TryActivate("unit", "pulse", SkillTriggerKind.Periodic, 4));
        }

        [Test]
        public void PassiveAuraCannotRollAsAnActiveSkill()
        {
            var engine = new CombatSkillTriggerEngine(new SequenceRandom());
            engine.RegisterUnit("unit", Unit(Skill("aura", SkillTriggerKind.PassiveAura, 1000000)));
            Assert.IsFalse(engine.TryActivate("unit", "aura", SkillTriggerKind.PassiveAura, 1));
        }

        [Test]
        public void InvalidRandomValueDoesNotActivateSkill()
        {
            var engine = new CombatSkillTriggerEngine(new SequenceRandom(1000000));
            engine.RegisterUnit("unit", Unit(Skill("chance", SkillTriggerKind.OnHit, 500000)));
            Assert.Throws<InvalidOperationException>(() =>
                engine.TryActivate("unit", "chance", SkillTriggerKind.OnHit, 1));
        }

        [Test]
        public void RemovingAndReaddingSameUnitStartsFreshCooldown()
        {
            var engine = new CombatSkillTriggerEngine(new SequenceRandom());
            var spec = Unit(Skill("guaranteed", SkillTriggerKind.OnAttack, 1000000, 10));
            engine.RegisterUnit("unit", spec);
            Assert.IsTrue(engine.TryActivate("unit", "guaranteed", SkillTriggerKind.OnAttack, 1));
            engine.RemoveUnit("unit");
            engine.RegisterUnit("unit", spec);
            Assert.IsTrue(engine.TryActivate("unit", "guaranteed", SkillTriggerKind.OnAttack, 1));
        }
    }
}
