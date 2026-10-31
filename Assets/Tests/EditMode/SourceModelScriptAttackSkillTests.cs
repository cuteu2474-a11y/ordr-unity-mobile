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

        private static Live Create(string unitId, bool trigger,
            double x = -4000, double y = 4000, bool enormousHealth = true)
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
            combat.SpawnCharacter(unitId, x, y);
            string practice = Data("mobile-practice-battle-model.json");
            if (enormousHealth)
                practice = practice.Replace("\"health\": 338,",
                    "\"health\": 1000000000000,");
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

        [Test]
        public void AdditionalSixCallbacksMatchPinnedMapAndRemainModelOnly()
        {
            var root = OrdrJson.Object(OrdrJson.Parse(
                Data("original-2323C-next-20-native-source-audit.json")), "next 20 audit");
            Assert.AreEqual(SourceModelNextAttackCatalog.SourceJassSha256,
                OrdrJson.RequiredString(root, "sourceJassSha256"));
            Assert.AreEqual(false, OrdrJson.Value(root, "runtimeAuthorization"));
            Assert.AreEqual(6, SourceModelNextAttackCatalog.All.Count);
            foreach (var raw in OrdrJson.Array(OrdrJson.Value(root, "units"), "units"))
            {
                var row = OrdrJson.Object(raw, "unit");
                var callbacks = OrdrJson.Array(OrdrJson.Value(row, "w5AttackCallbackEvidence"),
                    "registered callbacks");
                if (callbacks.Count == 0) continue;
                Assert.AreEqual(1, callbacks.Count);
                var callback = OrdrJson.Object(callbacks[0], "callback");
                SourceScriptAttackSpec spec;
                Assert.IsTrue(SourceModelNextAttackCatalog.TryGet(
                    OrdrJson.RequiredString(row, "originalUnitRawcode"), out spec));
                Assert.AreEqual(OrdrJson.RequiredString(row, "catalogId"), spec.CatalogId);
                Assert.AreEqual(OrdrJson.RequiredString(callback, "function"), spec.JassFunction);
                Assert.AreEqual(OrdrJson.Number(callback, "attackEventChancePercent"),
                    100.0 * spec.RollSuccess / spec.RollSides);
                Assert.IsFalse(string.IsNullOrWhiteSpace(spec.DeferredNativeEffect));
            }
        }

        [TestCase("S-08")][TestCase("S-12")][TestCase("S-15")]
        [TestCase("S-18")][TestCase("S-19")][TestCase("S-22")]
        public void SixAdditionalSkillBranchesActuallyFireInCombat(string unitId)
        {
            using (var live = Create(unitId, true, -4900, 5400))
            {
                var source = SourceModelNextAttackCatalog.All.Single(x => x.CatalogId == unitId);
                var events = live.Battle.Advance(10);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.Detail.Contains(source.JassFunction)), unitId);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.EffectDeferred &&
                    e.Detail.Contains(source.DeferredNativeEffect)), unitId);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.Detail.Contains(source.JassFunction)), unitId);
            }
        }

        [Test]
        public void SquardDirectJassAreaDamageOccursAfterSourcePointTwoNineDelay()
        {
            using (var live = Create("S-15", true, -4900, 5400))
            {
                bool activated = false;
                for (int i = 0; i < 150; i++)
                {
                    var events = live.Battle.Advance(.05);
                    if (!events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                        e.Detail.Contains("BoC"))) continue;
                    Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                        e.Detail.Contains("BoC")));
                    activated = true;
                    break;
                }
                Assert.IsTrue(activated, "First attack should dispatch BoC");
                // The declaration may occur part-way through the .05 step:
                // the .29 second hit must not be visible after just .20s.
                var before = live.Battle.Advance(.20);
                Assert.IsFalse(before.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.Detail.Contains("BoC")));
                var after = live.Battle.Advance(.15);
                Assert.IsTrue(after.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.Detail.Contains("BoC")));
            }
        }

        [TestCase("S-05", "언홀리 프렌지")]
        [TestCase("S-20", "리퀴드 파이어")]
        [TestCase("S-28", "피닉스 파이어")]
        public void SourceNativeBuffAndBurnActuallyActivate(string unitId, string ability)
        {
            using (var live = Create(unitId, true, -4900, 5400))
            {
                var events = live.Battle.Advance(8);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName != null && e.SkillName.Contains(ability)), unitId);
                if (unitId == "S-20" || unitId == "S-28")
                    Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                        e.Detail.Contains(unitId == "S-20" ? "Aliq" : "A0EA")),
                        "At least one native object modeled damage tick");
            }
        }

        [Test]
        public void MoriaBlackArrowCreatesOneTemporaryRealZombieOnKillingBlow()
        {
            using (var live = Create("S-09", true, -4900, 5400, false))
            {
                var events = live.Battle.Advance(2);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.Detail.Contains("A087/ANba")));
                var zombie = live.Combat.MobileUnitSnapshot()
                    .FirstOrDefault(x => x.Unit.Id == "U-14");
                Assert.IsNotNull(zombie, "Summon must be an attack-capable h00H unit");
                long firstZombieId = zombie.InstanceId;
                live.Battle.Advance(11);
                Assert.IsFalse(live.Combat.MobileUnitSnapshot().Any(x =>
                    x.InstanceId == firstZombieId), "DataC1=10 lifetime must expire");
            }
        }
    }
}
