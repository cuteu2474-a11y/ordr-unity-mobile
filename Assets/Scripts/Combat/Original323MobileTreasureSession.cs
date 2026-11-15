using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // External bounty-hunter CRW receipt. This is NOT a new manual map button.
    public interface IOriginal323TreasureMapPrizeWitness
    {
        bool TryConfirmMapPrize(string eventId,int round,
            out int remainingMapCharges,out string sourceEvidence);
    }

    // Optional native spell gate. Its implementation owns real A05D mana,
    // availability and cooldown; BJI alarms are NEVER used as a substitute.
    public interface IOriginal323MobileTreasureA05DCast
    {
        bool TryConfirmActualA05DCast(int round,double targetX,
            double targetY,out string originalCastEvidence);
    }

    // Explicit mobile adaptation of BeM/BFj/CJm/BeN: NOT a native JASS cast.
    // A tap samples an internal map position; the existing square geometry
    // and receipt-backed BeI payout still decide the actual result.
    public sealed partial class Original323MobileTreasureSession :
        IOriginal323MobileTreasureRevealedPolicy,
        IOriginal323TreasureLiveCastWitness,
        IOriginal323TreasureCastOriginWitness,
        IOriginal323GaimonCcqEventWitness
    {
        private const int PositionSteps=1000000;
        private readonly IOriginalWispRandom rng;
        private readonly Func<bool> dream;
        private readonly Func<bool> dreamNami;
        private readonly Func<bool> originalYlAtSearch;
        private readonly IOriginal323MobileTreasureA05DCast nativeA05d;
        private readonly IOriginal323GaimonCcqEventWitness ccqEvents;
        private string pendingCcqEvent;
        private string pendingCcqProof;
        private int pendingCcqRound;
        private double pendingCcqX,pendingCcqY;
        private string sessionId=Guid.NewGuid().ToString("N");
        private readonly HashSet<int> spawned=new HashSet<int>();
        private readonly HashSet<string> mapPrizes=new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> authorized=new HashSet<string>(StringComparer.Ordinal);
        private MobileTreasureAttempt pending;
        private int nextAttempt;

        // The mobile substitution does not claim external original calibration.
        public bool IsCalibrationApproved => false;
        public string CalibrationEvidence => "mobile-bfj-geometry-v1";

        public Original323MobileTreasureSession(IOriginalWispRandom random,
            Func<bool> dreamApplied, Func<bool> namiAtDream,
            Func<bool> originalYlFlag=null,
            IOriginal323MobileTreasureA05DCast sourceA05d=null,
            IOriginal323GaimonCcqEventWitness sourceCcq=null)
        {
            rng=random ?? throw new ArgumentNullException(nameof(random));
            dream=dreamApplied ?? throw new ArgumentNullException(nameof(dreamApplied));
            dreamNami=namiAtDream ?? throw new ArgumentNullException(nameof(namiAtDream));
            originalYlAtSearch=originalYlFlag;
            nativeA05d=sourceA05d;
            ccqEvents=sourceCcq;
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
                var packet=new List<Original323ObservedTreasureSpawn>();
                for(int i=0;i<points.Count;i++)
                {
                    string id="mobile-port:"+sessionId+":"+round+":"+i;
                    packet.Add(new Original323ObservedTreasureSpawn(id,
                        points[i][0],points[i][1],"mobile-port:BFj:"+id));
                }
                // The batch validates before any chest changes. A failed
                // wave cannot leave half of its five chests registered.
                hunt.RegisterObservedSpawnBatch(round,round,packet);
                spawned.Add(round);
                added+=points.Count;
            }
            return added;
        }

        // CRW map prizes 1-4 each spawn three fresh chests. The fifth
        // (remaining=0) instead awards the One Piece record and its native
        // abilities; that separate award is never fabricated here.
        public bool RegisterObservedTreasureMapPrize(
            Original323TreasureHunt hunt,string eventId,int round,
            int remainingAfter,string evidence)
        {
            if(hunt==null || string.IsNullOrWhiteSpace(eventId) ||
                eventId.Length>64 || round<1 || round>65 ||
                remainingAfter<1 || remainingAfter>4 ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence) ||
                mapPrizes.Contains(eventId) ||
                mapPrizes.Count!=4-remainingAfter) return false;
            var packet=new List<Original323ObservedTreasureSpawn>();
            for(int i=0;i<Original323TreasureRules.TreasureMapBonusSpawnCount;i++)
            {
                double x=Position(Original323TreasureRules.MinX,
                    Original323TreasureRules.MaxX);
                double y=Position(Original323TreasureRules.MinY,
                    Original323TreasureRules.MaxY);
                while(x==0 && y==0)
                {
                    x=Position(Original323TreasureRules.MinX,
                        Original323TreasureRules.MaxX);
                    y=Position(Original323TreasureRules.MinY,
                        Original323TreasureRules.MaxY);
                }
                string id="mobile-port:"+sessionId+":map:"+eventId+":"+i;
                if(id.Length>128) return false;
                packet.Add(new Original323ObservedTreasureSpawn(id,x,y,
                    "mobile-port:BFj:"+id+":"+evidence));
            }
            hunt.RegisterObservedSpawnBatch(0,round,packet);
            mapPrizes.Add(eventId);
            return true;
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
            bool yl=originalYlAtSearch!=null && originalYlAtSearch();
            consumed=new MobileTreasureAttempt(
                "mobile-port:"+sessionId+":attempt:"+(++nextAttempt),
                Original323VerifiedMonsterWaveCatalog.SourceW3xSha256,
                round,x,y,gaimon,nami,yl,true);
            pending=consumed;
            return true;
        }

        // Only an actually observed CCQ player-R event supplies the source
        // start location. CCQ exposes treasures, not a new A05D attempt.
        public bool TryObserveOriginalCcq(string eventId,int round,
            out double startX,out double startY,out string evidence)
        {
            startX=0;startY=0;evidence=null;
            if(ccqEvents==null || pendingCcqEvent!=null ||
                string.IsNullOrWhiteSpace(eventId) ||
                !ccqEvents.TryObserveOriginalCcq(eventId,round,
                    out startX,out startY,out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;
            pendingCcqEvent=eventId;pendingCcqRound=round;
            pendingCcqX=startX;pendingCcqY=startY;
            pendingCcqProof=evidence;
            return true;
        }

        public bool TryConfirmOriginalCast(double x,double y,
            double halfExtent,bool collect,int round,out string evidence)
        {
            evidence=null;
            if(!collect)
            {
                if(pendingCcqEvent==null || pendingCcqRound!=round ||
                    pendingCcqX!=x || pendingCcqY!=y ||
                    halfExtent!=Original323TreasureRules.RevealHalfExtent)
                    return false;
                evidence="mobile-port:CCQ:"+pendingCcqEvent+":"+pendingCcqProof;
                pendingCcqEvent=null;pendingCcqProof=null;
                // CCQ is reveal-only. It must never appear in the A05D
                // caster journal or corrupt its save-state validation.
                return true;
            }
            if(pending==null || pending.Round!=round ||
                pending.InternalX!=x || pending.InternalY!=y ||
                halfExtent!=Original323TreasureRules.SearchHalfExtent(
                    pending.Gaimon,pending.Nami))
                return false;
            string nativeEvidence=null;
            if(nativeA05d!=null &&
                (!nativeA05d.TryConfirmActualA05DCast(round,x,y,
                    out nativeEvidence) ||
                 !Original323BossRoundRules.TrustedRuntimeEvidence(nativeEvidence)))
            {
                // A rejected native spell is not a permanently locked ticket.
                pending=null;
                return false;
            }
            evidence="mobile-port:A05D:"+pending.Id+
                (nativeEvidence==null ? "" : ":"+nativeEvidence);
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
