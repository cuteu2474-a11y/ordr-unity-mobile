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
    public sealed class Original323NativeSpeedRuntimeIntegrationTests
    {
        private sealed class TestOnlyFinalDamage : IOriginalDamageResolver
        {
            public int Calls;
            public long ResolveBasicDamage(BasicDamageRequest request,
                CombatEnemy target)
            {
                Calls++;
                return 10; // TEST_ONLY; never a real ORDR final HP formula.
            }
        }

        private sealed class NativeBuffTarget : IOriginal323SpecialAuraTargetRules
        {
            public bool Approved = true;
            public bool CanAffectEnemy(Original323SpecialAura aura,
                UnitState caster, SpatialCombatEnemy target,
                double time, out string evidence)
            { evidence = null; return false; }

            public bool CanAffectAlly(Original323SpecialAura aura,
                UnitState caster, UnitState target,
                double time, out string evidence)
            {
                bool can = Approved && aura.AbilityRawcode == "A0FT";
                evidence = can ? "TEST_ONLY live Warcraft attack speed buff witness" : null;
                return can;
            }
        }

        // Source-verified hook test: .5 seconds is TEST_ONLY, not a
        // calculated 10% AOae result. The code cannot invent an IAS formula.
        private sealed class Timing : IOriginal323VerifiedNativeAttackCadence
        {
            public int Calls;
            public bool Approved = true;
            public bool TryResolveNextInterval(string unit, BasicAttackSpec attack,
                IReadOnlyList<CombatEffect> active, double combatTime,
                out double seconds, out string evidence)
            {
                Calls++;
                seconds = 0.5;
                evidence = Approved
                    ? "TEST_ONLY native Warcraft AOae current-cycle timing" : null;
                return Approved;
            }
        }

        private sealed class Setup
        {
            public CharacterCombatRuntime World;
            public TestOnlyFinalDamage Damage;
            public Timing Cadence;
            public NativeBuffTarget BuffWitness;
            public long Brook;
            public long Attacker;
        }

        private static string Data(string name)
        {
            return File.ReadAllText(Path.Combine(
                Application.dataPath, "..", "data", name));
        }

        private static Setup Build(bool configureNativeTiming)
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var six = new Original323SpecialAuraCatalog(map);
            var armor = Original323ArmorAuraCatalog.FromJson(
                Data("original-2323C-all-unit-aura-audit.json"), map, six);
            var speed = Original323NativeSpeedAuraCatalog.FromJson(
                Data("original-2323C-all-unit-aura-audit.json"), map);
            var all = new Original323CompositeAuraCatalog(map, armor, speed);
            var formula = new TestOnlyFinalDamage();
            var world = new CharacterCombatRuntime(units,
                CharacterSkillCatalog.FromJson(
                    "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}"),
                formula, new RejectUnverifiedDamageFormula(),
                new SeededNonOriginalProcRandom(13));
            var witness = new NativeBuffTarget();
            var cadence = new Timing();
            world.ConfigureOriginalAuras(all, witness);
            if (configureNativeTiming)
                world.ConfigureVerifiedOriginal323NativeAttackCadence(cadence);
            world.Start();
            world.BeginWave(1);
            var brook = world.SpawnCharacter("R-18", 0, 0);
            var attacker = world.SpawnCharacter("C-01", 100, 0);
            world.BindVerifiedBasicAttack(attacker,
                new BasicAttackSpec(10, 1, 500,
                    DamageSchool.Physical, DamageTraits.None), 0);
            world.SpawnEnemy("target", 1000,
                CharacterEnemyKind.Line, 150, 0, 0);
            return new Setup {
                World = world, Damage = formula, Cadence = cadence,
                BuffWitness = witness, Brook = brook, Attacker = attacker
            };
        }

        [Test]
        public void RareBrookAuraActuallyChangesLuffyNextNativeAttackTime()
        {
            var c = Build(true);
            bool hasSpeed = false;
            foreach (var effect in c.World.UnitStatusOf(c.Attacker).Snapshot())
                if (effect.Type == CombatEffectType.AttackSpeedBuff &&
                    Math.Abs(effect.Amount - 10) < 0.00001)
                    hasSpeed = true;
            Assert.IsTrue(hasSpeed);
            c.World.Step(0);
            Assert.AreEqual(990, c.World.EnemyOf("target").Health);
            Assert.AreEqual(0.5,
                c.World.InspectBasicAttack(c.Attacker).NextAttackAtSeconds.Value, 1e-9);
            c.World.Step(0.49);
            Assert.AreEqual(990, c.World.EnemyOf("target").Health);
            c.World.Step(0.01);
            Assert.AreEqual(980, c.World.EnemyOf("target").Health);
            Assert.AreEqual(1.0,
                c.World.InspectBasicAttack(c.Attacker).NextAttackAtSeconds.Value, 1e-9);
            Assert.AreEqual(2, c.Cadence.Calls);
        }

        [Test]
        public void UnverifiedRareBrookCadenceNeverSpendsHitOrRandomBeforeApproval()
        {
            var c = Build(false);
            c.World.Step(0);
            Assert.AreEqual(1000, c.World.EnemyOf("target").Health);
            Assert.AreEqual(0, c.Damage.Calls);
            Assert.AreEqual(BasicAttackReadinessKind.FormulaDeferred,
                c.World.InspectBasicAttack(c.Attacker).Kind);
            StringAssert.Contains("원본 네이티브 공격 간격",
                c.World.InspectBasicAttack(c.Attacker).Detail);
            Assert.AreEqual(0,
                c.World.InspectBasicAttack(c.Attacker).NextAttackAtSeconds.Value, 1e-9);
        }

        [Test]
        public void RemovingOriginalBrookBuffRestoresBaseAttackInterval()
        {
            var c = Build(true);
            c.World.Step(0);
            c.BuffWitness.Approved = false;
            c.World.MoveCharacter(c.Brook, 10, 0);
            c.World.Step(0.5);
            Assert.AreEqual(980, c.World.EnemyOf("target").Health);
            Assert.AreEqual(1.5,
                c.World.InspectBasicAttack(c.Attacker).NextAttackAtSeconds.Value, 1e-9);
            Assert.AreEqual(1, c.Cadence.Calls);
        }
    }
}
