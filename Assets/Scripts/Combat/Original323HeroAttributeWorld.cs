using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Source 2.323[C] hero attributes: pinned original UnitBalance.slk
    // + live XP growth + an independently observed native stat source.
    // Only proven GetHeroStatBJ(true) totals include item/ability/buff
    // effects; AttackPowerBuff and AttackSpeedBuff are NOT STR/AGI/INT.
    public sealed class Original323HeroAttributeWorld
    {
        public const string OriginalW3xSha256 =
            "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c";

        private sealed class BaseAttributes
        {
            public int Strength;
            public int Agility;
            public int Intelligence;
        }

        private sealed class FormTransfer
        {
            public string Rawcode;
            public long Revision;
            public long DeltaStrength;
            public long DeltaAgility;
            public long DeltaIntelligence;
        }

        private readonly Func<long, Original323HeroLevelState> liveLevels;
        private readonly Func<long, long> liveFormRevision;
        private readonly Dictionary<string, BaseAttributes> sourceBases =
            new Dictionary<string, BaseAttributes>(StringComparer.Ordinal);
        private readonly Dictionary<long, FormTransfer> transfers =
            new Dictionary<long, FormTransfer>();
        private IOriginal323CurrentNativeHeroAttributeWitness currentNativeStats;
        private Func<long, string, double?> liveHeroBonus;
        private Func<long, string, double?> liveSatelliteBonus;

        public Original323HeroAttributeWorld(
            Func<long, Original323HeroLevelState> witnessedLevels,
            Func<long, long> witnessedFormRevision = null)
        {
            liveLevels = witnessedLevels ??
                throw new ArgumentNullException(nameof(witnessedLevels));
            liveFormRevision = witnessedFormRevision;
        }

        // Four source H rows have literal STR/INT/AGI; the other 66 have
        // missing numeric cells decoded as zero by OpenRealm's typed SLK
        // loader. This is not verified Blizzard executable behavior.
        public void RegisterOriginalUnitBalanceRow(string sourceW3xSha256,
            string originalRawcode, int strength, int agility, int intelligence)
        {
            if (sourceW3xSha256 != OriginalW3xSha256 ||
                string.IsNullOrEmpty(originalRawcode) ||
                originalRawcode.Length != 4 || originalRawcode[0] != 'H' ||
                strength < 0 || agility < 0 || intelligence < 0)
                throw new ArgumentException("원본 영웅 능력치의 출처·형태·값이 유효하지 않습니다.");
            BaseAttributes existing;
            if (sourceBases.TryGetValue(originalRawcode, out existing))
            {
                if (existing.Strength != strength || existing.Agility != agility ||
                    existing.Intelligence != intelligence)
                    throw new InvalidOperationException(
                        "동일한 원본 영웅 형태에 상충하는 SLK 능력치가 등록됐습니다.");
                return;
            }
            sourceBases.Add(originalRawcode, new BaseAttributes {
                Strength = strength, Agility = agility, Intelligence = intelligence
            });
        }

        internal void RegisterOriginalUnitBalanceSparseNumericDefault(
            string mapSha256, string unitBalanceSlkSha256, string originalRawcode)
        {
            if (unitBalanceSlkSha256 !=
                Original323UnitBalanceHeroInitialAttributes.SourceUnitBalanceSlkSha256)
                throw new ArgumentException("원본 UnitBalance.slk 해시가 일치하지 않습니다.");
            RegisterOriginalUnitBalanceRow(mapSha256, originalRawcode, 0, 0, 0);
        }

        // Read on every skill evaluation: a different native revision after
        // equip, unequip, buff expiry, dispel or morph must replace the old
        // values. A missing native observation is never treated as bonus 0.
        public void ConnectOriginalCurrentNativeStats(
            IOriginal323CurrentNativeHeroAttributeWitness witness)
        {
            if (currentNativeStats != null || witness == null)
                throw new InvalidOperationException(
                    "원본 현재 능력치 관측기는 한 번만 연결합니다.");
            currentNativeStats = witness;
        }

        // Legacy per-stat live bonus evidence can still be provided when a
        // full native snapshot is not available. It must include an explicit
        // observed zero for 'no bonuses', never a guessed value.
        public void ConnectOriginalNativeBonuses(
            Func<long, string, double?> heroBonuses,
            Func<long, string, double?> satelliteBonuses)
        {
            if (liveHeroBonus != null || liveSatelliteBonus != null ||
                (heroBonuses == null && satelliteBonuses == null))
                throw new InvalidOperationException(
                    "원본 능력치 보너스의 중복/빈 연결이 허용되지 않습니다.");
            liveHeroBonus = heroBonuses;
            liveSatelliteBonus = satelliteBonuses;
        }

        private static bool ValidInteger(double? value)
        {
            return value.HasValue && !double.IsNaN(value.Value) &&
                !double.IsInfinity(value.Value) &&
                Math.Floor(value.Value) == value.Value &&
                value.Value >= -int.MaxValue && value.Value <= int.MaxValue;
        }

        private static double? Baseline(BaseAttributes source, string attribute)
        {
            if (attribute == "strength") return source.Strength;
            if (attribute == "agility") return source.Agility;
            if (attribute == "intelligence") return source.Intelligence;
            return null;
        }

        private long FormRevisionOf(long instanceId) =>
            liveFormRevision == null ? 0 : liveFormRevision(instanceId);

        private Original323NativeHeroAttributeSnapshot CurrentNativeHero(
            long instanceId, string rawcode, long formRevision)
        {
            if (currentNativeStats == null) return null;
            Original323NativeHeroAttributeSnapshot observation;
            if (!currentNativeStats.TryReadOwnedHero(instanceId, rawcode,
                    out observation) || observation == null ||
                !observation.Matches(instanceId, rawcode, formRevision))
                return null;
            return observation;
        }

        private Original323NativeHeroAttributeSnapshot CurrentNativeSatellite(
            long instanceId, string rawcode)
        {
            if (currentNativeStats == null) return null;
            Original323NativeHeroAttributeSnapshot observation;
            if (!currentNativeStats.TryReadPurchasedSatellite(instanceId, rawcode,
                    out observation) || observation == null ||
                !observation.Matches(instanceId, rawcode, 0))
                return null;
            return observation;
        }

        private double? ModelUnbonused(long instanceId,
            Original323HeroLevelState growth, string attribute, long revision)
        {
            BaseAttributes source;
            if (!sourceBases.TryGetValue(growth.SourceRawcode, out source))
                return null;
            double? baseline = Baseline(source, attribute);
            if (!baseline.HasValue) return null;
            long extra = attribute == "strength" ? growth.AddedStrength :
                attribute == "agility" ? growth.AddedAgility :
                growth.AddedIntelligence;
            FormTransfer transfer;
            if (transfers.TryGetValue(instanceId, out transfer))
            {
                if (transfer.Rawcode != growth.SourceRawcode ||
                    transfer.Revision != revision)
                    transfers.Remove(instanceId);
                else
                    extra += attribute == "strength" ? transfer.DeltaStrength :
                        attribute == "agility" ? transfer.DeltaAgility :
                        transfer.DeltaIntelligence;
            }
            double result = baseline.Value + extra;
            return ValidInteger(result) && result >= 0 ? (double?)result : null;
        }

        public double? ReadHero(long ownedInstanceId, string attribute,
            bool includeBonuses)
        {
            if (ownedInstanceId <= 0 ||
                (attribute != "strength" && attribute != "agility" &&
                 attribute != "intelligence")) return null;
            var growth = liveLevels(ownedInstanceId);
            if (growth == null) return null;
            long revision = FormRevisionOf(ownedInstanceId);
            var native = CurrentNativeHero(ownedInstanceId,
                growth.SourceRawcode, revision);
            if (native != null) return native.Read(attribute, includeBonuses);
            double? source = ModelUnbonused(ownedInstanceId, growth,
                attribute, revision);
            return source.HasValue ? ApplyBonus(source.Value, includeBonuses,
                liveHeroBonus, ownedInstanceId, attribute) : null;
        }

        // Satellites are individual purchases. A current native observation
        // supplies their full XP/items/bonuses. Without one, their initial
        // unbonused value can be read but full native stats remain UNKNOWN.
        public double? ReadSatellite(long satelliteInstanceId, string rawcode,
            string attribute, bool includeBonuses)
        {
            if (satelliteInstanceId <= 0 || string.IsNullOrEmpty(rawcode))
                return null;
            var native = CurrentNativeSatellite(satelliteInstanceId, rawcode);
            if (native != null) return native.Read(attribute, includeBonuses);
            BaseAttributes source;
            if (!sourceBases.TryGetValue(rawcode, out source))
                return null;
            double? initial = Baseline(source, attribute);
            if (!initial.HasValue) return null;
            return ApplyBonus(initial.Value, includeBonuses,
                liveSatelliteBonus, satelliteInstanceId, attribute);
        }

        private static double? ApplyBonus(double initial, bool includeBonuses,
            Func<long, string, double?> nativeBonus, long instanceId,
            string attribute)
        {
            if (!ValidInteger(initial) || initial < 0) return null;
            if (!includeBonuses) return initial;
            if (nativeBonus == null) return null;
            double? bonus = nativeBonus(instanceId, attribute);
            if (!ValidInteger(bonus)) return null;
            double result = initial + bonus.Value;
            return ValidInteger(result) && result >= 0 ? (double?)result : null;
        }

        // Original JASS Bjq and Bxc: before removing the old hero, capture
        // GetHeroStatBJ(STR/AGI/INT,false), then SetHeroXP and SET all three
        // on the new hero. Never use this for arbitrary native morphs.
        private static string VerifiedSetCallback(string from, string to)
        {
            switch (from)
            {
                case "H098": return to == "H08E" ? "Bjq" : null;
                case "H08V": return to == "H08B" ? "Bjq" : null;
                case "H05N": return to == "H0A5" ? "Bjq" : null;
                case "H09A": return to == "H0AY" ? "Bjq" : null;
                case "H096": return to == "H0BN" ? "Bjq" : null;
                case "H099": return to == "H0C9" ? "Bjq" : null;
                case "H0BD": return to == "H0BZ" ? "Bjq" : null;
                case "H08U": return to == "H0C1" ? "Bjq" : null;
                case "H09F": return to == "H0BT" ? "Bxc" : null;
                default: return null;
            }
        }

        // Call BEFORE a separately approved and committed source replacement.
        // A ticket never changes roster ownership, spends trait points or
        // authorizes an upgrade. No implicit preform snapshot from a cache.
        public Original323JassHeroFormTransfer CaptureJassSetHeroStats(
            long instanceId, string nextRawcode)
        {
            if (instanceId <= 0 || liveFormRevision == null) return null;
            var growth = liveLevels(instanceId);
            if (growth == null) return null;
            string callback = VerifiedSetCallback(growth.SourceRawcode, nextRawcode);
            if (callback == null) return null;
            long revision = FormRevisionOf(instanceId);
            var native = CurrentNativeHero(instanceId, growth.SourceRawcode, revision);
            double? strength = native == null
                ? ModelUnbonused(instanceId, growth, "strength", revision)
                : native.Read("strength", false);
            double? agility = native == null
                ? ModelUnbonused(instanceId, growth, "agility", revision)
                : native.Read("agility", false);
            double? intelligence = native == null
                ? ModelUnbonused(instanceId, growth, "intelligence", revision)
                : native.Read("intelligence", false);
            if (!ValidInteger(strength) || !ValidInteger(agility) ||
                !ValidInteger(intelligence) ||
                strength.Value < 0 || agility.Value < 0 || intelligence.Value < 0)
                return null;
            var unchanged = liveLevels(instanceId);
            if (unchanged == null || unchanged.SourceRawcode != growth.SourceRawcode ||
                unchanged.Experience != growth.Experience ||
                FormRevisionOf(instanceId) != revision) return null;
            return new Original323JassHeroFormTransfer(instanceId,
                growth.SourceRawcode, nextRawcode, callback,
                growth.Experience, revision, (int)strength.Value,
                (int)agility.Value, (int)intelligence.Value);
        }

        // Call AFTER an independently committed original replacement has
        // switched the existing mobile owner's source form exactly once.
        // The correction is to NEW-form initial+growth at transferred XP;
        // later XP adds only the new form's subsequent growth, never twice.
        public bool CommitJassSetHeroStats(Original323JassHeroFormTransfer ticket)
        {
            if (ticket == null || liveFormRevision == null ||
                VerifiedSetCallback(ticket.FromRawcode, ticket.ToRawcode) !=
                    ticket.OriginalJassCallback) return false;
            var current = liveLevels(ticket.InstanceId);
            if (current == null || current.SourceRawcode != ticket.ToRawcode ||
                current.Experience != ticket.Experience ||
                FormRevisionOf(ticket.InstanceId) !=
                    ticket.PreviousFormRevision + 1) return false;
            BaseAttributes to;
            if (!sourceBases.TryGetValue(ticket.ToRawcode, out to)) return false;
            transfers[ticket.InstanceId] = new FormTransfer {
                Rawcode = ticket.ToRawcode,
                Revision = FormRevisionOf(ticket.InstanceId),
                DeltaStrength = (long)ticket.Strength - to.Strength -
                    current.AddedStrength,
                DeltaAgility = (long)ticket.Agility - to.Agility -
                    current.AddedAgility,
                DeltaIntelligence = (long)ticket.Intelligence - to.Intelligence -
                    current.AddedIntelligence
            };
            return true;
        }

        // A removed/fused actor cannot donate old form-stat observations to
        // a future actor, even if its native rawcode is reused.
        public void ForgetOwnedHero(long ownedInstanceId)
        {
            transfers.Remove(ownedInstanceId);
        }
    }
}
