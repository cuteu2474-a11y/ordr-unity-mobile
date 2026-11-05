using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // CJP/CJS/CJK. Script branches and native pool selection stay separate.
    public sealed class SourceModelUnitGambling : IOriginalEconomyPort
    {
        private readonly EconomyWallet wallet;
        private readonly GameSession game;
        private readonly IOriginalWispRandom script, native;
        private readonly IOriginal323UnitGamblingWorld world;
        private readonly Func<bool> gate;
        private readonly Func<Original323GamblingNavigation> navigation;
        private readonly Action<string, OriginalGambleTier, bool> mission;
        private bool processing;
        private sealed class Draw
        {
            public OriginalGambleTier Tier;
            public Original323GamblingNavigation Navigation;
            public readonly List<int> Rolls = new List<int>();
            public readonly List<string> Rawcodes = new List<string>();
            public bool Ready, Won;
            public string Ability;
            public int WoodReward;
        }
        private Draw pending;
        private readonly List<string> recent = new List<string>();
        public IReadOnlyList<string> RecentResults => recent.AsReadOnly();
        public bool HasPendingDraw => processing || pending != null;
        public event Action<Exception> PostCommitObserverFailed;
        public SourceModelUnitGambling(EconomyWallet shared, GameSession session,
            IOriginalWispRandom scriptRandom, IOriginalWispRandom nativeRandom,
            IOriginal323UnitGamblingWorld rewards, Func<bool> interaction,
            Func<Original323GamblingNavigation> selectedNavigation,
            Action<string, OriginalGambleTier, bool> committedMission = null)
        {
            wallet = shared ?? throw new ArgumentNullException(nameof(shared));
            game = session ?? throw new ArgumentNullException(nameof(session));
            script = scriptRandom ?? throw new ArgumentNullException(nameof(scriptRandom));
            native = nativeRandom ?? throw new ArgumentNullException(nameof(nativeRandom));
            if (ReferenceEquals(script, native)) throw new ArgumentException("난수 경로를 분리해 주세요.");
            world = rewards ?? throw new ArgumentNullException(nameof(rewards));
            gate = interaction ?? throw new ArgumentNullException(nameof(interaction));
            navigation = selectedNavigation ?? throw new ArgumentNullException(nameof(selectedNavigation));
            mission = committedMission;
        }
        public IReadOnlyList<MobileManualOffer> AvailableOffers(ManualEconomyCategory category)
        {
            var offers = new List<MobileManualOffer>();
            if (category != ManualEconomyCategory.Gambling) return offers;
            foreach (OriginalGambleTier tier in Enum.GetValues(typeof(OriginalGambleTier)))
            {
                if (navigation() == Original323GamblingNavigation.Casino && tier != OriginalGambleTier.High) continue;
                offers.Add(new MobileManualOffer(Original323UnitGamblingRules.OfferId(tier),
                    Original323UnitGamblingRules.Label(tier) + " 유닛 도박",
                    Original323UnitGamblingRules.GoldCost(tier) + "골드 + 목재 " +
                    Original323UnitGamblingRules.WoodCost(tier) + "개 · " +
                    (tier == OriginalGambleTier.Low ? "실패 15% / 성공 시 해적선·흔함·안흔함" :
                     tier == OriginalGambleTier.Middle ? "실패 30% / 성공 시 해적선·특별함" :
                     "실패 30%: 토큰 / 성공 시 레일리·특별함·희귀함") +
                    (pending != null && pending.Tier != tier ? " · 다른 등급 보류 거래를 먼저 재시도해 주세요" : "")));
            }
            return offers.AsReadOnly();
        }
        private int Roll(ref int cursor, IOriginalWispRandom lane, int min, int max)
        {
            int slot = cursor++;
            if (slot == pending.Rolls.Count)
            {
                int value = lane.NextInclusive(min, max);
                if (value < min || value > max) throw new InvalidOperationException("유닛 도박 난수 범위 오류");
                pending.Rolls.Add(value);
            }
            return pending.Rolls[slot];
        }
        private string Pool(ref int cursor, IReadOnlyList<string> values) =>
            values[Roll(ref cursor, native, 0, values.Count - 1)];
        private void DrawOutcome()
        {
            if (pending.Ready) return;
            int cursor = 0;
            // Reconstruct from cached rolls after a partial RNG exception.
            pending.Rawcodes.Clear(); pending.Ability = null; pending.WoodReward = 0;
            pending.Won = false;
            int failure = pending.Tier == OriginalGambleTier.Low ? 15 : 30;
            if (Roll(ref cursor, script, 1, 100) <= failure)
            {
                if (pending.Tier == OriginalGambleTier.High)
                {
                    pending.Rawcodes.Add("h06G");
                    if (pending.Navigation == Original323GamblingNavigation.RiskHedge)
                    { pending.Rawcodes.Add("h06G"); pending.WoodReward = 1; }
                }
            }
            else
            {
                pending.Won = true;
                if (pending.Tier == OriginalGambleTier.Low)
                {
                    if (Roll(ref cursor, script, 1, 100) <= 2) pending.Rawcodes.Add("h060");
                    else if (Roll(ref cursor, script, 1, 100) <= 50)
                        pending.Rawcodes.Add(Original323UnitGamblingRules.Common[Roll(ref cursor, script, 1, 9) - 1]);
                    else pending.Rawcodes.Add(Pool(ref cursor, Original323UnitGamblingRules.Uncommon));
                }
                else if (pending.Tier == OriginalGambleTier.Middle)
                {
                    pending.Rawcodes.Add(Roll(ref cursor, script, 1, 1000) <= 45 ? "h060" :
                        Pool(ref cursor, Original323UnitGamblingRules.Special));
                }
                else if (pending.Navigation != Original323GamblingNavigation.Casino &&
                    Roll(ref cursor, script, 1, 100) <= 4) pending.Rawcodes.Add("h05X");
                else
                {
                    int threshold = pending.Navigation == Original323GamblingNavigation.Casino ? 0 : 50;
                    if (Roll(ref cursor, script, 1, 100) <= threshold)
                        pending.Rawcodes.Add(Pool(ref cursor, Original323UnitGamblingRules.HighSpecial));
                    else
                    {
                        pending.Rawcodes.Add(Pool(ref cursor, Original323UnitGamblingRules.HighRare));
                        pending.Ability = "A0YE";
                    }
                }
            }
            pending.Ready = true;
        }
        public ManualActionResult Confirm(ManualEconomyCategory category, string offerId)
        {
            OriginalGambleTier? selected = null;
            foreach (OriginalGambleTier tier in Enum.GetValues(typeof(OriginalGambleTier)))
                if (Original323UnitGamblingRules.OfferId(tier) == offerId) selected = tier;
            if (category != ManualEconomyCategory.Gambling || !selected.HasValue)
                return Reject("유닛 도박 등급을 선택해 주세요.");
            var kind = selected.Value;
            if (processing || game.Phase != SessionPhase.Running || !gate()) return Reject("진행 중에만 실행할 수 있습니다.");
            if (navigation() == Original323GamblingNavigation.Casino && kind != OriginalGambleTier.High)
                return Reject("카지노 항법에서는 하급·중급 도박을 이용할 수 없습니다.");
            if (navigation() == Original323GamblingNavigation.ExchangeHouse)
                return Reject("환전소 실물 거래 연결이 필요합니다.");
            if (pending != null && (pending.Tier != kind || pending.Navigation != navigation()))
                return Reject("보류된 유닛 도박을 같은 등급으로 먼저 재시도해 주세요.");
            int gold = Original323UnitGamblingRules.GoldCost(kind), wood = Original323UnitGamblingRules.WoodCost(kind);
            if (wallet.Balance("gold") < gold || wallet.Balance("wood") < wood)
                return Reject(gold + "골드와 목재 " + wood + "개가 필요합니다.");
            processing = true;
            try
            {
                if (pending == null) pending = new Draw { Tier = kind, Navigation = navigation() };
                DrawOutcome();
                string countId = "source:unit-gambling:" + kind + ":attempts";
                string receipt = "source:unit-gambling:" + kind + ":" + checked(wallet.Balance(countId) + 1);
                var rewards = new ResourceBundle().Add(countId, 1);
                if (pending.WoodReward > 0) rewards.Add("wood", pending.WoodReward);
                bool won = pending.Won;
                using (var transaction = world.Begin(pending.Rawcodes.AsReadOnly(), pending.Ability))
                {
                    string label = transaction.Stage();
                    bool committed;
                    Exception observer = null;
                    try { committed = wallet.TryExchangeOnce(receipt,
                        new ResourceBundle().Add("gold", gold).Add("wood", wood), rewards); }
                    catch (Exception error)
                    { if (!wallet.HasCompletedTransaction(receipt)) throw; committed = true; observer = error; }
                    if (!committed) return Reject("유닛 도박 결제 실패. 보류 결과를 유지합니다.");
                    transaction.Commit();
                    string text = Original323UnitGamblingRules.Label(kind) + " 도박 " +
                        (won ? "성공" : "실패") + " · " + label + (pending.WoodReward > 0 ? " · 목재 +1" : "");
                    pending = null;
                    recent.Insert(0, text); if (recent.Count > 6) recent.RemoveAt(6);
                    try { mission?.Invoke(receipt, kind, won); } catch (Exception error) { Report(error); }
                    if (observer != null) Report(observer);
                    return new ManualActionResult(ManualActionState.Succeeded, text);
                }
            }
            finally { processing = false; }
        }
        private static ManualActionResult Reject(string reason) => new ManualActionResult(ManualActionState.Rejected, reason);
        private void Report(Exception error) { try { PostCommitObserverFailed?.Invoke(error); } catch { } }
    }
}
