using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // A manually tapped mobile hunt must consume an externally verified,
    // original-equivalent opportunity. No guessed fixed win rate or position.
    public interface IOriginal323MobileTreasureGachaPolicy
    {
        bool IsCalibrationApproved { get; }
        string CalibrationEvidence { get; }
        bool TryConsumeApprovedAttempt(int round,int remainingChests,
            out MobileTreasureAttempt consumed);
    }

    public sealed class MobileTreasureAttempt
    {
        public string Id { get; private set; }
        public string OriginalW3xSha256 { get; private set; }
        public int Round { get; private set; }
        public double InternalX { get; private set; }
        public double InternalY { get; private set; }
        public bool Gaimon { get; private set; }
        public bool Nami { get; private set; }
        public bool ExtraQuantityRoll { get; private set; }
        public bool BerryDisabled { get; private set; }

        // Internal coordinates are never displayed or requested from the player.
        // Policy must have calibrated exploration against original-map play.
        public MobileTreasureAttempt(string id,string sourceHash,int wave,
            double internalX,double internalY,bool gaimon,bool nami,
            bool extraQuantityRoll,bool berryDisabled)
        {
            if(string.IsNullOrWhiteSpace(id)||id.Length>128 ||
                string.IsNullOrWhiteSpace(sourceHash) || wave<1||wave>65 ||
                !Original323TreasureRules.Finite(internalX) ||
                !Original323TreasureRules.Finite(internalY) ||
                (nami && !gaimon))
                throw new ArgumentException("원본 보물 탐색의 검증된 기회·상태가 필요합니다.");
            Id=id;OriginalW3xSha256=sourceHash;Round=wave;
            InternalX=internalX;InternalY=internalY;
            Gaimon=gaimon;Nami=nami;
            ExtraQuantityRoll=extraQuantityRoll;BerryDisabled=berryDisabled;
        }
    }

    public enum MobileTreasureOutcome { Unavailable, Miss, Found, Partial, Uncertain }

    public sealed class MobileTreasureGachaResult
    {
        public MobileTreasureOutcome Outcome { get; private set; }
        public int Matched { get; private set; }
        public int Collected { get; private set; }
        public string Message { get; private set; }
        public IReadOnlyList<Original323TreasureReward> Rewards { get; private set; }

        internal MobileTreasureGachaResult(MobileTreasureOutcome outcome,
            int matched,int collected,string message,
            IReadOnlyList<Original323TreasureReward> rewards=null)
        {
            Outcome=outcome;Matched=matched;Collected=collected;Message=message;
            Rewards=rewards ?? new Original323TreasureReward[0];
        }

        public string RewardText()
        {
            var lines=new List<string>();
            foreach(var reward in Rewards)
            {
                string name=reward.WispResourceId==WispResourceIds.Random ?
                    "랜덤위습" : reward.WispResourceId==WispResourceIds.Choice ?
                    "선택위습" : "안흔함위습";
                lines.Add(name+" "+reward.WispCount+"개"+
                    (reward.BonusBerry ? " · 베리 1개 추가" : ""));
            }
            return string.Join(" / ",lines.ToArray());
        }
    }

    // Never executes automatically: one button tap consumes at most one
    // source-approved attempt. Both original cast and wallet must also approve.
    public sealed class MobileTreasureGacha
    {
        private readonly Original323TreasureHunt hunt;
        private readonly IOriginal323MobileTreasureGachaPolicy policy;
        private readonly HashSet<string> consumedIds=
            new HashSet<string>(StringComparer.Ordinal);
        private bool inProgress;
        public int AttemptsUsed => consumedIds.Count;
        public bool CanAttempt => policy.IsCalibrationApproved &&
            Original323BossRoundRules.TrustedRuntimeEvidence(policy.CalibrationEvidence);

        public MobileTreasureGacha(Original323TreasureHunt source,
            IOriginal323MobileTreasureGachaPolicy calibratedPolicy)
        {
            hunt=source ?? throw new ArgumentNullException(nameof(source));
            policy=calibratedPolicy ??
                throw new ArgumentNullException(nameof(calibratedPolicy));
        }

        public MobileTreasureGachaResult TrySearch(int round)
        {
            if(!CanAttempt || round<1 || round>65)
                return Reject("원본 가챠 당첨률·탐색 기회 검증 대기");
            if(inProgress)return Reject("탐색 거래 처리 중");
            inProgress=true;
            try
            {
                MobileTreasureAttempt ticket;
                // A source ticket may be irreversibly consumed here.
                // Never call twice on a rejected/throwing transaction.
                if(!policy.TryConsumeApprovedAttempt(
                    round,hunt.UncollectedCount,out ticket) || ticket==null)
                    return Reject("원본에 대응하는 탐색 기회가 없습니다.");
                if(!consumedIds.Add(ticket.Id))
                    return Reject("이미 사용한 탐색 기회 · 중복 추첨 차단");
                if(ticket.Round!=round ||
                    ticket.OriginalW3xSha256 !=
                        Original323VerifiedMonsterWaveCatalog.SourceW3xSha256)
                    return Reject("원본 보물 맵 버전·라운드 불일치");
                var source=hunt.SearchObserved(round,ticket.InternalX,
                    ticket.InternalY,ticket.Gaimon,ticket.Nami,
                    ticket.ExtraQuantityRoll,ticket.BerryDisabled,false);
                if(!source.WasAuthorized)
                    return new MobileTreasureGachaResult(
                        MobileTreasureOutcome.Uncertain,0,0,
                        "원본 대응 탐색 시전 확인 실패 · 재추첨 없음");
                if(source.Matched==0)
                    return new MobileTreasureGachaResult(
                        MobileTreasureOutcome.Miss,0,0,
                        "꽝 · 이번 탐색에서 보물이 발견되지 않았습니다.");
                if(source.Collected<source.Matched)
                    return new MobileTreasureGachaResult(
                        MobileTreasureOutcome.Partial,source.Matched,
                        source.Collected,"보물 "+source.Collected+"/"+source.Matched+
                            "개 정산 · 미정산 보상 재추첨 없음",source.Rewards);
                return new MobileTreasureGachaResult(
                    MobileTreasureOutcome.Found,source.Matched,source.Collected,
                    "보물 "+source.Collected+"개 발견!",
                    source.Rewards);
            }
            catch(Exception ex)
            {
                return new MobileTreasureGachaResult(
                    MobileTreasureOutcome.Uncertain,0,0,
                    "원본 정산 여부 확인 필요 · 자동 재시도 없음: "+ex.Message);
            }
            finally { inProgress=false; }
        }

        private static MobileTreasureGachaResult Reject(string message)=>
            new MobileTreasureGachaResult(
                MobileTreasureOutcome.Unavailable,0,0,message);
    }
}
