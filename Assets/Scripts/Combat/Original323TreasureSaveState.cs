using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // Diagnostic checkpoint components: no save screen or live graph swap.
    // Lists and public fields are intentional for Unity JsonUtility.
    [Serializable]
    public sealed class Original323TreasureChestState
    {
        public string Id;
        public int Round;
        public double X;
        public double Y;
        public string SpawnEvidence;
        public bool Collected;
        public string PendingCast;
        public string PendingWisp;
        public int PendingCount;
        public bool PendingBerry;
        public string AwardedWisp;
        public int AwardedCount;
        public bool AwardedBerry;
    }

    [Serializable]
    public sealed class Original323TreasureHuntState
    {
        public int Version=1;
        public string SourceHash=OrdrCoreCheckpoint.SourceW3xSha256;
        public int CollectedCount;
        public string LastAuthorizedCast;
        public string LastCollectedCast;
        public List<Original323TreasureChestState> Chests=
            new List<Original323TreasureChestState>();
    }

    [Serializable]
    public sealed class Original323MobileTreasureState
    {
        public string SessionId;
        public int NextAttempt;
        public List<int> SpawnedRounds=new List<int>();
        public List<string> MapPrizeIds=new List<string>();
        public List<string> AuthorizedCastIds=new List<string>();
    }

    [Serializable]
    public sealed class Original323TreasureGachaState
    {
        public List<string> ConsumedAttemptIds=new List<string>();
    }

    [Serializable]
    public sealed class Original323TreasureAlarmState
    {
        public string Evidence;
        public double DueAt;
        public int Delay;
        public bool RecordGuidanceCaster;
        public bool RevealOnly;
    }

    [Serializable]
    public sealed class Original323TreasureAlarmJournalState
    {
        public List<string> SeenCasts=new List<string>();
        public List<Original323TreasureAlarmState> Pending=
            new List<Original323TreasureAlarmState>();
    }

    [Serializable]
    public sealed class Original323GaimonRevealState
    {
        public List<string> EventIds=new List<string>();
        public List<string> ChestIds=new List<string>();
        public int LastRound;
        public int LastCount;
    }

    [Serializable]
    public sealed class Original323TreasureSessionState
    {
        public Original323TreasureHuntState Hunt;
        public Original323MobileTreasureState Mobile;
        public Original323TreasureGachaState Gacha;
        public Original323TreasureAlarmJournalState Alarms;
        public Original323GaimonRevealState Reveals;
        public bool DreamApplied;
        public bool NamiAtDream;
    }

    public sealed partial class Original323GaimonRevealPort
    {
        public Original323GaimonRevealState CaptureState()
        {
            if(processing)
                throw new InvalidOperationException("보물 위치 공개 도중 저장 불가");
            var state=new Original323GaimonRevealState {
                EventIds=new List<string>(consumedEvents),
                ChestIds=new List<string>(revealedChestIds),
                LastRound=LastAuthorizedReveal?.OriginalRound ?? 0,
                LastCount=LastAuthorizedReveal?.RevealedCount ?? 0
            };
            state.EventIds.Sort(StringComparer.Ordinal);
            state.ChestIds.Sort(StringComparer.Ordinal);
            return state;
        }

        public static void ValidateState(Original323GaimonRevealState state,
            Original323TreasureHuntState hunt)
        {
            if(state==null || hunt==null || state.EventIds==null ||
                state.ChestIds==null || state.LastRound<0 ||
                state.LastRound>65 || state.LastCount<0)
                throw new FormatException("가이몬 공개 보물 저장본 손상");
            var events=new HashSet<string>(StringComparer.Ordinal);
            foreach(string id in state.EventIds)
                if(string.IsNullOrWhiteSpace(id) || id.Length>128 ||
                    !events.Add(id))
                    throw new FormatException("중복 가이몬 CCQ 사건");
            var chests=new HashSet<string>(StringComparer.Ordinal);
            foreach(var chest in hunt.Chests) chests.Add(chest.Id);
            var revealed=new HashSet<string>(StringComparer.Ordinal);
            foreach(string id in state.ChestIds)
                if(string.IsNullOrWhiteSpace(id) || !chests.Contains(id) ||
                    !revealed.Add(id))
                    throw new FormatException("존재하지 않는 가이몬 공개 보물");
            if((state.LastRound==0 && state.LastCount!=0) ||
                (state.LastRound!=0 && events.Count==0))
                throw new FormatException("마지막 CCQ 공개 상태 불일치");
        }

        public void RestoreState(Original323GaimonRevealState state,
            Original323TreasureHuntState huntState)
        {
            if(processing || consumedEvents.Count!=0 ||
                revealedChestIds.Count!=0)
                throw new InvalidOperationException("활성 CCQ 공개 복원 불가");
            ValidateState(state,huntState);
            foreach(string id in state.EventIds) consumedEvents.Add(id);
            foreach(string id in state.ChestIds) revealedChestIds.Add(id);
            if(state.LastRound>0)
                LastAuthorizedReveal=new Original323GaimonRevealResult(
                    true,state.LastRound,state.LastCount,"저장된 위치 공개 정보");
        }
    }

    public sealed partial class Original323TreasureHunt
    {
        public Original323TreasureHuntState CaptureState()
        {
            if(inSearch)
                throw new InvalidOperationException("보물 정산 중 저장 불가");
            var state=new Original323TreasureHuntState {
                CollectedCount=CollectedCount,
                LastAuthorizedCast=LastAuthorizedCastEvidence,
                LastCollectedCast=LastCollectedCastEvidence
            };
            foreach(var chest in ordered)
                state.Chests.Add(new Original323TreasureChestState {
                    Id=chest.OriginalChestId,Round=chest.SpawnRound,
                    X=chest.X,Y=chest.Y,SpawnEvidence=chest.SpawnEvidence,
                    Collected=chest.Collected,
                    PendingCast=chest.PendingCastEvidence,
                    PendingWisp=chest.Pending?.WispResourceId,
                    PendingCount=chest.Pending?.WispCount ?? 0,
                    PendingBerry=chest.Pending?.BonusBerry ?? false,
                    AwardedWisp=chest.Awarded?.WispResourceId,
                    AwardedCount=chest.Awarded?.WispCount ?? 0,
                    AwardedBerry=chest.Awarded?.BonusBerry ?? false
                });
            return state;
        }

        private static Original323TreasureReward ValidateReward(
            string wisp,int count,bool berry)
        {
            if(wisp==null)
            {
                if(count!=0 || berry)
                    throw new FormatException("없는 보물 보상에 수량이 있습니다.");
                return null;
            }
            try { return new Original323TreasureReward(wisp,count,berry); }
            catch(ArgumentException error)
            { throw new FormatException("보물 위습 보상 형식 오류",error); }
        }

        public static void ValidateState(Original323TreasureHuntState state,
            EconomyWallet restoredWallet)
        {
            if(state==null || restoredWallet==null || state.Version!=1 ||
                state.SourceHash!=OrdrCoreCheckpoint.SourceW3xSha256 ||
                state.Chests==null || state.Chests.Count>4096)
                throw new FormatException("보물 저장본 버전/월드가 유효하지 않습니다.");
            int collected=0;
            var ids=new HashSet<string>(StringComparer.Ordinal);
            var roundCount=new Dictionary<int,int>();
            foreach(var c in state.Chests)
            {
                if(c==null || string.IsNullOrWhiteSpace(c.Id) ||
                    c.Id.Length>128 || !ids.Add(c.Id) ||
                    !Original323TreasureRules.IsInOriginalSpawnRect(c.X,c.Y) ||
                    (c.X==0 && c.Y==0) ||
                    (c.Round!=0 && !Original323TreasureRules.IsScheduledSpawnRound(c.Round)) ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(c.SpawnEvidence))
                    throw new FormatException("보물 좌표/ID/라운드 저장본 손상");
                int count;
                roundCount.TryGetValue(c.Round,out count);
                roundCount[c.Round]=count+1;
                if(c.Round!=0 && count+1>Original323TreasureRules.ScheduledSpawnCount)
                    throw new FormatException("라운드 보물 생성 수 초과");
                var pending=ValidateReward(c.PendingWisp,c.PendingCount,c.PendingBerry);
                var awarded=ValidateReward(c.AwardedWisp,c.AwardedCount,c.AwardedBerry);
                if(c.Collected)
                {
                    if(pending!=null || c.PendingCast!=null || awarded==null ||
                        !restoredWallet.HasCompletedTransaction(
                            "original323:treasure:"+c.Id))
                        throw new FormatException("수집 보물과 지갑 영수증 불일치");
                    collected++;
                }
                else if((pending==null && restoredWallet.HasCompletedTransaction(
                    "original323:treasure:"+c.Id)) || awarded!=null ||
                    (pending==null && c.PendingCast!=null) ||
                    (pending!=null && !Original323BossRoundRules.TrustedRuntimeEvidence(
                        c.PendingCast)))
                    throw new FormatException("미수집 보물의 정산 상태 손상");
            }
            if(collected!=state.CollectedCount)
                throw new FormatException("수집 보물 수 장부 불일치");
            if(state.LastAuthorizedCast!=null &&
                !Original323BossRoundRules.TrustedRuntimeEvidence(
                    state.LastAuthorizedCast))
                throw new FormatException("마지막 보물 시전 증빙 오류");
            if(state.LastCollectedCast!=null &&
                !Original323BossRoundRules.TrustedRuntimeEvidence(
                    state.LastCollectedCast))
                throw new FormatException("마지막 수집 증빙 오류");
        }

        // On a NEW hunt with the exact restored wallet; never pay on restore.
        public void RestoreState(Original323TreasureHuntState state,
            EconomyWallet restoredWallet)
        {
            if(inSearch || ordered.Count!=0 || CollectedCount!=0)
                throw new InvalidOperationException("진행 중인 보물 월드 복원 금지");
            ValidateState(state,restoredWallet);
            foreach(var saved in state.Chests)
            {
                RegisterObservedSpawn(saved.Id,saved.Round,
                    saved.X,saved.Y,saved.SpawnEvidence);
                var chest=ordered[ordered.Count-1];
                chest.Collected=saved.Collected;
                chest.Pending=ValidateReward(
                    saved.PendingWisp,saved.PendingCount,saved.PendingBerry);
                chest.PendingCastEvidence=saved.PendingCast;
                chest.Awarded=ValidateReward(
                    saved.AwardedWisp,saved.AwardedCount,saved.AwardedBerry);
                if(chest.Collected) CollectedCount++;
            }
            LastAuthorizedCastEvidence=state.LastAuthorizedCast;
            LastCollectedCastEvidence=state.LastCollectedCast;
            var walletNative=native as Original323TreasureWalletWorld;
            if(walletNative==null &&
                (CollectedCount!=0 || HasPendingTreasure()))
                throw new NotSupportedException(
                    "외부 원본 보물 월드는 정산 복원 포트가 필요합니다.");
            walletNative?.RestoreTreasureReceipts(ordered);
        }

        private bool HasPendingTreasure()
        {
            foreach(var c in ordered) if(c.Pending!=null) return true;
            return false;
        }
    }

    public sealed partial class Original323MobileTreasureSession
    {
        public Original323MobileTreasureState CaptureState()
        {
            if(pending!=null || pendingCcqEvent!=null)
                throw new InvalidOperationException(
                    "소모 중인 보물 탐색/공개 사건이 있어 저장할 수 없습니다.");
            var state=new Original323MobileTreasureState {
                SessionId=sessionId,NextAttempt=nextAttempt,
                SpawnedRounds=new List<int>(spawned),
                MapPrizeIds=new List<string>(mapPrizes),
                AuthorizedCastIds=new List<string>(authorized)
            };
            state.SpawnedRounds.Sort();
            state.MapPrizeIds.Sort(StringComparer.Ordinal);
            state.AuthorizedCastIds.Sort(StringComparer.Ordinal);
            return state;
        }

        public static void ValidateState(Original323MobileTreasureState state,
            Original323TreasureHuntState hunt)
        {
            Guid parsed;
            if(state==null || hunt==null ||
                !Guid.TryParseExact(state.SessionId,"N",out parsed) ||
                state.NextAttempt<0 ||
                state.SpawnedRounds==null ||
                state.MapPrizeIds==null || state.AuthorizedCastIds==null ||
                state.MapPrizeIds.Count>4 || state.SpawnedRounds.Count>5)
                throw new FormatException("보물 탐색 세션 저장본 손상");
            var rounds=new HashSet<int>();
            foreach(int n in state.SpawnedRounds)
                if(!Original323TreasureRules.IsScheduledSpawnRound(n) ||
                    !rounds.Add(n))
                    throw new FormatException("중복 보물 스폰 라운드");
            var maps=new HashSet<string>(StringComparer.Ordinal);
            foreach(string id in state.MapPrizeIds)
                if(string.IsNullOrWhiteSpace(id) || id.Length>64 ||
                    !maps.Add(id))
                    throw new FormatException("중복 보물지도 사건");
            var casts=new HashSet<string>(StringComparer.Ordinal);
            foreach(string id in state.AuthorizedCastIds)
                if(!Original323BossRoundRules.TrustedRuntimeEvidence(id) ||
                    !id.StartsWith("mobile-port:A05D:mobile-port:"+
                        state.SessionId+":attempt:",StringComparison.Ordinal) ||
                    !casts.Add(id))
                    throw new FormatException("보물 사용 증빙 손상");
            foreach(int round in rounds)
            {
                int count=0;
                string prefix="mobile-port:"+state.SessionId+":"+round+":";
                foreach(var chest in hunt.Chests)
                    if(chest.Round==round && chest.Id.StartsWith(prefix,
                            StringComparison.Ordinal)) count++;
                if(count!=Original323TreasureRules.ScheduledSpawnCount)
                    throw new FormatException("라운드 보물과 탐색 세션 불일치");
            }
            foreach(string eventId in maps)
            {
                string prefix="mobile-port:"+state.SessionId+":map:"+eventId+":";
                int count=0;
                foreach(var chest in hunt.Chests)
                    if(chest.Round==0 && chest.Id.StartsWith(prefix,
                            StringComparison.Ordinal)) count++;
                if(count!=Original323TreasureRules.TreasureMapBonusSpawnCount)
                    throw new FormatException("보물지도 3개 출현 상태 불일치");
            }
            foreach(var chest in hunt.Chests)
                if(!chest.Id.StartsWith("mobile-port:"+state.SessionId+":",
                    StringComparison.Ordinal))
                    throw new FormatException("다른 보물 세션의 좌표 포함");
        }

        // Restores stable IDs BEFORE any next cast is generated.
        public void RestoreState(Original323MobileTreasureState saved,
            Original323TreasureHuntState hunt)
        {
            if(spawned.Count!=0 || mapPrizes.Count!=0 ||
                authorized.Count!=0 || pending!=null ||
                pendingCcqEvent!=null || nextAttempt!=0)
                throw new InvalidOperationException("활성 모바일 탐색 복원 불가");
            ValidateState(saved,hunt);
            sessionId=saved.SessionId;
            nextAttempt=saved.NextAttempt;
            foreach(int round in saved.SpawnedRounds) spawned.Add(round);
            foreach(string id in saved.MapPrizeIds) mapPrizes.Add(id);
            foreach(string id in saved.AuthorizedCastIds) authorized.Add(id);
        }
    }

    public sealed partial class Original323TreasureAlarmJournal
    {
        public Original323TreasureAlarmJournalState CaptureState()
        {
            var state=new Original323TreasureAlarmJournalState {
                SeenCasts=new List<string>(seenCasts)
            };
            state.SeenCasts.Sort(StringComparer.Ordinal);
            foreach(var x in pending)
                state.Pending.Add(new Original323TreasureAlarmState {
                    Evidence=x.CastEvidence,DueAt=x.DueAtCombatTime,
                    Delay=x.DelaySeconds,
                    RecordGuidanceCaster=x.RecordGuidanceCaster,
                    RevealOnly=x.RevealOnly
                });
            return state;
        }

        public static void ValidateState(Original323TreasureAlarmJournalState state)
        {
            if(state==null || state.SeenCasts==null ||
                state.Pending==null || state.Pending.Count>4096)
                throw new FormatException("보물 알람 저장본 손상");
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var s in state.SeenCasts)
                if(string.IsNullOrWhiteSpace(s) || s.Length>256 || !seen.Add(s))
                    throw new FormatException("중복 보물 알람 증빙");
            foreach(var a in state.Pending)
                if(a==null ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(a.Evidence) ||
                    !Original323TreasureRules.Finite(a.DueAt) || a.DueAt<0 ||
                    (a.Delay!=77 && a.Delay!=97 && a.Delay!=CcqRevealAlarmSeconds))
                    throw new FormatException("보물 알람 시간 저장본 손상");
        }

        public void RestoreState(Original323TreasureAlarmJournalState state)
        {
            if(seenCasts.Count!=0 || pending.Count!=0)
                throw new InvalidOperationException("진행 중인 보물 알람 복원 금지");
            ValidateState(state);
            foreach(string x in state.SeenCasts) seenCasts.Add(x);
            foreach(var a in state.Pending)
                pending.Add(new Original323TreasureAlarm(a.Evidence,
                    a.DueAt,a.Delay,a.RecordGuidanceCaster,a.RevealOnly));
        }
    }
}

