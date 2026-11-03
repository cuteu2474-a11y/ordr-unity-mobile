using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public interface IOriginalBulletArmadaLevelWitness
    {
        // 0 = unlearned, 1..4 = observed original A0WQ level.
        bool TryObserveArmadaLevel(long ownerId, out int level, out string evidence);
    }

    public sealed partial class CharacterCombatRuntime
    {
        private OriginalEquippedItemLedger sourceModelEquippedItems;
        private IOriginalBulletArmadaLevelWitness sourceModelBulletArmadaLevel;
        private readonly Dictionary<long, double> sourceModelBulletRokushikiUntil =
            new Dictionary<long, double>();
        private readonly Dictionary<long, long> sourceModelBulletRokushikiLane =
            new Dictionary<long, long>();

        public void ConfigureSourceModelEquippedItems(IOriginalEquippedItemWitness witness)
        {
            ConfigureSourceModelEquippedItems(witness, null);
        }

        public void ConfigureSourceModelEquippedItems(IOriginalEquippedItemWitness receipts,
            IOriginalEquippedInventorySnapshotWitness inventory)
        {
            if (!UsesSourceModelBasicAttacks ||
                (receipts == null && inventory == null) ||
                sourceModelEquippedItems != null || game.CurrentWave != 0 || processingStep)
                throw new InvalidOperationException("원본 장비 증거 공급자는 전투 시작 전에 한 번만 연결합니다.");
            sourceModelEquippedItems = new OriginalEquippedItemLedger(receipts, inventory);
        }

        public void ConfigureSourceModelBulletArmadaLevel(
            IOriginalBulletArmadaLevelWitness witness)
        {
            if (!UsesSourceModelBasicAttacks || witness == null ||
                sourceModelBulletArmadaLevel != null || game.CurrentWave != 0 || processingStep)
                throw new InvalidOperationException("원본 A0WQ 레벨 증거는 전투 시작 전에 한 번만 연결합니다.");
            sourceModelBulletArmadaLevel = witness;
        }

        // Called ONLY after a real original-world equipment change succeeded.
        // The witness authenticates both the rawcode and signed item transfer.
        public bool TryRecordSourceModelEquippedItem(string transactionId, long ownerId,
            string originalRawcode, int signedCount)
        {
            CharacterInstance actor;
            if (!UsesSourceModelBasicAttacks || processingStep ||
                activeOriginalFusionSwap != null || sourceModelEquippedItems == null ||
                !characters.TryGetValue(ownerId, out actor)) return false;
            return sourceModelEquippedItems.TryRecord(transactionId, ownerId,
                originalRawcode, signedCount);
        }

        // Alternative native integration for already-equipped items at game
        // load and later full-state native revisions. A given owner cannot
        // use both a full snapshot and delta receipts without a shared cursor.
        public bool TrySynchronizeSourceModelEquippedInventory(long ownerId)
        {
            CharacterInstance actor;
            if (!UsesSourceModelBasicAttacks || processingStep ||
                activeOriginalFusionSwap != null || sourceModelEquippedItems == null ||
                !characters.TryGetValue(ownerId, out actor)) return false;
            return sourceModelEquippedItems.TrySynchronizeFromNative(ownerId);
        }

        public int SynchronizeExistingSourceModelEquippedInventories()
        {
            if (!UsesSourceModelBasicAttacks || processingStep ||
                activeOriginalFusionSwap != null || sourceModelEquippedItems == null)
                return 0;
            var ids = new List<long>(characters.Keys);
            ids.Sort();
            int updated = 0;
            foreach (long id in ids)
                if (sourceModelEquippedItems.TrySynchronizeFromNative(id)) updated++;
            return updated;
        }

        public OriginalEquippedItemSnapshot SourceModelEquippedItemsOf(long ownerId)
        {
            CharacterInstance actor;
            if (!UsesSourceModelBasicAttacks ||
                !characters.TryGetValue(ownerId, out actor) ||
                sourceModelEquippedItems == null)
                return new OriginalEquippedItemSnapshot();
            return sourceModelEquippedItems.Snapshot(ownerId);
        }

        // Eligibility is only a tooltip threshold, not a claim that A0WQ
        // actually changed rank. The original ability level is witnessed below.
        public int SourceModelBulletArmadaThresholdOf(long ownerId)
        {
            CharacterInstance actor;
            if (!characters.TryGetValue(ownerId, out actor) ||
                actor.Catalog.Id != "I-05" || actor.Profile == null) return 0;
            ImportedCombatSkill upgrade = null;
            foreach (var candidate in actor.Profile.Skills)
                if (candidate.Id == "I-05:skill:4") { upgrade = candidate; break; }
            if (!SourceModelTwentiethSkillCatalog.CanModelExact(upgrade)) return 0;
            var items = SourceModelEquippedItemsOf(ownerId);
            int unique = checked(items.UniqueAttack + items.UniqueSpeed + items.UniqueArmor);
            int legendary = checked(items.LegendaryAttack + items.LegendarySpeed +
                items.LegendaryArmor);
            return legendary >= upgrade.Components[0].Number("legendaryThreshold").Value ? 2 :
                unique >= upgrade.Components[0].Number("uniqueThreshold").Value ? 1 : 0;
        }

        private void RefreshSourceModelBulletArmada()
        {
            if (!UsesSourceModelBasicAttacks) return;
            foreach (var actor in characters.Values)
            {
                if (actor.Catalog.Id != "I-05" || actor.Profile == null) continue;
                string key = "practice-tooltip:armada:A0WQ:" + actor.Id;
                var status = unitStatuses[InstanceKey(actor.Id)];
                status.RemoveSource(key);
                if (sourceModelBulletArmadaLevel == null) continue;
                ImportedCombatSkill skill = null;
                foreach (var candidate in actor.Profile.Skills)
                    if (candidate.Id == "I-05:skill:4") { skill = candidate; break; }
                if (!SourceModelTwentiethSkillCatalog.CanModelExact(skill)) continue;
                int level;
                string evidence;
                if (!sourceModelBulletArmadaLevel.TryObserveArmadaLevel(actor.Id,
                        out level, out evidence) || level < 1 || level > 4 ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(evidence)) continue;
                // Original W3A A0WQ levels: 5%, 20%, 40%, 50% attack power.
                int power = level == 1 ? 5 : level == 2 ? 20 : level == 3 ? 40 : 50;
                status.Apply(new CombatEffect(key, CombatEffectType.AttackPowerBuff,
                    CombatEffectOrigin.TriggeredSkill, power, 0));
            }
        }

        private void RefreshSourceModelBulletArmorAuras()
        {
            if (!UsesSourceModelBasicAttacks) return;
            foreach (var actor in characters.Values)
            {
                if (actor.Catalog.Id != "I-05" || actor.Profile == null ||
                    SourceModelEquippedItemsOf(actor.Id).UniqueArmor <= 0) continue;
                ImportedCombatSkill skill = null;
                foreach (var candidate in actor.Profile.Skills)
                    if (candidate.Id == "I-05:skill:3") { skill = candidate; break; }
                if (!SourceModelTwentiethSkillCatalog.CanModelExact(skill)) continue;
                string key = "practice-tooltip:bullet-armor-slow:" + actor.Id;
                foreach (var enemy in enemies.Values)
                {
                    if (!enemy.Enemy.IsAlive || sourceModelUnitEffects == null ||
                        !sourceModelUnitEffects.IsPracticeTarget(enemy.Id) ||
                        !CanAttackInThisLane(actor, enemy)) continue;
                    modelAuraKeys.Add(key);
                    statuses[enemy.Id].Apply(new CombatEffect(key,
                        CombatEffectType.MovementSlowPercent, CombatEffectOrigin.Aura,
                        20, 0));
                }
            }
        }

        private void ActivateSourceModelBulletRokushiki(CharacterInstance actor,
            ImportedCombatSkill skill)
        {
            if (!SourceModelTwentiethSkillCatalog.CanModelExact(skill)) return;
            double duration = skill.Components[0].Number("durationSeconds").Value;
            sourceModelBulletRokushikiUntil[actor.Id] = Time + duration;
            sourceModelBulletRokushikiLane[actor.Id] = SourceModelLaneRevision(actor.Id);
            events.Add(new CharacterCombatEvent(CharacterCombatEventType.StatusApplied,
                InstanceKey(actor.Id), InstanceKey(actor.Id), skill.Name,
                "TOOLTIP_MODEL_ONLY / 육식 체 3초 전투 상태", 0));
        }

        // Exactly ONCE per actual attack: an onAttack proc refreshes the 3s
        // stance, while a stance already active adds one packet per attack.
        private void ApplySourceModelBulletRokushikiAttack(CharacterInstance actor,
            SpatialCombatEnemy primary)
        {
            if (actor.Catalog.Id != "I-05" || actor.Profile == null) return;
            double expires;
            if (!sourceModelBulletRokushikiUntil.TryGetValue(actor.Id, out expires)) return;
            if (expires < Time)
            {
                sourceModelBulletRokushikiUntil.Remove(actor.Id);
                sourceModelBulletRokushikiLane.Remove(actor.Id);
                return;
            }
            if (!primary.Enemy.IsAlive) return;
            long laneRevision;
            if (!sourceModelBulletRokushikiLane.TryGetValue(actor.Id, out laneRevision) ||
                laneRevision != SourceModelLaneRevision(actor.Id))
            {
                sourceModelBulletRokushikiUntil.Remove(actor.Id);
                sourceModelBulletRokushikiLane.Remove(actor.Id);
                return;
            }
            ImportedCombatSkill skill = null;
            foreach (var candidate in actor.Profile.Skills)
                if (candidate.Id == "I-05:skill:2") { skill = candidate; break; }
            if (!SourceModelTwentiethCanActivate(actor, skill)) return;
            var equipment = SourceModelEquippedItemsOf(actor.Id);
            var targets = SourceScriptTargets(actor, primary, new SourceScriptHit(
                SourceScriptHitShape.Circle, skill.Radius.Value, 0, 0, 1, 1,
                Ordr323AttackType.Spells));
            foreach (var target in targets)
            {
                if (!target.Enemy.IsAlive) continue;
                SourceModelTwentiethDamage(actor, target, skill,
                    skill.Components[1].Number("amountPerHit").Value +
                    equipment.AttackSpeed *
                        skill.Components[1].Number("plusAttackSpeedItemStackMultiplier").Value,
                    "magical");
                int armor = (int)skill.Components[2].Number("stacks").Value +
                    (equipment.UniqueSpeed > 0 ? 2 : 0);
                int next = checked(ArmorBreakStacksOf(target.Id) + armor);
                armorBreakStacks[target.Id] = next;
                events.Add(new CharacterCombatEvent(CharacterCombatEventType.ArmorBreakApplied,
                    InstanceKey(actor.Id), target.Id, skill.Name,
                    "TOOLTIP_MODEL_ONLY / 육식 체 아머브레이크", next));
                if (equipment.UniqueSpeed > 0)
                    SourceModelTwentiethDamage(actor, target, skill,
                        equipment.AttackSpeed * 10000.0, "fixed");
            }
            if (equipment.LegendarySpeed > 0)
            {
                var expanded = SourceScriptTargets(actor, primary, new SourceScriptHit(
                    SourceScriptHitShape.Circle, 450, 0, 0, 1, 1,
                    Ordr323AttackType.Spells));
                foreach (var target in expanded)
                    SourceModelTwentiethDamage(actor, target, skill,
                        50000 + equipment.AttackSpeed * 4500.0, "magical");
            }
        }

        private void SourceModelBulletAfterConfirmedBasicHit(string actorKey,
            string enemyId, long actualBasicDamage)
        {
            if (actualBasicDamage < 600000) return;
            long id;
            CharacterInstance actor;
            SpatialCombatEnemy target;
            if (!long.TryParse(actorKey, out id) ||
                !characters.TryGetValue(id, out actor) || actor.Catalog.Id != "I-05" ||
                actor.Profile == null ||
                SourceModelEquippedItemsOf(id).LegendaryArmor <= 0 ||
                !enemies.TryGetValue(enemyId, out target) ||
                !CanAttackInThisLane(actor, target)) return;
            ImportedCombatSkill skill = null;
            foreach (var candidate in actor.Profile.Skills)
                if (candidate.Id == "I-05:skill:3") { skill = candidate; break; }
            if (!SourceModelTwentiethCanActivate(actor, skill)) return;
            // The threshold applies to an ACTUAL confirmed basic hit, not
            // nominal attack power or a preview damage guess.
            SourceModelTwentiethDamage(actor, target, skill,
                actualBasicDamage * .5, "fixed");
        }

        private void ScheduleSourceModelBulletUniqueStrike(CharacterInstance actor,
            SpatialCombatEnemy target, ImportedCombatSkill skill)
        {
            bool story = IsStoryDeployed(actor.Id);
            long laneRevision = SourceModelLaneRevision(actor.Id);
            string enemyId = target.Id;
            for (int tick = 1; tick <= 30; tick++)
            {
                effectExecutor.SchedulePracticeSourceAttack(InstanceKey(actor.Id), enemyId,
                    tick * .1, () => {
                        SpatialCombatEnemy current;
                        if (!CanFinishSourceModelDelayedAttack(actor.Id, story, laneRevision) ||
                            !enemies.TryGetValue(enemyId, out current) ||
                            !ReferenceEquals(current, target) || !current.Enemy.IsAlive)
                            return;
                        SourceModelTwentiethDamage(actor, current, skill, 50000, "magical");
                    });
            }
        }

        private void ForgetSourceModelTwentyFirstEquipment(long id)
        {
            sourceModelBulletRokushikiUntil.Remove(id);
            sourceModelBulletRokushikiLane.Remove(id);
            if (sourceModelEquippedItems != null) sourceModelEquippedItems.RemoveOwner(id);
        }
    }
}
