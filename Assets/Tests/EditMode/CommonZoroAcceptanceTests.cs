using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // Synthetic approval, armor and HP conversion exercise the real combat
    // pipeline. They are NEVER claims of original JASS/Warcraft game parity.
    public sealed class CommonZoroAcceptanceTests
    {
        private sealed class CountingRoll : ICombatProcRandom
        {
            public int Calls;
            public bool Maximum;
            public int Next(int upperExclusive)
            {
                Calls++;
                return Maximum ? upperExclusive - 1 : 0;
            }
        }

        private sealed class SyntheticNativeApproval : IOriginalCommonNativeAttackVerification
        {
            private readonly bool enabled;
            public SyntheticNativeApproval(bool enabledForZoro) { enabled = enabledForZoro; }
            public bool TryVerify(string catalogId, string rawcode, out double firstDelay,
                out DamageSchool school, out string evidence)
            {
                firstDelay = 0.25; // fixture from original object attack point only
                school = DamageSchool.Physical;
                bool matched = enabled && catalogId == "C-02" && rawcode == "h002";
                evidence = matched ? "TEST_ONLY: synthetic native JASS approval" : null;
                return matched;
            }
        }

        private sealed class SyntheticQuantizer : IOrdr323VerifiedDamageQuantization
        {
            public bool Fail;
            public bool ZeroDamage;
            public long Quantize(double value, Ordr323VerifiedDamageHit hit)
            {
                if (Fail) throw new NotSupportedException("TEST_ONLY: unknown HP quantization");
                return ZeroDamage ? 0 : (long)Math.Truncate(value);
            }
        }

        private static string Data(string name)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", name));
        }

        private static CharacterCombatRuntime World(bool approved, CountingRoll dice,
            SyntheticQuantizer quantizer = null)
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var bridge = new OriginalMapCommonNativeAttackBinding(map,
                new SyntheticNativeApproval(approved), dice);
            var world = Ordr323DamageWorldBinding.Create(
                units, skills, bridge, null, quantizer ?? new SyntheticQuantizer(), dice);
            world.ConfigureVerifiedUnitAttackProvider(bridge);
            world.ConfigureOriginalMapCommonAttackPreview(map);
            world.Start();
            world.BeginWave(1);
            return world;
        }

        private static void SpawnKnown(CharacterCombatRuntime world, string id,
            long health = 1000, CharacterEnemyKind kind = CharacterEnemyKind.Line,
            double armor = 0, double x = 10)
        {
            world.SpawnEnemyWithDefenses(id, health, kind, x, 0, 0,
                new EnemyDefenseProfile(kind, armor, null, "normal",
                    "TEST_ONLY: synthetic target armor/HP"));
        }

        [Test]
        public void C02ObjectIdentityAndAllFiveRawAbilitiesAreNotInventedCombatSkills()
        {
            var catalog = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), catalog);
            var unit = map.Find("C-02");
            Assert.AreEqual("h002", unit.OriginalRawcode);
            Assert.AreEqual(31, unit.Attack1.MinimumBaseDamage.Value);
            Assert.AreEqual(33, unit.Attack1.MaximumBaseDamage.Value);
            Assert.AreEqual(1.03, unit.Attack1.AttackCooldownSeconds.Value, 1e-9);
            Assert.AreEqual(0.25, unit.Attack1.AttackPointSeconds.Value, 1e-9);
            Assert.AreEqual(0.75, Ordr323MapDamageMath.AttackVersusArmor(
                Ordr323AttackType.Pierce, Ordr323ArmorType.Normal), 1e-9);
            CollectionAssert.AreEqual(new[] { "A0JE", "A09G", "A00W", "A032", "Avul" },
                unit.OriginalAbilityRawcodes);
            var world = World(false, new CountingRoll());
            long zoro = world.SpawnCharacter("C-02", 0, 0);
            Assert.IsNull(world.ProfileOf(zoro), "No tooltip profile means unknown, not proof of no skills");
        }

        [Test]
        public void NoIndependentNativeApprovalMeansOnlyCueAndZeroDamageRolls()
        {
            var dice = new CountingRoll();
            var world = World(false, dice);
            long id = world.SpawnCharacter("C-02", 0, 0);
            SpawnKnown(world, "target");
            Assert.IsFalse(world.HasVerifiedAttack(id));
            Assert.AreEqual(BasicAttackReadinessKind.UnverifiedSource,
                world.InspectBasicAttack(id).Kind);
            world.Step(4.0);
            Assert.AreEqual(1000L, world.EnemyOf("target").Health);
            Assert.AreEqual(0, dice.Calls);
            Assert.AreEqual(0, world.Game.DefeatedEnemies);
        }

        [Test]
        public void SyntheticApprovedZoroHitsAtDeclaredFixturePointThenEvery103Hundredths()
        {
            var dice = new CountingRoll();
            var world = World(true, dice);
            long zoro = world.SpawnCharacter("C-02", 0, 0);
            SpawnKnown(world, "target");
            Assert.IsTrue(world.HasVerifiedAttack(zoro));
            world.Step(0.24);
            Assert.AreEqual(1000L, world.EnemyOf("target").Health);
            world.Step(0.01);
            Assert.AreEqual(977L, world.EnemyOf("target").Health);
            world.Step(1.02);
            Assert.AreEqual(977L, world.EnemyOf("target").Health);
            world.Step(0.01);
            Assert.AreEqual(954L, world.EnemyOf("target").Health);
            Assert.AreEqual(2, dice.Calls);
            Assert.AreEqual(0, world.Game.DefeatedEnemies);
        }

        [Test]
        public void LargeFrameAndEightHalfSecondFramesHaveIdenticalDamage()
        {
            var once = World(true, new CountingRoll());
            once.SpawnCharacter("C-02", 0, 0);
            SpawnKnown(once, "target");
            once.Step(4.0);
            var partitioned = World(true, new CountingRoll());
            partitioned.SpawnCharacter("C-02", 0, 0);
            SpawnKnown(partitioned, "target");
            for (int i = 0; i < 8; i++) partitioned.Step(0.5);
            Assert.AreEqual(908L, once.EnemyOf("target").Health);
            Assert.AreEqual(once.EnemyOf("target").Health,
                partitioned.EnemyOf("target").Health);
        }

        [Test]
        public void EnemyDefeatAndGameCountersAreCommittedExactlyOnce()
        {
            var world = World(true, new CountingRoll());
            world.SpawnCharacter("C-02", 0, 0);
            SpawnKnown(world, "target", 23);
            var output = world.Step(0.25);
            int attacks = 0, deaths = 0;
            foreach (var evt in output)
            {
                if (evt.Type == CharacterCombatEventType.Attack) attacks++;
                if (evt.Type == CharacterCombatEventType.EnemyDefeated) deaths++;
            }
            Assert.AreEqual(1, attacks);
            Assert.AreEqual(1, deaths);
            Assert.IsNull(world.EnemyOf("target"));
            Assert.AreEqual(0, world.Game.AliveEnemies);
            Assert.AreEqual(1, world.Game.DefeatedEnemies);
            world.Step(5.0);
            Assert.AreEqual(1, world.Game.DefeatedEnemies);
        }

        [Test]
        public void UnknownArmorTargetCannotConsumeRngOrBlockLaterVerifiedTarget()
        {
            var dice = new CountingRoll();
            var world = World(true, dice);
            long zoro = world.SpawnCharacter("C-02", 0, 0);
            world.SpawnEnemy("unknown", 1000, CharacterEnemyKind.Line, 1, 0, 0);
            world.Step(3.0);
            Assert.AreEqual(0, dice.Calls);
            Assert.AreEqual(BasicAttackReadinessKind.UnverifiedTarget,
                world.InspectBasicAttack(zoro).Kind);
            SpawnKnown(world, "known");
            world.Step(0);
            Assert.AreEqual(977L, world.EnemyOf("known").Health);
            Assert.AreEqual(1000L, world.EnemyOf("unknown").Health);
            Assert.AreEqual(1, dice.Calls);
        }

        [Test]
        public void MainBattlefieldZoroNeverAttacksStoryOrCreepWithoutDispatch()
        {
            var dice = new CountingRoll();
            var world = World(true, dice);
            world.SpawnCharacter("C-02", 0, 0);
            SpawnKnown(world, "story", 1000, CharacterEnemyKind.Story, 0);
            SpawnKnown(world, "creep", 1000, CharacterEnemyKind.Creep, 0);
            world.Step(5.0);
            Assert.AreEqual(0, dice.Calls);
            SpawnKnown(world, "main", 1000, CharacterEnemyKind.Line, 0, 2000);
            world.Step(0); // TEST_ONLY synthetic reach; actual Warcraft attack range still unverified
            Assert.AreEqual(977L, world.EnemyOf("main").Health);
            Assert.AreEqual(1000L, world.EnemyOf("story").Health);
            Assert.AreEqual(1000L, world.EnemyOf("creep").Health);
        }

        [Test]
        public void FormulaDeferralDoesNotAdvanceCooldownAndCanBeResumedOnlyExplicitly()
        {
            var dice = new CountingRoll();
            var quantizer = new SyntheticQuantizer { Fail = true };
            var world = World(true, dice, quantizer);
            long id = world.SpawnCharacter("C-02", 0, 0);
            SpawnKnown(world, "target");
            world.Step(0.25);
            Assert.AreEqual(1000L, world.EnemyOf("target").Health);
            Assert.AreEqual(BasicAttackReadinessKind.FormulaDeferred,
                world.InspectBasicAttack(id).Kind);
            world.Step(1.5);
            Assert.AreEqual(1000L, world.EnemyOf("target").Health);
            quantizer.Fail = false;
            world.ResumeAfterVerifiedFormulaUpdate(id);
            world.Step(0);
            Assert.AreEqual(977L, world.EnemyOf("target").Health);
            Assert.AreEqual(BasicAttackReadinessKind.Cooldown,
                world.InspectBasicAttack(id).Kind);
            Assert.AreEqual(0, world.Game.DefeatedEnemies);
        }

        [Test]
        public void UnitRemovedBeforeFirstHitCannotFireGhostAttack()
        {
            var dice = new CountingRoll();
            var world = World(true, dice);
            long zoro = world.SpawnCharacter("C-02", 0, 0);
            SpawnKnown(world, "target");
            world.Step(0.2);
            world.RemoveCharacter(zoro);
            world.Step(5);
            Assert.AreEqual(0, dice.Calls);
            Assert.AreEqual(1000L, world.EnemyOf("target").Health);
        }

        [Test]
        public void ZeroDamageQuantizerNeverClaimsAFalseKill()
        {
            var dice = new CountingRoll();
            var quantizer = new SyntheticQuantizer { ZeroDamage = true };
            var world = World(true, dice, quantizer);
            world.SpawnCharacter("C-02", 0, 0);
            SpawnKnown(world, "target", 31);
            world.Step(0.25);
            Assert.AreEqual(31L, world.EnemyOf("target").Health);
            Assert.AreEqual(1, dice.Calls);
            Assert.AreEqual(0, world.Game.DefeatedEnemies);
        }

        [Test]
        public void SpawnedMidWaveUsesItsOwnFirstAttackReadyTime()
        {
            var world = World(true, new CountingRoll());
            SpawnKnown(world, "target");
            world.Step(2);
            long id = world.SpawnCharacter("C-02", 0, 0);
            Assert.AreEqual(BasicAttackReadinessKind.Cooldown,
                world.InspectBasicAttack(id).Kind);
            world.Step(0.24);
            Assert.AreEqual(1000L, world.EnemyOf("target").Health);
            world.Step(0.01);
            Assert.AreEqual(977L, world.EnemyOf("target").Health);
        }
    }
}
