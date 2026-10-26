using System;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Mobile
{
    // This adapter owns ONLY the true shared gold/wood wallet debit.
    // The actual original CHu/CHx unit swap and its restoration are separate
    // source-verification responsibilities; callers must never pass preview
    // object names or a synthetic UnitRoster for irreversible mutations.
    public interface IOriginalJassReversibleUnitFusionTransaction : IDisposable
    {
        bool TryStageOriginalUnitFusion(OriginalJassSimpleFusionCommand command,
            out long createdOriginalInstanceId, out string reason);
        // Must be atomic or fully reversible if Commit throws. No new units,
        // skills, aura timers or native effects may remain on an aborted scope.
        void Commit();
    }

    public interface IOriginalJassReversibleUnitFusionWorld
    {
        // Must lock the same live CharacterCombatRuntime until Dispose.
        IOriginalJassReversibleUnitFusionTransaction BeginExclusiveUnitFusion(
            CharacterCombatRuntime combat, SharedEconomySession economy);
    }

    // Implements actual economic spending and rollback for native CWR fusion.
    // Physical unit removal/spawn is delegated ONLY to a separately verified
    // reversible native unit transaction on the SAME combat instance.
    public sealed class OriginalJassSharedWalletFusionEffects :
        IOriginalJassLiveEffectsFactory
    {
        private readonly IOriginalJassReversibleUnitFusionWorld unitWorld;

        public OriginalJassSharedWalletFusionEffects(
            IOriginalJassReversibleUnitFusionWorld originalUnitWorld)
        {
            unitWorld = originalUnitWorld ??
                throw new ArgumentNullException(nameof(originalUnitWorld));
        }

        public IOriginalJassLiveEffectsTransaction BeginExclusiveNativeFusion(
            CharacterCombatRuntime combat, SharedEconomySession economy)
        {
            if (combat == null || economy == null ||
                !Object.ReferenceEquals(combat.Roster, economy.Units))
                throw new InvalidOperationException(
                    "공유 지갑과 전투 월드의 원본 유닛 로스터가 다릅니다.");
            var native = unitWorld.BeginExclusiveUnitFusion(combat, economy);
            if (native == null) return null;
            try { return new Transaction(native, economy.Wallet); }
            catch
            {
                native.Dispose();
                throw;
            }
        }

        private sealed class Transaction : IOriginalJassLiveEffectsTransaction
        {
            private readonly IOriginalJassReversibleUnitFusionTransaction unit;
            private readonly EconomyWallet wallet;
            private readonly EconomyWalletState walletBefore;
            private bool attempted;
            private bool spent;
            private bool ready;
            private bool committed;
            private bool disposed;

            internal Transaction(IOriginalJassReversibleUnitFusionTransaction native,
                EconomyWallet realWallet)
            {
                unit = native;
                wallet = realWallet;
                walletBefore = wallet.CaptureState();
            }

            public bool TryStageOriginalFusion(OriginalJassSimpleFusionCommand command,
                out long created, out string reason)
            {
                if (disposed || committed)
                    throw new InvalidOperationException("원본 제작 경제 거래가 종료됐습니다.");
                created = 0;
                reason = null;
                if (attempted || command == null ||
                    command.Gold < 0 || command.Lumber < 0)
                {
                    reason = "원본 거래가 중복됐거나 조합 금액이 유효하지 않습니다.";
                    return false;
                }
                attempted = true;
                var charge = new ResourceBundle();
                if (command.Gold != 0) charge.Add("gold", command.Gold);
                if (command.Lumber != 0) charge.Add("wood", command.Lumber);
                if (!wallet.TryExchange(charge, new ResourceBundle()))
                {
                    reason = "공유 지갑의 실제 골드·목재가 부족합니다.";
                    return false;
                }
                spent = true;
                // If the native world rejects after spending, Dispose restores
                // the ENTIRE wallet snapshot (including one-time rewards).
                if (!unit.TryStageOriginalUnitFusion(command,
                    out created, out reason))
                    return false;
                ready = true;
                return true;
            }

            public void Commit()
            {
                if (disposed || committed || !ready)
                    throw new InvalidOperationException("원본 재료/유닛 생성이 미완료입니다.");
                unit.Commit();
                committed = true;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                Exception failure = null;
                // Native rollback FIRST: it can include status, timers, RNG,
                // spawn side effects and original object identities.
                try { unit.Dispose(); }
                catch (Exception error) { failure = error; }
                if (!committed && spent)
                {
                    try { wallet.RestoreState(walletBefore); }
                    catch (Exception error)
                    {
                        if (failure == null) failure = error;
                        else failure = new AggregateException(failure, error);
                    }
                }
                if (failure != null) throw failure;
            }
        }
    }
}
