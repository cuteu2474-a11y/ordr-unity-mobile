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
    public sealed class Original323ArmorAuraCatalogTests
    {
        // Deliberately synthetic source-rank/target/buff/mode proofs. The
        // object catalog cannot establish real Warcraft target state.
        private class Targets : IOriginal323SpecialAuraTargetRules
        {
            public readonly Dictionary<string, int> Levels = new Dictionary<string, int>();
            public string OnlyAbility;
            public string ExcludedEnemy;
            public bool Evidence = true;
            public bool CanAffectEnemy(Original323SpecialAura aura, UnitState source,
                SpatialCombatEnemy target, double time, out string evidence)
            {
                int level;
                if (!Levels.TryGetValue(aura.AbilityRawcode, out level)) level = 1;
                evidence = Evidence ? "TEST_ONLY: current rank, full mask and native buff timeline" : null;
                return aura.Level == level && target.Id != ExcludedEnemy &&
                    (OnlyAbility == null || aura.AbilityRawcode == OnlyAbility);
            }
            public bool CanAffectAlly(Original323SpecialAura aura, UnitState source,
                UnitState target, double time, out string evidence)
            { evidence = "TEST_ONLY"; return true; }
        }

        private sealed class FlatTargets : Targets, IOriginal323FlatArmorAuraRules
        {
            public bool Flat = true;
            public bool ModeEvidence = true;
            public bool IsVerifiedFlatArmor(Original323SpecialAura aura, UnitState source,
                double time, out string evidence)
            { evidence = ModeEvidence ? "TEST_ONLY: independently verified flat mode" : null; return Flat; }
        }

        private static string Data(string name) => File.ReadAllText(
            Path.Combine(Application.dataPath, "..", "data", name));
        private static CharacterUnitCatalog Units() => CharacterUnitCatalog.FromJson(Data("units.json"));
        private static OriginalMapBaseStatCatalog Map() => OriginalMapBaseStatCatalog.FromJson(
            Data("original-2323C-map-base-stats.json"), Units());
        private static Original323ArmorAuraCatalog Catalog(bool includeSix = true)
        {
            var map = Map();
            return Original323ArmorAuraCatalog.FromJson(Data("original-2323C-all-unit-aura-audit.json"),
                map, includeSix ? new Original323SpecialAuraCatalog(map) : null);
        }
        private static CharacterCombatRuntime World(IOriginal323SpecialAuraTargetRules rules)
        {
            var reject = new RejectUnverifiedDamageFormula();
            var world = new CharacterCombatRuntime(Units(), CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}"), reject, reject,
                new SeededNonOriginalProcRandom(1));
            world.ConfigureOriginalAuras(Catalog(), rules);
            world.Start(); world.BeginWave(1);
            return world;
        }
        private static void Enemy(CharacterCombatRuntime world, string id = "enemy", double x = 0)
        {
            world.SpawnEnemyWithDefenses(id, 10000, CharacterEnemyKind.Line, x, 0, 0,
                new EnemyDefenseProfile(CharacterEnemyKind.Line, 100, null, "normal", "TEST_ONLY"));
        }
        private static Ordr323VerifiedPartialModifierRules Modifiers(CharacterCombatRuntime world) =>
            new Ordr323VerifiedPartialModifierRules(
                verifiedSpecialAuraProvenance: world.IsVerifiedOriginalSpecialAuraEffect);

        [Test]
        public void CatalogCoversEverySupportedArmorOwnerAndKeepsPreviousOtherEffects()
        {
            var armor = Catalog(false);
            Assert.AreEqual(54, armor.UnitCount);
            Assert.AreEqual(61, armor.ArmorDefinitionCount);
            var combined = Catalog();
            Assert.AreEqual(58, combined.UnitCount);
            Assert.AreEqual(61, combined.ArmorDefinitionCount);
            Assert.AreEqual(1, combined.FindAll("S-10").Count, "Do not duplicate the previous pinned armor.");
            Assert.AreEqual(CombatEffectType.AttackPowerBuff, combined.FindAll("S-25")[0].Type);
            Assert.AreEqual(CombatEffectType.MovementSlowPercent, combined.FindAll("S-29")[0].Type);
            Assert.AreEqual(0, combined.FindAll("E-02").Count);
            Assert.AreEqual(0, combined.FindAll("I-01").Count, "Positive armor is not negative armor.");
            Assert.AreEqual(0, combined.FindAll("TR-27").Count, "Zero-radius/zero-amount is not an active reduction.");
            Assert.AreEqual(3, combined.ExcludedDefinitions.Count);
        }

        [TestCase("R-01", "A132", "B00C", 5, 825)]
        [TestCase("L-07", "A06Q", "B00U", 20, 900)]
        [TestCase("H-21", "A0H5", "B02R", 12, 800)]
        [TestCase("TR-19", "A0OS", "B052", 8, 99999)]
        [TestCase("I-04", "A0JP", "B03A", 60, 850)]
        public void RawValuesAreNotNormalizedByGrade(string id, string code, string buff,
            double amount, double radius)
        {
            Original323SpecialAura found = null;
            foreach (var aura in Catalog().FindAll(id)) if (aura.AbilityRawcode == code) found = aura;
            Assert.IsNotNull(found);
            Assert.AreEqual(buff, found.BuffRawcode);
            Assert.AreEqual(amount, found.Amount);
            Assert.AreEqual(-amount, found.SignedRawObjectValue);
            Assert.AreEqual(radius, found.Radius);
            Assert.IsNull(found.RawArmorPercentageMode, "Missing inherited DataB is not zero.");
        }

        [Test]
        public void FullMasksAndMultipleAbilitiesArePreservedPerOwner()
        {
            var list = Catalog().FindAll("L-38");
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("air,nonancient,nonsapper,nonhero,enemies,ground", list[0].TargetMask);
            Assert.AreEqual("air,sapper,enemies,ground", list[1].TargetMask);
            var world = World(new FlatTargets { OnlyAbility = "A06H" });
            long id = world.SpawnCharacter("L-38", 0, 0); Enemy(world);
            Assert.AreEqual(2, world.OriginalAurasOf(id).Count);
            Assert.AreEqual(1, world.StatusOf("enemy").ActiveCount);
            Assert.AreEqual(45, world.StatusOf("enemy").Snapshot()[0].Amount);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RankChangeReplacesEffectAndRetiresOldProvenance(bool useHighest)
        {
            var targets = new FlatTargets();
            var world = World(targets);
            world.SpawnCharacter("TR-20", 0, 0); Enemy(world);
            var first = world.StatusOf("enemy").Snapshot()[0];
            Assert.AreEqual(30, first.Amount);
            targets.Levels["A0QE"] = useHighest ? 3 : 2;
            world.Step(0);
            var snapshot = world.StatusOf("enemy").Snapshot();
            Assert.AreEqual(1, snapshot.Count, "Candidate ranks never apply together.");
            Assert.AreEqual(useHighest ? 42 : 35, snapshot[0].Amount);
            Assert.IsTrue(snapshot[0].SourceId.EndsWith(useHighest ? ":3" : ":2"));
            Assert.IsFalse(world.IsVerifiedOriginalSpecialAuraEffect("enemy", first));
            double armor;
            Assert.IsTrue(Modifiers(world).TryResolveArmor(world.EnemyOf("enemy"), snapshot, 0, out armor));
            Assert.AreEqual(useHighest ? 58 : 65, armor);
            targets.Levels["A0QE"] = 0;
            world.Step(0);
            Assert.AreEqual(0, world.StatusOf("enemy").ActiveCount, "Ability removal clears all ranks.");
        }

        [Test]
        public void UnknownTargetOrNativeArmorModeNeverGetsAnImplicitDefault()
        {
            foreach (var targets in new IOriginal323SpecialAuraTargetRules[] {
                null, new Targets(), new FlatTargets { Flat = false },
                new FlatTargets { ModeEvidence = false }, new FlatTargets { Evidence = false }
            })
            {
                var world = World(targets); world.SpawnCharacter("R-01", 0, 0); Enemy(world);
                Assert.AreEqual(0, world.StatusOf("enemy").ActiveCount);
            }
        }

        [Test]
        public void SameAuraAcrossDifferentCatalogUnitsContributesOnceAndKeepsEachLifetime()
        {
            var world = World(new FlatTargets());
            long drake = world.SpawnCharacter("R-01", 0, 0);
            long king = world.SpawnCharacter("L-34", 0, 0); Enemy(world);
            double armor;
            Assert.AreEqual(2, world.StatusOf("enemy").ActiveCount);
            Assert.IsTrue(Modifiers(world).TryResolveArmor(world.EnemyOf("enemy"),
                world.StatusOf("enemy").Snapshot(), 0, out armor));
            Assert.AreEqual(95, armor);
            world.RemoveCharacter(drake);
            Assert.AreEqual(1, world.StatusOf("enemy").ActiveCount);
            Assert.IsTrue(Modifiers(world).TryResolveArmor(world.EnemyOf("enemy"),
                world.StatusOf("enemy").Snapshot(), 0, out armor));
            Assert.AreEqual(95, armor);
            world.RemoveCharacter(king);
            Assert.AreEqual(0, world.StatusOf("enemy").ActiveCount);
        }

        [Test]
        public void TwoAbilitiesOfOneCasterKeepIndependentRadiiAndUnverifiedStackingIsBlocked()
        {
            var world = World(new FlatTargets());
            long caster = world.SpawnCharacter("TR-19", 0, 0); Enemy(world, x: 1085);
            Assert.AreEqual(2, world.StatusOf("enemy").ActiveCount);
            double armor;
            Assert.IsFalse(Modifiers(world).TryResolveArmor(world.EnemyOf("enemy"),
                world.StatusOf("enemy").Snapshot(), 0, out armor), "Never guess 8 + 22 or strongest.");
            world.ForceMoveEnemy("enemy", 1085.01, 0, 0);
            Assert.AreEqual(1, world.StatusOf("enemy").ActiveCount);
            Assert.IsTrue(Modifiers(world).TryResolveArmor(world.EnemyOf("enemy"),
                world.StatusOf("enemy").Snapshot(), 0, out armor));
            Assert.AreEqual(92, armor);
            world.RemoveCharacter(caster);
            Assert.AreEqual(0, world.StatusOf("enemy").ActiveCount);
        }

        [Test]
        public void RejectedEnemyAndUnverifiedRanksDoNotReceiveOtherCandidates()
        {
            var targets = new FlatTargets { ExcludedEnemy = "immune" };
            targets.Levels["A0NR"] = 99;
            var world = World(targets); world.SpawnCharacter("E-12", 0, 0);
            Enemy(world); Enemy(world, "immune");
            Assert.AreEqual(0, world.StatusOf("enemy").ActiveCount);
            targets.Levels["A0NR"] = 2; world.Step(0);
            Assert.AreEqual(40, world.StatusOf("enemy").Snapshot()[0].Amount);
            Assert.AreEqual(0, world.StatusOf("immune").ActiveCount);
        }

        [Test]
        public void WrongVersionAndTamperedOwnershipNeverBecomeDefinitions()
        {
            string json = Data("original-2323C-all-unit-aura-audit.json");
            Assert.Throws<FormatException>(() => Original323ArmorAuraCatalog.FromJson(
                json.Replace("S2 2.323[C]", "S2 2.324"), Map()));
            Assert.Throws<FormatException>(() => Original323ArmorAuraCatalog.FromJson(
                json.Replace("\"unitRawcode\": \"h09X\"", "\"unitRawcode\": \"h003\""), Map()));
            Assert.Throws<FormatException>(() => Original323ArmorAuraCatalog.FromJson(
                json.Replace("\"safeForUnconditionalLiveCombat\": false", "\"safeForUnconditionalLiveCombat\": true"), Map()));
        }
    }
}
