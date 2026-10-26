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
    public sealed class Original323VerifiedNativeEnemyMovementTests
    {
        // TEST_ONLY destination: *not* the original Warcraft movement/slow
        // formula. The native movement policy must supply its own evidence.
        private sealed class ObservedMovement : IOriginal323VerifiedNativeEnemyMovementStep
        {
            public bool Approved = true;
            public bool Evidence = true;
            public bool DirtyPreflight;
            public int Calls;
            public int SourceSlowCount;
            public bool TryResolveMovement(SpatialCombatEnemy enemy,
                double x, double y, double progress, double elapsed,
                IReadOnlyList<CombatEffect> effects, double time,
                out double resultX, out double resultY,
                out double resultProgress, out string source)
            {
                Calls++;
                SourceSlowCount = 0;
                foreach (var effect in effects)
                    if (effect.Type == CombatEffectType.MovementSlowPercent)
                        SourceSlowCount++;
                resultX = enemy.X + (x - enemy.X) / 2;
                resultY = enemy.Y + (y - enemy.Y) / 2;
                resultProgress = enemy.PathProgress +
                    (progress - enemy.PathProgress) / 2;
                source = Evidence ? "TEST_ONLY full native movement path witness" : null;
                if (DirtyPreflight)
                    enemy.Move(resultX, resultY, resultProgress);
                return Approved;
            }
        }

        private sealed class NativeTarget : IOriginal323SpecialAuraTargetRules
        {
            public bool Approved = true;
            public bool CanAffectEnemy(Original323SpecialAura aura,
                UnitState caster, SpatialCombatEnemy target,
                double time, out string evidence)
            {
                bool allowed = Approved && aura.AbilityRawcode == "A023";
                evidence = allowed ? "TEST_ONLY current AOae buff presence" : null;
                return allowed;
            }
            public bool CanAffectAlly(Original323SpecialAura aura,
                UnitState caster, UnitState target,
                double time, out string evidence)
            {
                evidence = null;
                return false;
            }
        }

        private sealed class Setup
        {
            public CharacterCombatRuntime World;
            public ObservedMovement Movement;
            public NativeTarget Targets;
            public long AuraCaster;
        }

        private static string Data(string file)
        {
            return File.ReadAllText(Path.Combine(
                Application.dataPath, "..", "data", file));
        }

        private static Setup Create(bool configureMovement)
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var six = new Original323SpecialAuraCatalog(map);
            var originalArmor = Original323ArmorAuraCatalog.FromJson(
                Data("original-2323C-all-unit-aura-audit.json"), map, six);
            var originalSpeed = Original323NativeSpeedAuraCatalog.FromJson(
                Data("original-2323C-all-unit-aura-audit.json"), map);
            var composite = new Original323CompositeAuraCatalog(
                map, originalArmor, originalSpeed);
            var rejected = new RejectUnverifiedDamageFormula();
            var emptyProfiles = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var game = new CharacterCombatRuntime(
                units, emptyProfiles, rejected, rejected,
                new SeededNonOriginalProcRandom(19));
            var native = new NativeTarget();
            var movement = new ObservedMovement();
            game.ConfigureOriginalAuras(composite, native);
            if (configureMovement)
                game.ConfigureVerifiedOriginal323NativeEnemyMovement(movement);
            game.Start();
            game.BeginWave(1);
            long caster = game.SpawnCharacter("R-26", 0, 0);
            game.SpawnEnemy("line", 5000, CharacterEnemyKind.Line, 100, 0, 0);
            return new Setup {
                World = game, Movement = movement, Targets = native, AuraCaster = caster
            };
        }

        private static bool HasSlow(CombatStatusEngine target)
        {
            foreach (var effect in target.Snapshot())
                if (effect.Type == CombatEffectType.MovementSlowPercent)
                    return true;
            return false;
        }

        [Test]
        public void NativeMovementActuallyMovesLiveAokijiSlowedEnemyOnWitnessedStep()
        {
            var c = Create(true);
            Assert.IsTrue(HasSlow(c.World.StatusOf("line")));
            Assert.IsTrue(c.World.TryAdvanceEnemyAlongVerifiedOriginalRoute(
                "line", 200, 0, 1, 0.25));
            Assert.AreEqual(150, c.World.EnemyOf("line").Health > 0
                ? c.World.MobileEnemySnapshot()[0].X : -1, 1e-9);
            Assert.AreEqual(1, c.Movement.Calls);
            Assert.AreEqual(1, c.Movement.SourceSlowCount);
            Assert.AreEqual(5000, c.World.EnemyOf("line").Health);
        }

        [Test]
        public void NoOriginalNativeMovementFormulaBlocksUnmodifiedPathAdvancement()
        {
            var c = Create(false);
            Assert.IsFalse(c.World.TryAdvanceEnemyAlongVerifiedOriginalRoute(
                "line", 200, 0, 1, 0.25));
            Assert.AreEqual(100, c.World.MobileEnemySnapshot()[0].X);
            Assert.AreEqual(0, c.Movement.Calls);
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void DeniedNativePathOrEmptySourceEvidenceCannotMoveAnEnemy(
            bool approved, bool evidence)
        {
            var c = Create(true);
            c.Movement.Approved = approved;
            c.Movement.Evidence = evidence;
            Assert.IsFalse(c.World.TryAdvanceEnemyAlongVerifiedOriginalRoute(
                "line", 220, 0, 1, .3));
            Assert.AreEqual(100, c.World.MobileEnemySnapshot()[0].X);
        }

        [Test]
        public void VerifiedMovementBypassesSpeedFormulaAfterOriginalAuraRemoved()
        {
            var c = Create(true);
            c.Targets.Approved = false;
            c.World.MoveCharacter(c.AuraCaster, 1, 0);
            Assert.IsFalse(HasSlow(c.World.StatusOf("line")));
            Assert.IsTrue(c.World.TryAdvanceEnemyAlongVerifiedOriginalRoute(
                "line", 200, 0, 1, 0.25));
            Assert.AreEqual(200, c.World.MobileEnemySnapshot()[0].X);
            Assert.AreEqual(0, c.Movement.Calls);
        }

        [Test]
        public void OriginalStunStillBlocksAnyMovementBeforeNativeSpeedResolution()
        {
            var c = Create(true);
            c.World.StatusOf("line").Apply(new CombatEffect(
                "TEST_ONLY-native-stun", CombatEffectType.Stun,
                CombatEffectOrigin.TriggeredSkill, 1, .5));
            Assert.IsFalse(c.World.TryAdvanceEnemyAlongVerifiedOriginalRoute(
                "line", 200, 0, 1, 0.25));
            Assert.AreEqual(100, c.World.MobileEnemySnapshot()[0].X);
            Assert.AreEqual(0, c.Movement.Calls);
        }

        [Test]
        public void ReadOnlySourceWitnessCannotMoveEnemyInPreflight()
        {
            var c = Create(true);
            c.Movement.DirtyPreflight = true;
            Assert.Throws<InvalidOperationException>(() =>
                c.World.TryAdvanceEnemyAlongVerifiedOriginalRoute(
                    "line", 200, 0, 1, 0.25));
        }
    }
}
