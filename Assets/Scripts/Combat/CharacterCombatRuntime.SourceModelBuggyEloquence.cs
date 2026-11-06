using System;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    public sealed partial class CharacterCombatRuntime
    {
        private static string SourceBuggyReceipt(long id, int level) =>
            "practice-buggy-eloquence:" + id + ":" + level;

        // Receipts live in the already-persisted shared wallet, not in a
        // parallel practice counter that resets when a session reloads.
        public int SourceBuggyEloquenceUsesOf(long instanceId)
        {
            CharacterInstance unit;
            if (!UsesSourceModelBasicAttacks || bulkGiftWallet == null ||
                !characters.TryGetValue(instanceId, out unit) ||
                unit.Catalog.Id != "E-05") return 0;
            int count = 0;
            while (count < 5 &&
                   bulkGiftWallet.HasCompletedTransaction(
                       SourceBuggyReceipt(instanceId, count + 1))) count++;
            return count;
        }

        // The recorded aura numbers are +1,000 flat attack and +1% attack
        // speed per purchase. They follow living owners and their lane,
        // including allies acquired after purchase, without ghost buffs.
        private int SourceBuggySharedAuraLevel(long allyId)
        {
            if (!characters.ContainsKey(allyId)) return 0;
            bool story = IsStoryDeployed(allyId);
            int level = 0;
            foreach (var unit in characters.Values)
                if (unit.Catalog.Id == "E-05" &&
                    IsStoryDeployed(unit.Id) == story)
                    level = Math.Max(level, SourceBuggyEloquenceUsesOf(unit.Id));
            // Multiple Buggy auras use the strongest, never an unverified sum.
            return level;
        }

        private double SourceBuggyFlatAura(long allyId) =>
            1000d * SourceBuggySharedAuraLevel(allyId);

        private double SourceBuggySpeedAura(string actorKey)
        {
            long id;
            return long.TryParse(actorKey, out id) ?
                SourceBuggySharedAuraLevel(id) : 0;
        }

        public bool TryPurchaseSourceBuggyEloquence(long instanceId)
        {
            if (!UsesSourceModelBasicAttacks || bulkGiftWallet == null ||
                game.Phase != SessionPhase.Running || processingStep ||
                pendingStepSeconds != 0) return false;
            CharacterInstance actor;
            if (!characters.TryGetValue(instanceId, out actor) ||
                actor.Catalog.Id != "E-05" || actor.Profile == null ||
                actor.Profile.UnitId != "E-05" || actor.Mapping == null ||
                actor.Mapping.AttackSpec.UnitRawCode != "h05A") return false;
            var skill = SourceManualSkill(actor, "언변 강화");
            if (skill == null || skill.Id != "E-05:skill:3" ||
                skill.Trigger != "manual" ||
                skill.Number("goldCost") != 3333 ||
                skill.Components.Count != 1 ||
                skill.Components[0].Type != "permanentAuraUpgrade" ||
                skill.Components[0].Number("attackPowerIncreasePerUse") != 1000 ||
                skill.Components[0].Number("attackSpeedPercentIncreasePerUse") != 1 ||
                skill.Components[0].Number("maxUses") != 5) return false;
            int used = SourceBuggyEloquenceUsesOf(instanceId);
            if (used >= 5 || !bulkGiftWallet.TryExchangeOnce(
                    SourceBuggyReceipt(instanceId, used + 1),
                    new ResourceBundle().Add("gold", 3333), new ResourceBundle()))
                return false;
            pendingCommandEvents.Add(new CharacterCombatEvent(
                CharacterCombatEventType.SkillActivated, InstanceKey(instanceId),
                null, skill.Name, "TOOLTIP_MODEL_ONLY 구매 " + (used + 1) +
                "/5 · 공유 지갑 3333골드 · 같은 전장 아군 최고 오라 적용", 0));
            return true;
        }
    }
}
