using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Original Warcraft/JASS source cast. This is an observation, NOT a
    // cooldown, proc chance or targeting formula inferred from tooltips.
    public sealed class Original323ObservedSpecialRareNativeCast
    {
        public string CastId { get; private set; }
        public long ActorId { get; private set; }
        public string CatalogUnitId { get; private set; }
        public string OriginalUnitRawcode { get; private set; }
        public string OriginalAbilityRawcode { get; private set; }
        public string OriginalW3xSha256 { get; private set; }
        public double OriginalCastTime { get; private set; }
        public int ObservedNativeImpactCount { get; private set; }
        public string SourceCastEvidence { get; private set; }

        public Original323ObservedSpecialRareNativeCast(string castId, long actorId,
            string catalogUnitId, string originalUnitRawcode,
            string originalAbilityRawcode, string originalW3xSha256,
            double observedCastTime, int sourceVerifiedImpactCount,
            string nativeCastEvidence)
        {
            if (string.IsNullOrWhiteSpace(castId) || castId.Length > 256 ||
                actorId <= 0 || string.IsNullOrWhiteSpace(catalogUnitId) ||
                originalUnitRawcode == null || originalUnitRawcode.Length != 4 ||
                originalAbilityRawcode == null || originalAbilityRawcode.Length != 4 ||
                string.IsNullOrWhiteSpace(originalW3xSha256) ||
                double.IsNaN(observedCastTime) ||
                double.IsInfinity(observedCastTime) || observedCastTime < 0 ||
                sourceVerifiedImpactCount < 1 || sourceVerifiedImpactCount > 128 ||
                string.IsNullOrWhiteSpace(nativeCastEvidence))
                throw new ArgumentException(
                    "원본 스킬 시전 신원·타격 횟수·실제 시전 근거가 부족합니다.");
            CastId = castId;
            ActorId = actorId;
            CatalogUnitId = catalogUnitId;
            OriginalUnitRawcode = originalUnitRawcode;
            OriginalAbilityRawcode = originalAbilityRawcode;
            OriginalW3xSha256 = originalW3xSha256;
            OriginalCastTime = observedCastTime;
            ObservedNativeImpactCount = sourceVerifiedImpactCount;
            SourceCastEvidence = nativeCastEvidence;
        }
    }

    // One independently witnessed native impact of a direct/delayed/periodic
    // attack. Impact ordinal is SOURCE observed, never guessed by Unity time.
    public sealed class Original323ObservedSpecialRareNativeSequenceImpact
    {
        public string CastId { get; private set; }
        public int SourceImpactOrdinal { get; private set; }
        public Original323SpecialRareObservedImpact Impact { get; private set; }
        public string OriginalImpactEvidence { get; private set; }

        public Original323ObservedSpecialRareNativeSequenceImpact(string castId,
            int sourceImpactOrdinal, Original323SpecialRareObservedImpact impact,
            string observedNativeImpactEvidence)
        {
            if (string.IsNullOrWhiteSpace(castId) ||
                sourceImpactOrdinal < 1 || impact == null ||
                string.IsNullOrWhiteSpace(observedNativeImpactEvidence))
                throw new ArgumentException("원본 후속 타격 식별·순서·근거가 없습니다.");
            CastId = castId;
            SourceImpactOrdinal = sourceImpactOrdinal;
            Impact = impact;
            OriginalImpactEvidence = observedNativeImpactEvidence;
        }
    }

    // This provider must observe ACTUAL Warcraft cast ordering and contact
    // or tick timing, collision, immunity, final integer HP/stun, and native
    // cast completion. Source observation must be read-only.
    public interface IOriginal323SpecialRareNativeSequenceWitness
    {
        bool TryObserveNativeCast(string castId, CharacterCombatRuntime live,
            out Original323ObservedSpecialRareNativeCast sourceCast);
        bool TryObserveNextNativeImpact(
            Original323ObservedSpecialRareNativeCast sourceCast,
            int nextSourceOrdinal, CharacterCombatRuntime live,
            out Original323ObservedSpecialRareNativeSequenceImpact nativeImpact);
    }

    // Specific active-skill lifecycle for source-observed casts in special
    // and rare classes, e.g. Enel A084 / Doflamingo A057. One original cast
    // may have multiple separate impacts against the SAME enemy at DIFFERENT
    // native times. This adds no invented effects: only existing canonical
    // CombatResolution damage + stun can be committed.
    public sealed class Original323SpecialRareNativeSequenceWorld
    {
        private sealed class ActiveCast
        {
            public Original323ObservedSpecialRareNativeCast Source;
            public int NextOrdinal = 1;
            public double LastImpactTime;
        }

        private readonly CharacterCombatRuntime combat;
        private readonly OriginalMapBaseStatCatalog originalUnits;
        private readonly IOriginal323SpecialRareNativeSequenceWitness source;
        private readonly IOriginal323SpecialRareImpactWitness impactIdentity;
        private readonly Dictionary<string, ActiveCast> pending =
            new Dictionary<string, ActiveCast>(StringComparer.Ordinal);
        private readonly HashSet<string> knownCasts =
            new HashSet<string>(StringComparer.Ordinal);
        private bool insideSourceObservation;

        public int PendingCastCount { get { return pending.Count; } }

        public Original323SpecialRareNativeSequenceWorld(
            CharacterCombatRuntime liveCombat, OriginalMapBaseStatCatalog originalMap,
            IOriginal323SpecialRareNativeSequenceWitness nativeSequence,
            IOriginal323SpecialRareImpactWitness nativeImpactIdentity)
        {
            combat = liveCombat ?? throw new ArgumentNullException(nameof(liveCombat));
            originalUnits = originalMap ?? throw new ArgumentNullException(nameof(originalMap));
            source = nativeSequence ?? throw new ArgumentNullException(nameof(nativeSequence));
            impactIdentity = nativeImpactIdentity ??
                throw new ArgumentNullException(nameof(nativeImpactIdentity));
            if (originalUnits.SourceW3xSha256 !=
                Original323VerifiedMonsterWaveCatalog.SourceW3xSha256)
                throw new ArgumentException("원본 S2 2.323[C] 맵 지문이 일치하지 않습니다.");
        }

        public bool TryRegisterCast(string castId)
        {
            if (insideSourceObservation || string.IsNullOrWhiteSpace(castId) ||
                castId.Length > 256 || knownCasts.Contains(castId) ||
                combat.Game.Phase != SessionPhase.Running ||
                knownCasts.Count >= 100000)
                return false;
            double clock = combat.Time;
            int actors = combat.CharacterCount;
            int monsters = combat.EnemyCount;
            int defeated = combat.Game.DefeatedEnemies;
            Original323ObservedSpecialRareNativeCast native;
            insideSourceObservation = true;
            try
            {
                if (!source.TryObserveNativeCast(castId, combat, out native))
                    return false;
                if (combat.Time != clock || combat.CharacterCount != actors ||
                    combat.EnemyCount != monsters ||
                    combat.Game.DefeatedEnemies != defeated)
                    return false;
                if (native == null || native.CastId != castId ||
                    native.OriginalCastTime != clock ||
                    native.OriginalW3xSha256 != originalUnits.SourceW3xSha256)
                    return false;
                var owner = originalUnits.Find(native.CatalogUnitId);
                if (owner == null ||
                    owner.OriginalRawcode != native.OriginalUnitRawcode)
                    return false;
                string grade = null;
                try
                {
                    var actual = combat.Roster.Get(native.ActorId);
                    if (actual.UnitId != native.CatalogUnitId) return false;
                    // CatalogUnit grades are bound by original source catalog.
                    foreach (var view in combat.MobileUnitSnapshot())
                        if (view.InstanceId == native.ActorId)
                        {
                            grade = view.Unit.Grade;
                            break;
                        }
                }
                catch (KeyNotFoundException) { return false; }
                if (grade != "특별함" && grade != "희귀함")
                    return false;
                bool abilityOwned = false;
                foreach (var code in owner.OriginalAbilityRawcodes)
                    if (code == native.OriginalAbilityRawcode)
                        abilityOwned = true;
                foreach (var craft in owner.SourceJassFusionAbilityRawcodes)
                    if (craft == native.OriginalAbilityRawcode)
                        return false;
                if (!abilityOwned || IsForbiddenSkill(native.OriginalAbilityRawcode))
                    return false;

                string nativeIdentity;
                if (!impactIdentity.TryVerifyCurrentCasterIdentity(native.ActorId,
                        native.OriginalUnitRawcode, out nativeIdentity) ||
                    string.IsNullOrWhiteSpace(nativeIdentity) ||
                    combat.Time != clock ||
                    combat.CharacterCount != actors ||
                    combat.EnemyCount != monsters ||
                    combat.Game.DefeatedEnemies != defeated)
                    return false;

                knownCasts.Add(castId);
                pending.Add(castId, new ActiveCast {
                    Source = native, LastImpactTime = clock
                });
                return true;
            }
            finally { insideSourceObservation = false; }
        }

        private static bool IsForbiddenSkill(string raw)
        {
            // The existing original ATTACKED dummy executor owns A086/A0VS.
            // Native move, craft, invulnerability etc are not damage spells.
            return raw == "A086" || raw == "A0VS" ||
                raw == "A07Y" || raw == "A0JE" ||
                raw == "A0B8" || raw == "Avul" ||
                raw == "A029";
        }

        public bool TryResolveNextImpact(string castId)
        {
            ActiveCast cast;
            if (insideSourceObservation ||
                string.IsNullOrWhiteSpace(castId) ||
                !pending.TryGetValue(castId, out cast) ||
                combat.Game.Phase != SessionPhase.Running ||
                cast.NextOrdinal > cast.Source.ObservedNativeImpactCount)
                return false;
            double time = combat.Time;
            int actors = combat.CharacterCount;
            int monsters = combat.EnemyCount;
            int defeated = combat.Game.DefeatedEnemies;
            Original323ObservedSpecialRareNativeSequenceImpact report;
            insideSourceObservation = true;
            try
            {
                if (!source.TryObserveNextNativeImpact(
                        cast.Source, cast.NextOrdinal, combat, out report))
                    return false;
                if (time != combat.Time || actors != combat.CharacterCount ||
                    monsters != combat.EnemyCount ||
                    defeated != combat.Game.DefeatedEnemies ||
                    report == null || report.CastId != castId ||
                    report.SourceImpactOrdinal != cast.NextOrdinal)
                    return false;
                var impact = report.Impact;
                if (impact == null ||
                    impact.CasterInstanceId != cast.Source.ActorId ||
                    impact.CasterCatalogId != cast.Source.CatalogUnitId ||
                    impact.CasterOriginalRawcode != cast.Source.OriginalUnitRawcode ||
                    impact.AbilityRawcode != cast.Source.OriginalAbilityRawcode ||
                    impact.OriginalW3xSha256 != cast.Source.OriginalW3xSha256 ||
                    impact.ObservedImpactTime != time ||
                    time < cast.LastImpactTime)
                    return false;

                // Commit via the SAME verified source observed runtime route.
                // It records the unique sourceEventId only once, applies
                // original final HP / stun and updates death/assist ledgers.
                if (!combat.TryCommitOriginal323SpecialRareImpact(
                        impact.SourceEventId, impact))
                    return false;
                cast.LastImpactTime = time;
                cast.NextOrdinal++;
                if (cast.NextOrdinal > cast.Source.ObservedNativeImpactCount)
                    pending.Remove(castId);
                return true;
            }
            finally { insideSourceObservation = false; }
        }
    }
}
