using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        // Additional source-identity-bound *mobile* skill models. These do not
        // certify WC3 callback order or override existing native attack rolls.
        private readonly Dictionary<long, int> bulkAutoAttackCounts =
            new Dictionary<long, int>();
        private readonly Dictionary<long, double> bulkAutoFlatPower =
            new Dictionary<long, double>();
        // Sans's Q is an ally-targeted command, not a periodic attack proc.
        // Keep its source-defined bones until this specific Sans leaves the roster.
        private readonly Dictionary<long, int> bulkSansBones =
            new Dictionary<long, int>();
        private readonly Dictionary<long, double> bulkAllyRegenerationEnds =
            new Dictionary<long, double>();
        private readonly Dictionary<long, long> bulkGiftSerials =
            new Dictionary<long, long>();
        private readonly Dictionary<long, string> bulkArmorAuraTargets =
            new Dictionary<long, string>();
        // Source/recipient identity makes the passive buff reversible on
        // roster removal and story deployment changes; resource regen itself
        // is granted only once per eligible ally per frame.
        private readonly Dictionary<string, long> bulkAnyaAttackSpeedRecipients =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private EconomyWallet bulkGiftWallet;

        // The wallet is the combat controller's existing shared wallet, not a
        // parallel stash that would desynchronise the gold/wood HUD.
        public void ConfigureSourceModelGiftWallet(EconomyWallet wallet)
        {
            if (!UsesSourceModelBasicAttacks || processingStep ||
                pendingStepSeconds != 0 || bulkGiftWallet != null)
                throw new InvalidOperationException(
                    "원본 연습 선물상자 지갑은 시작 전에 한 번만 연결합니다.");
            bulkGiftWallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
        }

        // UI/command entry point for the explicitly Brook-consuming Q.
        // The player confirms the cost by tapping a grade-specific feed
        // button. No unknown cooldown/automatic repetition is synthesized.
        public bool TryActivateSourceModelSansBoneQ(long sansInstanceId,
            long brookInstanceId)
        {
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                game.Phase != SessionPhase.Running || processingStep ||
                pendingStepSeconds != 0 || sansInstanceId == brookInstanceId)
                return false;
            CharacterInstance sans, brook;
            if (!characters.TryGetValue(sansInstanceId, out sans) ||
                !characters.TryGetValue(brookInstanceId, out brook) ||
                !sans.AttackReady || sans.Mapping == null ||
                !IsBulkSourceOwner(sans.Catalog.Id,
                    sans.Mapping.AttackSpec.UnitRawCode) ||
                sans.Catalog.Id != "MY-07" ||
                IsStoryDeployed(sansInstanceId) != IsStoryDeployed(brookInstanceId))
                return false;
            string grade = brook.Catalog.Id == "U-03" ? "안흔함" :
                brook.Catalog.Id == "R-18" ? "희귀함" : null;
            if (grade == null) return false;
            var q = AdditionalAutoSkill(sans, "MY-07:skill:0");
            if (q == null || q.Trigger != "manual" ||
                q.Name != "체력본충(Q)") return false;
            int gain = 0;
            foreach (var effect in q.Components)
                if (effect.Type == "boneGain" &&
                    effect.Text("targetUnitGrade") == grade)
                    gain = (int)(effect.Number("amount") ?? 0);
            if (gain <= 0) return false;
            int current;
            bulkSansBones.TryGetValue(sansInstanceId, out current);
            // Spend the actual owned Brook first. RemoveCharacter is also
            // used by fusion/sales and cancels its combat, aura, summon and
            // roster lifetime. If the spend is blocked, no bone is credited.
            // No additional wallet or cloned inventory state is involved.
            RemoveCharacter(brookInstanceId);
            // Never wrap the ledger and accidentally deactivate upgrades.
            bulkSansBones[sansInstanceId] =
                current > int.MaxValue - gain ? int.MaxValue : current + gain;
            pendingCommandEvents.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated,
                InstanceKey(sansInstanceId), InstanceKey(brookInstanceId),
                q.Name, "TOOLTIP_MODEL_ONLY / 뼈 +" + gain, 0));
            if (!BulkSansIsEnhanced(sans) && current < 30)
                return true;
            // Aura groups are reconciled only after the whole Q transaction.
            RefreshEnemyAuras();
            return true;
        }

        public int SourceModelSansBonesOf(long sansInstanceId)
        {
            CharacterInstance actor;
            int bones;
            return characters.TryGetValue(sansInstanceId, out actor) &&
                actor.Catalog.Id == "MY-07" &&
                bulkSansBones.TryGetValue(sansInstanceId, out bones) ? bones : 0;
        }

        private bool BulkSansIsEnhanced(CharacterInstance actor)
        {
            if (actor == null || actor.Catalog.Id != "MY-07" ||
                actor.Profile == null || actor.Profile.UnitId != "MY-07")
                return false;
            object value;
            var condition = actor.Profile.OriginalFields.TryGetValue(
                "enhancedCondition", out value) ?
                value as Dictionary<string, object> : null;
            object resource, required;
            if (condition == null ||
                !condition.TryGetValue("resource", out resource) ||
                !condition.TryGetValue("required", out required) ||
                resource as string != "bones") return false;
            int bones;
            bulkSansBones.TryGetValue(actor.Id, out bones);
            return bones >= Convert.ToInt32(required);
        }

        private List<Dictionary<string, object>> BulkSansEnhancedChanges(
            CharacterInstance actor)
        {
            var result = new List<Dictionary<string, object>>();
            if (!BulkSansIsEnhanced(actor)) return result;
            object value;
            if (!actor.Profile.OriginalFields.TryGetValue(
                    "enhancedChanges", out value)) return result;
            var changes = value as List<object>;
            if (changes != null)
                foreach (var change in changes)
                {
                    var fields = change as Dictionary<string, object>;
                    if (fields != null) result.Add(fields);
                }
            return result;
        }

        private double BulkSansEnhancedAuraAmount(CharacterInstance actor,
            CombatEffectType type, double originalAmount)
        {
            foreach (var change in BulkSansEnhancedChanges(actor))
            {
                object kind, name, value;
                if (!change.TryGetValue("type", out kind) ||
                    !change.TryGetValue("name", out name) ||
                    kind as string != "replacePassive") continue;
                bool armor = type == CombatEffectType.ArmorReduction &&
                    name as string == "armorReductionAura";
                bool slow = type == CombatEffectType.MovementSlowPercent &&
                    name as string == "movementSlowAura";
                if (armor && change.TryGetValue("amount", out value) ||
                    slow && change.TryGetValue("percent", out value))
                    return Convert.ToDouble(value);
            }
            return originalAmount;
        }

        private static bool HasBulkAutomaticOwner(string rawcode)
        {
            switch (rawcode)
            {
                case "h083": case "h04F": case "h09M": case "h08M":
                case "h02P": case "h09Z": case "h02Q": case "h02N":
                case "h036": case "h038": case "H090": case "H08V":
                case "h04D": case "h04A": case "h058": case "h09V":
                case "h08I": case "h09N": case "h0AC": return true;
                default: return false;
            }
        }

        private static bool IsBulkSourceOwner(string catalog, string rawcode)
        {
            switch (catalog)
            {
                case "MY-07": return rawcode == "h083";
                case "I-08": return rawcode == "h04F";
                case "MY-01": return rawcode == "h09M";
                case "MY-08": return rawcode == "h08M";
                case "L-02": return rawcode == "h02P";
                case "L-03": return rawcode == "h09Z";
                case "L-05": return rawcode == "h02Q";
                case "L-12": return rawcode == "h02N";
                case "L-18": return rawcode == "h036";
                case "L-22": return rawcode == "h038";
                case "TR-01": return rawcode == "H090";
                case "TR-02": return rawcode == "H08V";
                case "I-02": return rawcode == "h04D";
                case "I-12": return rawcode == "h04A";
                case "E-04": return rawcode == "h058";
                case "D-06": return rawcode == "h09V";
                case "MY-05": return rawcode == "h08I";
                case "MY-06": return rawcode == "h09N";
                case "MY-13": return rawcode == "h0AC";
                default: return false;
            }
        }

        private void ForgetBulkAutomaticSkills(long actorId)
        {
            bulkAutoAttackCounts.Remove(actorId);
            bulkAutoFlatPower.Remove(actorId);
            bulkSansBones.Remove(actorId);
            bulkAllyRegenerationEnds.Remove(actorId);
            bulkGiftSerials.Remove(actorId);
            string oldTarget;
            if (bulkArmorAuraTargets.TryGetValue(actorId, out oldTarget))
            {
                SpatialCombatEnemy enemy;
                if (enemies.TryGetValue(oldTarget, out enemy))
                    enemy.Enemy.Status.RemoveSource(
                        "practice-tooltip:bulk:" + InstanceKey(actorId) +
                        ":I-08:skill:4:armor-on-hit");
                bulkArmorAuraTargets.Remove(actorId);
            }
            string prefix = "practice-tooltip:bulk:" + InstanceKey(actorId) +
                ":MY-08:passive-ally-speed";
            var removed = new List<string>();
            foreach (var item in bulkAnyaAttackSpeedRecipients)
                if (item.Key.StartsWith(prefix + ":", StringComparison.Ordinal))
                    removed.Add(item.Key);
            foreach (string source in removed)
            {
                long recipient = bulkAnyaAttackSpeedRecipients[source];
                CombatStatusEngine status;
                if (unitStatuses.TryGetValue(InstanceKey(recipient), out status))
                    status.RemoveSource(source);
                bulkAnyaAttackSpeedRecipients.Remove(source);
            }
        }

        private List<SpatialCombatEnemy> BulkTargets(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill)
        {
            string target = skill.Target ?? "single";
            if (target == "single" || target == "self")
                return SourceScriptTargets(actor, primary, new SourceScriptHit(
                    SourceScriptHitShape.Direct, 0, 0, 0, 1, 1,
                    Ordr323AttackType.Spells));
            if (target == "line")
            {
                var p = roster.Get(actor.Id);
                double dx = primary.X - p.X, dy = primary.Y - p.Y;
                return SourceScriptTargets(actor, primary, new SourceScriptHit(
                    SourceScriptHitShape.ProjectileLine, skill.Number("radius") ?? 0,
                    Math.Sqrt(dx * dx + dy * dy) + (skill.Number("radius") ?? 0),
                    0, 1, 1, Ordr323AttackType.Spells));
            }
            return AdditionalAutoCircle(actor, primary, skill.Number("radius") ?? 0,
                target == "casterArea");
        }

        private double BulkScale(ImportedCombatEffect c)
        {
            var range = c.Get("multiplierRange") as List<object>;
            return range == null || range.Count != 2 ? 1 :
                Ordr323MapDamageMath.ScriptDamageBeforeDefense(1,
                    Convert.ToDouble(range[0]), Convert.ToDouble(range[1]),
                    sourceModelRules.NextScriptRoll(1000000) / 1000000.0);
        }

        private void BulkStatus(CharacterInstance actor, SpatialCombatEnemy victim,
            ImportedCombatSkill skill, CombatEffectType type, double amount,
            double duration, string part)
        {
            if (!victim.Enemy.IsAlive || !CanAttackInThisLane(actor, victim))
                return;
            string source = "practice-tooltip:bulk:" + InstanceKey(actor.Id) +
                ":" + skill.Id + ":" + part;
            victim.Enemy.Status.RemoveSource(source);
            victim.Enemy.Status.Apply(new CombatEffect(source, type,
                duration > 0 ? CombatEffectOrigin.TriggeredSkill :
                    CombatEffectOrigin.Aura, amount, duration));
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                InstanceKey(actor.Id), victim.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / " + part, 0));
        }

        private void BulkSelfStatus(CharacterInstance actor,
            ImportedCombatSkill skill, CombatEffectType type,
            double amount, double duration, string part)
        {
            CombatStatusEngine status;
            if (!unitStatuses.TryGetValue(InstanceKey(actor.Id), out status)) return;
            string source = "practice-tooltip:bulk:" + InstanceKey(actor.Id) +
                ":" + skill.Id + ":" + part;
            status.RemoveSource(source);
            status.Apply(new CombatEffect(source, type,
                duration > 0 ? CombatEffectOrigin.TriggeredSkill :
                    CombatEffectOrigin.Aura, amount, duration));
        }

        private void BulkDamage(CharacterInstance actor,
            SpatialCombatEnemy victim, ImportedCombatSkill skill,
            double raw, string school)
        {
            if (raw <= 0 || !victim.Enemy.IsAlive ||
                !CanAttackInThisLane(actor, victim)) return;
            if (school != "fixed")
            {
                AdditionalAutoDamage(actor, new[] { victim }, skill.Name,
                    raw, school);
                return;
            }
            long actual;
            if (!resolution.TryApplySkillDamage(InstanceKey(actor.Id), victim.Id,
                    (long)Math.Floor(Math.Min(raw, long.MaxValue - 1.0)),
                    out actual)) return;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillDamage,
                InstanceKey(actor.Id), victim.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 고정 피해", actual));
            if (!victim.Enemy.IsAlive)
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.EnemyDefeated,
                    InstanceKey(actor.Id), victim.Id, skill.Name,
                    "연습 기술 피해로 처치", actual));
        }

        private void BulkTimedDamage(CharacterInstance actor,
            IReadOnlyList<SpatialCombatEnemy> victims,
            ImportedCombatSkill skill, double raw, string school,
            double interval, double duration)
        {
            if (interval <= 0 || duration <= 0) return;
            int ticks = (int)Math.Floor(duration / interval + .000001);
            if (ticks <= 0 || ticks > 120) return;
            bool lane = IsStoryDeployed(actor.Id);
            long revision = SourceModelLaneRevision(actor.Id);
            foreach (var victim in victims)
                for (int tick = 1; tick <= ticks; tick++)
                {
                    var target = victim;
                    effectExecutor.SchedulePracticeSourceAttack(
                        InstanceKey(actor.Id), target.Id, tick * interval, () => {
                            SpatialCombatEnemy current;
                            if (!CanFinishSourceModelDelayedAttack(actor.Id, lane,
                                    revision) || !enemies.TryGetValue(target.Id, out current) ||
                                !ReferenceEquals(current, target)) return;
                            BulkDamage(actor, target, skill, raw, school);
                        });
                }
        }

        private void BulkApplyDamagePacket(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            ImportedCombatEffect c, IReadOnlyList<SpatialCombatEnemy> targets,
            double damageScale = 1)
        {
            double scale = BulkScale(c);
            int hits = (int)(c.Number("hits") ?? 1);
            hits = Math.Max(1, Math.Min(30, hits));
            foreach (var victim in targets)
            {
                if (c.Text("scope") == "singlePrimaryTarget" &&
                    !ReferenceEquals(victim, primary)) continue;
                if (!SourceModelNextThirtyComponentTargets(c, primary, victim))
                    continue;
                double raw = (c.Number("amount") ?? c.Number("amountPerHit") ??
                    c.Number("amountPerAttack") ?? 0) * scale;
                // No pinned source strength snapshot is available for this
                // packet. Keep the literal base rather than a guessed STR.
                if (c.Number("intelligenceScalingPerHit").HasValue)
                {
                    // An original intelligence provider has not been supplied:
                    // retain the documented base packet, not a guessed stat.
                }
                raw += victim.Enemy.MaxHealth *
                    (c.Number("plusTargetMaxHealthPercent") ?? 0) / 100;
                if (c.Number("targetMaxHealthPercent").HasValue &&
                    victim.Kind != CharacterEnemyKind.Boss &&
                    victim.Kind != CharacterEnemyKind.Story)
                    raw += victim.Enemy.MaxHealth *
                        c.Number("targetMaxHealthPercent").Value / 100;
                if (c.Number("extraSingleAmount").HasValue &&
                    ReferenceEquals(victim, primary))
                    raw += c.Number("extraSingleAmount").Value;
                for (int hit = 0; hit < hits && victim.Enemy.IsAlive; hit++)
                    BulkDamage(actor, victim, skill, raw * damageScale, c.Type);
            }
        }

        private void BulkApplyStandard(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill)
        {
            var targets = BulkTargets(actor, primary, skill);
            foreach (var c in skill.Components)
            {
                if (c.Type == "physical" || c.Type == "magical" ||
                    c.Type == "fixed" || c.Type == "explosive")
                {
                    BulkApplyDamagePacket(actor, primary, skill, c, targets);
                    continue;
                }
                if (c.Type == "stun" || c.Type == "armorReduction" ||
                    c.Type == "movementSlow" ||
                    c.Type == "magicDamageAmplification" ||
                    c.Type == "allDamageAmplification")
                {
                    CombatEffectType type = c.Type == "stun" ? CombatEffectType.Stun :
                        c.Type == "armorReduction" ? CombatEffectType.ArmorReduction :
                        c.Type == "movementSlow" ? CombatEffectType.MovementSlowPercent :
                        c.Type == "allDamageAmplification" ?
                            CombatEffectType.AllDamageAmplification :
                            CombatEffectType.MagicDamageAmplification;
                    double value = c.Type == "stun" ? 1 :
                        (c.Number("amount") ?? c.Number("percent") ?? 0);
                    double duration = c.Number("durationSeconds") ??
                        skill.Number("durationSeconds") ?? 0;
                    // A per-hit proc with no source duration does not
                    // authorize a made-up three-second or infinite debuff.
                    if (duration <= 0) continue;
                    foreach (var victim in targets)
                        if (type != CombatEffectType.Stun ||
                            victim.Kind == CharacterEnemyKind.Line)
                            BulkStatus(actor, victim, skill, type, value,
                                duration, c.Type);
                }
            }
        }

        private void ExecuteBulkAutoAttack(CharacterInstance actor,
            SpatialCombatEnemy primary, string rawcode)
        {
            if (!IsBulkSourceOwner(actor.Catalog.Id, rawcode) ||
                !primary.Enemy.IsAlive || !CanAttackInThisLane(actor, primary)) return;
            string owner = actor.Catalog.Id;
            if (owner == "MY-07")
            {
                BulkRunAttackSkill(actor, primary, "MY-07:skill:1");
                BulkTrySansJudgment(actor, primary);
                int count;
                bulkAutoAttackCounts.TryGetValue(actor.Id, out count);
                count++;
                bulkAutoAttackCounts[actor.Id] = count;
                if (count % 20 == 0 && !BulkSansIsEnhanced(actor))
                {
                    var tired = AdditionalAutoSkill(actor, "MY-07:skill:3");
                    if (tired != null && tired.Components.Count == 2)
                    {
                        BulkApplyStandard(actor, primary, tired);
                        BulkSelfStatus(actor, tired, CombatEffectType.Stun, 1,
                            tired.Components[1].Number("durationSeconds") ?? 3,
                            "fatigue");
                    }
                }
            }
            else if (owner == "I-08")
            {
                BulkTryGabanManaStrike(actor, primary);
                BulkRunAttackSkill(actor, primary, "I-08:skill:1");
                BulkRunAttackSkill(actor, primary, "I-08:skill:2");
                BulkRunAttackSkill(actor, primary, "I-08:skill:5");
            }
            else if (owner == "MY-01")
            {
                BulkRunAttackSkill(actor, primary, "MY-01:skill:1");
                BulkRunAttackSkill(actor, primary, "MY-01:skill:3");
            }
            else if (owner == "L-02")
            {
                var pool = actor.Abilities.Resources;
                if (pool != null)
                    pool.SetHealth(Math.Min(pool.MaximumHealth, pool.Health + 1));
                BulkRunAttackSkill(actor, primary, "L-02:skill:0");
            }
            else if (owner == "L-03")
            {
                if (primary.Kind == CharacterEnemyKind.Boss ||
                    primary.Kind == CharacterEnemyKind.Enraged)
                    BulkRunAttackSkill(actor, primary, "L-03:skill:0");
                BulkRunAttackSkill(actor, primary, "L-03:skill:1");
            }
            else if (owner == "L-05")
            {
                int count;
                bulkAutoAttackCounts.TryGetValue(actor.Id, out count);
                bulkAutoAttackCounts[actor.Id] = ++count;
                BulkRunAttackSkill(actor, primary, "L-05:skill:0", count % 7 == 0);
            }
            else if (owner == "L-12")
            {
                // The skill has NO source chance value; do not secretly roll
                // it on every attack. Only its explicit 8.5s line deletion
                // can be attached without inventing a probability.
                var absorb = AdditionalAutoSkill(actor, "L-12:skill:0");
                if (absorb != null && primary.Kind == CharacterEnemyKind.Line)
                {
                    string key = InstanceKey(actor.Id) + ":L-12:skill:0:delete";
                    double until;
                    if (!additionalAutoReadyAt.TryGetValue(key, out until) ||
                        Time >= until)
                    {
                        additionalAutoReadyAt[key] = Time + 8.5;
                        AdditionalAutoDeleteLine(actor, primary, absorb);
                        // The source records this 8.5-second event but does
                        // not specify an independent proc probability. Bind
                        // the companion effects to this SAME accepted event.
                        BulkSelfStatus(actor, absorb,
                            CombatEffectType.AttackSpeedBuff, 35, 3,
                            "shadow-absorption-speed");
                        BulkSpawnShadowZombie(actor, absorb);
                    }
                }
            }
            else if (owner == "L-18")
            {
                BulkRunAttackSkill(actor, primary, "L-18:skill:0");
                BulkRunAttackSkill(actor, primary, "L-18:skill:1");
            }
            else if (owner == "TR-01")
            {
                BulkRunAttackSkill(actor, primary, "TR-01:skill:1");
                BulkRunAttackSkill(actor, primary, "TR-01:skill:3");
                BulkRunAttackSkill(actor, primary, "TR-01:skill:4");
            }
            else if (owner == "TR-02")
                BulkRunAttackSkill(actor, primary, "TR-02:skill:3");
            else if (owner == "I-12")
                BulkRunAttackSkill(actor, primary, "I-12:skill:1");
            else if (owner == "MY-06")
            {
                BulkRunAttackSkill(actor, primary, "MY-06:skill:2");
                BulkRunAttackSkill(actor, primary, "MY-06:skill:3");
            }
            else if (owner == "MY-13")
            {
                BulkRunAttackSkill(actor, primary, "MY-13:skill:0");
                BulkRunAttackSkill(actor, primary, "MY-13:skill:2");
                BulkTryHigumaRobbery(actor, primary);
            }
        }

        private void BulkRunAttackSkill(CharacterInstance actor,
            SpatialCombatEnemy primary, string id, bool guaranteed = false)
        {
            var skill = AdditionalAutoSkill(actor, id);
            if (skill == null || (!guaranteed && !AdditionalAutoRoll(skill))) return;
            if (skill.Components.Count == 0) return;
            if (id == "L-02:skill:0" || id == "L-03:skill:0")
            {
                // The exact SLK object HP is provisioned by the shared
                // source resource catalog (h02P/h09Z, no invented HP).
                var pool = actor.Abilities.Resources;
                double cost = skill.Number("healthCost") ?? 0;
                if (pool == null || cost <= 0 || !pool.CanSpend(cost, 0)) return;
                pool.Spend(cost, 0);
            }
            if (id == "I-08:skill:2")
            {
                // A flat +750,000 is NOT +750,000 percent. The common
                // AttackPowerBuff status stores percentage increases.
                double current;
                bulkAutoFlatPower.TryGetValue(actor.Id, out current);
                bulkAutoFlatPower[actor.Id] = Math.Max(current,
                    skill.Components[0].Number("amount") ?? 0);
            }
            else if (id == "MY-07:skill:1")
            {
                BulkApplyStandard(actor, primary, skill);
                int bones;
                bulkSansBones.TryGetValue(actor.Id, out bones);
                foreach (var component in skill.Components)
                    if (component.Type == "extraCast")
                    {
                        int per = (int)(component.Number("perBones") ?? 0);
                        int each = (int)(component.Number("countPer") ?? 0);
                        if (per <= 0 || each <= 0) break;
                        // Guard the simulation's frame budget, not a
                        // claimed original Warcraft maximum bone count.
                        long extra = (long)(bones / per) * each;
                        if (extra > 1024)
                        {
                            ReportSourceModelNextThirtyDeferred(
                                InstanceKey(actor.Id), primary.Id,
                                "샌즈 추가 발동 1024회/프레임 안전 제한; 남은 발동 보류");
                            extra = 1024;
                        }
                        for (long n = 0; n < extra; n++)
                            BulkApplyStandard(actor, primary, skill);
                        break;
                    }
            }
            else if (id == "L-05:skill:0")
            {
                var targets = BulkTargets(actor, primary, skill);
                BulkApplyDamagePacket(actor, primary, skill,
                    skill.Components[0], targets);
                double multiple = primary.Kind == CharacterEnemyKind.Story ? 3 :
                    primary.Kind == CharacterEnemyKind.Boss ? 2 : 1;
                BulkDamage(actor, primary, skill,
                    (skill.Components[1].Number("amount") ?? 0) * multiple,
                    "magical");
                foreach (var enemy in targets)
                    if (enemy.Kind == CharacterEnemyKind.Line)
                        BulkStatus(actor, enemy, skill, CombatEffectType.Stun,
                            1, 2.25, "headbutt");
                BulkSelfStatus(actor, skill, CombatEffectType.Stun, 1, .65,
                    "headbutt-recovery");
            }
            else if (id == "I-08:skill:1")
            {
                if (skill.Components.Count != 2 ||
                    skill.Components[1].Type != "physical" ||
                    skill.Components[1].Text("basis") != "targetMaxHealth" ||
                    skill.Components[1].Number("percent") != 20) return;
                BulkApplyDamagePacket(actor, primary, skill,
                    skill.Components[0], BulkTargets(actor, primary, skill));
                if (primary.Kind == CharacterEnemyKind.Line &&
                    primary.Enemy.IsAlive)
                    BulkDamage(actor, primary, skill,
                        primary.Enemy.MaxHealth * .20, "physical");
            }
            else if (id == "L-03:skill:1")
            {
                if (skill.Components.Count != 2 ||
                    skill.Components[1].Type != "fixed" ||
                    skill.Components[1].Number("extraTargetMaxHealthPercent") != 7.5)
                    return;
                var targets = BulkTargets(actor, primary, skill);
                BulkApplyDamagePacket(actor, primary, skill,
                    skill.Components[0], targets);
                foreach (var target in targets)
                    if (target.Kind == CharacterEnemyKind.Enraged &&
                        target.Enemy.IsAlive)
                        BulkDamage(actor, target, skill,
                            target.Enemy.MaxHealth * .075, "fixed");
            }
            else if (id == "L-18:skill:0")
            {
                if (skill.Components.Count != 2 ||
                    skill.Components[0].Type != "magical" ||
                    skill.Components[1].Type != "physical" ||
                    skill.Components[1].Text("basis") != "skillDamage" ||
                    skill.Components[1].Number("percent") != 225) return;
                var targets = BulkTargets(actor, primary, skill);
                BulkApplyDamagePacket(actor, primary, skill,
                    skill.Components[0], targets);
                // Skill-damage scaling uses the tooltip's stated base
                // packet; native magic-armor-to-physical ordering is unknown.
                if (primary.Kind != CharacterEnemyKind.Line &&
                    primary.Enemy.IsAlive)
                    BulkDamage(actor, primary, skill,
                        (skill.Components[0].Number("amount") ?? 0) * 2.25,
                        "physical");
            }
            else if (id == "MY-01:skill:3")
            {
                BulkApplyStandard(actor, primary, skill);
                var dot = skill.Components[1];
                BulkTimedDamage(actor, BulkTargets(actor, primary, skill),
                    skill, dot.Number("amountPerTick") ?? 0, "magical",
                    dot.Number("tickIntervalSeconds") ?? .2,
                    dot.Number("durationSeconds") ?? 2);
            }
            else if (id == "TR-01:skill:1")
            {
                BulkApplyStandard(actor, primary, skill);
                BulkApplyLingeringSlow(actor, primary, skill);
            }
            else if (id == "MY-13:skill:0")
            {
                BulkApplyStandard(actor, primary, skill);
                // Reuse the SAME existing practice max-speed policy as
                // Katakuri, never assert Warcraft's unobserved native cap.
                var cap = skill.Components[1];
                if (cap.Type == "selfAttackSpeedMax" &&
                    cap.Number("durationSeconds") == 1.25)
                    BulkSelfStatus(actor, skill,
                        CombatEffectType.AttackSpeedBuff, 400, 1.25,
                        "practice-max-speed");
            }
            else if (id == "I-12:skill:1")
            {
                BulkApplyStandard(actor, primary, skill);
                // The 75% modifier belongs to this ability's own damage
                // packet; last basic-hit damage is not a valid substitute.
            }
            else if (id == "L-18:skill:1")
            {
                BulkApplyStandard(actor, primary, skill);
                BulkApplyAllyAura(actor, skill, 30, 6);
            }
            else if (id == "TR-01:skill:4")
            {
                if (primary.Kind == CharacterEnemyKind.Story ||
                    primary.Kind == CharacterEnemyKind.Enraged)
                {
                    BulkDamage(actor, primary, skill,
                        primary.Kind == CharacterEnemyKind.Story ? 3000000 :
                            4000000, "magical");
                    foreach (var c in skill.Components)
                        if (c.Type != "magical")
                            BulkApplyStandardStatusOnly(actor, primary, skill, c);
                }
                else BulkApplyStandard(actor, primary, skill);
            }
            else if (id == "MY-06:skill:2" || id == "MY-06:skill:3")
            {
                // Current MP comes from the exact source-object resource
                // pool. The tooltip's 50%-of-current-MP bonus is capped 75%.
                double damageScale = BulkCurrentManaDamageScale(actor, skill);
                var targets = BulkTargets(actor, primary, skill);
                BulkApplyDamagePacket(actor, primary, skill,
                    skill.Components[0], targets, damageScale);
                if (id == "MY-06:skill:2" && skill.Components.Count > 1)
                {
                    var single = skill.Components[1];
                    BulkDamage(actor, primary, skill,
                        ((single.Number("extraSingleAmount") ?? 0) +
                         primary.Enemy.MaxHealth *
                         (single.Number("plusTargetMaxHealthPercent") ?? 0) / 100)
                        * damageScale,
                        "magical");
                    BulkRestoreAllyMana(actor, skill);
                }
                else if (id == "MY-06:skill:3" &&
                    skill.Components.Count > 1)
                {
                    double baseHit = skill.Components[0].Number("amountPerHit") ?? 0;
                    double bonus = skill.Components[1].Number(
                        "extraSingleTargetDamagePercent") ?? 0;
                    BulkDamage(actor, primary, skill,
                        baseHit * 2 * bonus / 100 * damageScale, "magical");
                }
            }
            else BulkApplyStandard(actor, primary, skill);
            ExecuteBulkAutoAfterSkill(actor, primary, skill);
        }

        private void BulkTrySansJudgment(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            foreach (var change in BulkSansEnhancedChanges(actor))
            {
                object type, name;
                if (!change.TryGetValue("type", out type) ||
                    !change.TryGetValue("name", out name) ||
                    type as string != "unlockSkill" ||
                    name as string != "심판자") continue;
                var unlocked = new ImportedCombatSkill("MY-07", 4,
                    new Dictionary<string, object>(change, StringComparer.Ordinal));
                if (unlocked.Trigger == "onAttack" &&
                    unlocked.Target == "single" &&
                    AdditionalAutoRoll(unlocked) &&
                    unlocked.Components.Count == 1 &&
                    unlocked.Components[0].Type == "physical")
                    BulkApplyStandard(actor, primary, unlocked);
                return;
            }
        }

        // The base Gaster packet is cast by the existing approved active
        // executor (100 original MP, 24 x 350,000 magic). Only the enhanced
        // extras belong here, after that SAME approved successful cast.
        private void BulkApplySansEnhancedGaster(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill accepted,
            IReadOnlyList<SpatialCombatEnemy> targets)
        {
            if (actor == null || accepted == null ||
                accepted.Id != "MY-07:skill:2" ||
                !BulkSansIsEnhanced(actor)) return;
            foreach (var change in BulkSansEnhancedChanges(actor))
            {
                object kind, skillName, raw;
                if (!change.TryGetValue("type", out kind) ||
                    !change.TryGetValue("skill", out skillName) ||
                    kind as string != "addSkillEffect" ||
                    skillName as string != accepted.Name ||
                    !change.TryGetValue("components", out raw)) continue;
                var components = raw as List<object>;
                if (components == null) return;
                int hitCount = 0;
                foreach (var original in accepted.Components)
                    if (original.Type == "magical" &&
                        original.Number("hits").HasValue)
                        hitCount = (int)original.Number("hits").Value;
                foreach (var entry in components)
                {
                    var fields = entry as Dictionary<string, object>;
                    if (fields == null) continue;
                    var effect = new ImportedCombatEffect(fields);
                    if (effect.Type == "physical" && hitCount > 0)
                    {
                        for (int hit = 0; hit < hitCount; hit++)
                            BulkApplyDamagePacket(actor, primary, accepted,
                                effect, targets);
                    }
                    else if (effect.Type == "damageOverTime" &&
                        effect.Text("school") == "magical")
                        BulkTimedDamage(actor, targets, accepted,
                            effect.Number("amountPerTick") ?? 0, "magical",
                            effect.Number("intervalSeconds") ?? 0,
                            effect.Number("durationSeconds") ?? 0);
                    else if (effect.Type == "armorReduction")
                        foreach (var victim in targets)
                            BulkStatus(actor, victim, accepted,
                                CombatEffectType.ArmorReduction,
                                effect.Number("amount") ?? 0,
                                effect.Number("durationSeconds") ?? 0,
                                "enhanced-gaster-armor");
                }
                return;
            }
        }

        private void BulkApplyStandardStatusOnly(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            ImportedCombatEffect component)
        {
            string type = component.Type;
            CombatEffectType status;
            if (type == "stun") status = CombatEffectType.Stun;
            else if (type == "magicDamageAmplification")
                status = CombatEffectType.MagicDamageAmplification;
            else if (type == "allDamageAmplification")
                status = CombatEffectType.AllDamageAmplification;
            else return;
            double? duration = component.Number("durationSeconds");
            if (!duration.HasValue || duration.Value <= 0) return;
            BulkStatus(actor, primary, skill, status,
                type == "stun" ? 1 : (component.Number("percent") ?? 0),
                duration.Value, type);
        }

        private void BulkApplyAllyAura(CharacterInstance actor,
            ImportedCombatSkill skill, double percent, double duration)
        {
            var point = roster.Get(actor.Id);
            foreach (var ally in characters.Values)
            {
                if (!ally.AttackReady || IsStoryDeployed(ally.Id) !=
                    IsStoryDeployed(actor.Id)) continue;
                var other = roster.Get(ally.Id);
                double dx = point.X - other.X, dy = point.Y - other.Y;
                if (dx * dx + dy * dy > 900 * 900) continue;
                CombatStatusEngine state;
                if (!unitStatuses.TryGetValue(InstanceKey(ally.Id), out state))
                    continue;
                string source = "practice-tooltip:bulk:" + InstanceKey(actor.Id) +
                    ":" + skill.Id + ":ally:" + InstanceKey(ally.Id);
                state.RemoveSource(source);
                state.Apply(new CombatEffect(source, CombatEffectType.AttackPowerBuff,
                    CombatEffectOrigin.TriggeredSkill, percent, duration));
                ProvisionSourceModelAuraRecipientResources(ally);
                // L-18's source packet also specifies +0.25 HP / MP per
                // second for the same six seconds. Keep this on the same
                // status clock so a large combat frame cannot over-credit.
                bulkAllyRegenerationEnds[ally.Id] =
                    state.CurrentTime + duration;
            }
        }

        private void AdvanceBulkAllyRegeneration(double seconds)
        {
            AdvanceBulkAnyaPassiveAura(seconds);
            if (seconds <= 0 || bulkAllyRegenerationEnds.Count == 0) return;
            var ended = new List<long>();
            foreach (var entry in bulkAllyRegenerationEnds)
            {
                CharacterInstance ally;
                CombatStatusEngine status;
                if (!characters.TryGetValue(entry.Key, out ally) ||
                    !unitStatuses.TryGetValue(InstanceKey(entry.Key), out status))
                {
                    ended.Add(entry.Key);
                    continue;
                }
                double active = Math.Min(seconds,
                    Math.Max(0, entry.Value - status.CurrentTime));
                var pool = ally.Abilities.Resources;
                if (pool != null && active > 0)
                {
                    pool.SetHealth(Math.Min(pool.MaximumHealth,
                        pool.Health + .25 * active));
                    pool.SetMana(Math.Min(pool.MaximumMana,
                        pool.Mana + .25 * active));
                }
                if (entry.Value <= status.CurrentTime + seconds)
                    ended.Add(entry.Key);
            }
            foreach (long id in ended) bulkAllyRegenerationEnds.Remove(id);
        }

        private void AdvanceBulkAnyaPassiveAura(double seconds)
        {
            if (!UsesSourceModelBasicAttacks || seconds <= 0) return;
            var active = new Dictionary<string, long>(StringComparer.Ordinal);
            var regened = new HashSet<long>();
            foreach (var owner in characters.Values)
            {
                if (!owner.AttackReady || owner.Profile == null ||
                    owner.Profile.UnitId != "MY-08" ||
                    owner.Catalog.Id != "MY-08" || owner.Mapping == null ||
                    owner.Mapping.AttackSpec.UnitRawCode != "h08M") continue;
                // Exact values from MY-08 tooltip. The fixed mobile policy
                // makes ally aura reach lane-wide, not a new 800-unit cap.
                var passives = owner.Profile.Passives;
                bool hp = false, mp = false, speed = false;
                foreach (var passive in passives)
                {
                    if (passive.Type == "allyHealthRegeneration" &&
                        passive.Number("perSecond") == 2) hp = true;
                    if (passive.Type == "allyManaRegeneration" &&
                        passive.Number("perSecond") == 1.75) mp = true;
                    if (passive.Type == "allyAttackSpeedBuff" &&
                        passive.Number("percent") == 30) speed = true;
                }
                if (!hp || !mp || !speed) continue;
                string source = "practice-tooltip:bulk:" + InstanceKey(owner.Id) +
                    ":MY-08:passive-ally-speed";
                foreach (var ally in characters.Values)
                {
                    if (!ally.AttackReady ||
                        IsStoryDeployed(ally.Id) != IsStoryDeployed(owner.Id))
                        continue;
                    CombatStatusEngine status;
                    if (!unitStatuses.TryGetValue(InstanceKey(ally.Id), out status))
                        continue;
                    active[source + ":" + InstanceKey(ally.Id)] = ally.Id;
                    if (!bulkAnyaAttackSpeedRecipients.ContainsKey(
                            source + ":" + InstanceKey(ally.Id)))
                    {
                        status.Apply(new CombatEffect(
                            source + ":" + InstanceKey(ally.Id),
                            CombatEffectType.AttackSpeedBuff,
                            CombatEffectOrigin.Aura, 30, 0));
                    }
                    if (!regened.Add(ally.Id)) continue;
                    ProvisionSourceModelAuraRecipientResources(ally);
                    var pool = ally.Abilities.Resources;
                    if (pool == null) continue;
                    pool.SetHealth(Math.Min(pool.MaximumHealth,
                        pool.Health + 2 * seconds));
                    pool.SetMana(Math.Min(pool.MaximumMana,
                        pool.Mana + 1.75 * seconds));
                }
            }
            foreach (var stale in bulkAnyaAttackSpeedRecipients)
            {
                if (active.ContainsKey(stale.Key)) continue;
                CombatStatusEngine status;
                if (unitStatuses.TryGetValue(InstanceKey(stale.Value), out status))
                    status.RemoveSource(stale.Key);
            }
            bulkAnyaAttackSpeedRecipients.Clear();
            foreach (var item in active)
                bulkAnyaAttackSpeedRecipients.Add(item.Key, item.Value);
        }

        private void BulkRestoreAllyMana(CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            if (skill.Components.Count < 3 ||
                skill.Components[2].Type != "manaRestoration" ||
                skill.Components[2].Text("to") != "nearbyAllies") return;
            double amount = skill.Components[2].Number("amount") ?? 0;
            if (amount <= 0) return;
            foreach (var ally in characters.Values)
            {
                if (ally.Id == actor.Id || !ally.AttackReady ||
                    IsStoryDeployed(ally.Id) != IsStoryDeployed(actor.Id))
                    continue;
                var pool = ally.Abilities.Resources;
                if (pool == null || pool.MaximumMana <= 0) continue;
                // The source lacks a separate nearby radius; use the
                // skill's pinned 415 AoE, not global lane-wide mana.
                var origin = roster.Get(actor.Id);
                var target = roster.Get(ally.Id);
                double dx = origin.X - target.X, dy = origin.Y - target.Y;
                double radius = skill.Number("radius") ?? 0;
                if (radius <= 0 || dx * dx + dy * dy > radius * radius)
                    continue;
                pool.SetMana(Math.Min(pool.MaximumMana, pool.Mana + amount));
            }
        }

        private static double BulkCurrentManaDamageScale(CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            var pool = actor.Abilities.Resources;
            double? percentByMana = skill.Number("damageBonusPercentByCurrentMana");
            double? cap = skill.Number("maxDamageBonusPercent");
            if (pool == null || !percentByMana.HasValue || !cap.HasValue ||
                percentByMana.Value != 50 || cap.Value != 75)
                return 1;
            // 100 current MP grants +50% damage, up to +75% at 150 MP.
            // This is a tooltip-only formula; no native JASS ordering claim.
            return 1 + Math.Min(cap.Value,
                pool.Mana * percentByMana.Value / 100) / 100;
        }

        private void BulkSpawnShadowZombie(CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            foreach (var component in skill.Components)
            {
                if (component.Type != "summon" ||
                    component.Text("unit") != "장군좀비" ||
                    component.Get("inheritsAttackSpeedAndAttackPower") == null)
                    continue;
                double? lifetime = component.Number("durationSeconds");
                if (!lifetime.HasValue || lifetime.Value <= 0 ||
                    component.Number("basicAttackExplosivePercent") != 60 ||
                    component.Number("basicAttackRadius") != 375) return;
                var point = roster.Get(actor.Id);
                // This skill says it inherits the owner's attack stats but
                // supplies no zombie object ID. A model-only shadow uses
                // the exact owner's source object, never a fake zombie ID.
                var summon = SpawnSourceTemporaryActor(actor, skill.Name,
                    "장군좀비", lifetime.Value,
                    actor.Mapping.AttackSpec.UnitRawCode,
                    spawnX: point.X, spawnY: point.Y, modeledCombat: true);
                SourceIndependentAttacker attacker;
                if (sourceIndependentAttackers.TryGetValue(
                    SummonAttackKey(summon.Id), out attacker))
                    attacker.ModelShadowZombie = true;
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.SkillActivated,
                    InstanceKey(actor.Id), null, skill.Name,
                    "TOOLTIP_MODEL_ONLY 장군좀비 18초 / 원본 개별 소환 확률 미확인", 0));
                return;
            }
        }

        private void BulkGenerateAndOpenGift(CharacterInstance actor,
            ImportedCombatSkill sourceSkill)
        {
            var opened = AdditionalAutoSkill(actor, "MY-08:skill:5");
            if (bulkGiftWallet == null || opened == null ||
                opened.Trigger != "onGiftBoxOpened" ||
                opened.Components.Count != 2 ||
                opened.Components[0].Type != "rewardGold" ||
                opened.Components[1].Type != "rewardLumber" ||
                opened.Components[0].Number("chancePercent") != 70 ||
                opened.Components[1].Number("chancePercent") != 30)
            {
                ReportSourceModelNextThirtyDeferred(InstanceKey(actor.Id),
                    null, "와쿠와쿠 선물상자 공유 지갑 또는 보상 명세 누락");
                return;
            }
            double gold = opened.Components[0].Number("amount") ?? 0;
            double wood = opened.Components[1].Number("amount") ?? 0;
            if (gold != 2500 || wood != 1) return;
            long serial;
            bulkGiftSerials.TryGetValue(actor.Id, out serial);
            if (serial == long.MaxValue) throw new OverflowException("선물상자 번호 초과");
            serial++;
            bool goldReward = sourceModelRules.NextScriptRoll(10000) < 7000;
            var reward = new ResourceBundle().Add(
                goldReward ? "gold" : "wood", goldReward ? 2500 : 1);
            // Once receipt: a repeated callback can never pay the same gift
            // twice. Opening happens immediately in the mobile model.
            string receipt = "practice-gift:MY-08:" + actor.Id + ":" + serial;
            if (!bulkGiftWallet.TryGrantOnce(receipt, reward)) return;
            bulkGiftSerials[actor.Id] = serial;
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), null, sourceSkill.Name,
                "TOOLTIP_MODEL_ONLY 선물상자 생성·자동 개봉 / " +
                (goldReward ? "금 2500" : "나무 1"), 0));
        }

        private void BulkTryHigumaRobbery(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            var skill = AdditionalAutoSkill(actor, "MY-13:skill:1");
            if (skill == null || skill.Trigger != "activeHealth" ||
                skill.Number("healthCost") != 75 ||
                skill.Components.Count != 2 || bulkGiftWallet == null ||
                skill.Components[0].Type != "fixed" ||
                skill.Components[0].Number("amount") != 5000000 ||
                skill.Components[1].Type != "currencyReward") return;
            // Higuma's 75HP / +0.3HP/s are the pinned h0AC SLK values.
            // An active-health tooltip is auto-fired on the first eligible
            // attack with sufficient HP (the user-defined mobile policy).
            ProvisionSourceModelAuraRecipientResources(actor);
            var pool = actor.Abilities.Resources;
            if (pool == null || !pool.CanSpend(75, 0)) return;
            var rewardOptions = skill.Components[1].Get("outcomes") as List<object>;
            if (rewardOptions == null || rewardOptions.Count != 2) return;
            var goldOption = rewardOptions[0] as Dictionary<string, object>;
            var woodOption = rewardOptions[1] as Dictionary<string, object>;
            if (goldOption == null || woodOption == null ||
                OrdrJson.Number(goldOption, "chancePercent") != 55 ||
                OrdrJson.Number(woodOption, "chancePercent") != 45 ||
                OrdrJson.Number(goldOption, "gold") != 1500 ||
                OrdrJson.Number(woodOption, "lumber") != 1) return;
            long serial;
            bulkGiftSerials.TryGetValue(actor.Id, out serial);
            if (serial == long.MaxValue) throw new OverflowException("강탈 영수증 번호 초과");
            bool earnGold = sourceModelRules.NextScriptRoll(10000) < 5500;
            var reward = new ResourceBundle().Add(
                earnGold ? "gold" : "wood", earnGold ? 1500 : 1);
            string receipt = "practice-robbery:MY-13:" + actor.Id +
                ":" + (serial + 1);
            double before = pool.Health;
            pool.Spend(75, 0);
            if (!bulkGiftWallet.TryGrantOnce(receipt, reward))
            {
                pool.SetHealth(before);
                return;
            }
            bulkGiftSerials[actor.Id] = serial + 1;
            BulkDamage(actor, primary, skill, 5000000, "fixed");
            events.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, InstanceKey(actor.Id),
                primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY HP75 소모 / " +
                (earnGold ? "금 1500" : "나무 1"), 0));
        }

        private void BulkTryGabanManaStrike(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            var skill = AdditionalAutoSkill(actor, "I-08:skill:0");
            if (skill == null || skill.Trigger != "activeMana" ||
                skill.Number("manaCost") != 45 ||
                skill.Components.Count != 1 ||
                skill.Components[0].Type != "fixed" ||
                skill.Components[0].Text("basis") != "targetMaxHealth") return;
            var proportions = skill.Components[0].Get("percentByTargetKind")
                as Dictionary<string, object>;
            if (proportions == null ||
                OrdrJson.Number(proportions, "line") != 50 ||
                OrdrJson.Number(proportions, "boss") != 10 ||
                OrdrJson.Number(proportions, "story") != 5) return;
            double fraction;
            switch (primary.Kind)
            {
                case CharacterEnemyKind.Line: fraction = .5; break;
                case CharacterEnemyKind.Boss: fraction = .1; break;
                case CharacterEnemyKind.Story: fraction = .05; break;
                default: return; // Enraged has no pinned separate entry.
            }
            ProvisionSourceModelAuraRecipientResources(actor);
            var pool = actor.Abilities.Resources;
            if (pool == null || !pool.CanSpend(0, 45)) return;
            // This mobile automatic active spends the source h04F mana once;
            // source JASS cast timing and immunity are not asserted.
            pool.Spend(0, 45);
            BulkDamage(actor, primary, skill,
                primary.Enemy.MaxHealth * fraction, "fixed");
        }

        private void BulkActivateAnyaFamilyPhoto(CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            if (skill.Trigger != "manual" ||
                skill.Number("cooldownSeconds") != 75 ||
                skill.Components.Count != 2 ||
                skill.Components[0].Type != "generateGiftBox" ||
                skill.Components[0].Number("count") != 1 ||
                skill.Components[1].Type != "summonLloydAndYor" ||
                skill.Components[1].Number("durationSeconds") != 3)
                return;
            if (bulkGiftWallet == null) return;
            var point = roster.Get(actor.Id);
            // Original companion attack rawcodes are not provided in the
            // profile. Preserve their 3s lifecycle without inventing hits.
            SpawnSourceTemporaryActor(actor, skill.Name, "로이드 포저",
                3, null, spawnX: point.X, spawnY: point.Y);
            SpawnSourceTemporaryActor(actor, skill.Name, "요르 포저",
                3, null, spawnX: point.X, spawnY: point.Y);
            BulkGenerateAndOpenGift(actor, skill);
        }

        private void OnBulkAutoSuccessfulHit(string actorKey,
            string rawcode, string victimId, long actual)
        {
            long actorId;
            CharacterInstance actor;
            SpatialCombatEnemy victim;
            if (!long.TryParse(actorKey, out actorId) ||
                !characters.TryGetValue(actorId, out actor) ||
                !enemies.TryGetValue(victimId, out victim) || actual <= 0 ||
                !IsBulkSourceOwner(actor.Catalog.Id, rawcode)) return;
            if (actor.Catalog.Id != "I-08") return;
            double flat;
            if (bulkAutoFlatPower.TryGetValue(actor.Id, out flat) && flat > 0)
            {
                var bonusSkill = AdditionalAutoSkill(actor, "I-08:skill:2");
                if (bonusSkill != null)
                    BulkDamage(actor, victim, bonusSkill, flat, "physical");
            }
            var specialHit = AdditionalAutoSkill(actor, "I-08:skill:3");
            if (specialHit != null && specialHit.Trigger == "onAttack" &&
                specialHit.ChancePercent == 55 &&
                specialHit.Components.Count == 1 &&
                specialHit.Components[0].Type == "fixed" &&
                specialHit.Components[0].Text("basis") == "basicAttackDamage" &&
                specialHit.Components[0].Number("percent") == 525)
            {
                bool storyOrBoss = victim.Kind == CharacterEnemyKind.Story ||
                    victim.Kind == CharacterEnemyKind.Boss;
                // The tooltip gives 55% normally and 13% vs story/boss.
                // Use the successful basic hit's settled damage as the
                // mobile basic-damage witness, not a made-up object roll.
                if (sourceModelRules.NextScriptRoll(10000) <
                    (storyOrBoss ? 1300 : 5500))
                    BulkDamage(actor, victim, specialHit, actual * 5.25,
                        "fixed");
            }
            var skill = AdditionalAutoSkill(actor, "I-08:skill:4");
            if (skill == null || skill.Components.Count == 0) return;
            if (skill.Trigger != "onHit" ||
                skill.Components[0].Type != "armorReduction" ||
                skill.Components[0].Number("amount") != 60) return;
            // No original duration exists: treat this as a live single-target
            // attack debuff, NOT a guessed timed five-second status. The
            // previous target loses it when Gaban hits another target or
            // leaves the roster, and it does not stack with other tooltips.
            string source = "practice-tooltip:bulk:" + InstanceKey(actor.Id) +
                ":I-08:skill:4:armor-on-hit";
            string previous;
            if (bulkArmorAuraTargets.TryGetValue(actor.Id, out previous) &&
                previous != victim.Id)
            {
                SpatialCombatEnemy other;
                if (enemies.TryGetValue(previous, out other))
                    other.Enemy.Status.RemoveSource(source);
            }
            if (!victim.Enemy.IsAlive) return;
            double armor = victim.Kind == CharacterEnemyKind.Boss ||
                victim.Kind == CharacterEnemyKind.Enraged ? 70 : 60;
            victim.Enemy.Status.RemoveSource(source);
            victim.Enemy.Status.Apply(new CombatEffect(source,
                CombatEffectType.ArmorReduction,
                CombatEffectOrigin.Aura, armor, 0));
            bulkArmorAuraTargets[actor.Id] = victim.Id;
        }

        private void ExecuteBulkAutoAfterSkill(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill activated)
        {
            if (actor == null || activated == null ||
                actor.Mapping == null ||
                !IsBulkSourceOwner(actor.Catalog.Id,
                    actor.Mapping.AttackSpec.UnitRawCode)) return;
            string id = activated.Id;
            if (actor.Catalog.Id == "MY-08" &&
                (id == "MY-08:skill:1" || id == "MY-08:skill:2"))
            {
                var gift = AdditionalAutoSkill(actor, "MY-08:skill:4");
                if (gift != null && gift.Trigger == "onFamilySkill" &&
                    gift.Components.Count == 1 &&
                    gift.Components[0].Type == "generateGiftBox" &&
                    gift.Components[0].Number("count") == 1 &&
                    AdditionalAutoRoll(gift))
                    BulkGenerateAndOpenGift(actor, gift);
                return;
            }
            if (primary == null || !primary.Enemy.IsAlive) return;
            if (id == "MY-01:skill:1")
            {
                var follow = AdditionalAutoSkill(actor, "MY-01:skill:2");
                if (follow == null) return;
                string key = InstanceKey(actor.Id) + ":" + follow.Id;
                double next;
                if (additionalAutoReadyAt.TryGetValue(key, out next) &&
                    Time < next) return;
                additionalAutoReadyAt[key] = Time +
                    (follow.Number("cooldownSeconds") ?? 15);
                BulkApplyStandard(actor, primary, follow);
            }
            else if (id == "TR-01:skill:1")
            {
                var follow = AdditionalAutoSkill(actor, "TR-01:skill:2");
                if (AdditionalAutoRoll(follow))
                {
                    BulkApplyStandard(actor, primary, follow);
                    BulkApplyLingeringSlow(actor, primary, follow);
                }
            }
            else if (id == "I-02:skill:0" || id == "I-02:skill:1")
            {
                var follow = AdditionalAutoSkill(actor, "I-02:skill:2");
                if (follow == null || follow.Components.Count < 2) return;
                string key = InstanceKey(actor.Id) + ":" + follow.Id;
                double next;
                if (additionalAutoReadyAt.TryGetValue(key, out next) &&
                    Time < next) return;
                additionalAutoReadyAt[key] = Time +
                    (follow.Number("absoluteCooldownSeconds") ?? 8.5);
                var targets = BulkTargets(actor, primary, follow);
                BulkApplyDamagePacket(actor, primary, follow,
                    follow.Components[0], targets);
                BulkTimedDamage(actor, targets, follow,
                    follow.Components[0].Number("damagePerTick") ?? 0,
                    "magical", .15, 2);
                BulkDamage(actor, primary, follow,
                    follow.Components[1].Number("amount") ?? 0, "magical");
                ExecuteBulkAutoAfterSkill(actor, primary, follow);
            }
            else if (id == "I-02:skill:2")
            {
                var follow = AdditionalAutoSkill(actor, "I-02:skill:3");
                if (follow == null || follow.Components.Count == 0) return;
                var point = roster.Get(actor.Id);
                double dx = primary.X - point.X, dy = primary.Y - point.Y;
                if (dx * dx + dy * dy > 150 * 150) return;
                var c = follow.Components[0];
                if (primary.Kind == CharacterEnemyKind.Line)
                    BulkDamage(actor, primary, follow,
                        (c.Number("amountPerHit") ?? 0) +
                        primary.Enemy.MaxHealth *
                        (c.Number("plusTargetMaxHealthPercent") ?? 0) / 100,
                        "explosive");
            }
            else if (id == "MY-05:skill:1")
            {
                var follow = AdditionalAutoSkill(actor, "MY-05:skill:3");
                if (follow == null || follow.Components.Count < 3) return;
                var targets = BulkTargets(actor, primary, follow);
                var fixedDot = follow.Components[1];
                foreach (var target in targets)
                {
                    double raw = target.Kind == CharacterEnemyKind.Boss ||
                        target.Kind == CharacterEnemyKind.Story ?
                        fixedDot.Number("exceptBossStoryAmount") ?? 0 :
                        (fixedDot.Number("amountPerTick") ?? 0) +
                        target.Enemy.MaxHealth *
                        (fixedDot.Number("plusTargetMaxHealthPercentPerTick") ?? 0) / 100;
                    BulkTimedDamage(actor, new[] { target }, follow,
                        raw, "fixed", .16, 4);
                }
                BulkTimedDamage(actor, targets, follow,
                    follow.Components[2].Number("amountPerTick") ?? 0,
                    "magical", .16, 4);
            }
            else if (id == "MY-06:skill:2" || id == "MY-06:skill:3")
            {
                // cfg is explicitly tied to an activated skill. Never fire
                // a 6%-max-health hit on an unrelated ordinary attack.
                var follow = AdditionalAutoSkill(actor, "MY-06:skill:0");
                if (follow != null && primary.Kind == CharacterEnemyKind.Line)
                    BulkApplyStandard(actor, primary, follow);
            }
        }

        private void BulkApplyLingeringSlow(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill)
        {
            var targets = BulkTargets(actor, primary, skill);
            foreach (var c in skill.Components)
                if (c.Type == "lingeringSlow")
                {
                    bool story = IsStoryDeployed(actor.Id);
                    long revision = SourceModelLaneRevision(actor.Id);
                    foreach (var enemy in targets)
                    {
                        var victim = enemy;
                        effectExecutor.SchedulePracticeSourceAttack(
                            InstanceKey(actor.Id), victim.Id, 3, () => {
                                SpatialCombatEnemy current;
                                if (!CanFinishSourceModelDelayedAttack(actor.Id,
                                        story, revision) ||
                                    !enemies.TryGetValue(victim.Id, out current) ||
                                    !ReferenceEquals(current, victim)) return;
                                BulkStatus(actor, victim, skill,
                                    CombatEffectType.MovementSlowPercent,
                                    c.Number("percent") ?? 0,
                                    c.Number("durationSeconds") ?? 2,
                                    "lingering-slow");
                            });
                    }
                }
        }

        private double? NextBulkAutoPeriodicAt(CharacterInstance actor)
        {
            if (!UsesSourceModelBasicAttacks || !actor.AttackReady ||
                actor.Mapping == null || actor.Profile == null ||
                !IsBulkSourceOwner(actor.Catalog.Id,
                    actor.Mapping.AttackSpec.UnitRawCode)) return null;
            string id = actor.Catalog.Id == "L-22" ? "L-22:skill:0" :
                actor.Catalog.Id == "MY-08" ? "MY-08:skill:6" : null;
            if (id == null) return null;
            var skill = AdditionalAutoSkill(actor, id);
            if (skill == null || skill.Components.Count == 0) return null;
            double interval = skill.Number("intervalSeconds") ??
                skill.Number("cooldownSeconds") ?? 0;
            if (interval <= 0 || double.IsInfinity(interval) ||
                double.IsNaN(interval)) return null;
            string key = InstanceKey(actor.Id) + ":" + id;
            double due;
            if (!additionalAutoReadyAt.TryGetValue(key, out due) ||
                due < Time - interval)
                additionalAutoReadyAt[key] = due = Time + interval;
            return due;
        }

        private int ExecuteBulkAutoPeriodic(int budget)
        {
            if (!UsesSourceModelBasicAttacks || budget <= 0) return 0;
            int count = 0;
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (var id in ids)
            {
                if (count >= budget) break;
                var actor = characters[id];
                double? due = NextBulkAutoPeriodicAt(actor);
                if (!due.HasValue || !CombatScheduler.HasReached(Time, due.Value))
                    continue;
                string skillId = actor.Catalog.Id == "MY-08" ?
                    "MY-08:skill:6" : "L-22:skill:0";
                var skill = AdditionalAutoSkill(actor, skillId);
                if (skill == null) continue;
                additionalAutoReadyAt[InstanceKey(actor.Id) + ":" + skill.Id] =
                    due.Value + (skill.Number("intervalSeconds") ??
                        skill.Number("cooldownSeconds") ?? .4);
                count++;
                if (actor.Catalog.Id == "MY-08")
                {
                    BulkActivateAnyaFamilyPhoto(actor, skill);
                    continue;
                }
                var candidates = BuildTargetCandidates(actor);
                string chosen = attackPipeline.SelectDefaultTarget(
                    InstanceKey(actor.Id), candidates, TargetPriority.Nearest);
                SpatialCombatEnemy primary;
                if (chosen == null || !enemies.TryGetValue(chosen, out primary) ||
                    !primary.Enemy.IsAlive) continue;
                // The tooltip omits damage school. Do not invent magic
                // damage and accidentally bypass the original armor type.
                ReportSourceModelNextThirtyDeferred(InstanceKey(actor.Id),
                    primary.Id, "L-22 가스 0.4초 피해계열 미확인");
            }
            return count;
        }
    }
}
