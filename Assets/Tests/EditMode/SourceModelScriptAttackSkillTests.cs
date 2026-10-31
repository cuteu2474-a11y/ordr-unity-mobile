using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceModelScriptAttackSkillTests
    {
        private sealed class Dice : ICombatProcRandom
        {
            private readonly bool trigger;
            public Dice(bool trigger) { this.trigger = trigger; }
            public int Next(int bound) => trigger ? 0 : bound - 1;
        }
        private sealed class NoGold : IOriginalWispRandom
        { public int NextInclusive(int min, int max) => max; }
        private sealed class Live : IDisposable
        {
            public CharacterCombatRuntime Combat;
            public SourceModelPracticeBattle Battle;
            public void Dispose() { Battle.Dispose(); }
        }
        private static string Data(string path) =>
            File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", path));

        private static Live Create(string unitId, bool trigger)
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var dice = new Dice(trigger);
            var rules = new SourceModelBasicCombatRules(map, dice);
            var combat = new CharacterCombatRuntime(units, skills, rules, rules, dice);
            var shared = new SharedEconomySession(new EconomyWallet(), combat.Roster);
            shared.ApplyVerifiedStart(Original323SoloStartProfile.Create(),
                (id, x, y) => shared.Units.Add(id, x, y), id => shared.Units.Remove(id));
            combat.Start(); rules.Enable(); combat.ConfigureSourceModelBasicAttacks(rules);
            combat.SpawnCharacter(unitId, -4000, 4000);
            string practice = Data("mobile-practice-battle-model.json")
                .Replace("\"health\": 338,", "\"health\": 1000000000000,");
            var battle = new SourceModelPracticeBattle(combat, new CombatWaveDirector(),
                shared, new SourceModelPracticeCatalog(practice, 10),
                Original323MonsterWaveIdentityCatalog.FromJson(
                    Data("original-2323C-monster-wave-script-evidence.json")),
                rules, new NoGold(), null);
            battle.Start();
            return new Live { Combat = combat, Battle = battle };
        }

        [Test]
        public void ExactTwentyCallbacksMatchSourceAuditAndAreModelOnly()
        {
            var root = OrdrJson.Object(OrdrJson.Parse(
                Data("original-2323C-attack-skill-batch.json")), "attack audit");
            Assert.AreEqual(SourceModelScriptAttackCatalog.SourceJassSha256,
                OrdrJson.RequiredString(root, "sourceJassSha256"));
            Assert.AreEqual(false, OrdrJson.Value(root, "runtimeAuthorization"));
            var audited = OrdrJson.Array(OrdrJson.Value(root, "units"), "units");
            Assert.AreEqual(20, SourceModelScriptAttackCatalog.All.Count);
            Assert.AreEqual(audited.Count, SourceModelScriptAttackCatalog.All.Count);
            foreach (var source in audited)
            {
                var row = OrdrJson.Object(source, "source attack");
                var cb = OrdrJson.Object(OrdrJson.Value(row, "callback"), "callback");
                string raw = OrdrJson.RequiredString(row, "rawcode");
                SourceScriptAttackSpec spec;
                Assert.IsTrue(SourceModelScriptAttackCatalog.TryGet(raw, out spec), raw);
                Assert.AreEqual(OrdrJson.RequiredString(row, "catalogId"), spec.CatalogId);
                Assert.AreEqual(OrdrJson.RequiredString(cb, "function"), spec.JassFunction);
                Assert.IsFalse(spec.OnProc.Count == 0);
            }
        }

        [TestCase("U-04")][TestCase("U-13")][TestCase("S-03")][TestCase("S-13")]
        [TestCase("R-11")][TestCase("R-12")][TestCase("R-13")][TestCase("R-21")]
        [TestCase("R-23")][TestCase("R-26")][TestCase("R-30")][TestCase("R-31")]
        [TestCase("R-35")][TestCase("R-38")][TestCase("H-11")][TestCase("H-16")]
        [TestCase("H-18")][TestCase("L-29")][TestCase("L-33")][TestCase("RN-04")]
        public void SourceAttackCallbackIsInvokedInRealPracticeCombat(string unitId)
        {
            using (var live = Create(unitId, true))
            {
                var source = SourceModelScriptAttackCatalog.All.Single(x => x.CatalogId == unitId);
                var events = live.Battle.Advance(10);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.Attack), unitId);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                    x.Detail.Contains(source.JassFunction)), unitId);
                // Native spell follow-ups remain explicitly deferred, not
                // silently claimed to have spawned real Warcraft actors.
                if (source.DeferredNativeEffect != null)
                    Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.EffectDeferred &&
                        x.Detail.Contains(source.DeferredNativeEffect)), unitId);
            }
        }

        [Test]
        public void FailedProcDoesNotGrantSkillDamageButCrackerAlwaysHits()
        {
            using (var ordinary = Create("R-21", false))
            {
                var events = ordinary.Battle.Advance(5);
                Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.Detail.Contains("BpM")));
            }
            using (var cracker = Create("L-33", false))
            {
                var events = cracker.Battle.Advance(5);
                Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.Detail.Contains("Btd")));
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.Detail.Contains("Btd")));
            }
        }
    }
}
