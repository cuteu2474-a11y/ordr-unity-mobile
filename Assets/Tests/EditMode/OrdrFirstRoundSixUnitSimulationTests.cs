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