using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Independent proof of native buff basis/stacking/rounding is required.
    // Preflight is read-only and precedes dice; Apply changes only raw damage.
    // No default 7%-of-tooltip or final-HP multiplication is supplied.
    public interface IOriginal323NativeAttackBuffRules
    {
        bool CanResolve(BasicDamageRequest request, CombatEnemy target, out string evidence);
        bool TryApply(BasicDamageRequest request, CombatEnemy target,
            Ordr323VerifiedDamageHit original, out Ordr323VerifiedDamageHit modified);
    }

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

    // A native hit requiring Warcraft armor must not select an enemy whose
    // armor data are unknown. Target preflight never rolls damage dice or
    // supplies a substitute defense value.
    public interface IOriginalVerifiedBasicAttackTargetFilter
    {
        bool CanAttemptBasicAttack(CatalogUnit unit, CombatEnemy enemy);
    }

    // 승인/방어 미확정 사유를 읽는 전용 포트. 타격·주사위는 실행하지 않는다.
    public interface IOriginalBasicAttackEvidenceDiagnostics
    {
        string ExplainUnapprovedUnit(CatalogUnit unit);
        string ExplainRejectedTarget(CatalogUnit unit, CombatEnemy enemy);
    }

    public sealed class OriginalMapCommonNativeAttackBinding :
        IOriginalVerifiedUnitAttackProvider, IOrdr323VerifiedHitSource,
        IOriginalVerifiedBasicAttackTargetFilter,
        IOriginalBasicAttackEvidenceDiagnostics
    {
        private readonly OriginalMapMappedNativeAttackEvidenceCatalog evidence;
        private readonly IOriginalCommonNativeAttackVerification verification;
        private readonly ICombatProcRandom diceRandom;
        private readonly IOriginal323NativeAttackBuffRules attackBuffRules;
        private readonly Dictionary<string, OriginalMapCommonNativeAttackEvidence> byRawcode =
            new Dictionary<string, OriginalMapCommonNativeAttackEvidence>(StringComparer.Ordinal);

        public OriginalMapCommonNativeAttackBinding(
            OriginalMapBaseStatCatalog map,
            IOriginalCommonNativeAttackVerification verifiedNativePath,
            ICombatProcRandom nativeDiceRandom,
            IOriginal323NativeAttackBuffRules verifiedAttackBuffRules = null)
        {
            evidence = new OriginalMapMappedNativeAttackEvidenceCatalog(map);
            verification = verifiedNativePath ??
                throw new ArgumentNullException(nameof(verifiedNativePath));
            diceRandom = nativeDiceRandom ??
                throw new ArgumentNullException(nameof(nativeDiceRandom));
            attackBuffRules = verifiedAttackBuffRules;
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
            // A direct damage-resolver call must obey the same target safety
            // gate as the automated targeting path, before consuming RNG.
            Ordr323ArmorType armorType;
            if (!enemy.Defenses.HasVerifiedArmor ||
                !Ordr323OriginalDamageRules.TryReadArmorType(
                    enemy.Defenses.WarcraftArmorType, out armorType)) return false;
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
            bool powerBuff = false;
            foreach (var effect in request.AttackerEffects)
                if (effect.Type == CombatEffectType.AttackPowerBuff) powerBuff = true;
            if (powerBuff)
            {
                string proof;
                if (attackBuffRules == null ||
                    !attackBuffRules.CanResolve(request, enemy, out proof) ||
                    string.IsNullOrWhiteSpace(proof)) return false;
            }
            long rawDamage = native.RollObjectBaseDamage(diceRandom);
            hit = new Ordr323VerifiedDamageHit(rawDamage,
                native.NativeAttackType, true, true, false,
                DamageTraits.None,
                approvalEvidence + " / original UnitWeapons.slk:" + native.Rawcode,
                school);
            if (powerBuff)
            {
                Ordr323VerifiedDamageHit modified;
                if (!attackBuffRules.TryApply(request, enemy, hit, out modified) ||
                    modified == null) { hit = null; return false; }
                if (modified.AttackType != hit.AttackType ||
                    modified.School != hit.School || modified.Traits != hit.Traits ||
                    modified.UseAttackTable != hit.UseAttackTable ||
                    modified.UseArmorRating != hit.UseArmorRating ||
                    modified.UseMagicResistance != hit.UseMagicResistance)
                    throw new InvalidOperationException("공격력 버프가 원본 피해 유형을 변경했습니다.");
                hit = modified;
            }
            return true;
        }

        public string ExplainUnapprovedUnit(CatalogUnit unit)
        {
            if (unit == null) return "카탈로그 유닛 누락";
            var native = evidence.Find(unit.Id);
            return native == null
                ? "원본 평타 객체/주사위·쿨타임 근거 미확인"
                : "원본 네이티브 공격 경로·첫 타격 시점·피해 계열의 독립 검증 대기: " + native.Rawcode;
        }

        public string ExplainRejectedTarget(CatalogUnit unit, CombatEnemy enemy)
        {
            if (enemy == null || !enemy.IsAlive) return "대상이 없거나 사망";
            if (unit == null || evidence.Find(unit.Id) == null)
                return "공격자의 원본 기본 공격 자료 미확정";
            if (!enemy.Defenses.HasVerifiedArmor)
                return "대상 " + enemy.Id + "의 원본 방어력 미검증";
            Ordr323ArmorType armor;
            if (!Ordr323OriginalDamageRules.TryReadArmorType(
                enemy.Defenses.WarcraftArmorType, out armor))
                return "대상 " + enemy.Id + "의 워크래프트 방어 타입 미검증";
            return "원본 대상 승인 조건 미충족";
        }

        public bool CanAttemptBasicAttack(CatalogUnit unit, CombatEnemy enemy)
        {
            if (unit == null || enemy == null || !enemy.IsAlive) return false;
            OriginalMapCommonNativeAttackEvidence native = evidence.Find(unit.Id);
            if (native == null || !enemy.Defenses.HasVerifiedArmor) return false;
            Ordr323ArmorType armorType;
            if (!Ordr323OriginalDamageRules.TryReadArmorType(
                enemy.Defenses.WarcraftArmorType, out armorType)) return false;
            double delay;
            DamageSchool school;
            string proof;
            return verification.TryVerify(unit.Id, native.Rawcode,
                out delay, out school, out proof) &&
                !string.IsNullOrWhiteSpace(proof) &&
                Enum.IsDefined(typeof(DamageSchool), school);
        }

        public bool TryResolveSkill(CharacterSkillDamageRequest request,
            out Ordr323VerifiedDamageHit hit)
        {
            hit = null;
            return false; // No tooltip-only skill is promoted to a verified hit.
        }
    }
}
