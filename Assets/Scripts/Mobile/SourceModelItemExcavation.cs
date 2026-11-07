using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // Native BhX/BhW: separate, exhaustible ordinary and unique ability pools.
    // BhS consumes one ordinary stack and chooses 90% ordinary / 10% unique
    // while both pools are nonempty. BhT consumes one unique stack and always
    // draws from unique. BhP marks the chosen ability as obtained exactly once.
    public sealed class SourceModelItemExcavation
    {
        public const string OrdinaryStacks = "source:item-excavation:fZ:ordinary-stacks";
        public const string UniqueStacks = "source:item-excavation:fb:unique-stacks";
        public const string DrawCount = "source:item-excavation:draw-count";
        public const string OrdinaryItemPrefix = "source:item:ordinary:";
        public const int MaximumStacks = 10;

        // BhM and BhN source pools in S2 2.323[C], not item drops invented
        // from hero tooltips. Original pools remove won abilities permanently.
        private static readonly string[] ordinaryPool = {
            "AI04", "AI05", "AI06", "AI07", "AI08", "AI09",
            "AI10", "AI11", "AI12", "AI13", "AI14", "AI15"
        };
        private static readonly string[] uniquePool = {
            "AI00", "AI01", "AI02", "AI03", "AI16",
            "AI17", "AI18", "AI19", "AI20", "AI21"
        };

        private readonly EconomyWallet wallet;
        private readonly GameSession game;
        private readonly IOriginalWispRandom random;
        private readonly Func<bool> interaction;
        private bool processing;
        private Draw pending;
        private sealed class Draw
        {
            public bool UniqueStack;
            public int? Branch, Index;
            public bool AwardUnique;
            public string Rawcode;
        }

        public SourceModelItemExcavation(EconomyWallet shared, GameSession session,
            IOriginalWispRandom nativeRandom, Func<bool> interactionGate)
        {
            wallet = shared ?? throw new ArgumentNullException(nameof(shared));
            game = session ?? throw new ArgumentNullException(nameof(session));
            random = nativeRandom ?? throw new ArgumentNullException(nameof(nativeRandom));
            interaction = interactionGate ?? throw new ArgumentNullException(nameof(interactionGate));
        }

        public long OrdinaryStock => wallet.Balance(OrdinaryStacks);
        public long UniqueStock => wallet.Balance(UniqueStacks);
        public bool HasPendingDraw => processing || pending != null;
        public int OrdinaryRemaining => Available(false).Count;
        public int UniqueRemaining => Available(true).Count;

        public static string OrdinaryItemWalletId(string rawcode) => OrdinaryItemPrefix + rawcode;

        private List<string> Available(bool unique)
        {
            var choices = new List<string>();
            foreach (string rawcode in unique ? uniquePool : ordinaryPool)
            {
                bool owned = unique
                    ? Original323FusionSourceItemLedger.WasEverObtained(wallet, rawcode)
                    : wallet.Balance(OrdinaryItemWalletId(rawcode)) > 0;
                if (!owned) choices.Add(rawcode);
            }
            return choices;
        }

        public bool CanDraw(bool uniqueStack)
        {
            if (processing || game.Phase != SessionPhase.Running || !interaction() ||
                (pending != null && pending.UniqueStack != uniqueStack)) return false;
            if (uniqueStack)
                return UniqueStock > 0 && UniqueRemaining > 0;
            return OrdinaryStock > 0 && (OrdinaryRemaining > 0 || UniqueRemaining > 0);
        }

        private int Roll(ref int? cached, int minimum, int maximum)
        {
            if (!cached.HasValue)
            {
                int result = random.NextInclusive(minimum, maximum);
                if (result < minimum || result > maximum)
                    throw new InvalidOperationException("원본 BhS/BhT 난수 범위 오류");
                cached = result;
            }
            return cached.Value;
        }

        public ManualActionResult DrawItem(bool uniqueStack)
        {
            if (!CanDraw(uniqueStack))
                return new ManualActionResult(ManualActionState.Rejected,
                    "발굴 스택·미획득 아이템·보류된 추첨 또는 게임 진행 상태를 확인해 주세요.");
            processing = true;
            try
            {
                if (pending == null) pending = new Draw { UniqueStack = uniqueStack };
                if (pending.Rawcode == null)
                {
                    List<string> common = Available(false), special = Available(true);
                    // BhS: (roll <= 90 && common left) || unique exhausted.
                    // BhT: always unique, without the BhS probability roll.
                    pending.AwardUnique = uniqueStack ||
                        !((Roll(ref pending.Branch, 1, 100) <= 90 && common.Count > 0) ||
                          special.Count == 0);
                    List<string> pool = pending.AwardUnique ? special : common;
                    if (pool.Count == 0) return Reject("남은 발굴 아이템이 없습니다.");
                    pending.Rawcode = pool[Roll(ref pending.Index, 0, pool.Count - 1)];
                }
                string rawcode = pending.Rawcode;
                if (pending.AwardUnique
                    ? Original323FusionSourceItemLedger.WasEverObtained(wallet, rawcode)
                    : wallet.Balance(OrdinaryItemWalletId(rawcode)) > 0)
                    return Reject("보류된 발굴 아이템이 다른 원본 보상으로 이미 획득되었습니다.");

                string transactionId = "source:item-excavation:Bh" +
                    (uniqueStack ? "T" : "S") + ":" + checked(wallet.Balance(DrawCount) + 1);
                var cost = new ResourceBundle().Add(uniqueStack ? UniqueStacks : OrdinaryStacks, 1);
                var reward = new ResourceBundle().Add(DrawCount, 1);
                if (pending.AwardUnique)
                {
                    reward.Add(Original323FusionSourceItemLedger.WalletId(rawcode), 1);
                    reward.Add(Original323FusionSourceItemLedger.EverObtainedWalletId(rawcode), 1);
                }
                else reward.Add(OrdinaryItemWalletId(rawcode), 1);

                bool committed;
                try { committed = wallet.TryExchangeOnce(transactionId, cost, reward); }
                catch
                {
                    if (!wallet.HasCompletedTransaction(transactionId)) throw;
                    committed = true; // A post-commit observer failure must not redraw.
                }
                if (!committed) return Reject("발굴 거래를 완료하지 못했습니다. 원본 추첨은 보류됩니다.");
                pending = null;
                return new ManualActionResult(ManualActionState.Succeeded,
                    (uniqueStack ? "유니크 발굴" : "아이템 발굴") + " 완료 · " +
                    rawcode + " 획득 (고유 효과·외형 일부 후속 연결 필요)");
            }
            catch (Exception error)
            {
                return Reject("아이템 발굴 실패 · " + error.Message + " · 결과는 재시도까지 보류됩니다.");
            }
            finally { processing = false; }
        }

        private static ManualActionResult Reject(string reason) =>
            new ManualActionResult(ManualActionState.Rejected, reason);
    }
}
