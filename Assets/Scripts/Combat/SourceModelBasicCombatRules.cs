using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Explicit development model. Does NOT implement a native-hit witness,
    // live monster approval, retail quantizer, or verified skill resolver.
    // The runtime's legacy damage interface is reused without granting retail
    // verification to this world. Source-object effects use a separate model catalog.
    public sealed class SourceModelBasicCombatRules : IOriginalDamageResolver,
        IVerifiedCharacterSkillDamageResolver, IOriginalVerifiedUnitAttackProvider
    {
        public const string Evidence = "MODEL_ONLY: original object attack dice/cooldown + map armor table; retail final combat unverified";
        private readonly OriginalMapBaseStatCatalog objects;
        private readonly OriginalMapMappedNativeAttackEvidenceCatalog attacks;
        private readonly ICombatProcRandom random;
        private readonly Dictionary<string, OriginalMapCommonNativeAttackEvidence> byRawcode =
            new Dictionary<string, OriginalMapCommonNativeAttackEvidence>();
        private readonly Dictionary<string, BasicAttackSpec> independentAttacks =
            new Dictionary<string, BasicAttackSpec>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> formRawcodes =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> fractions = new Dictionary<string, double>();
        public bool Enabled { get; private set; }
        public SourceModelUnitEffects UnitEffects { get; private set; }
        public void ConfigureUnitEffects(SourceModelUnitEffects effects)
        {
            if (Enabled || UnitEffects != null) throw new InvalidOperationException("연습 효과는 시작 전에 연결하세요.");
            UnitEffects = effects ?? throw new ArgumentNullException(nameof(effects));
        }

        public SourceModelBasicCombatRules(OriginalMapBaseStatCatalog source, ICombatProcRandom dice)
        {
            objects = source ?? throw new ArgumentNullException(nameof(source));
            random = dice ?? throw new ArgumentNullException(nameof(dice));
            if (source.SourceW3xSha256 != Ordr.Mobile.Core.OriginalCommonSaleRules.OriginalMapSha256)
                throw new ArgumentException("연습 전투의 원본 맵 자료가 다릅니다.");
            attacks = new OriginalMapMappedNativeAttackEvidenceCatalog(source);
            foreach (var row in attacks.All) byRawcode.Add(row.Rawcode, row);
            // Only explicit source-backed form/summon identities can join the
            // practice attacker path. Unlisted map objects stay ineligible.
            foreach (string raw in new[] { "H0B6", "h0AD", "H0AB", "H0AE", "H0AM", "h06N" })
            {
                var attack = objects.FindMapObjectAttack(raw);
                if (attack == null || !attack.BaseDamagePlus.HasValue ||
                    !attack.DiceCount.HasValue || !attack.DiceSides.HasValue ||
                    !attack.AttackCooldownSeconds.HasValue ||
                    !attack.AttackPointSeconds.HasValue ||
                    attack.AttackPointSeconds.Value > attack.AttackCooldownSeconds.Value)
                    throw new FormatException("원본 독립 공격 객체가 불완전합니다: " + raw);
                var evidence = new OriginalMapCommonNativeAttackEvidence(raw, raw, attack);
                byRawcode.Add(raw, evidence);
                independentAttacks.Add(raw, new BasicAttackSpec(
                    evidence.BaseDamagePlus + (long)evidence.DiceCount,
                    evidence.CooldownSeconds, DamageSchool.Physical, DamageTraits.None));
            }
        }
        internal BasicAttackSpec SourceObjectAttack(string rawcode)
        {
            BasicAttackSpec attack;
            return rawcode != null && independentAttacks.TryGetValue(rawcode, out attack)
                ? attack : null;
        }
        internal void SetSourceForm(string instanceId, string rawcode)
        {
            if (!Enabled || SourceObjectAttack(rawcode) == null)
                throw new InvalidOperationException("연습 변신 공격 출처가 없습니다.");
            formRawcodes[instanceId] = rawcode;
        }
        internal void ClearSourceForm(string instanceId) { formRawcodes.Remove(instanceId); }
        internal BasicAttackSpec FormAttackOf(string instanceId)
        {
            string raw;
            return formRawcodes.TryGetValue(instanceId, out raw)
                ? SourceObjectAttack(raw) : null;
        }
        public void Enable() { Enabled = true; }
        // Shared with the practice basic dice. This models source BFi/BFj
        // call order but is not a Warcraft-native RNG-seed claim.
        internal int NextScriptRoll(int exclusiveUpperBound)
        {
            if (!Enabled || exclusiveUpperBound <= 0)
                throw new InvalidOperationException("연습 공격 스크립트 난수 준비 전");
            int result = random.Next(exclusiveUpperBound);
            if (result < 0 || result >= exclusiveUpperBound)
                throw new InvalidOperationException("연습 공격 스크립트 난수 범위 오류");
            return result;
        }
        public void ForgetEnemy(string id) { fractions.Remove(id); }

        public bool TryResolve(CatalogUnit unit, CharacterSkillProfile profile,
            out OriginalVerifiedAttackBinding binding)
        {
            binding = null;
            if (!Enabled || unit == null) return false;
            var raw = objects.Find(unit.Id);
            var source = attacks.Find(unit.Id);
            if (raw == null || source == null || raw.Attack1 == null ||
                !raw.Attack1.AttackPointSeconds.HasValue) return false;
            binding = new OriginalVerifiedAttackBinding(new BasicAttackSpec(
                source.BaseDamagePlus + (long)source.DiceCount, source.CooldownSeconds,
                DamageSchool.Physical, DamageTraits.None), raw.Attack1.AttackPointSeconds.Value,
                source.Rawcode, Evidence);
            return true;
        }

        public long ResolveBasicDamage(BasicDamageRequest request, CombatEnemy target)
        {
            if (!Enabled) throw new NotSupportedException("연습 전투가 시작되지 않았습니다.");
            OriginalMapCommonNativeAttackEvidence attack;
            Ordr323ArmorType armor;
            string rawcode = request == null ? null : request.UnitRawCode;
            string transformed;
            if (request != null && formRawcodes.TryGetValue(request.UnitInstanceId, out transformed))
                rawcode = transformed;
            if (request == null || target == null || rawcode == null ||
                !byRawcode.TryGetValue(rawcode, out attack) ||
                !target.Defenses.BaseArmor.HasValue ||
                !Ordr323OriginalDamageRules.TryReadArmorType(target.Defenses.WarcraftArmorType, out armor))
                throw new NotSupportedException("출처가 있는 공격·적 방어 자료가 필요합니다.");
            foreach (var effect in request.AttackerEffects)
                if (!((effect.Type == CombatEffectType.AttackPowerBuff ||
                       effect.Type == CombatEffectType.AttackSpeedBuff) &&
                      effect.SourceId.StartsWith("practice-aura:", StringComparison.Ordinal)) &&
                    !(effect.Type == CombatEffectType.AttackSpeedBuff &&
                      effect.SourceId.StartsWith("practice-native:A08B:", StringComparison.Ordinal)) &&
                    !(effect.Type == CombatEffectType.AttackPowerBuff &&
                      effect.SourceId.StartsWith("practice-form:power:", StringComparison.Ordinal)) &&
                    !(effect.Type == CombatEffectType.AttackSpeedBuff &&
                      effect.SourceId.StartsWith("practice-form:interval:", StringComparison.Ordinal)))
                    throw new NotSupportedException("연습의 출처가 확인되지 않은 공격 효과입니다.");
            double rawDamage = attack.RollObjectBaseDamage(random) +
                SourceModelTemporarySkillCatalog.FlatFormPower(request.AttackerEffects);
            double power = SourceModelUnitEffects.ResolveSingleAura(request.AttackerEffects, CombatEffectType.AttackPowerBuff);
            rawDamage *= 1 + power / 100.0;
            if (UnitEffects != null) rawDamage = UnitEffects.BeforeBasicHit(request, target, rawDamage);
            double reduction = SourceModelUnitEffects.ResolvePracticeArmorReduction(target.Status.Snapshot());
            double damage = Ordr323MapDamageMath.ApplyVerifiedFactors(
                rawDamage, attack.NativeAttackType, armor,
                true, true, target.Defenses.BaseArmor.Value - reduction, false, 0, 1);
            // Preserve fractional damage across all attackers hitting the same
            // target. This is an explicit practice representation policy, NOT
            // a claim of Warcraft's floating-point HP rounding equivalence.
            double carry;
            fractions.TryGetValue(target.Id, out carry);
            double total = damage + carry;
            if (total >= long.MaxValue || double.IsNaN(total) || double.IsInfinity(total))
                throw new OverflowException("연습 피해량 범위를 초과했습니다.");
            long whole = (long)Math.Floor(total);
            fractions[target.Id] = total - whole;
            return whole;
        }
        public long ResolveDamage(CharacterSkillDamageRequest request)
        { throw new NotSupportedException("연습 전투의 미연결 스킬 피해는 계산하지 않습니다."); }
    }
}

