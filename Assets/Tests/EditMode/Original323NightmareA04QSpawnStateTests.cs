using System;
using System.IO;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323NightmareA04QSpawnStateTests
    {
        private sealed class ObservedEvent : IOriginal323NightmareA04QEventWitness
        {
            public bool Approved = true;
            public string Evidence = "observed JASS source action";
            public bool TryConfirm(Original323NightmareA04QEvent sourceEvent,
                int playerSlot, out string evidence)
            {
                evidence = Evidence;
                return Approved;
            }
        }

        [Test]
        public void NightmareStartsWithSixteenForFourLinesAndGlobalBosses()
        {
            var state = new Original323NightmareA04QSpawnState();
            for (int player = 0; player < 4; player++)
            {
                int level;
                double resistance;
                Assert.IsTrue(state.TryGetNextSpawnLevel(
                    CharacterEnemyKind.Line, player, out level));
                Assert.AreEqual(16, level);
                Assert.IsTrue(state.TryGetNextSpawnMagicResistance(
                    CharacterEnemyKind.Line, player, out resistance));
                Assert.AreEqual(10, resistance, 1e-4);
                Assert.IsFalse(state.TryGetNextSpawnLevel(
                    CharacterEnemyKind.Boss, player, out level));
            }
            int boss;
            Assert.IsTrue(state.TryGetNextSpawnLevel(
                CharacterEnemyKind.Boss,
                Original323NightmareA04QSpawnState.GlobalEventPlayerSlot,
                out boss));
            Assert.AreEqual(16, boss);
            Assert.IsFalse(state.TryGetNextSpawnLevel(
                CharacterEnemyKind.Line, -1, out boss));
        }

        [Test]
        public void WarcuryChangesFutureSpawnsAndNikaRestoresThem()
        {
            var state = new Original323NightmareA04QSpawnState();
            var witness = new ObservedEvent();
            Assert.IsTrue(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.WarcuryActivated, -1, witness));
            Assert.IsTrue(state.IsWarcuryActive);
            Assert.IsFalse(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.WarcuryActivated, -1, witness));
            for (int player = 0; player < 4; player++)
            {
                double resistance;
                Assert.IsTrue(state.TryGetNextSpawnMagicResistance(
                    CharacterEnemyKind.Line, player, out resistance));
                Assert.AreEqual(25, resistance, 1e-4);
            }
            double bossResistance;
            Assert.IsTrue(state.TryGetNextSpawnMagicResistance(
                CharacterEnemyKind.Boss, -1, out bossResistance));
            Assert.AreEqual(25, bossResistance, 1e-4);
            Assert.IsTrue(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.NikaCanceledWarcury, -1, witness));
            Assert.IsFalse(state.IsWarcuryActive);
            Assert.IsFalse(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.NikaCanceledWarcury, -1, witness));
            Assert.IsTrue(state.TryGetNextSpawnMagicResistance(
                CharacterEnemyKind.Boss, -1, out bossResistance));
            Assert.AreEqual(10, bossResistance, 1e-4);
        }

        [Test]
        public void ItemBrookAndOnePieceOnlyChangeThatPlayersNextLineSpawn()
        {
            var state = new Original323NightmareA04QSpawnState();
            var witness = new ObservedEvent();
            Assert.IsTrue(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.LineItemAI19Used, 2, witness));
            Assert.IsTrue(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.BrookTraitTR12Applied, 2, witness));
            Assert.IsTrue(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.OnePieceTreasureCompleted, 2, witness));
            int level;
            Assert.IsTrue(state.TryGetNextSpawnLevel(
                CharacterEnemyKind.Line, 2, out level));
            Assert.AreEqual(34, level);
            Assert.IsTrue(state.TryGetNextSpawnLevel(
                CharacterEnemyKind.Enraged, 2, out level));
            Assert.AreEqual(34, level);
            Assert.IsTrue(state.TryGetNextSpawnLevel(
                CharacterEnemyKind.Line, 1, out level));
            Assert.AreEqual(16, level);
            Assert.IsTrue(state.TryGetNextSpawnLevel(
                CharacterEnemyKind.Boss, -1, out level));
            Assert.AreEqual(16, level);
            double resistance;
            Assert.IsTrue(state.TryGetNextSpawnMagicResistance(
                CharacterEnemyKind.Line, 2, out resistance));
            Assert.AreEqual(-8, resistance, 1e-4);
        }

        [Test]
        public void WarcuryAndPersonalWeakeningDeltasRemainIndependent()
        {
            var state = new Original323NightmareA04QSpawnState();
            var witness = new ObservedEvent();
            Assert.IsTrue(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.LineItemAI19Used, 0, witness));
            Assert.IsTrue(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.BrookTraitTR12Applied, 0, witness));
            Assert.IsTrue(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.WarcuryActivated, -1, witness));
            int level;
            Assert.IsTrue(state.TryGetNextSpawnLevel(
                CharacterEnemyKind.Line, 0, out level));
            Assert.AreEqual(9, level);
            Assert.IsTrue(state.TryGetNextSpawnLevel(
                CharacterEnemyKind.Line, 1, out level));
            Assert.AreEqual(1, level);
            Assert.IsTrue(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.NikaCanceledWarcury, -1, witness));
            Assert.IsTrue(state.TryGetNextSpawnLevel(
                CharacterEnemyKind.Line, 0, out level));
            Assert.AreEqual(24, level);
        }

        [Test]
        public void MissingOrInvalidEventEvidenceCannotChangeLevels()
        {
            var state = new Original323NightmareA04QSpawnState();
            var witness = new ObservedEvent { Approved = false };
            Assert.IsFalse(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.WarcuryActivated, -1, null));
            Assert.IsFalse(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.WarcuryActivated, -1, witness));
            witness.Approved = true;
            witness.Evidence = "TEST_ONLY";
            Assert.IsFalse(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.WarcuryActivated, -1, witness));
            witness.Evidence = "observed JASS source action";
            Assert.IsFalse(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.LineItemAI19Used, -1, witness));
            Assert.IsFalse(state.TryApplyObservedEvent(
                Original323NightmareA04QEvent.WarcuryActivated, 0, witness));
            Assert.IsFalse(state.TryApplyObservedEvent(
                (Original323NightmareA04QEvent)999, 0, witness));
            int level;
            Assert.IsTrue(state.TryGetNextSpawnLevel(
                CharacterEnemyKind.Line, 0, out level));
            Assert.AreEqual(16, level);
        }

        [Test]
        public void JassAndAbilityDataAuditPinsOriginalSourcesAndLowLevels()
        {
            string json = File.ReadAllText(Path.Combine(Application.dataPath,
                "..", "data", "original-2323C-nightmare-a04q-event-evidence.json"));
            var root = OrdrJson.Object(OrdrJson.Parse(json), "a04q original audit");
            Assert.AreEqual("S2 2.323[C]",
                OrdrJson.RequiredString(root, "gameVersion"));
            Assert.AreEqual(Original323NightmareA04QSpawnState.SourceWar3mapJSha256,
                OrdrJson.RequiredString(root, "sourceWar3mapJSha256"));
            Assert.AreEqual(Original323MonsterObjectAbilityCatalog.OriginalAbilityDataSha256,
                OrdrJson.RequiredString(root, "sourceAbilityDataSlkSha256"));
            Assert.AreEqual("A04Q", OrdrJson.RequiredString(root, "abilityRawcode"));
            Assert.AreEqual(false, OrdrJson.Value(root, "safeForLiveWaveSpawn"));
            var low = OrdrJson.Array(
                OrdrJson.Value(root, "abilityLowLevels"), "abilityLowLevels");
            Assert.AreEqual(4, low.Count);
            for (int i = 0; i < low.Count; i++)
            {
                var entry = OrdrJson.Object(low[i], "level");
                Assert.AreEqual((double)(i + 1), OrdrJson.Number(entry, "level").Value);
                double factor;
                Assert.IsTrue(Ordr323VerifiedDefenseAbilities.TryGetMagicDamageTakenFactor(
                    i + 1, out factor));
                Assert.AreEqual(OrdrJson.Number(entry, "magicDamageTakenFactor").Value,
                    factor, 1e-6);
            }
        }

        [Test]
        public void OutOfSourceRangeRemainsUnknownRatherThanInventingAClampedFactor()
        {
            var state = new Original323NightmareA04QSpawnState();
            var witness = new ObservedEvent();
            for (int i = 0; i < 6; i++)
                Assert.IsTrue(state.TryApplyObservedEvent(
                    Original323NightmareA04QEvent.LineItemAI19Used, 0, witness));
            int level;
            Assert.IsTrue(state.TryGetNextSpawnLevel(
                CharacterEnemyKind.Line, 0, out level));
            Assert.AreEqual(46, level);
            double resistance;
            Assert.IsFalse(state.TryGetNextSpawnMagicResistance(
                CharacterEnemyKind.Line, 0, out resistance));
        }
    }
}
