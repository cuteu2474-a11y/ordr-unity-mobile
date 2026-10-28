using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // Pinned war3map.j: BeH/BeI/BeM/BeN/CCP/CCQ/CJm/CVC.
    // This implements SOURCE geometry, draw ordering and reward accounting.
    // Original BFj map coordinates and native spell execution must still
    // come from actual source witnesses. No fixed jackpot percentage.
    public static class Original323TreasureRules
    {
        public const double MinX = -6016;
        public const double MaxX = 3744;
        public const double MinY = -2336;
        public const double MaxY = 6080;
        public const double DefaultHalfExtent = 850;
        public const double GaimonHalfExtent = 978;
        public const double GaimonAndNamiHalfExtent = 1125;
        public const double RevealHalfExtent = 2000;
        // BeM(3) is a treasure-map effect, NOT a free game-start spawn.
        public const int TreasureMapBonusSpawnCount = 3;
        public const int InitialExtraTreasureCount = TreasureMapBonusSpawnCount;
        public const int ScheduledSpawnCount = 4;

        public static bool IsScheduledSpawnRound(int round) =>
            round > 0 && round <= 50 && round % 10 == 0;

        public static bool IsInOriginalSpawnRect(double x, double y) =>
            Finite(x) && Finite(y) &&
            x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

        public static bool Contains(double chestX, double chestY,
            double centerX, double centerY, double halfExtent)
        {
            return Finite(chestX) && Finite(chestY) &&
                Finite(centerX) && Finite(centerY) &&
                Finite(halfExtent) && halfExtent >= 0 &&
                !(chestX == 0 && chestY == 0) &&
                Math.Abs(chestX - centerX) <= halfExtent &&
                Math.Abs(chestY - centerY) <= halfExtent;
        }

        public static double SearchHalfExtent(bool gaimon,
            bool gaimonAndNami) =>
            !gaimon ? DefaultHalfExtent :
            gaimonAndNami ? GaimonAndNamiHalfExtent : GaimonHalfExtent;

        public static bool Finite(double v) => !double.IsNaN(v) &&
            !double.IsInfinity(v);

        // BeI makes exactly these calls, IN THIS ORDER:
        //  - pre-50 choose YK by BFi(1,2), after 50 choose 1 directly
        //  - if YL, choose YM by BFi(1,2)
        //  - if Gaimon YN, override YM=2 (still consume previous YL draw)
        //  - unless YU, BFi(1,1000) <=25 grants 1 Berry.
        public static Original323TreasureReward RollOriginalReward(
            int currentRound, bool gaimon, bool extraQuantityRoll,
            bool berryDisabled, IOriginalWispRandom originalScriptRandom)
        {
            if (currentRound < 1 || currentRound > 65)
                throw new ArgumentOutOfRangeException(nameof(currentRound));
            if (originalScriptRandom == null)
                throw new ArgumentNullException(nameof(originalScriptRandom));
            int outcome = currentRound < 50 ?
                Checked(originalScriptRandom,1,2) : 1;
            int quantity = extraQuantityRoll ?
                Checked(originalScriptRandom,1,2) : 1;
            if (gaimon) quantity = 2;
            string wisp = outcome == 1
                ? (gaimon && currentRound < 50 ?
                    WispResourceIds.Choice : WispResourceIds.Random)
                : WispResourceIds.Uncommon;
            bool berry = !berryDisabled &&
                Checked(originalScriptRandom,1,1000) <= 25;
            return new Original323TreasureReward(wisp,quantity,berry);
        }

        private static int Checked(IOriginalWispRandom rng,int min,int max)
        {
            int n=rng.NextInclusive(min,max);
            if(n<min||n>max)
                throw new InvalidOperationException("원본 보물 BFi 난수 범위 오류");
            return n;
        }
    }

    public sealed class Original323TreasureReward
    {
        public string WispResourceId { get; private set; }
        public int WispCount { get; private set; }
        public bool BonusBerry { get; private set; }
        public Original323TreasureReward(string wisp,int count,bool berry)
        {
            if(count<1||count>2 ||
                (wisp!=WispResourceIds.Random &&
                 wisp!=WispResourceIds.Uncommon &&
                 wisp!=WispResourceIds.Choice))
                throw new ArgumentException("원본 BeI 보물 보상 범위 오류");
            WispResourceId=wisp; WispCount=count; BonusBerry=berry;
        }
    }

    public sealed class Original323TreasureLocation
    {
        public string OriginalChestId { get; private set; }
        public int SpawnRound { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public bool Collected { get; internal set; }
        internal Original323TreasureReward Pending;
        internal string SpawnEvidence { get; private set; }

        internal Original323TreasureLocation(string id,int wave,double x,
            double y,string evidence)
        {
            OriginalChestId=id;SpawnRound=wave;X=x;Y=y;
            SpawnEvidence=evidence;
        }
    }

    // The ORIGINAL action/cost/witness must be supplied from a live cast.
    // A free tap or fake probability is not an original treasure search.
    public interface IOriginal323TreasureNativeWorld
    {
        bool TryConfirmOriginalTreasureCast(double x,double y,
            double halfExtent,bool collect,int wave,
            out string originalCastEvidence);
        // Must be idempotent by chestId AND atomically debit/pay the actual
        // original wallet. Failure cannot silently grant a reward.
        bool TrySettleTreasureOnce(string chestId,
            Original323TreasureReward originalReward,
            string originalCastEvidence,string spawnEvidence);
    }

    public sealed class Original323TreasureSearchResult
    {
        public int Matched { get; private set; }
        public int Collected { get; private set; }
        public bool WasAuthorized { get; private set; }
        public string Message { get; private set; }
        public IReadOnlyList<Original323TreasureReward> Rewards { get; private set; }
        internal Original323TreasureSearchResult(int n,int done,bool approved,
            string message,IReadOnlyList<Original323TreasureReward> paid=null)
        {
            Matched=n;Collected=done;WasAuthorized=approved;Message=message;
            Rewards=paid ?? new Original323TreasureReward[0];
        }
    }

    // One original round-spawn ledger and one original BFi stream per game.
    // Registration requires confirmed BFj positions; this does not invent
    // the 3 initial coordinates or four positions at each scheduled round.
    public sealed class Original323TreasureHunt
    {
        private readonly IOriginal323TreasureNativeWorld native;
        private readonly IOriginalWispRandom scriptRng;
        private readonly Dictionary<string,Original323TreasureLocation> byId =
            new Dictionary<string,Original323TreasureLocation>(StringComparer.Ordinal);
        private readonly List<Original323TreasureLocation> ordered =
            new List<Original323TreasureLocation>();
        private readonly Dictionary<int,int> countByWave=new Dictionary<int,int>();
        private bool inSearch;
        public int SpawnCount => ordered.Count;
        public int CollectedCount { get; private set; }
        public int UncollectedCount => SpawnCount-CollectedCount;
        public IReadOnlyList<Original323TreasureLocation> Chests => ordered.AsReadOnly();

        public Original323TreasureHunt(IOriginal323TreasureNativeWorld source,
            IOriginalWispRandom sharedOriginalBfi)
        {
            native=source ?? throw new ArgumentNullException(nameof(source));
            scriptRng=sharedOriginalBfi ??
                throw new ArgumentNullException(nameof(sharedOriginalBfi));
        }

        public void RegisterObservedSpawn(string uniqueId,int originalRound,
            double observedX,double observedY,string originalBfjEvidence)
        {
            // The round-zero slot tracks TREASURE MAP BONUS chests only.
            // It does not imply three free chests appear at game start.
            if (string.IsNullOrWhiteSpace(uniqueId) || uniqueId.Length>128 ||
                !Original323TreasureRules.IsInOriginalSpawnRect(observedX,observedY) ||
                (observedX==0 && observedY==0) ||
                (originalRound!=0 &&
                    !Original323TreasureRules.IsScheduledSpawnRound(originalRound)) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(originalBfjEvidence) ||
                byId.ContainsKey(uniqueId))
                throw new InvalidOperationException("검증되지 않은 원본 보물 좌표/출현 이벤트");

            int before;
            countByWave.TryGetValue(originalRound,out before);
            int maximum=originalRound==0 ?
                Original323TreasureRules.InitialExtraTreasureCount :
                Original323TreasureRules.ScheduledSpawnCount;
            if(before>=maximum)
                throw new InvalidOperationException("원본 라운드 보물 출현 수 초과");
            var chest=new Original323TreasureLocation(uniqueId,
                originalRound,observedX,observedY,originalBfjEvidence);
            byId.Add(uniqueId,chest);
            ordered.Add(chest);
            countByWave[originalRound]=before+1;
        }

        public IReadOnlyList<Original323TreasureLocation> Preview(
            double x,double y,double halfExtent)
        {
            var result=new List<Original323TreasureLocation>();
            foreach(var chest in ordered)
                if(!chest.Collected && Original323TreasureRules.Contains(
                    chest.X,chest.Y,x,y,halfExtent))result.Add(chest);
            return result.AsReadOnly();
        }

        public Original323TreasureSearchResult SearchObserved(
            int originalRound,double centerX,double centerY,
            bool gaimon,bool gaimonAndNami,bool extraQuantityRoll,
            bool berryDisabled,bool revealOnly)
        {
            if(inSearch || originalRound<1 || originalRound>65 ||
                !Original323TreasureRules.Finite(centerX) ||
                !Original323TreasureRules.Finite(centerY))
                return new Original323TreasureSearchResult(0,0,false,
                    "보물 탐색 시점/상태 미확인");
            double range=revealOnly ?
                Original323TreasureRules.RevealHalfExtent :
                Original323TreasureRules.SearchHalfExtent(
                    gaimon,gaimonAndNami);
            inSearch=true;
            try
            {
                string cast;
                if(!native.TryConfirmOriginalTreasureCast(centerX,centerY,
                    range,!revealOnly,originalRound,out cast) ||
                    !Original323BossRoundRules.TrustedRuntimeEvidence(cast))
                    return new Original323TreasureSearchResult(0,0,false,
                        "원본 보물 탐색 시전/대상 검증 실패");
                var matched=Preview(centerX,centerY,range);
                if(revealOnly)
                    return new Original323TreasureSearchResult(
                        matched.Count,0,true,"원본 보물 탐색 위치 표시 · 미수집");
                int done=0;
                var paid=new List<Original323TreasureReward>();
                foreach(var chest in matched)
                {
                    // An aborted native settlement cannot roll this chest
                    // a second time; its original reward stays pending.
                    if(chest.Pending==null)
                        chest.Pending=Original323TreasureRules.RollOriginalReward(
                            originalRound,gaimon,extraQuantityRoll,
                            berryDisabled,scriptRng);
                    if(!native.TrySettleTreasureOnce(chest.OriginalChestId,
                        chest.Pending,cast,chest.SpawnEvidence))
                        break;
                    paid.Add(chest.Pending);
                    chest.Collected=true;
                    chest.Pending=null;
                    CollectedCount++;
                    done++;
                }
                return new Original323TreasureSearchResult(matched.Count,
                    done,true,"원본 보물 "+done+"/"+matched.Count+
                    "개 수집"+(done<matched.Count?" · 남은 항목 미정산":""),
                    paid.AsReadOnly());
            }
            finally {inSearch=false;}
        }
    }
}
