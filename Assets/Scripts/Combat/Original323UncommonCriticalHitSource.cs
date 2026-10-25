using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // This provider must prove that A07I's Warcraft crit is eligible on the
    // particular target and that its multiplier belongs before the map's
    // armor/HP quantization. It must be read-only: the native dice and proc
    // RNG are managed by the shared attack source, not by a UI callback.
    public interface IOriginal323UncommonCriticalPrehitRules
    {
        bool TryAuthorize(OriginalUncommonProc proc, BasicDamageRequest attack,
            CombatEnemy target, out string sourceEvidence);
    }

    // The original WC3 Critical Strike multiplier applies to the unquantized
    // native attack roll, before armor/attack-type/HP conversion. Applying a
    // second OnHit damage packet would not match the original interaction.
    //
    // This opt-in decorator only handles A07I:critical on h00M/h00A.
    // A07I:rankyaku is a *separate* 10% line hit handled downstream.
    public sealed class Original323UncommonCriticalHitSource :
        IOrdr323VerifiedHitSource, IOrdr323VerifiedSkillLookup
    {
        private readonly IOrdr323VerifiedHitSource original;
        private readonly IOriginal323UncommonCriticalPrehitRules rules;
        private readonly ICombatProcRandom random;
        private readonly Dictionary<string, OriginalUncommonProc> byRawcode =
            new Dictionary<string, OriginalUncommonProc>(StringComparer.Ordinal);

        public Original323UncommonCriticalHitSource(
            IOrdr323VerifiedHitSource originalHits,
            Original323UncommonAbilityCatalog uncommon,
            IOriginal323UncommonCriticalPrehitRules verifiedPrehitRules,
            ICombatProcRandom originalProcRandom)
        {
            original = originalHits ?? throw new ArgumentNullException(nameof(originalHits));
            if (uncommon == null) throw new ArgumentNullException(nameof(uncommon));
            rules = verifiedPrehitRules ??
                throw new ArgumentNullException(nameof(verifiedPrehitRules));
            random = originalProcRandom ??
                throw new ArgumentNullException(nameof(originalProcRandom));
            foreach (string id in new[] { "U-04", "U-13" })
            {
                var proc = uncommon.FindProc(id, "A07I:critical");
                if (proc == null || proc.Kind != OriginalUncommonProcKind.Critical ||
                    proc.AbilityRawcode != "A07I" ||
                    proc.ChancePerMillion != 150000 ||
                    proc.CriticalMultiplier != 1.5 ||
                    (id == "U-04" && proc.OriginalUnitRawcode != "h00M") ||
                    (id == "U-13" && proc.OriginalUnitRawcode != "h00A"))
                    throw new FormatException("원본 A07I 크리티컬 수치/ID 불일치");
                byRawcode.Add(proc.OriginalUnitRawcode, proc);
            }
        }

        public bool TryResolveBasic(BasicDamageRequest request,
            CombatEnemy target, out Ordr323VerifiedDamageHit hit)
        {
            hit = null;
            if (!original.TryResolveBasic(request, target, out hit) || hit == null)
                return false;
            OriginalUncommonProc proc;
            if (request == null || !byRawcode.TryGetValue(
                request.UnitRawCode ?? "", out proc)) return true;
            string evidence;
            if (!rules.TryAuthorize(proc, request, target, out evidence) ||
                string.IsNullOrWhiteSpace(evidence))
                return true; // Keep approved vanilla hit, consume no crit RNG.

            int roll = random.Next(1000000);
            if (roll < 0 || roll >= 1000000)
                throw new InvalidOperationException("원본 크리티컬 난수 범위 오류");
            if (roll >= proc.ChancePerMillion)
                return true;

            double enhanced = hit.RawDamage * proc.CriticalMultiplier;
            if (double.IsNaN(enhanced) || double.IsInfinity(enhanced))
                throw new OverflowException("원본 크리티컬 원시 피해 초과");
            hit = new Ordr323VerifiedDamageHit(enhanced, hit.AttackType,
                hit.UseAttackTable, hit.UseArmorRating, hit.UseMagicResistance,
                hit.Traits, hit.Evidence + " / W3X A07I pre-armor x1.5 / " +
                evidence, hit.School);
            return true;
        }

        public bool TryResolveSkill(CharacterSkillDamageRequest request,
            out Ordr323VerifiedDamageHit hit)
        {
            return original.TryResolveSkill(request, out hit);
        }

        public bool CanResolveSkillComponent(CharacterSkillProfile profile,
            ImportedCombatSkill skill, ImportedCombatEffect effect)
        {
            var lookup = original as IOrdr323VerifiedSkillLookup;
            return lookup != null &&
                lookup.CanResolveSkillComponent(profile, skill, effect);
        }
    }
}
