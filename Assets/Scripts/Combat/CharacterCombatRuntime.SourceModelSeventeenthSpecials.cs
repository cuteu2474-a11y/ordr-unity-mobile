using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // This is an evidence port, not a fabricated STR/AGI stat. An original
        // game-state adapter may provide witnessed native attribute values for
        // a purchased Vegapunk satellite. Missing evidence contributes zero.
        private Func<long, string, string, double?> sourceSatelliteAttributeEvidence;
        private Func<long, long, string, string, double?>
            sourceSatelliteInstanceAttributeEvidence;
        private Func<long, string, string, bool> sourceBuggyCrewIdentityEvidence;
        private Func<long, string, bool> sourceKaidoNativeMorphEvidence;

        public void ConfigureSourceModelKaidoNativeMorphEvidence(
            Func<long, string, bool> evidence)
        {
            if (!UsesSourceModelBasicAttacks || processingStep ||
                pendingStepSeconds != 0 || sourceKaidoNativeMorphEvidence != null ||
                characters.Count != 0)
                throw new InvalidOperationException(
                    "카이도 네이티브 변신 확인자는 전투 전에 한 번만 연결합니다.");
            sourceKaidoNativeMorphEvidence = evidence ??
                throw new ArgumentNullException(nameof(evidence));
        }

        public bool TryApplyWitnessedSourceKaidoForm(long ownerId,
            string originalFormRawcode)
        {
            CharacterInstance actor;
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                game.Phase != SessionPhase.Running ||
                sourceKaidoNativeMorphEvidence == null ||
                !characters.TryGetValue(ownerId, out actor) ||
                actor.Catalog.Id != SourceModelNativeCharacterRules.Kaido ||
                actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != "h07M" ||
                (originalFormRawcode != "h07M" &&
                 originalFormRawcode != "h0AD") ||
                sourceModelRules.SourceObjectAttack(originalFormRawcode) == null ||
                !sourceKaidoNativeMorphEvidence(ownerId, originalFormRawcode))
                return false;

            if (originalFormRawcode == "h07M")
            {
                sourceModelRules.ClearSourceForm(InstanceKey(ownerId));
                sourceNativeForms.Remove(ownerId);
            }
            else
            {
                sourceModelRules.SetSourceForm(InstanceKey(ownerId), "h0AD");
                sourceNativeForms[ownerId] =
                    "카이도 용형 · h0AD (원본 변신 증거 반영)";
            }
            var notice = new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, InstanceKey(ownerId),
                InstanceKey(ownerId), "카이도 변신",
                "SOURCE_WITNESSED / " + originalFormRawcode +
                " / 기존 유닛·체력·카운터·조합 ID 유지", 0);
            if (processingStep) events.Add(notice);
            else pendingCommandEvents.Add(notice);
            return true;
        }

        public void ConfigureSourceModelBuggyCrewIdentityEvidence(
            Func<long, string, string, bool> evidence)
        {
            if (!UsesSourceModelBasicAttacks || processingStep ||
                pendingStepSeconds != 0 || sourceBuggyCrewIdentityEvidence != null ||
                characters.Count != 0)
                throw new InvalidOperationException(
                    "버기 해적단의 원본 객체 식별 근거는 전투 시작 전에 한 번만 연결합니다.");
            sourceBuggyCrewIdentityEvidence = evidence ??
                throw new ArgumentNullException(nameof(evidence));
        }

        // Invoked only after an original source adapter has witnessed a
        // selected crew member, mapped its real rawcode and supplied its
        // original spawn coordinate. Chance/order are NOT drawn a second time.
        public bool TrySpawnWitnessedSourceBuggyCrew(long ownerId,
            string candidateName, string originalRawcode, double x, double y)
        {
            CharacterInstance actor;
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                game.Phase != SessionPhase.Running ||
                sourceBuggyCrewIdentityEvidence == null ||
                !characters.TryGetValue(ownerId, out actor) ||
                !actor.AttackReady || actor.Catalog.Id != "E-05" ||
                actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != "h05A" ||
                string.IsNullOrWhiteSpace(originalRawcode) ||
                double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y) ||
                sourceModelRules.SourceObjectAttack(originalRawcode) == null ||
                !sourceBuggyCrewIdentityEvidence(ownerId, candidateName,
                    originalRawcode)) return false;

            var skill = FindSourceSkill(actor, "해적단 소환");
            if (skill == null || skill.Id != "E-05:skill:1" ||
                skill.Trigger != "onAttack" || skill.Target != "summon" ||
                skill.ChancePercent.HasValue || skill.Components.Count != 1 ||
                skill.Components[0].Type != "summon") return false;
            var entries = skill.Components[0].Get("candidates") as List<object>;
            if (entries == null || entries.Count != 4) return false;

            string[] names = { "파견 해적", "파견 해군", "이완코브", "거인족" };
            double[] chances = { 9, 5, 4.25, 2.55 };
            double[] lifetimes = { 3, 3, 4, 4 };
            int chosen = -1;
            for (int index = 0; index < entries.Count; index++)
            {
                var entry = entries[index] as Dictionary<string, object>;
                if (entry == null || !Equals(entry.ContainsKey("unit") ?
                        entry["unit"] : null, names[index]) ||
                    !SourceModelMatchesOriginalNumber(entry, "chancePercent",
                        chances[index]) ||
                    !SourceModelMatchesOriginalNumber(entry, "durationSeconds",
                        lifetimes[index])) return false;
                if (candidateName == names[index]) chosen = index;
            }
            if (chosen < 0) return false;

            SpawnSourceTemporaryActor(actor, skill.Name, candidateName,
                lifetimes[chosen], originalRawcode, false, x, y, true);
            var notice = new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, InstanceKey(ownerId),
                InstanceKey(ownerId), skill.Name,
                "SOURCE_WITNESSED / " + candidateName + " (" + originalRawcode +
                ") / 원본 지속시간 " + lifetimes[chosen] +
                "초 / 독립 평타 연결; 추가 고유기·면역 검증 전", 0);
            if (processingStep) events.Add(notice);
            else pendingCommandEvents.Add(notice);
            return true;
        }

        public void ConfigureSourceModelSatelliteAttributeEvidence(
            Func<long, string, string, double?> evidence)
        {
            if (!UsesSourceModelBasicAttacks || processingStep ||
                pendingStepSeconds != 0 || sourceSatelliteAttributeEvidence != null ||
                sourceSatellites.Count != 0)
                throw new InvalidOperationException(
                    "위성 능력치 증거는 전투·위성 소환 전에 한 번만 연결합니다.");
            sourceSatelliteAttributeEvidence = evidence ??
                throw new ArgumentNullException(nameof(evidence));
        }

        // Exact observed native attributes are keyed by the purchased
        // satellite instance, never solely by its creator and rawcode.
        public void ConfigureSourceModelSatelliteInstanceAttributeEvidence(
            Func<long, long, string, string, double?> evidence)
        {
            if (!UsesSourceModelBasicAttacks || processingStep ||
                pendingStepSeconds != 0 ||
                sourceSatelliteInstanceAttributeEvidence != null ||
                sourceSatellites.Count != 0)
                throw new InvalidOperationException(
                    "위성 개체별 원본 능력치 증거는 구매 전에 한 번 연결합니다.");
            sourceSatelliteInstanceAttributeEvidence = evidence ??
                throw new ArgumentNullException(nameof(evidence));
        }

        private bool TrySourceModelSatelliteAttribute(
            SourceIndependentAttacker attacker, string attribute,
            out double amount)
        {
            amount = 0;
            if ((sourceSatelliteAttributeEvidence == null &&
                 sourceSatelliteInstanceAttributeEvidence == null) ||
                (attribute != "strength" && attribute != "agility")) return false;
            foreach (var satellite in sourceSatellites)
            {
                if (SatelliteAttackKey(satellite.Id) != attacker.Key ||
                    satellite.OriginalRawcode != attacker.Rawcode) continue;
                double? value = sourceSatelliteInstanceAttributeEvidence == null ?
                    (double?)null : sourceSatelliteInstanceAttributeEvidence(
                        satellite.CreatorId, satellite.Id, attacker.Rawcode, attribute);
                if (!value.HasValue && sourceSatelliteAttributeEvidence != null)
                    value = sourceSatelliteAttributeEvidence(
                        satellite.CreatorId, attacker.Rawcode, attribute);
                if (!value.HasValue || value.Value < 0 ||
                    double.IsNaN(value.Value) || double.IsInfinity(value.Value))
                    return false;
                amount = value.Value;
                return true;
            }
            return false;
        }

        private static bool SourceModelSafeFixedAmount(double amount,
            out long whole)
        {
            whole = 0;
            if (double.IsNaN(amount) || double.IsInfinity(amount) ||
                amount < 0 || amount >= long.MaxValue) return false;
            whole = (long)Math.Floor(amount);
            return true;
        }

        private static bool SourceModelMatchesOriginalNumber(
            Dictionary<string, object> entry, string field, double expected)
        {
            object raw;
            if (!entry.TryGetValue(field, out raw) ||
                !(raw is double || raw is float || raw is decimal ||
                  raw is int || raw is long)) return false;
            double number = Convert.ToDouble(raw);
            return !double.IsNaN(number) && !double.IsInfinity(number) &&
                number == expected;
        }
    }
}
