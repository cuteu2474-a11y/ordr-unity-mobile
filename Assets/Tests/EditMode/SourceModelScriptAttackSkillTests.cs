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
            public bool Trigger;
            public Dice(bool trigger) { Trigger = trigger; }
            public int Next(int bound) => Trigger ? 0 : bound - 1;
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
            public long ActorId;
            public Dice Rolls;
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
            long actorId = combat.SpawnCharacter(unitId, x, y);
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
            return new Live { Combat = combat, Battle = battle,
                ActorId = actorId, Rolls = dice };
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
            int enabled = 0, activeEnabled = 0;
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
                foreach (string name in row.PracticeActiveSkills)
                {
                    var skill = profile.Skills.Single(x => x.Name == name);
                    Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill),
                        row.CatalogId + ": " + name);
                    activeEnabled++;
                }
            }
            Assert.AreEqual(29, enabled, "Only whole attack tooltip packets are allowed");
            Assert.AreEqual(7, activeEnabled, "Resource-gated, whole active packets only");
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
        [TestCase("L-03", "전격")]
        [TestCase("L-07", "명왕의 검술")]
        [TestCase("L-13", "배리어 피스트")]
        [TestCase("L-15", "타격 스턴")]
        [TestCase("L-20", "화이트블로우")]
        [TestCase("H-02", "보스·광폭화 함포")]
        [TestCase("H-12", "용의 발톱")]
        [TestCase("TR-09", "광역 3연타")]
        [TestCase("TR-10", "타격 스턴")]
        [TestCase("E-04", "연속 범위 타격")]
        [TestCase("E-08", "단일 스턴")]
        [TestCase("I-04", "타격·단일 스턴")]
        public void SecondThirtySupportedTooltipAttacksExecuteInPracticeOnly(
            string unitId, string name)
        {
            using (var live = Create(unitId, true, -4900, 5400))
            {
                var events = live.Battle.Advance(10);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == name && e.Detail.Contains("TOOLTIP_MODEL_ONLY")), unitId);
                if (name != "타격 스턴" && name != "단일 스턴")
                    Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                        e.SkillName == name && e.Amount > 0), unitId);
                else
                    Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.StatusApplied &&
                        e.SkillName == name), unitId);
            }
        }

        [TestCase("H-07", "갑판 포격", "H-07:skill:0", 95, false)]
        [TestCase("TR-04", "체력 소모 광역 공격", "TR-04:skill:0", 40, true)]
        [TestCase("TR-09", "마나 광역 공격", "TR-09:skill:1", 100, false)]
        [TestCase("TR-14", "킹코브라", "TR-14:skill:0", 85, false)]
        [TestCase("E-01", "마나 광역기", "E-01:skill:1", 150, false)]
        [TestCase("E-04", "마나 고정 피해", "E-04:skill:0", 175, false)]
        [TestCase("E-08", "마나 광역 스턴", "E-08:skill:0", 145, false)]
        public void SourceModelActiveConsumesOnlyConfiguredResourcesAndHits(
            string unitId, string name, string skillId, int cost, bool healthCost)
        {
            using (var live = Create(unitId, false, -4900, 5400))
            {
                live.Battle.Advance(2);
                var target = live.Combat.MobileEnemySnapshot().First();
                Assert.AreEqual(AbilityCastResult.NoResourceState,
                    live.Combat.TryCastActiveSkill(live.ActorId, skillId, target.Id));
                var resources = new CharacterResources(1000, 1000, 1000, 1000);
                live.Combat.ConfigureCharacterResources(live.ActorId, resources);
                Assert.AreEqual(AbilityCastResult.MissingTarget,
                    live.Combat.TryCastActiveSkill(live.ActorId, skillId, "missing"));
                Assert.AreEqual(AbilityCastResult.Cast,
                    live.Combat.TryCastActiveSkill(live.ActorId, skillId, target.Id));
                Assert.AreEqual(healthCost ? 1000 - cost : 1000, resources.Health);
                Assert.AreEqual(healthCost ? 1000 : 1000 - cost, resources.Mana);
                Assert.IsTrue(live.Combat.LastEvents.Any(x =>
                    x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == name && x.Amount > 0), unitId);
            }
        }

        [Test]
        public void SecondThirtyUnverifiedFormAndBossSkillsRemainDeferred()
        {
            foreach (var unit in new[] { "TR-23", "TR-30", "I-06", "I-11" })
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
        public void SecondThirtyIncompleteActivePacketsCannotSpendResources()
        {
            var profiles = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            foreach (var entry in new[] {
                new[] { "TR-23", "럼블볼 폭주" },
                new[] { "E-01", "거대화" },
                new[] { "TR-30", "베가펑크 마나 공격" }
            })
            {
                var skill = profiles.Require(entry[0]).Skills.Single(x => x.Name == entry[1]);
                Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill),
                    entry[0] + ": " + entry[1]);
                var row = SourceModelNextThirtySkillCatalog.All.Single(x => x.CatalogId == entry[0]);
                Assert.IsFalse(row.AllowsActive(skill.Name));
            }
        }

        [Test]
        public void SecondThirtyModelablePacketDoesNotGrantCastPermission()
        {
            var profiles = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var skill = profiles.Require("I-11").Skills.Single(
                x => x.Name == "체력 소모 광역 공격");
            // The I-11 tooltip has a parsable damage packet, but no reviewed
            // authorization in the unit's practice active-skill allow-list.
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill));
            Assert.IsFalse(SourceModelNextThirtySkillCatalog.All.Single(
                x => x.CatalogId == "I-11").AllowsActive(skill.Name));
        }

        [TestCase("TR-23", "TR-23:skill:0", "럼블볼 폭주")]
        [TestCase("E-01", "E-01:skill:0", "거대화")]
        [TestCase("TR-30", "TR-30:skill:0", "베가펑크 마나 공격")]
        [TestCase("I-11", "I-11:skill:0", "체력 소모 광역 공격")]
        [TestCase("TR-09", "TR-09:skill:0", "체력 소모 연타")]
        [TestCase("I-04", "I-04:skill:0", "광역 혼합 피해")]
        [TestCase("L-07", "L-07:skill:0", "패기발산")]
        [TestCase("I-06", "I-06:skill:0", "대자재천신")]
        public void SecondThirtyUnapprovedActiveSkillCannotSpendThroughGenericFallback(
            string unitId, string skillId, string skillName)
        {
            using (var live = Create(unitId, false, -4900, 5400))
            {
                live.Battle.Advance(2);
                var target = live.Combat.MobileEnemySnapshot().First();
                var resources = new CharacterResources(1000, 1000, 1000, 1000);
                live.Combat.ConfigureCharacterResources(live.ActorId, resources);
                Assert.AreEqual(AbilityCastResult.UnsupportedEffect,
                    live.Combat.TryCastActiveSkill(live.ActorId, skillId, target.Id), unitId);
                Assert.AreEqual(1000, resources.Health, unitId);
                Assert.AreEqual(1000, resources.Mana, unitId);
                Assert.IsFalse(live.Combat.LastEvents.Any(x =>
                    x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == skillName), unitId);
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

        [Test]
        public void TemporaryFormCatalogAcceptsOnlyCompleteStandalonePackets()
        {
            var profiles = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            SourceModelFormSpec form;
            Assert.IsTrue(SourceModelTemporarySkillCatalog.TryReadCompleteForm(
                profiles.Require("TR-14").Skills.Single(x => x.Name == "블랙 맘바 각성"), out form));
            Assert.AreEqual(8.5, form.Duration);
            Assert.AreEqual(12500, form.FlatAttackPower);
            Assert.AreEqual(.03, form.AttackIntervalReduction);
            Assert.IsTrue(SourceModelTemporarySkillCatalog.TryReadCompleteForm(
                profiles.Require("TR-18").Skills.Single(x => x.Name == "인수화"), out form));
            Assert.AreEqual(75000, form.FlatAttackPower);
            Assert.AreEqual(.07, form.AttackIntervalReduction);
            Assert.IsFalse(SourceModelTemporarySkillCatalog.TryReadCompleteForm(
                profiles.Require("TR-23").Skills.Single(x => x.Name == "럼블볼 폭주"), out form));
            Assert.IsFalse(SourceModelTemporarySkillCatalog.TryReadCompleteForm(
                profiles.Require("E-01").Skills.Single(x => x.Name == "거대화"), out form));
            double damage, radius;
            Assert.IsTrue(SourceModelTemporarySkillCatalog.TryReadClonePulse(
                profiles.Require("I-03").Skills.Single(x => x.Name == "분신"),
                out damage, out radius));
            Assert.AreEqual(100000, damage);
            Assert.AreEqual(400, radius);
            string name;
            double duration, stun;
            Assert.IsTrue(SourceModelTemporarySkillCatalog.TryReadTimedClone(
                profiles.Require("L-21").Skills.Single(x => x.Name == "인술 분신"),
                out name, out duration, out stun));
            Assert.AreEqual("분신", name);
            Assert.AreEqual(2, duration);
            Assert.AreEqual(1.5, stun);
        }

        [Test]
        public void SnakeFormUsesSameOwnedUnitAndExpiresWithoutRecreatingIt()
        {
            using (var live = Create("TR-14", true, -4900, 5400))
            {
                int owned = live.Combat.CharacterCount;
                var events = new List<CharacterCombatEvent>();
                for (int tick = 0; tick < 100 &&
                    live.Combat.PracticeFormOf(live.ActorId) == null; tick++)
                    events.AddRange(live.Battle.Advance(.2));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "블랙 맘바 각성"));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.StatusApplied &&
                    x.SkillName == "블랙 맘바 각성"));
                Assert.AreEqual("blackMamba", live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(owned, live.Combat.CharacterCount);
                var status = live.Combat.UnitStatusOf(live.ActorId).Snapshot();
                Assert.AreEqual(12500, SourceModelTemporarySkillCatalog.FlatFormPower(status));
                Assert.AreEqual(.03, SourceModelTemporarySkillCatalog.FormIntervalReduction(status));
                live.Rolls.Trigger = false;
                live.Battle.Advance(15);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(owned, live.Combat.CharacterCount);
            }
        }

        [Test]
        public void FailedSnakeFormRollDoesNotApplyAnyBuff()
        {
            using (var live = Create("TR-14", false, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsFalse(events.Any(x => x.SkillName == "블랙 맘바 각성" &&
                    x.Type == CharacterCombatEventType.StatusApplied));
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
            }
        }

        [Test]
        public void PendingSnakeFormDoesNotReturnAcrossStoryDispatchAndRecall()
        {
            using (var live = Create("TR-14", true, -4900, 5400))
            {
                var lane = new MutableStoryLane();
                live.Combat.ConfigureStoryDeployment(lane);
                bool castStarted = false;
                for (int tick = 0; tick < 100; tick++)
                {
                    var events = live.Battle.Advance(.05);
                    if (!events.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                        x.SkillName == "블랙 맘바 각성")) continue;
                    castStarted = true;
                    break;
                }
                Assert.IsTrue(castStarted);
                live.Rolls.Trigger = false;
                lane.Deployed = true;
                live.Combat.RefreshAfterCommittedStoryLaneChange(live.ActorId);
                lane.Deployed = false;
                live.Combat.RefreshAfterCommittedStoryLaneChange(live.ActorId);
                var after = live.Battle.Advance(3);
                Assert.IsFalse(after.Any(x => x.Type == CharacterCombatEventType.StatusApplied &&
                    x.SkillName == "블랙 맘바 각성"));
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
            }
        }

        [Test]
        public void YamatoFormSpendsConfiguredHealthOnceAndReverts()
        {
            using (var live = Create("TR-18", false, -4900, 5400))
            {
                var resources = new CharacterResources(1000, 1000, 0, 0);
                live.Combat.ConfigureCharacterResources(live.ActorId, resources);
                Assert.AreEqual(AbilityCastResult.Cast,
                    live.Combat.TryCastActiveSkill(live.ActorId, "TR-18:skill:0", null));
                Assert.AreEqual(905, resources.Health);
                Assert.AreEqual("beast", live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(75000, SourceModelTemporarySkillCatalog.FlatFormPower(
                    live.Combat.UnitStatusOf(live.ActorId).Snapshot()));
                live.Battle.Advance(4);
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
                Assert.AreEqual(905, resources.Health);
            }
        }

        [Test]
        public void FormWithoutResourceStateCannotSpendHealthOrActivate()
        {
            using (var live = Create("TR-18", true, -4900, 5400))
            {
                Assert.AreEqual(AbilityCastResult.NoResourceState,
                    live.Combat.TryCastActiveSkill(live.ActorId, "TR-18:skill:0", null));
                Assert.IsNull(live.Combat.PracticeFormOf(live.ActorId));
            }
        }

        [Test]
        public void RayleighClonePulseUsesActualCombatDamageAndProcRoll()
        {
            using (var live = Create("I-03", true, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "분신"));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.Detail.Contains("clone-proxy") && x.Amount > 0));
            }
            using (var live = Create("I-03", false, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsFalse(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.Detail.Contains("clone-proxy")));
            }
        }

        [Test]
        public void ShinobuCloneKeepsOwnerAndDoesNotInventIndependentAttacks()
        {
            using (var live = Create("L-21", true, -4900, 5400))
            {
                int owned = live.Combat.CharacterCount;
                var events = new List<CharacterCombatEvent>();
                for (int tick = 0; tick < 100 &&
                    live.Combat.PracticeSummonsOf(live.ActorId).Count == 0; tick++)
                    events.AddRange(live.Battle.Advance(.2));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "인술 분신"));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.EffectDeferred &&
                    x.Detail.Contains("분신 기본 공격 횟수")));
                Assert.AreEqual(owned, live.Combat.CharacterCount);
                var summons = live.Combat.PracticeSummonsOf(live.ActorId);
                Assert.IsTrue(summons.Count > 0);
                Assert.IsTrue(summons.All(x => x.OwnerId == live.ActorId &&
                    x.Name == "분신" && !x.HasIndependentCombat));
                live.Combat.RemoveCharacter(live.ActorId);
                Assert.AreEqual(0, live.Combat.PracticeSummonsOf(live.ActorId).Count);
            }
        }
    }
}
