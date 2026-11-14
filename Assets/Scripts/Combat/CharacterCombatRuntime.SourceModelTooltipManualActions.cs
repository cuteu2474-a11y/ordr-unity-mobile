using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // Tooltip-first manual skills, using the existing wallet, actor swap,
    // status engine, lane checks and source-model combat clock.
    public sealed partial class CharacterCombatRuntime
    {
        private readonly Dictionary<long, int> manualMariaMode =
            new Dictionary<long, int>();
        private readonly Dictionary<long, double> manualMariaReady =
            new Dictionary<long, double>();
        // Separate active-effect clock from the 30-second reuse cooldown.
        // Displaying only last chosen mode implied a permanent effect.
        private readonly Dictionary<long, double> manualMariaActiveUntil =
            new Dictionary<long, double>();
        private readonly Dictionary<long, long> manualMariaToken =
            new Dictionary<long, long>();
        private long manualMariaNextToken;

        private bool HasExactManualOwner(long id, string catalog, string rawcode,
            string skillName)
        {
            CharacterInstance actor;
            if (!UsesSourceModelBasicAttacks || sourceModelRules == null ||
                game.Phase != SessionPhase.Running || processingStep ||
                pendingStepSeconds != 0 || activeOriginalFusionSwap != null ||
                !characters.TryGetValue(id, out actor) ||
                IsLockedFromRosterTransactions(id) ||
                actor.Catalog.Id != catalog || actor.Profile == null ||
                actor.Profile.UnitId != catalog || actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != rawcode) return false;
            var skill = SourceManualSkill(actor, skillName);
            if (skill == null || skill.Trigger != "manual") return false;
            if (catalog == "E-06")
                return skill.Id == "E-06:skill:1" &&
                    skill.Number("lumberCost") == 2 &&
                    skill.Number("basicRandomWispCost") == 3 &&
                    skill.Components.Count == 1 &&
                    skill.Components[0].Type == "upgradeSkill" &&
                    skill.Components[0].Text("name") == "알라바스타";
            if (catalog == "L-14")
                return skill.Id == "L-14:skill:0" &&
                    skill.Target == "alliedUnit" &&
                    skill.Components.Count == 2 &&
                    skill.Components[0].Type == "transformAllyUnit" &&
                    Equals(skill.Components[0].Get("toSelectedLegendaryUnit"), true) &&
                    skill.Components[0].Number("traitPointsCost") == 1 &&
                    skill.Components[1].Type == "attackSpeedDebuff" &&
                    skill.Components[1].Number("percent") == 5;
            if (catalog == "L-39")
                return skill.Id == "L-39:skill:1" &&
                    skill.Target == "ownedAlly" &&
                    skill.Number("uses") == 1 &&
                    skill.Components.Count == 1 &&
                    skill.Components[0].Type == "permanentAllyAttackSpeedBuff" &&
                    skill.Components[0].Number("percent") == 12 &&
                    skill.Components[0].Text("overridePercentForUnit") == "코비 초월" &&
                    skill.Components[0].Number("overridePercent") == 20 &&
                    Equals(skill.Components[0].Get("consumesItem"), true);
            if (catalog == "D-01")
                return skill.Id == "D-01:skill:2" &&
                    skill.Number("cooldownSeconds") == 30 &&
                    skill.Components.Count == 1 &&
                    skill.Components[0].Type == "cycleEffect" &&
                    Equals(skill.Components[0].Get("changeOnlyWhenPreviousExpired"), true);
            return false;
        }

        public bool CanRollSourceModelViviUpgrade(long id, int chance) =>
            HasExactManualOwner(id, "E-06", "h057", "알라바스타 강화") &&
            chance >= 1 && chance <= 100;

        public bool TryRollSourceModelViviUpgrade(long id, int chance, out bool won)
        {
            won = false;
            if (!CanRollSourceModelViviUpgrade(id, chance)) return false;
            won = sourceModelRules.NextScriptRoll(100) < chance;
            return true;
        }

        // One verified UI action owns level validation, the original RNG roll
        // and the shared-wallet debit/level credit. Callers cannot submit an
        // arbitrary success rate or forget the failed-roll material cost.
        public bool TryUpgradeSourceModelViviAlabasta(long id, EconomyWallet wallet,
            out bool success, out int updatedLevel)
        {
            success = false;
            updatedLevel = 0;
            if (wallet == null ||
                (otamaRewardWallet != null &&
                 !Object.ReferenceEquals(otamaRewardWallet, wallet)))
                return false;
            string levelKey = Original323SourceNativeActionKeys.ViviLevel(id);
            long progress = wallet.Balance(levelKey);
            if (progress < 0 || progress >= 9) return false;
            int currentLevel = (int)progress + 1;
            int chance = Original323SourceNativeActionKeys.ViviChance(currentLevel);
            if (!CanRollSourceModelViviUpgrade(id, chance) ||
                wallet.Balance(SourceModelNativeCharacterRules.WoodResourceId) < 2 ||
                wallet.Balance(WispResourceIds.Random) < 3) return false;
            var before = wallet.CaptureState();
            bool rolled;
            if (!TryRollSourceModelViviUpgrade(id, chance, out rolled)) return false;
            try
            {
                var cost = new ResourceBundle()
                    .Add(SourceModelNativeCharacterRules.WoodResourceId, 2)
                    .Add(WispResourceIds.Random, 3);
                var credit = new ResourceBundle();
                if (rolled) credit.Add(levelKey, 1);
                if (!wallet.TryExchange(cost, credit)) return false;
            }
            catch
            {
                // A Grant observer can throw after an otherwise successful
                // exchange. Do not keep a level increase without its cost,
                // or a cost without an acknowledged result.
                wallet.RestoreState(before);
                return false;
            }
            success = rolled;
            updatedLevel = currentLevel + (rolled ? 1 : 0);
            return true;
        }

        public bool CanSourceModelBruleeCopy(long sourceId, long targetId)
        {
            CharacterInstance target;
            return sourceId != targetId &&
                HasExactManualOwner(sourceId, "L-14", "h08S", "미러미러열매") &&
                characters.TryGetValue(targetId, out target) &&
                target.Catalog.Id.StartsWith("L-", StringComparison.Ordinal) &&
                target.Mapping != null && target.AttackReady &&
                !IsLockedFromRosterTransactions(targetId) &&
                CanShareAllyCombatLane(sourceId, targetId);
        }

        public bool TrySourceModelBruleeCopy(long sourceId, long targetId,
            EconomyWallet wallet, out long copyId)
        {
            copyId = 0;
            if (wallet == null || !CanSourceModelBruleeCopy(sourceId, targetId) ||
                (otamaRewardWallet != null &&
                 !Object.ReferenceEquals(otamaRewardWallet, wallet)) ||
                wallet.Balance(SourceModelNativeCharacterRules.TraitPointsResourceId) < 1)
                return false;
            var source = roster.Get(sourceId);
            string legend = characters[targetId].Catalog.Id;
            var walletBefore = wallet.CaptureState();
            bool paid = false;
            try
            {
                // Copying consumes BRULEE, not the target legendary. The
                // existing atomic roster swap preserves the template actor.
                using (var swap = BeginOriginalFusionUnitSwap(
                    new long[] { sourceId }, legend, source.X, source.Y))
                {
                    copyId = swap.ResultInstanceId;
                    if (copyId <= 0) return false;
                    unitStatuses[InstanceKey(copyId)].Apply(new CombatEffect(
                        "practice-brulee:copy:" + sourceId + ":" + copyId,
                        CombatEffectType.AttackSpeedPenalty,
                        CombatEffectOrigin.Aura, 5, 0));
                    paid = wallet.TryExchange(new ResourceBundle().Add(
                        SourceModelNativeCharacterRules.TraitPointsResourceId, 1),
                        new ResourceBundle());
                    if (!paid) return false;
                    swap.Commit();
                    return true;
                }
            }
            catch
            {
                // A failed staged swap keeps Brulee in the original roster;
                // the cost-only debit can then safely be restored.
                if (paid && characters.ContainsKey(sourceId))
                    wallet.RestoreState(walletBefore);
                copyId = 0;
                return false;
            }
        }

        public bool CanUseSourceModelHibariBento(long id, long recipientId,
            EconomyWallet wallet)
        {
            CharacterInstance recipient;
            if (wallet == null ||
                (otamaRewardWallet != null &&
                 !Object.ReferenceEquals(otamaRewardWallet, wallet)) ||
                !HasExactManualOwner(id, "L-39", "h0CM", "사랑의도시락(B)") ||
                !characters.TryGetValue(recipientId, out recipient) ||
                !CanShareAllyCombatLane(id, recipientId) ||
                IsLockedFromRosterTransactions(recipientId) ||
                wallet.HasCompletedTransaction(
                    Original323SourceNativeActionKeys.HibariReceipt(id)))
                return false;
            foreach (var old in unitStatuses[InstanceKey(recipientId)].Snapshot())
                if (old.Type == CombatEffectType.AttackSpeedBuff &&
                    (old.SourceId.StartsWith("practice-tooltip:hibari-bento:",
                        StringComparison.Ordinal) ||
                     old.SourceId.IndexOf("포만감", StringComparison.Ordinal) >= 0))
                    return false;
            return true;
        }

        public bool TryUseSourceModelHibariBento(long id, long recipientId,
            EconomyWallet wallet)
        {
            if (!CanUseSourceModelHibariBento(id, recipientId, wallet)) return false;
            var before = wallet.CaptureState();
            string receipt = Original323SourceNativeActionKeys.HibariReceipt(id);
            string source = "practice-tooltip:hibari-bento:" + id + ":" + recipientId;
            try
            {
                if (!wallet.TryExchangeOnce(receipt,
                        new ResourceBundle(), new ResourceBundle())) return false;
                double bonus = characters[recipientId].Catalog.Id == "TR-24" ? 20 : 12;
                unitStatuses[InstanceKey(recipientId)].Apply(new CombatEffect(
                    source,
                    CombatEffectType.AttackSpeedBuff,
                    CombatEffectOrigin.Aura, bonus, 0));
                pendingCommandEvents.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.StatusApplied,
                    InstanceKey(id), InstanceKey(recipientId), "사랑의도시락(B)",
                    "TOOLTIP_MODEL_ONLY 1회 공속 +" + bonus + "%", 0));
                return true;
            }
            catch
            {
                // If the status event fails after the one-use receipt, unwind
                // both the receipt and its effect instead of burning a Bento.
                CombatStatusEngine status;
                if (unitStatuses.TryGetValue(InstanceKey(recipientId), out status))
                    status.RemoveSource(source);
                wallet.RestoreState(before);
                return false;
            }
        }

        public double SourceModelMariaCooldownRemaining(long id)
        {
            double due;
            return manualMariaReady.TryGetValue(id, out due) ?
                Math.Max(0, due - Time) : 0;
        }
        public string SourceModelMariaCurrentModeName(long id)
        {
            int mode;
            double activeUntil;
            if (!manualMariaMode.TryGetValue(id, out mode))
                return "미발동";
            return manualMariaToken.ContainsKey(id) &&
                manualMariaActiveUntil.TryGetValue(id, out activeUntil) &&
                Time < activeUntil ? MariaModeName(mode) : "효과 종료";
        }

        public double SourceModelMariaEffectRemaining(long id)
        {
            double until;
            return manualMariaToken.ContainsKey(id) &&
                manualMariaActiveUntil.TryGetValue(id, out until)
                    ? Math.Max(0, until - Time) : 0;
        }
        public string SourceModelMariaNextModeName(long id)
        {
            int mode;
            return MariaModeName(manualMariaMode.TryGetValue(id, out mode) ?
                (mode + 1) % 3 : 0);
        }
        private static string MariaModeName(int stage) =>
            stage == 0 ? "꼼짝말란 마리아" :
            stage == 1 ? "멈춰서란 마리아" : "타오르란 마리아";

        public bool CanCycleSourceModelMaria(long id) =>
            HasExactManualOwner(id, "D-01", "h04U", "마리아네트 전환") &&
            SourceModelMariaCooldownRemaining(id) == 0 &&
            SourceModelMariaEffectRemaining(id) == 0 &&
            !IsOriginalUnitCasting(id) && !SourceFormIsCasting(id);

        public bool TryCycleSourceModelMaria(long id)
        {
            if (!CanCycleSourceModelMaria(id)) return false;
            CharacterInstance actor = characters[id];
            int previous;
            int stage = manualMariaMode.TryGetValue(id, out previous)
                ? (previous + 1) % 3 : 0;
            if (manualMariaNextToken == long.MaxValue)
                throw new OverflowException("마리아네트 기술 순번 초과");
            long token = ++manualMariaNextToken;
            manualMariaMode[id] = stage;
            manualMariaToken[id] = token;
            // The slow is refreshed by the last tick at t=25 and lasts 6s.
            // Do not change to another mode while it is still active.
            manualMariaReady[id] = Time + (stage == 1 ? 31.01 : 30);
            // Last periodic tick at +25.01. Its effect expires after a
            // 3.5s stun / 6s slow; fire packets are instant.
            manualMariaActiveUntil[id] = Time +
                (stage == 0 ? 28.51 : stage == 1 ? 31.01 : 25.01);
            bool lane = IsStoryDeployed(id);
            long revision = SourceModelLaneRevision(id);
            for (int tick = 0; tick < 6; tick++)
            {
                int number = tick;
                effectExecutor.SchedulePracticeSourceAttack(InstanceKey(id),
                    "practice-maria:" + id + ":" + token + ":" + tick,
                    .01 + tick * 5, () => {
                        long live;
                        if (!manualMariaToken.TryGetValue(id, out live) ||
                            live != token ||
                            !CanFinishSourceModelDelayedAttack(id, lane, revision))
                            return;
                        TickSourceMaria(actor, stage, number);
                    });
            }
            pendingCommandEvents.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated,
                InstanceKey(id), null, MariaModeName(stage),
                "TOOLTIP_MODEL_ONLY 30초/5초 모델, WC3 실제 시전 프레임 미검증", 0));
            return true;
        }

        private void TickSourceMaria(CharacterInstance actor, int stage, int tick)
        {
            var position = roster.Get(actor.Id);
            var targets = new List<SpatialCombatEnemy>();
            foreach (var target in enemies.Values)
            {
                if (!target.Enemy.IsAlive || !CanAttackInThisLane(actor, target))
                    continue;
                double dx = position.X - target.X, dy = position.Y - target.Y;
                if (dx * dx + dy * dy <= 900 * 900) targets.Add(target);
            }
            targets.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            foreach (var target in targets)
            {
                if (!target.Enemy.IsAlive) continue;
                string source = "practice-tooltip:maria:" + actor.Id;
                if (stage == 0 && target.Kind == CharacterEnemyKind.Line)
                {
                    target.Enemy.Status.RemoveSource(source);
                    target.Enemy.Status.Apply(new CombatEffect(source,
                        CombatEffectType.Stun,
                        CombatEffectOrigin.TriggeredSkill, 1, 3.5));
                }
                else if (stage == 1)
                {
                    target.Enemy.Status.RemoveSource(source);
                    target.Enemy.Status.Apply(new CombatEffect(source,
                        CombatEffectType.MovementSlowPercent,
                        CombatEffectOrigin.TriggeredSkill, 40, 6));
                }
                else if (stage == 2)
                {
                    ApplySourceModelTooltipDamage(actor, target, "타오르란 마리아",
                        "주기 200000 마법", 200000, false);
                    if ((target.Kind == CharacterEnemyKind.Line ||
                         target.Kind == CharacterEnemyKind.Enraged) &&
                        target.Enemy.IsAlive)
                        ApplySourceModelTooltipDamage(actor, target,
                            "타오르란 마리아", "잃은 체력 5% 물리",
                            (target.Enemy.MaxHealth - target.Enemy.Health) * .05,
                            false, Ordr323AttackType.Melee);
                }
            }
            if (stage != 2 && targets.Count > 0)
                events.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.StatusApplied,
                    InstanceKey(actor.Id), targets[0].Id,
                    MariaModeName(stage), "TOOLTIP_MODEL_ONLY 900범위·주기 " +
                    (tick + 1), 0));
        }

        private void ForgetSourceModelTooltipManualActor(long id, bool deleted)
        {
            // Cancel the actual pending callbacks as well as their token.
            // Leaving canceled Action lambdas in the queue blocks draft
            // checkpoint capture and retains dead actor references.
            effectExecutor.CancelCasterTargets(InstanceKey(id), target =>
                target != null && target.StartsWith(
                    "practice-maria:" + id + ":", StringComparison.Ordinal));
            manualMariaToken.Remove(id);
            manualMariaActiveUntil.Remove(id);
            if (deleted)
            {
                manualMariaMode.Remove(id);
                manualMariaReady.Remove(id);
            }
            string source = "practice-tooltip:maria:" + id;
            foreach (var status in statuses.Values) status.RemoveSource(source);
        }
    }
}
