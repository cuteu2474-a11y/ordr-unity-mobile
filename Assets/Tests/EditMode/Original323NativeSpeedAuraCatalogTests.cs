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
    public sealed class Original323NativeSpeedAuraCatalogTests
    {
        // All runtime proofs in this fixture are TEST_ONLY. The real AOae
        // mask, current rank, immunity and current native buff must be
        // independently observed before a shipped game may apply the aura.
        private sealed class ObservedNativeTargets : IOriginal323SpecialAuraTargetRules,
            IOriginal323FlatArmorAuraRules
        {
            public bool Allow = true;
            public string ApprovedAbility;
            public bool CanAffectEnemy(Original323SpecialAura aura,
                UnitState caster, SpatialCombatEnemy target,
                double time, out string evidence)
            {
                bool approved = Allow &&
                    (ApprovedAbility == null || ApprovedAbility == aura.AbilityRawcode);
                evidence = approved
                    ? "TEST_ONLY current unit rank/mask/buff/immune proof" : null;
                return approved;
            }
            public bool CanAffectAlly(Original323SpecialAura aura,
                UnitState caster, UnitState target,
                double time, out string evidence)
            {
                bool approved = Allow &&
                    (ApprovedAbility == null || ApprovedAbility == aura.AbilityRawcode);
                evidence = approved
                    ? "TEST_ONLY current friend/self native buff evidence" : null;
                return approved;
            }
            public bool IsVerifiedFlatArmor(Original323SpecialAura aura,
                UnitState caster, double time, out string evidence)
            {
                evidence = null;
                return false;
            }
        }

        private static string Data(string name)
        {
            return File.ReadAllText(Path.Combine(
                Application.dataPath, "..", "data", name));
        }

        private static CharacterUnitCatalog Units()
        { return CharacterUnitCatalog.FromJson(Data("units.json")); }

        private static OriginalMapBaseStatCatalog Map()
        {
            var units = Units();
            return OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
        }

        private static Original323NativeSpeedAuraCatalog Speeds()
        {
            return Original323NativeSpeedAuraCatalog.FromJson(
                Data("original-2323C-all-unit-aura-audit.json"), Map());
        }

        private static Original323CompositeAuraCatalog Combined()
        {
            var map = Map();
            var six = new Original323SpecialAuraCatalog(map);
            var armor = Original323ArmorAuraCatalog.FromJson(
                Data("original-2323C-all-unit-aura-audit.json"), map, six);
            var speeds = Original323NativeSpeedAuraCatalog.FromJson(
                Data("original-2323C-all-unit-aura-audit.json"), map);
            return new Original323CompositeAuraCatalog(map, armor, speeds);
        }

        private static Original323SpecialAura Only(
            IOriginal323AuraCatalog catalog, string id)
        {
            var defs = catalog.FindAll(id);
            Assert.AreEqual(1, defs.Count, id);
            return defs[0];
        }

        private static bool HasEffect(CombatStatusEngine status,
            CombatEffectType type, double amount)
        {
            foreach (var entry in status.Snapshot())
                if (entry.Type == type &&
                    Math.Abs(entry.Amount - amount) < 0.000001)
                    return true;
            return false;
        }

        private static CharacterCombatRuntime World(
            ObservedNativeTargets witness)
        {
            var failedDamage = new RejectUnverifiedDamageFormula();
            var world = new CharacterCombatRuntime(
                Units(), CharacterSkillCatalog.FromJson(
                    "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}"),
                failedDamage, failedDamage,
                new SeededNonOriginalProcRandom(7));
            world.ConfigureOriginalAuras(Combined(), witness);
            world.Start();
            world.BeginWave(1);
            return world;
        }

        [Test]
        public void PinnedNativeAoaeAuditLinks59OwnersWith51SlowsAnd18AttackSpeedBuffs()
        {
            var source = Speeds();
            Assert.AreEqual(59, source.UnitCount);
            Assert.AreEqual(51, source.MovementSlowDefinitionCount);
            Assert.AreEqual(18, source.AllyAttackSpeedDefinitionCount);

            var smoker = Only(source, "S-14");
            Assert.AreEqual("A08F", smoker.AbilityRawcode);
            Assert.AreEqual(CombatEffectType.MovementSlowPercent, smoker.Type);
            Assert.AreEqual(5, smoker.Amount);
            Assert.AreEqual(800, smoker.Radius);

            var aokiji = Only(source, "R-26");
            Assert.AreEqual("A023", aokiji.AbilityRawcode);
            Assert.AreEqual(CombatEffectType.MovementSlowPercent, aokiji.Type);
            Assert.AreEqual(10, aokiji.Amount);
            Assert.AreEqual(800, aokiji.Radius);

            var captainKid = Only(source, "R-37");
            Assert.AreEqual("A03S", captainKid.AbilityRawcode);
            Assert.AreEqual(15, captainKid.Amount);

            var crocodile = Only(source, "R-38");
            Assert.AreEqual("A03W", crocodile.AbilityRawcode);
            Assert.AreEqual(600, crocodile.Radius);

            var perona = Only(source, "R-41");
            Assert.AreEqual("A03R", perona.AbilityRawcode);
            Assert.AreEqual(20, perona.Amount);

            var brook = Only(source, "R-18");
            Assert.AreEqual("A0FT", brook.AbilityRawcode);
            Assert.AreEqual(CombatEffectType.AttackSpeedBuff, brook.Type);
            Assert.AreEqual(10, brook.Amount);
            Assert.IsTrue(brook.TargetsAllies);
        }

        [Test]
        public void CompositePreservesExistingArmorAndOnlyOneOfPinnedSixAuras()
        {
            var all = Combined();
            Assert.Greater(all.UnitCount, 58);
            Assert.AreEqual(131, all.DefinitionCount);
            Assert.AreEqual(1, all.FindAll("S-14").Count,
                "Pinned smoker slow cannot stack with the identical W3X audit.");
            Assert.AreEqual(1, all.FindAll("S-29").Count);
            Assert.AreEqual(1, all.FindAll("S-30").Count);
            Assert.AreEqual(CombatEffectType.AttackPowerBuff,
                Only(all, "S-25").Type);
            Assert.AreEqual(CombatEffectType.MovementSlowPercent,
                Only(all, "R-26").Type);
            Assert.AreEqual(CombatEffectType.AttackSpeedBuff,
                Only(all, "R-18").Type);
        }

        [Test]
        public void RareAokijiNativeSlowReachesDistantEnemyButRequiresLiveTargetProof()
        {
            var native = new ObservedNativeTargets { ApprovedAbility = "A023" };
            var world = World(native);
            long caster = world.SpawnCharacter("R-26", 0, 0);
            world.SpawnEnemy("line", 10000, CharacterEnemyKind.Line, 100, 0, 0);
            Assert.IsTrue(HasEffect(world.StatusOf("line"),
                CombatEffectType.MovementSlowPercent, 10));
            Assert.AreEqual("R-26", world.Roster.Get(caster).UnitId);

            Assert.IsTrue(world.TryMoveEnemy("line", 900, 0, 0));
            Assert.IsTrue(HasEffect(world.StatusOf("line"),
                CombatEffectType.MovementSlowPercent, 10),
                "Mobile aura radius is unlimited for an otherwise eligible enemy.");

            Assert.IsTrue(world.TryMoveEnemy("line", 200, 0, 0));
            Assert.IsTrue(HasEffect(world.StatusOf("line"),
                CombatEffectType.MovementSlowPercent, 10));
            native.Allow = false;
            world.MoveCharacter(caster, 1, 0);
            Assert.IsFalse(HasEffect(world.StatusOf("line"),
                CombatEffectType.MovementSlowPercent, 10));
        }

        [Test]
        public void RareBrookNativeAttackSpeedAuraReachesDistantApprovedAllies()
        {
            var native = new ObservedNativeTargets { ApprovedAbility = "A0FT" };
            var world = World(native);
            long brook = world.SpawnCharacter("R-18", 0, 0);
            long ally = world.SpawnCharacter("C-01", 100, 0);
            Assert.IsTrue(HasEffect(world.UnitStatusOf(ally),
                CombatEffectType.AttackSpeedBuff, 10));
            Assert.IsTrue(HasEffect(world.UnitStatusOf(brook),
                CombatEffectType.AttackSpeedBuff, 10));

            world.MoveCharacter(ally, 950, 0);
            Assert.IsTrue(HasEffect(world.UnitStatusOf(ally),
                CombatEffectType.AttackSpeedBuff, 10));
            Assert.IsTrue(HasEffect(world.UnitStatusOf(brook),
                CombatEffectType.AttackSpeedBuff, 10));

            native.Allow = false;
            world.MoveCharacter(brook, 1, 0);
            Assert.IsFalse(HasEffect(world.UnitStatusOf(brook),
                CombatEffectType.AttackSpeedBuff, 10));
        }

        [Test]
        public void UnverifiedNativeSpeedAuraHasNoEffectOrImplicitSkillDamage()
        {
            var native = new ObservedNativeTargets { Allow = false };
            var world = World(native);
            world.SpawnCharacter("R-26", 0, 0);
            world.SpawnCharacter("R-18", 0, 0);
            world.SpawnEnemy("line", 1000, CharacterEnemyKind.Line, 50, 0, 0);
            Assert.AreEqual(0, world.StatusOf("line").ActiveCount);
            Assert.AreEqual(0, world.ReadyAttackers);
            Assert.AreEqual(1000, world.EnemyOf("line").Health);
        }

        [Test]
        public void SourceIdentityMismatchRejectsReusedAuraRows()
        {
            var map = Map();
            string text = Data("original-2323C-all-unit-aura-audit.json");
            Assert.Throws<FormatException>(() =>
                Original323NativeSpeedAuraCatalog.FromJson(
                    text.Replace("0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c",
                        new string('0', 64)), map));
        }
    }
}
