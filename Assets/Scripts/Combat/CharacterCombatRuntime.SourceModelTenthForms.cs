using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // A source-profile form changes the SAME combatant's state, rather than
    // minting a second fusion unit. The native Chopper/Kaido object forms and
    // the existing complete Snake Man/Yamato packets stay independent.
    public sealed partial class CharacterCombatRuntime
    {
        private sealed class SourceModelTenthFormState
        {
            public string Form;
            public double ExpiresAt;
            public bool StoryLane;
            public long LaneRevision;
        }

        private readonly Dictionary<long, SourceModelTenthFormState> sourceModelTenthForms =
            new Dictionary<long, SourceModelTenthFormState>();
        private readonly Dictionary<long, int> sourceModelForestNutrients =
            new Dictionary<long, int>();
        // An unlocked Franky General is NOT an ordinary Franky. The owner of
        // the original enhancement economy must attest actual eligibility.
        private Func<long, bool> sourceModelFrankyEnhancedEligibility;

        public void ConfigureSourceModelFrankyEnhancedEligibility(Func<long, bool> eligible)
        {
            if (!UsesSourceModelBasicAttacks || processingStep ||
                pendingStepSeconds != 0 || sourceModelFrankyEnhancedEligibility != null)
                throw new InvalidOperationException("프랑키 장군 해금 검증자는 전투 시작 전에 한 번만 연결합니다.");
            sourceModelFrankyEnhancedEligibility = eligible ??
                throw new ArgumentNullException(nameof(eligible));
        }

        public int PracticeForestNutrientsOf(long id)
        {
            int count;
            return UsesSourceModelBasicAttacks && characters.ContainsKey(id) &&
                sourceModelForestNutrients.TryGetValue(id, out count) ? count : 0;
        }

        private string SourceModelTenthActiveForm(long id)
        {
            SourceModelTenthFormState state;
            if (!sourceModelTenthForms.TryGetValue(id, out state)) return null;
            if (state.ExpiresAt > effectExecutor.Time &&
                (state.Form != "nika" || SourceModelEleventhBonneyHasMana(id)) &&
                state.StoryLane == IsStoryDeployed(id) &&
                state.LaneRevision == SourceModelLaneRevision(id)) return state.Form;
            ForgetSourceModelTenthFormState(id);
            return null;
        }

        private void ForgetSourceModelTenthFormState(long id)
        {
            SourceModelTenthFormState previous;
            if (sourceModelTenthForms.TryGetValue(id, out previous) &&
                previous.Form == "nika" && sourceModelRules != null &&
                sourceModelRules.SourceFormRawcodeOf(InstanceKey(id)) == "H0BY")
            {
                sourceModelRules.ClearSourceForm(InstanceKey(id));
                CharacterInstance actor;
                SourceModelUnitResourceSpec source;
                if (characters.TryGetValue(id, out actor) &&
                    sourceModelManagedResourceRows.TryGetValue(id, out source) &&
                    actor.Abilities.Resources != null)
                    actor.Abilities.Resources.SetSourceModelFormManaPool(
                        source.CreateStartingPool().MaximumMana,
                        actor.Abilities.Resources.Mana);
            }
            sourceModelTenthForms.Remove(id);
            SourceModelEleventhForgetForm(id);
            CombatStatusEngine status;
            if (unitStatuses.TryGetValue(InstanceKey(id), out status))
            {
                status.RemoveSource("practice-form:interval:" + id);
                status.RemoveSource("practice-form:executioner:" + id);
            }
        }

        private void ForgetSourceModelTenthForm(long id)
        {
            ForgetSourceModelTenthFormState(id);
            sourceModelForestNutrients.Remove(id);
            ForgetSourceModelEleventhActor(id);
        }

        private void AdvanceSourceModelTenthForms(double seconds)
        {
            if (!UsesSourceModelBasicAttacks || sourceModelTenthForms.Count == 0) return;
            var finished = new List<long>();
            foreach (var pair in sourceModelTenthForms)
                if (!characters.ContainsKey(pair.Key) ||
                    pair.Value.ExpiresAt <= effectExecutor.Time ||
                    (pair.Value.Form == "nika" &&
                     !SourceModelEleventhBonneyHasMana(pair.Key)) ||
                    pair.Value.StoryLane != IsStoryDeployed(pair.Key) ||
                    pair.Value.LaneRevision != SourceModelLaneRevision(pair.Key))
                    finished.Add(pair.Key);
            foreach (var id in finished)
            {
                var form = sourceModelTenthForms[id].Form;
                ForgetSourceModelTenthFormState(id);
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                    InstanceKey(id), InstanceKey(id), "변신 종료",
                    "TOOLTIP_MODEL_ONLY / " + form + " 해제·원래 상태 복귀", 0));
            }
        }

        private static bool SourceModelTenthFormProfile(CharacterInstance actor,
            string id, string rawcode)
        {
            return actor != null && actor.Catalog.Id == id && actor.Profile != null &&
                actor.Profile.UnitId == id && actor.Mapping != null &&
                actor.Mapping.AttackSpec.UnitRawCode == rawcode;
        }

        private static bool SourceModelTenthExact(ImportedCombatEffect effect,
            string type, string form, double duration)
        {
            return effect != null && effect.Type == type &&
                effect.Text("form") == form && effect.Number("durationSeconds") == duration;
        }

        private static bool SourceModelTenthOnlyFields(
            IReadOnlyDictionary<string, object> fields, params string[] expected)
        {
            if (fields == null || fields.Count != expected.Length) return false;
            foreach (var field in expected)
                if (!fields.ContainsKey(field)) return false;
            return true;
        }

        private static bool SourceModelTenthCompleteForm(ImportedCombatSkill skill,
            string ownerId)
        {
            if (skill == null || skill.Id != ownerId + ":skill:0" ||
                skill.Trigger != "activeMana" || skill.Target != null) return false;
            if (ownerId == "TR-03")
                return skill.Name == "각성" && skill.Number("manaCost") == 150 &&
                    SourceModelTenthOnlyFields(skill.OriginalFields,
                        "name", "trigger", "components", "manaCost", "skillReplacement") &&
                    skill.Components.Count == 3 &&
                    SourceModelTenthOnlyFields(skill.Components[0].Fields,
                        "type", "form", "durationSeconds", "attackIntervalReductionSeconds") &&
                    SourceModelTenthOnlyFields(skill.Components[1].Fields,
                        "type", "amount", "durationSeconds", "radius") &&
                    SourceModelTenthOnlyFields(skill.Components[2].Fields,
                        "type", "mode") &&
                    SourceModelTenthExact(skill.Components[0], "transform", "awakening", 4.5) &&
                    skill.Components[0].Number("attackIntervalReductionSeconds") == .03 &&
                    skill.Components[1].Type == "armorReduction" &&
                    skill.Components[1].Number("amount") == 60 &&
                    skill.Components[1].Number("durationSeconds") == 7.5 &&
                    skill.Components[1].Number("radius") == 850 &&
                    skill.Components[2].Type == "attackTypeChange" &&
                    skill.Components[2].Text("mode") == "haki";
            if (ownerId == "TR-06")
                return skill.Name == "숲인간 변신" && skill.Number("manaCost") == 130 &&
                    SourceModelTenthOnlyFields(skill.OriginalFields,
                        "name", "trigger", "components", "manaCost") &&
                    skill.Components.Count == 2 &&
                    SourceModelTenthOnlyFields(skill.Components[0].Fields,
                        "type", "form", "durationSeconds", "basicAttackSplashRadius") &&
                    SourceModelTenthOnlyFields(skill.Components[1].Fields,
                        "type", "stacks") &&
                    SourceModelTenthExact(skill.Components[0], "transform", "forest", 7) &&
                    skill.Components[0].Number("basicAttackSplashRadius") == 400 &&
                    skill.Components[1].Type == "nutrientGain" &&
                    skill.Components[1].Number("stacks") == 1;
            return false;
        }

        private void SourceModelTenthStartForm(CharacterInstance actor, string form,
            double duration, ImportedCombatSkill skill)
        {
            sourceModelTenthForms[actor.Id] = new SourceModelTenthFormState {
                Form = form, ExpiresAt = effectExecutor.Time + duration,
                StoryLane = IsStoryDeployed(actor.Id),
                LaneRevision = SourceModelLaneRevision(actor.Id)
            };
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), InstanceKey(actor.Id), skill.Name,
                "TOOLTIP_MODEL_ONLY / " + form + " 상태 시작 / " + duration +
                "초 / 기존 유닛·소유권·조합 ID 유지", 0));
        }

        private void SourceModelTenthDoflamingoArmor(CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            var position = roster.Get(actor.Id);
            var effect = skill.Components[1];
            double radius = effect.Number("radius").Value;
            foreach (var enemy in enemies.Values)
            {
                if (!enemy.Enemy.IsAlive || !CanAttackInThisLane(actor, enemy)) continue;
                double dx = enemy.X - position.X, dy = enemy.Y - position.Y;
                if (dx * dx + dy * dy > radius * radius) continue;
                string source = "practice-tooltip:awakening-armor:" + actor.Id;
                enemy.Enemy.Status.RemoveSource(source);
                enemy.Enemy.Status.Apply(new CombatEffect(source,
                    CombatEffectType.ArmorReduction, CombatEffectOrigin.TriggeredSkill,
                    effect.Number("amount").Value,
                    effect.Number("durationSeconds").Value));
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                    InstanceKey(actor.Id), enemy.Id, skill.Name,
                    "TOOLTIP_MODEL_ONLY / 원본 방어력 감소 60 / 7.5초", 0));
            }
        }

        private static bool SourceModelTenthCompleteFrankyUpgrade(CharacterInstance actor)
        {
            if (!SourceModelTenthFormProfile(actor, "TR-28", "H08Y")) return false;
            object source;
            if (!actor.Profile.OriginalFields.TryGetValue("enhancedChanges", out source))
                return false;
            var changes = source as List<object>;
            if (changes == null || changes.Count != 1) return false;
            var change = changes[0] as Dictionary<string, object>;
            if (!SourceModelTenthOnlyFields(change,
                    "type", "name", "manaCost", "durationSeconds", "onTransformSkill") ||
                !Equals(change["type"], "unlockTransform") ||
                !Equals(change["name"], "프랑키 장군") ||
                !Equals(change["manaCost"], 150d) ||
                !Equals(change["durationSeconds"], 6d)) return false;
            var special = change["onTransformSkill"] as Dictionary<string, object>;
            return SourceModelTenthOnlyFields(special,
                    "name", "magicalDamage", "radius") &&
                Equals(special["name"], "제너럴 프레스") &&
                Equals(special["magicalDamage"], 4000000d) &&
                Equals(special["radius"], 800d);
        }

        private bool TryCastSourceModelTenthForm(CharacterInstance actor,
            string skillId, out AbilityCastResult result)
        {
            if (TryCastSourceModelEleventhForm(actor, skillId, out result))
                return true;
            result = AbilityCastResult.UnsupportedEffect;
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                actor.Profile == null || actor.Mapping == null) return false;

            bool franky = skillId == "TR-28:enhanced:frankyGeneral" &&
                SourceModelTenthFormProfile(actor, "TR-28", "H08Y");
            bool doffy = skillId == "TR-03:skill:0" &&
                SourceModelTenthFormProfile(actor, "TR-03", "H09E");
            bool greenbull = skillId == "TR-06:skill:0" &&
                SourceModelTenthFormProfile(actor, "TR-06", "H0BL");
            if (!franky && !doffy && !greenbull) return false;
            if (SourceModelTenthActiveForm(actor.Id) != null)
            {
                result = AbilityCastResult.Cooldown;
                return true;
            }
            var resource = actor.Abilities.Resources;
            if (resource == null)
            {
                result = AbilityCastResult.NoResourceState;
                return true;
            }
            if (franky)
            {
                if (!SourceModelTenthCompleteFrankyUpgrade(actor) ||
                    sourceModelFrankyEnhancedEligibility == null ||
                    !sourceModelFrankyEnhancedEligibility(actor.Id))
                {
                    result = AbilityCastResult.MissingOrUnsupportedRequirement;
                    return true;
                }
                if (!resource.CanSpend(0, 150))
                {
                    result = AbilityCastResult.InsufficientResources;
                    return true;
                }
                var targets = BuildTargetCandidates(actor);
                string primaryId = targets.Count == 0 ? null : ChoosePreviewTarget(actor, targets);
                SpatialCombatEnemy primary;
                if (primaryId == null || !enemies.TryGetValue(primaryId, out primary))
                {
                    result = AbilityCastResult.MissingTarget;
                    return true;
                }
                resource.Spend(0, 150);
                sourceModelTenthForms[actor.Id] = new SourceModelTenthFormState {
                    Form = "frankyGeneral", ExpiresAt = effectExecutor.Time + 6,
                    StoryLane = IsStoryDeployed(actor.Id),
                    LaneRevision = SourceModelLaneRevision(actor.Id)
                };
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                    InstanceKey(actor.Id), primary.Id, "프랑키 장군",
                    "TOOLTIP_MODEL_ONLY / 강화 해금 검증 후 프랑키 장군 6초 / MP150", 0));
                var hits = SourceScriptTargets(actor, primary, new SourceScriptHit(
                    SourceScriptHitShape.Circle, 800, 0, 0, 1, 1,
                    Ordr323AttackType.Spells));
                foreach (var target in hits)
                    ApplySourceModelSeventhTimedHit(actor, target,
                        "제너럴 프레스", 4000000, "magical");
                RemoveDefeatedEnemies();
                result = AbilityCastResult.Cast;
                return true;
            }

            var skill = FindSourceSkill(actor, doffy ? "각성" : "숲인간 변신");
            if (!SourceModelTenthCompleteForm(skill, actor.Catalog.Id))
            {
                result = AbilityCastResult.UnsupportedEffect;
                return true;
            }
            ImportedCombatSkill accepted;
            result = actor.Abilities.TryCast(skillId, true, true,
                component => component.Type == "transform" ||
                    doffy && (component.Type == "armorReduction" ||
                              component.Type == "attackTypeChange") ||
                    greenbull && component.Type == "nutrientGain", out accepted);
            if (result != AbilityCastResult.Cast) return true;
            string formName = doffy ? "awakening" : "forest";
            SourceModelTenthStartForm(actor, formName, doffy ? 4.5 : 7, accepted);
            if (doffy)
            {
                string source = "practice-form:interval:" + actor.Id;
                unitStatuses[InstanceKey(actor.Id)].RemoveSource(source);
                unitStatuses[InstanceKey(actor.Id)].Apply(new CombatEffect(source,
                    CombatEffectType.AttackSpeedBuff, CombatEffectOrigin.TriggeredSkill,
                    .03, 4.5));
                SourceModelTenthDoflamingoArmor(actor, accepted);
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                    InstanceKey(actor.Id), InstanceKey(actor.Id), accepted.Name,
                    "TOOLTIP_MODEL_ONLY / 패기 공격 타입·기술 교체의 원본 엔진 규칙 미검증", 0));
            }
            else
            {
                int previous;
                sourceModelForestNutrients.TryGetValue(actor.Id, out previous);
                sourceModelForestNutrients[actor.Id] = checked(previous + 1);
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                    InstanceKey(actor.Id), InstanceKey(actor.Id), accepted.Name,
                    "TOOLTIP_MODEL_ONLY / 양분 +1 반영; 스플래시 400의 피해 배율 미검증", 0));
            }
            return true;
        }

        private bool TryAutoCastSourceModelTenthForm(CharacterInstance actor)
        {
            if (TryAutoCastSourceModelEleventhForm(actor)) return true;
            string id = null;
            if (SourceModelTenthFormProfile(actor, "TR-03", "H09E"))
                id = "TR-03:skill:0";
            else if (SourceModelTenthFormProfile(actor, "TR-06", "H0BL"))
                id = "TR-06:skill:0";
            else if (SourceModelTenthFormProfile(actor, "TR-28", "H08Y") &&
                sourceModelFrankyEnhancedEligibility != null &&
                sourceModelFrankyEnhancedEligibility(actor.Id))
                id = "TR-28:enhanced:frankyGeneral";
            if (id == null || SourceModelTenthActiveForm(actor.Id) != null ||
                actor.Abilities.Resources == null || BuildTargetCandidates(actor).Count == 0)
                return false;
            // Use the public cast path so UI-command events are transferred
            // to the next battle step exactly once, as for other actives.
            return TryCastActiveSkill(actor.Id, id, null) == AbilityCastResult.Cast;
        }

        private void ExecuteSourceModelTenthFormDeclaredAttack(CharacterInstance actor,
            SpatialCombatEnemy primary, string rawcode)
        {
            ExecuteSourceModelEleventhFormDeclaredAttack(actor, primary, rawcode);
            string form = SourceModelTenthActiveForm(actor.Id);
            if (form == "awakening" &&
                SourceModelTenthFormProfile(actor, "TR-03", "H09E") &&
                rawcode == "H09E")
            {
                var skill = FindSourceSkill(actor, "각성 추가 공격");
                if (skill == null || skill.Id != "TR-03:skill:4" ||
                    skill.Trigger != "onAttack" || skill.ChancePercent != 20 ||
                    (skill.Get("requiresForm") as string) != "awakening" ||
                    skill.Radius != 525 || skill.Components.Count != 2 ||
                    skill.Components[0].Type != "physical" ||
                    skill.Components[0].Number("amount") != 400000 ||
                    skill.Components[1].Type != "magical" ||
                    skill.Components[1].Number("amount") != 150000 ||
                    skill.Components[1].Number("plusTargetCurrentHealthPercent") != 1 ||
                    !SourceRollSkill(skill)) return;
                var targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                    SourceScriptHitShape.Circle, 525, 0, 0, 1, 1,
                    Ordr323AttackType.Spells));
                ApplySourceModelNextThirtySkill(actor, primary, skill, targets);
            }
            else if (form == "frankyGeneral" &&
                SourceModelTenthFormProfile(actor, "TR-28", "H08Y") &&
                rawcode == "H08Y")
            {
                var sword = FindSourceSkill(actor, "제너럴 소드");
                if (sword == null || sword.Id != "TR-28:skill:4" ||
                    sword.Trigger != "onEveryAttack" || sword.Target != "area" ||
                    sword.Radius != 300 ||
                    (sword.Get("requiresForm") as string) != "frankyGeneral" ||
                    sword.Components.Count != 2 ||
                    sword.Components[0].Type != "magical" ||
                    sword.Components[0].Number("amount") != 200000 ||
                    sword.Components[1].Type != "magical" ||
                    sword.Components[1].Number("amount") != 600000 ||
                    sword.Components[1].Number("chancePercent") != 33 ||
                    sword.Components[1].Number("radius") != 450) return;
                foreach (var target in SourceScriptTargets(actor, primary,
                    new SourceScriptHit(SourceScriptHitShape.Circle, 300, 0, 0,
                        1, 1, Ordr323AttackType.Spells)))
                    ApplySourceModelSeventhTimedHit(actor, target, sword.Name,
                        200000, "magical");
                if (sourceModelRules.NextScriptRoll(1000000) < 330000)
                    foreach (var target in SourceScriptTargets(actor, primary,
                        new SourceScriptHit(SourceScriptHitShape.Circle, 450, 0, 0,
                            1, 1, Ordr323AttackType.Spells)))
                        ApplySourceModelSeventhTimedHit(actor, target, sword.Name,
                            600000, "magical");
                var beam = FindSourceSkill(actor, "제너럴 빔");
                if (beam == null || beam.Id != "TR-28:skill:3" ||
                    beam.Trigger != "onAttack" || beam.ChancePercent != 7.5 ||
                    beam.Radius != 800 ||
                    (beam.Get("requiresForm") as string) != "frankyGeneral" ||
                    beam.Components.Count != 1 ||
                    beam.Components[0].Type != "magical" ||
                    beam.Components[0].Number("amount") != 500000 ||
                    beam.Components[0].Number("plusTargetMaxHealthPercent") != 1 ||
                    !SourceRollSkill(beam)) return;
                var targets = SourceScriptTargets(actor, primary,
                    new SourceScriptHit(SourceScriptHitShape.Circle, 800, 0, 0,
                        1, 1, Ordr323AttackType.Spells));
                ApplySourceModelNextThirtySkill(actor, primary, beam, targets);
            }
        }
    }
}
