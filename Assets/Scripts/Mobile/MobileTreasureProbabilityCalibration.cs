using System;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Mobile
{
    // This is the exact BFj uniform chest-placement + BeH square-hit
    // probability for a FIXED search center BEFORE discoveries.
    // It is NOT an original-player hit-rate or an approved mobile gacha policy.
    public static class MobileTreasureProbabilityCalibration
    {
        public const string Mode = "SOURCE_GEOMETRY_ONLY_NOT_PLAYER_PARITY";
        public static TreasureGeometryProbability FixedCenter(
            double centerX,double centerY,double halfExtent,int freshChests)
        {
            if(!Original323TreasureRules.Finite(centerX) ||
               !Original323TreasureRules.Finite(centerY) ||
               !Original323TreasureRules.Finite(halfExtent) ||
               halfExtent<0 || freshChests<0 || freshChests>100000)
                throw new ArgumentOutOfRangeException(nameof(freshChests));

            double xmin=Math.Max(Original323TreasureRules.MinX,centerX-halfExtent);
            double xmax=Math.Min(Original323TreasureRules.MaxX,centerX+halfExtent);
            double ymin=Math.Max(Original323TreasureRules.MinY,centerY-halfExtent);
            double ymax=Math.Min(Original323TreasureRules.MaxY,centerY+halfExtent);
            double overlapX=Math.Max(0d,xmax-xmin);
            double overlapY=Math.Max(0d,ymax-ymin);
            double total=(Original323TreasureRules.MaxX-Original323TreasureRules.MinX)*
                (Original323TreasureRules.MaxY-Original323TreasureRules.MinY);
            double hit=Math.Max(0d,Math.Min(1d,overlapX*overlapY/total));
            double miss=freshChests==0 ? 1d : Math.Pow(1d-hit,freshChests);
            double single=freshChests==0 ? 0d :
                freshChests*hit*Math.Pow(1d-hit,freshChests-1);
            return new TreasureGeometryProbability(hit,miss,single,
                Math.Max(0d,1d-miss-single),freshChests);
        }

        // Each cohort must be captured from independently observed player
        // play and the mobile adaptation. Counts are descriptive only: this
        // cannot flip IOriginal323MobileTreasureGachaPolicy approval.
        public static TreasureDistributionComparison CompareObserved(
            TreasureDistributionSample source,
            TreasureDistributionSample mobile)
        {
            if(source==null || mobile==null)
                throw new ArgumentNullException(source==null?nameof(source):nameof(mobile));
            if(source.Round!=mobile.Round ||
               source.Gaimon!=mobile.Gaimon ||
               source.Nami!=mobile.Nami ||
               source.ChestsAtStart!=mobile.ChestsAtStart)
                throw new ArgumentException("비교 표본의 라운드·가이몬·나미·잔여 보물 조건 불일치");
            if(source.Attempts<1000 || mobile.Attempts<1000)
                return new TreasureDistributionComparison(false,
                    "각 비교군의 실제 탐색 표본이 1000회 미만입니다.",
                    0d,0d,0d);

            double miss=Difference(source.Misses,source.Attempts,
                mobile.Misses,mobile.Attempts);
            double single=Difference(source.SingleFinds,source.Attempts,
                mobile.SingleFinds,mobile.Attempts);
            double multi=Difference(source.MultiFinds,source.Attempts,
                mobile.MultiFinds,mobile.Attempts);
            // Conservative two-sided 95% confidence intervals, separated
            // for 0 / 1 / 2+ treasure outcomes.
            bool consistent=Similar(source.Misses,source.Attempts,
                    mobile.Misses,mobile.Attempts) &&
                Similar(source.SingleFinds,source.Attempts,
                    mobile.SingleFinds,mobile.Attempts) &&
                Similar(source.MultiFinds,source.Attempts,
                    mobile.MultiFinds,mobile.Attempts);

            // Statistical agreement does NOT prove source-parity: event
            // timing, reward totals, attempt availability and reveal strategy
            // still require independent evidence. Never self-approve here.
            return new TreasureDistributionComparison(consistent,
                consistent ? "발견 건수 분포 통계는 일치 범위 · 실제 수급/행동 조건 검증 별도" :
                "꽝·단일·복수 발견 중 원본 플레이 표본과 분포 불일치",
                miss,single,multi);
        }

        private static double Difference(int a,int an,int b,int bn)=>
            Math.Abs((double)a/an-(double)b/bn);

        private static bool Similar(int a,int an,int b,int bn)
        {
            double ap=(double)a/an,bp=(double)b/bn;
            double pooled=(double)(a+b)/(an+bn);
            double error=Math.Sqrt(pooled*(1d-pooled)*
                (1d/an+1d/bn));
            return Math.Abs(ap-bp)<=1.96d*error+1d/Math.Max(an,bn);
        }
    }

    public sealed class TreasureGeometryProbability
    {
        public double PerChestHit {get;}
        public double Miss {get;}
        public double Single {get;}
        public double Multiple {get;}
        public int UncollectedFreshChests {get;}
        public TreasureGeometryProbability(double perChest,double miss,
            double single,double multiple,int count)
        {
            PerChestHit=perChest;Miss=miss;Single=single;
            Multiple=multiple;UncollectedFreshChests=count;
        }
    }

    public sealed class TreasureDistributionSample
    {
        public int Round {get;}
        public bool Gaimon {get;}
        public bool Nami {get;}
        public int ChestsAtStart {get;}
        public int Attempts {get;}
        public int Misses {get;}
        public int SingleFinds {get;}
        public int MultiFinds {get;}
        public TreasureDistributionSample(int round,bool gaimon,bool nami,
            int chests,int misses,int singles,int multi)
        {
            if(round<1 || round>65 || chests<0 || chests>23 ||
               (nami&&!gaimon) || misses<0 || singles<0 || multi<0)
                throw new ArgumentException("관측 표본의 원본 상태가 잘못됐습니다.");
            long n=(long)misses+singles+multi;
            if(n<=0 || n>int.MaxValue || (chests==0 && singles+multi>0))
                throw new ArgumentException("관측 표본에서 탐색 횟수/결과 불일치");
            Round=round;Gaimon=gaimon;Nami=nami;ChestsAtStart=chests;
            Attempts=(int)n;Misses=misses;SingleFinds=singles;MultiFinds=multi;
        }
    }

    public sealed class TreasureDistributionComparison
    {
        // This is a statistical signal, never a game unlock token.
        public bool DistributionsConsistent {get;}
        public string Description {get;}
        public double MissDifference {get;}
        public double SingleDifference {get;}
        public double MultipleDifference {get;}
        public TreasureDistributionComparison(bool consistent,string description,
            double miss,double single,double multiple)
        {
            DistributionsConsistent=consistent;Description=description;
            MissDifference=miss;SingleDifference=single;
            MultipleDifference=multiple;
        }
    }
}
