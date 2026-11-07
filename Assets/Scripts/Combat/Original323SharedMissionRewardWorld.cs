using System;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // The native adapter must confirm that the original admiral-specific
    // buff was actually applied, not just that the boss was defeated.
    public interface IOriginal323SharedMissionFollowUpWitness
    {
        bool TryConfirmAppliedAdmiralBuff(string sourceEventId,
            string originalAdmiralRawcode, out string sourceEvidence);
    }

    // One wallet receipt per distinct original CN*/CM* shared-mission clear.
    // The source witness certifies the in-game event; this class never
    // manufactures a building death, treasure, admiral or native item dig.
    public sealed class Original323SharedMissionRewardWorld
    {
        public const string ReceiptPrefix = "shared:original323:mission:";
        // Shared-mission Bhd grants the SAME fZ ordinary excavation stack
        // that source-confirmed BhS consumes. There is no second voucher.
        public const string ExcavationStackResource = SourceModelItemExcavation.OrdinaryStacks;
        public const string PendingExcavationResource = ExcavationStackResource; // old caller alias
        public const string PendingGateAdmiralResource = "shared:pending:gate-admiral";
        public const string PendingAdmiralBuffResource = "shared:pending:admiral-buff";

        private readonly CharacterCombatRuntime combat;
        private readonly SharedEconomySession economy;
        private readonly OriginalMapBaseStatCatalog originals;
        private readonly Original323PoneglyphSession missions;
        private readonly IOriginalWispRandom scriptRandom;

        public Original323SharedMissionRewardWorld(CharacterCombatRuntime combat,
            SharedEconomySession economy, OriginalMapBaseStatCatalog originalObjects,
            Original323PoneglyphSession sharedMissions,
            IOriginalWispRandom originalScriptRandom)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
            originals = originalObjects ?? throw new ArgumentNullException(nameof(originalObjects));
            missions = sharedMissions ?? throw new ArgumentNullException(nameof(sharedMissions));
            scriptRandom = originalScriptRandom ??
                throw new ArgumentNullException(nameof(originalScriptRandom));
            if (!ReferenceEquals(combat.Roster, economy.Units) ||
                GameSession.ModeId != "nightmare-otherworld")
                throw new ArgumentException("공유 도전과제는 악몽 이세계의 같은 전투·지갑에서만 처리합니다.");
        }

        public long OrdinaryExcavationStock => economy.Wallet.Balance(ExcavationStackResource);
        public long PendingExcavations => OrdinaryExcavationStock; // compatibility getter
        public long PendingGateAdmirals => economy.Wallet.Balance(PendingGateAdmiralResource);
        public long PendingAdmiralBuffs => economy.Wallet.Balance(PendingAdmiralBuffResource);

        // Called only after the objective world has witnessed the matching
        // admiral's buff effect; never infer a buff from a defeat alone.
        internal bool TryRecordRealAdmiralBuff(string sourceEventId,
            string originalAdmiralRawcode, string verifiedAppliedEvidence)
        {
            if (combat.Game.Phase != SessionPhase.Running ||
                !ValidSourceId(sourceEventId) ||
                !Original323SharedObjectiveSpawnPlan.IsOriginalIdentity(
                    Original323SharedMissionKind.MarineAdmiral,
                    "CM3", originalAdmiralRawcode) ||
                !missions.IsSharedMissionCompleted(
                    Original323SharedMissionKind.MarineAdmiral) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(
                    verifiedAppliedEvidence))
                return false;
            return TryConsumeNativeFollowUp(
                "shared:original323:admiral-buff:" + sourceEventId,
                PendingAdmiralBuffResource);
        }

        private bool TryConsumeNativeFollowUp(string receipt, string pendingId)
        {
            if (economy.Wallet.HasCompletedTransaction(receipt)) return true;
            try
            {
                if (economy.Wallet.TryExchangeOnce(receipt,
                    new ResourceBundle().Add(pendingId, 1),
                    new ResourceBundle()))
                    return true;
            }
            catch
            {
                // Granted observers run after the durable wallet commit.
                if (!economy.Wallet.HasCompletedTransaction(receipt)) throw;
            }
            return economy.Wallet.HasCompletedTransaction(receipt);
        }

        private static bool ValidSourceId(string id) =>
            !string.IsNullOrWhiteSpace(id) && id.Length <= 160;

        // A gate outcome is no longer pending only after the randomly
        // selected, source-witnessed admiral has physically spawned.
        internal bool TryRecordRealGateAdmiralSpawn(string sourceSpawnEventId)
        {
            if (!ValidSourceId(sourceSpawnEventId)) return false;
            string receipt = "shared:original323:gate-admiral-spawn:" +
                sourceSpawnEventId;
            if (economy.Wallet.HasCompletedTransaction(receipt)) return true;
            try
            {
                if (economy.Wallet.TryExchangeOnce(receipt,
                    new ResourceBundle().Add(PendingGateAdmiralResource, 1),
                    new ResourceBundle()))
                    return true;
            }
            catch
            {
                // Transaction observers may throw after an irrevocable debit.
                // Trust the wallet receipt, never debit a second admiral.
                if (!economy.Wallet.HasCompletedTransaction(receipt)) throw;
            }
            return economy.Wallet.HasCompletedTransaction(receipt);
        }

        // Story deadlines and treasure collection have their own native
        // event witnesses. A building/boss mission must pass through a
        // source-observed enemy encounter instead of this public entrypoint.
        public bool TrySettle(Original323SharedMissionKind kind, string sourceEventId,
            int currentRound, string sourceEvidence)
        {
            return !Original323SharedObjectiveSpawnPlan.RequiresCombatDefeat(kind) &&
                TrySettleVerified(kind, sourceEventId, currentRound, sourceEvidence);
        }

        internal bool TrySettleDefeatedObjective(
            Original323SharedObjectiveEncounter encounter, int currentRound)
        {
            return encounter != null && encounter.Plan != null &&
                encounter.State == Original323SharedObjectiveState.DefeatedUnverified &&
                Original323SharedObjectiveSpawnPlan.RequiresCombatDefeat(
                    encounter.Plan.Mission) &&
                !string.IsNullOrWhiteSpace(encounter.ConfirmedDefeatEventId) &&
                !string.IsNullOrWhiteSpace(encounter.ConfirmedDefeatEvidence) &&
                TrySettleVerified(encounter.Plan.Mission,
                    encounter.ConfirmedDefeatEventId, currentRound,
                    encounter.ConfirmedDefeatEvidence);
        }

        private bool TrySettleVerified(Original323SharedMissionKind kind,
            string sourceEventId, int currentRound, string sourceEvidence)
        {
            if (combat.Game.Phase != SessionPhase.Running ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(sourceEvidence) ||
                !missions.CanRecordVerifiedSharedMission(kind, sourceEventId, currentRound) ||
                (kind == Original323SharedMissionKind.MarineAdmiral &&
                 !missions.IsSharedMissionCompleted(
                     Original323SharedMissionKind.JusticeGate)))
                return false;

            // Shared missions credit the original fZ excavation stack;
            // BhS alone draws an item. Boss spawn and admiral-specific buff
            // remain pending until their source-world events are observed.
            var rewards = new ResourceBundle();
            bool excavationDue = false;
            switch (kind)
            {
                case Original323SharedMissionKind.RumbarSouls:
                    rewards.Add("gold", 3000).Add("wood", 1);
                    excavationDue = true;
                    break;
                case Original323SharedMissionKind.JusticeGate:
                    rewards.Add("gold", 20000).Add(WispResourceIds.Choice, 2)
                        .Add(PendingGateAdmiralResource, 1);
                    excavationDue = true;
                    break;
                case Original323SharedMissionKind.ShandoraBell:
                    rewards.Add("gold", 3000).Add(WispResourceIds.Random, 1);
                    excavationDue = true;
                    break;
                case Original323SharedMissionKind.SeaKing:
                    // CNU calls BdL(EK,1) in the very same verified completion.
                    // Share this mission receipt with the other awards so a
                    // replay cannot credit the same charge independently.
                    rewards.Add("gold", 3000).Add(WispResourceIds.Choice, 1)
                        .Add(Original323OtherworldGamblingRules.ChargesResource,
                            Original323OtherworldGamblingRules.ChargesPerBdLEvent(
                                economy.Wallet.Balance(SourceModelGamblingNavigation.NavigationResource) ==
                                    (int)Original323GamblingNavigation.Casino));
                    AddLiveHeroExperience(rewards, 325);
                    excavationDue = true;
                    break;
                case Original323SharedMissionKind.FastAlabasta:
                    rewards.Add(WispResourceIds.Random, 1);
                    break;
                case Original323SharedMissionKind.DestructionKing:
                    rewards.Add("wood", 2);
                    excavationDue = true;
                    break;
                case Original323SharedMissionKind.MarineAdmiral:
                    rewards.Add("wood", 1)
                        .Add(OriginalMissionWalletRewards.TraitPointsId, 1)
                        .Add(PendingAdmiralBuffResource, 1);
                    AddLiveHeroExperience(rewards, 600);
                    break;
                case Original323SharedMissionKind.TreasureHunter:
                    rewards.Add("wood", 2);
                    break;
                default: return false;
            }

            // Bhd caps fZ at 10. A capped charge is omitted without
            // dropping the OTHER rewards from the same mission receipt.
            if (excavationDue && OrdinaryExcavationStock <
                SourceModelItemExcavation.MaximumStacks)
                rewards.Add(ExcavationStackResource, 1);

            string receipt = ReceiptPrefix + kind;
            bool wasPaid = economy.Wallet.HasCompletedTransaction(receipt);
            if (!wasPaid && kind == Original323SharedMissionKind.ShandoraBell)
            {
                // CM5: the extra AI01 roll is independent of story-item rolls.
                // Replayed callbacks never roll twice; BhZ forbids duplicates.
                int roll = scriptRandom.NextInclusive(1, 100);
                if (roll < 1 || roll > 100)
                    throw new InvalidOperationException("황금종 BFi(1,100) 결과 범위 오류");
                if (roll <= 5 && !Original323FusionSourceItemLedger.WasEverObtained(
                    economy.Wallet, Original323FusionSourceItemLedger.NikaItemRawcode))
                {
                    rewards.Add(Original323FusionSourceItemLedger.WalletId(
                        Original323FusionSourceItemLedger.NikaItemRawcode), 1);
                    rewards.Add(Original323FusionSourceItemLedger.EverObtainedWalletId(
                        Original323FusionSourceItemLedger.NikaItemRawcode), 1);
                }
            }

            try
            {
                if (!economy.Wallet.TryGrantOnce(receipt, rewards) &&
                    !economy.Wallet.HasCompletedTransaction(receipt))
                    return false;
            }
            catch
            {
                // Wallet observers run after committing; a throwing observer
                // must not roll a second item or lose the completion count.
                if (!economy.Wallet.HasCompletedTransaction(receipt)) throw;
            }
            if (!missions.RecordVerifiedSharedMission(kind, sourceEventId,
                currentRound, sourceEvidence))
                throw new InvalidOperationException(
                    "공유 미션 재화 지급 후 완료 영수증 연결 실패: " + kind);
            return true;
        }

        private void AddLiveHeroExperience(ResourceBundle bundle, int amount)
        {
            foreach (var owned in combat.MobileUnitSnapshot())
            {
                if (owned.Unit == null || owned.Unit.CatalogEntryKind == "support-object")
                    continue;
                if (!Original323MobileStoryHeroExperienceWorld.IsSourceStoryHero(
                    originals.Find(owned.Unit.Id)))
                    continue;
                bundle.Add(Original323MobileStoryHeroExperienceWorld.ExperienceWalletId(
                    owned.InstanceId), amount);
            }
        }
    }
}
