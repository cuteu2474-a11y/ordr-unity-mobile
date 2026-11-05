using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    public sealed class SourceModelGamblingNavigation
    {
        public const string NavigationResource = "source:navigation:gambling";
        public const string RareRerollsResource = "source:reroll:LN";
        private readonly EconomyWallet wallet;
        private readonly Original323NavigationSelection selection;
        private readonly Func<bool> gate, pending;
        public event Action<Exception> PostCommitObserverFailed;
        public Original323GamblingNavigation Navigation =>
            (Original323GamblingNavigation)wallet.Balance(NavigationResource);
        public long RareRerolls => wallet.Balance(RareRerollsResource);
        public string ChosenNavigation => selection.ChosenNavigation;
        public bool CanChoose => selection.CanChoose && Navigation == Original323GamblingNavigation.None &&
            gate() && !pending();
        public SourceModelGamblingNavigation(EconomyWallet shared,
            Original323NavigationSelection sharedSelection, Func<bool> interaction, Func<bool> hasPendingDraw)
        {
            wallet = shared ?? throw new ArgumentNullException(nameof(shared));
            selection = sharedSelection ?? throw new ArgumentNullException(nameof(sharedSelection));
            gate = interaction ?? throw new ArgumentNullException(nameof(interaction));
            pending = hasPendingDraw ?? throw new ArgumentNullException(nameof(hasPendingDraw));
            // Native initialization sets LN=2, independently of navigation.
            const string receipt = "source:reroll:initial-LN-2";
            try { wallet.TryGrantOnce(receipt, new ResourceBundle().Add(RareRerollsResource, 2)); }
            catch (Exception) { if (!wallet.HasCompletedTransaction(receipt)) throw; }
        }
        public bool TryChoose(Original323GamblingNavigation navigation)
        {
            // Exchange-house requires its own shop/comp system, not a label.
            if ((navigation != Original323GamblingNavigation.Casino &&
                 navigation != Original323GamblingNavigation.RiskHedge) || !CanChoose) return false;
            string label = navigation == Original323GamblingNavigation.Casino ? "카지노" : "리스크헷지";
            return selection.TryChoose("도박광 :: " + label, () =>
            {
                var rewards = new ResourceBundle().Add(NavigationResource, (int)navigation)
                    .Add(RareRerollsResource, navigation == Original323GamblingNavigation.Casino ? 1 : 2);
                if (navigation == Original323GamblingNavigation.Casino)
                {
                    // CKR kP'=2*kP+j4. Increment by kP+j4; do not erase spent attempts.
                    long extra = checked(wallet.Balance(Original323OtherworldGamblingRules.ChargesResource) +
                        wallet.Balance(Original323OtherworldGamblingRules.AttemptsResource));
                    if (extra > 0) rewards.Add(Original323OtherworldGamblingRules.ChargesResource, extra);
                }
                const string receipt = "source:navigation:CKW:CKR";
                try
                {
                    if (!wallet.TryGrantOnce(receipt, rewards))
                        throw new InvalidOperationException("이미 처리한 항법 선택입니다.");
                }
                catch (Exception error)
                {
                    if (!wallet.HasCompletedTransaction(receipt)) throw;
                    try { PostCommitObserverFailed?.Invoke(error); } catch { }
                }
            });
        }
    }
}
