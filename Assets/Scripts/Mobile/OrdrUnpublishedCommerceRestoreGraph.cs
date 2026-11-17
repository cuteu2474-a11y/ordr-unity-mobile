using System;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // This is a reconstructed, UNPUBLISHED commerce graph. No live UI, reward
    // callback or existing game session is replaced by this object.
    // Native ability receipts, original navigation selection, all additional
    // RNG streams and source-side inventories still need their own restore
    // contracts before a complete playable Continue may be exposed.
    public sealed class OrdrUnpublishedCommerceRestoreGraph
    {
        // Exclusive sources: fallback replay history OR original observed
        // shared H6. Original draws use purpose-labelled views of one owner.
        public MobileRollbackWispRandom ScriptRandom { get; private set; }
        public Original323SharedBfiRandom OriginalScriptRandom { get; private set; }
        public IOriginalWispRandom GamblingRandom { get; private set; }
        public IOriginalWispRandom RareBranchRandom { get; private set; }
        public MobileRollbackWispRandom WispPoolRandom { get; private set; }
        public MobileRollbackWispRandom GraduatedRarePoolRandom { get; private set; }
        public MobileRollbackWispRandom TierPoolRandom { get; private set; }
        public SourceModelMoneyGamblingDesk MoneyDesk { get; private set; }
        public SourceModelMaterialSupport MaterialSupport { get; private set; }
        public SourceModelUnitGambling UnitTiers { get; private set; }
        public SourceModelOtherworldUnitGambling Otherworld { get; private set; }
        public SourceModelUnitReroll Reroll { get; private set; }

        // Consumers of the unpublished auxiliary world must reuse this
        // exact BFi owner. For original mode each purpose is a labelled
        // view of ONE global cursor; no service may restore a second seed.
        public IOriginalWispRandom RandomForPurpose(
            Original323ScriptRandomPurpose purpose)
        {
            if (!Enum.IsDefined(typeof(Original323ScriptRandomPurpose), purpose))
                throw new ArgumentOutOfRangeException(nameof(purpose));
            return OriginalScriptRandom == null ? (IOriginalWispRandom)ScriptRandom :
                OriginalScriptRandom.ForPurpose(purpose);
        }

        internal OrdrUnpublishedCommerceRestoreGraph(
            MobileRollbackWispRandom script, MobileRollbackWispRandom wisp,
            MobileRollbackWispRandom graduatedRare, MobileRollbackWispRandom tier,
            SourceModelMoneyGamblingDesk desk, SourceModelMaterialSupport support,
            SourceModelUnitGambling tiers,
            SourceModelOtherworldUnitGambling otherworld,
            SourceModelUnitReroll reroll,
            Original323SharedBfiRandom observedOriginal,
            IOriginalWispRandom gamblingRandom,
            IOriginalWispRandom rareBranchRandom)
        {
            ScriptRandom = script;
            OriginalScriptRandom = observedOriginal;
            GamblingRandom = gamblingRandom;
            RareBranchRandom = rareBranchRandom;
            WispPoolRandom = wisp;
            GraduatedRarePoolRandom = graduatedRare;
            TierPoolRandom = tier;
            MoneyDesk = desk;
            MaterialSupport = support;
            UnitTiers = tiers;
            Otherworld = otherworld;
            Reroll = reroll;
        }
    }

    public static class OrdrCommerceRestoreGraphBuilder
    {
        // Require the whole source commerce family. A partially missing
        // wallet/draw/research/wisp ledger must not become an empty, free shop.
        // Public for isolated validation and corrupt-save regression checks.
        public static void ValidateCommerceDraft(OrdrCoreCheckpointState save)
        {
            OrdrCoreCheckpoint.Validate(save);
            if (save.Combat == null ||
                (save.ScriptRandom == null && save.OriginalScriptRandom == null) ||
                (save.ScriptRandom != null && save.OriginalScriptRandom != null) ||
                save.WispPoolRandom == null ||
                save.GraduatedRarePoolRandom == null ||
                save.TierPoolRandom == null || save.MoneyDesk == null ||
                save.MoneyDesk.MaterialSupport == null ||
                save.UnitGambling == null ||
                save.OtherworldGambling == null || save.UnitReroll == null)
                throw new NotSupportedException(
                    "원본 거래·공통 BFi·네이티브 난수/보류 추첨 중 누락이 있습니다.");
            if (save.ScriptRandom != null)
                MobileRollbackWispRandom.Restore(save.ScriptRandom);
            else
                Original323SharedBfiRandom.ValidateCheckpoint(
                    save.OriginalScriptRandom);
            MobileRollbackWispRandom.Restore(save.WispPoolRandom);
            MobileRollbackWispRandom.Restore(save.GraduatedRarePoolRandom);
            MobileRollbackWispRandom.Restore(save.TierPoolRandom);
            // A pending purchase cannot be resumed in a different casino/
            // exchange navigation: no re-draw or re-debit is permitted.
            var navigation = Original323GamblingNavigation.None;
            foreach (var balance in save.MissionGameplay.Wallet.Balances)
                if (balance.Id == SourceModelGamblingNavigation.NavigationResource)
                    navigation = (Original323GamblingNavigation)balance.Amount;
            if (!Enum.IsDefined(typeof(Original323GamblingNavigation),
                    navigation) ||
                (save.UnitGambling.Pending != null &&
                    save.UnitGambling.Pending.Navigation != navigation) ||
                (save.OtherworldGambling.Pending != null &&
                    save.OtherworldGambling.Pending.Navigation != navigation) ||
                (save.UnitReroll.PendingActor != 0 &&
                    save.UnitReroll.PendingNavigation != navigation))
                throw new FormatException(
                    "보류 중인 유닛 거래와 공유 지갑의 도박소 항법이 다릅니다.");
        }

        // Factories receive ONLY the newly reconstructed combat owner. They
        // must create fresh source-checked reward ports for this exact world;
        // caller-owned ports from a running game are never reused.
        //
        // All side effects remain private until the higher-level publisher
        // has reattached navigation, native abilities, missions and the rest
        // of the game world. This builder deliberately never starts a game.
        public static OrdrUnpublishedCommerceRestoreGraph Prepare(
            OrdrUnpublishedCombatRestoreGraph graph,
            OrdrCoreCheckpointState save,
            Func<CharacterCombatRuntime, Original323WispDeferredAbilityRegistry> createAbilities,
            Func<CharacterCombatRuntime, Original323WispDeferredAbilityRegistry,
                Original323UnitGamblingWorld> createUnitWorld,
            Func<CharacterCombatRuntime, Original323WispDeferredAbilityRegistry,
                IOriginal323OtherworldGamblingWorld> createOtherworldWorld,
            Func<CharacterCombatRuntime, IOriginalWispRandom, IOriginalWispRandom,
                Original323WispDeferredAbilityRegistry,
                IOriginal323GraduatedRareShopWorld> createRareWorld,
            Func<bool> interactionGate,
            Func<Original323GamblingNavigation> selectedNavigation,
            Func<bool> hasStageFiveResearch,
            Action<string, OriginalGambleTier, bool> missionCommit = null,
            IOriginal323SharedBfiRestoreWitness originalStateWitness = null,
            IOriginal323ScriptRandomDrawWitness originalDrawWitness = null)
        {
            if (graph == null || graph.Core == null || graph.Combat == null ||
                createAbilities == null || createUnitWorld == null ||
                createOtherworldWorld == null || createRareWorld == null ||
                interactionGate == null || selectedNavigation == null ||
                hasStageFiveResearch == null)
                throw new ArgumentNullException(
                    "복원된 전투·원본 보상 월드 생성기·항법·거래 조건이 모두 필요합니다.");
            ValidateCommerceDraft(save);
            var wallet = graph.Core.Economy.Wallet;
            var game = graph.Core.Game;
            var expectedNavigation = (Original323GamblingNavigation)
                wallet.Balance(SourceModelGamblingNavigation.NavigationResource);
            if (!Enum.IsDefined(typeof(Original323GamblingNavigation),
                    expectedNavigation) || selectedNavigation() != expectedNavigation)
                throw new FormatException(
                    "저장된 공유 지갑 항법과 새 게임 항법 상태가 다릅니다.");

            // Separate replay streams: BFi and native pool selection must not
            // share a cursor, including after a partially completed draw.
            var script = save.ScriptRandom == null ? null :
                MobileRollbackWispRandom.Restore(save.ScriptRandom);
            var original = save.OriginalScriptRandom == null ? null :
                Original323SharedBfiRandom.RestoreCheckpoint(
                    save.OriginalScriptRandom, save.SessionId,
                    save.LogicalTime, originalStateWitness, originalDrawWitness);
            IOriginalWispRandom gamblingRandom = original == null ?
                (IOriginalWispRandom)script :
                original.ForPurpose(Original323ScriptRandomPurpose.Gambling);
            IOriginalWispRandom rareBranchRandom = original == null ?
                (IOriginalWispRandom)script :
                original.ForPurpose(Original323ScriptRandomPurpose.RareWispBranch);
            var wisp = MobileRollbackWispRandom.Restore(save.WispPoolRandom);
            var graduatedRare = MobileRollbackWispRandom.Restore(
                save.GraduatedRarePoolRandom);
            var tier = MobileRollbackWispRandom.Restore(save.TierPoolRandom);
            var abilities = createAbilities(graph.Combat) ??
                throw new InvalidOperationException("원본 스킬 능력 장부가 누락됐습니다.");
            var unitWorld = createUnitWorld(graph.Combat, abilities) ??
                throw new InvalidOperationException("원본 유닛 도박 보상 월드가 누락됐습니다.");
            var otherworldWorld = createOtherworldWorld(graph.Combat, abilities) ??
                throw new InvalidOperationException("이세계 도박 실물 월드가 누락됐습니다.");
            var rareWorld = createRareWorld(graph.Combat, rareBranchRandom, graduatedRare,
                abilities) ??
                throw new InvalidOperationException("졸업 상점 원본 유닛 월드가 누락됐습니다.");

            var money = new SourceModelMoneyGamblingDesk(wallet, game,
                gamblingRandom, () => graph.Combat.Time, interactionGate);
            var support = new SourceModelMaterialSupport(wallet, game,
                gamblingRandom, unitWorld, interactionGate);
            money.ConfigureMaterialSupport(support);
            money.ConfigureRareReward(rareWorld, hasStageFiveResearch);
            var tiers = new SourceModelUnitGambling(wallet, game, gamblingRandom,
                tier, unitWorld, interactionGate, selectedNavigation,
                missionCommit);
            money.ConfigureUnitTiers(tiers);
            var otherworld = new SourceModelOtherworldUnitGambling(wallet,
                game, gamblingRandom, interactionGate, otherworldWorld, selectedNavigation);
            money.ConfigureOtherworld(otherworld);
            var reroll = new SourceModelUnitReroll(graph.Combat, wallet,
                unitWorld, abilities, gamblingRandom, interactionGate, selectedNavigation);

            // These do not consume a new roll, regrant a receipt, emit an
            // event or spawn a new character. Any mismatch abandons the
            // entire fresh unpublished graph; the live game is unchanged.
            money.RestoreState(save.MoneyDesk);
            tiers.RestoreState(save.UnitGambling);
            otherworld.RestoreState(save.OtherworldGambling);
            reroll.RestoreState(save.UnitReroll);
            return new OrdrUnpublishedCommerceRestoreGraph(script, wisp,
                graduatedRare, tier, money, support, tiers, otherworld, reroll,
                original, gamblingRandom, rareBranchRandom);
        }
    }
}
