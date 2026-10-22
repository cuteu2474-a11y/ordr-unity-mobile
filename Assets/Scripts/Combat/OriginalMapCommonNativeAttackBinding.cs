using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Native object stats alone do not prove the first swing delay, JASS damage
    // replacement, splash behavior or whether attacks stay in the native path.
    // This gate must be implemented with external evidence before real HP can
    // be affected; an absent gate cannot silently enable the prototype.
    public interface IOriginalCommonNativeAttackVerification
    {
        bool TryVerify(string catalogId, string originalRawcode,
            out double firstAttackDelaySeconds, out DamageSchool school,
            out string originalEvidence);
    }

    public sealed class OriginalMapCommonNativeAttackBinding :
        IOriginalVerifiedUnitAttackProvider, IOrdr323VerifiedHitSource
    {
        private readonly OriginalMapMappedNativeAttackEvidenceCatalog evidence;
        private readonly IOriginalCommonNativeAttackVerification verification;
        private readonly ICombatProcRandom diceRandom;
        private readonly Dictionary<string, OriginalMapCommonNativeAttackEvidence> byRawcode =
            new Dictionary<string, OriginalMapCommonNativeAttackEvidence>(StringComparer.Ordinal);

        public OriginalMapCommonNativeAttackBinding(
            OriginalMapBaseStatCatalog map,
            IOriginalCommonNativeAttackVerification verifiedNativePath,
            ICombatProcRandom nativeDiceRandom)
        {
            evidence = new OriginalMapMappedNativeAttackEvidenceCatalog(map);
            verification = verifiedNativePath ??
                throw new ArgumentNullException(nameof(verifiedNativePath));
            diceRandom = nativeDiceRandom ??
                throw new ArgumentNullException(nameof(nativeDiceRandom));
            foreach (var mapped in evidence.All)
                byRawcode.Add(mapped.Rawcode, mapped);
        }

        public bool TryResolve(CatalogUnit unit, CharacterSkillProfile detailed,
            out OriginalVerifiedAttackBinding original)
        {
            original = null;
            if (unit == null) return false;
            var native = evidence.Find(unit.Id);
            if (native == null) return false;
            double firstDelay;
            DamageSchool school;
            string approvalEvidence;
            if (!verification.TryVerify(unit.Id, native.Rawcode,
                out firstDelay, out school, out approvalEvidence)) return false;
            if (string.IsNullOrWhiteSpace(approvalEvidence) ||
                !Enum.IsDefined(typeof(DamageSchool), school))
                throw new InvalidOperationException(
                    "원본 공격 승인에 피해 계열/검증 출처가 빠졌습니다.");
            original = new OriginalVerifiedAttackBinding(
                new BasicAttackSpec(native.BaseDamagePlus + (long)native.DiceCount,
                    native.CooldownSeconds, school, DamageTraits.None),
                firstDelay, native.Rawcode,
                approvalEvidence + " / original UnitWeapons.slk:" + native.Rawcode);
            return true;
        }

        public bool TryResolveBasic(BasicDamageRequest request,
            CombatEnemy enemy, out Ordr323VerifiedDamageHit hit)
        {
            hit = null;
            if (request == null || enemy == null) return false;
            OriginalMapCommonNativeAttackEvidence native;
            if (request.UnitRawCode == null ||
                !byRawcode.TryGetValue(request.UnitRawCode, out native)) return false;
            double firstDelay;
            DamageSchool school;
            string approvalEvidence;
            if (!verification.TryVerify(native.UnitId, native.Rawcode,
                out firstDelay, out school, out approvalEvidence)) return false;
            if (string.IsNullOrWhiteSpace(approvalEvidence) ||
                !Enum.IsDefined(typeof(DamageSchool), school))
                throw new InvalidOperationException("원본 공격 피해 계열/검증 근거 누락");

            // Original Warcraft object attack and armor table, *provided*
            // the independent verification attests the native path. Target
            // armor/type and final HP rounding are still validated by
            // Ordr323OriginalDamageRules and its supplied quantizer.
            long rawDamage = native.RollObjectBaseDamage(diceRandom);
            hit = new Ordr323VerifiedDamageHit(rawDamage,
                native.NativeAttackType, true, true, false,
                DamageTraits.None,
                approvalEvidence + " / original UnitWeapons.slk:" + native.Rawcode,
                school);
            return true;
        }

        public bool TryResolveSkill(CharacterSkillDamageRequest request,
            out Ordr323VerifiedDamageHit hit)
        {
            hit = null;
            return false; // No tooltip-only skill is promoted to a verified hit.
        }
    }
}
