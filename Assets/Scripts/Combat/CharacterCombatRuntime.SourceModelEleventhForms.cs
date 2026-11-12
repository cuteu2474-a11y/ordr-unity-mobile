using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // 11th batch: tooltip-defined transformations remain states of the SAME
    // owner. Every entry requires the original mapped source object; E-02
    // cannot silently borrow E-01's rawcode. None is retail/WC3 verification.
    public sealed partial class CharacterCombatRuntime
    {
        private Func<long, string> sourceModelMarcoVerifiedForm;
        private const double SourceModelMarcoManualTransformCooldownSeconds = 10.75;
        private readonly Dictionary<long, string> sourceModelMarcoManualForms =
            new Dictionary<long, string>();
        private readonly Dictionary<long, double> sourceModelMarcoTransformReadyAt =
            new Dictionary<long, double>();
        private readonly Dictionary<string, int> sourceModelBonneyAge =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<long> sourceModelKingExecutionUsed =
            new HashSet<long>();

        private void SourceModelEleventhForgetForm(long id)
        {
            sourceModelKingExecutionUsed.Remove(id);
        }

        // Source 2.323(C) public F command: human <-> phoenix, 10.75s CD.
        // An external original-form witness still overrides this fallback
        // and may not be mixed with the mobile manual form clock.
        public void ConfigureSourceModelMarcoFormEvidence(Func<long, string> evidence)
        {
            if (!UsesSourceModelBasicAttacks || processingStep ||
                pendingStepSeconds != 0 || sourceModelMarcoVerifiedForm != null ||
                sourceModelMarcoManualForms.Count != 0)
                throw new InvalidOperationException(
                    "마르코 원본 형태 증거는 수동 F 전환 전에 한 번만 연결합니다.");
            sourceModelMarcoVerifiedForm = evidence ??
                throw new ArgumentNullException(nameof(evidence));
        }

        private bool SourceModelIsMarcoOwner(long id)
        {
            CharacterInstance actor;
            return UsesSourceModelBasicAttacks &&
                characters.TryGetValue(id, out actor) &&
                SourceModelTenthFormProfile(actor, "LI-03", "h08O");
        }

        private string SourceModelEleventhMarcoForm(long id)
        {
            if (!SourceModelIsMarcoOwner(id) ||
                IsOriginalPoneglyphChanneling(id) ||
                IsSharedObjectiveDeployed(id)) return null;
            // A supplied original form state is authoritative when present.
            if (sourceModelMarcoVerifiedForm != null)
            {
                string witnessed = sourceModelMarcoVerifiedForm(id);
                return witnessed == "phoenix" || witnessed == "human"
                    ? witnessed : null;
            }
            string manual;
            // h08O is the map's base (human) object. Its owner and
            // cooldown never change on form transition.
            return sourceModelMarcoManualForms.TryGetValue(id, out manual)
                ? manual : "human";
        }

        public double SourceModelMarcoTransformCooldownRemaining(long id)
        {
            if (!SourceModelIsMarcoOwner(id)) return 0;
            double readyAt;
            return sourceModelMarcoTransformReadyAt.TryGetValue(id, out readyAt)
                ? Math.Max(0, readyAt - Time) : 0;
        }

        public bool CanToggleSourceModelMarcoForm(long id)
        {
            if (!SourceModelIsMarcoOwner(id) ||
                sourceModelMarcoVerifiedForm != null ||
                Game.Phase != SessionPhase.Running ||
                processingStep || pendingStepSeconds != 0 ||
                activeOriginalFusionSwap != null ||
                IsOriginalPoneglyphChanneling(id) ||
                IsSharedObjectiveDeployed(id))
                return false;
            CharacterInstance actor;
            if (!characters.TryGetValue(id, out actor) || !actor.AttackReady)
                return false;
            double readyAt;
            return !sourceModelMarcoTransformReadyAt.TryGetValue(id, out readyAt) ||
                CombatScheduler.HasReached(Time, readyAt);
        }

        // F is explicitly MANUAL, unlike the normal automatic Q/W/R.
        // Never spawn another character, debit HP/MP or reset its attack
        // clock. An unknown phoenix attack-power/interval object is not
        // fabricated; the existing documented form skill callbacks switch.
        public bool TryToggleSourceModelMarcoForm(long id)
        {
            if (!CanToggleSourceModelMarcoForm(id)) return false;
            string previous = SourceModelEleventhMarcoForm(id);
            string next = previous == "phoenix" ? "human" : "phoenix";
            sourceModelMarcoManualForms[id] = next;
            sourceModelMarcoTransformReadyAt[id] =
                Time + SourceModelMarcoManualTransformCooldownSeconds;
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated,
                InstanceKey(id), InstanceKey(id), "마르코 · 새새열매 F 변신",
                "MODEL_ONLY 원본 F 수동 명령 / " + previous + " → " + next +
                " / 10.75초 쿨타임 / 원본 형태별 공격력·속도 감소폭 미검증", 0));
            return true;
        }

        // Human-form IAS +125% is explicitly source-documented; other
        // transformation stats lack exact values and remain unmodified.
        private double SourceModelMarcoHumanAttackSpeedBonus(long id) =>
            SourceModelEleventhMarcoForm(id) == "human" ? 125 : 0;

        private void ForgetSourceModelMarcoOwner(long id)
        {
            sourceModelMarcoManualForms.Remove(id);
            sourceModelMarcoTransformReadyAt.Remove(id);
        }

        private void ForgetSourceModelEleventhActor(long id)
        {
            SourceModelEleventhForgetForm(id);
            SourceModelTwelfthForgetOwner(id);
            SourceModelTwentyNinthForgetOwner(id);
            SourceModelThirtiethForgetOwner(id);
            SourceModelZephyrForgetOwner(id);
            ForgetSourceModelBurgessElbow(id);
            string prefix = InstanceKey(id) + ":";
            var removed = new List<string>();
            foreach (var item in sourceModelBonneyAge.Keys)
                if (item.StartsWith(prefix, StringComparison.Ordinal)) removed.Add(item);
            foreach (string key in removed) sourceModelBonneyAge.Remove(key);
            SourceModelTwelfthPruneDefeated();
            SourceModelTwentyNinthPruneDefeated();
            SourceModelThirtiethPruneDefeated();
        }

        private void AdvanceSourceModelEleventhBonney(double seconds)
        {
            var removed = new List<string>();
            foreach (string key in sourceModelBonneyAge.Keys)
            {
                int split = key.IndexOf(':');
                long owner;
                if (split < 0 || !long.TryParse(key.Substring(0, split), out owner) ||
                    !characters.ContainsKey(owner) ||
                    !enemies.ContainsKey(key.Substring(split + 1))) removed.Add(key);
            }
            foreach (string key in removed) sourceModelBonneyAge.Remove(key);
            SourceModelTwelfthPruneDefeated();
            SourceModelTwentyNinthPruneDefeated();
            SourceModelThirtiethPruneDefeated();
        }

        private static bool SourceModelEleventhFormHasAttack(string raw) =>
            raw == "H0BK" || raw == "H0BX" || raw == "H0BY" ||
            raw == "h0AI" || raw == "h08O";

        private bool SourceModelEleventhBonneyHasMana(long id)
        {
            CharacterInstance actor;
            return characters.TryGetValue(id, out actor) &&
                actor.Abilities.Resources != null && actor.Abilities.Resources.Mana > 0;
        }

        private static bool SourceModelEleventhSkill(ImportedCombatSkill skill,
            string id, string name, string trigger, int parts)
        {
            return skill != null && skill.Id == id && skill.Name == name &&
                skill.Trigger == trigger && skill.Components.Count == parts;
        }

        private static bool SourceModelEleventhGiant(ImportedCombatSkill skill)
        {
            return SourceModelEleventhSkill(skill, "E-01:skill:0", "거대화",
                    "activeHealth", 3) && skill.Target == "self" &&
                SourceModelTenthOnlyFields(skill.OriginalFields,
                    "name", "trigger", "healthCost", "target", "components") &&
                SourceModelTenthOnlyFields(skill.Components[0].Fields,
                    "type", "durationSeconds") &&
                SourceModelTenthOnlyFields(skill.Components[1].Fields,
                    "type", "percent") &&
                SourceModelTenthOnlyFields(skill.Components[2].Fields,
                    "type", "amount", "plusBasicAttackDamagePercent", "radius", "trigger") &&
                skill.Number("healthCost") == 115 &&
                skill.Components[0].Type == "transform" &&
                skill.Components[0].Number("durationSeconds") == 4.25 &&
                skill.Components[1].Type == "basicAttackSpeedReduction" &&
                skill.Components[1].Number("percent") == 225 &&
                skill.Components[2].Type == "physical" &&
                skill.Components[2].Number("amount") == 200000 &&
                skill.Components[2].Number("plusBasicAttackDamagePercent") == 200 &&
                skill.Components[2].Number("radius") == 450 &&
                skill.Components[2].Text("trigger") == "everyAttackWhileTransformed";
        }

        private static bool SourceModelEleventhBonney(ImportedCombatSkill skill)
        {
            if (!SourceModelEleventhSkill(skill, "TR-21:skill:0", "니카같은 미래",
                    "activeMana", 2)) return false;
            var costs = skill.Components[1].Get("skillCosts") as Dictionary<string, object>;
            return SourceModelEleventhSkill(skill, "TR-21:skill:0", "니카같은 미래",
                    "activeMana", 2) && skill.Target == null &&
                SourceModelTenthOnlyFields(skill.OriginalFields,
                    "name", "trigger", "components", "manaCost") &&
                SourceModelTenthOnlyFields(skill.Components[0].Fields,
                    "type", "form", "untilManaExhausted", "attackSpeedIncrease", "procChanceIncrease") &&
                SourceModelTenthOnlyFields(skill.Components[1].Fields,
                    "type", "skillCosts") &&
                SourceModelTenthOnlyFields(costs, "오일쇼크", "임사체험") &&
                Equals(costs["오일쇼크"], 2d) && Equals(costs["임사체험"], 4d) &&
                skill.Number("manaCost") == 150 &&
                skill.Components[0].Type == "transform" &&
                skill.Components[0].Text("form") == "nika" &&
                Equals(skill.Components[0].Get("untilManaExhausted"), true) &&
                skill.Components[1].Type == "manaCostPerSkill";
        }

        private static bool SourceModelEleventhKing(ImportedCombatSkill skill)
        {
            return SourceModelEleventhSkill(skill, "LI-10:skill:0", "처형자",
                    "verifiedCondition", 2) &&
                SourceModelTenthOnlyFields(skill.OriginalFields,
                    "name", "trigger", "components", "requiredTargetArmorBreak",
                    "manaAtLeast", "manaCost") &&
                SourceModelTenthOnlyFields(skill.Components[0].Fields,
                    "type", "form", "durationSeconds") &&
                SourceModelTenthOnlyFields(skill.Components[1].Fields,
                    "type", "percent") &&
                skill.Number("requiredTargetArmorBreak") == 50 &&
                skill.Number("manaAtLeast") == 35 &&
                skill.Number("manaCost") == 33 &&
                skill.Components[0].Type == "transform" &&
                skill.Components[0].Text("form") == "executioner" &&
                skill.Components[0].Number("durationSeconds") == 1.25 &&
                skill.Components[1].Type == "selfAttackSpeedBuff" &&
                skill.Components[1].Number("percent") == 25;
        }

        private bool TryCastSourceModelEleventhForm(CharacterInstance actor,
            string skillId, out AbilityCastResult result)
        {
            result = AbilityCastResult.UnsupportedEffect;
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                actor == null || actor.Profile == null || actor.Mapping == null) return false;
            bool giant = skillId == "E-01:skill:0" &&
                SourceModelTenthFormProfile(actor, "E-01", "H0BK");
            bool bonney = skillId == "TR-21:skill:0" &&
                SourceModelTenthFormProfile(actor, "TR-21", "H0BX");
            bool king = skillId == "LI-10:skill:0" &&
                SourceModelTenthFormProfile(actor, "LI-10", "h0AI");
            if (!giant && !bonney && !king) return false;
            if (SourceModelTenthActiveForm(actor.Id) != null)
            {
                result = AbilityCastResult.Cooldown;
                return true;
            }
            var resource = actor.Abilities.Resources;
            if (resource == null || !sourceModelManagedResourceRows.ContainsKey(actor.Id))
            {
                result = AbilityCastResult.NoResourceState;
                return true;
            }
            var skill = FindSourceSkill(actor, giant ? "거대화" :
                bonney ? "니카같은 미래" : "처형자");
            if (!(giant && SourceModelEleventhGiant(skill) ||
                  bonney && SourceModelEleventhBonney(skill) ||
                  king && SourceModelEleventhKing(skill)))
            {
                result = AbilityCastResult.UnsupportedEffect;
                return true;
            }
            if (king)
            {
                // The source condition is FIFTY accumulated armor breaks,
                // not a guessed armor debuff duration or defense percentage.
                bool eligible = false;
                foreach (var candidate in BuildTargetCandidates(actor))
                {
                    SpatialCombatEnemy victim;
                    if (enemies.TryGetValue(candidate.Id, out victim) &&
                        victim.Enemy.IsAlive && ArmorBreakStacksOf(victim.Id) >= 50)
                        eligible = true;
                }
                if (!eligible)
                {
                    result = AbilityCastResult.MissingOrUnsupportedRequirement;
                    return true;
                }
                if (resource.Mana < 35 || !resource.CanSpend(0, 33))
                {
                    result = AbilityCastResult.InsufficientResources;
                    return true;
                }
                resource.Spend(0, 33);
                SourceModelTenthStartForm(actor, "executioner", 1.25, skill);
                string source = "practice-form:executioner:" + actor.Id;
                var status = unitStatuses[InstanceKey(actor.Id)];
                status.RemoveSource(source);
                status.Apply(new CombatEffect(source,
                    CombatEffectType.AttackSpeedBuff,
                    CombatEffectOrigin.TriggeredSkill, 25, 1.25));
                result = AbilityCastResult.Cast;
                return true;
            }
            if (bonney && sourceModelRules.SourceObjectAttack("H0BY") == null)
            {
                result = AbilityCastResult.MissingOrUnsupportedRequirement;
                return true;
            }
            ImportedCombatSkill accepted;
            result = actor.Abilities.TryCast(skillId, true, true,
                component => giant ?
                    component.Type == "transform" ||
                    component.Type == "basicAttackSpeedReduction" ||
                    component.Type == "physical" :
                    component.Type == "transform" ||
                    component.Type == "manaCostPerSkill", out accepted);
            if (result != AbilityCastResult.Cast) return true;
            if (giant)
            {
                SourceModelTenthStartForm(actor, "giant", 4.25, accepted);
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.EffectDeferred,
                    InstanceKey(actor.Id), InstanceKey(actor.Id), accepted.Name,
                    "TOOLTIP_MODEL_ONLY / 공격속도 감소 225%의 원본 간격식 미확인", 0));
            }
            else
            {
                // H0BY is the original separate Bonney Nika attack OBJECT;
                // it is never a second fusion unit. Original pool is MP100.
                sourceModelRules.SetSourceForm(InstanceKey(actor.Id), "H0BY");
                resource.SetSourceModelFormManaPool(100, 100);
                sourceModelTenthForms[actor.Id] = new SourceModelTenthFormState {
                    Form = "nika", ExpiresAt = double.PositiveInfinity,
                    StoryLane = IsStoryDeployed(actor.Id),
                    LaneRevision = SourceModelLaneRevision(actor.Id)
                };
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                    InstanceKey(actor.Id), InstanceKey(actor.Id), accepted.Name,
                    "TOOLTIP_MODEL_ONLY / H0BX→H0BY 같은 유닛 / MP100, 소진 시 원상복귀", 0));
                var candidates = BuildTargetCandidates(actor);
                if (candidates.Count > 0)
                {
                    SpatialCombatEnemy primary;
                    string key = ChoosePreviewTarget(actor, candidates);
                    if (key != null && enemies.TryGetValue(key, out primary))
                    {
                        var opening = FindSourceSkill(actor, "해방의 니카 펀치");
                        if (SourceModelEleventhSkill(opening, "TR-21:skill:3",
                                "해방의 니카 펀치", "onTransform", 3) &&
                            opening.Radius == 600 &&
                            opening.Components[0].Number("amount") == 3000000)
                        {
                            foreach (var target in SourceModelEleventhCircle(actor, primary, 600))
                            {
                                ApplySourceModelSeventhTimedHit(actor, target,
                                    opening.Name, 3000000, "magical");
                                SourceModelEleventhStun(actor, target, opening.Name, 3);
                            }
                            RemoveDefeatedEnemies();
                        }
                    }
                }
            }
            return true;
        }

        private bool TryAutoCastSourceModelEleventhForm(CharacterInstance actor)
        {
            string skillId = SourceModelTenthFormProfile(actor, "E-01", "H0BK") ?
                "E-01:skill:0" :
                SourceModelTenthFormProfile(actor, "TR-21", "H0BX") ?
                "TR-21:skill:0" :
                SourceModelTenthFormProfile(actor, "LI-10", "h0AI") ?
                "LI-10:skill:0" : null;
            return skillId != null && SourceModelTenthActiveForm(actor.Id) == null &&
                actor.Abilities.Resources != null &&
                BuildTargetCandidates(actor).Count > 0 &&
                TryCastActiveSkill(actor.Id, skillId, null) == AbilityCastResult.Cast;
        }

        private IReadOnlyList<SpatialCombatEnemy> SourceModelEleventhCircle(
            CharacterInstance actor, SpatialCombatEnemy primary, double radius)
        {
            return SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.Circle, radius, 0, 0, 1, 1,
                Ordr323AttackType.Spells));
        }

        private void SourceModelEleventhStun(CharacterInstance actor,
            SpatialCombatEnemy enemy, string skillName, double duration)
        {
            if (enemy.Kind != CharacterEnemyKind.Line || !enemy.Enemy.IsAlive) return;
            ApplySourceModelLineStun(InstanceKey(actor.Id), enemy, skillName, duration);
        }

        private void ExecuteSourceModelEleventhFormDeclaredAttack(
            CharacterInstance actor, SpatialCombatEnemy primary, string rawcode)
        {
            if (SourceModelTenthFormProfile(actor, "E-01", "H0BK") && rawcode == "H0BK" &&
                SourceModelTenthActiveForm(actor.Id) == "giant")
            {
                var giant = FindSourceSkill(actor, "거대화");
                if (!SourceModelEleventhGiant(giant)) return;
                double baseDamage = SourceModelUpgradeAdjustedAttackPower(actor);
                foreach (var victim in SourceModelEleventhCircle(actor, primary, 450))
                    ApplySourceModelSeventhTimedHit(actor, victim, "거대화 평타",
                        200000 + baseDamage * 2, "physical");
                var blow = FindSourceSkill(actor, "거대화 타격");
                if (SourceModelEleventhSkill(blow, "E-01:skill:5", "거대화 타격",
                        "onAttack", 2) && blow.ChancePercent == 20 &&
                    blow.Radius == 500 &&
                    (blow.Get("requiresForm") as string) == "giant" &&
                    blow.Components[0].Number("amount") == 1000000 &&
                    blow.Components[0].Number("plusBasicAttackDamagePercent") == 500 &&
                    blow.Components[1].Number("durationSeconds") == 2 &&
                    SourceRollSkill(blow))
                {
                    double multiplier = 1 + sourceModelRules.NextScriptRoll(500001) / 1000000.0;
                    foreach (var victim in SourceModelEleventhCircle(actor, primary, 500))
                    {
                        ApplySourceModelSeventhTimedHit(actor, victim, blow.Name,
                            1000000 * multiplier + baseDamage * 5, "physical");
                        SourceModelEleventhStun(actor, victim, blow.Name, 2);
                    }
                }
                RemoveDefeatedEnemies();
                return;
            }
            if (SourceModelTenthFormProfile(actor, "LI-10", "h0AI") && rawcode == "h0AI")
            {
                SourceModelTwelfthKingSlow(actor, primary);
                if (SourceModelTenthActiveForm(actor.Id) == "executioner")
                {
                    var execution = FindSourceSkill(actor, "참수");
                    // The state is unlocked by ANY actual eligible target;
                    // a different first preview target must not waste it.
                    SpatialCombatEnemy victim = primary;
                    if (ArmorBreakStacksOf(victim.Id) < 50)
                        foreach (var candidate in BuildTargetCandidates(actor))
                        {
                            SpatialCombatEnemy eligible;
                            if (enemies.TryGetValue(candidate.Id, out eligible) &&
                                eligible.Enemy.IsAlive && ArmorBreakStacksOf(candidate.Id) >= 50)
                            { victim = eligible; break; }
                        }
                    if (!sourceModelKingExecutionUsed.Contains(actor.Id) &&
                        ArmorBreakStacksOf(victim.Id) >= 50 &&
                        SourceModelEleventhSkill(execution, "LI-10:skill:1", "참수",
                            "verifiedCondition", 2) &&
                        execution.Number("requiredTargetArmorBreak") == 50 &&
                        execution.Components[0].Number("amount") == 10000000 &&
                        execution.Components[0].Number("bossOverrideAmount") == 100000 &&
                        execution.Components[1].Number("stacks") == 1)
                    {
                        sourceModelKingExecutionUsed.Add(actor.Id);
                        long amount = victim.Kind == CharacterEnemyKind.Boss ? 100000 : 10000000;
                        ApplySourceModelFixedPrimaryPacket(InstanceKey(actor.Id), victim,
                            amount, execution.Name);
                        if (victim.Enemy.IsAlive)
                            ApplySourceModelCompositeArmorBreak(actor, victim, execution,
                                execution.Components[1], new[] { victim });
                        RemoveDefeatedEnemies();
                    }
                    return;
                }
                // The original "Cruel Swordsmanship" +4 break producer is
                // required to make the 50-stack executioner gate reachable.
                var sword = FindSourceSkill(actor, "잔혹한 검술");
                if (SourceModelEleventhSkill(sword, "LI-10:skill:2", "잔혹한 검술",
                        "onAttack", 2) && sword.ChancePercent == 10 &&
                    sword.Radius == 450 &&
                    sword.Components[0].Type == "physical" &&
                    sword.Components[0].Number("amount") == 800000 &&
                    sword.Components[1].Type == "armorBreak" &&
                    sword.Components[1].Number("stacks") == 4 &&
                    sword.Components[1].Text("scope") == "singlePrimaryTarget" &&
                    SourceRollSkill(sword))
                {
                    foreach (var victim in SourceModelEleventhCircle(actor, primary, 450))
                        ApplySourceModelSeventhTimedHit(actor, victim, sword.Name,
                            800000, "physical");
                    if (primary.Enemy.IsAlive)
                        ApplySourceModelCompositeArmorBreak(actor, primary, sword,
                            sword.Components[1], new[] { primary });
                    RemoveDefeatedEnemies();
                }
                SourceModelTwelfthKingFire(actor, primary);
                return;
            }
            if (SourceModelTenthFormProfile(actor, "TR-21", "H0BX") &&
                (rawcode == "H0BX" || rawcode == "H0BY"))
                SourceModelEleventhBonneyAttack(actor, primary);
            if (SourceModelTenthFormProfile(actor, "LI-03", "h08O") && rawcode == "h08O")
            {
                SourceModelMarcoHumanAttackEffect(actor, primary);
                SourceModelEleventhMarcoAttack(actor, primary);
                SourceModelMarcoPhoenixAttackHeal(actor);
            }
        }

        private void SourceModelEleventhBonneyAttack(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            var resources = actor.Abilities.Resources;
            if (resources == null) return;
            bool nika = SourceModelTenthActiveForm(actor.Id) == "nika";
            foreach (string name in new[] { "오일쇼크", "임사체험" })
            {
                var skill = FindSourceSkill(actor, name);
                bool oil = name == "오일쇼크";
                if (!SourceModelEleventhSkill(skill,
                        oil ? "TR-21:skill:1" : "TR-21:skill:2",
                        name, "onAttack", oil ? 3 : 2) ||
                    skill.ChancePercent != (oil ? 15 : 30) ||
                    skill.Number("selfHealthCost") != (oil ? 1 : 4) ||
                    skill.Components[0].Number("amount") != (oil ? 400000 : 750000) ||
                    skill.Components[oil ? 2 : 1].Type != "ageStacks" ||
                    skill.Components[oil ? 2 : 1].Number("add") != (oil ? 2 : 20))
                    continue;
                double cost = oil ? 1 : 4;
                double mp = nika ? (oil ? 2 : 4) : 0;
                if (!resources.CanSpend(cost, mp) || !SourceRollSkill(skill)) continue;
                resources.Spend(cost, mp);
                var targets = oil ? SourceModelEleventhCircle(actor, primary, 320) :
                    (IReadOnlyList<SpatialCombatEnemy>)new[] { primary };
                foreach (var victim in targets)
                {
                    ApplySourceModelSeventhTimedHit(actor, victim, skill.Name,
                        oil ? 400000 : 750000, oil ? "magical" : "physical");
                    string key = InstanceKey(actor.Id) + ":" + victim.Id;
                    int existing;
                    sourceModelBonneyAge.TryGetValue(key, out existing);
                    sourceModelBonneyAge[key] = checked(existing + (oil ? 2 : 20));
                    SourceModelTwelfthBonneyAgeAdded(actor, victim, existing,
                        sourceModelBonneyAge[key]);
                }
                if (nika && resources.Mana == 0)
                    ForgetSourceModelTenthFormState(actor.Id);
            }
            SourceModelTwelfthBonneyStun(actor, primary);
            RemoveDefeatedEnemies();
        }

        private static bool SourceModelMarcoLineOnly(ImportedCombatEffect effect)
        {
            var kinds = effect.Get("onlyTargetKinds") as IReadOnlyList<object>;
            return kinds != null && kinds.Count == 1 &&
                string.Equals(kinds[0] as string, "line", StringComparison.Ordinal);
        }

        // All target selectors share the actual combat lane and a stable
        // order. This avoids a duplicated hit or a UI-only enemy becoming
        // one of the eight additional phoenix recipients.
        private List<SpatialCombatEnemy> SourceModelMarcoAreaTargets(
            CharacterInstance actor, SpatialCombatEnemy center,
            double radius, bool excludePrimary)
        {
            var result = new List<SpatialCombatEnemy>();
            double radiusSquared = radius * radius;
            foreach (var target in enemies.Values)
            {
                if (!target.Enemy.IsAlive ||
                    !CanAttackInThisLane(actor, target) ||
                    (excludePrimary && target.Id == center.Id)) continue;
                double dx = target.X - center.X, dy = target.Y - center.Y;
                if (dx * dx + dy * dy <= radiusSquared)
                    result.Add(target);
            }
            result.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            return result;
        }

        private static ImportedCombatEffect SourceModelMarcoPassive(
            CharacterInstance actor, string type)
        {
            if (actor.Profile == null) return null;
            foreach (var passive in actor.Profile.Passives)
                if (passive.Type == type) return passive;
            return null;
        }

        private void SourceModelMarcoHumanAttackEffect(
            CharacterInstance actor, SpatialCombatEnemy target)
        {
            if (SourceModelEleventhMarcoForm(actor.Id) != "human" ||
                !target.Enemy.IsAlive) return;
            var passive = SourceModelMarcoPassive(
                actor, "marcoHumanBonusExplosion");
            if (passive == null ||
                passive.Text("requiresForm") != "human" ||
                passive.Number("chancePercent") != 10 ||
                passive.Number("targetCurrentHealthPercent") != 20 ||
                passive.Number("specialMonsterOverrideAmount") != 200000 ||
                !SourceModelIsMarcoOwner(actor.Id) ||
                sourceModelRules.NextScriptRoll(1000000) >= 100000)
                return;
            // Original tooltip: each declared human attack has an independent
            // 10% target-current-HP explosion. Special monsters use 200k.
            // There is no speculative cast cost or extra area volley.
            double amount = target.Kind == CharacterEnemyKind.Line
                ? target.Enemy.Health * .2 : 200000;
            ApplySourceModelSeventhTimedHit(actor, target,
                "새새열매 · 인간형 추가 폭발", amount, "explosive");
            // The wave-step owner removes defeated enemies after all
            // declared follow-ups, never in this callback.
        }

        private void SourceModelMarcoPhoenixAttackHeal(CharacterInstance actor)
        {
            if (SourceModelEleventhMarcoForm(actor.Id) != "phoenix") return;
            var passive = SourceModelMarcoPassive(
                actor, "marcoPhoenixAttackHealthRestore");
            var pool = actor.Abilities.Resources;
            if (passive == null || passive.Text("requiresForm") != "phoenix" ||
                passive.Number("healthPerAttack") != 1 || pool == null)
                return;
            // This is a declared-attack +1 HP, not a heal for every enemy hit.
            pool.SetHealth(Math.Min(pool.MaximumHealth, pool.Health + 1));
        }

        private void AdvanceSourceModelMarcoAllyRegeneration(double seconds)
        {
            if (seconds <= 0 || !UsesSourceModelBasicAttacks ||
                !IsFiniteSourceModelMarcoTick(seconds)) return;
            // One same-lane feather aura per recipient. Neither two Marcos
            // nor an owner's own natural SLK +0.5 may multiply this +3.
            foreach (var ally in characters.Values)
            {
                if (IsOriginalPoneglyphChanneling(ally.Id) ||
                    IsSharedObjectiveDeployed(ally.Id)) continue;
                bool hasFeathers = false;
                foreach (var marco in characters.Values)
                {
                    if (!SourceModelIsMarcoOwner(marco.Id) ||
                        IsOriginalPoneglyphChanneling(marco.Id) ||
                        IsSharedObjectiveDeployed(marco.Id) ||
                        !CanShareAllyCombatLane(marco.Id, ally.Id)) continue;
                    var passive = SourceModelMarcoPassive(marco,
                        "healthRegeneration");
                    if (passive == null || passive.Number("perSecond") != 3)
                        continue;
                    hasFeathers = true;
                    break;
                }
                if (!hasFeathers) continue;
                ProvisionSourceModelAuraRecipientResources(ally);
                var pool = ally.Abilities.Resources;
                if (pool != null)
                    pool.SetHealth(Math.Min(pool.MaximumHealth,
                        pool.Health + 3 * seconds));
            }
        }

        private static bool IsFiniteSourceModelMarcoTick(double seconds) =>
            !double.IsNaN(seconds) && !double.IsInfinity(seconds);

        // The native A15N human 15% x3.25 critical is already rolled in
        // SourceModelUnitEffects/BasicDamageResolver. Do not roll it a second
        // time here. Source exact phoenix 15% x3 remains a separate object
        // replacement gap and must not silently inherit a fabricated roll.
        private void SourceModelMarcoBasicSplash(CharacterInstance actor,
            BasicAttackResult hit, SpatialCombatEnemy primary)
        {
            if (!hit.BasicHitApplied || hit.ActualDamage <= 0 ||
                !SourceModelIsMarcoOwner(actor.Id)) return;
            string form = SourceModelEleventhMarcoForm(actor.Id);
            if (form == null) return;
            bool phoenix = form == "phoenix";
            var passive = SourceModelMarcoPassive(actor,
                phoenix ? "marcoPhoenixBasicPhysicalSplash" :
                    "basicAttackMagicSplash");
            double rate = phoenix ? 80 : 100;
            double radius = phoenix ? 400 : 375;
            if (passive == null || passive.Number("basicDamagePercent") != rate ||
                passive.Number("radius") != radius ||
                (phoenix && passive.Text("requiresForm") != "phoenix")) return;
            // The committed basic-hit HP loss is the only available source
            // damage receipt. Use it as the tooltip splash basis, then send
            // each hit through the existing armor/resistance/death system.
            // This approximation is explicitly NOT a Warcraft native
            // pre-armor splash or crit-order equivalence claim.
            double amount = hit.ActualDamage * (rate / 100.0);
            foreach (var victim in SourceModelMarcoAreaTargets(
                         actor, primary, radius, false))
                ApplySourceModelSeventhTimedHit(actor, victim,
                    phoenix ? "환수종 · 불사조 광역 기본공격" :
                        "환수종 · 인간형 광역 기본공격",
                    amount, phoenix ? "physical" : "magical");
            // The caller's shared wave step owns defeated-enemy removal;
            // do not mutate its dictionary in the confirmed-hit callback.
        }

        private static bool SourceModelMarcoBossStoryOnly(ImportedCombatEffect part)
        {
            var types = part.Get("onlyTargetKinds") as IReadOnlyList<object>;
            if (types == null || types.Count != 2) return false;
            return string.Equals(types[0] as string, "boss",
                       StringComparison.Ordinal) &&
                string.Equals(types[1] as string, "story",
                    StringComparison.Ordinal);
        }

        private void SourceModelEleventhMarcoAttack(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            string form = SourceModelEleventhMarcoForm(actor.Id);
            if (form == null || actor.Abilities.Resources == null ||
                !primary.Enemy.IsAlive) return;
            bool phoenix = form == "phoenix";
            var skill = FindSourceSkill(actor, phoenix ?
                "불사조 상태 폭발" : "인간형 돌진");
            if (!SourceModelEleventhSkill(skill, phoenix ? "LI-03:skill:0" :
                    "LI-03:skill:1", phoenix ? "불사조 상태 폭발" : "인간형 돌진",
                    "onAttack", phoenix ? 3 : 4) ||
                skill.ChancePercent != (phoenix ? 18 : 15) ||
                skill.Number("healthCost") != (phoenix ? 10 : 7) ||
                skill.Number("minHealth") != 15 ||
                skill.Target != "area" ||
                skill.Radius != (phoenix ? 500 : 450) ||
                skill.Components[0].Type != (phoenix ? "physical" : "magical") ||
                (phoenix && (skill.Components[0].Number("basicAttackPercent") != 400 ||
                             skill.Components[0].Number("flatAmount") != 500000 ||
                             skill.Components[1].Type != "fixed" ||
                             skill.Components[1].Number("bonusDamagePercent") != 40 ||
                             skill.Components[1].Text("scope") != "singlePrimaryTarget" ||
                             skill.Components[2].Type != "magical" ||
                             skill.Components[2].Number("amount") != 750000 ||
                             skill.Components[2].Number("maxAdditionalTargets") != 8 ||
                             skill.Components[2].Number("radius") != 500 ||
                             skill.Components[2].Text("scope") != "additionalEnemies")) ||
                (!phoenix && (skill.Components[0].Number("amount") != 750000 ||
                              skill.Components[1].Type != "explosive" ||
                              skill.Components[1].Number("targetCurrentHealthPercent") != 30 ||
                              skill.Components[1].Text("scope") != "singlePrimaryTarget" ||
                              !SourceModelMarcoLineOnly(skill.Components[1]) ||
                              skill.Components[2].Type != "stun" ||
                              skill.Components[2].Number("durationSeconds") != 2 ||
                              skill.Components[2].Text("scope") != "singlePrimaryTarget" ||
                              skill.Components[3].Type != "magical" ||
                              skill.Components[3].Number("amount") != 200000 ||
                              skill.Components[3].Text("scope") != "singlePrimaryTarget" ||
                              !SourceModelMarcoBossStoryOnly(skill.Components[3]))))
                return;
            var resource = actor.Abilities.Resources;
            double cost = phoenix ? 10 : 7;
            // The recorded 2.323 tooltip says HP >= 15 BEFORE the proc
            // consumes 10/7 HP. Previous code incorrectly required 25/22,
            // making both low-health form skills fail too early.
            if (resource.Health < 15 || !resource.CanSpend(cost, 0) ||
                !SourceRollSkill(skill)) return;
            resource.Spend(cost, 0);
            double basic = SourceModelUpgradeAdjustedAttackPower(actor);
            long primaryHealthBefore = primary.Enemy.Health;
            foreach (var target in SourceModelEleventhCircle(actor, primary,
                         phoenix ? 500 : 450))
                ApplySourceModelSeventhTimedHit(actor, target, skill.Name,
                    phoenix ? 500000 + basic * 4 : 750000,
                    phoenix ? "physical" : "magical");
            if (phoenix && primary.Enemy.IsAlive)
            {
                // Tooltip: the primary target takes a separate 40% fixed
                // follow-up. With no Warcraft hit receipt available here,
                // use 40% of the ACTUAL first modeled HP loss, never 40%
                // of an unobserved native crit or an extra radius hit.
                // This is TOOLTIP_MODEL_ONLY, not certified JASS ordering.
                long firstHpLoss = Math.Max(0L,
                    primaryHealthBefore - primary.Enemy.Health);
                long fixedBonus;
                if (firstHpLoss > 0 &&
                    SourceModelSafeFixedAmount(firstHpLoss * .40, out fixedBonus) &&
                    fixedBonus > 0)
                    ApplySourceModelFixedPrimaryPacket(InstanceKey(actor.Id),
                        primary, fixedBonus, skill.Name + " · 단일 추가 고정 피해");
            }
            if (phoenix)
            {
                // Additional 8 distinct live enemies. The imported tooltip
                // gives a count but not the native target priority; use a
                // stable same-lane circle around the primary at model radius
                // 500, without selecting the primary a second time.
                int additional = 0;
                foreach (var victim in SourceModelMarcoAreaTargets(
                             actor, primary, 500, true))
                {
                    if (additional >= 8) break;
                    ApplySourceModelSeventhTimedHit(actor, victim,
                        skill.Name + " · 추가 8명 마법", 750000, "magical");
                    additional++;
                }
            }
            else if (primary.Enemy.IsAlive)
            {
                if (primary.Kind == CharacterEnemyKind.Line)
                {
                    ApplySourceModelSeventhTimedHit(actor, primary, skill.Name,
                        primary.Enemy.Health * .3, "explosive");
                    SourceModelEleventhStun(actor, primary, skill.Name, 2);
                }
                else if (primary.Kind == CharacterEnemyKind.Boss ||
                         primary.Kind == CharacterEnemyKind.Story)
                {
                    ApplySourceModelSeventhTimedHit(actor, primary,
                        skill.Name + " · 보스·스토리 추가 마법",
                        200000, "magical");
                    // Boss/story stun immunity and reduction are not yet
                    // verified; never apply the ordinary line stun there.
                }
            }
            RemoveDefeatedEnemies();
        }
    }
}

