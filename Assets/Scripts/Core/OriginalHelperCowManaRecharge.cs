using System;

namespace Ordr.Mobile.Core
{
    // Original war3map.j: CUo creates kk[player] as h08A (line 58932).
    // CR8 (56309-56321) consumes e0IX and increases kk mana by
    // min(100, 20 + R2I(X3*1.5)). Final stored mana is capped by native max.
    public static class OriginalHelperCowRules
    {
        public const string HelperRawcode = "h08A";
        public const string OfferId = "original:e0IX:helper_mana";

        public static int RechargeAmount(int round)
        {
            if (round < 0) throw new ArgumentOutOfRangeException(nameof(round));
            // Exact for nonnegative integer rounds, avoiding real->int overflow.
            return (int)Math.Min(100L, 20L + (3L * round) / 2L);
        }
    }

    // This represents the ORIGINAL physical cow, not a wallet-only proxy.
    // Its mana is authoritative for other cow spells, passive regen and buffs.
    public interface IOriginalHelperCowManaActor
    {
        string OriginalRawcode { get; }
        int OwnerPlayerId { get; }
        bool IsAlive { get; }
        double CurrentMana { get; }
        double MaximumMana { get; }
        void SetMana(double mana);
    }

    public sealed class OriginalHelperCowManaSnapshot
    {
        public readonly double Current;
        public readonly double Maximum;
        public OriginalHelperCowManaSnapshot(double now, double max)
        {
            Current = now; Maximum = max;
        }
    }

    public interface IOriginalHelperCowManaWorld
    {
        bool TryRead(out OriginalHelperCowManaSnapshot snapshot);
        // Returns null if the helper unit is not yet present or has vanished.
        IOriginalHelperCowManaTransaction BeginRecharge();
    }

    public interface IOriginalHelperCowManaTransaction : IDisposable
    {
        // Must restore original unit mana on uncommitted Dispose.
        double ApplyRecharge(int amount);
        void Commit();
    }

    // Live unit adapter. The caller supplies a real, source-verified h08A
    // actor. We do not invent max/initial mana or create a fake helper cow.
    public sealed class OriginalHelperCowManaWorld : IOriginalHelperCowManaWorld
    {
        private readonly Func<IOriginalHelperCowManaActor> actorSource;
        private readonly int owner;
        private bool busy;

        public OriginalHelperCowManaWorld(
            Func<IOriginalHelperCowManaActor> liveActor, int ownerPlayerId = 0)
        {
            actorSource = liveActor ?? throw new ArgumentNullException(nameof(liveActor));
            if (ownerPlayerId < 0) throw new ArgumentOutOfRangeException(nameof(ownerPlayerId));
            owner = ownerPlayerId;
        }

        private bool Valid(IOriginalHelperCowManaActor a)
        {
            if (a == null || !a.IsAlive ||
                a.OriginalRawcode != OriginalHelperCowRules.HelperRawcode ||
                a.OwnerPlayerId != owner) return false;
            double m = a.CurrentMana, max = a.MaximumMana;
            return !double.IsNaN(m) && !double.IsInfinity(m) &&
                !double.IsNaN(max) && !double.IsInfinity(max) &&
                max > 0 && m >= 0 && m <= max;
        }

        public bool TryRead(out OriginalHelperCowManaSnapshot snapshot)
        {
            snapshot = null;
            var a = actorSource();
            if (!Valid(a)) return false;
            snapshot = new OriginalHelperCowManaSnapshot(a.CurrentMana, a.MaximumMana);
            return true;
        }

        public IOriginalHelperCowManaTransaction BeginRecharge()
        {
            if (busy) throw new InvalidOperationException("이미 진행 중인 도움소 거래");
            var a = actorSource();
            if (!Valid(a)) return null;
            busy = true;
            return new Transaction(this, a);
        }

        private sealed class Transaction : IOriginalHelperCowManaTransaction
        {
            private readonly OriginalHelperCowManaWorld world;
            private readonly IOriginalHelperCowManaActor actor;
            private readonly double before;
            private bool attempted, committed, disposed;

            public Transaction(OriginalHelperCowManaWorld w, IOriginalHelperCowManaActor a)
            {
                world = w; actor = a; before = a.CurrentMana;
            }

            public double ApplyRecharge(int amount)
            {
                if (disposed || attempted || amount < 1 || amount > 100)
                    throw new InvalidOperationException("도움소 충전 금액 또는 거래 상태 오류");
                if (!world.Valid(actor) || !ReferenceEquals(actor, world.actorSource()))
                    throw new InvalidOperationException("원본 도움소가 변경되었습니다.");
                attempted = true; // also roll back a partially failing setter
                actor.SetMana(Math.Min(actor.MaximumMana, before + amount));
                if (!world.Valid(actor))
                    throw new InvalidOperationException("도움소 마나 상태가 올바르지 않습니다.");
                return actor.CurrentMana;
            }

            public void Commit()
            {
                if (disposed || committed || !attempted ||
                    !world.Valid(actor) || !ReferenceEquals(actor, world.actorSource()))
                    throw new InvalidOperationException("도움소 최종 거래 확인 실패");
                committed = true;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    if (attempted && !committed) actor.SetMana(before);
                }
                finally { world.busy = false; }
            }
        }
    }

    public sealed class OriginalHelperCowRechargeResult
    {
        public readonly int RequestedMana;
        public readonly double CurrentMana;

        public OriginalHelperCowRechargeResult(int requested, double current)
        {
            RequestedMana = requested; CurrentMana = current;
        }
    }

    public sealed class OriginalHelperCowManaRecharge
    {
        private readonly EconomyWallet wallet;
        private readonly GameSession game;
        private readonly IOriginalHelperCowManaWorld world;

        public OriginalHelperCowManaRecharge(EconomyWallet sharedWallet,
            GameSession session, IOriginalHelperCowManaWorld verifiedWorld)
        {
            wallet = sharedWallet ?? throw new ArgumentNullException(nameof(sharedWallet));
            game = session ?? throw new ArgumentNullException(nameof(session));
            world = verifiedWorld ?? throw new ArgumentNullException(nameof(verifiedWorld));
        }

        public bool TryGetCurrentMana(out OriginalHelperCowManaSnapshot now) =>
            world.TryRead(out now);

        // CR8 has no round > 0 condition. At game wave 0, recovery is 20.
        // A full mana bar still spends a Wisp in the original trigger.
        public bool CanRecharge
        {
            get
            {
                OriginalHelperCowManaSnapshot unused;
                return game.Phase == SessionPhase.Running &&
                    wallet.Balance(WispResourceIds.Random) >= 1 &&
                    world.TryRead(out unused);
            }
        }

        public OriginalHelperCowRechargeResult SpendOne()
        {
            if (!CanRecharge) return null;
            int gain = OriginalHelperCowRules.RechargeAmount(game.CurrentWave);
            using (var txn = world.BeginRecharge())
            {
                if (txn == null) return null;
                double mana = txn.ApplyRecharge(gain);
                var before = wallet.CaptureState();
                try
                {
                    if (!wallet.TryExchange(
                        new ResourceBundle().Add(WispResourceIds.Random, 1),
                        new ResourceBundle()))
                        return null; // uncommitted actor state rolls back
                    txn.Commit();
                }
                catch
                {
                    wallet.RestoreState(before);
                    throw; // txn.Dispose() rolls back cow mana
                }
                return new OriginalHelperCowRechargeResult(gain, mana);
            }
        }
    }
}
