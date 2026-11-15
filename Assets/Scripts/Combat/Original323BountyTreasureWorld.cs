using System;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // A CRW chest is an actual original chest-open event. The source must
    // identify the chest, original BFi(1,100) roll, bounty eligibility and
    // (for a gold branch) the native source award. Ordinary monster deaths,
    // an A05D search, and mobile button presses are NOT CRW events.
    public interface IOriginal323BountyChestOpenWitness
    {
        bool TryObserveOriginalCrwOpen(string chestEventId, int round,
            out int originalRoll, out int sourceGoldAmount,
            out string sourceEvidence);
    }

    // Last-map abilities are added to actual player record guidance and i4.
    // A durable wallet entitlement alone is not a verified native ability.
    public interface IOriginal323OnePieceNativeAbilityWorld
    {
        // Must be idempotent by receiptId, including observer exceptions.
        // Source operation: H0C4 adds A0KV; i4 adds A0KT/A0KU/A0KS/A0KR.
        bool TryApplyOnePieceOriginalAbilitiesOnce(string receiptId,
            string originalCrwEvidence);
    }

    public enum Original323CrwOutcome
    {
        Unavailable, Wood, RandomWisp, Gold, TreasureMap, OnePiece
    }

    public sealed class Original323CrwResult
    {
        public bool Succeeded { get; private set; }
        public Original323CrwOutcome Outcome { get; private set; }
        public int Gold { get; private set; }
        public int MapsRemaining { get; private set; }
        public string Message { get; private set; }
        internal Original323CrwResult(bool success,Original323CrwOutcome kind,
            int gold,int maps,string message)
        {
            Succeeded=success;Outcome=kind;Gold=gold;
            MapsRemaining=maps;Message=message;
        }
    }

    // Pinned war3map.j CRW (55657..55688): LV starts at five.
    // 1..20 wood, 21..40 random Wisp, 41..95 gold 2000..5000,
    // 96..100 map while LV>0 (gold when exhausted). Map 1..4 invokes
    // BeM(3); map 5 invokes the One Piece awards, not BeM.
    // All resource mutations use the existing shared wallet/receipts.
    public sealed class Original323BountyTreasureWorld
    {
        public const int InitialMaps=5;
        public const int MinGold=2000;
        public const int MaxGold=5000;
        public const string MapsRemainingId="source:bounty:CRW:LV";
        public const string OnePieceRecordId="source:bounty:CRW:A0KV";
        public const string OnePieceI4A0KT="source:bounty:CRW:i4:A0KT";
        public const string OnePieceI4A0KU="source:bounty:CRW:i4:A0KU";
        public const string OnePieceI4A0KS="source:bounty:CRW:i4:A0KS";
        public const string OnePieceI4A0KR="source:bounty:CRW:i4:A0KR";
        public const string LineExplosionIncreaseId="source:bounty:CRW:LL";
        public const string LineMagicDefenseIncreaseId="source:bounty:CRW:LK";
        public const string NativeAbilitiesPendingId="source:bounty:CRW:native-pending";
        public const int OnePieceLineIncrease=10;
        public const string FinalReceiptId="source:bounty:CRW:one-piece";
        private const string InitReceiptId="source:bounty:CRW:LV-initial-5";
        private readonly EconomyWallet wallet;
        private readonly GameSession game;
        private readonly Original323TreasureHunt hunt;
        private readonly Original323MobileTreasureSession mobile;
        private readonly IOriginal323BountyChestOpenWitness source;
        private readonly IOriginal323OnePieceNativeAbilityWorld native;
        private bool processing;
        private string pendingMapId;
        private int pendingMapRound,pendingMapRemaining;
        private string pendingMapEvidence;
        private string pendingNativeEvent,pendingNativeEvidence;
        public bool HasUnfinishedMapSpawn => pendingMapId!=null;
        public bool HasUnfinishedNativeAbility => NativeOnePiecePending;

        public Original323BountyTreasureWorld(EconomyWallet shared,
            GameSession session,Original323TreasureHunt originalHunt,
            Original323MobileTreasureSession mobileSearch,
            IOriginal323BountyChestOpenWitness verifiedSource=null,
            IOriginal323OnePieceNativeAbilityWorld nativeAbility=null)
        {
            wallet=shared ?? throw new ArgumentNullException(nameof(shared));
            game=session ?? throw new ArgumentNullException(nameof(session));
            hunt=originalHunt ?? throw new ArgumentNullException(nameof(originalHunt));
            mobile=mobileSearch ?? throw new ArgumentNullException(nameof(mobileSearch));
            source=verifiedSource; native=nativeAbility;
            // Source LV[player]=5 in CUL; once per game, never reset on a
            // later node choice, title selection, or a resumed session.
            const string id=InitReceiptId;
            if(!wallet.HasCompletedTransaction(id))
            {
                try
                {
                    wallet.TryGrantOnce(id,new ResourceBundle().Add(
                        MapsRemainingId,InitialMaps));
                }
                catch
                {
                    if(!wallet.HasCompletedTransaction(id)) throw;
                }
            }
        }

        public int MapsRemaining => (int)wallet.Balance(MapsRemainingId);
        public bool OnePieceRecorded =>
            wallet.HasCompletedTransaction(FinalReceiptId);
        public bool NativeOnePiecePending =>
            wallet.Balance(NativeAbilitiesPendingId)>0;
        private static string Receipt(string id) => "source:bounty:CRW:open:"+id;

        private Original323CrwResult Refuse(string why) =>
            new Original323CrwResult(false,Original323CrwOutcome.Unavailable,
                0,MapsRemaining,why);

        public Original323CrwResult ObserveChestOpen(string eventId)
        {
            if(source==null || game.Phase!=SessionPhase.Running ||
                processing || string.IsNullOrWhiteSpace(eventId) ||
                eventId.Length>64)
                return Refuse("원본 바운티헌터 CRW 상자 개봉 증빙 필요");
            int roll,gold; string evidence;
            if(!source.TryObserveOriginalCrwOpen(eventId,game.CurrentWave,
                    out roll,out gold,out evidence))
                return Refuse("원본 CRW 시전/보상 사건 미확인");
            return ProcessObserved(eventId,game.CurrentWave,roll,gold,evidence);
        }

        // The standalone original observer is permitted only for the
        // source-confirmed BeM map prize, not for fabricated chest rolls.
        public Original323CrwResult ProcessObservedMapPrize(string eventId,
            int round,int observedRemaining,string evidence)
        {
            if(round!=game.CurrentWave || game.Phase!=SessionPhase.Running ||
                observedRemaining<0 || observedRemaining>4)
                return Refuse("바운티헌터 지도 남은 횟수/라운드 불일치");
            return Commit(eventId,round,Original323CrwOutcome.TreasureMap,
                0,observedRemaining,evidence);
        }

        private Original323CrwResult ProcessObserved(string eventId,int round,
            int originalRoll,int sourceGold,string evidence)
        {
            if(originalRoll<1 || originalRoll>100 ||
                round!=game.CurrentWave || game.Phase!=SessionPhase.Running)
                return Refuse("원본 CRW 추첨값/라운드 불일치");
            Original323CrwOutcome branch;
            if(originalRoll<=20) branch=Original323CrwOutcome.Wood;
            else if(originalRoll<=40) branch=Original323CrwOutcome.RandomWisp;
            else if(originalRoll<=95 || MapsRemaining==0)
                branch=Original323CrwOutcome.Gold;
            else branch=Original323CrwOutcome.TreasureMap;
            if(branch==Original323CrwOutcome.Gold)
            {
                if(sourceGold<MinGold || sourceGold>MaxGold)
                    return Refuse("원본 CRW 골드 보상 실측값 누락/범위 오류");
            }
            else if(sourceGold!=0)
                return Refuse("골드가 없는 CRW 분기에서 골드 관측됨");
            return Commit(eventId,round,branch,sourceGold,
                branch==Original323CrwOutcome.TreasureMap ?
                    MapsRemaining-1 : MapsRemaining,evidence);
        }

        private Original323CrwResult Commit(string eventId,int round,
            Original323CrwOutcome branch,int gold,int expectedRemaining,
            string evidence)
        {
            if(processing || string.IsNullOrWhiteSpace(eventId) ||
                eventId.Length>64 ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence) ||
                round<1 || round>65 || game.CurrentWave!=round ||
                game.Phase!=SessionPhase.Running)
                return Refuse("중복/미검증 CRW 상자 사건");
            bool finalMap=branch==Original323CrwOutcome.TreasureMap &&
                expectedRemaining==0;
            string receipt=finalMap ? FinalReceiptId : Receipt(eventId);
            if(wallet.HasCompletedTransaction(receipt))
                return Refuse("이미 정산한 CRW 상자");
            if(pendingMapId!=null)
                return Refuse("앞선 CRW 지도 보물 출현 후속 정산 대기");
            processing=true;
            try
            {
                var prizes=new ResourceBundle();
                var cost=new ResourceBundle();
                bool map=branch==Original323CrwOutcome.TreasureMap;
                int actual=MapsRemaining;
                if(actual<0 || actual>InitialMaps ||
                    (map && (actual<=0 || actual-1!=expectedRemaining)))
                    return Refuse("원본 LV 보물지도 남은 횟수 불일치");
                if(map)
                    cost.Add(MapsRemainingId,1);
                if(branch==Original323CrwOutcome.Wood)
                    prizes.Add("wood",1);
                else if(branch==Original323CrwOutcome.RandomWisp)
                    prizes.Add(WispResourceIds.Random,1);
                else if(branch==Original323CrwOutcome.Gold)
                    prizes.Add("gold",gold);
                else if(!map)
                    return Refuse("알 수 없는 CRW 보상 분기");

                if(finalMap)
                {
                    // A0KV belongs to record guidance; the other abilities
                    // belong to i4. Native application is a separate obligation.
                    prizes.Add(OnePieceRecordId,1)
                        .Add(OnePieceI4A0KT,1).Add(OnePieceI4A0KU,1)
                        .Add(OnePieceI4A0KS,1).Add(OnePieceI4A0KR,1)
                        .Add(LineExplosionIncreaseId,OnePieceLineIncrease)
                        .Add(LineMagicDefenseIncreaseId,OnePieceLineIncrease)
                        .Add(NativeAbilitiesPendingId,1);
                }

                bool committed;
                try
                {
                    committed=wallet.TryExchangeOnce(receipt,cost,prizes);
                }
                catch
                {
                    if(!wallet.HasCompletedTransaction(receipt)) throw;
                    committed=true; // Granted observer failed AFTER commit.
                }
                if(!committed) return Refuse("CRW 거래 미정산");
                if(map && !finalMap)
                {
                    // Source LV debit is already durable. Preserve this
                    // precise obligation until BeM(3) registers atomically.
                    pendingMapId=eventId;pendingMapRound=round;
                    pendingMapRemaining=expectedRemaining;
                    pendingMapEvidence=evidence;
                    if(!ReconcileMapSpawn())
                        return Refuse("지도 차감 완료 · 보물 3개 출현 재처리 대기");
                }
                if(finalMap)
                {
                    pendingNativeEvent=eventId;
                    pendingNativeEvidence=evidence;
                    TryReconcileOnePieceNative();
                    return new Original323CrwResult(true,
                        Original323CrwOutcome.OnePiece,0,MapsRemaining,
                        NativeOnePiecePending ?
                        "원피스 발견 · LL/LK +10 · 원본 능력 적용 대기" :
                        "원피스 발견 · LL/LK +10 · 기록 능력 적용");
                }
                return new Original323CrwResult(true,branch,gold,MapsRemaining,
                    branch==Original323CrwOutcome.Wood ? "목재 1 획득" :
                    branch==Original323CrwOutcome.RandomWisp ? "랜덤위습 1 획득" :
                    branch==Original323CrwOutcome.Gold ? "골드 "+gold+" 획득" :
                    "보물지도 발견 · 보물 3개 추가 생성");
            }
            catch(Exception error)
            {
                return Refuse("CRW 정산 중단 · 영수증 재확인 필요: "+error.Message);
            }
            finally {processing=false;}
        }

        // Retry the SAME committed map obligation without consuming
        // another source chest or LV charge. Called from the combat tick.
        public bool ReconcileMapSpawn()
        {
            if(pendingMapId==null) return true;
            if(mobile.RegisterObservedTreasureMapPrize(
                hunt,pendingMapId,pendingMapRound,pendingMapRemaining,
                pendingMapEvidence))
            {
                pendingMapId=null;pendingMapEvidence=null;
                return true;
            }
            return false;
        }

        public bool TryReconcileOnePieceNative() =>
            pendingNativeEvent!=null &&
            TryApplyPendingOnePieceNativeAbilities(pendingNativeEvent,
                pendingNativeEvidence);

        public bool TryApplyPendingOnePieceNativeAbilities(
            string sourceEventId,string evidence)
        {
            if(native==null || !OnePieceRecorded || !NativeOnePiecePending ||
                game.Phase!=SessionPhase.Running ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;
            const string done="source:bounty:CRW:one-piece-native-applied";
            if(wallet.HasCompletedTransaction(done)) return true;
            if(!native.TryApplyOnePieceOriginalAbilitiesOnce(done,evidence))
                return false;
            bool applied;
            try
            {
                applied=wallet.TryExchangeOnce(done,
                    new ResourceBundle().Add(NativeAbilitiesPendingId,1),
                    new ResourceBundle());
            }
            catch
            {
                applied=wallet.HasCompletedTransaction(done);
            }
            if(applied)
            {
                pendingNativeEvent=null;pendingNativeEvidence=null;
            }
            return applied;
        }
    }
}