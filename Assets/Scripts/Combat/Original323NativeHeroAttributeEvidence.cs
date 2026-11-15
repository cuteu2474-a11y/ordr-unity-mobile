using System;

namespace Ordr.Mobile.Combat
{
    // An actual native GetHeroStatBJ observation, not a unit-point score,
    // attack-power buff, or inferred equipment rank. The observer must
    // refresh its revision when inventory, dispel, temporary buff or form
    // changes. Never synthesize a snapshot when the original source is absent.
    public sealed class Original323NativeHeroAttributeSnapshot
    {
        public string SourceW3xSha256 { get; }
        public long InstanceId { get; }
        public string OriginalRawcode { get; }
        public long FormRevision { get; }
        public long NativeRevision { get; }
        public int StrengthWithoutBonuses { get; }
        public int AgilityWithoutBonuses { get; }
        public int IntelligenceWithoutBonuses { get; }
        public int StrengthWithBonuses { get; }
        public int AgilityWithBonuses { get; }
        public int IntelligenceWithBonuses { get; }

        public Original323NativeHeroAttributeSnapshot(string sourceW3xSha256,
            long instanceId, string originalRawcode, long formRevision,
            long nativeRevision, int strengthWithoutBonuses,
            int agilityWithoutBonuses, int intelligenceWithoutBonuses,
            int strengthWithBonuses, int agilityWithBonuses,
            int intelligenceWithBonuses)
        {
            if (sourceW3xSha256 != Original323HeroAttributeWorld.OriginalW3xSha256 ||
                instanceId <= 0 || string.IsNullOrEmpty(originalRawcode) ||
                originalRawcode.Length != 4 || formRevision < 0 ||
                nativeRevision <= 0 || strengthWithoutBonuses < 0 ||
                agilityWithoutBonuses < 0 || intelligenceWithoutBonuses < 0 ||
                strengthWithBonuses < 0 || agilityWithBonuses < 0 ||
                intelligenceWithBonuses < 0)
                throw new ArgumentException("원본 영웅 현재 능력치 증거가 유효하지 않습니다.");
            SourceW3xSha256 = sourceW3xSha256;
            InstanceId = instanceId;
            OriginalRawcode = originalRawcode;
            FormRevision = formRevision;
            NativeRevision = nativeRevision;
            StrengthWithoutBonuses = strengthWithoutBonuses;
            AgilityWithoutBonuses = agilityWithoutBonuses;
            IntelligenceWithoutBonuses = intelligenceWithoutBonuses;
            StrengthWithBonuses = strengthWithBonuses;
            AgilityWithBonuses = agilityWithBonuses;
            IntelligenceWithBonuses = intelligenceWithBonuses;
        }

        internal bool Matches(long instanceId, string rawcode, long formRevision) =>
            InstanceId == instanceId &&
            OriginalRawcode == rawcode &&
            FormRevision == formRevision &&
            SourceW3xSha256 == Original323HeroAttributeWorld.OriginalW3xSha256;

        internal double? Read(string attribute, bool includeBonuses)
        {
            if (attribute == "strength")
                return includeBonuses ? StrengthWithBonuses : StrengthWithoutBonuses;
            if (attribute == "agility")
                return includeBonuses ? AgilityWithBonuses : AgilityWithoutBonuses;
            if (attribute == "intelligence")
                return includeBonuses ? IntelligenceWithBonuses : IntelligenceWithoutBonuses;
            return null;
        }
    }

    public interface IOriginal323CurrentNativeHeroAttributeWitness
    {
        // Must be a single current-source observation, NOT an item receipt
        // or a stale cached snapshot from before a buff/transform.
        bool TryReadOwnedHero(long ownedInstanceId, string activeRawcode,
            out Original323NativeHeroAttributeSnapshot snapshot);
        bool TryReadPurchasedSatellite(long satelliteInstanceId, string rawcode,
            out Original323NativeHeroAttributeSnapshot snapshot);
    }

    // Capture-before / commit-after ticket for the exact JASS functions
    // that call GetHeroStatBJ(false), SetHeroXP and ModifyHeroStat(SET).
    // This does NOT itself authorize an upgrade, purchase or character swap.
    public sealed class Original323JassHeroFormTransfer
    {
        public long InstanceId { get; }
        public string FromRawcode { get; }
        public string ToRawcode { get; }
        public string OriginalJassCallback { get; }
        internal long Experience { get; }
        internal long PreviousFormRevision { get; }
        internal int Strength { get; }
        internal int Agility { get; }
        internal int Intelligence { get; }

        internal Original323JassHeroFormTransfer(long instanceId,
            string from, string to, string callback, long xp, long revision,
            int strength, int agility, int intelligence)
        {
            InstanceId = instanceId;
            FromRawcode = from;
            ToRawcode = to;
            OriginalJassCallback = callback;
            Experience = xp;
            PreviousFormRevision = revision;
            Strength = strength;
            Agility = agility;
            Intelligence = intelligence;
        }
    }
}
