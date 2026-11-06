using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    // Separate from h06Q/h06R upgrades: CV9 -> CV8 grants these auras on
    // RESEARCH_START. Original completion/cancellation/refund is not witnessed.
    public enum Original323ResearchStartStatus
    {
        Started, AlreadyStarted, UnknownResearch, NotRunning,
        InsufficientResources, EffectUnavailable, TransactionDeclined,
        InconsistentReceipt
    }

    public sealed class Original323ResearchQuote
    {
        public string Rawcode { get; }
        public int Level { get; }
        public ResourceBundle Cost { get; }
        public Original323ResearchStartStatus Status { get; }
        public bool CanStart => Status == Original323ResearchStartStatus.Started;

        internal Original323ResearchQuote(string rawcode, int level,
            ResourceBundle cost, Original323ResearchStartStatus status)
        {
            Rawcode = rawcode;
            Level = level;
            Cost = cost;
            Status = status;
        }
    }

    [Serializable]
    public sealed class Original323PlayerResearchState
    {
        public int Version = 1;
        public List<string> Started = new List<string>();
    }

    // Models the witnessed start event only. Start is an atomic one-time
    // debit + permanent practice aura entitlement. Do not pretend that
    // SLK timebase=100 proves a 100-second completion or a refund rule.
    public sealed class Original323PlayerResearchSession
    {
        private readonly EconomyWallet wallet;
        private readonly GameSession game;
        private readonly Func<string, bool> canApply;
        private readonly HashSet<string> started =
            new HashSet<string>(StringComparer.Ordinal);
        private bool starting;

        public Original323PlayerResearchSession(EconomyWallet wallet,
            GameSession game, Func<string, bool> canApply)
        {
            this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            this.game = game ?? throw new ArgumentNullException(nameof(game));
            this.canApply = canApply ?? throw new ArgumentNullException(nameof(canApply));
        }

        private static bool Known(string rawcode)
        {
            return rawcode == "Rhgb" || rawcode == "Rhra" ||
                rawcode == "Rhpt" || rawcode == "Rhfc";
        }

        // Fixed from source h06S UpgradeData.slk at level 1; no invented
        // money price, automatic completion, repeated purchase or refund.
        public static ResourceBundle SourceCost(string rawcode)
        {
            switch (rawcode)
            {
                case "Rhgb": return new ResourceBundle().Add("gold", 25000).Add("wood", 2);
                case "Rhra": return new ResourceBundle().Add("gold", 15000).Add("wood", 1);
                case "Rhpt": return new ResourceBundle().Add("gold", 25000).Add("wood", 3);
                case "Rhfc": return new ResourceBundle().Add("gold", 15000).Add("wood", 2);
                default: return null;
            }
        }

        private static string Receipt(string rawcode) =>
            "original323-research-start:" + rawcode + ":1";

        public bool HasStarted(string rawcode) =>
            rawcode != null && started.Contains(rawcode);

        public Original323ResearchQuote Quote(string rawcode)
        {
            if (!Known(rawcode))
                return new Original323ResearchQuote(rawcode, 0, null,
                    Original323ResearchStartStatus.UnknownResearch);
            if (started.Contains(rawcode))
                return new Original323ResearchQuote(rawcode, 1, null,
                    Original323ResearchStartStatus.AlreadyStarted);
            var cost = SourceCost(rawcode);
            if (wallet.HasCompletedTransaction(Receipt(rawcode)))
                return new Original323ResearchQuote(rawcode, 0, cost,
                    Original323ResearchStartStatus.InconsistentReceipt);
            if (!canApply(rawcode))
                return new Original323ResearchQuote(rawcode, 0, cost,
                    Original323ResearchStartStatus.EffectUnavailable);
            if (game.Phase != SessionPhase.Running)
                return new Original323ResearchQuote(rawcode, 0, cost,
                    Original323ResearchStartStatus.NotRunning);
            foreach (var entry in cost.Entries)
                if (wallet.Balance(entry.Key) < entry.Value)
                    return new Original323ResearchQuote(rawcode, 0, cost,
                        Original323ResearchStartStatus.InsufficientResources);
            return new Original323ResearchQuote(rawcode, 0, cost,
                Original323ResearchStartStatus.Started);
        }

        public Original323ResearchStartStatus Start(string rawcode)
        {
            if (starting) return Original323ResearchStartStatus.TransactionDeclined;
            var quote = Quote(rawcode);
            if (!quote.CanStart) return quote.Status;
            var walletBefore = wallet.CaptureState();
            starting = true;
            try
            {
                if (!wallet.TryExchangeOnce(Receipt(rawcode), quote.Cost,
                        new ResourceBundle()))
                    return Original323ResearchStartStatus.TransactionDeclined;
                if (!started.Add(rawcode))
                    throw new InvalidOperationException("중복 연구 시작");
                return Original323ResearchStartStatus.Started;
            }
            catch
            {
                started.Remove(rawcode);
                wallet.RestoreState(walletBefore);
                throw;
            }
            finally { starting = false; }
        }

        // Rollback/S01 contract only; caller must capture/restore the shared
        // wallet in the same checkpoint. No standalone save-to-disk is claimed.
        public Original323PlayerResearchState CaptureState()
        {
            var state = new Original323PlayerResearchState();
            state.Started.AddRange(started);
            state.Started.Sort(StringComparer.Ordinal);
            return state;
        }

        public void RestoreState(Original323PlayerResearchState state)
        {
            if (state == null || state.Version != 1 || state.Started == null)
                throw new ArgumentException("연구 시작 저장 상태가 잘못됐습니다.");
            var incoming = new HashSet<string>(StringComparer.Ordinal);
            foreach (string rawcode in state.Started)
                if (!Known(rawcode) || !incoming.Add(rawcode) ||
                    !wallet.HasCompletedTransaction(Receipt(rawcode)))
                    throw new FormatException("연구 시작 상태와 공유 지갑 영수증이 다릅니다.");
            started.Clear();
            foreach (string rawcode in incoming) started.Add(rawcode);
        }
    }
}
