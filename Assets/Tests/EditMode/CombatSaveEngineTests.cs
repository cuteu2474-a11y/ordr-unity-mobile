using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public sealed class CombatSaveEngineTests
    {
        private sealed class ZeroRandom : ICombatProcRandom
        {
            public int Next(int maximum) => 0;
        }

        private sealed class FixedDamage : IOriginalDamageResolver
        {
            public long ResolveBasicDamage(BasicDamageRequest request, CombatEnemy enemy)
                => 10;
        }

        private sealed class AttackWorld
        {
            public CombatScheduler Clock = new CombatScheduler();
            public CombatSkillTriggerEngine Triggers =
                new CombatSkillTriggerEngine(new ZeroRandom());
            public CombatAttackPipeline Pipeline;
            public CombatResolution Resolution;

            public AttackWorld()
            {
                var game = new GameSession();
                game.Start();
                game.BeginWave(1);
                Resolution = new CombatResolution(game, Clock);
                Pipeline = new CombatAttackPipeline(Clock, Resolution,
                    Triggers, new FixedDamage());
                Pipeline.RegisterUnit("unit-2",
                    new UnitCombatSpec("TEST_ONLY_UNIT",
                        new BasicAttackSpec(10, 2, 6, DamageSchool.Physical,
                            DamageTraits.None),
                        new [] { new CombatSkillSpec("test-proc",
                            SkillTriggerKind.OnAttack, 1000000, 2, 0,
                            SkillTargetArea.Single, 0,
                            new [] { new SkillEffectSpec(
                                SkillEffectKind.Damage, 10, 0,
                                DamageSchool.Magical, DamageTraits.None) },
                            "TEST_FIXTURE") }, "TEST_FIXTURE"), 0);
                Resolution.Spawn(new CombatEnemy("enemy-1", 100));
            }
        }

        [Test]
        public void RestoresAttackCooldownSkillSequenceWithoutRerolling()
        {
            var a = new AttackWorld();
            BasicAttackResult hit;
            Assert.IsTrue(a.Pipeline.TryBasicAttack("unit-2", new [] {
                new TargetCandidate("enemy-1", 1, 0, true)
            }, out hit));
            a.Pipeline.Advance(0.75);
            var captured = a.Pipeline.CaptureState();
            Assert.AreEqual(0.75, captured.Scheduler.Time);
            Assert.AreEqual(2, captured.Scheduler.Attackers[0].DueAt);
            Assert.AreEqual(1, captured.Events[0].Sequence);
            Assert.AreEqual(1, captured.Skills.Cooldowns[0].LastEventSequence);

            var b = new AttackWorld();
            b.Pipeline.RestoreState(captured);
            Assert.AreEqual(0.75, b.Clock.Time);
            Assert.AreEqual(2, b.Pipeline.NextAttackAt("unit-2"));
            Assert.IsFalse(b.Clock.IsReady("unit-2"));
            Assert.IsFalse(b.Pipeline.TryBasicAttack("unit-2", new [] {
                new TargetCandidate("enemy-1", 1, 0, true)
            }, out hit));
            b.Pipeline.Advance(1.25);
            Assert.IsTrue(b.Pipeline.TryBasicAttack("unit-2", new [] {
                new TargetCandidate("enemy-1", 1, 0, true)
            }, out hit));
            Assert.AreEqual(2, b.Pipeline.CaptureState().Events[0].Sequence);
        }

        [Test]
        public void BrokenAttackSkillStateCannotPartiallyChangeLiveClock()
        {
            var world = new AttackWorld();
            var saved = world.Pipeline.CaptureState();
            saved.Skills.Cooldowns[0].SkillId = "missing-skill";
            Assert.Throws<FormatException>(() => world.Pipeline.RestoreState(saved));
            Assert.AreEqual(0, world.Clock.Time);
            Assert.AreEqual(0, world.Pipeline.NextAttackAt("unit-2"));
            Assert.AreEqual(0, world.Pipeline.CaptureState().Events[0].Sequence);
        }

        [Test]
        public void QueueRestoresNamedHitWithExactDueTimeAndOrder()
        {
            var original = new CombatTimedEffectQueue();
            var executed = new List<string>();
            original.ScheduleRestorable("unit-1", "enemy-1", 1.5,
                "TEST_ONLY_HIT", "one", () => executed.Add("one"));
            original.ScheduleRestorable("unit-1", "enemy-1", 1.5,
                "TEST_ONLY_HIT", "two", () => executed.Add("two"));
            original.Advance(0.5, _ => {});
            var state = original.CaptureState();
            var restored = new CombatTimedEffectQueue();
            restored.RestoreState(state, (kind, payload, caster, target) =>
                kind == "TEST_ONLY_HIT" ? (Action)(() => executed.Add(payload)) : null);
            restored.Advance(0.9, _ => {});
            Assert.IsEmpty(executed);
            restored.Advance(0.1, _ => {});
            CollectionAssert.AreEqual(new [] { "one", "two" }, executed);
            Assert.AreEqual(1.5, restored.Time, 1e-8);
            Assert.AreEqual(0, restored.PendingCount);
        }

        [Test]
        public void LegacyUnidentifiedLambdaCannotBeSilentlyDiscardedOnSave()
        {
            var queue = new CombatTimedEffectQueue();
            queue.Schedule("unit-1", "enemy-1", 0.5, () => {});
            Assert.Throws<InvalidOperationException>(() => queue.CaptureState());
            var other = new CombatTimedEffectQueue();
            other.ScheduleRestorable("unit-1", "enemy-1", 1,
                "TEST_ONLY_HIT", "safe", () => {});
            var state = other.CaptureState();
            state.Pending[0].Kind = "missing-handler";
            Assert.Throws<FormatException>(() => new CombatTimedEffectQueue()
                .RestoreState(state, (kind, payload, caster, target) => null));
        }

        [Test]
        public void ActiveSkillResourcePoolAndClockRestoreWithoutRefilling()
        {
            var source = new CharacterActiveAbilities(null);
            source.ConfigureResources(new CharacterResources(100, 60, 50, 13, true));
            source.Advance(4.25);
            var saved = source.CaptureState();
            var restored = new CharacterActiveAbilities(null);
            restored.RestoreState(saved);
            Assert.AreEqual(4.25, restored.Time);
            Assert.AreEqual(60, restored.Resources.Health);
            Assert.AreEqual(13, restored.Resources.Mana);
            Assert.AreEqual(100, restored.Resources.MaximumHealth);
            Assert.AreEqual(50, restored.Resources.MaximumMana);
            Assert.IsTrue(restored.Resources.AllowsZeroHealth);
            Assert.Throws<InvalidOperationException>(() =>
                restored.RestoreState(saved));
            saved.Resources.Mana = 500;
            Assert.Throws<FormatException>(() =>
                new CharacterActiveAbilities(null).RestoreState(saved));
        }

        [Test]
        public void AttackClockRejectsDuplicateIdWithoutModifyingExistingWorld()
        {
            var scheduler = new CombatScheduler();
            scheduler.Register("TEST_ONLY_A", 1);
            var state = scheduler.CaptureState();
            state.Attackers.Add(new CombatAttackReadyState {
                UnitId = "TEST_ONLY_A", DueAt = 2
            });
            Assert.Throws<FormatException>(() => scheduler.RestoreState(state));
            Assert.AreEqual(1, scheduler.NextReadyAt("TEST_ONLY_A"));
            Assert.AreEqual(0, scheduler.Time);
        }

        [Test]
        public void StatusPreservesAuraTimedExpiryAndRevisionOnResume()
        {
            var first = new CombatStatusEngine();
            first.Apply(new CombatEffect("TEST_ONLY_SPEED",
                CombatEffectType.AttackSpeedBuff, CombatEffectOrigin.Aura, 20, 0));
            first.Apply(new CombatEffect("TEST_ONLY_STUN",
                CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill, 1, 2));
            first.Advance(0.75);
            var state = first.CaptureState();
            var restored = CombatStatusEngine.Restore(state);
            Assert.AreEqual(first.Revision, restored.Revision);
            Assert.AreEqual(2, restored.ActiveCount);
            restored.Advance(1.2);
            Assert.IsTrue(restored.IsStunned());
            restored.Advance(0.1);
            Assert.IsFalse(restored.IsStunned());
            Assert.AreEqual(1, restored.ActiveCount);
            Assert.AreEqual(2, first.ActiveCount);

            state.Effects[1].ExpiresAt = -1;
            Assert.Throws<FormatException>(() => CombatStatusEngine.Restore(state));
        }
    }
}