namespace Ordr.Mobile.Mobile
{
    using Ordr.Mobile.Combat;

    public sealed partial class MobileTreasureGacha
    {
        public Original323TreasureGachaState CaptureState()
        {
            if(inProgress) throw new InvalidOperationException(
                "진행 중인 탐색 시도는 저장할 수 없습니다.");
            var state=new Original323TreasureGachaState {
                ConsumedAttemptIds=new List<string>(consumedIds)
            };
            state.ConsumedAttemptIds.Sort(StringComparer.Ordinal);
            return state;
        }

        public static void ValidateState(Original323TreasureGachaState state,
            Original323MobileTreasureState session)
        {
            if(state==null || session==null ||
                state.ConsumedAttemptIds==null ||
                state.ConsumedAttemptIds.Count>session.NextAttempt)
                throw new FormatException("보물 탐색 기회 저장본 손상");
            var seen=new HashSet<string>(StringComparer.Ordinal);
            string prefix="mobile-port:"+session.SessionId+":attempt:";
            foreach(string id in state.ConsumedAttemptIds)
            {
                int attemptNo;
                if(string.IsNullOrWhiteSpace(id) ||
                    !id.StartsWith(prefix,StringComparison.Ordinal) ||
                    !int.TryParse(id.Substring(prefix.Length),out attemptNo) ||
                    attemptNo<=0 || attemptNo>session.NextAttempt ||
                    !seen.Add(id))
                    throw new FormatException("보물 탐색권 사용 이력 오류");
            }
        }

        public void RestoreState(Original323TreasureGachaState state,
            Original323MobileTreasureState session)
        {
            if(inProgress || consumedIds.Count!=0)
                throw new InvalidOperationException("활성 탐색권 장부 복원 불가");
            ValidateState(state,session);
            foreach(string id in state.ConsumedAttemptIds) consumedIds.Add(id);
        }
    }
}
