using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    // A source-approved level contains BOTH a witnessed original debit and a
    // witnessed combat effect. A tooltip or maximum level alone is not approval.
    public sealed class Original323VerifiedUpgradeStep
    {
        public string Rawcode { get; }
        public int Level { get; }
        private readonly ResourceBundle cost;
        public ResourceBundle Cost
        {
            get
            {
                var copy = new ResourceBundle();
                foreach (var item in cost.Entries) copy.Add(item.Key, item.Value);
                return copy;
            }
        }
        public string CostEvidence { get; }
        public string EffectEvidence { get; }
        public string EffectSummary { get; }
        public double CooldownSeconds { get; }

        public Original323VerifiedUpgradeStep(string rawcode, int level,
            ResourceBundle cost, string costEvidence, string effectEvidence,
            string effectSummary, double cooldownSeconds = 0)
        {
            if (string.IsNullOrWhiteSpace(rawcode) || level <= 0 ||
                cost == null || string.IsNullOrWhiteSpace(costEvidence) ||
                string.IsNullOrWhiteSpace(effectEvidence) ||
                string.IsNullOrWhiteSpace(effectSummary) ||
                double.IsNaN(cooldownSeconds) || double.IsInfinity(cooldownSeconds) ||
                cooldownSeconds < 0)
                throw new ArgumentException("원본 강화 단계의 비용과 실제 효과 근거가 필요합니다.");
            bool hasCost = false;
            foreach (var item in cost.Entries) { hasCost = true; break; }
            if (!hasCost) throw new ArgumentException("강화 비용이 비어 있습니다.");
            Rawcode = rawcode;
            Level = level;
            this.cost = new ResourceBundle();
            foreach (var item in cost.Entries) this.cost.Add(item.Key, item.Value);
            CostEvidence = costEvidence;
            EffectEvidence = effectEvidence;
            EffectSummary = effectSummary;
            CooldownSeconds = cooldownSeconds;
        }
    }

    // Implementations must connect the original upgrade's effect to LIVE
    // attacks, skills and owned units, including units spawned after purchase.
    // TryApply must be atomic; Undo restores the previous level after failure.
    public interface IOriginal323VerifiedUpgradeWorld
    {
        bool CanApply(Original323VerifiedUpgradeStep step);
        bool TryApply(Original323VerifiedUpgradeStep step, int previousLevel);
        void Undo(Original323VerifiedUpgradeStep step, int previousLevel);
    }

    public enum Original323UpgradePurchaseStatus
    {
        Succeeded, UnknownUpgrade, NotRunning, MaximumLevel,
        SourceMissing, WorldMissing, Cooldown, InsufficientResources,
        TransactionDeclined, EffectRejected
    }

    public sealed class Original323UpgradeQuote
    {
        public string Rawcode { get; }
        public int Level { get; }
        public int MaximumLevel { get; }
        public Original323VerifiedUpgradeStep NextStep { get; }
        public Original323UpgradePurchaseStatus Status { get; }
        public double CooldownRemaining { get; }
        public bool CanBuy => Status == Original323UpgradePurchaseStatus.Succeeded;

        public Original323UpgradeQuote(string rawcode, int level, int maximumLevel,
            Original323VerifiedUpgradeStep step,
            Original323UpgradePurchaseStatus status, double remaining)
        {
            Rawcode = rawcode; Level = level; MaximumLevel = maximumLevel;
            NextStep = step; Status = status; CooldownRemaining = remaining;
        }
    }

    // Shared-wallet and level-limited upgrade transaction for BOTH h06Q/h06R.
    // Missing W3X cost/effect tables deliberately leave purchases unavailable.
    public sealed class Original323PlayerUpgradeSession
    {
        private readonly EconomyWallet wallet;
        private readonly GameSession game;
        private readonly Func<double> gameTime;
        private readonly Dictionary<string, Original323PlayerBuildingResearch> catalog =
            new Dictionary<string, Original323PlayerBuildingResearch>(StringComparer.Ordinal);
        private readonly Dictionary<string, Original323VerifiedUpgradeStep> steps =
            new Dictionary<string, Original323VerifiedUpgradeStep>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> levels =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> nextPurchaseAt =
            new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly IOriginal323VerifiedUpgradeWorld world;
        private bool purchasing;

        public Original323PlayerUpgradeSession(EconomyWallet wallet, GameSession game,
            Func<double> gameTime, IEnumerable<Original323VerifiedUpgradeStep> sourceSteps = null,
            IOriginal323VerifiedUpgradeWorld verifiedWorld = null)
        {
            this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            this.game = game ?? throw new ArgumentNullException(nameof(game));
            this.gameTime = gameTime ?? throw new ArgumentNullException(nameof(gameTime));
            world = verifiedWorld;
            foreach (var building in new[] {
                Original323PlayerBuildingResearchCatalog.FirstUpgradeBuilding,
                Original323PlayerBuildingResearchCatalog.SecondUpgradeBuilding })
                foreach (var item in Original323PlayerBuildingResearchCatalog.ForBuilding(building))
                    catalog.Add(item.Rawcode, item);
            if (sourceSteps != null)
                foreach (var step in sourceSteps) RegisterSourceStep(step);
        }

        public int LevelOf(string rawcode)
        {
            int value;
            return rawcode != null && levels.TryGetValue(rawcode, out value) ? value : 0;
        }

        public void RegisterSourceStep(Original323VerifiedUpgradeStep step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            Original323PlayerBuildingResearch item;
            if (!catalog.TryGetValue(step.Rawcode, out item) || step.Level > item.MaximumLevel)
                throw new ArgumentException("원본 강화소에 없는 강화 단계입니다.");
            string key = StepKey(step.Rawcode, step.Level);
            if (steps.ContainsKey(key)) throw new InvalidOperationException("중복 강화 단계");
            steps.Add(key, step);
        }

        public Original323UpgradeQuote Quote(string rawcode)
        {
            Original323PlayerBuildingResearch item;
            if (rawcode == null || !catalog.TryGetValue(rawcode, out item))
                return new Original323UpgradeQuote(rawcode, 0, 0, null,
                    Original323UpgradePurchaseStatus.UnknownUpgrade, 0);
            int level = LevelOf(rawcode);
            if (level >= item.MaximumLevel)
                return new Original323UpgradeQuote(rawcode, level, item.MaximumLevel, null,
                    Original323UpgradePurchaseStatus.MaximumLevel, 0);
            Original323VerifiedUpgradeStep step;
            steps.TryGetValue(StepKey(rawcode, level + 1), out step);
            var status = Original323UpgradePurchaseStatus.Succeeded;
            double remaining = 0;
            if (step == null) status = Original323UpgradePurchaseStatus.SourceMissing;
            else if (world == null || !world.CanApply(step))
                status = Original323UpgradePurchaseStatus.WorldMissing;
            else if (game.Phase != SessionPhase.Running)
                status = Original323UpgradePurchaseStatus.NotRunning;
            else
            {
                double deadline;
                if (nextPurchaseAt.TryGetValue(rawcode, out deadline))
                    remaining = Math.Max(0, deadline - gameTime());
                if (remaining > 0) status = Original323UpgradePurchaseStatus.Cooldown;
                else foreach (var cost in step.Cost.Entries)
                    if (wallet.Balance(cost.Key) < cost.Value)
                    {
                        status = Original323UpgradePurchaseStatus.InsufficientResources;
                        break;
                    }
            }
            return new Original323UpgradeQuote(rawcode, level, item.MaximumLevel, step,
                status, remaining);
        }

        public Original323UpgradePurchaseStatus Purchase(string rawcode)
        {
            if (purchasing) return Original323UpgradePurchaseStatus.TransactionDeclined;
            var quote = Quote(rawcode);
            if (!quote.CanBuy) return quote.Status;
            var step = quote.NextStep;
            string receipt = StepKey(rawcode, step.Level);
            if (wallet.HasCompletedTransaction(receipt))
                return Original323UpgradePurchaseStatus.TransactionDeclined;
            var before = wallet.CaptureState();
            purchasing = true;
            try
            {
                if (!wallet.TryExchangeOnce(receipt, step.Cost, new ResourceBundle()))
                    return Original323UpgradePurchaseStatus.TransactionDeclined;
                try
                {
                    if (!world.TryApply(step, quote.Level))
                    {
                        try { world.Undo(step, quote.Level); }
                        finally { wallet.RestoreState(before); }
                        return Original323UpgradePurchaseStatus.EffectRejected;
                    }
                }
                catch
                {
                    try { world.Undo(step, quote.Level); }
                    finally { wallet.RestoreState(before); }
                    throw;
                }
                levels[rawcode] = step.Level;
                nextPurchaseAt[rawcode] = gameTime() + step.CooldownSeconds;
                return Original323UpgradePurchaseStatus.Succeeded;
            }
            finally { purchasing = false; }
        }

        private static string StepKey(string rawcode, int level) =>
            "original323-upgrade:" + rawcode + ":" + level;
    }
}
