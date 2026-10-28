using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Source CCQ is a player R reveal at the ORIGINAL start location.
    // The caller never supplies a free-search coordinate or new loot.
    public interface IOriginal323GaimonCcqEventWitness
    {
        bool TryObserveOriginalCcq(string originalEventId, int originalRound,
            out double playerStartX, out double playerStartY,
            out string originalEventEvidence);
    }

    public sealed class Original323GaimonRevealResult
    {
        public bool Authorized { get; private set; }
        public int OriginalRound { get; private set; }
        public int RevealedCount { get; private set; }
        public string Message { get; private set; }
        internal Original323GaimonRevealResult(bool authorized,int round,
            int count,string message)
        {
            Authorized=authorized;
            OriginalRound=round;
            RevealedCount=count;
            Message=message;
        }
    }

    // Mobile follows the user decision: Q/W/R automatic, manual treasure
    // search remains a single gacha button. CCQ reveals ONLY; its observed
    // outcome never pays a Wisp, Berry, or rolls the original BeI stream.
    public sealed class Original323GaimonRevealPort
    {
        private readonly Original323TreasureHunt hunt;
        private readonly IOriginal323GaimonCcqEventWitness source;
        private readonly HashSet<string> consumedEvents =
            new HashSet<string>(StringComparer.Ordinal);
        private bool processing;
        public int ObservedEventCount => consumedEvents.Count;
        public Original323GaimonRevealResult LastAuthorizedReveal
            { get; private set; }

        public Original323GaimonRevealPort(Original323TreasureHunt originalHunt,
            IOriginal323GaimonCcqEventWitness sourceCcq)
        {
            hunt=originalHunt ??
                throw new ArgumentNullException(nameof(originalHunt));
            source=sourceCcq ??
                throw new ArgumentNullException(nameof(sourceCcq));
        }

        public Original323GaimonRevealResult ObserveOriginalCcq(
            string eventId,int round,bool ownsGaimon,bool dreamApplied)
        {
            if(processing || !ownsGaimon || !dreamApplied ||
                round<1 || round>65 || string.IsNullOrWhiteSpace(eventId) ||
                eventId.Length>128 || consumedEvents.Contains(eventId))
                return Reject(round,"가이몬 꿈·원본 CCQ 이벤트 확인 필요");

            processing=true;
            try
            {
                double x,y;string witnessEvidence;
                if(!source.TryObserveOriginalCcq(eventId,round,
                    out x,out y,out witnessEvidence) ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(
                        witnessEvidence) ||
                    !Original323TreasureRules.Finite(x) ||
                    !Original323TreasureRules.Finite(y))
                    return Reject(round,"원본 CCQ 시전 또는 시작 위치 미검증");

                // Native CCQ is already observed. Consume this event before
                // calling another world component; failed authorization must
                // not permit replaying the same spell ID.
                if(!consumedEvents.Add(eventId))
                    return Reject(round,"CCQ 중복 이벤트 차단");

                // The native world must independently confirm the same
                // reveal-only spell. SearchObserved passes collect=false
                // and 2000 source radius. It never calls BeI or BFi.
                var found=hunt.SearchObserved(round,x,y,
                    true,false,false,false,true);
                if(!found.WasAuthorized)
                    return Reject(round,"원본 CCQ 공개 시전 승인 실패");

                var report=new Original323GaimonRevealResult(true,round,
                    found.Matched,
                    "보물 위치 공개 "+found.Matched+
                    "개 · 상자는 소비하지 않았으며 보상도 지급하지 않았습니다.");
                LastAuthorizedReveal=report;
                return report;
            }
            catch(Exception)
            {
                // No extra attempt to replay an already consumed CCQ ID.
                return Reject(round,"원본 CCQ 이벤트 처리 실패 · 재시전하지 않음");
            }
            finally {processing=false;}
        }

        private static Original323GaimonRevealResult Reject(
            int round,string message) =>
            new Original323GaimonRevealResult(false,round,0,message);
    }
}
