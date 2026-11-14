using System;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Core
{
    // S01 core ledger contract only. NEVER expose this as a resumable game:
    // combat actors, enemies, timers, RNG and world services are not here yet.
    [Serializable]
    public sealed class OrdrCoreCheckpointState
    {
        public int Version = 1;
        public string SourceVersion = OrdrCoreCheckpoint.SourceVersion;
        public string SourceW3xSha256 = OrdrCoreCheckpoint.SourceW3xSha256;
        public string ModeId = GameSession.ModeId;
        public string SessionId;
        public double LogicalTime;
        public GameSessionState Game;
        public UnitRosterState Roster;
        // Same wallet, event receipts and mobile RNG stream as live gameplay.
        public PersonalMissionGameplayState MissionGameplay;
    }

    public sealed class OrdrRestoredCoreCheckpoint
    {
        public GameSession Game { get; private set; }
        public SharedEconomySession Economy { get; private set; }
        public PersonalMissionGameplay MissionGameplay { get; private set; }
        public OriginalPersonalMissionSession Missions => MissionGameplay.Missions;
        public double LogicalTime { get; private set; }

        internal OrdrRestoredCoreCheckpoint(GameSession game,
            SharedEconomySession economy, PersonalMissionGameplay missions,
            double logicalTime)
        {
            Game = game; Economy = economy; MissionGameplay = missions;
            LogicalTime = logicalTime;
        }
    }

    public static class OrdrCoreCheckpoint
    {
        public const string SourceVersion = "ORDR_S2_2.323[C]";
        public const string SourceW3xSha256 =
            "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c";

        public static OrdrCoreCheckpointState Capture(GameSession game,
            SharedEconomySession economy, PersonalMissionGameplay missions,
            double logicalTime)
        {
            if (game == null || economy == null || missions == null ||
                !economy.HasStarted || game.Phase == SessionPhase.NotStarted ||
                double.IsNaN(logicalTime) || double.IsInfinity(logicalTime) ||
                logicalTime < 0)
                throw new InvalidOperationException(
                    "진행 중인 세션과 유효한 논리 시계가 필요합니다.");
            var state = new OrdrCoreCheckpointState {
                SessionId = missions.Missions.SessionId, LogicalTime = logicalTime,
                Game = game.CaptureState(), Roster = economy.Units.CaptureState(),
                MissionGameplay = missions.CaptureState()
            };
            // The mission gameplay component carries the SAME shared wallet.
            // A different wallet or a torn cross-service snapshot is rejected.
            if (!SameWallet(economy.Wallet.CaptureState(),
                    state.MissionGameplay.Wallet))
                throw new InvalidOperationException(
                    "경제와 개인미션 지갑이 같은 저장 시점을 사용하지 않습니다.");
            Validate(state);
            return state;
        }

        private static bool SameWallet(EconomyWalletState a, EconomyWalletState b)
        {
            if (a == null || b == null || a.Balances == null ||
                b.Balances == null || a.CompletedRewards == null ||
                b.CompletedRewards == null || a.PendingWispRewards == null ||
                b.PendingWispRewards == null ||
                a.Balances.Count != b.Balances.Count ||
                a.CompletedRewards.Count != b.CompletedRewards.Count ||
                a.PendingWispRewards.Count != b.PendingWispRewards.Count)
                return false;
            for (int i = 0; i < a.Balances.Count; i++)
                if (a.Balances[i].Id != b.Balances[i].Id ||
                    a.Balances[i].Amount != b.Balances[i].Amount)
                    return false;
            for (int i = 0; i < a.CompletedRewards.Count; i++)
                if (a.CompletedRewards[i] != b.CompletedRewards[i]) return false;
            for (int i = 0; i < a.PendingWispRewards.Count; i++)
                if (a.PendingWispRewards[i].SourceWispId !=
                        b.PendingWispRewards[i].SourceWispId ||
                    a.PendingWispRewards[i].UnitRawcode !=
                        b.PendingWispRewards[i].UnitRawcode ||
                    a.PendingWispRewards[i].BonusAbilityRawcode !=
                        b.PendingWispRewards[i].BonusAbilityRawcode)
                    return false;
            return true;
        }

        // A dry validation returns a new object graph, never mutating the
        // running game, and never paying start/mission rewards on recovery.
        public static void Validate(OrdrCoreCheckpointState state)
        {
            Restore(state);
        }

        public static OrdrRestoredCoreCheckpoint Restore(
            OrdrCoreCheckpointState state)
        {
            if (state == null || state.Version != 1 ||
                state.SourceVersion != SourceVersion ||
                state.SourceW3xSha256 != SourceW3xSha256 ||
                state.ModeId != GameSession.ModeId ||
                string.IsNullOrWhiteSpace(state.SessionId) ||
                double.IsNaN(state.LogicalTime) ||
                double.IsInfinity(state.LogicalTime) ||
                state.LogicalTime < 0 ||
                state.Game == null || state.Roster == null ||
                state.MissionGameplay == null ||
                state.MissionGameplay.Missions == null ||
                state.MissionGameplay.Missions.SessionId != state.SessionId ||
                !state.MissionGameplay.Missions.Otherworld)
                throw new FormatException(
                    "원본 버전·세션 ID·게임 상태를 확인할 수 없습니다.");

            var game = GameSession.Restore(state.Game);
            if (game.Phase == SessionPhase.NotStarted ||
                state.MissionGameplay.Missions.CurrentRound != game.CurrentWave)
                throw new FormatException(
                    "라운드와 개인미션 진행 상태가 서로 다릅니다.");
            // First restore all independent economic state. Neither the new
            // wallet nor the new roster is published to gameplay until every
            // component validates successfully.
            var economy = SharedEconomySession.RestoreStarted(
                state.MissionGameplay.Wallet, state.Roster);
            var missions = PersonalMissionGameplay.Restore(
                game, economy.Wallet, state.MissionGameplay);
            return new OrdrRestoredCoreCheckpoint(game, economy, missions,
                state.LogicalTime);
        }
    }
}
