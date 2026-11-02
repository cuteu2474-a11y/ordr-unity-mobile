using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private readonly Dictionary<long, double> sourceFormCastFinishes =
            new Dictionary<long, double>();
        private readonly Dictionary<long, string> sourceFormNames =
            new Dictionary<long, string>();
        private bool sourceFormCastWake;
        private readonly List<SourceModelSummonState> sourceTemporarySummons =
            new List<SourceModelSummonState>();
        private long sourceNextSummonId;

        private SourceModelSummonState SpawnSourceTemporaryActor(
            CharacterInstance owner, string skillName, string name,
            double lifetimeSeconds, string sourceRawcode,
            bool unresolvedLootAbility = false)
        {
            if (!UsesSourceModelBasicAttacks || owner == null ||
                !characters.ContainsKey(owner.Id) ||
                double.IsNaN(lifetimeSeconds) || double.IsInfinity(lifetimeSeconds) ||
                lifetimeSeconds <= 0 || lifetimeSeconds > 3600)
                throw new InvalidOperationException("연습 소환수 생성 조건이 올바르지 않습니다.");
            if (sourceNextSummonId == long.MaxValue)
                throw new OverflowException("연습 소환 ID 범위 초과");
            var summon = new SourceModelSummonState(++sourceNextSummonId,
                owner.Id, skillName, name, effectExecutor.Time + lifetimeSeconds,
                IsStoryDeployed(owner.Id), false, sourceRawcode,
                unresolvedLootAbility);
            sourceTemporarySummons.Add(summon);
            return summon;
        }

        // These two rawcode paths are deliberately separate from the verified
        // Warcraft observer and from the existing script damage catalogs.
        private static bool IsStandaloneSourceTemporaryAttack(string rawcode) =>
            rawcode == "H0B2" || rawcode == "h049";

        private bool SourceFormIsCasting(long id) =>
            UsesSourceModelBasicAttacks && sourceFormCastFinishes.ContainsKey(id);

        public string PracticeFormOf(long id)
        {
            if (!UsesSourceModelBasicAttacks || !characters.ContainsKey(id)) return null;
            string nativeForm;
            if (sourceNativeForms.TryGetValue(id, out nativeForm))
                return nativeForm;
            string name;
            if (!sourceFormNames.TryGetValue(id, out name)) return null;
            foreach (var effect in unitStatuses[InstanceKey(id)].Snapshot())
                if (effect.SourceId == "practice-form:power:" + id)
                    return name;
            sourceFormNames.Remove(id);
            return null;
        }

        public IReadOnlyList<SourceModelSummonState> PracticeSummonsOf(long ownerId)
        {
            var result = new List<SourceModelSummonState>();
            foreach (var summon in sourceTemporarySummons)
                if (summon.OwnerId == ownerId &&
                    summon.ExpiresAt > effectExecutor.Time &&
                    characters.ContainsKey(ownerId) &&
                    IsStoryDeployed(ownerId) == summon.StoryLane)
                    result.Add(summon);
            return result.AsReadOnly();
        }

        private void RemoveSourceTemporaryActors(long ownerId)
        {
            sourceFormCastFinishes.Remove(ownerId);
            sourceFormNames.Remove(ownerId);
            ForgetNativeSourceCharacter(ownerId);
            sourceTemporarySummons.RemoveAll(x => x.OwnerId == ownerId);
        }

        private void RefreshSourceTemporaryActors()
        {
            if (!UsesSourceModelBasicAttacks) return;
            sourceTemporarySummons.RemoveAll(x =>
                x.ExpiresAt <= effectExecutor.Time ||
                !characters.ContainsKey(x.OwnerId) ||
                IsStoryDeployed(x.OwnerId) != x.StoryLane);
        }

        private static ImportedCombatSkill FindSourceSkill(
            CharacterInstance actor, string name)
        {
            if (actor.Profile == null) return null;
            foreach (var skill in actor.Profile.Skills)
                if (skill.Name == name) return skill;
            return null;
        }

        private bool SourceRollSkill(ImportedCombatSkill skill)
        {
            if (!skill.ChancePercent.HasValue) return false;
            int threshold = (int)Math.Round(skill.ChancePercent.Value * 10000,
                MidpointRounding.AwayFromZero);
            return sourceModelRules.NextScriptRoll(1000000) < threshold;
        }

        private void ApplySourceForm(long id, ImportedCombatSkill skill,
            SourceModelFormSpec form)
        {
            if (!characters.ContainsKey(id)) return;
            string actorId = InstanceKey(id);
            var status = unitStatuses[actorId];
            string powerSource = "practice-form:power:" + id;
            string intervalSource = "practice-form:interval:" + id;
            status.RemoveSource(powerSource);
            status.RemoveSource(intervalSource);
            status.Apply(new CombatEffect(powerSource,
                CombatEffectType.AttackPowerBuff,
                CombatEffectOrigin.TriggeredSkill,
                form.FlatAttackPower, form.Duration));
            status.Apply(new CombatEffect(intervalSource,
                CombatEffectType.AttackSpeedBuff,
                CombatEffectOrigin.TriggeredSkill,
                form.AttackIntervalReduction, form.Duration));
            sourceFormNames[id] = form.Name;
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.StatusApplied, actorId, actorId,
                skill.Name, "MODEL_ONLY 변신 " + form.Name +
                " / 기존 유닛 유지 / " + form.Duration + "초 / 추가 기술 연동 대기", 0));
        }

        // The mobile source model's two fully specified flat-power forms.
        // Health/mana cannot be invented: an active form only casts when the
        // caller has configured actual resources on that unit.
        private bool TryCastSourceForm(CharacterInstance caster, string skillId,
            out AbilityCastResult result)
        {
            result = AbilityCastResult.UnsupportedEffect;
            if (!UsesSourceModelBasicAttacks || caster.Catalog.Id != "TR-18" ||
                caster.Profile == null) return false;
            ImportedCombatSkill chosen = null;
            foreach (var candidate in caster.Profile.Skills)
                if (candidate.Id == skillId) { chosen = candidate; break; }
            SourceModelFormSpec form;
            if (!SourceModelTemporarySkillCatalog.TryReadCompleteForm(chosen, out form) ||
                chosen.Trigger != "activeHealth") return false;
            ImportedCombatSkill cast;
            result = caster.Abilities.TryCast(skillId, true, true,
                component => component.Type == "transform", out cast);
            if (result != AbilityCastResult.Cast) return true;
            ApplySourceForm(caster.Id, cast, form);
            return true;
        }

        // Attack-declared callbacks: exact tooltip packet shape is required
        // before one draw can produce a form or an instant clone impact.
        private void ExecuteSourceTemporaryAttack(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            if (actor.Catalog.Id == "TR-14")
            {
                ImportedCombatSkill skill = FindSourceSkill(actor, "블랙 맘바 각성");
                SourceModelFormSpec form;
                if (!SourceModelTemporarySkillCatalog.TryReadCompleteForm(skill, out form) ||
                    sourceFormCastFinishes.ContainsKey(actor.Id) ||
                    !SourceRollSkill(skill)) return;
                double castTime = skill.Number("castTimeSeconds").Value;
                bool lane = IsStoryDeployed(actor.Id);
                long revision = SourceModelLaneRevision(actor.Id);
                string attacker = InstanceKey(actor.Id);
                sourceFormCastFinishes[actor.Id] = effectExecutor.Time + castTime;
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated, attacker,
                    primary.Id, skill.Name,
                    "MODEL_ONLY 변신 시전 시작 / " + castTime + "초", 0));
                effectExecutor.SchedulePracticeSourceAttack(attacker,
                    "practice-form-cast:" + attacker, castTime, () => {
                        sourceFormCastFinishes.Remove(actor.Id);
                        sourceFormCastWake = true;
                        if (CanFinishSourceModelDelayedAttack(actor.Id, lane, revision))
                            ApplySourceForm(actor.Id, skill, form);
                    });
                return;
            }

            if (actor.Catalog.Id == "I-03")
            {
                ImportedCombatSkill skill = FindSourceSkill(actor, "분신");
                double damage, radius;
                if (!SourceModelTemporarySkillCatalog.TryReadClonePulse(
                        skill, out damage, out radius) || !SourceRollSkill(skill)) return;
                string attacker = InstanceKey(actor.Id);
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated, attacker,
                    primary.Id, skill.Name,
                    "TOOLTIP_MODEL_ONLY 분신 공격의 단일 파동 / 실제 분신 연속 공격 미검증", 0));
                var source = new SourceScriptAttackSpec(actor.Catalog.Id,
                    actor.Mapping.AttackSpec.UnitRawCode, "tooltip-clone-proxy", 1, 1,
                    null, Array.Empty<SourceScriptHit>(), null);
                ApplySourceScriptHits(actor, primary, source,
                    new[] { new SourceScriptHit(SourceScriptHitShape.Circle,
                        radius, 0, damage, 1, 1, Ordr323AttackType.Spells) });
            }
        }

        // A short-lived clone is an independent session actor, not an owned
        // fusion material. Its source profile does not specify the number or
        // timing of attacks during those 2 seconds, so it cannot deal guessed
        // damage. The separately documented primary stun can be applied.
        private void ExecuteSourceTimedClone(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            if (actor.Catalog.Id != "L-21") return;
            ImportedCombatSkill skill = FindSourceSkill(actor, "인술 분신");
            string name;
            double duration, stun;
            if (!SourceModelTemporarySkillCatalog.TryReadTimedClone(skill,
                    out name, out duration, out stun) || !SourceRollSkill(skill)) return;
            var summon = SpawnSourceTemporaryActor(
                actor, skill.Name, name, duration, null);
            string attacker = InstanceKey(actor.Id);
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, attacker, primary.Id,
                skill.Name, "TOOLTIP_MODEL_ONLY " + name +
                " 생성 / " + duration + "초 / 독립 공격 간격 미검증", 0));
            if (primary.Kind == CharacterEnemyKind.Line && primary.Enemy.IsAlive)
            {
                string source = "practice-summon-stun:" + summon.Id;
                primary.Enemy.Status.Apply(new CombatEffect(source,
                    CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill,
                    1, stun));
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.StatusApplied, attacker, primary.Id,
                    skill.Name, "TOOLTIP_MODEL_ONLY 분신 기절 / 보스 저항 미검증", 0));
            }
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.EffectDeferred, attacker, primary.Id,
                skill.Name, "분신 기본 공격 횟수·타이밍 미검증: 추가 피해는 보류", 0));
        }
    }
}
