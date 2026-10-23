using System;
using System.Collections.Generic;
namespace Ordr.Mobile.Core
{
    // One wallet and the exact combat roster; never represent physical units
    // as separate wallet balances.
    public sealed class SharedEconomySession
    {
        public EconomyWallet Wallet { get; private set; }
        public UnitRoster Units { get; private set; }
        public bool HasStarted { get; private set; }
        private bool starting;
        public SharedEconomySession(EconomyWallet wallet, UnitRoster roster)
        {
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            Units = roster ?? throw new ArgumentNullException(nameof(roster));
        }
        public void ApplyVerifiedStart(VerifiedEconomyStartProfile profile,
            Func<string,double,double,long> spawn, Action<long> remove)
        {
            if (profile == null || !profile.IsSourceVerified)
                throw new InvalidOperationException("원본 2.323[C] 시작 자원 근거 미검증");
            if (spawn == null || remove == null) throw new ArgumentNullException("spawn/remove");
            if (HasStarted || starting || Units.Count != 0)
                throw new InvalidOperationException("중복 시작 또는 기존 유닛 존재");
            var before = Wallet.CaptureState();
            var created = new List<long>();
            starting = true;
            try
            {
                Wallet.Grant(profile.Resources);
                foreach (var unit in profile.StartingUnits)
                {
                    long id = spawn(unit.CatalogId, unit.X, unit.Y);
                    if (id <= 0 || Units.Get(id).UnitId != unit.CatalogId)
                        throw new InvalidOperationException("시작 유닛 생성 불일치");
                    created.Add(id);
                }
                HasStarted = true;
            }
            catch
            {
                for (int i = created.Count - 1; i >= 0; i--) remove(created[i]);
                Wallet.RestoreState(before);
                throw;
            }
            finally { starting = false; }
        }
    }

    public sealed class VerifiedStartingUnit
    {
        public string CatalogId { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public VerifiedStartingUnit(string id, double x, double y)
        {
            if (string.IsNullOrWhiteSpace(id) || double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y))
                throw new ArgumentException("검증된 시작 유닛 ID·좌표 필요");
            CatalogId = id; X = x; Y = y;
        }
    }

    public sealed class VerifiedEconomyStartProfile
    {
        public bool IsSourceVerified { get; private set; }
        public ResourceBundle Resources { get; private set; }
        public IReadOnlyList<VerifiedStartingUnit> StartingUnits { get; private set; }
        public string Evidence { get; private set; }
        public VerifiedEconomyStartProfile(string mapVersion, string mode,
            string sourceEvidence, bool verified, ResourceBundle resources,
            IEnumerable<VerifiedStartingUnit> units)
        {
            if (mapVersion != "ORDR_S2_2.323[C]" ||
                mode != GameSession.ModeId || string.IsNullOrWhiteSpace(sourceEvidence) ||
                resources == null || units == null)
                throw new ArgumentException("원본 시작 경제 버전·출처 불완전");
            var list = new List<VerifiedStartingUnit>();
            foreach (var u in units)
            {
                if (u == null) throw new ArgumentException("null 시작 유닛");
                list.Add(u);
            }
            IsSourceVerified = verified; Resources = resources;
            Evidence = sourceEvidence; StartingUnits = list.AsReadOnly();
        }
    }
}
