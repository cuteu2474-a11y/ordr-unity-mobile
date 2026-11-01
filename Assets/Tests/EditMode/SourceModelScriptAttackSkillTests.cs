using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

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
        private sealed class MutableStoryLane : IOriginalStoryDispatchPort
        {
            public bool Deployed;
            public bool CanDispatch(long id) => false;
            public ManualActionResult Dispatch(long id) => null;
            public bool IsDeployed(long id) => Deployed;
            public bool CanRecall(long id) => false;
            public ManualActionResult Recall(long id) => null;
        }
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

        [TestCase("R-13", "A14X", .9)]
        [TestCase("R-26", "A14Y", .95)]
        [TestCase("R-30", "A150", 1.15)]
        [TestCase("R-31", "A151", 1.2)]
        [TestCase("H-16", "A156", 1.7)]
        public void FiveInfernoFollowupsApplyPinnedNativeStunNotExtraDamage(
            string unitId, string ability, double duration)
        {
            var spec = SourceModelLegacyNativeFollowupCatalog.All
                .Single(x => x.CatalogId == unitId);
            Assert.AreEqual(ability, spec.AbilityRawcode);
            Assert.AreEqual(duration, spec.NormalStunSeconds, 1e-9);
            Assert.AreEqual(.01, spec.ImpactDelaySeconds, 1e-9);
            using (var live = Create(unitId, true, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.Detail.Contains(ability) && e.Detail.Contains("기절")), unitId);
                Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.Detail.Contains(ability)), "AUin DataA1 is ZERO; native stun is not extra damage");
            }
        }

        [Test]
        public void NamiDummyPlacesAnActualFivePercentSlowZone()
        {
            using (var live = Create("S-03", true, -4900, 5400))
            {
                var events = live.Battle.Advance(2.5);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.Detail.Contains("e00K/A089")));
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(e =>
                    live.Combat.StatusOf(e.Id).Snapshot().Any(status =>
                        status.SourceId.StartsWith("practice-nami:A089:") &&
                        status.Type == CombatEffectType.MovementSlowPercent &&
                        status.Amount == 5)));
            }
        }

        [Test]
        public void SentinelAppliesFifteenNativeA04RLevelsAndSixtySecondFaerieFire()
        {
            using (var live = Create("RN-04", true, -4900, 5400))
            {
                var events = live.Battle.Advance(5);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.Detail.Contains("Bd8(A04R): 1→16")));
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.Detail.Contains("e0LE/A10I")));
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(e =>
                    live.Combat.StatusOf(e.Id).Snapshot().Any(effect =>
                        effect.SourceId == "practice-native:A10I" &&
                        effect.Type == CombatEffectType.ArmorReduction &&
                        effect.Amount == 50 && effect.DurationSeconds == 60)));
            }
        }

        [Test]
        public void CrackerVisualDummyAddsNoUnverifiedSecondDamageSource()
        {
            var cracker = SourceModelScriptAttackCatalog.All.Single(x => x.CatalogId == "L-33");
            Assert.IsNull(cracker.DeferredNativeEffect,
                "e04X contains only Aloc and Avul; no native combat damage");
            Assert.AreEqual(1, cracker.Always.Count);
            Assert.AreEqual(20000, cracker.Always[0].Damage);
            Assert.AreEqual(250000, cracker.OnProc.Single().Damage);
        }

        [Test]
        public void BonneyBaseAttackDeclaresA04PBeforeHitWithoutInventingConditionalProcs()
        {
            using (var live = Create("TR-21", true, -4900, 5400))
            {
                var events = live.Battle.Advance(5);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "보니 · A04P" && e.Detail.Contains("B4C/Bd2")));
                Assert.IsFalse(events.Any(e => e.SkillName == "보니 · A04P" &&
                    (e.Detail.Contains("SingleProc") || e.Detail.Contains("AreaProc"))));
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(e =>
                    live.Combat.StatusOf(e.Id).Snapshot().Any(effect =>
                        effect.SourceId == "practice-native:A04P" &&
                        effect.Type == CombatEffectType.ArmorReduction &&
                        effect.Amount >= 10 && effect.Amount <= 40)));
            }
        }

        [Test]
        public void DeferredZoroCircleCannotJumpAcrossStoryLaneOrBackOnReturn()
        {
            using (var live = Create("L-27", true, -4900, 5400))
            {
                var lane = new MutableStoryLane();
                live.Combat.ConfigureStoryDeployment(lane);
                long id = live.Combat.MobileUnitSnapshot()
                    .Single(x => x.Unit.Id == "L-27").InstanceId;
                bool declared = false;
                for (int i = 0; i < 1000; ++i)
                {
                    var events = live.Battle.Advance(.005);
                    if (!events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                        e.Detail.Contains("Bsk:10/100"))) continue;
                    Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                        e.Detail.Contains("Bsk/Bsj")));
                    declared = true;
                    break;
                }
                Assert.IsTrue(declared, "The source attack should declare Bsj before .04s");
                lane.Deployed = true;
                live.Combat.RefreshAfterCommittedStoryLaneChange(id);
                lane.Deployed = false;
                live.Combat.RefreshAfterCommittedStoryLaneChange(id);
                var after = live.Battle.Advance(.06);
                Assert.IsFalse(after.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.Detail.Contains("Bsk/Bsj")),
                    "A returning caster must not revive the old main-lane blast");
            }
        }

        [Test]
        public void SecondThirtyModelsHaveExactRosterAndOnlyWholeTooltipPackets()
        {
            var roster = OrdrJson.Object(OrdrJson.Parse(Data("unit-runtime-status.json")),
                "source roster");
            var actual = new Dictionary<string, string>();
            foreach (var value in OrdrJson.Array(OrdrJson.Value(roster, "entries"), "entries"))
            {
                var row = OrdrJson.Object(value, "unit");
                actual.Add(OrdrJson.RequiredString(row, "catalogId"),
                    OrdrJson.String(row, "originalRawcode"));
            }
            var firstThirty = OrdrJson.Object(OrdrJson.Parse(
                Data("original-2323C-next-30-runtime-coverage.json")), "first thirty");
            var previous = new HashSet<string>(OrdrJson.Array(
                OrdrJson.Value(firstThirty, "units"), "units")
                .Select(x => OrdrJson.RequiredString(OrdrJson.Object(x, "unit"), "id")));
            var profiles = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            Assert.AreEqual(30, SourceModelNextThirtySkillCatalog.All.Count);
            Assert.AreEqual(30, SourceModelNextThirtySkillCatalog.All
                .Select(x => x.CatalogId).Distinct().Count());
            int enabled = 0;
            foreach (var row in SourceModelNextThirtySkillCatalog.All)
            {
                Assert.IsFalse(previous.Contains(row.CatalogId), "Previous batch: " + row.CatalogId);
                Assert.AreEqual(actual[row.CatalogId], row.Rawcode);
                SourceModelNextThirtyUnit matched;
                Assert.IsTrue(SourceModelNextThirtySkillCatalog.TryGet(row.Rawcode, out matched));
                Assert.AreSame(row, matched);
                var profile = profiles.Require(row.CatalogId);
                foreach (string name in row.PracticeAttackSkills)
                {
                    var skill = profile.Skills.Single(x => x.Name == name);
                    Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill),
                        row.CatalogId + ": " + name);
                    enabled++;
                }
            }
            Assert.AreEqual(16, enabled, "Only fully parsed tooltip packets are allowed");
        }

        [TestCase("L-04", "바람바람 열매")]
        [TestCase("L-13", "배리어 볼")]
        [TestCase("L-15", "왼손은 수면제")]
        [TestCase("TR-02", "미라쥬 템포")]
        [TestCase("TR-04", "범위 마법 스턴")]
        [TestCase("H-04", "숲 물리")]
        [TestCase("H-22", "마리조아붕괴")]
        [TestCase("E-01", "혼합 광역 타격")]
        [TestCase("E-04", "매 타격 추가 마법")]
        [TestCase("E-08", "범위 물리")]
        [TestCase("I-04", "광역 물리 타격")]
        public void SecondThirtySupportedTooltipAttacksExecuteInPracticeOnly(
            string unitId, string name)
        {
            using (var live = Create(unitId, true, -4900, 5400))
            {
                var events = live.Battle.Advance(10);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == name && e.Detail.Contains("TOOLTIP_MODEL_ONLY")), unitId);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == name && e.Amount > 0), unitId);
            }
        }

        [Test]
        public void SecondThirtyUnverifiedFormAndBossSkillsRemainDeferred()
        {
            foreach (var unit in new[] { "L-03", "TR-23", "TR-30", "I-06", "I-11" })
            {
                using (var live = Create(unit, true, -4900, 5400))
                {
                    var events = live.Battle.Advance(6);
                    Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.EffectDeferred &&
                        e.Detail.Contains("도감") && e.Detail.Contains("실행하지 않음")), unit);
                    Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                        e.Detail.Contains("TOOLTIP_MODEL_ONLY")), unit);
                }
            }
        }

        [Test]
        public void SecondThirtyFailedProcNeverAppliesTooltipDamage()
        {
            using (var live = Create("L-13", false, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsFalse(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "배리어 볼"));
            }
        }
    }
}
