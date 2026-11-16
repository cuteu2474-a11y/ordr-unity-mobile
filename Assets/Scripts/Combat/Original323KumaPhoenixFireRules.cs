using System;

namespace Ordr.Mobile.Combat
{
    // Immutable Warcraft Apxf payload. Shared by the autonomous missile
    // scheduler and source-model damage rules, not by normal basic attacks.
    // Values below were read from the user-owned original 2.323[C] map,
    // Units/AbilityData.slk, not estimated from a tooltip.
    public sealed class Original323NativeAutoMissile
    {
        public string OwnerRawcode { get; private set; }
        public string AbilityRawcode { get; private set; }
        public string BaseAbilityRawcode { get; private set; }
        public double CooldownSeconds { get; private set; }
        public double AcquisitionRadius { get; private set; }
        public double InitialDamage { get; private set; }
        public double DamagePerSecond { get; private set; }
        public double NormalDuration { get; private set; }
        public double HeroDuration { get; private set; }
        public string TargetMask { get; private set; }

        public Original323NativeAutoMissile(string owner, string ability,
            string baseAbility, double cooldown, double radius,
            double initialDamage, double damagePerSecond,
            double normalDuration, double heroDuration, string targets)
        {
            if (string.IsNullOrWhiteSpace(owner) ||
                string.IsNullOrWhiteSpace(ability) ||
                string.IsNullOrWhiteSpace(baseAbility) ||
                string.IsNullOrWhiteSpace(targets) ||
                double.IsNaN(cooldown) || double.IsInfinity(cooldown) ||
                cooldown <= 0 || double.IsNaN(radius) ||
                double.IsInfinity(radius) || radius <= 0 ||
                double.IsNaN(initialDamage) ||
                double.IsInfinity(initialDamage) || initialDamage < 0 ||
                double.IsNaN(damagePerSecond) ||
                double.IsInfinity(damagePerSecond) || damagePerSecond < 0 ||
                double.IsNaN(normalDuration) ||
                double.IsInfinity(normalDuration) || normalDuration < 0 ||
                double.IsNaN(heroDuration) ||
                double.IsInfinity(heroDuration) || heroDuration < 0)
                throw new ArgumentException("원본 자동 발사 객체 수치가 유효하지 않습니다.");
            OwnerRawcode = owner;
            AbilityRawcode = ability;
            BaseAbilityRawcode = baseAbility;
            CooldownSeconds = cooldown;
            AcquisitionRadius = radius;
            InitialDamage = initialDamage;
            DamagePerSecond = damagePerSecond;
            NormalDuration = normalDuration;
            HeroDuration = heroDuration;
            TargetMask = targets;
        }

        public bool Contains(double x, double y, double targetX, double targetY)
        {
            double dx = targetX - x, dy = targetY - y;
            return !double.IsInfinity(dx * dx + dy * dy) &&
                dx * dx + dy * dy <= AcquisitionRadius * AcquisitionRadius;
        }
    }

    public static class Original323KumaPhoenixFireRules
    {
        public const string SourceW3xSha256 =
            "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c";
        public const string SourceAbilityDataSha256 =
            "23e9eb93d805a63cc76bfb617189331aef1e40c0c0ec69860bd7399a3bef2eda";
        public const string SourceUnitAbilitiesSha256 =
            "fbd47be22d1482d63df15d70d7a8fa38f7f26b288443a5d74aa056104aef139d";
        public const string SourceW3aSha256 =
            "aa938f02c22884f38c5e31bcd6229a35e917a8fab0128331fdab88980cfd50e0";

        public static readonly Original323NativeAutoMissile PhoenixFire =
            new Original323NativeAutoMissile("h017", "A0EA", "Apxf",
                0.2, 600.0, 150.0, 0.0, 0.01, 0.01,
                "ground,air,enemy");
    }
}
