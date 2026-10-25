using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Tests
{
    // All monsters and numeric values below are TEST-ONLY inventions.
    // They validate the import guard and time-based combat wiring, not map parity.
    public sealed class Original323VerifiedMonsterWaveCatalogTests
    {
        private const string Fixture =
            "{\"format\":\"ordr-verified-monster-waves-v1\"," +
            "\"gameVersion\":\"S2 2.323[C]\"," +
            "\"mode\":\"nightmare-otherworld\"," +
            "\"sourceW3xSha256\":\"0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c\"," +
            "\"waves\":[{\"waveNumber\":1,\"waveEvidence\":\"TEST_ONLY: schedule\"," +
            "\"spawns\":[" +
            "{\"atSeconds\":2,\"instanceId\":\"second\",\"originalRawcode\":\"n001\"," +
            "\"kind\":\"Line\",\"kindEvidence\":\"TEST_ONLY: classification\"," +
            "\"health\":1000,\"healthEvidence\":\"TEST_ONLY: effective HP\"," +
            "\"baseArmor\":10,\"warcraftArmorType\":\"medium\"," +
            "\"armorEvidence\":\"TEST_ONLY: final armor/type\"," +
            "\"spawnEvidence\":\"TEST_ONLY: spawn trigger\"," +
            "\"positionEvidence\":\"TEST_ONLY: position\"," +
            "\"x\":20,\"y\":0,\"pathProgress\":2}," +
            "{\"atSeconds\":0,\"instanceId\":\"first\",\"originalRawcode\":\"n001\"," +
            "\"kind\":\"Line\",\"kindEvidence\":\"TEST_ONLY: classification\"," +
            "\"health\":1000,\"healthEvidence\":\"TEST_ONLY: effective HP\"," +
            "\"baseArmor\":10,\"warcraftArmorType\":\"medium\"," +
            "\"armorEvidence\":\"TEST_ONLY: final armor/type\"," +
            "\"spawnEvidence\":\"TEST_ONLY: spawn trigger\"," +
            "\"positionEvidence\":\"TEST_ONLY: position\"," +
            "\"x\":10,\"y\":0,\"pathProgress\":1}" +
            "]}]}";

        private static Original323VerifiedMonsterWaveCatalog Read(string text = null)
        {
            return Original323VerifiedMonsterWaveCatalog.FromJson(text ?? Fixture);
        }

        [Test]
        public void MultipleInstancesOfSameOriginalMonsterStayDistinctAndTimeOrdered()
        {
            var catalog = Read();
            Assert.AreEqual(1, catalog.Count);
            VerifiedWavePlan wave;
            Assert.IsTrue(catalog.TryGetPlan(1, out wave));
            Assert.AreEqual(2, wave.Spawns.Count);
            Assert.AreEqual("first", wave.Spawns[0].EnemyId);
            Assert.AreEqual("second", wave.Spawns[1].EnemyId);
            Assert.AreEqual("n001", wave.Spawns[0].OriginalRawcode);
            Assert.AreEqual("n001", wave.Spawns[1].OriginalRawcode);
            Assert.AreEqual(1000L, wave.Spawns[0].Health);
            Assert.AreEqual(10d, wave.Spawns[0].Defenses.BaseArmor.Value);
            Assert.AreEqual("medium", wave.Spawns[0].Defenses.WarcraftArmorType);
            Assert.IsTrue(wave.Spawns[0].Defenses.HasVerifiedArmor);
            Assert.IsFalse(wave.Spawns[0].Defenses.HasVerifiedMagicResistance);
            Assert.IsNull(wave.Spawns[0].Defenses.MagicResistancePercent);
            Assert.IsTrue(wave.Spawns[0].Defenses.Source.Contains("TEST_ONLY"));
            Assert.IsTrue(wave.Evidence.Contains("TEST_ONLY"));
        }

        [Test]
        public void UnknownWaveIsNotSilentlyReplacedWithSyntheticMonsters()
        {
            VerifiedWavePlan plan;
            Assert.IsFalse(Read().TryGetPlan(2, out plan));
            Assert.IsNull(plan);
        }

        [Test]
        public void InvalidMapChecksumOrModeCannotEnableSpawnData()
        {
            Assert.Throws<FormatException>(() => Read(Fixture.Replace(
                "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c",
                "0000000000000000000000000000000000000000000000000000000000000000")));
            Assert.Throws<FormatException>(() => Read(Fixture.Replace(
                "nightmare-otherworld", "nightmare-mutation")));
        }

        [Test]
        public void MissingEffectiveHpOrFinalArmorProofIsRejected()
        {
            Assert.Throws<FormatException>(() => Read(Fixture.Replace(
                "\"healthEvidence\":\"TEST_ONLY: effective HP\",", "")));
            Assert.Throws<FormatException>(() => Read(Fixture.Replace(
                "\"armorEvidence\":\"TEST_ONLY: final armor/type\",", "")));
            Assert.Throws<FormatException>(() => Read(Fixture.Replace(
                "\"spawnEvidence\":\"TEST_ONLY: spawn trigger\",", "")));
            Assert.Throws<FormatException>(() => Read(Fixture.Replace(
                "\"kindEvidence\":\"TEST_ONLY: classification\",", "")));
            Assert.Throws<FormatException>(() => Read(Fixture.Replace(
                "\"positionEvidence\":\"TEST_ONLY: position\",", "")));
        }

        [Test]
        public void UnknownArmorTypeAndNonIntegerOrZeroHpAreRejected()
        {
            Assert.Throws<FormatException>(() => Read(Fixture.Replace(
                "\"warcraftArmorType\":\"medium\"", "\"warcraftArmorType\":\"not-an-armor\"")));
            Assert.Throws<FormatException>(() => Read(Fixture.Replace(
                "\"health\":1000", "\"health\":0")));
            Assert.Throws<FormatException>(() => Read(Fixture.Replace(
                "\"health\":1000", "\"health\":1000.25")));
            Assert.Throws<FormatException>(() => Read(Fixture.Replace(
                "\"health\":1000", "\"health\":9007199254740992")));
        }

        [Test]
        public void MissingMagicProofCannotPretendZeroResistance()
        {
            const string magic = "\"magicResistancePercent\":0,";
            Assert.Throws<FormatException>(() => Read(Fixture.Replace(
                "\"health\":1000,", magic + "\"health\":1000,")));
            Assert.Throws<FormatException>(() => Read(Fixture.Replace(
                "\"health\":1000,", "\"a04qLevel\":16,\"health\":1000,")));
            string withProof = Fixture.Replace("\"health\":1000,",
                "\"magicResistancePercent\":10," +
                "\"magicResistanceEvidence\":\"TEST_ONLY: A04Q\"," +
                "\"a04qLevel\":16,\"health\":1000,");
            VerifiedWavePlan plan;
            Assert.IsTrue(Read(withProof).TryGetPlan(1, out plan));
            Assert.AreEqual(10d, plan.Spawns[0].Defenses.MagicResistancePercent.Value, 0.0001);
            Assert.AreEqual(16, plan.Spawns[0].Defenses.MagicAbilityLevel.Value);
            Assert.Throws<ArgumentException>(() => Read(withProof.Replace(
                "\"magicResistancePercent\":10", "\"magicResistancePercent\":11")));
        }

        [Test]
        public void DuplicateInstanceAndDuplicateWaveNumbersFailClosed()
        {
            Assert.Throws<ArgumentException>(() => Read(Fixture.Replace(
                "\"instanceId\":\"second\"", "\"instanceId\":\"first\"")));
            string duplicateWave = Fixture.Substring(0, Fixture.Length - 2) + "," +
                Fixture.Substring(Fixture.IndexOf("{\"waveNumber\":1", StringComparison.Ordinal),
                    Fixture.Length - Fixture.IndexOf("{\"waveNumber\":1", StringComparison.Ordinal) - 2) + "]}";
            Assert.Throws<FormatException>(() => Read(duplicateWave));
        }

        private sealed class TestLiveApproval : IOriginal323LiveMonsterWaveEvidence
        {
            public bool Allow;
            public string Evidence;
            public int Calls;

            public bool TryApprove(VerifiedWavePlan plan, out string approvalEvidence)
            {
                Calls++;
                approvalEvidence = Evidence;
                return Allow;
            }
        }

        [Test]
        public void SyntheticWaveDataCannotBecomeLiveEvenWithAnApprovalProvider()
        {
            VerifiedWavePlan wave;
            Assert.IsTrue(Read().TryGetPlan(1, out wave));
            var approval = new TestLiveApproval {
                Allow = true, Evidence = "original live inspection"
            };
            string reason;
            Assert.IsFalse(Original323VerifiedMonsterWaveCatalog.TryAuthorizeForLive(
                wave, approval, out reason));
            Assert.AreEqual(0, approval.Calls);
            Assert.IsNull(reason);
        }

        [Test]
        public void LiveWaveNeedsIndependentProofBeyondJsonEvidenceStrings()
        {
            // The substituted labels are test data, not genuine retail proof.
            // They establish that text alone cannot grant runtime permission.
            var catalog = Read(Fixture.Replace("TEST_ONLY:", "SOURCE_REVIEW:"));
            VerifiedWavePlan wave;
            Assert.IsTrue(catalog.TryGetPlan(1, out wave));
            string proof;
            Assert.IsFalse(Original323VerifiedMonsterWaveCatalog.TryAuthorizeForLive(
                wave, null, out proof));

            var approval = new TestLiveApproval {
                Allow = false, Evidence = "independent audit rejected"
            };
            Assert.IsFalse(Original323VerifiedMonsterWaveCatalog.TryAuthorizeForLive(
                wave, approval, out proof));
            approval.Allow = true;
            approval.Evidence = "TEST_ONLY: accepted";
            Assert.IsFalse(Original323VerifiedMonsterWaveCatalog.TryAuthorizeForLive(
                wave, approval, out proof));
            approval.Evidence = "independent live target and final-stat audit";
            Assert.IsTrue(Original323VerifiedMonsterWaveCatalog.TryAuthorizeForLive(
                wave, approval, out proof));
            Assert.AreEqual(approval.Evidence, proof);
        }

        [Test]
        public void WaveNumberOutsideOriginalSixtyFiveCannotReceiveLiveApproval()
        {
            VerifiedWavePlan wave;
            Assert.IsTrue(Read(Fixture.Replace(
                "\"waveNumber\":1", "\"waveNumber\":66")).TryGetPlan(66, out wave));
            var approval = new TestLiveApproval {
                Allow = true, Evidence = "independent live audit"
            };
            string proof;
            Assert.IsFalse(Original323VerifiedMonsterWaveCatalog.TryAuthorizeForLive(
                wave, approval, out proof));
            Assert.AreEqual(0, approval.Calls);
        }

        [Test]
        public void VerifiedSpawnTimesActuallyControlWorldEnemyHealthAndArmor()
        {
            var units = CharacterUnitCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"entries\":[]}");
            var skills = CharacterSkillCatalog.FromJson(
                "{\"gameVersion\":\"S2 2.323[C]\",\"profiles\":[]}");
            var unavailable = new RejectUnverifiedDamageFormula();
            var world = new CharacterCombatRuntime(units, skills,
                unavailable, unavailable, new SeededNonOriginalProcRandom(123));
            world.Start();
            VerifiedWavePlan wave;
            Assert.IsTrue(Read().TryGetPlan(1, out wave));
            var director = new CombatWaveDirector();
            director.Begin(world, wave);
            director.Step(world, 1);
            Assert.AreEqual(1, world.Game.AliveEnemies);
            Assert.AreEqual(1, director.RemainingSpawns);
            Assert.AreEqual(1000L, world.EnemyOf("first").Health);
            Assert.AreEqual(10d, world.EnemyOf("first").Defenses.BaseArmor.Value);
            Assert.IsNull(world.EnemyOf("second"));
            director.Step(world, 1);
            Assert.AreEqual(2, world.Game.AliveEnemies);
            Assert.AreEqual(0, director.RemainingSpawns);
            Assert.AreEqual(1000L, world.EnemyOf("second").MaxHealth);
            Assert.AreEqual(2d, director.ElapsedSeconds);
        }
    }
}
