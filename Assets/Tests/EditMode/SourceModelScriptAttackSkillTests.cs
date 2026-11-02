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
            public bool ForceSugarSuicide;
            public bool BlockKaidoMorph;
            public bool ForceKaidoSecondBranch;
            public Dice(bool trigger) { Trigger = trigger; }
            public int Next(int bound) => ForceSugarSuicide && bound == 1000
                ? 9 : ForceKaidoSecondBranch && bound == 100
                    ? 14 : BlockKaidoMorph && bound == 100
                    ? 99 : Trigger ? 0 : bound - 1;
        }
        private sealed class NoGold : IOriginalWispRandom
        { public int NextInclusive(int min, int max) => max; }
        private sealed class MutableStoryLane : IOriginalStoryDispatchPort
        {
            public bool Deployed;
            public readonly HashSet<long> DeployedIds = new HashSet<long>();
            public bool CanDispatch(long id) => false;
            public ManualActionResult Dispatch(long id) => null;
            public bool IsDeployed(long id) => Deployed || DeployedIds.Contains(id);
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
            double x = -4000, double y = 4000, bool enormousHealth = true,
            bool withNativeAuras = false)
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            // Pinned W3X stats are validated against the source 277 catalog
            // before the nine source/wikilink identities are overlaid.
            if (unitId == "MY-14" || unitId == "TR-31" || unitId == "R-44")
                units.ApplyWikiExtension(Data("wiki-unit-catalog-extension.json"));
            var dice = new Dice(trigger);
            var rules = new SourceModelBasicCombatRules(map, dice);
            if (withNativeAuras)
            {
                string audit = Data("original-2323C-all-unit-aura-audit.json");
                var six = new Original323SpecialAuraCatalog(map);
                var combined = new Original323CompositeAuraCatalog(map,
                    Original323ArmorAuraCatalog.FromJson(audit, map, six),
                    Original323NativeSpeedAuraCatalog.FromJson(audit, map),
                    Original323NativeCommandAuraCatalog.FromJson(audit, map));
                rules.ConfigureUnitEffects(new SourceModelUnitEffects(
                    Original323NativePassiveCatalog.FromJson(
                        Data("original-2323C-native-pure-passives.json"), map),
                    combined, dice, Data("practice-native-inherited-defaults.json"),
                    Original323MapObjectAuraCatalog.FromJson(audit, map)));
            }
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
        public void NewMysticHanmaUsesOriginalObjectAttackInsteadOfEmptyShell()
        {
            var source = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"),
                CharacterUnitCatalog.FromJson(Data("units.json")));
            var original = source.FindMapObjectAttack("h0C2");
            Assert.NotNull(original);
            Assert.AreEqual(152501d, original.MinimumBaseDamage.Value);
            Assert.AreEqual(152504d, original.MaximumBaseDamage.Value);
            Assert.AreEqual(0.71d, original.AttackCooldownSeconds.Value, 0.00001d);
            using (var live = Create("MY-14", true))
            {
                var status = live.Combat.InspectBasicAttack(live.ActorId);
                Assert.AreNotEqual(BasicAttackReadinessKind.UnverifiedSource, status.Kind);
                var results = live.Battle.Advance(5);
                Assert.IsTrue(results.Any(x => x.Type == CharacterCombatEventType.Attack),
                    "Hanma's original source attack must reach the normal battle pipeline");
            }
        }

        [Test]
        public void WikiSupportDoesNotInventDamageAndFormsCannotSpawnAlone()
        {
            using (var live = Create("R-44", true))
            {
                Assert.AreEqual(BasicAttackReadinessKind.UnverifiedSource,
                    live.Combat.InspectBasicAttack(live.ActorId).Kind);
            }
            Assert.Throws<InvalidOperationException>(() => Create("TR-31", true),
                "The extra Zoro form must never become a free independent unit");
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
                    Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanCastApprovedActive(
                        row.CatalogId, row.PracticeActiveSkills, skill));
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
        public void SourceSlkBaselineMatchesAllSevenPreviouslyUnusableActivePools()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var resourceCatalog = SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                Data("original-2323C-unit-object-stats.json"), map);
            Assert.Greater(resourceCatalog.Count, 200);
            var expected = new[] {
                new object[] { "H-07", "h03R", 10d, 95d, 0d, 0d },
                new object[] { "TR-04", "H098", 40d, 0d, 0.5d, 0d },
                new object[] { "TR-09", "H094", 40d, 100d, 0.33d, 0d },
                new object[] { "TR-14", "H0B2", 10d, 85d, 0d, 0d },
                new object[] { "E-01", "H0BK", 115d, 150d, 0.25d, 0d },
                new object[] { "E-04", "h058", 10d, 175d, 0d, 2d },
                new object[] { "E-08", "h08R", 10d, 145d, 0d, 0d },
                new object[] { "TR-18", "H0BD", 95d, 0d, 0.33d, 0d }
            };
            foreach (var row in expected)
            {
                var source = resourceCatalog.Find((string)row[0]);
                Assert.IsNotNull(source, (string)row[0]);
                Assert.AreEqual(row[1], source.OriginalRawcode);
                Assert.AreEqual((double)row[2], source.MaximumHealth, 1e-9);
                Assert.AreEqual((double)row[3], source.MaximumMana, 1e-9);
                Assert.AreEqual((double)row[4], source.HealthRegeneration, 1e-9);
                Assert.AreEqual((double)row[5], source.ManaRegeneration, 1e-9);
            }
            Assert.Throws<InvalidOperationException>(() =>
                SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                    Data("original-2323C-unit-object-stats.json")
                        .Replace("0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5",
                            "not-source-matched"), map));
        }

        [Test]
        public void FullCostHealthIsSpendableOnlyInExplicitSourceMode()
        {
            var verified = new CharacterResources(40, 40, 0, 0);
            Assert.IsFalse(verified.CanSpend(40, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => verified.SetHealth(0));
            var source = new CharacterResources(40, 40, 0, 0, true);
            Assert.IsTrue(source.CanSpend(40, 0));
            source.Spend(40, 0);
            Assert.AreEqual(0, source.Health);
            Assert.IsFalse(source.CanSpend(1, 0));
            source.RegenerateSourceModel(0.5, 0, 2);
            Assert.AreEqual(1, source.Health, 1e-9);
            source.RegenerateSourceModel(0.5, 0, 500);
            Assert.AreEqual(40, source.Health);
            Assert.Throws<InvalidOperationException>(() =>
                verified.RegenerateSourceModel(0.5, 0, 1));
        }

        [TestCase("H-07", "갑판 포격", 95d, false)]
        [TestCase("TR-04", "체력 소모 광역 공격", 40d, true)]
        [TestCase("TR-09", "마나 광역 공격", 100d, false)]
        [TestCase("TR-14", "킹코브라", 85d, false)]
        [TestCase("E-01", "마나 광역기", 150d, false)]
        [TestCase("E-04", "마나 고정 피해", 175d, false)]
        [TestCase("E-08", "마나 광역 스턴", 145d, false)]
        public void RealSourceBaselineEnablesOneAutomaticActiveWithExactDebit(
            string unitId, string skillName, double cost, bool spendsHealth)
        {
            using (var live = Create(unitId, false, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                var resources = live.Combat.ResourcesOf(live.ActorId);
                Assert.IsNotNull(resources);
                Assert.AreEqual(spendsHealth ? cost : cost,
                    spendsHealth ? resources.MaximumHealth : resources.MaximumMana);
                live.Battle.Advance(2);
                Assert.AreEqual(1, live.Combat.AutoCastApprovedSourceModelActives(),
                    unitId + " source model cast");
                Assert.AreEqual(spendsHealth ? 0 : resources.MaximumHealth,
                    resources.Health, 1e-8);
                Assert.AreEqual(spendsHealth ? resources.MaximumMana : 0,
                    resources.Mana, 1e-8);
                Assert.AreEqual(0, live.Combat.AutoCastApprovedSourceModelActives(),
                    "Casting without replenished resources must not duplicate rewards.");
                // External source model events settle only on the next
                // combat step, using the existing central event queue.
                var events = live.Battle.Advance(.1);
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == skillName), unitId);
            }
        }

        // Thirteen source-statted models, deliberately reusing the existing
        // resource ledger and tooltip effect executor. They are not Warcraft
        // native-cast parity and must never run without original target data.
        [TestCase("MY-07", "가스터 블래스터", 100d, false)]
        [TestCase("MY-08", "요르 포저", 75d, false)]
        [TestCase("L-32", "우르스스 쇼크", 115d, false)]
        [TestCase("TR-11", "마나 범위 타격", 125d, false)]
        [TestCase("TR-12", "유성소바 킥", 115d, false)]
        [TestCase("TR-15", "해왕류 다이브", 120d, false)]
        [TestCase("TR-19", "파이어 버드 스타", 140d, false)]
        [TestCase("TR-24", "어니스티 임팩트", 150d, true)]
        [TestCase("I-01", "특제 대포환", 160d, false)]
        [TestCase("I-06", "대자재천신", 115d, true)]
        [TestCase("MY-04", "나선섬광초륜무후삼식", 100d, false)]
        [TestCase("MY-05", "종경백제검", 145d, false)]
        [TestCase("MY-06", "이성의 창조물", 145d, false)]
        public void AdditionalThirteenCharactersCastSourceStattedActiveInRealPracticeBattle(
            string unitId, string skillName, double cost, bool spendsHealth)
        {
            using (var live = Create(unitId, false, -4900, 5400))
            {
                var units = CharacterUnitCatalog.FromJson(Data("units.json"));
                var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
                var profile = skills.Find(unitId);
                Assert.IsNotNull(profile);
                var skill = profile.Skills.Single(x => x.Name == skillName);
                Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill),
                    "All documented components and cost fields must be supported.");
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"), units);
                var source = SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                    Data("original-2323C-unit-object-stats.json"), map);
                live.Combat.ConfigureSourceModelUnitResources(source);
                var resources = live.Combat.ResourcesOf(live.ActorId);
                Assert.IsNotNull(resources, unitId);
                Assert.AreEqual(cost,
                    spendsHealth ? resources.MaximumHealth : resources.MaximumMana, 1e-8);
                live.Battle.Advance(2);
                Assert.AreEqual(1, live.Combat.AutoCastApprovedSourceModelActives(),
                    unitId + " / " + skillName + " active cast");
                Assert.AreEqual(0, spendsHealth ? resources.Health : resources.Mana, 1e-8);
                Assert.AreEqual(0, live.Combat.AutoCastApprovedSourceModelActives(),
                    "No double debit or second free cast in the same combat step");
                var events = live.Battle.Advance(.1);
                Assert.IsTrue(events.Any(x =>
                    x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == skillName),
                    unitId + " active must reach the real shared enemy HP pipeline");
            }
        }

        [Test]
        public void KobySourceManaRegenWithoutMaximumCannotAbortEntireCharacterResourceIndex()
        {
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"),
                CharacterUnitCatalog.FromJson(Data("units.json")));
            var index = SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                Data("original-2323C-unit-object-stats.json"), map);
            var koby = index.Find("TR-24");
            Assert.IsNotNull(koby);
            Assert.AreEqual("H0CO", koby.OriginalRawcode);
            Assert.AreEqual(150, koby.MaximumHealth);
            Assert.AreEqual(0, koby.MaximumMana,
                "Unspecified source mana capacity is not a free guessed MP pool.");
            Assert.AreEqual(0, koby.ManaRegeneration);
            Assert.AreEqual(0.5, koby.DeferredSourceManaRegeneration, 1e-9);
            Assert.AreEqual(0, koby.CreateStartingPool().Mana);
        }

        [Test]
        public void UnsupportedAnyaSlowAndOtherNonAllowlistedEffectsRemainDeferred()
        {
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var profile = skills.Find("MY-08");
            var unverified = profile.Skills.Single(x => x.Name == "로이드 포저");
            Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(unverified));
            SourceModelUnconnectedSkillRow row;
            Assert.IsTrue(SourceModelUnconnectedSkillCatalog.TryGet("h08M", out row));
            Assert.IsFalse(SourceModelUnconnectedSkillCatalog.AllowsActive(row, "로이드 포저"));
            Assert.IsTrue(SourceModelUnconnectedSkillCatalog.AllowsActive(row, "요르 포저"));
            Assert.AreEqual(0, row.PracticeAttackSkills.Count,
                "Auto-cast-only data cannot create an invented attack proc.");
        }

        [Test]
        public void SourceResourceLifecycleHandlesLateSpawnAndExplicitOverride()
        {
            using (var live = Create("C-01", false, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                Assert.IsNull(live.Combat.ResourcesOf(live.ActorId),
                    "Unapproved unit must not obtain artificial spell resources.");
                long late = live.Combat.SpawnCharacter("E-04", -4900, 5400);
                Assert.AreEqual(175, live.Combat.ResourcesOf(late).Mana);
                var external = new CharacterResources(500, 490, 500, 450);
                live.Combat.ConfigureCharacterResources(late, external);
                live.Battle.Advance(2);
                Assert.AreSame(external, live.Combat.ResourcesOf(late));
                Assert.AreEqual(490, external.Health);
                Assert.AreEqual(450, external.Mana,
                    "Externally provided resources must not receive model regen.");
                live.Combat.RemoveCharacter(late);
                Assert.Throws<KeyNotFoundException>(() => live.Combat.ResourcesOf(late));
            }
        }

        [Test]
        public void OriginalNativeFormsAndSatelliteCatalogUsePinnedJass()
        {
            Assert.AreEqual(SourceModelScriptAttackCatalog.SourceJassSha256,
                SourceModelNativeCharacterRules.JassSha256);
            Assert.AreEqual(150, SourceModelNativeCharacterRules.ChopperTransformMana);
            Assert.AreEqual(200, SourceModelNativeCharacterRules.VegapunkMainMana);
            Assert.AreEqual(1.23, SourceModelNativeCharacterRules.VegapunkMainDelaySeconds);
            Assert.AreEqual(14, SourceModelNativeCharacterRules.KaidoTransformRollPercent);
            string mapped;
            Assert.IsTrue(SourceModelNativeCharacterRules.TryGetNativeAttack("H091", out mapped));
            Assert.AreEqual("TR-23", mapped);
            Assert.IsTrue(SourceModelNativeCharacterRules.TryGetNativeAttack("H0AA", out mapped));
            Assert.AreEqual("TR-30", mapped);
            Assert.IsTrue(SourceModelNativeCharacterRules.TryGetNativeAttack("h07M", out mapped));
            Assert.AreEqual("I-11", mapped);
            Assert.IsFalse(SourceModelNativeCharacterRules.TryGetNativeAttack("h01G", out mapped),
                "Docking5 activation is unverified and must not be auto-invented.");
        }

        [TestCase("H0AB", 0, 2, 7)]
        [TestCase("H0AE", 1, 4, 14)]
        [TestCase("H0AM", 0, 4, 0)]
        [TestCase("H0AM", 2, 4, 0)]
        public void VegapunkSatelliteCostsFollowB4aStage(
            string rawcode, int previous, long expectedTraits, long expectedWood)
        {
            long traits, wood;
            Assert.IsTrue(SourceModelNativeCharacterRules.TryPlanSatelliteCost(
                rawcode, previous, out traits, out wood));
            Assert.AreEqual(expectedTraits, traits);
            Assert.AreEqual(expectedWood, wood);
        }

        [Test]
        public void OriginalNativeFormsUseMapObjectAttackAndKeepSplashModelOnly()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var catalog = SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                Data("original-2323C-unit-object-stats.json"), map);
            Assert.AreEqual(150, catalog.Find("TR-23").MaximumMana);
            Assert.AreEqual(200, catalog.Find("TR-30").MaximumMana);
            Assert.AreEqual(100, catalog.Find("I-11").MaximumHealth);
            Assert.AreEqual(285001, map.FindMapObjectAttack("H0B6").MinimumBaseDamage.Value);
            Assert.AreEqual(1.2, map.FindMapObjectAttack("H0B6").AttackCooldownSeconds.Value);
            Assert.AreEqual(250001, map.FindMapObjectAttack("h0AD").MinimumBaseDamage.Value);
            using (var chopper = Create("TR-23", true, -4900, 5400))
            {
                chopper.Combat.ConfigureSourceModelUnitResources(catalog);
                var events = chopper.Battle.Advance(4);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "쵸파 럼블볼 폭주"));
                Assert.IsNotNull(chopper.Combat.PracticeFormOf(chopper.ActorId));
                Assert.IsTrue(chopper.Combat.PracticeFormOf(chopper.ActorId)
                    .Contains("객체 평타 모델"));
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.Attack &&
                    e.ActorId == chopper.ActorId.ToString() && e.Amount > 0));
            }
            using (var vegapunk = Create("TR-30", true, -4900, 5400))
            {
                vegapunk.Combat.ConfigureSourceModelUnitResources(catalog);
                var events = vegapunk.Battle.Advance(4);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "베가펑크 마나 공격"));
                Assert.Less(vegapunk.Combat.ResourcesOf(vegapunk.ActorId).Mana, 200);
            }
            using (var kaido = Create("I-11", true, -4900, 5400))
            {
                var events = kaido.Battle.Advance(6);
                Assert.IsFalse(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "카이도 변신" && e.Detail.Contains("h0AD")));
                Assert.IsNull(kaido.Combat.PracticeFormOf(kaido.ActorId),
                    "Original h07M lacks A0PS: CBL must not roll 14% auto metamorphosis.");
                Assert.IsTrue(events.Any(e => e.SkillName == "카이도 100 체력 공격" &&
                    e.Type == CharacterCombatEventType.SkillDamage && e.Amount > 0));
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.Attack &&
                    e.ActorId == kaido.ActorId.ToString() && e.Amount > 0));
            }
        }

        [Test]
        public void TransformedChopperRawObjectAurasAreTimedByFormAndNotExtraBaseDamage()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var resources = SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                Data("original-2323C-unit-object-stats.json"), map);
            using (var live = Create("TR-23", true, -4900, 5400,
                withNativeAuras: true))
            {
                live.Combat.ConfigureSourceModelUnitResources(resources);
                long farAlly = live.Combat.SpawnCharacter("C-01", 4900, -5400);
                live.Battle.Advance(4);
                Assert.IsNotNull(live.Combat.PracticeNativeFormOf(live.ActorId));
                var effects = live.Combat.UnitStatusOf(live.ActorId).Snapshot();
                Assert.AreEqual(1, effects.Count(e =>
                    e.Type == CombatEffectType.AttackPowerBuff &&
                    e.SourceId.StartsWith("practice-aura:") && e.Amount == 40));
                Assert.AreEqual(1, effects.Count(e =>
                    e.Type == CombatEffectType.AttackSpeedBuff &&
                    e.SourceId.StartsWith("practice-aura:") && e.Amount == 40));
                Assert.IsTrue(live.Combat.UnitStatusOf(farAlly).Snapshot().Any(e =>
                    e.Type == CombatEffectType.AttackPowerBuff && e.Amount == 40),
                    "Mobile native auras cover the same lane regardless of W3X radius.");
                live.Combat.RemoveCharacter(live.ActorId);
            }
        }

        [Test]
        public void PinnedMapObjectAurasUseOriginalObjectIdentityAndLevel()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var objects = Original323MapObjectAuraCatalog.FromJson(
                Data("original-2323C-all-unit-aura-audit.json"), map);
            var lilith = objects.FindAll("H0AB");
            Assert.AreEqual(1, lilith.Count);
            Assert.AreEqual("A034", lilith[0].AbilityRawcode);
            Assert.AreEqual("B088", lilith[0].BuffRawcode);
            Assert.AreEqual(CombatEffectType.ArmorReduction, lilith[0].Type);
            Assert.AreEqual(40, lilith[0].Amount);
            Assert.AreEqual(1, lilith[0].Level);
            var form = objects.FindAll("H0B6");
            CollectionAssert.AreEquivalent(new[] { "A0OK", "A0OJ", "A0OI", "A0OL" },
                form.Select(a => a.AbilityRawcode).ToArray());
            Assert.IsEmpty(objects.FindAll("not-a-map-object"));
        }

        [Test]
        public void LilithArmorAuraSurvivesCreatorRemovalAtUnlimitedSameLaneReach()
        {
            using (var live = Create("TR-30", false, -4000, 4000,
                withNativeAuras: true))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                wallet.Grant(new ResourceBundle()
                    .Add(OriginalMissionWalletRewards.TraitPointsId, 2).Add("wood", 7));
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AB", wallet));
                live.Combat.RemoveCharacter(live.ActorId);
                live.Battle.Advance(.5);
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(enemy =>
                    live.Combat.StatusOf(enemy.Id).Snapshot().Any(effect =>
                        effect.Type == CombatEffectType.ArmorReduction &&
                        effect.SourceId.StartsWith("practice-aura:") && effect.Amount == 40)),
                    "Satellite object aura is independent of the creator and of its native 800 radius.");
            }
        }

        [Test]
        public void DifferentCharacterAndSatelliteArmorAurasDeferInsteadOfSilentlyStacking()
        {
            using (var live = Create("TR-23", false, -4000, 4000,
                withNativeAuras: true))
            {
                long vegapunk = live.Combat.SpawnCharacter("TR-30", -4000, 4000);
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                wallet.Grant(new ResourceBundle()
                    .Add(OriginalMissionWalletRewards.TraitPointsId, 2).Add("wood", 7));
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    vegapunk, "H0AB", wallet));
                live.Battle.Advance(.5);
                Assert.Greater(live.Combat.PracticeUnitEffects.DeferredAuraGroups, 0);
                Assert.IsFalse(live.Combat.MobileEnemySnapshot().Any(enemy =>
                    live.Combat.StatusOf(enemy.Id).Snapshot().Any(effect =>
                        effect.Type == CombatEffectType.ArmorReduction &&
                        effect.SourceId.StartsWith("practice-aura:")));
                live.Combat.RemoveCharacter(live.ActorId);
                live.Battle.Advance(.1);
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(enemy =>
                    live.Combat.StatusOf(enemy.Id).Snapshot().Any(effect =>
                        effect.Type == CombatEffectType.ArmorReduction &&
                        effect.SourceId.StartsWith("practice-aura:") && effect.Amount == 40)));
            }
        }

        [Test]
        public void NativeObjectAuraDoesNotLeakAcrossStoryAndMainLanes()
        {
            using (var live = Create("TR-23", false, -4900, 5400,
                withNativeAuras: true))
            {
                long storyAlly = live.Combat.SpawnCharacter("C-01", 4500, -4000);
                long mainAlly = live.Combat.SpawnCharacter("C-02", 4500, -4000);
                var lanes = new MutableStoryLane();
                live.Combat.ConfigureStoryDeployment(lanes);
                lanes.DeployedIds.Add(live.ActorId);
                lanes.DeployedIds.Add(storyAlly);
                live.Combat.RefreshAfterCommittedStoryLaneChange(live.ActorId);
                live.Battle.Advance(.5);
                Assert.IsTrue(live.Combat.UnitStatusOf(storyAlly).Snapshot().Any(e =>
                    e.Type == CombatEffectType.AttackPowerBuff && e.Amount == 40));
                Assert.IsFalse(live.Combat.UnitStatusOf(mainAlly).Snapshot().Any(e =>
                    e.Type == CombatEffectType.AttackPowerBuff));
            }
        }

        [Test]
        public void VegapunkSatellitePurchaseIsAtomicUniqueAndNotACombatRosterUnit()
        {
            using (var live = Create("TR-30", false, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                Assert.IsFalse(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AB", wallet));
                wallet.Grant(new ResourceBundle()
                    .Add(OriginalMissionWalletRewards.TraitPointsId, 10).Add("wood", 21));
                int before = live.Combat.CharacterCount;
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AB", wallet));
                Assert.AreEqual(8, wallet.Balance(OriginalMissionWalletRewards.TraitPointsId));
                Assert.AreEqual(14, wallet.Balance("wood"));
                Assert.IsFalse(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AB", wallet));
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AE", wallet));
                Assert.AreEqual(4, wallet.Balance(OriginalMissionWalletRewards.TraitPointsId));
                Assert.AreEqual(0, wallet.Balance("wood"));
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AM", wallet));
                Assert.AreEqual(0, wallet.Balance(OriginalMissionWalletRewards.TraitPointsId));
                Assert.AreEqual(3, live.Combat.PracticeSatellites().Count);
                Assert.AreEqual(before, live.Combat.CharacterCount);
                Assert.IsFalse(live.Combat.PracticeSatellites()[0].IndependentCombatVerified);
                var sources = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                Assert.AreEqual(27001, sources.FindMapObjectAttack("H0AB").MinimumBaseDamage.Value);
                var autonomous = live.Battle.Advance(2.5);
                Assert.IsTrue(autonomous.Any(e =>
                    e.Type == CharacterCombatEventType.Attack &&
                    e.ActorId.StartsWith("practice-satellite:") && e.Amount > 0),
                    "Purchased satellites must commit real model HP damage.");
                live.Combat.RemoveCharacter(live.ActorId);
                Assert.AreEqual(3, live.Combat.PracticeSatellites().Count,
                    "Original B4a satellites survive removal of their creator.");
                autonomous = live.Battle.Advance(2);
                Assert.IsTrue(autonomous.Any(e =>
                    e.Type == CharacterCombatEventType.Attack &&
                    e.ActorId.StartsWith("practice-satellite:") && e.Amount > 0),
                    "Satellite attacks cannot silently depend on creator existence.");
            }
        }

        [Test]
        public void SourceStoryRewardsCanUnlockActualVegapunkPurchaseWithoutFakeCurrency()
        {
            using (var live = Create("TR-30", false, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                wallet.Grant(new ResourceBundle().Add("wood", 7));
                var port = new Original323SourceDedicatedSkillPort(live.Combat, wallet);
                Assert.IsFalse(port.AvailableSkills().First(x =>
                    x.Id == SourceModelNativeCharacterRules.SatelliteActionId("H0AB"))
                    .CanExecute);
                var queue = new Original323StoryNativeRewardQueue();
                var story = new Original323StoryMobileProgression(wallet);
                queue.RecordCommittedClear(8);
                queue.RecordCommittedClear(9);
                Assert.AreEqual(4, queue.ApplyPending(story)); // 2 research, 2 BdL
                Assert.AreEqual(2, wallet.Balance(
                    OriginalMissionWalletRewards.TraitPointsId));
                Assert.IsTrue(port.AvailableSkills().First(x =>
                    x.Id == SourceModelNativeCharacterRules.SatelliteActionId("H0AB"))
                    .CanExecute);
                Assert.AreEqual(ManualActionState.Succeeded,
                    port.Execute(SourceModelNativeCharacterRules.SatelliteActionId("H0AB")).State);
                Assert.AreEqual(0, wallet.Balance(
                    OriginalMissionWalletRewards.TraitPointsId));
                Assert.AreEqual(0, wallet.Balance("wood"));
                Assert.AreEqual(1, live.Combat.PracticeSatellites().Count);
                Assert.AreEqual(0, queue.ApplyPending(story));
                Assert.AreEqual(0, wallet.Balance(
                    OriginalMissionWalletRewards.TraitPointsId));
            }
        }

        [Test]
        public void KaidoHundredLifeAttackUsesPinnedElevenSourceAreaPackets()
        {
            using (var live = Create("I-11", true, -4900, 5400))
            {
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"),
                    CharacterUnitCatalog.FromJson(Data("units.json")));
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                var events = live.Battle.Advance(3);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.EffectDeferred &&
                    e.SkillName == "카이도 100 체력 공격"));
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "카이도 100 체력 공격" && e.Amount > 0));
                Assert.Greater(live.Combat.PracticeKaidoAttackCounterOf(live.ActorId), 0);
            }
        }

        [Test]
        public void OriginalSpecialSourcesMatchThePinnedJassAndCatalogRawcodes()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            Assert.AreEqual(SourceModelScriptAttackCatalog.SourceJassSha256,
                SourceModelOriginalSpecialCallbackRules.JassSha256);
            foreach (var id in new[] { "L-19", "R-03", "R-07", "L-27", "L-32" })
            {
                var unit = map.Find(id);
                Assert.IsNotNull(unit);
                string mapped;
                Assert.IsTrue(SourceModelOriginalSpecialCallbackRules.TryGetUnit(
                    unit.OriginalRawcode, out mapped), id);
                Assert.AreEqual(id, mapped);
            }
            Assert.IsTrue(SourceModelOriginalSpecialCallbackRules.SugarToyBranch(8));
            Assert.IsFalse(SourceModelOriginalSpecialCallbackRules.SugarToyBranch(9));
            Assert.IsFalse(SourceModelOriginalSpecialCallbackRules.ZoroSourceExecuteCondition(
                false, 10, 100));
            Assert.IsTrue(SourceModelOriginalSpecialCallbackRules.ZoroSourceExecuteCondition(
                true, 15, 100));
            Assert.AreEqual(7.9,
                SourceModelOriginalSpecialCallbackRules.RyumaHealthBonus(0, 100),
                0.0001);
        }

        [TestCase("L-19", "장난감 No.31")]
        [TestCase("R-03", "검은수염 어둠")]
        [TestCase("R-07", "흑도 슈스이")]
        [TestCase("L-27", "사자의노래")]
        [TestCase("L-32", "우르스스 쇼크")]
        public void SourceJassSpecialAttackedCallbackReachesLivePractice(
            string id, string skill)
        {
            using (var live = Create(id, true, -4900, 5400))
            {
                if (id == "L-32")
                {
                    var units = CharacterUnitCatalog.FromJson(Data("units.json"));
                    var map = OriginalMapBaseStatCatalog.FromJson(
                        Data("original-2323C-map-base-stats.json"), units);
                    live.Combat.ConfigureSourceModelUnitResources(
                        SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                            Data("original-2323C-unit-object-stats.json"), map));
                }
                var all = live.Battle.Advance(6);
                Assert.IsTrue(all.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == skill), id + " " + skill);
            }
        }

        [Test]
        public void SugarToyUsesSourceSpawnLifetimeNotFusionRoster()
        {
            using (var live = Create("L-19", true, -4900, 5400))
            {
                int before = live.Combat.Roster.Count;
                var attacks = live.Battle.Advance(3);
                var toys = live.Combat.PracticeSummonsOf(live.ActorId);
                Assert.IsTrue(toys.Any(x => x.SourceUnitRawcode == "h06N" &&
                    x.ExpiresAt > live.Combat.Time && x.HasIndependentCombat &&
                    x.SpawnX == -4900 && x.SpawnY == 5400));
                Assert.IsTrue(attacks.Any(x => x.Type == CharacterCombatEventType.Attack &&
                    x.ActorId.StartsWith("practice-summon:") && x.Amount > 0));
                Assert.AreEqual(before, live.Combat.Roster.Count,
                    "A temporary toy is never a fusion material.");
                live.Combat.RemoveCharacter(live.ActorId);
                Assert.AreEqual(0, live.Combat.PracticeSummonsOf(live.ActorId).Count);
            }
        }

        [Test]
        public void SugarSuicideUsesSingleElseIfBranchAndAppliesTooltipAreaPacket()
        {
            using (var live = Create("L-19", true, -4900, 5400))
            {
                live.Rolls.ForceSugarSuicide = true;
                var events = live.Battle.Advance(3);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "하비하비열매 자폭" && e.Amount > 0));
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.EffectDeferred &&
                    e.Detail.Contains("타이밍 미검증")));
                Assert.IsFalse(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "장난감 No.31"));
                Assert.IsTrue(live.Combat.PracticeSummonsOf(live.ActorId).Any(x =>
                    x.SourceUnitRawcode == "h06P" &&
                    x.SpawnX == -4900 && x.SpawnY == 5400));
            }
        }

        [Test]
        public void LilithSatelliteProcsSurviveCreatorRemovalWithoutFusionOwnership()
        {
            using (var live = Create("TR-30", true, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                wallet.Grant(new ResourceBundle()
                    .Add(OriginalMissionWalletRewards.TraitPointsId, 2)
                    .Add("wood", 7));
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AB", wallet));
                live.Combat.RemoveCharacter(live.ActorId);
                var events = live.Battle.Advance(3);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "릴리스 단일 스턴"));
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.ActorId.StartsWith("practice-satellite:") &&
                    e.SkillName == "릴리스 광역 마법" && e.Amount > 0));
            }
        }

        [Test]
        public void AtlasSelfHasteUsesOwnIndependentStatusNotCreatorStatus()
        {
            using (var live = Create("TR-30", true, -4900, 5400))
            {
                var wallet = new EconomyWallet();
                live.Combat.ConfigureOtamaWoodWallet(wallet);
                wallet.Grant(new ResourceBundle()
                    .Add(OriginalMissionWalletRewards.TraitPointsId, 2)
                    .Add("wood", 7));
                Assert.IsTrue(live.Combat.TryAcquirePracticeSatellite(
                    live.ActorId, "H0AE", wallet));
                live.Combat.RemoveCharacter(live.ActorId);
                var events = live.Battle.Advance(3);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.StatusApplied &&
                    e.ActorId.StartsWith("practice-satellite:") &&
                    e.SkillName == "아틀라스 날뛰기"));
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.ActorId.StartsWith("practice-satellite:") &&
                    e.SkillName == "아틀라스 평타·치명타" && e.Amount > 0));
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "아틀라스 날뛰기" && e.Amount > 0));
            }
        }

        [Test]
        public void KaidoTenthAttackModelsOnlyPinnedFlatPacketAndDefersScaling()
        {
            using (var live = Create("I-11", false, -4900, 5400))
            {
                var events = live.Battle.Advance(30);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "인간형 열 번째 공격" && e.Amount > 0));
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.EffectDeferred &&
                    e.Detail.Contains("평타 비례 추가피해")));
                Assert.IsFalse(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == "카이도 변신"));
            }
        }

        [Test]
        public void KaidoTwoOrderedSourceRollsAndUserDataIncrementsAreIndependent()
        {
            var audit = OrdrJson.Object(OrdrJson.Parse(
                Data("original-2323C-source-character-callback-evidence.json")), "source");
            var original = OrdrJson.Object(OrdrJson.Value(audit, "kaido"), "kaido");
            Assert.AreEqual(false, OrdrJson.Value(original, "humanObjectHasA0PS"));
            Assert.AreEqual(true, OrdrJson.Value(original, "dragonObjectHasA0PS"));
            Assert.IsTrue(SourceModelNativeCharacterRules.KaidoCanRollAutoMorph("h0AD"));
            Assert.IsFalse(SourceModelNativeCharacterRules.KaidoCanRollAutoMorph("h07M"));
            Assert.AreEqual(2, SourceModelNativeCharacterRules.KaidoUserDataIncrease(8));
            Assert.AreEqual(2, SourceModelNativeCharacterRules.KaidoUserDataIncrease(15));
            Assert.AreEqual(1, SourceModelNativeCharacterRules.KaidoUserDataIncrease(0));
            using (var firstBranch = Create("I-11", true, -4900, 5400))
            {
                var all = firstBranch.Battle.Advance(5);
                Assert.IsTrue(all.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "인간형 타격"));
                Assert.IsFalse(all.Any(x => x.SkillName == "카이도 변신"));
            }
            using (var normalBranch = Create("I-11", false, -4900, 5400))
            {
                var all = normalBranch.Battle.Advance(5);
                Assert.IsTrue(all.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "인간형 기본 콜백"));
            }
            using (var secondBranch = Create("I-11", false, -4900, 5400))
            {
                secondBranch.Rolls.ForceKaidoSecondBranch = true;
                var all = secondBranch.Battle.Advance(5);
                Assert.IsTrue(all.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "인간형 두 번째 타격"));
            }
        }

        [Test]
        public void KaidoModelEighthPercentHitAndArmorStacksUseSharedEnemyStatus()
        {
            using (var live = Create("I-11", true, -4900, 5400))
            {
                live.Rolls.BlockKaidoMorph = true;
                var events = live.Battle.Advance(4);
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.SkillDamage &&
                    e.SkillName == "인간형 타격" && e.Amount > 0));
                Assert.IsTrue(events.Any(e =>
                    e.Type == CharacterCombatEventType.StatusApplied &&
                    e.SkillName == "약육강식"));
                Assert.IsTrue(live.Combat.MobileEnemySnapshot().Any(enemy =>
                    live.Combat.StatusOf(enemy.Id).Snapshot().Any(effect =>
                        effect.Type == CombatEffectType.ArmorReduction &&
                        effect.SourceId.StartsWith("practice-kaido:armor:") &&
                        effect.Amount >= 70)));
                live.Combat.RemoveCharacter(live.ActorId);
                Assert.IsFalse(live.Combat.MobileEnemySnapshot().Any(enemy =>
                    live.Combat.StatusOf(enemy.Id).Snapshot().Any(effect =>
                        effect.SourceId.StartsWith("practice-kaido:armor:"))));
            }
        }

        [Test]
        public void KumaJassManaUsesAttackDeclaredNotFreeAutoCast()
        {
            using (var live = Create("L-32", false, -4900, 5400))
            {
                var units = CharacterUnitCatalog.FromJson(Data("units.json"));
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"), units);
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                Assert.AreEqual(115, live.Combat.ResourcesOf(live.ActorId).Mana);
                Assert.AreEqual(0, live.Combat.AutoCastApprovedSourceModelActives());
                live.Battle.Advance(5);
                Assert.Less(live.Combat.ResourcesOf(live.ActorId).Mana, 115);
                Assert.AreEqual(0, live.Combat.AutoCastApprovedSourceModelActives());
            }
        }

        [Test]
        public void NewAttackPacketsAreIndependentAndSourceProfilesStayComplete()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var skills = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var mapping = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            Assert.GreaterOrEqual(SourceModelUnconnectedSkillCatalog.ApprovedAttackPacketCount, 30);
            var seen = new HashSet<string>();
            foreach (var row in SourceModelUnconnectedSkillCatalog.All)
            {
                Assert.IsTrue(seen.Add(row.Rawcode));
                Assert.AreEqual(row.Rawcode, mapping.Find(row.CatalogId).OriginalRawcode);
                Assert.IsNotNull(skills.Find(row.CatalogId));
                SourceModelNextThirtyUnit previouslyIncluded;
                Assert.IsFalse(SourceModelNextThirtySkillCatalog.TryGet(
                    row.Rawcode, out previouslyIncluded), row.CatalogId);
                foreach (string name in row.PracticeAttackSkills)
                {
                    var skill = skills.Find(row.CatalogId).Skills.Single(x => x.Name == name);
                    Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteAttack(skill),
                        row.CatalogId + " " + name);
                }
                foreach (string name in row.PracticeActiveSkills)
                {
                    var skill = skills.Find(row.CatalogId).Skills.Single(x => x.Name == name);
                    Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(skill),
                        row.CatalogId + " " + name);
                }
            }
        }

        [TestCase("RN-02", "대옥 나선환")]
        [TestCase("RN-13", "비상식량")]
        [TestCase("LI-01", "광역 마법 공격")]
        [TestCase("L-24", "산화 부지화")]
        [TestCase("I-02", "범위 마법 스턴")]
        public void MissingAttackPacketExecutesInSharedPracticeCombat(
            string unitId, string skillName)
        {
            using (var live = Create(unitId, true, -4900, 5400))
            {
                var events = live.Battle.Advance(6);
                Assert.IsTrue(events.Any(e => e.Type == CharacterCombatEventType.SkillActivated &&
                    e.SkillName == skillName && e.Detail.Contains("TOOLTIP_MODEL_ONLY")),
                    unitId + " " + skillName);
            }
        }

        [Test]
        public void KumaOriginalObjectManaPoolEnablesNewApprovedActiveOnce()
        {
            using (var live = Create("L-32", false, -4900, 5400))
            {
                var units = CharacterUnitCatalog.FromJson(Data("units.json"));
                var map = OriginalMapBaseStatCatalog.FromJson(
                    Data("original-2323C-map-base-stats.json"), units);
                live.Combat.ConfigureSourceModelUnitResources(
                    SourceModelUnitResourceCatalog.FromOriginalObjectStats(
                        Data("original-2323C-unit-object-stats.json"), map));
                Assert.AreEqual(115, live.Combat.ResourcesOf(live.ActorId).Mana);
                Assert.AreEqual(0, live.Combat.AutoCastApprovedSourceModelActives(),
                    "BtL is a SOURCE attack callback, not a button/autocast.");
                var result = live.Battle.Advance(5);
                Assert.IsTrue(result.Any(x =>
                    x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "우르스스 쇼크"));
                Assert.Less(live.Combat.ResourcesOf(live.ActorId).Mana, 115);
            }
        }

        [Test]
        public void RareLawAppliesOneTimedAttackSpeedBuffToAnAllyOnly()
        {
            using (var live = Create("R-05", false, -4900, 5400))
            {
                long ally = live.Combat.SpawnCharacter("C-01", -4900, 5400);
                live.Battle.Advance(1);
                Assert.AreEqual(1, live.Combat.AutoCastSourceRareLawAcceleration());
                Assert.IsTrue(live.Combat.UnitStatusOf(ally).Snapshot().Any(
                    x => x.Type == CombatEffectType.AttackSpeedBuff &&
                        x.Amount == 120 && x.DurationSeconds == 8));
                Assert.AreEqual(0, live.Combat.AutoCastSourceRareLawAcceleration());
                live.Combat.RemoveCharacter(ally);
                Assert.AreEqual(0, live.Combat.AutoCastSourceRareLawAcceleration());
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

        [Test]
        public void SameNamedActivePacketsCannotBorrowAnotherUnitsPermission()
        {
            var profiles = CharacterSkillCatalog.FromJson(Data("character-skill-profiles.json"));
            var robin = profiles.Require("TR-04").Skills.Single(
                x => x.Name == "체력 소모 광역 공격");
            var kaido = profiles.Require("I-11").Skills.Single(x => x.Name == robin.Name);
            var approved = SourceModelNextThirtySkillCatalog.All.Single(
                x => x.CatalogId == "TR-04").PracticeActiveSkills;
            var denied = SourceModelNextThirtySkillCatalog.All.Single(
                x => x.CatalogId == "I-11").PracticeActiveSkills;
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanModelCompleteActive(kaido));
            Assert.IsTrue(SourceModelNextThirtySkillCatalog.CanCastApprovedActive(
                "TR-04", approved, robin));
            Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanCastApprovedActive(
                "TR-04", approved, kaido));
            Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanCastApprovedActive(
                "I-11", denied, kaido));
            Assert.IsFalse(SourceModelNextThirtySkillCatalog.CanCastApprovedActive(
                "TR-0", approved, robin));
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
                int eventCount = live.Combat.LastEvents.Count;
                for (int attempt = 0; attempt < 3; attempt++)
                    Assert.AreEqual(AbilityCastResult.UnsupportedEffect,
                        live.Combat.TryCastActiveSkill(live.ActorId, skillId, target.Id), unitId);
                Assert.AreEqual(1000, resources.Health, unitId);
                Assert.AreEqual(1000, resources.Mana, unitId);
                Assert.AreEqual(eventCount, live.Combat.LastEvents.Count, unitId);
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
        public void ShinobuCloneUsesPinnedIndependentObjectDiceAndLifetime()
        {
            var units = CharacterUnitCatalog.FromJson(Data("units.json"));
            var map = OriginalMapBaseStatCatalog.FromJson(
                Data("original-2323C-map-base-stats.json"), units);
            var first = map.FindMapObjectAttack("h085");
            var second = map.FindMapObjectAttack("h0A9");
            Assert.AreEqual(.73, first.AttackCooldownSeconds.Value);
            Assert.AreEqual(first.AttackCooldownSeconds, second.AttackCooldownSeconds);
            Assert.AreEqual(first.MinimumBaseDamage, second.MinimumBaseDamage);
            Assert.AreEqual(first.MaximumBaseDamage, second.MaximumBaseDamage);
            using (var live = Create("L-21", true, -4900, 5400))
            {
                int owned = live.Combat.CharacterCount;
                var events = new List<CharacterCombatEvent>();
                for (int tick = 0; tick < 100 &&
                    live.Combat.PracticeSummonsOf(live.ActorId).Count == 0; tick++)
                    events.AddRange(live.Battle.Advance(.2));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillActivated &&
                    x.SkillName == "인술 분신"));
                Assert.AreEqual(owned, live.Combat.CharacterCount);
                var summons = live.Combat.PracticeSummonsOf(live.ActorId);
                Assert.IsTrue(summons.Count > 0);
                Assert.IsTrue(summons.All(x => x.OwnerId == live.ActorId &&
                    x.Name == "분신" && x.SourceUnitRawcode == "h085" &&
                    x.HasIndependentCombat && x.SpawnX != -4900 &&
                    x.SpawnY != 5400));
                Assert.IsTrue(events.Any(x => x.SkillName == "인술 분신" &&
                    x.Detail.Contains("반경119")));
                events.AddRange(live.Battle.Advance(1.1));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.Attack &&
                    x.ActorId.StartsWith("practice-summon:") && x.Amount > 0));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "인술 분신 범위 마법" && x.Amount > 0));
                Assert.IsTrue(events.Any(x => x.Type == CharacterCombatEventType.SkillDamage &&
                    x.SkillName == "분신 급습" && x.Amount > 0));
                live.Combat.RemoveCharacter(live.ActorId);
                Assert.AreEqual(0, live.Combat.PracticeSummonsOf(live.ActorId).Count);
            }
        }
    }
}
