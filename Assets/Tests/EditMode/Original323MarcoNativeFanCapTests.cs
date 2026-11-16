using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323MarcoNativeFanCapTests
    {
        private sealed class ForcedRoll : ICombatProcRandom
        {
            public int Next(int upperExclusive) => 0;
        }

        private sealed class NoGold : IOriginalWispRandom
        {
            public int NextInclusive(int min, int max) => max;
        }

        private static string Data(string name) =>
            File.ReadAllText(Path.Combine(Application.dataPath,
                "..", "data", name));

        [Test]
        public void MarcoObjectA08LUsesTheVerifiedSevenTargetBound()
        {
            SourceScriptAttackSpec marco;
            Assert.IsTrue(SourceModelNextAttackCatalog.TryGet("h00T", out marco));
            Assert.AreEqual("S-08", marco.CatalogId);
            Assert.AreEqual("Bn0", marco.JassFunction);
            Assert.AreEqual(100, marco.RollSides);
            Assert.AreEqual(10, marco.RollSuccess);
            var fan = marco.OnProc.Single();
            Assert.AreEqual(SourceScriptHitShape.AttackerCircle, fan.Shape);
            Assert.AreEqual(1500, fan.Damage);
            Assert.AreEqual(1150, fan.Radius);
            Assert.AreEqual(7, fan.MaxTargets);
            Assert.AreEqual(Ordr323AttackType.Spells, fan.Attack);

            SourceScriptAttackSpec squard;
            Assert.IsTrue(SourceModelNextAttackCatalog.TryGet("h012", out squard));
            Assert.AreEqual(0, squard.OnProc.Single().MaxTargets,
                "Unrelated circular spells remain uncapped");
        }

        [Test]
        public void MarcoFanOfKnivesHitsOnlySevenDistinctInRadiusEnemies()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(
                Data("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var roll = new ForcedRoll();
            var rules = new SourceModelBasicCombatRules(map, roll);
            var combat = new CharacterCombatRuntime(units, skills,
                rules, rules, roll);
            var shared = new SharedEconomySession(
                new EconomyWallet(), combat.Roster);
            shared.ApplyVerifiedStart(Original323SoloStartProfile.Create(),
                (id, x, y) => shared.Units.Add(id, x, y),
                id => shared.Units.Remove(id));
            combat.Start();
            rules.Enable();
            combat.ConfigureSourceModelBasicAttacks(rules);
            combat.SpawnCharacter("S-08", -4900, 5400);
            string practice = Data("mobile-practice-battle-model.json")
                .Replace("\"health\": 338,", "\"health\": 1000000000000,");
            using (var battle = new SourceModelPracticeBattle(
                combat, new CombatWaveDirector(), shared,
                new SourceModelPracticeCatalog(practice, 10),
                Original323MonsterWaveIdentityCatalog.FromJson(
                    Data("original-2323C-monster-wave-script-evidence.json")),
                rules, new NoGold(), null))
            {
                battle.Start();
                // Ten eligible enemies near the caster, all too durable
                // to die from a single fan. One is beyond Area1=1150.
                for (int i = 0; i < 10; i++)
                    combat.SpawnEnemyWithDefenses("marco-fan-" + i, 1000000,
                        CharacterEnemyKind.Line, -4750 + 40 * i, 5400, 0,
                        new EnemyDefenseProfile(CharacterEnemyKind.Line, 0,
                            null, "normal", "TEST_ONLY A08L target eligibility"));
                combat.SpawnEnemyWithDefenses(
                    "marco-fan-outside", 1000000, CharacterEnemyKind.Line,
                    -3700, 5400, 0,
                    new EnemyDefenseProfile(CharacterEnemyKind.Line, 0,
                        null, "normal", "TEST_ONLY outside 1150"));
                CharacterCombatEvent[] fanHits = null;
                for (int step = 0; step < 100; step++)
                {
                    var events = battle.Advance(.05);
                    if (!events.Any(e =>
                        e.Type == CharacterCombatEventType.SkillActivated &&
                        e.Detail.Contains("Bn0"))) continue;
                    fanHits = events.Where(e =>
                        e.Type == CharacterCombatEventType.SkillDamage &&
                        e.Detail.Contains("Bn0")).ToArray();
                    break;
                }
                Assert.IsNotNull(fanHits, "Forced native proc should activate");
                Assert.AreEqual(7, fanHits.Length,
                    "A08L DataC1=7 is the maximum total victim count");
                Assert.AreEqual(7,
                    fanHits.Select(e => e.EnemyId).Distinct().Count());
                Assert.IsFalse(fanHits.Any(e =>
                    e.EnemyId == "marco-fan-outside"));
                Assert.AreEqual(1000000L, combat.MobileEnemySnapshot()
                    .Single(e => e.Id == "marco-fan-outside").Health);
            }
        }
    }
}
