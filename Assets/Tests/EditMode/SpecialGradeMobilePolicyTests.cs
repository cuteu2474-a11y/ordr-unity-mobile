using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class SpecialGradeMobilePolicyTests
    {
        private sealed class FixtureDamage : IOriginalDamageResolver,
            IVerifiedCharacterSkillDamageResolver
        {
            public long ResolveBasicDamage(BasicDamageRequest r, CombatEnemy e)
            { return r.NominalAttackPower; }
            public long ResolveDamage(CharacterSkillDamageRequest r) { return 5; }
        }

        private static CharacterCombatRuntime World(string skillJson = null)
        {
            var units = CharacterUnitCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[" +
                "{\"id\":\"S-09\",\"grade\":\"특별함\",\"name\":\"모리아\",\"roleTags\":[]}," +
                "{\"id\":\"S-02\",\"grade\":\"특별함\",\"name\":\"갱 뱃지\",\"roleTags\":[]}," +
                "{\"id\":\"S-05\",\"grade\":\"특별함\",\"name\":\"로우\",\"roleTags\":[]}," +
                "{\"id\":\"S-08\",\"grade\":\"특별함\",\"name\":\"마르코\",\"roleTags\":[]}," +
                "{\"id\":\"S-32\",\"grade\":\"특별함\",\"name\":\"도킹\",\"roleTags\":[]}," +
                "{\"id\":\"TEST-01\",\"grade\":\"흔함\",\"name\":\"가상 유닛\",\"roleTags\":[]}]}" );
            var profiles = CharacterSkillCatalog.FromJson(skillJson ??
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var damage = new FixtureDamage();
            var world = new CharacterCombatRuntime(units, profiles, damage, damage,
                new SeededNonOriginalProcRandom(1));
            world.Start(); world.BeginWave(1);
            return world;
        }

        private sealed class FixtureStory : IOriginalStoryDispatchPort
        {
            public readonly HashSet<long> Deployed = new HashSet<long>();
            public bool CanDispatch(long id) { return true; }
            public ManualActionResult Dispatch(long id)
            { Deployed.Add(id); return new ManualActionResult(ManualActionState.Succeeded, "TEST_ONLY"); }
            public bool IsDeployed(long id) { return Deployed.Contains(id); }
            public bool CanRecall(long id) { return true; }
            public ManualActionResult Recall(long id)
            { Deployed.Remove(id); return new ManualActionResult(ManualActionState.Succeeded, "TEST_ONLY"); }
        }

        private sealed class FixtureLaw : ISpecialGradeLawCastService
        {
            public readonly Dictionary<long, double> Scores = new Dictionary<long, double>();
            public long LastTarget;
            public int Casts;
            public bool Accept = true;
            public bool CanCastNow(long id, double time) { return true; }
            public bool TryEvaluateTarget(long caster, long target, double time, out double dps)
            { return Scores.TryGetValue(target, out dps); }
            public bool TryCast(long caster, long target, double time)
            { if (!Accept) return false; LastTarget = target; Casts++; return true; }
        }

        [Test]
        public void LawReevaluatesDpsAfterCooldownAndNeverBuffsOtherBattlefield()
        {
            var world = World();
            long law = world.SpawnCharacter("S-05", 0, 0);
            long first = world.SpawnCharacter("TEST-01", 0, 0);
            long second = world.SpawnCharacter("TEST-01", 0, 0);
            long storyUnit = world.SpawnCharacter("TEST-01", 0, 0);
            var story = new FixtureStory(); story.Deployed.Add(storyUnit);
            world.ConfigureStoryDeployment(story);
            var service = new FixtureLaw();
            service.Scores[first] = 200; service.Scores[second] = 100;
            service.Scores[storyUnit] = 9999;
            world.ConfigureSpecialGradeLawCasting(service);
            Assert.IsTrue(world.TryRunLawAcceleration(law));
            Assert.AreEqual(first, service.LastTarget);
            service.Scores[second] = 300;
            Assert.IsFalse(world.TryRunLawAcceleration(law));
            world.Step(50);
            Assert.AreEqual(second, service.LastTarget);
            Assert.AreEqual(2, service.Casts);
            story.Deployed.Add(law);
            world.Step(50);
            Assert.AreEqual(storyUnit, service.LastTarget);
        }

        [Test]
        public void LawRejectedCastDoesNotConsumeCooldownAndRemovedTargetIsNotReused()
        {
            var world = World();
            long law = world.SpawnCharacter("S-05", 0, 0);
            long target = world.SpawnCharacter("TEST-01", 0, 0);
            var service = new FixtureLaw(); service.Scores[target] = 100; service.Accept = false;
            world.ConfigureSpecialGradeLawCasting(service);
            Assert.IsFalse(world.TryRunLawAcceleration(law));
            service.Accept = true;
            Assert.IsTrue(world.TryRunLawAcceleration(law));
            world.RemoveCharacter(target);
            world.Step(50);
            Assert.AreEqual(1, service.Casts);
        }

        [Test]
        public void LawSelectionRejectsUnknownDpsAndAccelerationBlockedTarget()
        {
            Assert.AreEqual(4, SpecialGradeMobilePolicy.ChooseLawTarget(false, new[] {
                new LawDpsCandidate(1, true, true, 999),
                new LawDpsCandidate(2, false, false, 999),
                new LawDpsCandidate(3, false, true, null),
                new LawDpsCandidate(4, false, true, 10)
            }));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new LawDpsCandidate(5, false, true, double.NaN));
        }

        [Test]
        public void MoriaParticipationAwardsOneMaterialWhenAnotherUnitGetsLastHit()
        {
            var world = World();
            long moria = world.SpawnCharacter("S-09", 0, 0);
            long killer = world.SpawnCharacter("TEST-01", 0, 0);
            world.BindVerifiedBasicAttack(moria, new BasicAttackSpec(10, 1,
                DamageSchool.Physical, DamageTraits.None), 0);
            world.BindVerifiedBasicAttack(killer, new BasicAttackSpec(10, 1,
                DamageSchool.Physical, DamageTraits.None), 0);
            world.SpawnEnemy("line", 20, CharacterEnemyKind.Line, 0, 0, 0);
            world.Step(0);
            Assert.AreEqual(1, world.MoriaZombieMaterials);
            Assert.AreEqual(1, world.Game.DefeatedEnemies);
            world.Step(1);
            Assert.AreEqual(1, world.MoriaZombieMaterials);
        }

        [Test]
        public void MoriaZeroDamageBossAndLeakDoNotProduceZombie()
        {
            var ledger = new MoriaParticipationLedger();
            ledger.Record("zero", true, 0, CharacterEnemyKind.Line);
            ledger.Record("boss", true, 10, CharacterEnemyKind.Boss);
            ledger.Record("leak", true, 10, CharacterEnemyKind.Line);
            ledger.Forget("leak");
            Assert.IsFalse(ledger.Complete("zero"));
            Assert.IsFalse(ledger.Complete("boss"));
            Assert.IsFalse(ledger.Complete("leak"));
            ledger.Record("line", true, 1, CharacterEnemyKind.Line);
            ledger.Record("line", true, 3, CharacterEnemyKind.Line);
            Assert.IsTrue(ledger.Complete("line"));
            Assert.IsFalse(ledger.Complete("line"));
        }

        [Test]
        public void SkillDamageReportsItsCasterToParticipationObserver()
        {
            var session = new Ordr.Mobile.Core.GameSession(); session.Start(); session.BeginWave(1);
            var resolution = new CombatResolution(session, new CombatScheduler());
            string observed = null; bool killed = false;
            resolution.DamageApplied += (actor, enemy, damage, dead) =>
                { observed = actor; killed = dead; };
            resolution.Spawn(new CombatEnemy("e", 5));
            long actual;
            Assert.IsTrue(resolution.TryApplySkillDamage("moria", "e", 10, out actual));
            Assert.AreEqual("moria", observed);
            Assert.AreEqual(5, actual);
            Assert.IsTrue(killed);
        }

        private static string AreaProfile(string unitId)
        {
            return "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[{" +
                "\"unitId\":\"" + unitId + "\",\"sourceUrl\":\"TEST_ONLY\"," +
                "\"skillSpecCompleteness\":\"partial\",\"basicAttack\":{},\"passives\":[],\"skills\":[{" +
                "\"name\":\"가상 범위 기술\",\"trigger\":\"manual\",\"manaCost\":0," +
                "\"target\":\"casterArea\",\"radius\":10,\"components\":[{\"type\":\"magical\",\"amount\":5}]}]}]}";
        }

        [TestCase("S-08", 100, 95)]
        [TestCase("TEST-01", 95, 100)]
        public void MarcoOnlyMovesAreaCenterToAttackedEnemy(string unit, long casterHp, long targetHp)
        {
            var world = World(AreaProfile(unit));
            long actor = world.SpawnCharacter(unit, 0, 0);
            world.ConfigureCharacterResources(actor, new CharacterResources(100, 100, 0, 0));
            world.SpawnEnemy("near-caster", 100, CharacterEnemyKind.Line, 0, 0, 0);
            world.SpawnEnemy("primary", 100, CharacterEnemyKind.Line, 100, 0, 0);
            world.SpawnEnemy("near-primary", 100, CharacterEnemyKind.Line, 105, 0, 0);
            world.SpawnEnemy("other-lane", 100, CharacterEnemyKind.Story, 105, 0, 0);
            Assert.AreEqual(AbilityCastResult.Cast,
                world.TryCastActiveSkill(actor, unit + ":skill:0", "primary"));
            Assert.AreEqual(casterHp, world.EnemyOf("near-caster").Health);
            Assert.AreEqual(targetHp, world.EnemyOf("near-primary").Health);
            Assert.AreEqual(100, world.EnemyOf("other-lane").Health);
        }

        [Test]
        public void ManualDecompositionAndAirEligibilityRemainSeparateFromAutoCombat()
        {
            Assert.IsTrue(SpecialGradeMobilePolicy.IsManualOnly("S-02"));
            Assert.IsFalse(SpecialGradeMobilePolicy.IsManualOnly("S-05"));
            Assert.IsTrue(SpecialGradeMobilePolicy.UsesCurrentAttackTarget("S-32"));
            Assert.IsFalse(SpecialGradeMobilePolicy.CanEnterSideBattle(true, false, true));
            Assert.IsTrue(SpecialGradeMobilePolicy.CanEnterSideBattle(true, true, true));
            Assert.IsFalse(SpecialGradeMobilePolicy.CanEnterSideBattle(true, true, false));
        }

        private sealed class LeadingRules : IOriginalCombatTargetSelectionRules
        {
            public bool TryChoose(CatalogUnit unit, IReadOnlyList<VerifiedTargetContact> candidates,
                double range, out string target)
            {
                target = null; double progress = double.MinValue;
                foreach (var c in candidates)
                    if (c.Candidate.PathProgress > progress)
                    { progress = c.Candidate.PathProgress; target = c.Candidate.Id; }
                return target != null;
            }
        }

        private sealed class ApproveVisible : IOriginalAutoSkillRules
        {
            public int Calls;
            public bool TryApproveCast(CatalogUnit unit, ImportedCombatSkill skill,
                Ordr.Mobile.Core.UnitState position, IReadOnlyList<MobileEnemySnapshot> enemies,
                double time, out string target)
            {
                Calls++; target = enemies.Count == 0 ? null : enemies[0].Id;
                return target != null;
            }
        }

        private static string StunProfile(string id, string trigger)
        {
            return "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[{" +
                "\"unitId\":\"" + id + "\",\"sourceUrl\":\"TEST_ONLY\"," +
                "\"skillSpecCompleteness\":\"partial\",\"basicAttack\":{},\"passives\":[],\"skills\":[{" +
                "\"name\":\"가상 스턴\",\"trigger\":\"" + trigger + "\",\"manaCost\":0," +
                "\"target\":\"single\",\"cooldownSeconds\":25," +
                "\"components\":[{\"type\":\"stun\",\"durationSeconds\":2}]}]}]}";
        }

        [Test]
        public void DockingAutoCastUsesSameLeadingTargetAsBasicAttack()
        {
            var world = World(StunProfile("S-32", "onSkillUse"));
            long unit = world.SpawnCharacter("S-32", 0, 0);
            world.ConfigureCharacterResources(unit, new CharacterResources(100, 100, 0, 0));
            world.BindVerifiedBasicAttack(unit, new BasicAttackSpec(10, 1,
                DamageSchool.Physical, DamageTraits.None), 0);
            world.ConfigureVerifiedTargetSelection(new LeadingRules());
            world.MobileAutomation.Configure(null, null, new ApproveVisible());
            world.SpawnEnemy("a-near", 100, CharacterEnemyKind.Line, 0, 0, 0);
            world.SpawnEnemy("z-front", 100, CharacterEnemyKind.Line, 100, 0, 0.9);
            world.SpawnEnemy("story", 100, CharacterEnemyKind.Story, 0, 0, 1);
            world.Step(0);
            Assert.IsTrue(world.EnemyOf("z-front").Status.IsStunned());
            Assert.IsFalse(world.EnemyOf("a-near").Status.IsStunned());
            Assert.IsFalse(world.EnemyOf("story").Status.IsStunned());
            Assert.AreEqual(90, world.EnemyOf("z-front").Health);
        }

        [Test]
        public void CaponeNeverEntersGenericAutomaticQwrLoop()
        {
            var world = World(StunProfile("S-02", "manual"));
            long unit = world.SpawnCharacter("S-02", 0, 0);
            world.ConfigureCharacterResources(unit, new CharacterResources(100, 100, 0, 0));
            var rules = new ApproveVisible();
            world.MobileAutomation.Configure(null, null, rules);
            world.SpawnEnemy("line", 100, CharacterEnemyKind.Line, 0, 0, 0);
            world.Step(0);
            Assert.AreEqual(0, rules.Calls);
            Assert.IsFalse(world.EnemyOf("line").Status.IsStunned());
        }
    }
}
