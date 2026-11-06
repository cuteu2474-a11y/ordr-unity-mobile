using System;
using Ordr.Mobile.Core;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Mobile
{
    // CKR(3) grants I009 with ZERO initial charges. Only actual source
    // events may add kj[player], which this model mirrors in the shared wallet.
    public interface IOriginal323CompuAwardWitness
    {
        bool TryConfirm(string sourceEventId, long amount, out string evidence);
    }

    // CKG/CKJ create real roster actors. The owner must roll back every
    // uncommitted actor (including CKJ's second physical luck token).
    public interface IOriginal323CompuUnitWorld
    {
        IOriginal323CompuUnitTransaction Begin(string recipe, string randomRawcode);
    }
    public interface IOriginal323CompuUnitTransaction : IDisposable
    {
        bool TrySpawn(out string description);
        void Commit();
    }

    // CKH/CKI are ITEM excavation stacks, not a virtual currency voucher.
    // Until an actual item inventory port is installed those recipes stay locked.
    public interface IOriginal323CompuItemWorld
    {
        IOriginal323CompuItemTransaction Begin(bool unique);
    }
    public interface IOriginal323CompuItemTransaction : IDisposable
    {
        bool TryCreate();
        void Commit();
    }

    public sealed class SourceModelCompuExchange
    {
        public const string CompuResource = "source:exchange:I009:kj";
        public const string ExchangeCount = "source:exchange:completed";
        public const string OrdinaryUsed = "source:exchange:CKH:used";
        public const string UniqueUsed = "source:exchange:CKI:used";
        private readonly EconomyWallet wallet;
        private readonly GameSession game;
        private readonly IOriginalWispRandom random;
        private readonly Func<bool> gate;
        private readonly Func<Original323GamblingNavigation> navigation;
        private readonly IOriginal323CompuUnitWorld units;
        private IOriginal323CompuItemWorld items;
        private IOriginal323CompuAwardWitness witness;
        private bool processing;
        private PendingRoll pending;
        private sealed class PendingRoll
        {
            public string Recipe;
            public int Roll;
            public string Rawcode;
        }
        public long Compu => wallet.Balance(CompuResource);
        public bool HasVerifiedAwardWitness => witness != null;
        public bool IsOpen => navigation() == Original323GamblingNavigation.ExchangeHouse &&
            game.Phase == SessionPhase.Running && game.CurrentWave > 20 && gate();
        public bool HasPendingExchange => processing || pending != null;

        public SourceModelCompuExchange(EconomyWallet sharedWallet, GameSession session,
            IOriginalWispRandom scriptRandom, Func<bool> interaction,
            Func<Original323GamblingNavigation> chosenNavigation,
            IOriginal323CompuUnitWorld unitWorld,
            IOriginal323CompuItemWorld itemWorld = null,
            IOriginal323CompuAwardWitness sourceWitness = null)
        {
            wallet = sharedWallet ?? throw new ArgumentNullException(nameof(sharedWallet));
            game = session ?? throw new ArgumentNullException(nameof(session));
            random = scriptRandom ?? throw new ArgumentNullException(nameof(scriptRandom));
            gate = interaction ?? throw new ArgumentNullException(nameof(interaction));
            navigation = chosenNavigation ?? throw new ArgumentNullException(nameof(chosenNavigation));
            units = unitWorld ?? throw new ArgumentNullException(nameof(unitWorld));
            items = itemWorld;
            witness = sourceWitness;
        }

        public void ConfigureItemWorld(IOriginal323CompuItemWorld sourceItems)
        {
            items = sourceItems ?? throw new ArgumentNullException(nameof(sourceItems));
        }

        public void ConfigureVerifiedAwardWitness(IOriginal323CompuAwardWitness sourceWitness)
        {
            witness = sourceWitness ?? throw new ArgumentNullException(nameof(sourceWitness));
        }

        // Supply uses a verified callback; no admin/UI button creates Compu.
        // The source event ID becomes a durable idempotent wallet receipt.
        public bool RecordObservedAward(string eventId, long amount)
        {
            if (navigation() != Original323GamblingNavigation.ExchangeHouse ||
                game.Phase != SessionPhase.Running || witness == null || amount <= 0 ||
                string.IsNullOrWhiteSpace(eventId)) return false;
            string evidence;
            if (!witness.TryConfirm(eventId, amount, out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence)) return false;
            string receipt = "source:exchange:award:" + eventId;
            try { return wallet.TryGrantOnce(receipt, new ResourceBundle().Add(CompuResource, amount)); }
            catch
            {
                if (!wallet.HasCompletedTransaction(receipt)) throw;
                return true;
            }
        }

        public static int Cost(string recipe)
        {
            switch (recipe)
            {
                case "CKD": return 1;
                case "CKE": case "CKF": return 3;
                case "CKG": return 5;
                case "CKH": return 2;
                case "CKI": return 6;
                case "CKJ": return 20;
                default: return 0;
            }
        }

        public bool CanExchange(string recipe)
        {
            if (!IsOpen || processing || Cost(recipe) == 0 || Compu < Cost(recipe) ||
                (pending != null && pending.Recipe != recipe)) return false;
            if ((recipe == "CKH" || recipe == "CKI") && items == null) return false;
            return (recipe != "CKH" || wallet.Balance(OrdinaryUsed) == 0) &&
                (recipe != "CKI" || wallet.Balance(UniqueUsed) == 0);
        }

        private int Roll(PendingRoll state, int minimum, int maximum)
        {
            if (state.Roll == 0)
            {
                state.Roll = random.NextInclusive(minimum, maximum);
                if (state.Roll < minimum || state.Roll > maximum)
                    throw new InvalidOperationException("콤푸 교환 원본 BFi 난수 범위 오류");
            }
            return state.Roll;
        }

        public ManualActionResult Exchange(string recipe)
        {
            if (!CanExchange(recipe))
                return new ManualActionResult(ManualActionState.Rejected,
                    items == null && (recipe == "CKH" || recipe == "CKI")
                    ? "원본 아이템 발굴 실물 지급 연결 대기"
                    : "환전소 선택·콤푸·1회 제한 또는 보류 거래를 확인해 주세요.");
            processing = true;
            try
            {
                // Never draw RNG until both navigation and cost are valid.
                if (pending == null) pending = new PendingRoll { Recipe = recipe };
                var outcome = new ResourceBundle();
                string label;
                if (recipe == "CKD")
                {
                    int roll = Roll(pending, 1, 10);
                    if (roll <= 5) { outcome.Add("gold", 5000); label = "골드 5,000"; }
                    else if (roll <= 9) { outcome.Add(WispResourceIds.Random, 1); label = "랜덤위습 1"; }
                    else { outcome.Add(WispResourceIds.Choice, 1); label = "흔함선택위습 1"; }
                }
                else if (recipe == "CKE")
                {
                    int amount = Roll(pending, 1, 2);
                    outcome.Add("wood", amount); label = "목재 " + amount;
                }
                else if (recipe == "CKF")
                {
                    int amount = Roll(pending, 1, 100) <= 35 ? 2 : 1;
                    outcome.Add(WispResourceIds.Choice, amount);
                    label = "흔함선택위습 " + amount;
                }
                else if (recipe == "CKG") label = "해적선 1기";
                else if (recipe == "CKH")
                {
                    outcome.Add(OrdinaryUsed, 1);
                    label = "일반 아이템 발굴 1스택";
                }
                else if (recipe == "CKI")
                {
                    outcome.Add(UniqueUsed, 1);
                    label = "유니크 아이템 발굴 1스택";
                }
                else
                {
                    int index = Roll(pending, 1,
                        Original323OtherworldGamblingRules.RandomRawcodes.Count) - 1;
                    pending.Rawcode = Original323OtherworldGamblingRules.RandomRawcodes[index];
                    label = "랜덤전용 유닛 1기 + 행운의 토큰 1기";
                }

                string receipt = "source:exchange:" + (wallet.Balance(ExchangeCount) + 1);
                outcome.Add(ExchangeCount, 1);
                var costs = new ResourceBundle().Add(CompuResource, Cost(recipe));
                // Stage every physical result before the single wallet debit.
                using (var spawned = recipe == "CKG" || recipe == "CKJ"
                    ? units.Begin(recipe, pending.Rawcode) : null)
                using (var item = recipe == "CKH" || recipe == "CKI"
                    ? items.Begin(recipe == "CKI") : null)
                {
                    if (((recipe == "CKG" || recipe == "CKJ") && spawned == null) ||
                        ((recipe == "CKH" || recipe == "CKI") && item == null))
                        return Reject("원본 실물 거래 준비 실패. 콤푸는 유지됩니다.");
                    string physicalDescription;
                    if (spawned != null &&
                        !spawned.TrySpawn(out physicalDescription))
                        return Reject("유닛/토큰 실물 생성 실패. 콤푸는 유지됩니다.");
                    if (item != null && !item.TryCreate())
                        return Reject("아이템 발굴 실물 생성 실패. 콤푸는 유지됩니다.");
                    bool committed;
                    try { committed = wallet.TryExchangeOnce(receipt, costs, outcome); }
                    catch
                    {
                        if (!wallet.HasCompletedTransaction(receipt)) throw;
                        committed = true; // Observer threw after durable commit.
                    }
                    if (!committed) return Reject("환전 거래 불가. 콤푸는 유지됩니다.");
                    if (spawned != null) spawned.Commit();
                    if (item != null) item.Commit();
                    pending = null;
                    return new ManualActionResult(ManualActionState.Succeeded,
                        Cost(recipe) + "콤푸 교환 완료 · " + label);
                }
            }
            catch (Exception error)
            {
                return Reject("환전 실패 · " + error.Message +
                    " · 원본 추첨 결과는 재시도까지 보류합니다.");
            }
            finally { processing = false; }
        }

        private static ManualActionResult Reject(string reason) =>
            new ManualActionResult(ManualActionState.Rejected, reason);
    }
}
