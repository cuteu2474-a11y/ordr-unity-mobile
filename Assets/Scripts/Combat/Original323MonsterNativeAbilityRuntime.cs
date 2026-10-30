using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // This is a CURRENT native-unit observation, not a copy of the base
    // abilities in UnitAbilities.slk. Scripts may add/remove native abilities.
    public sealed class Original323ObservedMonsterNativeAbilities
    {
        public string EnemyId { get; private set; }
        public string OriginalUnitRawcode { get; private set; }
        public string SourceW3xSha256 { get; private set; }
        public string NativeEvidence { get; private set; }
        public bool VisibleGhost { get; private set; }
        public bool MagicImmune { get; private set; }
        public bool Invulnerable { get; private set; }
        public bool ResistantSkin { get; private set; }
        public bool WarcraftHero { get; private set; }
        public bool BashImmune { get; private set; }

        public Original323ObservedMonsterNativeAbilities(string enemyId,
            string originalUnitRawcode, string originalW3xSha256,
            bool visibleGhost, bool magicImmune, bool invulnerable,
            bool resistantSkin, bool warcraftHero, bool bashImmune,
            string currentNativeEvidence)
        {
            if (string.IsNullOrWhiteSpace(enemyId) ||
                string.IsNullOrWhiteSpace(originalUnitRawcode) ||
                originalUnitRawcode.Length != 4 ||
                string.IsNullOrWhiteSpace(originalW3xSha256) ||
                string.IsNullOrWhiteSpace(currentNativeEvidence))
                throw new ArgumentException("현재 원본 몬스터 객체·능력 상태 증빙이 필요합니다.");
            EnemyId = enemyId;
            OriginalUnitRawcode = originalUnitRawcode;
            SourceW3xSha256 = originalW3xSha256;
            VisibleGhost = visibleGhost;
            MagicImmune = magicImmune;
            Invulnerable = invulnerable;
            ResistantSkin = resistantSkin;
            WarcraftHero = warcraftHero;
            BashImmune = bashImmune;
            NativeEvidence = currentNativeEvidence;
        }
    }

    public interface IOriginal323MonsterNativeAbilitiesWitness
    {
        bool TryObserveCurrent(CombatEnemy live,
            out Original323ObservedMonsterNativeAbilities observed);
    }

    // Returns the ACTUAL stun duration after Warcraft's hero/resistant-skin/
    // spell-specific resolution. A08Q membership cannot supply this number.
    public interface IOriginal323MonsterStunImpactWitness
    {
        bool TryObserveFinalStun(string impactId, string abilityRawcode,
            CombatEnemy liveTarget, out double nativeDurationSeconds,
            out string nativeImpactEvidence);
    }

    // One shared target-ability boundary for source-verified combat paths.
    // This does NOT invent damage, stun resistance percentages, collisions,
    // live ability flags, or a witness from the initial object catalogue.
    public sealed class Original323MonsterNativeAbilityRuntime
    {
        private readonly Original323MonsterObjectAbilityCatalog originalObjects;
        private readonly IOriginal323MonsterNativeAbilitiesWitness liveWitness;
        private readonly HashSet<string> consumedStunImpacts =
            new HashSet<string>(StringComparer.Ordinal);

        public Original323MonsterNativeAbilityRuntime(
            Original323MonsterObjectAbilityCatalog originalCatalog,
            IOriginal323MonsterNativeAbilitiesWitness currentNativeWitness)
        {
            originalObjects = originalCatalog ??
                throw new ArgumentNullException(nameof(originalCatalog));
            liveWitness = currentNativeWitness ??
                throw new ArgumentNullException(nameof(currentNativeWitness));
            if (originalObjects.Count != 100)
                throw new ArgumentException("원본 몬스터 능력 100종 자료가 필요합니다.");
        }

        // Object data is diagnostic. In particular Aeth (Ghost Visible)
        // is never treated as Aetl (Ethereal) or as magic immunity.
        public bool HasStartingAbility(string objectRawcode, string abilityRawcode)
        {
            return originalObjects.HasObjectBaseAbility(objectRawcode, abilityRawcode);
        }

        public bool TryInspect(CombatEnemy live,
            out Original323ObservedMonsterNativeAbilities observed)
        {
            observed = null;
            if (live == null || !live.IsAlive) return false;
            long health = live.Health;
            var defense = live.Defenses;
            var status = live.Status;
            long revision = status.Revision;
            double time = status.CurrentTime;
            long nativeRevision = live.NativePassiveTargetRevision;
            Original323ObservedMonsterNativeAbilities candidate;
            if (!liveWitness.TryObserveCurrent(live, out candidate) ||
                candidate == null || candidate.EnemyId != live.Id ||
                candidate.SourceW3xSha256 !=
                    Original323VerifiedMonsterWaveCatalog.SourceW3xSha256 ||
                !originalObjects.HasObject(candidate.OriginalUnitRawcode) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(
                    candidate.NativeEvidence) ||
                live.Health != health || !live.IsAlive ||
                !ReferenceEquals(live.Defenses, defense) ||
                !ReferenceEquals(live.Status, status) ||
                status.Revision != revision || status.CurrentTime != time ||
                live.NativePassiveTargetRevision != nativeRevision ||
                (live.NativePassiveTargetProfile != null &&
                 live.NativePassiveTargetProfile.OriginalUnitRawcode !=
                     candidate.OriginalUnitRawcode)) return false;
            observed = candidate;
            return true;
        }

        // Eligibility only. A separate source-verified resolver must still
        // calculate and commit final HP damage and native target selection.
        public bool TryCanReceiveNativeMagic(CombatEnemy target, out bool canHit)
        {
            canHit = false;
            Original323ObservedMonsterNativeAbilities state;
            if (!TryInspect(target, out state)) return false;
            canHit = !state.MagicImmune && !state.Invulnerable;
            return true;
        }

        // The live Bash flag is separate from Amim/Avul and from A08Q.
        // A story object with A08Q is NOT automatically stun-immune.
        public bool TryInspectBash(CombatEnemy target, out bool isHero,
            out bool bashImmune, out string evidence)
        {
            isHero = false;
            bashImmune = true;
            evidence = null;
            Original323ObservedMonsterNativeAbilities state;
            if (!TryInspect(target, out state)) return false;
            isHero = state.WarcraftHero;
            bashImmune = state.BashImmune;
            evidence = state.NativeEvidence;
            return true;
        }

        // Apply exactly the source-observed final stun, or no stun on a
        // source-observed zero-duration impact. Never infer an Arsk divisor.
        public bool TryApplyObservedStun(CombatEnemy target, string impactId,
            string abilityRawcode, IOriginal323MonsterStunImpactWitness impact,
            out bool applied)
        {
            applied = false;
            if (target == null || !target.IsAlive ||
                string.IsNullOrWhiteSpace(impactId) ||
                string.IsNullOrWhiteSpace(abilityRawcode) ||
                abilityRawcode.Length != 4 || impact == null ||
                consumedStunImpacts.Contains(impactId)) return false;

            long health = target.Health;
            var defense = target.Defenses;
            var status = target.Status;
            long revision = status.Revision;
            double now = status.CurrentTime;
            Original323ObservedMonsterNativeAbilities native;
            if (!TryInspect(target, out native)) return false;
            double duration;
            string evidence;
            if (!impact.TryObserveFinalStun(impactId, abilityRawcode, target,
                    out duration, out evidence) ||
                double.IsNaN(duration) || double.IsInfinity(duration) ||
                duration < 0 ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence) ||
                // Bash immunity only constrains confirmed native Bash.
                // Other stun spells must use their own observed outcome.
                (native.BashImmune &&
                 abilityRawcode == Original323UncommonBashWorld.AbilityRawcode &&
                 duration > 0) ||
                !target.IsAlive || target.Health != health ||
                !ReferenceEquals(target.Defenses, defense) ||
                !ReferenceEquals(target.Status, status) ||
                status.Revision != revision || status.CurrentTime != now)
                return false;

            // Existing stun stacking/refresh rules need their own native
            // observation. Do not combine timers using an invented formula.
            if (duration > 0 && status.IsStunned()) return false;
            if (duration > 0)
            {
                status.Apply(new CombatEffect("original-stun:" + impactId,
                    CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill,
                    1, duration));
                applied = true;
            }
            consumedStunImpacts.Add(impactId);
            return true;
        }
    }
}
