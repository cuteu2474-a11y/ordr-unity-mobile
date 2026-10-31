using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Development model only. Never supplies a retail hit/target/rank witness.
    // All values come from the existing source-audited object catalogs.
    public sealed class SourceModelUnitEffects
    {
        private readonly Original323NativePassiveCatalog passives;
        private readonly IOriginal323AuraCatalog auras;
        private readonly ICombatProcRandom random;
        private readonly Dictionary<string, OriginalNativePassive> pending =
            new Dictionary<string, OriginalNativePassive>(StringComparer.Ordinal);
        private readonly HashSet<string> practiceTargets = new HashSet<string>(StringComparer.Ordinal);
        internal void RegisterPracticeTarget(string id) { practiceTargets.Add(id); }
        internal void ForgetPracticeTarget(string id) { practiceTargets.Remove(id); }
        internal bool IsPracticeTarget(string id) => practiceTargets.Contains(id);
        private readonly bool sourceFlatArmorMode;
        public int DeferredAuraGroups { get; internal set; }
        public SourceModelUnitEffects(Original323NativePassiveCatalog nativePassives,
            IOriginal323AuraCatalog sourceAuras, ICombatProcRandom procRandom, string inheritedDefaultsJson = null)
        {
            passives = nativePassives ?? throw new ArgumentNullException(nameof(nativePassives));
            auras = sourceAuras ?? throw new ArgumentNullException(nameof(sourceAuras));
            random = procRandom ?? throw new ArgumentNullException(nameof(procRandom));
            if (inheritedDefaultsJson != null)
            {
                var root = OrdrJson.Object(OrdrJson.Parse(inheritedDefaultsJson), "native inheritance");
                if (OrdrJson.RequiredString(root, "format") != "ordr-practice-native-inheritance-v1" ||
                    OrdrJson.RequiredString(root, "sourceW3xSha256") != Original323UncommonAbilityCatalog.SourceW3xSha256 ||
                    OrdrJson.RequiredString(root, "sourceBaseAbilityDataSha256") !=
                        "ddc360800784f808219bbc2518b16b0f12413082aada2755cc00944adcc31cec" ||
                    OrdrJson.Number(root, "AHadFlatArmorMode") != 0 ||
                    !Equals(OrdrJson.Value(root, "safeForRetailVerifiedRuntime"), false))
                    throw new FormatException("네이티브 상속 필드 출처 불일치");
                sourceFlatArmorMode = true;
            }
        }
        public OriginalNativePassive PassiveFor(string rawcode) => passives.ForRawcode(rawcode) ??
            passives.PracticeFirstLevelForRawcode(rawcode) ?? passives.PracticeBonusForRawcode(rawcode);
        public IReadOnlyList<Original323SpecialAura> AurasFor(string id) => auras.FindAll(id);
        public IReadOnlyList<string> Describe(string id, string rawcode)
        {
            var result = new List<string>();
            var passive = PassiveFor(rawcode);
            if (passive != null) result.Add(passive.Name + " · 객체 확률 " +
                (passive.ChancePerMillion / 10000.0).ToString("0.####") + "%" +
                (passive.Kind == OriginalNativePassiveKind.Stun ? " · 일반 몬스터 / 보스 저항 검증 대기" :
                    (passive.Kind == OriginalNativePassiveKind.CriticalStun || passive.Kind == OriginalNativePassiveKind.BonusStun || passive.Kind == OriginalNativePassiveKind.CriticalBonusStun) ?
                    " · 일반 몬스터 기절 / 보스 저항 검증 대기" : "") +
                (passives.PracticeFirstLevelForRawcode(rawcode) != null ? " · 연습 1단계 / 강화 미반영" : "") +
                (passive.DeclaredLevels > 1 ? " · 연습 1단계 / 강화 미반영" : "") +
                (passive.BonusDamage > 0 ? " · 객체 추가 피해 " + passive.BonusDamage.ToString("0.####") +
                    " / 연습 계산·원작 면역 미검증" : ""));
            if (id == "U-01") result.Add("꽃꽃 열매 · 같은 전장 최대 3명 타격 (연습 대상 선택/동시 타격 모델)");
            foreach (var aura in AurasFor(id))
                if (IsSupportedAura(aura)) result.Add((aura.Type == CombatEffectType.AttackSpeedBuff &&
                    aura.TargetMask == "invulnerable,self" ? "자신의 공격속도 증가(%)" : AuraName(aura.Type)) + " " +
                    aura.Amount.ToString("0.####") + " · 1단계 / 중첩 충돌 시 보류");
            SourceScriptAttackSpec script;
            if (SourceModelScriptAttackCatalog.TryGet(rawcode, out script) &&
                script.CatalogId == id)
                result.Add("공격 선언 기술 · " +
                    (100.0 * script.RollSuccess / script.RollSides).ToString("0.####") +
                    "% 발동 · 연습 피해/범위 모델, 보조 주문·면역·원작 동등성 미검증");
            return result.AsReadOnly();
        }
        public static string AuraName(CombatEffectType type)
        {
            switch (type)
            {
                case CombatEffectType.ArmorReduction: return "적 방어력 감소";
                case CombatEffectType.MovementSlowPercent: return "적 이동속도 감소(%)";
                case CombatEffectType.AttackPowerBuff: return "아군 기본 공격력 증가(%)";
                case CombatEffectType.AttackSpeedBuff: return "아군 공격속도 증가(%)";
                default: return type.ToString();
            }
        }
        internal bool IsSupportedAura(Original323SpecialAura aura)
        {
            if (aura.Level != 1) return false;
            // Koby owns another speed aura with unresolved W3A overrides;
            // applying only one could silently invent its stacking result.
            if (aura.Type == CombatEffectType.AttackSpeedBuff && aura.UnitId == "TR-24") return false;
            if (aura.Type == CombatEffectType.AttackSpeedBuff &&
                aura.TargetMask != "air,invulnerable,self,ground,vulnerable,friend" &&
                aura.TargetMask != "air,invulnerable,notself,ground,vulnerable,friend" &&
                aura.TargetMask != "invulnerable,self") return false;
            if (!aura.TargetsAllies)
            {
                bool air = false, enemy = false;
                foreach (string token in aura.TargetMask.Split(','))
                {
                    if (token == "air") air = true;
                    if (token == "enemies") enemy = true;
                    if (token != "air" && token != "ground" && token != "enemies" && token != "neutral") return false;
                }
                if (!air || !enemy) return false;
            } // Dynamic rank upgrades are NOT assumed.
            if (aura.Type == CombatEffectType.ArmorReduction)
                return aura.RawArmorPercentageMode.HasValue ? aura.RawArmorPercentageMode.Value == 0 : sourceFlatArmorMode;
            return aura.Type == CombatEffectType.MovementSlowPercent || aura.Type == CombatEffectType.AttackPowerBuff ||
                aura.Type == CombatEffectType.AttackSpeedBuff;
        }
        internal double BeforeBasicHit(BasicDamageRequest request, CombatEnemy target, double raw)
        {
            string key = request.UnitInstanceId + ":" + target.Id;
            pending.Remove(key);
            var passive = PassiveFor(request.UnitRawCode);
            if (!practiceTargets.Contains(target.Id) || passive == null || (target.Defenses.Kind != CharacterEnemyKind.Line &&
                target.Defenses.Kind != CharacterEnemyKind.Boss)) return raw;
            // The development combat path treats its line/boss targets as air.
            // Boss Arsk/JASS resistance has no approved reduction equation;
            // model stuns therefore only affect ordinary line monsters.
            if (passive.Kind == OriginalNativePassiveKind.Stun &&
                target.Defenses.Kind != CharacterEnemyKind.Line) return raw;
            bool air = false;
            foreach (string token in passive.OriginalTargetMask.Split(',')) if (token == "air") air = true;
            if (!air) return raw;
            int roll = random.Next(1000000);
            if (roll < 0 || roll >= 1000000) throw new InvalidOperationException("패시브 난수 범위 오류");
            if (roll >= passive.ChancePerMillion) return raw;
            pending[key] = passive;
            if (passive.Kind == OriginalNativePassiveKind.Critical ||
                passive.Kind == OriginalNativePassiveKind.CriticalStun ||
                passive.Kind == OriginalNativePassiveKind.CriticalBonus ||
                passive.Kind == OriginalNativePassiveKind.CriticalBonusStun)
                return raw * passive.CriticalMultiplier + passive.BonusDamage;
            // Warsmash reference pre-damage bonus path: bonus is added to the
            // attack packet before the model's armor calculation. This is not
            // a claim about retail Warcraft bonus school or final HP parity.
            return raw + passive.BonusDamage;
        }
        internal OriginalNativePassive Take(string actor, string target)
        {
            string key = actor + ":" + target;
            OriginalNativePassive skill;
            pending.TryGetValue(key, out skill);
            pending.Remove(key);
            return skill;
        }
        internal static double ResolveSingleAura(IReadOnlyList<CombatEffect> effects, CombatEffectType type)
        {
            // Only one distinct source aura identity is admitted per type.
            // Duplicate identical casters do not stack. Mixed identities were
            // filtered out before emission, rather than guessing sum/max.
            double value = 0;
            foreach (var effect in effects)
                if (effect.Type == type && effect.SourceId.StartsWith("practice-aura:", StringComparison.Ordinal))
                    value = effect.Amount;
            return value;
        }
    }
}

