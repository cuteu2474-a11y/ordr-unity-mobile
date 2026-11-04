using NUnit.Framework;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Tests
{
    public sealed class Original323UtaSharkBatch53Tests
    {
        private static CombatEffect Effect(string source, CombatEffectType kind) =>
            new CombatEffect(source, kind, CombatEffectOrigin.Aura, 55, 0);

        [Test]
        public void NativeSharkAndBulletPowerAreAcceptedByBasicAttackResolver()
        {
            var sources = new[]
            {
                Effect("practice-native:SPH3:A0N1:13", CombatEffectType.AttackPowerBuff),
                Effect("practice-tooltip:armada:A0WQ:7", CombatEffectType.AttackPowerBuff)
            };
            foreach (var effect in sources)
                Assert.IsTrue(SourceModelBasicCombatRules.AllowsPracticeAttackerEffect(effect));
        }

        [Test]
        public void ExistingNativeDefensiveStatesDoNotDisableOwnBasicAttack()
        {
            Assert.IsTrue(SourceModelBasicCombatRules.AllowsPracticeAttackerEffect(
                Effect("practice-native:SPH3:A0N1:13", CombatEffectType.ArmorBuff)));
            Assert.IsTrue(SourceModelBasicCombatRules.AllowsPracticeAttackerEffect(
                Effect("practice-native:ET12:A11L:22", CombatEffectType.ArmorBuff)));
            Assert.IsTrue(SourceModelBasicCombatRules.AllowsPracticeAttackerEffect(
                Effect("practice-native:R00R:A11B:9", CombatEffectType.MovementSpeedBuff)));
        }

        [Test]
        public void UnrelatedStatusesCannotSlipThroughTheAttackAllowlist()
        {
            Assert.IsFalse(SourceModelBasicCombatRules.AllowsPracticeAttackerEffect(
                Effect("practice-native:SPH3:A0N1:13", CombatEffectType.AttackSpeedBuff)));
            Assert.IsFalse(SourceModelBasicCombatRules.AllowsPracticeAttackerEffect(
                Effect("practice-native:unknown:13", CombatEffectType.AttackPowerBuff)));
            Assert.IsFalse(SourceModelBasicCombatRules.AllowsPracticeAttackerEffect(
                Effect("practice-native:R00R:A11B:9", CombatEffectType.AttackPowerBuff)));
        }

        [Test]
        public void UtaTraitUsesDistinctDurablePerActorReceipt()
        {
            Assert.AreEqual("source-native:uta:unlock:15",
                Original323SourceNativeActionKeys.UtaUnlockAction(15));
            Assert.AreEqual("source:native-trait:A0D2:15",
                Original323SourceNativeActionKeys.UtaUnlockResource(15));
            Assert.AreNotEqual(Original323SourceNativeActionKeys.UtaUnlockResource(15),
                Original323SourceNativeActionKeys.UtaUnlockResource(16));
        }

        [Test]
        public void UtaB6rSnapshotsLivingGroupAndUsesOriginalAreaPacket()
        {
            Assert.AreEqual(.69, SourceModelUtaB6rRules.WindupSeconds, 0.00001);
            Assert.AreEqual(400, SourceModelUtaB6rRules.AreaRadius);
            Assert.AreEqual(0, SourceModelUtaB6rRules.AreaDamage(0));
            Assert.AreEqual(1100000, SourceModelUtaB6rRules.AreaDamage(2));
            Assert.AreEqual(1550000, SourceModelUtaB6rRules.FinalPrimaryDamage);
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                SourceModelUtaB6rRules.AreaDamage(-1));
        }

        [Test]
        public void UtaB6rPrimaryHealthDamageRequiresSourceBdlEligibility()
        {
            // JD level 21 on a new normal line corresponds to factor 1.0.
            double correction = Ordr323MapDamageMath.ScriptHealthProportionalCorrection(21);
            Assert.AreEqual(200000, SourceModelUtaB6rRules.ConditionalPrimaryDamage(
                2, 1000000, correction, true), .001);
            Assert.AreEqual(0, SourceModelUtaB6rRules.ConditionalPrimaryDamage(
                2, 1000000, correction, false));
            Assert.AreEqual(0, SourceModelUtaB6rRules.ConditionalPrimaryDamage(
                0, 1000000, correction, true));
        }
    }
}
