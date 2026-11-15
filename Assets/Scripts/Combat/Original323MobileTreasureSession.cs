using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // Explicit mobile adaptation of BeM/BFj/CJm/BeN: NOT a native JASS cast.
    // A tap samples an internal map position; the existing square geometry
    // and receipt-backed BeI payout still decide the actual result.
    public sealed class Original323MobileTreasureSession :
        IOriginal323MobileTreasureRevealedPolicy,
        IOriginal323TreasureLiveCastWitness,
        IOriginal323TreasureCastOriginWitness
    {
        private const int PositionSteps=1000000;
        private readonly IOriginalWispRandom rng;
        private readonly Func<bool> dream;
        private readonly Func<bool> dreamNami;
        private readonly string sessionId=Guid.NewGuid().ToString("N");
        private readonly HashSet<int> spawned=new HashSet<int>();
        private readonly HashSet<string> authorized=new HashSet<string>(StringComparer.Ordinal);
        private MobileTreasureAttempt pending;
        private int nextAttempt;

        // The mobile substitution does not claim external original calibration.
        public bool IsCalibrationApproved => false;
        public string CalibrationEvidence => "mobile-bfj-geometry-v1";

        public Original323MobileTreasureSession(IOriginalWispRandom random,
            Func<bool> dreamApplied, Func<bool> namiAtDream)
        {
            rng=random ?? throw new ArgumentNullException(nameof(random));
            dream=dreamApplied ?? throw new ArgumentNullException(nameof(dreamApplied));
            dreamNami=namiAtDream ?? throw new ArgumentNullException(nameof(namiAtDream));
        }

        private double Position(double min,double max)
        {
            int draw=rng.NextInclusive(0,PositionSteps);
            if(draw<0 || draw>PositionSteps)
                throw new InvalidOperationException("BFj 모바일 위치 난수 범위 오류");
            return min+(max-min)*(double)draw/PositionSteps;
        }

        public int SpawnThroughRound(Original323TreasureHunt hunt,int currentRound)
        {
            if(hunt==null) throw new ArgumentNullException(nameof(hunt));
            if(currentRound<1 || currentRound>65) return 0;
            int added=0;
            for(int round=10;round<=50 && round<=currentRound;round+=10)
            {
                if(spawned.Contains(round)) continue;
                var points=new List<double[]>();
                for(int i=0;i<Original323TreasureRules.ScheduledSpawnCount;i++)
                {
                    double x,y;
                    do
                    {
                        x=Position(Original323TreasureRules.MinX,Original323TreasureRules.MaxX);
                        y=Position(Original323TreasureRules.MinY,Original323TreasureRules.MaxY);
                    } while(x==0 && y==0);
                    points.Add(new [] {x,y});
                }
                for(int i=0;i<points.Count;i++)
                    hunt.RegisterMobilePortSpawn(
                        "mobile-port:"+sessionId+":"+round+":"+i,
                        round,points[i][0],points[i][1]);
                spawned.Add(round);
                added+=points.Count;
            }
            return added;
        }

        public bool TryConsumeApprovedAttempt(int round,int remaining,
            out MobileTreasureAttempt consumed)
        {
            return Consume(round,remaining,null,out consumed);
        }

        public bool TryConsumeRevealedAttempt(int round,int remaining,
            IReadOnlyList<Original323TreasureLocation> revealed,
            out MobileTreasureAttempt consumed)
        {
            return Consume(round,remaining,revealed,out consumed);
        }

        private bool Consume(int round,int remaining,
            IReadOnlyList<Original323TreasureLocation> revealed,
            out MobileTreasureAttempt consumed)
        {
            consumed=null;
            if(round<1 || round>65 || remaining<=0 || pending!=null)
                return false;
            // A revealed coordinate is known information, not a free payout.
            double x=revealed!=null && revealed.Count>0 ? revealed[0].X :
                Position(Original323TreasureRules.MinX,Original323TreasureRules.MaxX);
            double y=revealed!=null && revealed.Count>0 ? revealed[0].Y :
                Position(Original323TreasureRules.MinY,Original323TreasureRules.MaxY);
            bool gaimon=dream();
            bool nami=gaimon && dreamNami();
            consumed=new MobileTreasureAttempt(
                "mobile-port:"+sessionId+":attempt:"+(++nextAttempt),
                Original323VerifiedMonsterWaveCatalog.SourceW3xSha256,
                round,x,y,gaimon,nami,nami,true);
            pending=consumed;
            return true;
        }

        public bool TryConfirmOriginalCast(double x,double y,
            double halfExtent,bool collect,int round,out string evidence)
        {
            evidence=null;
            if(pending==null || !collect || pending.Round!=round ||
                pending.InternalX!=x || pending.InternalY!=y ||
                halfExtent!=Original323TreasureRules.SearchHalfExtent(
                    pending.Gaimon,pending.Nami))
                return false;
            evidence="mobile-port:A05D:"+pending.Id;
            authorized.Add(evidence);
            pending=null;
            return true;
        }

        public bool TryIdentifyOriginalTreasureCaster(
            string evidence,out bool recordGuidanceCaster)
        {
            // The left-menu tap stands in for record-guidance's A05D only.
            recordGuidanceCaster=authorized.Contains(evidence ?? "");
            return recordGuidanceCaster;
        }
    }
}
