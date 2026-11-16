using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Only the offending status may unblock an otherwise unverified attack.
    internal sealed class UnsupportedPracticeAttackerStatusException : NotSupportedException
    {
        internal string StatusSourceId { get; private set; }

        internal UnsupportedPracticeAttackerStatusException(string sourceId)
            : base("연습의 출처가 확인되지 않은 공격 효과입니다.")
        {
            StatusSourceId = sourceId;
        }
    }

    // Damage interval shown in the selected-unit HUD. This is the actual
    // source-model ATTACKER packet before native procs, enemy armor, damage
    // quantization and additional multi-target effects. It is never a
    // promise of final target HP reduction or retail Warcraft parity.
    internal sealed class SourceModelBasicAttackPowerPreview
    {
        internal double Minimum { get; private set; }
        internal double Maximum { get; private set; }
        internal double UpgradeBonus { get; private set; }
        internal double OtherFlatBonus { get; private set; }
        internal double BuffPercent { get; private set; }
        internal bool ResearchBonusActive { get; private set; }

        internal SourceModelBasicAttackPowerPreview(double minimum, double maximum,
            double upgrade, double otherFlat, double buff, bool research)
        {
            Minimum = minimum;
            Maximum = maximum;
            UpgradeBonus = upgrade;
            OtherFlatBonus = otherFlat;
            BuffPercent = buff;
            ResearchBonusActive = research;
        }
    }

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
        private readonly Dictionary<string, long> formRevisions =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> fractions = new Dictionary<string, double>();
        private Func<string, double> sourceModelFlatAttackPower;
        internal void ConfigurePracticeFlatAttackPower(Func<string, double> source)
        {
            if (sourceModelFlatAttackPower != null || source == null)
                throw new InvalidOperationException("연습 공격력 추가 공급자는 한 번만 연결 가능합니다.");
            sourceModelFlatAttackPower = source;
        }
        public bool Enabled { get; private set; }
        public SourceModelUnitEffects UnitEffects { get; private set; }
        public SourceModelPlayerUpgrades PlayerUpgrades { get; private set; }
        public Ordr.Mobile.Core.Original323PlayerResearchSession PlayerResearch { get; private set; }
        public void ConfigurePlayerResearch(
            Ordr.Mobile.Core.Original323PlayerResearchSession research)
        {
            if (Enabled || PlayerResearch != null)
                throw new InvalidOperationException("연구소 오라는 전투 시작 전에 한 번만 연결합니다.");
            PlayerResearch = research ?? throw new ArgumentNullException(nameof(research));
        }
        public void ConfigurePlayerUpgrades(SourceModelPlayerUpgrades upgrades)
        {
            if (Enabled || PlayerUpgrades != null)
                throw new InvalidOperationException("강화 모델은 독립 전투 시작 전에 한 번만 연결하세요.");
            PlayerUpgrades = upgrades ?? throw new ArgumentNullException(nameof(upgrades));
        }
        public void ConfigureUnitEffects(SourceModelUnitEffects effects)
        {
            if (Enabled || UnitEffects != null) throw new InvalidOperationException("연습 효과는 시작 전에 연결하세요.");
            UnitEffects = effects ?? throw new ArgumentNullException(nameof(effects));
        }

        // Original UnitData.slk points are GetUnitPointValue, not
        // hero Strength/Agility/Intelligence. Require the mapped rawcode
        // to prevent alternate forms from inheriting the wrong row.
        public int? OriginalUnitPointValue(string catalogId, string rawcode)
        {
            if (string.IsNullOrWhiteSpace(catalogId) ||
                string.IsNullOrWhiteSpace(rawcode)) return null;
            var entry = objects.Find(catalogId);
            return entry != null && entry.OriginalRawcode == rawcode ?
                entry.UnitPointValue : null;
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
            foreach (string raw in new[] { "H0B6", "h0AD", "H0AB", "H0AE", "H0AM", "h06N", "h085", "h0A9", "h0C2", "H0BY", "h04O", "h0AF", "h00H" })
            {
                var attack = objects.FindMapObjectAttack(raw);
                if (attack == null || !attack.BaseDamagePlus.HasValue ||
                    !attack.DiceCount.HasValue || !attack.DiceSides.HasValue ||
                    !attack.AttackCooldownSeconds.HasValue ||
                    !attack.AttackPointSeconds.HasValue ||
                    attack.AttackPointSeconds.Value > attack.AttackCooldownSeconds.Value)
                    throw new FormatException("원본 독립 공격 객체가 불완전합니다: " + raw);
                // h00H is also a registered permanent uncommon roster unit.
                // Reuse its existing native evidence instead of registering
                // duplicate rawcode rows just to model Moria's transient hit.
                OriginalMapCommonNativeAttackEvidence evidence;
                if (!byRawcode.TryGetValue(raw, out evidence))
                {
                    evidence = new OriginalMapCommonNativeAttackEvidence(raw, raw, attack);
                    byRawcode.Add(raw, evidence);
                }
                independentAttacks.Add(raw, new BasicAttackSpec(
                    evidence.BaseDamagePlus + (long)evidence.DiceCount,
                    evidence.CooldownSeconds, DamageSchool.Physical, DamageTraits.None));
            }
            // L-12's actual h05D shadow and tooltip model "장군좀비"
            // inheriting h02N's original dice may attack only when those
            // exact source-object fields exist. Missing evidence never
            // becomes a guessed owner-stat or generic copied attack.
            foreach (string raw in new[] { "h05D", "h02N" })
            {
                // Constructor parameter 'source' is the map catalog. Keep
                // each optional shadow/zombie attack object unambiguous.
                var objectAttack = objects.FindMapObjectAttack(raw);
                if (objectAttack == null || !objectAttack.BaseDamagePlus.HasValue ||
                    !objectAttack.DiceCount.HasValue || !objectAttack.DiceSides.HasValue ||
                    !objectAttack.AttackCooldownSeconds.HasValue ||
                    !objectAttack.AttackPointSeconds.HasValue ||
                    objectAttack.DiceCount.Value <= 0 || objectAttack.DiceSides.Value <= 0 ||
                    objectAttack.AttackCooldownSeconds.Value <= 0 ||
                    objectAttack.AttackPointSeconds.Value >
                        objectAttack.AttackCooldownSeconds.Value ||
                    (objectAttack.OriginalAttackType != "normal" &&
                     objectAttack.OriginalAttackType != "pierce" &&
                     objectAttack.OriginalAttackType != "siege" &&
                     objectAttack.OriginalAttackType != "hero"))
                    continue;
                OriginalMapCommonNativeAttackEvidence evidence;
                if (!byRawcode.TryGetValue(raw, out evidence))
                {
                    evidence = new OriginalMapCommonNativeAttackEvidence(
                        raw, raw, objectAttack);
                    byRawcode.Add(raw, evidence);
                }
                independentAttacks.Add(raw, new BasicAttackSpec(
                    evidence.BaseDamagePlus + (long)evidence.DiceCount,
                    evidence.CooldownSeconds, DamageSchool.Physical,
                    DamageTraits.None));
            }
        }
        internal BasicAttackSpec SourceObjectAttack(string rawcode)
        {
            BasicAttackSpec attack;
            return rawcode != null && independentAttacks.TryGetValue(rawcode, out attack)
                ? attack : null;
        }
        internal OriginalMapObjectResources SourceObjectResources(string rawcode) =>
            objects.FindMapObjectResources(rawcode);

        // Shared native self/ally bonuses may reach the basic resolver from
        // several producers. Keep a narrow, source-and-type allowlist so an
        // unknown status cannot turn a formerly verified practice attack into
        // unbounded or mislabeled damage.
        public static bool AllowsPracticeAttackerEffect(CombatEffect effect)
        {
            if (effect == null || string.IsNullOrEmpty(effect.SourceId)) return false;
            string source = effect.SourceId;
            return ((effect.Type == CombatEffectType.AttackPowerBuff ||
                     effect.Type == CombatEffectType.AttackSpeedBuff) &&
                    (source.StartsWith("practice-aura:", StringComparison.Ordinal) ||
                     source.StartsWith("practice-shadow:", StringComparison.Ordinal))) ||
                (effect.Type == CombatEffectType.AttackSpeedBuff &&
                 (SourceModelUnitEffects.IsPracticeAttackSpeedPercentEffect(effect) ||
                  // This is an interval reduction, not an IAS percentage.
                  source.StartsWith("practice-form:interval:", StringComparison.Ordinal))) ||
                (effect.Type == CombatEffectType.AttackPowerBuff &&
                 (source.StartsWith("practice-form:power:", StringComparison.Ordinal) ||
                  source.StartsWith("practice-tooltip:allypower:", StringComparison.Ordinal) ||
                  source.StartsWith("practice-native:SPH3:A0N1:", StringComparison.Ordinal) ||
                  source.StartsWith("practice-tooltip:armada:A0WQ:", StringComparison.Ordinal) ||
                  source.StartsWith("practice-tooltip:bulk:", StringComparison.Ordinal))) ||
                // Defensive/position-only statuses are carried in the full
                // attacker snapshot. They must not suppress normal damage.
                (effect.Type == CombatEffectType.ArmorBuff &&
                 (source.StartsWith("practice-native:SPH3:A0N1:", StringComparison.Ordinal) ||
                  source.StartsWith("practice-native:ET12:A11L:", StringComparison.Ordinal) ||
                  // The source-remaining Frost Armor buff has zero known armor.
                  (effect.Amount == 0 &&
                   source.StartsWith("practice-native:frostarmor:", StringComparison.Ordinal)))) ||
                (effect.Type == CombatEffectType.MovementSpeedBuff &&
                 source.StartsWith("practice-native:R00R:A11B:", StringComparison.Ordinal)) ||
                (effect.Type == CombatEffectType.AttackSpeedPenalty &&
                 source.StartsWith("practice-brulee:", StringComparison.Ordinal) &&
                 effect.Amount == 5);
        }
        internal long RollSourceObjectAttackPower(string rawcode)
        {
            OriginalMapCommonNativeAttackEvidence evidence;
            if (!Enabled || !independentAttacks.ContainsKey(rawcode) ||
                !byRawcode.TryGetValue(rawcode, out evidence))
                throw new NotSupportedException("원본 독립 객체 공격력 주사위 자료가 없습니다.");
            double basePower = evidence.RollObjectBaseDamage(random) +
                (PlayerUpgrades?.AttackBonus(rawcode) ?? 0);
            if (PlayerResearch?.HasStarted("Rhra") == true)
                basePower *= 1.12;
            if (basePower < 0 || basePower >= long.MaxValue)
                throw new OverflowException("연구소 공격력 보정 범위를 초과했습니다.");
            return (long)Math.Floor(basePower);
        }
        private void AdvanceSourceFormRevision(string instanceId)
        {
            long previous;
            formRevisions.TryGetValue(instanceId, out previous);
            formRevisions[instanceId] = checked(previous + 1);
        }
        internal long SourceFormRevisionOf(string instanceId)
        {
            long revision;
            return instanceId != null &&
                formRevisions.TryGetValue(instanceId, out revision) ? revision : 0;
        }
        internal void SetSourceForm(string instanceId, string rawcode)
        {
            if (!Enabled || SourceObjectAttack(rawcode) == null ||
                string.IsNullOrEmpty(instanceId))
                throw new InvalidOperationException("연습 변신 공격 출처가 없습니다.");
            string previous;
            if (formRawcodes.TryGetValue(instanceId, out previous) &&
                previous == rawcode) return;
            formRawcodes[instanceId] = rawcode;
            AdvanceSourceFormRevision(instanceId);
        }
        internal void ClearSourceForm(string instanceId)
        {
            if (instanceId != null && formRawcodes.Remove(instanceId))
                AdvanceSourceFormRevision(instanceId);
        }
        internal void ForgetSourceFormInstance(string instanceId)
        {
            if (instanceId == null) return;
            formRawcodes.Remove(instanceId);
            formRevisions.Remove(instanceId);
        }
        internal string SourceFormRawcodeOf(string instanceId)
        {
            string rawcode;
            return instanceId != null && formRawcodes.TryGetValue(instanceId, out rawcode)
                ? rawcode : null;
        }
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
            // Hanma is an ORIGINAL W3X CWR A0QG fusion result omitted from
            // the older 277-entry source-linked catalog. Attack-only practice
            // evidence is independent of the separately guarded CWR/mobile
            // fusion permit and never certifies its native skill callbacks.
            if (unit.Id == "MY-14" && unit.CatalogEntryKind == "fusion-result" &&
                unit.OriginalSourceRawcode == "h0C2")
            {
                var attack = objects.FindMapObjectAttack("h0C2");
                OriginalMapCommonNativeAttackEvidence evidence;
                if (attack == null || !attack.AttackPointSeconds.HasValue ||
                    !byRawcode.TryGetValue("h0C2", out evidence)) return false;
                binding = new OriginalVerifiedAttackBinding(new BasicAttackSpec(
                    evidence.BaseDamagePlus + (long)evidence.DiceCount,
                    evidence.CooldownSeconds, attack.OriginalRange.GetValueOrDefault(),
                    DamageSchool.Physical, DamageTraits.None),
                    attack.AttackPointSeconds.Value, evidence.Rawcode, Evidence);
                return true;
            }
            var raw = objects.Find(unit.Id);
            var source = attacks.Find(unit.Id);
            if (raw == null || source == null || raw.Attack1 == null ||
                !raw.Attack1.AttackPointSeconds.HasValue) return false;
            binding = new OriginalVerifiedAttackBinding(new BasicAttackSpec(
                source.BaseDamagePlus + (long)source.DiceCount, source.CooldownSeconds,
                raw.Attack1.OriginalRange.GetValueOrDefault(),
                DamageSchool.Physical, DamageTraits.None), raw.Attack1.AttackPointSeconds.Value,
                source.Rawcode, Evidence);
            return true;
        }

        private sealed class BasicAttackPowerAdjustment
        {
            internal double Upgrade;
            internal double FormFlat;
            internal double SourceFlat;
            internal double OtherFlat => FormFlat + SourceFlat;
            internal double BuffPercent;
            internal bool Research;
            internal double Apply(double objectDamage)
            {
                // Preserve the original sequence: dice + form + upgrade,
                // then flat hero/ability state, then multiplicative buffs.
                double result = objectDamage + FormFlat + Upgrade;
                result += SourceFlat;
                result *= 1 + BuffPercent / 100.0;
                if (Research) result *= 1.12;
                return result;
            }
        }

        // Shared by actual attacks and the read-only selected-unit HUD.
        // In particular, the HUD must never roll dice or read a mock target.
        private BasicAttackPowerAdjustment CurrentBasicAttackPowerAdjustment(
            string attackerId, string rawcode, IReadOnlyList<CombatEffect> effects)
        {
            foreach (var effect in effects)
                if (!AllowsPracticeAttackerEffect(effect))
                    throw new UnsupportedPracticeAttackerStatusException(effect?.SourceId);
            double flat = sourceModelFlatAttackPower == null
                ? 0 : sourceModelFlatAttackPower(attackerId);
            if (flat < 0 || double.IsNaN(flat) || double.IsInfinity(flat))
                throw new NotSupportedException("연습 공격력 증가량이 올바르지 않습니다.");
            return new BasicAttackPowerAdjustment {
                FormFlat = SourceModelTemporarySkillCatalog.FlatFormPower(effects),
                Upgrade = PlayerUpgrades?.AttackBonus(rawcode) ?? 0,
                SourceFlat = flat,
                BuffPercent = SourceModelUnitEffects.ResolvePracticeAttackPower(effects),
                Research = PlayerResearch?.HasStarted("Rhra") == true
            };
        }

        internal SourceModelBasicAttackPowerPreview PreviewBasicAttackPower(
            string attackerId, string registeredRawcode, IReadOnlyList<CombatEffect> effects)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(attackerId) ||
                string.IsNullOrWhiteSpace(registeredRawcode) || effects == null)
                return null;
            string rawcode = SourceFormRawcodeOf(attackerId) ?? registeredRawcode;
            OriginalMapCommonNativeAttackEvidence evidence;
            if (!byRawcode.TryGetValue(rawcode, out evidence)) return null;
            var adjustment = CurrentBasicAttackPowerAdjustment(attackerId, rawcode, effects);
            double minimum = evidence.BaseDamagePlus + (long)evidence.DiceCount;
            double maximum = evidence.BaseDamagePlus + (long)evidence.DiceCount * evidence.DiceSides;
            return new SourceModelBasicAttackPowerPreview(
                adjustment.Apply(minimum), adjustment.Apply(maximum),
                adjustment.Upgrade, adjustment.OtherFlat,
                adjustment.BuffPercent, adjustment.Research);
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
            // Identical source-model attacker adjustments are used in the
            // read-only HUD preview. Target armor/passive procs remain hit-only.
            var adjustment = CurrentBasicAttackPowerAdjustment(
                request.UnitInstanceId, rawcode, request.AttackerEffects);
            double rawDamage = adjustment.Apply(attack.RollObjectBaseDamage(random));
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


