using System;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // A mobile-local treasure draft reconstructed ONLY in a new unpublished
    // session. It does not attempt a native Warcraft A05D cast, BFi draw,
    // bounty map spawn or reconciliation of an already committed chest.
    public sealed class OrdrUnpublishedTreasureRestoreGraph
    {
        public Original323GaimonDreamState Dream { get; private set; }
        public Original323MobileTreasureSession Mobile { get; private set; }
        public Original323TreasureWalletWorld Wallet { get; private set; }
        public Original323TreasureHunt Hunt { get; private set; }
        public MobileTreasureGacha Gacha { get; private set; }
        public Original323TreasureAlarmJournal Alarms { get; private set; }
        public Original323GaimonRevealPort Reveals { get; private set; }
        public Original323BountyTreasureWorld Bounty { get; private set; }

        internal OrdrUnpublishedTreasureRestoreGraph(
            Original323GaimonDreamState dream,
            Original323MobileTreasureSession mobile,
            Original323TreasureWalletWorld wallet,
            Original323TreasureHunt hunt, MobileTreasureGacha gacha,
            Original323TreasureAlarmJournal alarms,
            Original323GaimonRevealPort reveals,
            Original323BountyTreasureWorld bounty)
        {
            Dream = dream; Mobile = mobile; Wallet = wallet;
            Hunt = hunt; Gacha = gacha; Alarms = alarms;
            Reveals = reveals; Bounty = bounty;
        }

        public Original323TreasureSessionState CaptureState()
        {
            var snapshot = Dream.Capture();
            return new Original323TreasureSessionState {
                Hunt = Hunt.CaptureState(),
                Mobile = Mobile.CaptureState(),
                Gacha = Gacha.CaptureState(),
                Alarms = Alarms.CaptureState(),
                Reveals = Reveals?.CaptureState(),
                DreamApplied = snapshot.Applied,
                NamiAtDream = snapshot.NamiBonusWhenApplied
            };
        }
    }

    public static class OrdrTreasureRestoreGraphBuilder
    {
        private const string BountyInitialReceipt =
            "source:bounty:CRW:LV-initial-5";

        // Only the already-certified mobile substitute has portable BFj
        // positions/attempt IDs in this schema. Native ability, native
        // treasure map and unfinished bounty obligations are NOT replayable.
        public static void ValidatePrerequisites(
            OrdrCoreCheckpointState save, EconomyWallet wallet)
        {
            if (save == null || wallet == null)
                throw new ArgumentNullException(
                    save == null ? nameof(save) : nameof(wallet));
            var t = save.Treasure;
            if (t == null) return;
            if (save.Combat == null || save.ScriptRandom == null ||
                save.OriginalScriptRandom != null || t.Hunt == null ||
                t.Mobile == null || t.Gacha == null || t.Alarms == null)
                throw new NotSupportedException(
                    "보물의 모바일 난수와 탐색/알람/영수증을 함께 복원해야 합니다.");
            if (!t.DreamApplied && t.NamiAtDream)
                throw new FormatException(
                    "가이몬 꿈이 없는 상태에서 나미 보너스가 적용됐습니다.");
            Original323TreasureHunt.ValidateState(t.Hunt, wallet);
            Original323MobileTreasureSession.ValidateState(t.Mobile, t.Hunt);
            MobileTreasureGacha.ValidateState(t.Gacha, t.Mobile);
            Original323TreasureAlarmJournal.ValidateState(t.Alarms);
            if (t.Reveals != null)
                Original323GaimonRevealPort.ValidateState(t.Reveals, t.Hunt);
            if (!wallet.HasCompletedTransaction(BountyInitialReceipt) ||
                wallet.Balance(Original323BountyTreasureWorld.NativeAbilitiesPendingId) > 0)
                throw new NotSupportedException(
                    "바운티헌터 초기 지도/원피스 원본 후속 영수증이 미확정입니다.");
        }

        public static OrdrUnpublishedTreasureRestoreGraph Prepare(
            OrdrCoreCheckpointState save,
            OrdrUnpublishedCombatRestoreGraph combat,
            OrdrUnpublishedCommerceRestoreGraph commerce)
        {
            if (save == null || combat == null || combat.Core == null ||
                commerce == null)
                throw new ArgumentNullException(
                    "새 보물 세계에는 같은 공유 지갑·난수·전투가 필요합니다.");
            var shared = combat.Core.Economy.Wallet;
            ValidatePrerequisites(save, shared);
            if (save.Treasure == null) return null;
            if (commerce.ScriptRandom == null ||
                commerce.OriginalScriptRandom != null)
                throw new NotSupportedException(
                    "보물 모바일 BFj 난수는 저장된 모바일 공통 난수열이 필요합니다.");

            var state = save.Treasure;
            var dream = new Original323GaimonDreamState();
            dream.Restore(new Original323GaimonDreamSnapshot(
                state.DreamApplied, state.NamiAtDream));
            var mobile = new Original323MobileTreasureSession(
                commerce.ScriptRandom, () => dream.Applied,
                () => dream.NamiBonusWhenApplied);
            var wallet = new Original323TreasureWalletWorld(shared, mobile);
            var hunt = new Original323TreasureHunt(wallet,
                commerce.ScriptRandom);
            // No spawn, search, cast, BFi draw or TrySettle is performed.
            // Hunt recovery rehydrates only verified location/receipt owners.
            hunt.RestoreState(state.Hunt, shared);
            mobile.RestoreState(state.Mobile, state.Hunt);
            var gacha = new MobileTreasureGacha(hunt, mobile);
            gacha.RestoreState(state.Gacha, state.Mobile);
            var alarms = new Original323TreasureAlarmJournal();
            alarms.RestoreState(state.Alarms);
            Original323GaimonRevealPort reveals = null;
            if (state.Reveals != null)
            {
                reveals = new Original323GaimonRevealPort(hunt, mobile);
                reveals.RestoreState(state.Reveals, state.Hunt);
            }
            // This existing constructor is receipt-idempotent, but never
            // allow it to issue the initial 5-map award on restore.
            // The prerequisite above verifies that receipt first.
            var bounty = new Original323BountyTreasureWorld(shared,
                combat.Core.Game, hunt, mobile);
            var result = new OrdrUnpublishedTreasureRestoreGraph(dream,
                mobile, wallet, hunt, gacha, alarms, reveals, bounty);
            OrdrWorldRestoreGraphBuilder.AssertSameState(
                "보물/탐색권/알람", state, result.CaptureState());
            return result;
        }
    }
}
