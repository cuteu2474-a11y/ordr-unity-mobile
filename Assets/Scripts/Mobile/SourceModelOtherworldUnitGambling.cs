using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    public interface IOriginal323OtherworldGamblingWorld
    {
        IOriginal323OtherworldGamblingTransaction Begin(Original323OtherworldGamblingOutcome outcome);
    }
    public interface IOriginal323OtherworldGamblingTransaction : IDisposable
    {
        bool TrySpawn(out long instanceId, out string displayName);
        // Only seals already staged objects; no callbacks or new RNG here.
        void Commit();
    }

    // Result-only mobile commerce, not an observed Warcraft RNG witness.
    // CJM has no BCj call and does NOT count as Low/Mid/High unit gambling.
    public sealed class SourceModelOtherworldUnitGambling : IOriginalEconomyPort
    {
        public const string OfferId = "original:H0AW:otherworld-unit-gambling";
        private readonly EconomyWallet wallet;
        private readonly GameSession game;
        private readonly IOriginalWispRandom random;
        private readonly Func<bool> canInteract;
        private readonly Func<Original323GamblingNavigation> navigation;
        private readonly IOriginal323OtherworldGamblingWorld world;
        private bool processing;
        private sealed class Draw
        {
            public Original323GamblingNavigation Navigation;
            public long RiskBonus;
            public int? Branch, CasinoBranch, Index;
            public Original323OtherworldGamblingOutcome Outcome;
        }
        private Draw pending;
        private readonly List<string> recent = new List<string>();
        public IReadOnlyList<string> RecentResults => recent.AsReadOnly();
        public event Action<Exception> PostCommitObserverFailed;
        public long Charges => wallet.Balance(Original323OtherworldGamblingRules.ChargesResource);
        public long Attempts => wallet.Balance(Original323OtherworldGamblingRules.AttemptsResource);
        public long Wins => wallet.Balance(Original323OtherworldGamblingRules.WinsResource);
        public long RiskBonus => wallet.Balance(Original323OtherworldGamblingRules.RiskBonusResource);
        public Original323GamblingNavigation Navigation => navigation();
        public int SuccessPercent => Original323OtherworldGamblingRules.SuccessPercent(Navigation, RiskBonus);

        public SourceModelOtherworldUnitGambling(EconomyWallet shared, GameSession session,
            IOriginalWispRandom scriptRandom, Func<bool> interactionGate,
            IOriginal323OtherworldGamblingWorld rewardWorld,
            Func<Original323GamblingNavigation> originalNavigation = null)
        {
            wallet = shared ?? throw new ArgumentNullException(nameof(shared));
            game = session ?? throw new ArgumentNullException(nameof(session));
            random = scriptRandom ?? throw new ArgumentNullException(nameof(scriptRandom));
            canInteract = interactionGate ?? throw new ArgumentNullException(nameof(interactionGate));
            world = rewardWorld ?? throw new ArgumentNullException(nameof(rewardWorld));
            navigation = originalNavigation ?? (() => Original323GamblingNavigation.None);
        }

        // Called only from the real n00G encounter's first defeated signal.
        // Stable receipt prevents repeated lives/UI refreshes from granting
        // CM9's no[player] once-per-game reward more than once.
        internal void RecordThirdCreepDefeated(string rawcode)
        {
            if (rawcode != "n00G" || game.Phase != SessionPhase.Running) return;
            GrantChargeOnce("source:unit-gambling:BdL:CM9:n00G");
        }

        private void GrantChargeOnce(string receipt)
        {
            if (wallet.HasCompletedTransaction(receipt)) return;
            int count = Navigation == Original323GamblingNavigation.Casino ? 2 : 1;
            try { wallet.TryGrantOnce(receipt, new ResourceBundle().Add(
                Original323OtherworldGamblingRules.ChargesResource, count)); }
            catch (Exception error)
            {
                if (!wallet.HasCompletedTransaction(receipt)) throw;
                Report(error);
            }
        }

        public IReadOnlyList<MobileManualOffer> AvailableOffers(ManualEconomyCategory category)
        {
            if (category != ManualEconomyCategory.Gambling) return Array.Empty<MobileManualOffer>();
            return new[] { new MobileManualOffer(OfferId, "이세계 유닛 도박",
                "3500골드 + 목재 5개 + 충전 1회 · 성공 " + SuccessPercent +
                "%: 랜덤전용 14종 중 1기 · 실패: 행운의 토큰 1개" +
                (Navigation == Original323GamblingNavigation.Casino ? " (실패 중 50%는 희귀 유닛으로 대체)" : "") +
                " · 남은 충전 " + Charges + "회 · 스토리 8~13단계/3단계 크립 실제 격파로 충전") };
        }

        private int Roll(ref int? saved, int minimum, int maximum)
        {
            if (!saved.HasValue)
            {
                int value = random.NextInclusive(minimum, maximum);
                if (value < minimum || value > maximum)
                    throw new InvalidOperationException("CJM 원본 난수 범위 오류");
                saved = value;
            }
            return saved.Value;
        }

        private Original323OtherworldGamblingOutcome DrawOutcome()
        {
            if (pending == null) pending = new Draw { Navigation = Navigation, RiskBonus = RiskBonus };
            if (pending.Outcome != null) return pending.Outcome;
            int threshold = Original323OtherworldGamblingRules.SuccessPercent(
                pending.Navigation, pending.RiskBonus);
            int first = Roll(ref pending.Branch, 1, 100);
            bool won = first <= threshold;
            string rawcode = Original323OtherworldGamblingRules.TokenRawcode;
            if (won)
                rawcode = Original323OtherworldGamblingRules.RandomRawcodes[Roll(ref pending.Index, 0, 13)];
            else if (pending.Navigation == Original323GamblingNavigation.Casino &&
                Roll(ref pending.CasinoBranch, 1, 2) == 1)
                rawcode = Original323OtherworldGamblingRules.CasinoRareRawcodes[Roll(ref pending.Index, 0, 41)];
            pending.Outcome = new Original323OtherworldGamblingOutcome(won, rawcode, pending.Navigation);
            return pending.Outcome;
        }

        public ManualActionResult Confirm(ManualEconomyCategory category, string offerId)
        {
            if (category != ManualEconomyCategory.Gambling || offerId != OfferId)
                return Reject("이세계 유닛 도박을 선택해 주세요.");
            if (processing || game.Phase != SessionPhase.Running || !canInteract())
                return Reject("진행 중인 게임에서 일시정지를 해제한 뒤 실행해 주세요.");
            if (Charges < 1 || wallet.Balance("gold") < Original323OtherworldGamblingRules.GoldCost ||
                wallet.Balance("wood") < Original323OtherworldGamblingRules.WoodCost)
                return Reject("3500골드·목재 5개·유닛 도박 충전 1회가 필요합니다.");
            processing = true;
            try
            {
                long attempt = checked(Attempts + 1);
                var outcome = DrawOutcome();
                if (pending.RiskBonus != RiskBonus || pending.Navigation != Navigation)
                    return Reject("보류된 추첨과 항법/저장 상태가 다릅니다.");
                string receipt = "source:unit-gambling:CJM:purchase:" + attempt;
                var costs = new ResourceBundle().Add("gold", Original323OtherworldGamblingRules.GoldCost)
                    .Add("wood", Original323OtherworldGamblingRules.WoodCost)
                    .Add(Original323OtherworldGamblingRules.ChargesResource, 1);
                var rewards = new ResourceBundle().Add(Original323OtherworldGamblingRules.AttemptsResource, 1);
                if (outcome.Won) rewards.Add(Original323OtherworldGamblingRules.WinsResource, 1);
                if (outcome.Won || outcome.Navigation == Original323GamblingNavigation.RiskHedge)
                {
                    if (pending.RiskBonus > 0) costs.Add(Original323OtherworldGamblingRules.RiskBonusResource, pending.RiskBonus);
                    if (!outcome.Won) rewards.Add(Original323OtherworldGamblingRules.RiskBonusResource,
                        checked(pending.RiskBonus + 15));
                }
                using (var spawn = world.Begin(outcome))
                {
                    long actor; string label;
                    if (!spawn.TrySpawn(out actor, out label) || actor <= 0 || string.IsNullOrWhiteSpace(label))
                        return Reject("결과 유닛을 생성하지 못했습니다. 비용과 충전은 유지됩니다.");
                    bool committed;
                    Exception observerError = null;
                    try { committed = wallet.TryExchangeOnce(receipt, costs, rewards); }
                    catch (Exception error)
                    {
                        if (!wallet.HasCompletedTransaction(receipt)) throw;
                        committed = true; observerError = error;
                    }
                    if (!committed) return Reject("이세계 유닛 도박 거래를 완료할 수 없습니다.");
                    spawn.Commit();
                    pending = null;
                    string text = (outcome.Won ? "당첨" : "실패") + " · " + label + " 1개 획득" +
                        " · 3500골드/목재 5개/충전 1회 사용";
                    recent.Insert(0, text);
                    if (recent.Count > 6) recent.RemoveAt(6);
                    if (observerError != null) Report(observerError);
                    return new ManualActionResult(ManualActionState.Succeeded, text);
                }
            }
            finally { processing = false; }
        }
        private static ManualActionResult Reject(string text) =>
            new ManualActionResult(ManualActionState.Rejected, text);
        private void Report(Exception error)
        { try { PostCommitObserverFailed?.Invoke(error); } catch { } }
    }
}
