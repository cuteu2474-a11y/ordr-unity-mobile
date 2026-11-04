using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Native B4R: ck starts at the Koby fusion's current X3 wave and
    // increments A1CU once only after X3 - savedWave > 4 (cap level six).
    public static class SourceModelKobyRoundRules
    {
        public const int OriginalRoundsPerLevel = 5;
        public const int MaximumLevels = 6;
        public static bool CanAdvance(int currentWave, int savedWave, int level) =>
            currentWave >= 1 && savedWave >= 0 &&
            currentWave - savedWave >= OriginalRoundsPerLevel &&
            level >= 0 && level < MaximumLevels;
    }

    public sealed partial class CharacterCombatRuntime
    {
        // This is a tooltip-only battlefield model, not a claim about native
        // JASS attack order, immunity, criticals or imported hero attributes.
        private readonly HashSet<long> sourceModelYamatoOpeningReady =
            new HashSet<long>();
        private readonly Dictionary<long, int> sourceModelMariaAttackCount =
            new Dictionary<long, int>();
        private readonly Dictionary<long, int> sourceModelKobyRoundStacks =
            new Dictionary<long, int>();
        private readonly Dictionary<long, int> sourceModelKobyLastUpgradeWave =
            new Dictionary<long, int>();

        // Only a completed CWR TR29/H0CO fusion can anchor the native ck
        // watcher. A Koby already present in a restored roster is initialized
        // conservatively on its first observed wave, not backdated to wave 0.
        public bool RecordSourceModelKobyFusionRound(long actorId,
            string catalogId, string rawcode)
        {
            CharacterInstance actor;
            if (!UsesSourceModelBasicAttacks || catalogId != "TR-24" ||
                rawcode != "H0CO" || !characters.TryGetValue(actorId, out actor) ||
                actor.Catalog.Id != catalogId || actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != rawcode ||
                sourceModelKobyLastUpgradeWave.ContainsKey(actorId)) return false;
            sourceModelKobyLastUpgradeWave.Add(actorId, Math.Max(0, game.CurrentWave));
            return true;
        }
        private readonly HashSet<string> sourceModelEighthWithinApproach =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> sourceModelEighthApproachRolled =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> sourceModelEighthHakiClaims =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> sourceModelEighthPeriodicDue =
            new Dictionary<string, double>(StringComparer.Ordinal);

        private static bool IsSourceModelEighthStatusApproach(ImportedCombatSkill skill)
        {
            return skill != null && (skill.Id == "MY-08:skill:0" ||
                skill.Id == "TR-13:skill:1" || skill.Id == "LI-07:skill:2" ||
                skill.Id == "L-17:skill:1");
        }

        private static bool SourceModelEighthApproachValid(ImportedCombatSkill skill)
        {
            if (!IsSourceModelEighthStatusApproach(skill) ||
                skill.Trigger != "onEnemyEnterRange" || skill.Components.Count != 2)
                return false;
            var first = skill.Components[0];
            var second = skill.Components[1];
            if (first.Type != "magical") return false;
            switch (skill.Id)
            {
                case "MY-08:skill:0":
                    return skill.Name == "독심술" && skill.Target == "single" &&
                        skill.Radius == 800 &&
                        skill.Number("limitPerEnemy") == 1 &&
                        first.Number("amount") == 100000 &&
                        second.Type == "magicResistanceReduction" &&
                        second.Number("percent") == 10;
                case "L-17:skill:1":
                    return skill.Name == "패왕색 패기 접근 발동" &&
                        skill.Target == "enemiesEnteringRange" &&
                        skill.Number("triggerRadius") == 1050 &&
                        skill.Number("limitPerEnemy") == 1 &&
                        first.Text("basis") == "targetMaxHealth" &&
                        first.Number("percent") == 3 &&
                        second.Type == "stun" &&
                        second.Number("durationSeconds") == .64;
                case "TR-13:skill:1":
                    return skill.Name == "패기 접근 효과" && skill.Radius == 750 &&
                        Equals(skill.Get("oncePerEnemy"), true) &&
                        first.Number("plusTargetMaxHealthPercent") == 3 &&
                        second.Type == "stun" &&
                        second.Number("durationSeconds") == .625 &&
                        SourceModelEighthShanksChainedValid(skill);
                case "LI-07:skill:2":
                    return skill.Name == "대상 접근 시 낙뢰" && skill.Radius == 425 &&
                        skill.Target == "area" &&
                        skill.Number("limitPerEnemy") == 1 &&
                        skill.ChancePercent == 14.25 &&
                        first.Number("amount") == 850000 &&
                        second.Type == "magical" &&
                        second.Number("amount") == 8500000 &&
                        second.Text("scope") == "singlePrimaryTarget";
                default: return false;
            }
        }

        private static bool SourceModelEighthShanksChainedValid(ImportedCombatSkill skill)
        {
            var chained = skill.Get("chained") as List<object>;
            var overrides = skill.Get("overrides") as List<object>;
            if (chained == null || chained.Count != 2 ||
                overrides == null || overrides.Count != 1) return false;
            var first = chained[0] as Dictionary<string, object>;
            var second = chained[1] as Dictionary<string, object>;
            var over = overrides[0] as Dictionary<string, object>;
            return first != null && first.Count == 3 &&
                Convert.ToDouble(first["chancePercent"]) == 16 &&
                Convert.ToDouble(first["magicalAmount"]) == 1000000 &&
                Convert.ToDouble(first["extraMaxHealthPercent"]) == 3 &&
                second != null && second.Count == 5 &&
                Convert.ToDouble(second["chancePercent"]) == 7.25 &&
                Convert.ToDouble(second["magicalAmount"]) == 1000000 &&
                Convert.ToDouble(second["extraMaxHealthPercent"]) == 6 &&
                Convert.ToDouble(second["additionalAreaMagicalAmount"]) == 250000 &&
                Convert.ToDouble(second["stunSeconds"]) == 3 &&
                over != null && Equals(over["bypassOnce"], true);
        }

        private void AdvanceSourceModelEighthTimeline(double elapsed)
        {
            if (!UsesSourceModelBasicAttacks || elapsed <= 0 ||
                game.Phase != Ordr.Mobile.Core.SessionPhase.Running) return;
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            foreach (long id in ids)
            {
                CharacterInstance actor;
                if (!characters.TryGetValue(id, out actor) ||
                    actor.Profile == null || actor.Mapping == null ||
                    !actor.AttackReady) continue;
                foreach (var skill in actor.Profile.Skills)
                {
                    if (SourceModelEighthApproachValid(skill))
                        ExecuteSourceModelEighthApproach(actor, skill);
                    if (SourceModelTwentyNinthSkillCatalog.CanModelKatakuriApproach(skill))
                        ExecuteSourceModelThirtiethKatakuriApproach(actor, skill);
                    if (SourceModelEighthPeriodicValid(skill))
                        ExecuteSourceModelEighthPeriodic(actor, skill);
                    if (SourceModelEighthKamusariValid(skill))
                        ExecuteSourceModelEighthKamusariPeriodic(actor, skill);
                }
            }
        }

        private void ExecuteSourceModelEighthApproach(CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            double radius = skill.Number("triggerRadius") ?? skill.Radius.Value;
            var origin = roster.Get(actor.Id);
            var enemyIds = new List<string>(enemies.Keys);
            enemyIds.Sort(StringComparer.Ordinal);
            foreach (string targetId in enemyIds)
            {
                SpatialCombatEnemy enemy;
                if (!enemies.TryGetValue(targetId, out enemy) ||
                    !enemy.Enemy.IsAlive || !CanAttackInThisLane(actor, enemy))
                    continue;
                double dx = origin.X - enemy.X, dy = origin.Y - enemy.Y;
                string key = actor.Id + ":" + skill.Id + ":" + targetId;
                if (dx * dx + dy * dy > radius * radius)
                {
                    sourceModelEighthWithinApproach.Remove(key);
                    continue;
                }
                if (!sourceModelEighthWithinApproach.Add(key)) continue;
                bool boss = enemy.Kind == CharacterEnemyKind.Boss ||
                            enemy.Kind == CharacterEnemyKind.Enraged;
                bool bypassOnce = skill.Id == "TR-13:skill:1" && boss;
                if (!bypassOnce && !sourceModelEighthApproachRolled.Add(key)) continue;
                // Named mutually exclusive haki effects may not overwrite one
                // another on the same target. Lightning is not in this group.
                bool haki = skill.Id != "LI-07:skill:2";
                string claimant = actor.Id + ":" + skill.Id;
                string claimedBy;
                if (haki && sourceModelEighthHakiClaims.TryGetValue(targetId,
                        out claimedBy) &&
                    !(bypassOnce && claimedBy == claimant)) continue;
                double chance = skill.ChancePercent ?? 100;
                if (skill.Id == "LI-07:skill:2" &&
                    (boss || enemy.Kind == CharacterEnemyKind.Story)) chance = 100;
                if (chance < 100 && sourceModelRules.NextScriptRoll(1000000) >=
                    (int)Math.Round(chance * 10000)) continue;
                if (haki) sourceModelEighthHakiClaims[targetId] = claimant;
                var targets = skill.Id == "LI-07:skill:2" ?
                    SourceScriptTargets(actor, enemy, new SourceScriptHit(
                        SourceScriptHitShape.Circle, skill.Radius.Value,
                        0, 0, 1, 1, Ordr323AttackType.Spells)) :
                    SourceScriptTargets(actor, enemy, new SourceScriptHit(
                        SourceScriptHitShape.Direct, 0,
                        0, 0, 1, 1, Ordr323AttackType.Spells));
                if (targets.Count == 0) continue;
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                    InstanceKey(actor.Id), enemy.Id, skill.Name,
                    "TOOLTIP_MODEL_ONLY / 접근 반응 · 원작 판정 미검증", 0));
                ApplySourceModelNextThirtySkill(actor, enemy, skill, targets);
                if (skill.Id == "TR-13:skill:1")
                    ApplySourceModelEighthShanksChain(actor, enemy, skill);
            }
        }

        private void ApplySourceModelEighthShanksChain(CharacterInstance actor,
            SpatialCombatEnemy enemy, ImportedCombatSkill skill)
        {
            var chained = (List<object>)skill.Get("chained");
            foreach (var item in chained)
            {
                var row = (Dictionary<string, object>)item;
                double chance = Convert.ToDouble(row["chancePercent"]);
                if (sourceModelRules.NextScriptRoll(1000000) >=
                    (int)Math.Round(chance * 10000)) continue;
                double raw = Convert.ToDouble(row["magicalAmount"]) +
                    enemy.Enemy.MaxHealth *
                    Convert.ToDouble(row["extraMaxHealthPercent"]) / 100;
                ApplySourceModelSeventhTimedHit(actor, enemy, skill.Name, raw, "magical");
                if (!row.ContainsKey("additionalAreaMagicalAmount")) continue;
                var affected = SourceScriptTargets(actor, enemy, new SourceScriptHit(
                    SourceScriptHitShape.Circle, skill.Radius.Value,
                    0, 0, 1, 1, Ordr323AttackType.Spells));
                foreach (var victim in affected)
                {
                    ApplySourceModelSeventhTimedHit(actor, victim, skill.Name,
                        Convert.ToDouble(row["additionalAreaMagicalAmount"]), "magical");
                    if (victim.Kind != CharacterEnemyKind.Line || !victim.Enemy.IsAlive)
                        continue;
                    string source = "practice-tooltip-stun:" + actor.Id + ":" +
                        skill.Id + ":chain";
                    victim.Enemy.Status.RemoveSource(source);
                    victim.Enemy.Status.Apply(new CombatEffect(source,
                        CombatEffectType.Stun, CombatEffectOrigin.TriggeredSkill,
                        1, Convert.ToDouble(row["stunSeconds"])));
                }
            }
        }

        private static bool SourceModelEighthPeriodicValid(ImportedCombatSkill skill)
        {
            if (skill.Id != "E-11:skill:2" || skill.Name != "곤 인페르노" ||
                !SourceModelTwentiethSkillCatalog.CanModelExact(skill) ||
                skill.Trigger != "periodic" || skill.PeriodSeconds != 1.5 ||
                skill.Radius != 325 || skill.Target != "area" ||
                skill.Components.Count != 3 || skill.ChancePercent.HasValue)
                return false;
            var magic = skill.Components[0];
            var line = skill.Components[1];
            var special = skill.Components[2];
            var range = magic.Get("multiplierRange") as List<object>;
            return magic.Type == "magical" && magic.Number("amount") == 200000 &&
                range != null && range.Count == 2 &&
                Convert.ToDouble(range[0]) == 1 &&
                Convert.ToDouble(range[1]) == 1.5 &&
                line.Type == "explosive" && line.Text("basis") == "targetMaxHealth" &&
                line.Number("percent") == 15 &&
                line.Text("scope") == "singlePrimaryTarget" &&
                special.Type == "fixed" && special.Number("amount") == 400000 &&
                special.Text("scope") == "singlePrimaryTarget";
        }

        private void ExecuteSourceModelEighthPeriodic(CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            string key = actor.Id + ":" + skill.Id;
            double due;
            if (!sourceModelEighthPeriodicDue.TryGetValue(key, out due))
            {
                sourceModelEighthPeriodicDue[key] = Time + skill.PeriodSeconds.Value;
                return;
            }
            if (due > Time + .000001) return;
            double next = due + skill.PeriodSeconds.Value;
            while (next <= Time + .000001) next += skill.PeriodSeconds.Value;
            sourceModelEighthPeriodicDue[key] = next;
            var candidates = BuildTargetCandidates(actor);
            if (candidates.Count == 0) return;
            string chosen = ChoosePreviewTarget(actor, candidates);
            SpatialCombatEnemy primary;
            if (chosen == null || !enemies.TryGetValue(chosen, out primary) ||
                !primary.Enemy.IsAlive || !CanAttackInThisLane(actor, primary)) return;
            var targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.Circle, skill.Radius.Value,
                0, 0, 1, 1, Ordr323AttackType.Spells));
            if (targets.Count == 0) return;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / 1.5초 주기 기술", 0));
            ApplySourceModelTwentiethSkill(actor, primary, skill, targets);
            SourceModelTwentiethAfterPeriodic(actor, primary, skill);
        }

        private static bool SourceModelEighthKamusariValid(ImportedCombatSkill skill)
        {
            return skill != null && skill.Id == "TR-13:skill:0" &&
                skill.Name == "카무사리" &&
                skill.Trigger == "activeManaOrPeriodic" &&
                skill.PeriodSeconds == 14.25 && skill.Number("manaCost") == 35 &&
                skill.Target == "area" && skill.Radius == 1100 &&
                skill.Components.Count == 2 &&
                skill.Components[0].Type == "magical" &&
                skill.Components[0].Number("amount") == 1500000 &&
                skill.Components[1].Type == "stun" &&
                skill.Components[1].Number("durationSeconds") == 3;
        }

        private void ExecuteSourceModelEighthKamusariPeriodic(CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            string key = actor.Id + ":" + skill.Id;
            double due;
            if (!sourceModelEighthPeriodicDue.TryGetValue(key, out due))
            {
                sourceModelEighthPeriodicDue[key] = Time + skill.PeriodSeconds.Value;
                return;
            }
            if (due > Time + .000001) return;
            var candidates = BuildTargetCandidates(actor);
            string chosen = candidates.Count > 0 ? ChoosePreviewTarget(actor, candidates) : null;
            SpatialCombatEnemy primary;
            if (chosen != null && enemies.TryGetValue(chosen, out primary) &&
                primary.Enemy.IsAlive && CanAttackInThisLane(actor, primary))
                ApplySourceModelEighthKamusari(actor, primary, skill, "주기 발동");
            sourceModelEighthPeriodicDue[key] = Time + skill.PeriodSeconds.Value;
        }

        private bool TryCastSourceModelEighthKamusari(CharacterInstance actor,
            string skillId, string enemyId, out AbilityCastResult result)
        {
            result = AbilityCastResult.UnsupportedEffect;
            if (!UsesSourceModelBasicAttacks || actor.Catalog.Id != "TR-13" ||
                actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != "H08Z" ||
                actor.Profile == null || skillId != "TR-13:skill:0")
                return false;
            ImportedCombatSkill skill = null;
            foreach (var candidate in actor.Profile.Skills)
                if (candidate.Id == skillId && SourceModelEighthKamusariValid(candidate))
                    skill = candidate;
            if (skill == null) return true;
            SpatialCombatEnemy primary;
            if (enemyId == null || !enemies.TryGetValue(enemyId, out primary) ||
                !primary.Enemy.IsAlive || !CanAttackInThisLane(actor, primary))
            {
                result = AbilityCastResult.MissingTarget;
                return true;
            }
            if (actor.Abilities.Resources == null)
            {
                result = AbilityCastResult.NoResourceState;
                return true;
            }
            if (!actor.Abilities.Resources.CanSpend(0, skill.Number("manaCost").Value))
            {
                result = AbilityCastResult.InsufficientResources;
                return true;
            }
            string key = actor.Id + ":" + skill.Id;
            double due;
            if (sourceModelEighthPeriodicDue.TryGetValue(key, out due) &&
                due > Time + .000001)
            {
                result = AbilityCastResult.Cooldown;
                return true;
            }
            actor.Abilities.Resources.Spend(0, skill.Number("manaCost").Value);
            sourceModelEighthPeriodicDue[key] = Time + skill.PeriodSeconds.Value;
            ApplySourceModelEighthKamusari(actor, primary, skill, "마나 시전");
            result = AbilityCastResult.Cast;
            return true;
        }

        private void ApplySourceModelEighthKamusari(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill, string cause)
        {
            var targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.Circle, skill.Radius.Value,
                0, 0, 1, 1, Ordr323AttackType.Spells));
            if (targets.Count == 0) return;
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.SkillActivated,
                InstanceKey(actor.Id), primary.Id, skill.Name,
                "TOOLTIP_MODEL_ONLY / " + cause + " · 14.25초 공유 주기", 0));
            ApplySourceModelNextThirtySkill(actor, primary, skill, targets);
        }

        private bool SourceModelEighthAttackCondition(CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            if (!SourceModelNextThirtySkillCatalog.IsEighthCompositeSkill(skill))
                return true;
            if (skill.Id == "TR-18:skill:5")
                return PracticeFormOf(actor.Id) == "beast" &&
                    sourceModelYamatoOpeningReady.Contains(actor.Id);
            if (skill.Id == "TR-18:skill:4")
            {
                if (PracticeFormOf(actor.Id) == "beast") return true;
                CombatStatusEngine effects;
                return unitStatuses.TryGetValue(InstanceKey(actor.Id), out effects) &&
                    SourceModelUnitEffects.ResolvePracticeAttackSpeed(
                        effects.Snapshot()) > 0;
            }
            if (skill.Id == "D-01:skill:1")
            {
                int previous;
                sourceModelMariaAttackCount.TryGetValue(actor.Id, out previous);
                int current = previous == int.MaxValue ? 1 : previous + 1;
                sourceModelMariaAttackCount[actor.Id] = current;
                return current % (int)skill.Number("everyAttacks").Value == 0;
            }
            return true;
        }

        private double SourceModelEighthProcChance(ImportedCombatSkill skill,
            SpatialCombatEnemy primary, CharacterInstance actor)
        {
            double chance = SourceModelSixthProcChance(skill, primary, actor);
            if (skill.Id != "TR-18:skill:2" ||
                !SourceModelNextThirtySkillCatalog.IsEighthCompositeSkill(skill))
                return chance;
            if (PracticeFormOf(actor.Id) == "beast") return 100;
            return SourceModelEighthTargetSlow(primary) >=
                skill.Components[0].Number("maxBonusByTargetSlowPercent").Value ?
                33 : chance;
        }

        private static double SourceModelEighthTargetSlow(SpatialCombatEnemy target)
        {
            double slow = 0;
            foreach (var effect in target.Enemy.Status.Snapshot())
                if (effect.Type == CombatEffectType.MovementSlowPercent)
                    slow = Math.Max(slow, effect.Amount);
            return slow;
        }

        private void SourceModelEighthCommitAttackCondition(CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            if (skill.Id == "TR-18:skill:5")
                sourceModelYamatoOpeningReady.Remove(actor.Id);
        }

        private static bool SourceModelEighthSkipDamageComponent(ImportedCombatSkill skill,
            ImportedCombatEffect component)
        {
            return SourceModelNextThirtySkillCatalog.IsEighthCompositeSkill(skill) &&
                (component.Type == "strengthScaling" ||
                 component.Type == "singleTargetPhysicalBonus");
        }

        private double SourceModelEighthRawDamageAdjustment(CharacterInstance actor,
            SpatialCombatEnemy primary, ImportedCombatSkill skill,
            ImportedCombatEffect component, SpatialCombatEnemy target, double raw)
        {
            if (skill.Id == "TR-20:skill:1" && component.Type == "physical")
            {
                var scale = skill.Components[3];
                double strength = SourceModelEighthHeroStrength(actor.Id);
                raw += strength * scale.Number("aoePerStrength").Value;
                if (ReferenceEquals(primary, target))
                    raw = raw * (1 + skill.Components[1].Number("percent").Value / 100) +
                        strength * scale.Number("singlePerStrength").Value;
            }
            // Extreme Tiger's 2%-per-strength bonus applies to the named
            // Santoryu skills only, including the previously wired Crow.
            if ((skill.Id == "TR-20:skill:3" ||
                 skill.Id == "TR-20:skill:2") && component.Type == "physical")
            {
                ImportedCombatSkill tiger = null;
                foreach (var candidate in actor.Profile.Skills)
                    if (candidate.Id == "TR-20:skill:3" &&
                        SourceModelNextThirtySkillCatalog.IsEighthCompositeSkill(candidate))
                        tiger = candidate;
                if (tiger != null && tiger.Components.Count == 3)
                    raw *= 1 + SourceModelEighthHeroStrength(actor.Id) *
                        tiger.Components[2].Number("percentPerStrength").Value / 100;
            }
            if (skill.Id == "TR-18:skill:4" && component.Type == "magical" &&
                ReferenceEquals(primary, target))
                raw *= 1 + component.Number("extraSingleTargetPercent").Value / 100;
            if (skill.Id == "TR-18:skill:2" && component.Type == "fixed")
                raw *= 1 + Math.Min(SourceModelEighthTargetSlow(target),
                    component.Number("maxBonusByTargetSlowPercent").Value) / 100;
            if (skill.Id == "H-20:skill:1" &&
                ReferenceEquals(primary, target))
            {
                var range = (List<object>)component.Get("extraSingleMultiplierRange");
                raw *= Ordr323MapDamageMath.ScriptDamageBeforeDefense(1,
                    Convert.ToDouble(range[0]), Convert.ToDouble(range[1]),
                    sourceModelRules.NextScriptRoll(1000000) / 1000000.0);
            }
            if (skill.Id == "H-14:skill:1" &&
                target.Kind == CharacterEnemyKind.Line)
                raw += target.Enemy.MaxHealth *
                    component.Number("extraLineMaxHealthPercent").Value / 100;
            if (skill.Id == "D-01:skill:1" && component.Type == "fixed")
                raw += component.Number("amount").Value;
            if (skill.Id == "LI-07:skill:2" &&
                component.Text("scope") == "singlePrimaryTarget" &&
                ReferenceEquals(primary, target) &&
                (target.Kind == CharacterEnemyKind.Boss ||
                 target.Kind == CharacterEnemyKind.Enraged ||
                 target.Kind == CharacterEnemyKind.Story))
                raw = raw * 2 + target.Enemy.MaxHealth * .10;
            return raw;
        }

        private double SourceModelEighthHeroStrength(long actorId)
        {
            if (sourceModelStrengthProvider == null) return 0;
            double? value = sourceModelStrengthProvider(actorId);
            return value.HasValue && value.Value >= 0 &&
                !double.IsInfinity(value.Value) && !double.IsNaN(value.Value)
                ? value.Value : 0;
        }

        private void ForgetSourceModelEighthActor(long actorId)
        {
            string prefix = actorId + ":";
            sourceModelEighthPeriodicDue.Remove(prefix + "E-11:skill:2");
            sourceModelEighthPeriodicDue.Remove(prefix + "TR-13:skill:0");
            sourceModelEighthWithinApproach.RemoveWhere(
                value => value.StartsWith(prefix, StringComparison.Ordinal));
            sourceModelEighthApproachRolled.RemoveWhere(
                value => value.StartsWith(prefix, StringComparison.Ordinal));
        }

        private void ForgetSourceModelEighthEnemy(string enemyId)
        {
            sourceModelEighthHakiClaims.Remove(enemyId);
            string suffix = ":" + enemyId;
            sourceModelEighthWithinApproach.RemoveWhere(
                value => value.EndsWith(suffix, StringComparison.Ordinal));
            sourceModelEighthApproachRolled.RemoveWhere(
                value => value.EndsWith(suffix, StringComparison.Ordinal));
        }

        // B4R watches the global X3 wave once a second, but increments only
        // when the fusion-specific saved round is five waves behind. Use the
        // wave director's same crossing (no timer polling), including fusion
        // at wave 4 -> first gain at wave 9, NOT the global wave 5.
        internal void OnSourceModelPracticeRoundStarted(int wave)
        {
            if (!UsesSourceModelBasicAttacks || wave <= 0)
                return;
            foreach (var actor in characters.Values)
            {
                if (!actor.AttackReady || actor.Catalog.Id != "TR-24" ||
                    actor.Mapping == null ||
                    actor.Mapping.AttackSpec.UnitRawCode != "H0CO" ||
                    actor.Profile == null) continue;
                ImportedCombatSkill skill = null;
                foreach (var candidate in actor.Profile.Skills)
                    if (candidate.Id == "TR-24:skill:4" &&
                        candidate.Name == "군함 백" &&
                        candidate.Trigger == "everyNthRound" &&
                        candidate.Target == null && candidate.ChancePercent == null &&
                        candidate.Components.Count == 1 &&
                        candidate.Components[0].Type == "selfAttackSpeedBuff" &&
                        candidate.Components[0].Fields.Count == 3 &&
                        candidate.Components[0].Number("percentPerLevel") == 7 &&
                        candidate.Components[0].Number("maxLevels") == 6)
                        skill = candidate;
                if (skill == null) continue;
                int savedWave;
                if (!sourceModelKobyLastUpgradeWave.TryGetValue(actor.Id,
                        out savedWave))
                {
                    sourceModelKobyLastUpgradeWave.Add(actor.Id, wave);
                    continue;
                }
                int previous;
                sourceModelKobyRoundStacks.TryGetValue(actor.Id, out previous);
                if (!SourceModelKobyRoundRules.CanAdvance(wave,
                        savedWave, previous)) continue;
                int next = Math.Min((int)skill.Components[0].Number("maxLevels").Value,
                    previous + 1);
                sourceModelKobyRoundStacks[actor.Id] = next;
                sourceModelKobyLastUpgradeWave[actor.Id] = wave;
                string actorKey = InstanceKey(actor.Id);
                string source = "practice-tooltip:" + actorKey + ":TR-24:skill:4";
                var status = unitStatuses[actorKey];
                status.RemoveSource(source);
                status.Apply(new CombatEffect(source, CombatEffectType.AttackSpeedBuff,
                    CombatEffectOrigin.Aura,
                    next * skill.Components[0].Number("percentPerLevel").Value, 0));
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                    actorKey, actorKey, skill.Name,
                    "MODEL_ONLY B4J/B4R/A1CU / 조합 후 " +
                    (wave - savedWave) + "라운드 · 공격속도 " +
                    (next * 7) + "% (" + next + "/6)", 0));
            }
        }
    }
}
