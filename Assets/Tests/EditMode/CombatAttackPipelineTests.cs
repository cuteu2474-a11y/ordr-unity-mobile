using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public class CombatAttackPipelineTests
    {
        private sealed class ZeroRandom : ICombatProcRandom
        {
            public int Next(int max) { return 0; }
        }

        // 피해 공식 검증 전에는 외부 계산기를 반드시 주입한다.
        // 테스트 구현은 '공격력 표시값 그대로'이며 원본 피해 공식이 아니다.
        private sealed class TestOnlyDamageResolver : IOriginalDamageResolver
        {
            public BasicDamageRequest LastRequest;
            public long? OverrideDamage;
            public long ResolveBasicDamage(BasicDamageRequest request, CombatEnemy target)
            {
                LastRequest = request;
                return OverrideDamage ?? request.NominalAttackPower;
            }
        }

        private static CombatSkillSpec Skill(string id, SkillTriggerKind trigger)
        {
            return new CombatSkillSpec(id, trigger, 1000000, 0, 0,
                SkillTargetArea.Single, 0,
                new[] { new SkillEffectSpec(SkillEffectKind.Damage, 5, 0,
                    DamageSchool.Magical, DamageTraits.Explosive) }, "TEST_FIXTURE");
        }

        private static UnitCombatSpec Unit()
        {
            return new UnitCombatSpec("test-rawcode",
                new BasicAttackSpec(40, 1, 10, DamageSchool.Physical, DamageTraits.None),
                new [] {
                    Skill("on-attack", SkillTriggerKind.OnAttack),
                    Skill("on-hit", SkillTriggerKind.OnHit)
                }, "TEST_FIXTURE");
        }

        private sealed class Context
        {
            public GameSession Game;
            public CombatScheduler Scheduler;
            public CombatResolution Resolution;
            public CombatSkillTriggerEngine Triggers;
            public TestOnlyDamageResolver Formula;
            public CombatAttackPipeline Pipeline;
        }

        private static Context Setup()
        {
            var context = new Context();
            context.Game = new GameSession();
            context.Game.Start();
            context.Game.BeginWave(1);
            context.Scheduler = new CombatScheduler();
            context.Resolution = new CombatResolution(context.Game, context.Scheduler);
            context.Triggers = new CombatSkillTriggerEngine(new ZeroRandom());
            context.Formula = new TestOnlyDamageResolver();
            context.Pipeline = new CombatAttackPipeline(context.Scheduler, context.Resolution,
                context.Triggers, context.Formula);
            return context;
        }

        private static TargetCandidate Target(string id, double distance, bool alive = true)
        {
            return new TargetCandidate(id, distance * distance, 0, alive);
        }

        [Test]
        public void AttackDeclaredCallbackExecutesBeforeBasicHitFormula()
        {
            var c = Setup();
            c.Resolution.Spawn(new CombatEnemy("enemy", 100));
            c.Pipeline.RegisterUnit("unit-1", Unit(), 0);
            int callbacks = 0;
            c.Pipeline.ConfigureVerifiedAttackDeclared((unitId, enemyId) =>
            {
                Assert.AreEqual("unit-1",unitId);
                Assert.AreEqual("enemy",enemyId);
                callbacks++;
                // The formula sees this change only if declaration precedes damage.
                c.Formula.OverrideDamage = 7;
            });
            BasicAttackResult hit;
            Assert.IsTrue(c.Pipeline.TryBasicAttack("unit-1",
                new[] { Target("enemy", 1) }, out hit));
            Assert.AreEqual(7L,hit.ActualDamage);
            Assert.AreEqual(1,callbacks);
            Assert.IsFalse(c.Pipeline.TryBasicAttack("unit-1",
                new[] { Target("enemy", 1) }, out hit));
            Assert.AreEqual(1,callbacks);
        }

        [Test]
        public void AttackDeclaredCanOccurBeforeFormulaFailureWithoutConsumingCooldown()
        {
            var c = Setup();
            c.Resolution.Spawn(new CombatEnemy("enemy", 100));
            c.Pipeline.RegisterUnit("unit-1", Unit(), 0);
            int declarations = 0;
            c.Pipeline.ConfigureVerifiedAttackDeclared((unitId,targetId) =>
                declarations++);
            c.Formula.OverrideDamage = -1;
            Assert.Throws<InvalidOperationException>(() =>
            {
                BasicAttackResult hit;
                c.Pipeline.TryBasicAttack("unit-1",
                    new[] { Target("enemy", 1) }, out hit);
            });
            Assert.AreEqual(1,declarations);
            Assert.AreEqual(100,c.Resolution.GetEnemy("enemy").Health);
            Assert.IsTrue(c.Scheduler.IsReady("unit-1"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeclaredSkillKillConsumesOneAttackWithoutRollingBasicHit(bool scriptCallback)
        {
            var c = Setup();
            c.Resolution.Spawn(new CombatEnemy("enemy", 100));
            c.Resolution.Spawn(new CombatEnemy("next", 200));
            c.Pipeline.RegisterUnit("unit-1", Unit(), 0);
            int declarations = 0, deaths = 0;
            c.Resolution.EnemyExited += (id, reason) => deaths++;
            Action<string, string> kill = (actor, target) => {
                declarations++;
                long actual;
                Assert.IsTrue(c.Resolution.TryApplySkillDamage(actor, target, 100, out actual));
            };
            if (scriptCallback)
                c.Pipeline.ConfigureOriginalScriptAttackDeclared((actor, raw, target) => kill(actor, target));
            else c.Pipeline.ConfigureVerifiedAttackDeclared(kill);
            BasicAttackResult hit;
            Assert.IsTrue(c.Pipeline.TryBasicAttack("unit-1",
                new[] { Target("enemy", 1), Target("next", 2) }, out hit));
            Assert.IsFalse(hit.BasicHitApplied);
            Assert.AreEqual(0, hit.ActualDamage);
            Assert.IsEmpty(hit.ActivatedSkillRawCodes);
            Assert.IsEmpty(hit.NativePassiveActivations);
            Assert.IsNull(c.Formula.LastRequest, "Dead target must not consume damage/proc RNG");
            Assert.AreEqual(1, deaths);
            Assert.AreEqual(1, declarations);
            Assert.AreEqual(1, c.Scheduler.NextReadyAt("unit-1"));
            Assert.IsFalse(c.Pipeline.TryBasicAttack("unit-1", new[] { Target("next", 2) }, out hit));
            Assert.AreEqual(1, declarations);
            c.Pipeline.Advance(1);
            Assert.IsTrue(c.Pipeline.TryBasicAttack("unit-1", new[] { Target("next", 2) }, out hit));
            Assert.IsTrue(hit.BasicHitApplied);
            Assert.AreEqual(40, hit.ActualDamage);
            Assert.AreEqual(60, c.Resolution.GetEnemy("next").Health);
            Assert.AreEqual(1, deaths);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeclaredTargetRemovalCannotHitAReplacementWithTheSameId(bool replace)
        {
            var c = Setup();
            c.Resolution.Spawn(new CombatEnemy("enemy", 100));
            c.Pipeline.RegisterUnit("unit-1", Unit(), 0);
            c.Pipeline.ConfigureVerifiedAttackDeclared((actor, target) => {
                Assert.IsTrue(c.Resolution.TryLeak(target));
                if (replace) c.Resolution.Spawn(new CombatEnemy(target, 200));
            });
            BasicAttackResult hit;
            Assert.IsTrue(c.Pipeline.TryBasicAttack("unit-1", new[] { Target("enemy", 1) }, out hit));
            Assert.IsFalse(hit.BasicHitApplied);
            Assert.IsNull(c.Formula.LastRequest);
            Assert.IsFalse(c.Scheduler.IsReady("unit-1"));
            if (replace) Assert.AreEqual(200, c.Resolution.GetEnemy("enemy").Health);
            else Assert.IsNull(c.Resolution.GetEnemy("enemy"));
        }

        [Test]
        public void BasicHitUsesInjectedFormulaAndTriggersBothSkills()
        {
            var c = Setup();
            c.Resolution.Spawn(new CombatEnemy("far", 200));
            c.Resolution.Spawn(new CombatEnemy("near", 100));
            c.Pipeline.RegisterUnit("unit-1", Unit(), 0);
            BasicAttackResult result;
            Assert.IsTrue(c.Pipeline.TryBasicAttack("unit-1",
                new [] { Target("far", 8), Target("near", 2) }, out result));
            Assert.AreEqual("near", result.TargetId);
            Assert.AreEqual(40, result.ActualDamage);
            Assert.AreEqual(60, c.Resolution.GetEnemy("near").Health);
            Assert.AreEqual(DamageSchool.Physical, c.Formula.LastRequest.School);
            Assert.AreEqual(DamageTraits.None, c.Formula.LastRequest.Traits);
            CollectionAssert.AreEquivalent(new [] { "on-attack", "on-hit" }, result.ActivatedSkillRawCodes);
            Assert.IsFalse(c.Scheduler.IsReady("unit-1"));
        }

        [Test]
        public void AttackDoesNotConsumeCooldownWithoutTarget()
        {
            var c = Setup();
            c.Resolution.Spawn(new CombatEnemy("enemy", 100));
            c.Pipeline.RegisterUnit("unit-1", Unit(), 0);
            BasicAttackResult result;
            Assert.IsFalse(c.Pipeline.TryBasicAttack("unit-1",
                new [] { Target("enemy", 100) }, out result));
            Assert.IsNull(result);
            Assert.IsTrue(c.Scheduler.IsReady("unit-1"));
            Assert.AreEqual(100, c.Resolution.GetEnemy("enemy").Health);
        }

        [Test]
        public void AttackCooldownBlocksRepeatAndCanAdvance()
        {
            var c = Setup();
            c.Resolution.Spawn(new CombatEnemy("enemy", 100));
            c.Pipeline.RegisterUnit("unit-1", Unit(), 0);
            BasicAttackResult result;
            Assert.IsTrue(c.Pipeline.TryBasicAttack("unit-1",
                new[] { Target("enemy", 1) }, out result));
            Assert.IsFalse(c.Pipeline.TryBasicAttack("unit-1",
                new[] { Target("enemy", 1) }, out result));
            c.Pipeline.Advance(1);
            Assert.AreEqual(1, c.Triggers.Time);
            Assert.AreEqual(1, c.Scheduler.Time);
            Assert.IsTrue(c.Pipeline.TryBasicAttack("unit-1",
                new[] { Target("enemy", 1) }, out result));
            Assert.AreEqual(20, c.Resolution.GetEnemy("enemy").Health);
        }

        [Test]
        public void ZeroDamageDoesNotTriggerOnHitSkill()
        {
            var c = Setup();
            c.Formula.OverrideDamage = 0;
            c.Resolution.Spawn(new CombatEnemy("enemy", 100));
            c.Pipeline.RegisterUnit("unit-1", Unit(), 0);
            BasicAttackResult result;
            Assert.IsTrue(c.Pipeline.TryBasicAttack("unit-1",
                new[] { Target("enemy", 1) }, out result));
            Assert.AreEqual(0, result.ActualDamage);
            CollectionAssert.AreEqual(new[] { "on-attack" }, result.ActivatedSkillRawCodes);
            Assert.AreEqual(100, c.Resolution.GetEnemy("enemy").Health);
        }

        [Test]
        public void LethalDamageRemovesEnemyAndCountsOneKill()
        {
            var c = Setup();
            c.Resolution.Spawn(new CombatEnemy("enemy", 20));
            c.Pipeline.RegisterUnit("unit-1", Unit(), 0);
            BasicAttackResult result;
            Assert.IsTrue(c.Pipeline.TryBasicAttack("unit-1",
                new [] { Target("enemy", 1) }, out result));
            Assert.AreEqual(20, result.ActualDamage);
            Assert.AreEqual(1, c.Game.DefeatedEnemies);
            Assert.AreEqual(0, c.Game.AliveEnemies);
            Assert.IsNull(c.Resolution.GetEnemy("enemy"));
        }

        [Test]
        public void InvalidFinalDamageLeavesTargetAndCooldownUnchanged()
        {
            var c = Setup();
            c.Formula.OverrideDamage = -1;
            c.Resolution.Spawn(new CombatEnemy("enemy", 100));
            c.Pipeline.RegisterUnit("unit-1", Unit(), 0);
            Assert.Throws<InvalidOperationException>(() =>
            {
                BasicAttackResult unused;
                c.Pipeline.TryBasicAttack("unit-1",
                    new[] { Target("enemy", 1) }, out unused);
            });
            Assert.AreEqual(100, c.Resolution.GetEnemy("enemy").Health);
            Assert.IsTrue(c.Scheduler.IsReady("unit-1"));
        }

        [Test]
        public void StaleTargetDoesNotHideARealAliveEnemy()
        {
            var c = Setup();
            c.Resolution.Spawn(new CombatEnemy("alive", 100));
            c.Pipeline.RegisterUnit("unit-1", Unit(), 0);
            BasicAttackResult result;
            Assert.IsTrue(c.Pipeline.TryBasicAttack("unit-1",
                new[] { Target("already-gone", 1), Target("alive", 2) }, out result));
            Assert.AreEqual("alive", result.TargetId);
            Assert.AreEqual(60, c.Resolution.GetEnemy("alive").Health);
        }

        [Test]
        public void UnitRemovalDisablesFurtherAttacksAndSkillState()
        {
            var c = Setup();
            c.Pipeline.RegisterUnit("unit-1", Unit(), 0);
            c.Pipeline.RemoveUnit("unit-1");
            Assert.IsFalse(c.Triggers.HasUnit("unit-1"));
            Assert.Throws<InvalidOperationException>(() =>
            {
                BasicAttackResult unused;
                c.Pipeline.TryBasicAttack("unit-1",
                    new[] { Target("enemy", 1) }, out unused);
            });
        }
    }
}
