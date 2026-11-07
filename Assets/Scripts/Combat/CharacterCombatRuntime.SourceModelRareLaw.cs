using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private readonly Dictionary<long, double> sourceRareLawReadyAt =
            new Dictionary<long, double>();

        public int AutoCastSourceRareLawAcceleration(int maximumPerFrame = 16)
        {
            if (maximumPerFrame < 1 || maximumPerFrame > 128)
                throw new ArgumentOutOfRangeException(nameof(maximumPerFrame));
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                game.Phase != SessionPhase.Running || processingStep ||
                pendingStepSeconds != 0) return 0;

            int count = 0;
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                if (count >= maximumPerFrame) break;
                CharacterInstance caster;
                if (!characters.TryGetValue(id, out caster) ||
                    caster.Catalog.Id != "R-05" || caster.Mapping == null ||
                    caster.Mapping.AttackSpec.UnitRawCode != "h02L" ||
                    !caster.AttackReady || IsOriginalUnitCasting(id) ||
                    SourceFormIsCasting(id)) continue;

                double at;
                if (sourceRareLawReadyAt.TryGetValue(id, out at) && Time < at)
                    continue;

                ImportedCombatSkill skill = null;
                foreach (var item in caster.Profile == null
                    ? Array.Empty<ImportedCombatSkill>() : caster.Profile.Skills)
                    if (item.Name == "외과수술 가속") { skill = item; break; }

                // Fail closed on partial/changed tooltip evidence. The
                // original native target logic and buffs remain unverified.
                if (skill == null || skill.Trigger != "manual" ||
                    skill.Target != "selectedAlly" ||
                    skill.Number("cooldownSeconds") != 50 ||
                    skill.Components.Count != 1 ||
                    skill.Components[0].Type != "allyAttackSpeedBuff" ||
                    skill.Components[0].Number("percent") != 120 ||
                    skill.Components[0].Number("durationSeconds") != 8)
                    continue;

                bool story = IsStoryDeployed(id);
                var candidates = new List<LawDpsCandidate>();
                foreach (long candidateId in ids)
                {
                    if (candidateId == id || !characters.ContainsKey(candidateId))
                        continue;
                    var ally = characters[candidateId];
                    if (!ally.AttackReady || ally.Mapping == null ||
                        IsOriginalUnitCasting(candidateId) ||
                        !CanShareAllyCombatLane(id, candidateId))
                        continue;
                    if (unitStatuses[InstanceKey(candidateId)]
                        .HasActiveEffect(CombatEffectType.AttackSpeedBuff,
                            CombatEffectOrigin.TriggeredSkill))
                        continue; // mutually exclusive with other acceleration

                    var attack = ally.Mapping.AttackSpec.BasicAttack;
                    if (attack == null || attack.IntervalSeconds <= 0)
                        continue;
                    double score = attack.NominalAttackPower / attack.IntervalSeconds;
                    candidates.Add(new LawDpsCandidate(candidateId, story, true, score));
                }

                long? chosen = SpecialGradeMobilePolicy.ChooseLawTarget(story, candidates);
                if (!chosen.HasValue) continue;
                string targetId = InstanceKey(chosen.Value);
                string sourceId = "practice-rare-law:" + id;
                var targetStatus = unitStatuses[targetId];
                targetStatus.RemoveSource(sourceId);
                targetStatus.Apply(new CombatEffect(sourceId,
                    CombatEffectType.AttackSpeedBuff,
                    CombatEffectOrigin.TriggeredSkill, 120, 8));
                sourceRareLawReadyAt[id] = Time + 50;
                pendingCommandEvents.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated, InstanceKey(id),
                    targetId, "외과수술 가속",
                    "TOOLTIP_MODEL_ONLY +120% 공격속도 / 8초 / 50초 · 실제 원본 대상 선정 미검증", 0));
                count++;
            }
            return count;
        }
    }
}
