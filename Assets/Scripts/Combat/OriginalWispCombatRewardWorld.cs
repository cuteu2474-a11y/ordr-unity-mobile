using System;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Coordinates must be reconstructed from the current player's original
    // start location (e016/e019/e015) or Lq center (e017).
    public interface IOriginalWispSpawnPositionPort
    {
        bool TryGetPosition(string originalWispId, out double x, out double y);
    }

    // Adds the actual rare-Wisp A0YE behavior to an already created unit.
    // Returning false blocks consumption; cosmetic marking is insufficient.
    public interface IOriginalWispBonusAbilityPort
    {
        bool TryApplyOriginalAbility(long instanceId, string abilityRawcode);
    }

    // A real Combat.SpawnCharacter writes to the very same UnitRoster as the
    // economy session. Rawcode guesses are rejected: only reviewed mappings
    // may instantiate a catalog character.
    public sealed class OriginalWispCombatRewardWorld : IWispRewardWorld
    {
        private readonly CharacterCombatRuntime combat;
        private readonly IVerifiedUnitRawcodeMap rawcodes;
        private readonly IOriginalWispSpawnPositionPort positions;
        private readonly IOriginalWispBonusAbilityPort abilities;

        public OriginalWispCombatRewardWorld(CharacterCombatRuntime liveCombat,
            IVerifiedUnitRawcodeMap verifiedMap, IOriginalWispSpawnPositionPort originalPositions,
            IOriginalWispBonusAbilityPort originalAbilities)
        {
            combat = liveCombat ?? throw new ArgumentNullException(nameof(liveCombat));
            rawcodes = verifiedMap ?? throw new ArgumentNullException(nameof(verifiedMap));
            positions = originalPositions ?? throw new ArgumentNullException(nameof(originalPositions));
            abilities = originalAbilities ?? throw new ArgumentNullException(nameof(originalAbilities));
        }

        public IWispRewardTransaction Begin() => new Transaction(this);

        private sealed class Transaction : IWispRewardTransaction
        {
            private readonly OriginalWispCombatRewardWorld owner;
            private long created;
            private bool committed;
            private bool disposed;

            public Transaction(OriginalWispCombatRewardWorld world) { owner = world; }
            public void Apply(WispReward reward)
            {
                if (disposed || created != 0 || reward == null)
                    throw new InvalidOperationException("위습 보상 트랜잭션 재사용 금지");
                string catalogId;
                double x, y;
                if (!owner.rawcodes.TryResolveUnit(reward.UnitRawcode, out catalogId) ||
                    string.IsNullOrWhiteSpace(catalogId) ||
                    !owner.positions.TryGetPosition(reward.SourceWispId, out x, out y) ||
                    double.IsNaN(x) || double.IsInfinity(x) ||
                    double.IsNaN(y) || double.IsInfinity(y))
                    throw new InvalidOperationException("원본 위습 보상 객체/소환 좌표 미검증");
                created = owner.combat.SpawnCharacter(catalogId, x, y);
                if (created <= 0 || owner.combat.Roster.Get(created).UnitId != catalogId)
                    throw new InvalidOperationException("공유 유닛 목록 등록 실패");
                if (reward.BonusAbilityRawcode != null &&
                    (!String.Equals(reward.BonusAbilityRawcode, "A0YE", StringComparison.Ordinal) ||
                     !owner.abilities.TryApplyOriginalAbility(created, reward.BonusAbilityRawcode)))
                    throw new InvalidOperationException("원본 희귀위습 A0YE 적용 실패");
            }

            public void Commit()
            {
                if (disposed || created <= 0 || committed)
                    throw new InvalidOperationException("위습 월드 거래 확정 실패");
                committed = true;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (!committed && created > 0)
                    owner.combat.RollbackUncommittedSpawn(created);
            }
        }
    }

    // e0IX common recruitment uses a DIFFERENT original function and pool
    // from the four source-extracted automatic Wisp pools. No fallback table
    // or assumed one-Wisp price is supplied here. The source-backed port
    // owns the roll, live spawn and rollback as ONE transaction.
    public interface IOriginal323CommonWispRecruitmentWorld
    {
        int OriginalRandomWispCost { get; }
        string OriginalSourceEvidence { get; }
        IOriginal323CommonWispRecruitmentTransaction Begin();
    }

    // Optional availability guard for a source branch that cannot yet be
    // represented by the Character catalog (e.g. the original pirate ship).
    // The draw remains pending; it must never be silently redrawn as a common.
    public interface IOriginal323CommonWispRecruitmentAvailability
    {
        bool CanDraw { get; }
        string UnavailableReason { get; }
    }

    public interface IOriginal323CommonWispRecruitmentTransaction : IDisposable
    {
        bool TryCreate(out long instanceId, out string originalUnitRawcode);
        void Commit();
    }

    public sealed class Original323CommonWispRecruitmentResult
    {
        public long InstanceId { get; private set; }
        public string CatalogId { get; private set; }
        public int SpentRandomWisps { get; private set; }

        internal Original323CommonWispRecruitmentResult(long id, string unitId, int spent)
        {
            InstanceId = id; CatalogId = unitId; SpentRandomWisps = spent;
        }
    }

    public sealed class Original323CommonWispRecruitmentService
    {
        private readonly CharacterCombatRuntime combat;
        private readonly SharedEconomySession shared;
        private readonly OriginalMapBaseStatCatalog originalUnitObjects;
        private readonly IOriginal323CommonWispRecruitmentWorld source;
        private readonly int cost;
        private bool processing;

        public int Cost => cost;
        public bool CanRecruit => !processing &&
            combat.Game.Phase == SessionPhase.Running &&
            shared.Wallet.Balance(WispResourceIds.Random) >= Cost &&
            (!(source is IOriginal323CommonWispRecruitmentAvailability) ||
             ((IOriginal323CommonWispRecruitmentAvailability)source).CanDraw);

        public Original323CommonWispRecruitmentService(CharacterCombatRuntime liveCombat,
            SharedEconomySession sameSession, OriginalMapBaseStatCatalog originalMap,
            IOriginal323CommonWispRecruitmentWorld verifiedSource)
        {
            combat = liveCombat ?? throw new ArgumentNullException(nameof(liveCombat));
            shared = sameSession ?? throw new ArgumentNullException(nameof(sameSession));
            originalUnitObjects = originalMap ?? throw new ArgumentNullException(nameof(originalMap));
            source = verifiedSource ?? throw new ArgumentNullException(nameof(verifiedSource));
            cost = source.OriginalRandomWispCost;
            if (!ReferenceEquals(combat.Roster, shared.Units) ||
                cost <= 0 ||
                !Trusted(source.OriginalSourceEvidence) ||
                source.OriginalSourceEvidence.IndexOf(
                    Original323MonsterWaveIdentityCatalog.OriginalWar3mapJSha256,
                    StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException(
                    "원본 흔함 소환의 비용·난수·월드 및 공유 목록 승인 근거가 없습니다.");
        }

        public Original323CommonWispRecruitmentResult RecruitOne()
        {
            if (!CanRecruit) return null;
            processing = true;
            try
            {
                var beforeWallet = shared.Wallet.CaptureState();
                var beforeUnits = combat.MobileUnitSnapshot();
                double beforeTime = combat.Time;
                try
                {
                    using (var transaction = source.Begin() ??
                        throw new InvalidOperationException("원본 흔함 소환 거래를 열 수 없습니다."))
                    {
                        long instanceId;
                        string rawcode;
                        if (!transaction.TryCreate(out instanceId, out rawcode))
                        {
                            if (combat.Time != beforeTime ||
                                combat.MobileUnitSnapshot().Count != beforeUnits.Count ||
                                !SameWallet(beforeWallet, shared.Wallet.CaptureState()))
                                throw new InvalidOperationException(
                                    "거절된 원본 소환이 공유 상태를 변경했습니다.");
                            return null;
                        }
                        var afterUnits = combat.MobileUnitSnapshot();
                        if (instanceId <= 0 || string.IsNullOrWhiteSpace(rawcode) ||
                            rawcode.Length != 4 || beforeTime != combat.Time ||
                            afterUnits.Count != beforeUnits.Count + 1 ||
                            !SameWallet(beforeWallet, shared.Wallet.CaptureState()))
                            throw new InvalidOperationException(
                                "원본 흔함 유닛 생성 또는 난수 거래가 공유 상태를 변경했습니다.");
                        foreach (var old in beforeUnits)
                        {
                            bool found = false;
                            foreach (var current in afterUnits)
                                if (current.InstanceId == old.InstanceId &&
                                    current.Unit.Id == old.Unit.Id &&
                                    current.Position.X == old.Position.X &&
                                    current.Position.Y == old.Position.Y)
                                { found = true; break; }
                            if (!found) throw new InvalidOperationException(
                                "흔함 소환 도중 기존 유닛이 변경되었습니다.");
                        }
                        string catalogId = null;
                        foreach (var candidate in afterUnits)
                            if (candidate.InstanceId == instanceId)
                            {
                                if (catalogId != null) throw new InvalidOperationException(
                                    "동일 소환 인스턴스 중복");
                                catalogId = candidate.Unit.Id;
                                if (candidate.Unit.Grade != "흔함")
                                    throw new InvalidOperationException("흔함이 아닌 원본 소환 결과");
                            }
                        if (string.IsNullOrWhiteSpace(catalogId))
                            throw new InvalidOperationException("소환한 유닛의 실제 보유 정보가 없습니다.");
                        var original = originalUnitObjects.Find(catalogId);
                        if (original == null || original.OriginalRawcode != rawcode)
                            throw new InvalidOperationException(
                                "원본 소환 객체 ID가 캐릭터 카탈로그와 일치하지 않습니다.");

                        if (!shared.Wallet.TryExchange(
                            new ResourceBundle().Add(WispResourceIds.Random, Cost),
                            new ResourceBundle()))
                            throw new InvalidOperationException("랜덤위습 잔액 변경 충돌");
                        transaction.Commit();
                        return new Original323CommonWispRecruitmentResult(
                            instanceId, catalogId, Cost);
                    }
                }
                catch
                {
                    shared.Wallet.RestoreState(beforeWallet);
                    throw;
                }
            }
            finally { processing = false; }
        }

        private static bool SameWallet(EconomyWalletState a, EconomyWalletState b)
        {
            if (a.Balances.Count != b.Balances.Count ||
                a.CompletedRewards.Count != b.CompletedRewards.Count) return false;
            for (int i = 0; i < a.Balances.Count; i++)
                if (a.Balances[i].Id != b.Balances[i].Id ||
                    a.Balances[i].Amount != b.Balances[i].Amount) return false;
            for (int i = 0; i < a.CompletedRewards.Count; i++)
                if (a.CompletedRewards[i] != b.CompletedRewards[i]) return false;
            return true;
        }

        private static bool Trusted(string sourceEvidence)
        {
            if (string.IsNullOrWhiteSpace(sourceEvidence)) return false;
            foreach (var marker in new[] {
                "TEST_ONLY", "TEST_FIXTURE", "SYNTHETIC", "MODEL_ONLY", "MOCK_ONLY" })
                if (sourceEvidence.IndexOf(marker,
                    StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }
    }
}
