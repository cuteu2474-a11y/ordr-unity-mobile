using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Unity;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // Real standalone Unity combat world, not an external damage estimate.
    public sealed class OrdrFirstRoundSixUnitSimulationTests
    {
        private sealed class Meter
        {
            public string Id, Name, Actor;
            public int Attacks, SkillHits, Kills, NoTargetSamples;
            public long BasicDamage, SkillDamage;
            // Resolution.DamageApplied is authoritative for HP loss; events
            // above are useful cues but can omit extra/proxy skill damage.
            public int ActualHpEvents, LethalHpEvents;
            public long ActualHpDamage;
            public int InCombatSamples, ReadySamples, CooldownSamples,
                OutOfRangeSamples, FormulaDeferredSamples,
                UnverifiedSourceSamples, UnverifiedTargetSamples,
                NoBattlefieldTargetSamples;
        }

        [Test]
        public void SixUnitRoundOneProducesObservedBattlefieldCounters()
        {
            var root = new GameObject("six requested units, real first-round engine");
            root.SetActive(false);
            var output = new StringBuilder();
            try
            {
                var owner = root.AddComponent<OrdrCharacterCombatController>();
                var awake = typeof(OrdrCharacterCombatController).GetMethod(
                    "Awake", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(awake);
                awake.Invoke(owner, null);
                owner.ConfigureCatalogOnlyWorld(2323u);
                owner.BeginUnitPreparation();

                string[] unitIds = { "U-12", "C-01", "C-02", "C-04", "C-05", "C-08" };
                string[] names = { "하찌", "루피", "조로", "우솝", "상디", "총병" };
                var meters = new Dictionary<string, Meter>(StringComparer.Ordinal);
                var ordered = new List<Meter>();
                for (int i = 0; i < unitIds.Length; i++)
                {
                    long instanceId = owner.Combat.SpawnCharacter(unitIds[i], -4000, 4000);
                    var meter = new Meter {
                        Id = unitIds[i], Name = names[i],
                        Actor = instanceId.ToString(CultureInfo.InvariantCulture)
                    };
                    ordered.Add(meter);
                    meters.Add(meter.Actor, meter);
                }
                Assert.AreEqual(6, owner.Combat.CharacterCount);
                Assert.AreEqual(6, owner.Combat.ReadyAttackers);

                owner.BeginStandaloneBattle();
                Assert.IsNotNull(owner.PracticeBattle);
                Assert.IsTrue(owner.PracticeBattle.MobileMainLaneEnabled);
                Assert.AreEqual(1, owner.Combat.Game.CurrentWave);
                Assert.AreEqual(56d, owner.PracticeBattle.SecondsRemaining, 0.00001);
                Assert.AreEqual(35, owner.OriginalMonsterSpawnIdentities
                    .BuildSinglePlayerSpawnTemplates(1).Count);
                Log(output, "START | seed=2323 | six actors | source-based 65R mobile engine, not retail parity");
                Log(output, "UNITS | " + string.Join(", ", ordered.Select(
                    m => m.Name + ":" + m.Id + ":" + m.Actor)));
                Snapshot(output, owner, ordered, 0);

                // The real wave director advances the original spawn cadence.
                // We measure the complete 21s preparation plus 35s wave.
                for (int tick = 1; tick <= 112; tick++)
                {
                    IReadOnlyList<CharacterCombatEvent> events =
                        owner.PracticeBattle.Advance(0.5);
                    foreach (var evt in events)
                    {
                        Meter m;
                        if (!meters.TryGetValue(evt.ActorId ?? "", out m)) continue;
                        if (evt.Type == CharacterCombatEventType.Attack)
                        {
                            m.Attacks++;
                            m.BasicDamage += evt.Amount;
                        }
                        else if (evt.Type == CharacterCombatEventType.SkillDamage)
                        {
                            m.SkillHits++;
                            m.SkillDamage += evt.Amount;
                        }
                        else if (evt.Type == CharacterCombatEventType.EnemyDefeated)
                            m.Kills++;
                    }
                    if (owner.Combat.Game.Phase == SessionPhase.Running &&
                        owner.Combat.PendingSimulationSeconds == 0)
                    {
                        // Mirror the controller Update's post-step auto-skill phase.
                        owner.Combat.AutoCastApprovedSourceModelActives();
                        owner.Combat.AutoCastSourceRareLawAcceleration();
                        owner.Combat.AutoCastSourceManualSupportAndBurst();
                        owner.Combat.AutoCastSourceModelManualUtility();
                    }
                    double t = tick * 0.5;
                    if (t == 21 || t == 22 || t == 25 || t == 30 || t == 35 ||
                        t == 40 || t == 45 || t == 50 || t == 55 || t == 56)
                        Snapshot(output, owner, ordered, t);
                }

                Assert.AreEqual(56d, owner.Combat.Time, 0.0001);
                Assert.AreEqual(2, owner.Combat.Game.CurrentWave);
                Assert.AreEqual(1, owner.PracticeBattle.RoundHistory.Count);
                var first = owner.PracticeBattle.RoundHistory[0];
                Assert.AreEqual(1, first.Wave);
                Assert.AreEqual(56d, first.ElapsedSeconds, 0.0001);
                Assert.AreEqual(35, first.Defeated + first.AliveEnemies);
                Assert.AreEqual(0, owner.Combat.Game.LeakedEnemies);

                Log(output, "RESULT | spawned=35 | killed=" + first.Defeated +
                    " | remaining=" + first.AliveEnemies +
                    " | first_round_gold=" + first.GoldAwarded);
                foreach (var m in ordered)
                    Log(output, "ACTOR | " + m.Name + " (" + m.Id + ")" +
                        " | attacks=" + m.Attacks + " | actual_basic_damage=" +
                        m.BasicDamage + " | skill_hits=" + m.SkillHits +
                        " | actual_skill_damage=" + m.SkillDamage +
                        " | kill_credits=" + m.Kills +
                        " | no_target_checkpoint_samples=" + m.NoTargetSamples);
                var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                    "sim-results", "first-round-six-units.txt"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, output.ToString(), Encoding.UTF8);
                TestContext.WriteLine(output.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SixUnitFirstFourRoundsProduceObservedWavePressureTrace()
        {
            // A real Unity 65R controller + director + damage resolver, but
            // SIX directly spawned debug units, NOT a legal five-Wisp draw.
            // A loss before R4 is an observation, not an assertion failure:
            // the report must survive and show the death countdown.
            var root = new GameObject("six-unit early-wave accumulation telemetry");
            root.SetActive(false);
            var output = new StringBuilder();
            string artifact = Path.GetFullPath(Path.Combine(Application.dataPath,
                "..", "sim-results", "first-four-round-six-units.txt"));
            try
            {
                var owner = root.AddComponent<OrdrCharacterCombatController>();
                var awake = typeof(OrdrCharacterCombatController).GetMethod(
                    "Awake", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(awake);
                awake.Invoke(owner, null);
                owner.ConfigureCatalogOnlyWorld(2323u);
                owner.BeginUnitPreparation();

                string[] ids = { "U-12", "C-01", "C-02", "C-04", "C-05", "C-08" };
                string[] names = { "하찌", "루피", "조로", "우솝", "상디", "총병" };
                var meters = new Dictionary<string, Meter>(StringComparer.Ordinal);
                var ordered = new List<Meter>();
                for (int i = 0; i < ids.Length; i++)
                {
                    long actor = owner.Combat.SpawnCharacter(ids[i], -4000, 4000);
                    var row = new Meter {
                        Id = ids[i], Name = names[i],
                        Actor = actor.ToString(CultureInfo.InvariantCulture)
                    };
                    meters.Add(row.Actor, row);
                    ordered.Add(row);
                }
                Assert.AreEqual(6, owner.Combat.CharacterCount);
                Assert.AreEqual(6, owner.Combat.ReadyAttackers);

                // Observe committed HP deltas, not Attack/SkillDamage UI cues.
                // This fixture has no story/creep targets, only scheduled wave
                // monsters. The battle's own by-origin ledger independently
                // checks the attribution on every observation.
                owner.Combat.ActualEnemyDamage += (actorId, enemyId, hp, killed) =>
                {
                    Meter meter;
                    if (hp <= 0 || !meters.TryGetValue(actorId ?? "", out meter))
                        return;
                    meter.ActualHpDamage += hp;
                    meter.ActualHpEvents++;
                    if (killed) meter.LethalHpEvents++;
                };
                owner.BeginStandaloneBattle();
                Assert.IsNotNull(owner.PracticeBattle);
                Assert.IsTrue(owner.PracticeBattle.MobileMainLaneEnabled);
                Assert.AreEqual(1, owner.Combat.Game.CurrentWave);

                Log(output, "START | seed=2323 | 1-4R | six direct debug spawns | " +
                    "real Unity 65R source-model damage and path, NOT WC3 retail parity");
                Log(output, "LIMIT | no real Wisp spending/recruitment; " +
                    "65R model HP/movement are not confirmed Nightmare retail values");
                Log(output, "UNITS | " + string.Join(", ", ordered.Select(
                    m => m.Name + ":" + m.Id + ":" + m.Actor)));
                int lastWave = 1;
                int reportedHistory = 0;
                bool lastDeathTimer = false;
                ObserveFourRoundFrame(output, owner, ordered, true);

                // 21s 1R preparation + 35s each of four waves: 161s maximum.
                // Stop on 5R or an actual model defeat; never fabricate kills
                // or continue stepping a lost session.
                const int maxHalfSecondSteps = 350;
                for (int tick = 1; tick <= maxHalfSecondSteps; tick++)
                {
                    if (owner.Combat.Game.Phase != SessionPhase.Running ||
                        owner.Combat.Game.CurrentWave >= 5) break;
                    var events = owner.PracticeBattle.Advance(0.5);
                    foreach (var evt in events)
                    {
                        Meter meter;
                        if (!meters.TryGetValue(evt.ActorId ?? "", out meter))
                            continue;
                        if (evt.Type == CharacterCombatEventType.Attack)
                        {
                            meter.Attacks++;
                            meter.BasicDamage += evt.Amount;
                        }
                        else if (evt.Type == CharacterCombatEventType.SkillDamage)
                        {
                            meter.SkillHits++;
                            meter.SkillDamage += evt.Amount;
                        }
                        else if (evt.Type == CharacterCombatEventType.EnemyDefeated)
                            meter.Kills++;
                    }
                    if (owner.Combat.Game.Phase == SessionPhase.Running &&
                        owner.Combat.PendingSimulationSeconds == 0)
                    {
                        // Match the real controller's post-step auto-cast phase.
                        owner.Combat.AutoCastApprovedSourceModelActives();
                        owner.Combat.AutoCastSourceRareLawAcceleration();
                        owner.Combat.AutoCastSourceManualSupportAndBurst();
                        owner.Combat.AutoCastSourceModelManualUtility();
                    }
                    var pressure = owner.PracticeBattle.CaptureCombatPressure();
                    Assert.AreEqual(pressure.ObservedWaveHpDamageThisRound,
                        pressure.OriginWaves.Sum(x => x.AppliedHpDamageThisRound),
                        "Every actual HP delta must retain its source wave.");
                    Assert.AreEqual(pressure.ObservedDamageEventsThisRound,
                        pressure.OriginWaves.Sum(x => x.DamageEventsThisRound));
                    Assert.AreEqual(owner.Combat.Game.AliveEnemies,
                        pressure.TrackedWaveAliveEnemies,
                        "Only actual verified wave enemies are present here.");

                    if (pressure.TrackedWaveAliveEnemies > 0)
                        foreach (var readiness in pressure.Units)
                        {
                            Meter meter;
                            if (!meters.TryGetValue(
                                readiness.InstanceId.ToString(CultureInfo.InvariantCulture),
                                out meter)) continue;
                            meter.InCombatSamples++;
                            switch (readiness.Kind)
                            {
                                case BasicAttackReadinessKind.Ready:
                                    meter.ReadySamples++; break;
                                case BasicAttackReadinessKind.Cooldown:
                                    meter.CooldownSamples++; break;
                                case BasicAttackReadinessKind.OutOfRange:
                                    meter.OutOfRangeSamples++; break;
                                case BasicAttackReadinessKind.FormulaDeferred:
                                    meter.FormulaDeferredSamples++; break;
                                case BasicAttackReadinessKind.UnverifiedSource:
                                    meter.UnverifiedSourceSamples++; break;
                                case BasicAttackReadinessKind.UnverifiedTarget:
                                    meter.UnverifiedTargetSamples++; break;
                                case BasicAttackReadinessKind.NoBattlefieldTarget:
                                    meter.NoBattlefieldTargetSamples++; break;
                                default:
                                    throw new InvalidOperationException(
                                        "Unknown combat readiness classification.");
                            }
                        }

                    while (reportedHistory < owner.PracticeBattle.RoundHistory.Count)
                    {
                        var round = owner.PracticeBattle.RoundHistory[reportedHistory++];
                        Log(output, "ROUND_END | wave=" + round.Wave +
                            " | duration=" + round.ElapsedSeconds.ToString("F1",
                                CultureInfo.InvariantCulture) +
                            " | defeated=" + round.Defeated +
                            " | survivors=" + round.AliveEnemies +
                            " | gold=" + round.GoldAwarded);
                    }
                    bool waveChanged = pressure.Wave != lastWave;
                    bool timerChanged = owner.PracticeBattle.IsDeathCountdownActive !=
                        lastDeathTimer;
                    if (tick % 10 == 0 || waveChanged || timerChanged ||
                        owner.Combat.Game.Phase != SessionPhase.Running ||
                        pressure.Wave >= 5)
                        ObserveFourRoundFrame(output, owner, ordered, false);
                    lastWave = pressure.Wave;
                    lastDeathTimer = owner.PracticeBattle.IsDeathCountdownActive;
                }
                Log(output, "RESULT | phase=" + owner.Combat.Game.Phase +
                    " | wave=" + owner.Combat.Game.CurrentWave +
                    " | time=" + owner.Combat.Time.ToString("F1",
                        CultureInfo.InvariantCulture) +
                    " | alive=" + owner.Combat.Game.AliveEnemies +
                    " | total_kills=" + owner.Combat.Game.DefeatedEnemies +
                    " | death_count_active=" +
                        owner.PracticeBattle.IsDeathCountdownActive +
                    " | reason=" + owner.PracticeBattle.ResultMessage);
                foreach (var meter in ordered)
                    Log(output, "ACTOR_TOTAL | " + meter.Name + ":" + meter.Id +
                        " | actual_hp=" + meter.ActualHpDamage +
                        " | hp_events=" + meter.ActualHpEvents +
                        " | lethal_hp_events=" + meter.LethalHpEvents +
                        " | basic_events=" + meter.Attacks +
                        " | basic_event_damage=" + meter.BasicDamage +
                        " | skill_events=" + meter.SkillHits +
                        " | skill_event_damage=" + meter.SkillDamage +
                        " | live_target_samples=" + meter.InCombatSamples +
                        " | ready=" + meter.ReadySamples +
                        " | cooldown=" + meter.CooldownSamples +
                        " | out_of_range=" + meter.OutOfRangeSamples +
                        " | formula_deferred=" + meter.FormulaDeferredSamples +
                        " | source_unverified=" + meter.UnverifiedSourceSamples +
                        " | target_unverified=" + meter.UnverifiedTargetSamples +
                        " | no_battlefield_target=" +
                            meter.NoBattlefieldTargetSamples);
            }
            finally
            {
                // Preserve the partial report even if a parity assertion or
                // exception interrupts the long-running integrated scenario.
                Directory.CreateDirectory(Path.GetDirectoryName(artifact));
                File.WriteAllText(artifact, output.ToString(), Encoding.UTF8);
                TestContext.WriteLine(output.ToString());
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ObserveFourRoundFrame(StringBuilder output,
            OrdrCharacterCombatController owner, IReadOnlyList<Meter> meters,
            bool initial)
        {
            var pressure = owner.PracticeBattle.CaptureCombatPressure();
            string origins = string.Join(",", pressure.OriginWaves.Select(x =>
                x.SpawnWave + ":alive=" + x.AliveEnemies +
                "/hp=" + x.AppliedHpDamageThisRound +
                "/hits=" + x.DamageEventsThisRound +
                "/kills=" + x.DefeatedThisRound));
            Log(output, "FRAME_1_4R | t=" +
                owner.Combat.Time.ToString("F1", CultureInfo.InvariantCulture) +
                " | wave=" + pressure.Wave +
                " | alive=" + pressure.AliveEnemies +
                " | tracked=" + pressure.TrackedWaveAliveEnemies +
                " | ready=" + pressure.Ready +
                " | cooldown=" + pressure.Cooldown +
                " | out_of_range=" + pressure.OutOfRange +
                " | no_target=" + pressure.NoBattlefieldTarget +
                " | formula_deferred=" + pressure.FormulaDeferred +
                " | rejected=" + pressure.UnverifiedTarget +
                " | unverified=" + pressure.UnverifiedSource +
                " | total_hp=" + pressure.ObservedWaveHpDamageSinceAttach +
                " | this_round_hp=" + pressure.ObservedWaveHpDamageThisRound +
                " | timer=" + owner.PracticeBattle.IsDeathCountdownActive +
                " | timer_left=" + owner.PracticeBattle
                    .DeathCountdownSecondsRemaining.ToString("F1",
                        CultureInfo.InvariantCulture) +
                " | origins=[" + origins + "]");
            if (initial) return;
            Log(output, "ACTOR_ROUND | " + string.Join(",",
                meters.Select(m =>
                {
                    var row = pressure.ActorDamageThisRound
                        .FirstOrDefault(x => x.ActorId == m.Actor);
                    return m.Name + "=" + (row == null ? 0L : row.AppliedHpDamage);
                })));
        }

        private static void Snapshot(StringBuilder output,
            OrdrCharacterCombatController owner, IReadOnlyList<Meter> meters, double seconds)
        {
            var line = owner.Combat.MobileEnemySnapshot()
                .Where(e => e.Kind == CharacterEnemyKind.Line)
                .OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            int currentWave = owner.Combat.Game.CurrentWave;
            int spawned = currentWave == 1
                ? owner.VerifiedWaveDirector.CurrentProgress.Spawned : 35;
            foreach (var m in meters)
            {
                long actor = long.Parse(m.Actor, CultureInfo.InvariantCulture);
                if (owner.Combat.InspectBasicAttack(actor).Kind ==
                    BasicAttackReadinessKind.UnverifiedTarget)
                    m.NoTargetSamples++;
            }
            string samples = string.Join(",", line.Take(5).Select(e =>
                e.Id + "=" + e.Health + "/" + e.MaxHealth +
                "@" + e.X.ToString("F0", CultureInfo.InvariantCulture) +
                ":" + e.Y.ToString("F0", CultureInfo.InvariantCulture)));
            Log(output, "FRAME | second=" +
                seconds.ToString("F1", CultureInfo.InvariantCulture) +
                " | wave=" + currentWave + " | spawned_1R=" + spawned +
                " | defeated=" + owner.Combat.Game.DefeatedEnemies +
                " | live=" + line.Count + " | live_hp_sum=" +
                line.Sum(e => e.Health) + " | examples=" + samples);
        }

        private static void Log(StringBuilder output, string value)
        {
            output.AppendLine(value);
            Debug.Log("[ORDR_SIX_UNIT_SIM] " + value);
        }
    }
}