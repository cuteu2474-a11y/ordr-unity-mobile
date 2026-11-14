using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ordr.Mobile.Combat
{
    // Presentation-only, read-only projection. The existing status clock,
    // actor ownership and summon lifecycle are the sole truth. No effects
    // are started or prolonged by polling the mobile UI.
    public sealed partial class CharacterCombatRuntime
    {
        public double SourceModelRogerInfernoCooldownRemaining(long id)
        {
            double due;
            if (!characters.ContainsKey(id)) return 0;
            return sourceModelRogerInfernoReadyAt.TryGetValue(id, out due)
                ? Math.Max(0, due - Time) : 0;
        }

        private static string SkillUiNumber(double number) =>
            number.ToString("0.#", CultureInfo.InvariantCulture);

        public IReadOnlyList<string> SourceModelActiveUnitEffectsOf(long id)
        {
            var result = new List<string>();
            if (!characters.ContainsKey(id)) return result.AsReadOnly();
            CombatStatusEngine status;
            if (!unitStatuses.TryGetValue(InstanceKey(id), out status))
                return result.AsReadOnly();
            var snapshot = status.CaptureState();
            foreach (var entry in snapshot.Effects)
            {
                if (entry == null || string.IsNullOrEmpty(entry.SourceId))
                    continue;
                string label = null;
                // Only exact source-specific effects are translated; do not
                // advertise a generic armor/aura as a verified tooltip buff.
                if (entry.SourceId.StartsWith("practice-brulee:copy:",
                        StringComparison.Ordinal) &&
                    entry.Type == CombatEffectType.AttackSpeedPenalty)
                    label = "브륄레 복사 패널티 · 공속 -" +
                        SkillUiNumber(entry.Amount) + "%";
                else if (entry.SourceId.StartsWith(
                        "practice-tooltip:hibari-bento:", StringComparison.Ordinal) &&
                    entry.Type == CombatEffectType.AttackSpeedBuff)
                    label = "사랑의 도시락 · 공속 +" +
                        SkillUiNumber(entry.Amount) + "%";
                else if (entry.SourceId.StartsWith(
                        "practice-source-manual:", StringComparison.Ordinal))
                {
                    if (entry.Type == CombatEffectType.AttackSpeedBuff)
                        label = "아군 지원 · 공속 +" +
                            SkillUiNumber(entry.Amount) + "%";
                    else if (entry.Type == CombatEffectType.AttackPowerBuff)
                        label = "아군 지원 · 공격력 +" +
                            SkillUiNumber(entry.Amount) + "%";
                }
                if (label == null) continue;
                if (entry.NeverExpires)
                    label += " · 지속";
                else
                {
                    double remain = Math.Max(0, entry.ExpiresAt - snapshot.Time);
                    if (remain <= 0) continue;
                    label += " · " + SkillUiNumber(remain) + "초";
                }
                result.Add(label);
                if (result.Count == 8) break;
            }
            return result.AsReadOnly();
        }

        // Counts real tracked h04O attackers owned by this specific Uta.
        // Unknown original summons are NOT inferred from the character name.
        public int SourceModelActiveUtaSoldiersOf(long id)
        {
            CharacterInstance actor;
            if (!characters.TryGetValue(id, out actor) ||
                actor.Catalog.Id != "E-09") return 0;
            List<string> owned;
            if (!sourceModelUtaSongSoldierKeys.TryGetValue(id, out owned))
                return 0;
            int living = 0;
            foreach (string key in owned)
                if (sourceIndependentAttackers.ContainsKey(key))
                    living++;
            return living;
        }

        // A temporary scripted object may only be displayed as a generic
        // helper. It is not necessarily a combat summon with attack rules.
        public int SourceModelActiveTemporaryObjectCountOf(long id,
            out double maximumRemainingSeconds)
        {
            maximumRemainingSeconds = 0;
            if (!characters.ContainsKey(id)) return 0;
            int count = 0;
            foreach (var obj in sourceTemporarySummons)
            {
                if (obj.OwnerId != id || obj.IsVirtualAttackEffect ||
                    obj.ExpiresAt <= effectExecutor.Time ||
                    obj.StoryLane != IsStoryDeployed(id) ||
                    obj.LaneRevision != SourceModelLaneRevision(id))
                    continue;
                count++;
                maximumRemainingSeconds = Math.Max(maximumRemainingSeconds,
                    obj.ExpiresAt - effectExecutor.Time);
            }
            return count;
        }
    }
}
