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
    public sealed class Original323NativeCommandAuraTests
    {
        private sealed class NativeDice : ICombatProcRandom
        {
            public int Calls;
            public int Next(int upperExclusive) { Calls++; return 0; }
        }

        private sealed class TestOnlyOriginalAttack : IOriginalCommonNativeAttackVerification
        {
            public bool TryVerify(string id, string originalRawcode,
                out double firstDelay, out DamageSchool school, out string evidence)
            {
                firstDelay = 0;
                school = DamageSchool.Physical;
                evidence = id == "C-01" ?
                    "TEST_ONLY original native basic attack path" : null;
                return evidence != null;
            }
        }

        private sealed class TestOnlyQuantizer : IOrdr323VerifiedDamageQuantization
        {
            public long Quantize(double source, Ordr323VerifiedDamageHit original)
            { return (long)source; }
        }

        private sealed class TestOnlyCommandRawDamage : IOriginal323NativeAttackBuffRules
        {
            public int Preflights;
            public int Applications;
            public double RawBefore;
            public double RawAfter;
            public bool CanResolve(BasicDamageRequest request,
                CombatEnemy enemy, out string sourceEvidence)
            {
                Preflights++;
                int horn = 0;
                foreach (var effect in request.AttackerEffects)
                    if (effect.Type == CombatEffectType.AttackPowerBuff &&
                        Math.Abs(effect.Amount - 25) < 0.000001 &&
                        effect.SourceId.Contains(":A0EN:B00V:1"))
                        horn++;
                sourceEvidence = horn == 1 ?
                    "TEST_ONLY source-witnessed single Chopper Horn buff" : null;
                return horn == 1;
            }

            public bool TryApply(BasicDamageRequest request,
                CombatEnemy enemy, Ordr323VerifiedDamageHit sourceHit,
                out Ordr323VerifiedDamageHit approved)
            {
                Applications++;
                RawBefore = sourceHit.RawDamage;
                RawAfter = sourceHit.RawDamage * 1.25;
                // TEST_ONLY percentage formula. The production game must
                // verify native stack, cap, arithmetic and HP conversion.
                approved = new Ordr323VerifiedDamageHit(RawAfter,
                    sourceHit.AttackType, sourceHit.UseAttackTable,
                    sourceHit.UseArmorRating, sourceHit.UseMagicResistance,
                    sourceHit.Traits,
                    "TEST_ONLY original ACac native raw damage calculation",
                    sourceHit.School);
                return true;
            }
        }

        private class VerifiedCurrentTargets :
            IOriginal323SpecialAuraTargetRules, IOriginal323FlatArmorAuraRules
        {
            public bool Approved = true;
            public bool CanAffectEnemy(Original323SpecialAura aura,
                UnitState caster, SpatialCombatEnemy target,
                double time, out string evidence)
            {
                evidence = null;
                return false;
            }
            public bool IsVerifiedFlatArmor(Original323SpecialAura aura,
                UnitState caster, double time, out string evidence)
            {
                evidence = null;
                return false;
            }
            public bool CanAffectAlly(Original323SpecialAura aura,
                UnitState caster, UnitState target,
                double time, out string evidence)
            {
                // TEST_ONLY; does not certify actual WC3 current rank,
                // buff presence, immunity or linger timing.
                evidence = Approved ?
                    "TEST_ONLY observed native current friend/self mask" : null;
                return Approved;
            }
        }

        private sealed class ObservedNativeRank : VerifiedCurrentTargets,
            IOriginal323NativeAuraRankWitness
        {
            public int Level = 2;
            public bool HasEvidence = true;
            public bool TryObserveCurrentRank(long sourceId,
                string abilityCode, double time, out int observedRank,
                out string sourceEvidence)
            {
                observedRank = Level;
                sourceEvidence = HasEvidence && abilityCode == "A0IN"
                    ? "TEST_ONLY current native Warcraft ACac level" : null;
                return HasEvidence && abilityCode == "A0IN";
            }
        }

        private static string Data(string name)
        {
            return File.ReadAllText(Path.Combine(
                Application.dataPath, "..", "data", name));
        }

        private static CharacterUnitCatalog Units()
        {
            return CharacterUnitCatalog.FromJson(Data("units.json"));
        }

        private static OriginalMapBaseStatCatalog Map()
        {
            var units = Units();
            return OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
        }

        private static Original323NativeCommandAuraCatalog Commands()
        {
            return Original323NativeCommandAuraCatalog.FromJson(
                Data("original-2323C-all-unit-aura-audit.json"), Map());
        }

        private static Original323CompositeAuraCatalog AllAuras()
        {
            var map = Map();
            var six = new Original323SpecialAuraCatalog(map);
            var armor = Original323ArmorAuraCatalog.FromJson(
                Data("original-2323C-all-unit-aura-audit.json"), map, six);
            var speed = Original323NativeSpeedAuraCatalog.FromJson(
                Data("original-2323C-all-unit-aura-audit.json"), map);
            var command = Original323NativeCommandAuraCatalog.FromJson(
                Data("original-2323C-all-unit-aura-audit.json"), map);
            return new Original323CompositeAuraCatalog(
                map, armor, speed, command);
        }

        private static CharacterCombatRuntime World(VerifiedCurrentTargets native)
        {
            var fallback = new RejectUnverifiedDamageFormula();
            var game = new CharacterCombatRuntime(Units(),
                CharacterSkillCatalog.FromJson(
                    "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}"),
                fallback, fallback, new SeededNonOriginalProcRandom(41));
            game.ConfigureOriginalAuras(AllAuras(), native);
            game.Start();
            game.BeginWave(1);
            return game;
        }

        private static Original323SpecialAura ExactlyOne(
            IOriginal323AuraCatalog catalog, string unitId)
        {
            var found = catalog.FindAll(unitId);
            Assert.AreEqual(1, found.Count, unitId);
            return found[0];
        }

        private static int Effects(CombatStatusEngine status,
            CombatEffectType type, double amount)
        {
            int n = 0;
            foreach (var buff in status.Snapshot())
                if (buff.Type == type &&
                    Math.Abs(buff.Amount - amount) < 0.000001) n++;
            return n;
        }

        // One unit can legitimately own an AHad defense aura and a
        // distinct ACac command aura. Assert only on source ACac rather
        // than pretending the entire union contains exactly one effect.
        private static Original323SpecialAura ExactlyOneCommand(
            IOriginal323AuraCatalog catalog, string unitId)
        {
            Original323SpecialAura found = null;
            foreach (var aura in catalog.FindAll(unitId))
            {
                if (aura.ParentAbility != "ACac") continue;
                Assert.IsNull(found, unitId + " has multiple ACac candidates");
                found = aura;
            }
            Assert.IsNotNull(found, unitId + " has no ACac candidate");
            return found;
        }

        [Test]
        public void MapAcacDefinesChopperGuardSevenAndHornTwentyFivePercent()
        {
            var command = Commands();
            Assert.AreEqual(17, command.UnitCount);
            Assert.AreEqual(19, command.NativeAttackPowerDefinitions);
            var guard = ExactlyOne(command, "S-25");
            var horn = ExactlyOne(command, "R-35");
            Assert.AreEqual("A0EM", guard.AbilityRawcode);
            Assert.AreEqual("A0EN", horn.AbilityRawcode);
            Assert.AreEqual("ACac", horn.ParentAbility);
            Assert.AreEqual(7, guard.Amount);
            Assert.AreEqual(25, horn.Amount);
            Assert.AreEqual(850, guard.Radius);
            Assert.AreEqual(850, horn.Radius);
            Assert.AreEqual("B00V", guard.BuffRawcode);
            Assert.AreEqual("B00V", horn.BuffRawcode);
            Assert.AreEqual(CombatEffectType.AttackPowerBuff, horn.Type);
            Assert.AreEqual(0.25, horn.SignedRawObjectValue, 1e-9);
            Assert.IsTrue(guard.AllowsAllyRole(true));
            Assert.IsTrue(guard.AllowsAllyRole(false));
        }

        [Test]
        public void FullAuditAddsEighteenNewDefinitionsWithoutDuplicatingGuard()
        {
            var all = AllAuras();
            Assert.AreEqual(149, all.DefinitionCount);
            var guard = all.FindAll("S-25");
            Assert.AreEqual(1, guard.Count, "Existing A0EM is not applied twice.");
            Assert.AreEqual(25, ExactlyOne(all, "R-35").Amount);
            Assert.AreEqual(40, ExactlyOne(all, "X-06").Amount);
            Assert.AreEqual(32, ExactlyOneCommand(all, "L-03").Amount);
            Assert.AreEqual(60, ExactlyOneCommand(all, "I-04").Amount);
            Assert.GreaterOrEqual(all.FindAll("L-03").Count, 2,
                "Keep the legitimate separate original aura instead of discarding it.");
        }

        [Test]
        public void ChopperHornAuraActuallyUpdatesLiveAlliesWithinOriginalRadius()
        {
            var native = new VerifiedCurrentTargets();
            var world = World(native);
            long horn = world.SpawnCharacter("R-35", 0, 0);
            long ally = world.SpawnCharacter("C-01", 100, 0);
            Assert.AreEqual(1, Effects(world.UnitStatusOf(horn),
                CombatEffectType.AttackPowerBuff, 25));
            Assert.AreEqual(1, Effects(world.UnitStatusOf(ally),
                CombatEffectType.AttackPowerBuff, 25));

            world.MoveCharacter(ally, 851, 0);
            Assert.AreEqual(0, Effects(world.UnitStatusOf(ally),
                CombatEffectType.AttackPowerBuff, 25));
            world.MoveCharacter(ally, 850, 0);
            Assert.AreEqual(1, Effects(world.UnitStatusOf(ally),
                CombatEffectType.AttackPowerBuff, 25));

            native.Approved = false;
            world.MoveCharacter(horn, 1, 0);
            Assert.AreEqual(0, Effects(world.UnitStatusOf(ally),
                CombatEffectType.AttackPowerBuff, 25));
        }

        [Test]
        public void NativeSelfOnlyAndNotSelfFlagsAreRespectedIndependentOfWitness()
        {
            var world = World(new VerifiedCurrentTargets());
            long nika = world.SpawnCharacter("E-01", 0, 0);
            long other = world.SpawnCharacter("C-01", 100, 0);
            Assert.AreEqual(1, Effects(world.UnitStatusOf(nika),
                CombatEffectType.AttackPowerBuff, 25));
            Assert.AreEqual(0, Effects(world.UnitStatusOf(other),
                CombatEffectType.AttackPowerBuff, 25));

            long oden = world.SpawnCharacter("E-08", 0, 0);
            Assert.AreEqual(0, Effects(world.UnitStatusOf(oden),
                CombatEffectType.AttackPowerBuff, 50));
            Assert.AreEqual(1, Effects(world.UnitStatusOf(other),
                CombatEffectType.AttackPowerBuff, 50));
            Assert.AreEqual(1, Effects(world.UnitStatusOf(nika),
                CombatEffectType.AttackPowerBuff, 50));
        }

        [Test]
        public void TranscendentChopperHasSeparateSelfAndNotSelfCommands()
        {
            var world = World(new VerifiedCurrentTargets());
            long chopper = world.SpawnCharacter("TR-23", 0, 0);
            long marine = world.SpawnCharacter("C-01", 100, 0);
            Assert.AreEqual(1, Effects(world.UnitStatusOf(chopper),
                CombatEffectType.AttackPowerBuff, 40));
            Assert.AreEqual(2, Effects(world.UnitStatusOf(marine),
                CombatEffectType.AttackPowerBuff, 40),
                "Two original native abilities remain separate status entries; " +
                "final stacking still needs independent original proof.");
        }

        [Test]
        public void UnobservedNativeSkillRankCannotStackSelfOnlyLevels()
        {
            var noNativeRank = World(new VerifiedCurrentTargets());
            long actor = noNativeRank.SpawnCharacter("I-08", 0, 0);
            Assert.AreEqual(0, Effects(noNativeRank.UnitStatusOf(actor),
                CombatEffectType.AttackPowerBuff, 25));
            Assert.AreEqual(0, Effects(noNativeRank.UnitStatusOf(actor),
                CombatEffectType.AttackPowerBuff, 50));
        }

        [TestCase(1, 25, 50)]
        [TestCase(2, 50, 25)]
        public void CurrentNativeAbilityLevelAllowsOnlyItsActualOneRank(
            int sourceRank, int activePercent, int inactivePercent)
        {
            var proof = new ObservedNativeRank { Level = sourceRank };
            var world = World(proof);
            long actor = world.SpawnCharacter("I-08", 0, 0);
            Assert.AreEqual(1, Effects(world.UnitStatusOf(actor),
                CombatEffectType.AttackPowerBuff, activePercent));
            Assert.AreEqual(0, Effects(world.UnitStatusOf(actor),
                CombatEffectType.AttackPowerBuff, inactivePercent));

            proof.HasEvidence = false;
            world.MoveCharacter(actor, 1, 0);
            Assert.AreEqual(0, Effects(world.UnitStatusOf(actor),
                CombatEffectType.AttackPowerBuff, activePercent));
        }

        [Test]
        public void CannotTreatOriginalFlatOrOpposingModifiersAsPercentageBuffs()
        {
            var command = Commands();
            foreach (var id in new[] {"TR-04", "E-05", "I-11"})
                Assert.AreEqual(0, command.FindAll(id).Count,
                    "Native flat/negative/unknown mode is not an ally percent buff.");
            Assert.IsTrue(command.ExcludedDefinitions.Count > 0);
            var eligible = command.FindAll("I-08");
            Assert.AreEqual(2, eligible.Count,
                "Two native self-only ranks must not be merged as one ability.");
            Assert.AreEqual(25, eligible[0].Amount);
            Assert.AreEqual(50, eligible[1].Amount);
        }

        [Test]
        public void SourceHashMismatchRejectsAllNewCommandAuras()
        {
            string json = Data("original-2323C-all-unit-aura-audit.json");
            var corrupted = json.Replace(
                Original323UncommonAbilityCatalog.SourceW3xSha256,
                new string('0', 64));
            Assert.Throws<FormatException>(() =>
                Original323NativeCommandAuraCatalog.FromJson(corrupted, Map()));
        }

        [Test]
        public void ChopperHornAuraChangesNativeRawAttackOnlyWithIndependentRules()
        {
            var dice = new NativeDice();
            var formula = new TestOnlyCommandRawDamage();
            var originalUnits = Units();
            var attackPath = new OriginalMapCommonNativeAttackBinding(
                Map(), new TestOnlyOriginalAttack(), dice, formula);
            var world = Ordr323DamageWorldBinding.Create(
                originalUnits,
                CharacterSkillCatalog.FromJson(
                    "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}"),
                attackPath, null, new TestOnlyQuantizer(), dice);
            world.ConfigureOriginalAuras(AllAuras(), new VerifiedCurrentTargets());
            world.ConfigureVerifiedUnitAttackProvider(attackPath);
            world.Start();
            world.BeginWave(1);
            world.SpawnCharacter("R-35", 0, 0);
            world.SpawnCharacter("C-01", 0, 0);
            world.SpawnEnemyWithDefenses("line", 10000,
                CharacterEnemyKind.Line, 10, 0, 0,
                new EnemyDefenseProfile(CharacterEnemyKind.Line, 10, null,
                    "normal", "TEST_ONLY original physical defense"));
            world.Step(0);
            Assert.AreEqual(1, formula.Preflights);
            Assert.AreEqual(1, formula.Applications);
            Assert.AreEqual(29, formula.RawBefore);
            Assert.AreEqual(36.25, formula.RawAfter, 1e-9);
            Assert.Less(world.EnemyOf("line").Health, 10000);
            Assert.AreEqual(1, dice.Calls);
        }

        [Test]
        public void HornBuffWithoutOriginalNativeDamageFormulaCannotFakeExtraDamage()
        {
            var dice = new NativeDice();
            var originalUnits = Units();
            var attackPath = new OriginalMapCommonNativeAttackBinding(
                Map(), new TestOnlyOriginalAttack(), dice);
            var world = Ordr323DamageWorldBinding.Create(
                originalUnits,
                CharacterSkillCatalog.FromJson(
                    "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}"),
                attackPath, null, new TestOnlyQuantizer(), dice);
            world.ConfigureOriginalAuras(AllAuras(), new VerifiedCurrentTargets());
            world.ConfigureVerifiedUnitAttackProvider(attackPath);
            world.Start();
            world.BeginWave(1);
            world.SpawnCharacter("R-35", 0, 0);
            world.SpawnCharacter("C-01", 0, 0);
            world.SpawnEnemyWithDefenses("line", 10000,
                CharacterEnemyKind.Line, 10, 0, 0,
                new EnemyDefenseProfile(CharacterEnemyKind.Line, 10, null,
                    "normal", "TEST_ONLY original physical defense"));
            world.Step(0);
            Assert.AreEqual(10000, world.EnemyOf("line").Health,
                "A 25% original ACac field is NOT a verified raw damage formula.");
            Assert.AreEqual(0, dice.Calls,
                "Unapproved native hit may not consume its original damage dice.");
        }

        [Test]
        public void MissingCurrentNativeTargetProofCannotCreateAnyAttackBonus()
        {
            var world = World(new VerifiedCurrentTargets { Approved = false });
            long horn = world.SpawnCharacter("R-35", 0, 0);
            long ally = world.SpawnCharacter("C-01", 100, 0);
            Assert.AreEqual(0, Effects(world.UnitStatusOf(horn),
                CombatEffectType.AttackPowerBuff, 25));
            Assert.AreEqual(0, Effects(world.UnitStatusOf(ally),
                CombatEffectType.AttackPowerBuff, 25));
            Assert.AreEqual(0, world.ReadyAttackers,
                "Native attack and buff formula remain separately unverified.");
        }
    }
}
