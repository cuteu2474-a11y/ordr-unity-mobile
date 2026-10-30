using System;
using System.Collections.Generic;
using System.Globalization;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // The offline game uses the pinned 2.323[C] story sequence, not fabricated
    // "unlock" buttons. Combat HP is currently an OBJECT-BASE PRACTICE MODEL;
    // it is not an observed, fully modified Nightmare runtime health value.
    public sealed class Original323SoloStoryScenario : IDisposable
    {
        public const int Nightmare = 6;
        public const double BuildingX = -6768.0; // original B1 center
        public const double BuildingY = -5072.0;

        private struct Payout
        {
            public int Gold, Wood, Choice, Random, Uncommon, Special, Rare;
            public int MvpGold, MvpChoice, MvpRandom;
            public int ContributionWood, ContributionRandom;
            public Payout(int gold, int wood, int choice, int random,
                int uncommon, int special, int rare, int mvpGold, int mvpChoice,
                int mvpRandom, int contribWood, int contribRandom)
            {
                Gold=gold; Wood=wood; Choice=choice; Random=random;
                Uncommon=uncommon; Special=special; Rare=rare;
                MvpGold=mvpGold; MvpChoice=mvpChoice; MvpRandom=mvpRandom;
                ContributionWood=contribWood; ContributionRandom=contribRandom;
            }
        }

        // Source: JASS CNw..CRE, 52731..55503. The full native event also
        // includes research/trait/experience/ship, later unlocks and
        // contribution scoring. Those remain flagged as NOT yet reproduced.
        // Resource payments below are explicitly limited to practice gameplay.
        private static readonly Payout[] Pay = {
            new Payout(0,0,0,0,0,0,0,0,0,0,0,0),
            new Payout(180,0,3,1,0,0,0,500,0,1,0,0),
            new Payout(800,0,3,1,0,0,0,1500,0,1,0,0),
            new Payout(1000,0,4,1,2,0,0,2500,0,1,0,0),
            new Payout(2000,1,0,1,1,2,0,3000,0,1,0,0),
            new Payout(3000,1,0,1,2,2,0,5000,0,1,0,0),
            new Payout(4000,3,0,0,2,3,0,0,1,0,0,1),
            new Payout(6000,4,0,0,0,0,2,0,1,0,1,0),
            new Payout(8000,4,0,0,0,0,3,0,1,0,1,0),
            new Payout(9000,5,1,0,0,0,0,0,1,0,1,0),
            new Payout(10000,4,0,2,0,0,0,0,1,1,1,0),
            new Payout(12500,4,3,0,2,0,0,0,1,1,1,0),
            new Payout(10000,3,3,0,0,2,0,0,1,1,1,0),
            new Payout(5000,3,2,0,0,0,0,0,1,1,1,0),
            new Payout(5000,0,0,0,0,0,0,0,1,0,0,1)
        };

        public const string PracticeTranscendWisp = "practice:story:e01A";
        public const string PendingNativeSideEffects = "practice:story:pending-native-rewards";

        private readonly CharacterCombatRuntime combat;
        private readonly Original323StoryBuildingPracticeWorld world;
        private readonly SharedEconomySession economy;
        private readonly Dictionary<string, long> ownedDamage =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly HashSet<int> completed = new HashSet<int>();
        private readonly HashSet<int> issued = new HashSet<int>();
        private bool disposed;
        private int pendingStage;
        private double nextSpawnAt;
        private double eggheadSpawnAt = -1;

        public int ActiveStage { get; private set; }
        public int HighestDestroyedStage { get; private set; }
        public bool CompletedAllBuildings { get; private set; }
        public bool LostByStory { get; private set; }
        public string LastNotice { get; private set; }
        public int AwardedGold { get; private set; }
        public int AwardedWood { get; private set; }
        public double EggheadWaitRemaining =>
            pendingStage == 14 ? Math.Max(0, nextSpawnAt - combat.Time) : 0;
        public double EggheadDeadlineRemaining =>
            eggheadSpawnAt >= 0 && !CompletedAllBuildings
                ? Math.Max(0, Original323StoryUnlockRules.EggheadKillDeadlineSeconds -
                      (combat.Time - eggheadSpawnAt)) : 0;

        public Original323SoloStoryScenario(CharacterCombatRuntime live,
            Original323StoryBuildingPracticeWorld buildings,
            SharedEconomySession session)
        {
            combat = live ?? throw new ArgumentNullException(nameof(live));
            world = buildings ?? throw new ArgumentNullException(nameof(buildings));
            economy = session ?? throw new ArgumentNullException(nameof(session));
            if (!ReferenceEquals(combat.Roster, economy.Units) ||
                GameSession.ModeId != "nightmare-otherworld")
                throw new ArgumentException("악몽 이세계 공유 전투·지갑이 필요합니다.");
            combat.ActualEnemyDamage += OnDamage;
            combat.EnemyExited += OnEnemyExit;
        }

        public void Start()
        {
            if (disposed || ActiveStage != 0 || issued.Count != 0 ||
                combat.Game.Phase != SessionPhase.Running)
                throw new InvalidOperationException("스토리 게임을 중복 시작하거나 잘못 시작했습니다.");
            SpawnNext(1);
        }

        private void SpawnNext(int stage)
        {
            if (issued.Contains(stage) || stage < 1 || stage > 14)
                throw new InvalidOperationException("스토리 단계 중복 또는 순서 오류");
            // Every JASS CNv..CRB creates the next static building in B1.
            var spawn = world.SpawnPracticeBuilding(stage, BuildingX, BuildingY);
            issued.Add(stage);
            ActiveStage = stage;
            LastNotice = spawn.Building.Name + " 건물 등장 (객체 기본 모델)";
            if (stage == 14) eggheadSpawnAt = combat.Time;
        }

        private void OnDamage(string actor, string targetId, long amount, bool killed)
        {
            if (disposed || amount <= 0 ||
                world.FindEncounter(targetId) == null) return;
            long actorId;
            if (!long.TryParse(actor, NumberStyles.None,
                CultureInfo.InvariantCulture, out actorId)) return;
            if (!combat.IsOwnedCombatCharacter(actorId)) return;
            long already;
            ownedDamage.TryGetValue(targetId, out already);
            ownedDamage[targetId] = checked(already + amount);
        }

        private static ResourceBundle Bundle(Payout payout, bool contributor,
            int stage)
        {
            var result = new ResourceBundle();
            if (payout.Gold > 0) result.Add("gold",
                payout.Gold + (contributor ? payout.MvpGold : 0));
            if (payout.Wood > 0) result.Add("wood",
                payout.Wood + (contributor ? payout.ContributionWood : 0));
            else if (contributor && payout.ContributionWood > 0)
                result.Add("wood", payout.ContributionWood);
            int choice = payout.Choice + (contributor ? payout.MvpChoice : 0);
            int random = payout.Random +
                (contributor ? payout.MvpRandom + payout.ContributionRandom : 0);
            if (choice > 0) result.Add(WispResourceIds.Choice, choice);
            if (random > 0) result.Add(WispResourceIds.Random, random);
            if (payout.Uncommon > 0) result.Add(WispResourceIds.Uncommon, payout.Uncommon);
            if (payout.Special > 0) result.Add(WispResourceIds.Special, payout.Special);
            if (payout.Rare > 0) result.Add(WispResourceIds.Rare, payout.Rare);
            // Actual CQB gives a physical e01A actor; do not grant a
            // source-verified Kuma entitlement from a MODEL_ONLY kill.
            if (stage == 10) result.Add(PracticeTranscendWisp, 1);
            // Non-resource native side effects are tracked, not invented.
            if (stage >= 5) result.Add(PendingNativeSideEffects, 1);
            return result;
        }

        private void OnEnemyExit(string id, CombatEnemyExitReason reason)
        {
            if (disposed) return;
            var record = world.FindEncounter(id);
            if (record == null || record.Building.Stage != ActiveStage ||
                reason != CombatEnemyExitReason.Defeated) return;
            int stage = record.Building.Stage;
            if (!completed.Add(stage)) return;
            HighestDestroyedStage = Math.Max(HighestDestroyedStage, stage);
            long damage;
            // In the single-player model the sole owning player receives MVP
            // when their real owned actor inflicted damage; zero-hit scripted
            // removals never receive contribution bonuses.
            bool contributor = ownedDamage.TryGetValue(id, out damage) && damage > 0;
            ownedDamage.Remove(id);
            var payout = Pay[stage];
            var rewards = Bundle(payout, contributor, stage);
            string receipt = "practice:story-stage:" + stage;
            if (economy.Wallet.TryGrantOnce(receipt, rewards))
            {
                AwardedGold += payout.Gold + (contributor ? payout.MvpGold : 0);
                AwardedWood += payout.Wood + (contributor ? payout.ContributionWood : 0);
            }
            int next; double delay;
            if (Original323StoryUnlockRules.TryNextStageOnDestroy(stage,
                Nightmare, out next, out delay))
            {
                pendingStage = next;
                nextSpawnAt = combat.Time + delay;
                LastNotice = record.Building.Name + " 격파 · 다음 단계 " + next +
                    (delay > 0 ? " · 120초 후 등장" : " 등장 대기");
            }
            else if (stage == 14)
            {
                CompletedAllBuildings = true;
                LastNotice = "에그헤드 격파 · 14개 스토리 건물 완료";
            }
            else
                LastNotice = record.Building.Name + " 격파";
        }

        // Called at the source-model round-start boundary, before round combat
        // advances. Round-35 failure is NOT a 41R or a wall-clock condition.
        public void OnRoundStarted()
        {
            if (disposed || LostByStory || CompletedAllBuildings ||
                combat.Game.Phase != SessionPhase.Running) return;
            if (Original323StoryUnlockRules.ShouldLoseAtRoundStart(
                    combat.Game.CurrentWave, Nightmare, HighestDestroyedStage))
                Lose("35라운드까지 와노쿠니 건물을 파괴하지 못했습니다.");
        }

        public void Advance()
        {
            if (disposed || LostByStory || CompletedAllBuildings ||
                combat.Game.Phase != SessionPhase.Running) return;
            if (pendingStage != 0 && combat.Time >= nextSpawnAt)
            {
                int next = pendingStage;
                pendingStage = 0;
                SpawnNext(next);
            }
            if (eggheadSpawnAt >= 0 &&
                Original323StoryUnlockRules.EggheadTimedOut(
                    Nightmare, combat.Time - eggheadSpawnAt,
                    CompletedAllBuildings))
                Lose("에그헤드 등장 후 240초 제한시간 초과");
        }

        private void Lose(string reason)
        {
            if (combat.Game.Phase != SessionPhase.Running) return;
            LostByStory = true;
            LastNotice = reason;
            combat.Game.Finish(false);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            combat.ActualEnemyDamage -= OnDamage;
            combat.EnemyExited -= OnEnemyExit;
        }
    }
}
