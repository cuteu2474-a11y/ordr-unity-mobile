using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // Tooltip-first manual skills, using the existing wallet, actor swap,
    // status engine, lane checks and source-model combat clock.
    public sealed partial class CharacterCombatRuntime
    {
        // An interrupted upgrade keeps the already sampled source-script
        // outcome, rather than charging another RNG call on a retry.
        private sealed class PendingViviUpgrade
        {
            public long Progress;
            public bool Won;
            public string Receipt;
        }
        private readonly Dictionary<long, PendingViviUpgrade> pendingViviUpgrades =
            new Dictionary<long, PendingViviUpgrade>();

        private readonly Dictionary<long, int> manualMariaMode =
            new Dictionary<long, int>();
        private readonly Dictionary<long, double> manualMariaReady =
            new Dictionary<long, double>();
        // Separate active-effect clock from the 30-second reuse cooldown.
        // Displaying only last chosen mode implied a permanent effect.
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
            PendingViviUpgrade pending;
            if (pendingViviUpgrades.TryGetValue(id, out pending))
            {
                if (pending.Progress != progress)
                    throw new InvalidOperationException(
                        "비비 강화의 보류 레벨과 현재 레벨이 다릅니다.");
                if (wallet.HasCompletedTransaction(pending.Receipt))
                    return false; // Re-entrant duplicate during notification.
            }
            else
            {
                bool won;
                if (!TryRollSourceModelViviUpgrade(id, chance, out won))
                    return false;
                pending = new PendingViviUpgrade {
                    Progress = progress, Won = won,
                    // A failed roll also consumes materials. It must not
                    // permanently lock this level against later paid tries.
                    Receipt = "practice-tooltip:vivi-alabasta:" + id + ":" +
                        Guid.NewGuid().ToString("N")
                };
                pendingViviUpgrades.Add(id, pending);
            }

            var cost = new ResourceBundle()
                .Add(SourceModelNativeCharacterRules.WoodResourceId, 2)
                .Add(WispResourceIds.Random, 3);
            var credit = new ResourceBundle();
            if (pending.Won) credit.Add(levelKey, 1);
            bool committed;
            try
            {
                committed = wallet.TryExchangeOnce(pending.Receipt, cost, credit);
            }
            catch
            {
                // The wallet can notify listeners AFTER committing both the
                // debit and durable receipt. Never roll that back and risk a
                // duplicated reward on retry. A pre-commit failure retains
                // this outcome for the next attempt.
                committed = wallet.HasCompletedTransaction(pending.Receipt);
                if (!committed) return false;
            }
            if (!committed) return false;
            pendingViviUpgrades.Remove(id);
            success = pending.Won;
            updatedLevel = currentLevel + (pending.Won ? 1 : 0);
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

        public bool CanCreateSourceModelHibariBento(long id, EconomyWallet wallet) =>
            wallet != null &&
            (otamaRewardWallet == null ||
             Object.ReferenceEquals(otamaRewardWallet, wallet)) &&
            HasExactManualOwner(id, "L-39", "h0CM", "사랑의도시락(B)") &&
            !wallet.HasCompletedTransaction(
                Original323SourceNativeActionKeys.HibariCreateReceipt(id));

        public bool TryCreateSourceModelHibariBento(long id, EconomyWallet wallet)
        {
            if (!CanCreateSourceModelHibariBento(id, wallet)) return false;
            string receipt = Original323SourceNativeActionKeys.HibariCreateReceipt(id);
            try
            {
                return wallet.TryGrantOnce(receipt, new ResourceBundle().Add(
                    Original323SourceNativeActionKeys.HibariItem(id), 1));
            }
            catch { return wallet.HasCompletedTransaction(receipt); }
        }

        private bool IsSourceHibariBentoTarget(long targetId)
        {
            CharacterInstance target;
            if (!characters.TryGetValue(targetId, out target) ||
                target.Mapping == null || !target.AttackReady ||
                target.Mapping.AttackSpec.UnitRawCode == "h0CM" ||
                IsLockedFromRosterTransactions(targetId))
                return false;
            // JASS Buy: GetUnitPointValue(target) >= 101.
            // Never substitute a rarity/grade threshold.
            int? points = sourceModelRules == null ? null :
                sourceModelRules.OriginalUnitPointValue(target.Catalog.Id,
                    target.Mapping.AttackSpec.UnitRawCode);
            return points.HasValue && points.Value >= 101;
        }

        public bool CanUseSourceModelHibariBento(long id, long recipientId,
            EconomyWallet wallet)
        {
            if (id <= 0 || wallet == null ||
                (otamaRewardWallet != null &&
                 !Object.ReferenceEquals(otamaRewardWallet, wallet)) ||
                wallet.Balance(Original323SourceNativeActionKeys.HibariItem(id)) != 1 ||
                !wallet.HasCompletedTransaction(
                    Original323SourceNativeActionKeys.HibariCreateReceipt(id)) ||
                wallet.HasCompletedTransaction(
                    Original323SourceNativeActionKeys.HibariReceipt(id)) ||
                !IsSourceHibariBentoTarget(recipientId)) return false;
            CharacterInstance owner;
            if (characters.TryGetValue(id, out owner) &&
                !CanShareAllyCombatLane(id, recipientId)) return false;
            foreach (var effect in unitStatuses[InstanceKey(recipientId)].Snapshot())
                if (effect.Type == CombatEffectType.AttackSpeedBuff &&
                    (effect.SourceId.StartsWith("practice-tooltip:hibari-bento:",
                        StringComparison.Ordinal) ||
                     effect.SourceId.Contains("포만감")))
                    return false;
            return true;
        }

        public bool TryUseSourceModelHibariBento(long id, long recipientId,
            EconomyWallet wallet)
        {
            if (!CanUseSourceModelHibariBento(id, recipientId, wallet)) return false;
            string receipt = Original323SourceNativeActionKeys.HibariReceipt(id);
            string source = "practice-tooltip:hibari-bento:" + id + ":" + recipientId;
            var status = unitStatuses[InstanceKey(recipientId)];
            double percent = characters[recipientId].Catalog.Id == "TR-24" ? 20 : 12;
            try
            {
                // First stage a removable buff. Item and 1-use receipt
                // commit atomically; a post-commit event exception is NOT
                // allowed to refund an already consumed physical Bento.
                status.Apply(new CombatEffect(source,
                    CombatEffectType.AttackSpeedBuff,
                    CombatEffectOrigin.Aura, percent, 0));
                bool committed;
                try
                {
                    committed = wallet.TryExchangeOnce(receipt,
                        new ResourceBundle().Add(
                            Original323SourceNativeActionKeys.HibariItem(id), 1),
                        new ResourceBundle());
                }
                catch { committed = wallet.HasCompletedTransaction(receipt); }
                if (!committed)
                {
                    status.RemoveSource(source);
                    return false;
                }
                pendingCommandEvents.Add(new CharacterCombatEvent(
                    CharacterCombatEventType.StatusApplied,
                    InstanceKey(id), InstanceKey(recipientId),
                    "사랑의도시락(B)", "JASS A0HN/h0CN 소비→A0HO Lv." +
                    (percent == 20 ? "2" : "1") + " 공속 +" + percent + "%", 0));
                return true;
            }
            catch
            {
                if (wallet.HasCompletedTransaction(receipt)) return true;
                status.RemoveSource(source);
                return false;
            }
        }

        public bool TrySellSourceModelHibariBento(long id, EconomyWallet wallet)
        {
            if (id <= 0 || wallet == null ||
                (otamaRewardWallet != null &&
                 !Object.ReferenceEquals(otamaRewardWallet, wallet)) ||
                wallet.Balance(Original323SourceNativeActionKeys.HibariItem(id)) != 1 ||
                !wallet.HasCompletedTransaction(
                    Original323SourceNativeActionKeys.HibariCreateReceipt(id)))
                return false;
            string receipt = Original323SourceNativeActionKeys.HibariSellReceipt(id);
            try
            {
                return wallet.TryExchangeOnce(receipt,
                    new ResourceBundle().Add(
                        Original323SourceNativeActionKeys.HibariItem(id), 1),
                    new ResourceBundle().Add(WispResourceIds.Choice, 1));
            }
            catch { return wallet.HasCompletedTransaction(receipt); }
        }

        public double SourceModelMariaCooldownRemaining(long id)
        {
            double due;
            return manualMariaReady.TryGetValue(id, out due) ?
                Math.Max(0, due - Time) : 0;
        }
        public string SourceModelMariaCurrentModeName(long id)
        {
            int stage;
            return manualMariaMode.TryGetValue(id, out stage) &&
                manualMariaToken.ContainsKey(id) ?
                MariaModeName(stage) : "미발동";
        }
        public double SourceModelMariaEffectRemaining(long id)
        {
            // The CDh JASS periodic loop has no six-tick duration.
            // Reuse cooldown is separate from the indefinitely active aura.
            return 0;
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
            !IsOriginalUnitCasting(id) && !SourceFormIsCasting(id);

        // One versioned descriptor per active CDh loop. The pending
        // callback's original sequence/At are restored by the shared queue,
        // so continue never restarts the 0.06s startup or spends a cast.
        private const string SourceMariaPulseKind = "source-maria-cdh-v1";

        private static string SourceMariaPulseTarget(long id, long token, int tick) =>
            "practice-maria:" + id.ToString(CultureInfo.InvariantCulture) + ":" +
            token.ToString(CultureInfo.InvariantCulture) + ":" +
            tick.ToString(CultureInfo.InvariantCulture);

        private static string SourceMariaPulsePayload(long id, long token,
            bool story, long lane, int tick) =>
            id.ToString(CultureInfo.InvariantCulture) + "|" +
            token.ToString(CultureInfo.InvariantCulture) + "|" +
            (story ? "1" : "0") + "|" +
            lane.ToString(CultureInfo.InvariantCulture) + "|" +
            tick.ToString(CultureInfo.InvariantCulture);

        private static bool TryReadSourceMariaPulse(string payload,
            out long id, out long token, out bool story, out long lane, out int tick)
        {
            id = 0; token = 0; lane = 0; story = false; tick = 0;
            if (string.IsNullOrWhiteSpace(payload)) return false;
            string[] parts = payload.Split('|');
            if (parts.Length != 5 ||
                !long.TryParse(parts[0], NumberStyles.None,
                    CultureInfo.InvariantCulture, out id) ||
                !long.TryParse(parts[1], NumberStyles.None,
                    CultureInfo.InvariantCulture, out token) ||
                (parts[2] != "0" && parts[2] != "1") ||
                !long.TryParse(parts[3], NumberStyles.None,
                    CultureInfo.InvariantCulture, out lane) ||
                !int.TryParse(parts[4], NumberStyles.None,
                    CultureInfo.InvariantCulture, out tick))
                return false;
            story = parts[2] == "1";
            return id > 0 && token > 0 && lane >= 0 && tick >= 0 &&
                tick < int.MaxValue &&
                SourceMariaPulsePayload(id, token, story, lane, tick) == payload;
        }

        private Action CreateSourceMariaPulse(CharacterInstance actor,
            long token, bool storyLane, long laneRevision, int tick)
        {
            return () => {
                long active;
                int stage;
                if (!manualMariaToken.TryGetValue(actor.Id, out active) ||
                    active != token ||
                    !manualMariaMode.TryGetValue(actor.Id, out stage) ||
                    !CanFinishSourceModelDelayedAttack(actor.Id,
                        storyLane, laneRevision)) return;
                TickSourceMaria(actor, stage, tick);
                ScheduleSourceMariaPulse(actor, token, storyLane,
                    laneRevision, 5, tick + 1);
            };
        }

        // Original CDh 42569-42613: .03+.03 startup, then a 5-second
        // recurring pulse (4.97+.03 phases) for the lifetime of its owner.
        // A0T5 changes the current mode; it never resets the CDh clock.
        private void ScheduleSourceMariaPulse(CharacterInstance actor,
            long token, bool storyLane, long laneRevision, double delay, int tick)
        {
            if (tick < 0 || tick >= int.MaxValue)
                throw new OverflowException("마리아 반복 주기 순번이 초과됐습니다.");
            effectExecutor.ScheduleRestorableSourceAttack(InstanceKey(actor.Id),
                SourceMariaPulseTarget(actor.Id, token, tick), delay,
                SourceMariaPulseKind,
                SourceMariaPulsePayload(actor.Id, token, storyLane,
                    laneRevision, tick),
                CreateSourceMariaPulse(actor, token, storyLane, laneRevision, tick));
        }

        private Action RestoreSourceModelTimedEffect(string kind,
            string payload, string casterId, string targetId)
        {
            if (kind != SourceMariaPulseKind)
                return RestoreSourceModelKumaPhoenixFire(
                    kind, payload, casterId, targetId);
            long id, token, lane;
            int tick;
            bool story;
            if (!UsesSourceModelBasicAttacks ||
                !TryReadSourceMariaPulse(payload, out id, out token,
                    out story, out lane, out tick) ||
                casterId != InstanceKey(id) ||
                targetId != SourceMariaPulseTarget(id, token, tick))
                return null;
            CharacterInstance actor;
            long active;
            if (!characters.TryGetValue(id, out actor) ||
                actor.Catalog.Id != "D-01" ||
                !manualMariaToken.TryGetValue(id, out active) ||
                active != token || token > manualMariaNextToken ||
                story != IsStoryDeployed(id) ||
                lane != SourceModelLaneRevision(id))
                return null;
            return CreateSourceMariaPulse(actor, token, story, lane, tick);
        }

        public bool TryCycleSourceModelMaria(long id)
        {
            if (!CanCycleSourceModelMaria(id)) return false;
            CharacterInstance actor = characters[id];
            int previous;
            int stage = manualMariaMode.TryGetValue(id, out previous)
                ? (previous + 1) % 3 : 0;
            manualMariaMode[id] = stage;
            if (!manualMariaToken.ContainsKey(id))
            {
                if (manualMariaNextToken == long.MaxValue)
                    throw new OverflowException("마리아네트 효과 순번 초과");
                long token = ++manualMariaNextToken;
                manualMariaToken[id] = token;
                ScheduleSourceMariaPulse(actor, token, IsStoryDeployed(id),
                    SourceModelLaneRevision(id), .06, 0);
            }
            // The 30s reuse rule is in the imported skill profile, NOT
            // explicitly in JASS CDs; do not conflate it with the CDh loop.
            manualMariaReady[id] = Time + 30;
            pendingCommandEvents.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, InstanceKey(id), null,
                MariaModeName(stage),
                "JASS CDh 0.06초 기동/5초 반복 · A0T5 별도 모드 교체", 0));
            return true;
        }

        // Original CDg at 42562-42568 uses CHAOS/DAMAGE_TYPE_NORMAL
        // for the 5% missing-HP physical follow-up.
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
                            false, Ordr323AttackType.Chaos);
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
                target != null &&
                (target.StartsWith("practice-maria:" + id + ":",
                    StringComparison.Ordinal) ||
                 target.StartsWith("practice-volley:" + id + ":",
                    StringComparison.Ordinal)));
            manualMariaToken.Remove(id);
            if (deleted)
            {
                manualMariaMode.Remove(id);
                manualMariaReady.Remove(id);
                pendingViviUpgrades.Remove(id);
            }
            string source = "practice-tooltip:maria:" + id;
            foreach (var status in statuses.Values) status.RemoveSource(source);
        }
    }
}
