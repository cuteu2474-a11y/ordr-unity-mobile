using System;
using System.Collections.Generic;
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
            ImportedCombatEffect c, IReadOnlyList<SpatialCombatEnemy> targets)
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
                    BulkDamage(actor, victim, skill, raw, c.Type);
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
                int count;
                bulkAutoAttackCounts.TryGetValue(actor.Id, out count);
                count++;
                bulkAutoAttackCounts[actor.Id] = count;
                if (count % 20 == 0)
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
                BulkRunAttackSkill(actor, primary, "I-08:skill:2");
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
            else if (owner == "L-03" &&
                     (primary.Kind == CharacterEnemyKind.Boss ||
                      primary.Kind == CharacterEnemyKind.Enraged))
                BulkRunAttackSkill(actor, primary, "L-03:skill:0");
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
                    }
                }
            }
            else if (owner == "L-18")
                BulkRunAttackSkill(actor, primary, "L-18:skill:1");
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
                BulkRunAttackSkill(actor, primary, "MY-13:skill:0");
        }

        private void BulkRunAttackSkill(CharacterInstance actor,
            SpatialCombatEnemy primary, string id, bool guaranteed = false)
        {
            var skill = AdditionalAutoSkill(actor, id);
            if (skill == null || (!guaranteed && !AdditionalAutoRoll(skill))) return;
            if (skill.Components.Count == 0) return;
            if (id == "L-02:skill:0" || id == "L-03:skill:0")
            {
                // HP abilities require a verified resource pool. The unit
                // catalog does not currently pin either actor's SLK HP.
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
                // 400% is the existing mobile cadence cap, not a claim of
                // the Warcraft native attack-speed formula.
                // The source calls this 'maximum attack speed' without a
                // percent. The native max IAS formula is not pinned, so do
                // not inject an invented 400% buff as an original result.
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
                // Mana-based percentage scaling cannot be guessed without
                // a source-mapped current-mana provider. Base packets only.
                var targets = BulkTargets(actor, primary, skill);
                BulkApplyDamagePacket(actor, primary, skill,
                    skill.Components[0], targets);
                if (id == "MY-06:skill:2" && skill.Components.Count > 1)
                {
                    var single = skill.Components[1];
                    BulkDamage(actor, primary, skill,
                        (single.Number("extraSingleAmount") ?? 0) +
                        primary.Enemy.MaxHealth *
                        (single.Number("plusTargetMaxHealthPercent") ?? 0) / 100,
                        "magical");
                }
                else if (id == "MY-06:skill:3" &&
                    skill.Components.Count > 1)
                {
                    double baseHit = skill.Components[0].Number("amountPerHit") ?? 0;
                    double bonus = skill.Components[1].Number(
                        "extraSingleTargetDamagePercent") ?? 0;
                    BulkDamage(actor, primary, skill,
                        baseHit * 2 * bonus / 100, "magical");
                }
            }
            else BulkApplyStandard(actor, primary, skill);
            ExecuteBulkAutoAfterSkill(actor, primary, skill);
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
            }
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
            var skill = AdditionalAutoSkill(actor, "I-08:skill:4");
            if (skill == null || skill.Components.Count == 0) return;
            // Source data lacks a duration and exclusivity contract; do not
            // install an invented lingering debuff. The hit event is wired,
            // and the missing original status inputs remain visible in docs.
        }

        private void ExecuteBulkAutoAfterSkill(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill activated)
        {
            if (actor == null || activated == null ||
                !primary.Enemy.IsAlive || actor.Mapping == null ||
                !IsBulkSourceOwner(actor.Catalog.Id,
                    actor.Mapping.AttackSpec.UnitRawCode)) return;
            string id = activated.Id;
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
            string id = actor.Catalog.Id == "L-22" ? "L-22:skill:0" : null;
            if (id == null) return null;
            var skill = AdditionalAutoSkill(actor, id);
            if (skill == null || skill.Components.Count == 0) return null;
            double interval = skill.Number("intervalSeconds") ?? 0;
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
                var skill = AdditionalAutoSkill(actor, "L-22:skill:0");
                if (skill == null) continue;
                additionalAutoReadyAt[InstanceKey(actor.Id) + ":" + skill.Id] =
                    due.Value + .4;
                count++;
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
