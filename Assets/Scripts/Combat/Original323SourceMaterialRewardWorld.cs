using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Combat
{
    // External native/replay observation, NOT a UI button or a predicted RNG
    // draw. It must certify that the source purchase/story settlement already
    // occurred, including every other grant/cost in its original transaction.
    public interface IOriginal323SourceMaterialEventWitness
    {
        bool TryConfirmSettledOriginalEvent(string eventId, string function,
            string rewardedRawcode, int awardedCount, out string nativeEvidence);
    }

    // Stage 9's source queue already records a committed story-building clear.
    // Turn that entitlement into one physical CWR UNIT material, not a free
    // shop entry, currency token or attacker with invented combat statistics.
    public sealed class Original323AncientShipStoryRewardPort :
        IOriginal323StoryNativeRewardPort
    {
        public const string MaterialCatalogId = "R-44";
        public const string MaterialRawcode = "h05Y";
        public const string OriginalMapName = "고대의 배 - 희귀";
        public const string StageNineReceipt = "story:native:9:AncientShip:h05Y";
        private readonly CharacterCombatRuntime combat;
        private readonly SharedEconomySession economy;
        private bool inFlight;

        public Original323AncientShipStoryRewardPort(CharacterCombatRuntime world,
            SharedEconomySession session, CharacterUnitCatalog units,
            OriginalMapBaseStatCatalog source)
        {
            combat = world ?? throw new ArgumentNullException(nameof(world));
            economy = session ?? throw new ArgumentNullException(nameof(session));
            var material = units == null ? null : units.Find(MaterialCatalogId);
            if (!ReferenceEquals(world.Roster, session.Units) ||
                source == null ||
                source.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256 ||
                material == null || material.Grade != "희귀함" ||
                material.CatalogEntryKind != "support-object" ||
                material.OriginalSourceRawcode != MaterialRawcode ||
                source.Find(MaterialCatalogId) != null ||
                !source.HasNoncombatMapObject(MaterialRawcode, OriginalMapName) ||
                GameSession.ModeId != "nightmare-otherworld")
                throw new ArgumentException("9단계 고대의 배 원본 객체 신원이 다릅니다.");
        }

        public bool TryApplySourceNativeReward(Original323StoryNativeReward award)
        {
            if (inFlight || award == null ||
                award.Kind != Original323StoryNativeRewardKind.AncientShip ||
                award.Stage != 9 || award.Amount != 1 ||
                award.OriginalRawcode != MaterialRawcode ||
                award.Receipt != StageNineReceipt ||
                !economy.HasStarted || combat.Game.Phase != SessionPhase.Running)
                return false;
            // The durable receipt also covers an already consumed ship after
            // a saved session is restored. Never re-create the material when
            // a player has legitimately used it for a fusion.
            if (economy.Wallet.HasCompletedTransaction(award.Receipt)) return true;
            inFlight = true;
            long created = 0;
            try
            {
                // The source callback's exact spawn coordinates have not
                // been verified. This is the game's standard mobile player
                // start position, not a claim about Warcraft's placement.
                created = combat.SpawnVerifiedOriginalPositionCharacter(
                    MaterialCatalogId,
                    Original323SourceMaterialRewardWorld.SourceStartX,
                    Original323SourceMaterialRewardWorld.SourceStartY);
                try
                {
                    if (!economy.Wallet.TryGrantOnce(award.Receipt,
                            new ResourceBundle()))
                    {
                        combat.RollbackUncommittedSpawn(created);
                        return false;
                    }
                }
                catch
                {
                    if (economy.Wallet.HasCompletedTransaction(award.Receipt))
                        return true; // post-commit observer failure
                    combat.RollbackUncommittedSpawn(created);
                    throw;
                }
                return true;
            }
            finally { inFlight = false; }
        }
    }

    // Converts source-confirmed noncombat awards into the SAME runtime roster
    // used for CWR UNIT materials. Does not invent story victories, shop
    // purchases, gift boxes, native item costs, or an unobserved random roll.
    public sealed class Original323SourceMaterialRewardWorld
    {
        public const string SeraphimMaterialRawcode = "h0AN";
        public const string SeraphimMaterialCatalogId = "SM-01";
        public const string SeraphimMaterialMapName = "세라핌 등급 유닛";
        public const string RewardBoxRawcode = "h0BR";
        public const string RewardBoxCatalogId = "TW-01";
        public const string RewardBoxMapName = "꾸러미 상자 - 초월";
        public const string RayleighRawcode = "h05X";
        public const string RayleighCatalogId = "R-43";
        public const string PirateShipRawcode = "h060";
        public const string PirateShipCatalogId = "PS-01";
        public const string KumaRawcode = "h04S";
        public const string KumaCatalogId = "KM-01";
        public const string TokenRawcode = "h06G";
        public const string TokenCatalogId = "LT-01";
        public const string StoryWispRawcode = "e01A";
        public const string PendingStoryWispResource = "source:story-transcend-wisp";
        public const string SettledStoryWispMarker = "story:transcend-stage10:original-settled";
        // CSN creates its player-owned result at the player-zero start.
        public const double SourceStartX = -3776.0;
        public const double SourceStartY = 4480.0;

        private readonly CharacterCombatRuntime combat;
        private readonly SharedEconomySession economy;
        private readonly IOriginal323SourceMaterialEventWitness witness;
        private readonly Original323CwrAcquisitionRoutes cwrAcquisitions;
        private bool inFlight;
        private readonly Original323KumaWispChoiceWorld kumaChoice;

        public Original323SourceMaterialRewardWorld(CharacterCombatRuntime world,
            SharedEconomySession session, CharacterUnitCatalog units,
            OriginalMapBaseStatCatalog originalObjects,
            IOriginal323SourceMaterialEventWitness verifiedSource,
            OriginalJassFusionCatalog originalRegistrations = null)
        {
            combat = world ?? throw new ArgumentNullException(nameof(world));
            economy = session ?? throw new ArgumentNullException(nameof(session));
            witness = verifiedSource ?? throw new ArgumentNullException(nameof(verifiedSource));
            if (!ReferenceEquals(combat.Roster, economy.Units) ||
                units == null || originalObjects == null ||
                originalObjects.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256 ||
                GameSession.ModeId != "nightmare-otherworld")
                throw new ArgumentException("악몽 이세계 원본 재료와 공유 세션의 근거 불일치");
            RequireIdentity(units, originalObjects,
                KumaCatalogId, KumaRawcode, "초월함",
                "verified-source-story-transcend-choice");
            kumaChoice = new Original323KumaWispChoiceWorld(
                combat, economy, units, originalObjects);
            RequireIdentity(units, originalObjects,
                TokenCatalogId, TokenRawcode, "재료",
                "verified-source-unit-gambling-reward");
            if (originalRegistrations != null)
            {
                var seraphim = units.Find(SeraphimMaterialCatalogId);
                if (seraphim == null || seraphim.Grade != "재료" ||
                    seraphim.CatalogEntryKind != "support-object" ||
                    seraphim.OriginalSourceRawcode != SeraphimMaterialRawcode ||
                    originalObjects.Find(SeraphimMaterialCatalogId) != null ||
                    !originalObjects.HasNoncombatMapObject(
                        SeraphimMaterialRawcode, SeraphimMaterialMapName))
                    throw new ArgumentException("원본 베가펑크 특수 재료 객체 신원이 다릅니다.");
                cwrAcquisitions = new Original323CwrAcquisitionRoutes(
                    originalRegistrations, units, originalObjects);
            }
        }

        private static void RequireIdentity(CharacterUnitCatalog units,
            OriginalMapBaseStatCatalog source, string id, string raw,
            string grade, string mappingStatus)
        {
            var unit = units.Find(id);
            var original = source.Find(id);
            if (unit == null || unit.Grade != grade || original == null ||
                original.OriginalRawcode != raw || original.MappingStatus != mappingStatus ||
                original.Attack1 != null)
                throw new ArgumentException("원본 비전투 재료 매핑 변경: " + raw);
        }

        private bool Ready()
        {
            return !inFlight && economy.HasStarted &&
                combat.Game.Phase == SessionPhase.Running &&
                GameSession.ModeId == "nightmare-otherworld";
        }

        public long PendingTranscendWisps => economy.Wallet.Balance(PendingStoryWispResource);

        // A successful, fully settled CJM random draw owns exactly one of the
        // 14 source A800/RDUN actors. It cannot be a manual IS01..IS14 craft,
        // and the default practice game never fakes the gambling transaction.
        public bool TryRecordObservedOtherworldRandomUnit(string eventId,
            string resultRawcode, out long actorId)
        {
            actorId = 0;
            string catalogId;
            if (cwrAcquisitions == null ||
                !cwrAcquisitions.TryResolveRandomOnlyAward(resultRawcode, out catalogId))
                return false;
            // CJM failure pays the lucky token, CJM success pays a random
            // actor. A repeated parent event can never pay both branches.
            if (economy.Wallet.HasCompletedTransaction(
                    "original323:luck-token:CJM:" + eventId)) return false;
            return TryRecordOneWitnessedNoncombatOrRandomAward(eventId,
                "CJM", resultRawcode, catalogId,
                "original323:otherworld-rdun:CJM:", out actorId);
        }

        // The h0AN W3X object is a distinct non-attacker, not any Seraphim
        // fighter. Only a witness confirming its ORIGINAL parent reward may
        // furnish the real TR30 material; source function is never guessed.
        public bool TryRecordObservedSeraphimMaterial(string eventId,
            string originalFunction, out long actorId)
        {
            actorId = 0;
            if (cwrAcquisitions == null || string.IsNullOrWhiteSpace(originalFunction)) return false;
            return TryRecordOneWitnessedNoncombatOrRandomAward(eventId,
                originalFunction, SeraphimMaterialRawcode,
                SeraphimMaterialCatalogId, "original323:seraphim-material:",
                out actorId);
        }

        private bool TryRecordOneWitnessedNoncombatOrRandomAward(string eventId,
            string function, string sourceRawcode, string catalogId,
            string receiptPrefix, out long actorId)
        {
            actorId = 0;
            if (!Ready() || string.IsNullOrWhiteSpace(eventId)) return false;
            string receipt = receiptPrefix + eventId;
            if (economy.Wallet.HasCompletedTransaction(receipt)) return false;
            string evidence;
            if (!witness.TryConfirmSettledOriginalEvent(eventId, function,
                    sourceRawcode, 1, out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;
            inFlight = true;
            long spawned = 0;
            try
            {
                spawned = combat.SpawnVerifiedOriginalPositionCharacter(catalogId,
                    SourceStartX, SourceStartY);
                // No fabricated gold, lumber, items, or second random roll.
                // The source witness has already settled the parent cost.
                try
                {
                    if (!economy.Wallet.TryGrantOnce(receipt,
                            new ResourceBundle()))
                    {
                        combat.RollbackUncommittedSpawn(spawned);
                        return false;
                    }
                }
                catch
                {
                    if (!economy.Wallet.HasCompletedTransaction(receipt))
                    {
                        combat.RollbackUncommittedSpawn(spawned);
                        throw;
                    }
                    // Post-commit notification failed: the actor and receipt
                    // remain committed and may not be granted again.
                }
                actorId = spawned;
                return true;
            }
            finally { inFlight = false; }
        }

        // CQB grants one e01A after a source-observed story-boss clear. The
        // witness MUST also confirm 10000 GOLD, 4 WOOD and two YR rewards were
        // independently settled. Those rewards are NOT duplicated here.
        public bool TryRecordObservedStoryClearWisp(string storyEventId)
        {
            if (!Ready() || string.IsNullOrWhiteSpace(storyEventId)) return false;
            string evidence;
            if (!witness.TryConfirmSettledOriginalEvent(storyEventId,
                "CQB", StoryWispRawcode, 1, out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;
            inFlight = true;
            string receipt = "original323:story-wisp:" + storyEventId;
            try
            {
                try
                {
                    // The practice stage-10 payout and the independently
                    // observed CQB are two proofs of ONE stage reward.
                    // A marker survives even if the player used the Wisp.
                    bool alreadyEarned =
                        economy.Wallet.HasCompletedTransaction("practice:story-stage:10") ||
                        economy.Wallet.Balance(SettledStoryWispMarker) > 0;
                    var award = new ResourceBundle();
                    if (economy.Wallet.Balance(SettledStoryWispMarker) == 0)
                        award.Add(SettledStoryWispMarker, 1);
                    if (!alreadyEarned) award.Add(PendingStoryWispResource, 1);
                    return economy.Wallet.TryGrantOnce(receipt, award);
                }
                catch
                {
                    // Wallet observers are called after a durable commit.
                    if (economy.Wallet.HasCompletedTransaction(receipt))
                        return true;
                    throw;
                }
            }
            finally { inFlight = false; }
        }

        // Mobile equivalent of taking exactly one acquired e01A through B7.
        // A user-confirmed choice spends one outstanding story Wisp; no new
        // source proof is forged and the same wallet pays for the actor.
        public bool TryChooseKuma(out long resultId)
        {
            resultId = 0;
            return Ready() &&
                kumaChoice.TryChooseKuma(PendingStoryWispResource, out resultId);
        }

        public bool TryChooseRewardBox(out IReadOnlyList<long> resultIds)
        {
            resultIds = Array.Empty<long>();
            return Ready() && kumaChoice.TryChoose(
                PendingStoryWispResource, Original323TranscendWispOutcome.RewardBox,
                out resultIds);
        }

        public bool TryChooseRayleighAndShip(out IReadOnlyList<long> resultIds)
        {
            resultIds = Array.Empty<long>();
            return Ready() && kumaChoice.TryChoose(
                PendingStoryWispResource, Original323TranscendWispOutcome.RayleighAndShip,
                out resultIds);
        }

        // CJK = high-grade unit gamble fail; CJM = otherworld unit gamble
        // fail; CGk = gift chest; CCk = verified bonus; CKJ = Compu exchange.
        // The witness proves the FULL parent transaction, including purchase,
        // failures, special choice state and incidental wood/gold rewards.
        // Here we create ONLY the token actors confirmed by that source.
        public bool TryRecordObservedTokenAward(string eventId, string function,
            int confirmedTokenCount, out IReadOnlyList<long> unitIds)
        {
            unitIds = Array.Empty<long>();
            if (!Ready() || string.IsNullOrWhiteSpace(eventId) ||
                (confirmedTokenCount != 1 && confirmedTokenCount != 2) ||
                (confirmedTokenCount == 2 && function != "CJK" && function != "CCk") ||
                (function != "CJK" && function != "CJM" && function != "CGk" &&
                 function != "CCk" && function != "CKJ"))
                return false;
            string receipt = "original323:luck-token:" + function + ":" + eventId;
            if (economy.Wallet.HasCompletedTransaction(receipt)) return false;
            if (function == "CJM" && economy.Wallet.HasCompletedTransaction(
                    "original323:otherworld-rdun:CJM:" + eventId)) return false;
            string evidence;
            if (!witness.TryConfirmSettledOriginalEvent(eventId, function,
                    TokenRawcode, confirmedTokenCount, out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;

            inFlight = true;
            var spawned = new List<long>();
            try
            {
                for (int i = 0; i < confirmedTokenCount; i++)
                    spawned.Add(combat.SpawnVerifiedOriginalPositionCharacter(TokenCatalogId,
                        SourceStartX, SourceStartY));
                // No invented gold/wood grant. An empty durable reward bundle
                // reserves the unique receipt alongside the already observed
                // native settlement and prevents a second token payout.
                if (!economy.Wallet.TryGrantOnce(receipt, new ResourceBundle()))
                {
                    for (int i = spawned.Count - 1; i >= 0; i--)
                        combat.RollbackUncommittedSpawn(spawned[i]);
                    return false;
                }
                unitIds = spawned.AsReadOnly();
                return true;
            }
            catch
            {
                if (!economy.Wallet.HasCompletedTransaction(receipt))
                    for (int i = spawned.Count - 1; i >= 0; i--)
                        combat.RollbackUncommittedSpawn(spawned[i]);
                throw;
            }
            finally { inFlight = false; }
        }
    }

    // Source tooltip-model Q + Q token exchange on the same live, physical
    // roster as CWR UNIT fusion. This is a mobile result-equivalent policy:
    // Warcraft's native spell order/seed and rare-pool execution are not claimed.
    public sealed class Original323LuckyTokenService
    {
        private sealed class ElizabethCast
        {
            public double NextStartAt;
            public double FinishAt;
            public bool Casting;
        }

        public const string ElizabethCatalogId = "RN-06";
        public const string ElizabethRawcode = "h06Z";
        public const int ExchangeCost = 3;
        public const int SpecialChancePercent = 80;
        public const double CastSeconds = 2;
        public const double CooldownSeconds = 110;
        private readonly CharacterCombatRuntime combat;
        private readonly SharedEconomySession economy;
        private readonly IOriginalWispRandom random;
        private readonly Func<string, bool> canResolveWisp;
        private readonly Dictionary<long, ElizabethCast> casts =
            new Dictionary<long, ElizabethCast>();
        private readonly string receiptPrefix =
            "original323:elizabeth-lucky-token:" + Guid.NewGuid().ToString("N") + ":";
        private long sequence;
        private bool processing;
        public event Action<Exception> PostCommitObserverFailed;

        public Original323LuckyTokenService(CharacterCombatRuntime combat,
            SharedEconomySession economy, CharacterUnitCatalog units,
            OriginalMapBaseStatCatalog originalObjects,
            IOriginalWispRandom sharedScriptRandom,
            Func<string, bool> canAutomaticallyResolveWisp)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
            random = sharedScriptRandom ?? throw new ArgumentNullException(nameof(sharedScriptRandom));
            canResolveWisp = canAutomaticallyResolveWisp ??
                throw new ArgumentNullException(nameof(canAutomaticallyResolveWisp));
            if (!ReferenceEquals(combat.Roster, economy.Units) ||
                units == null || originalObjects == null ||
                originalObjects.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256 ||
                GameSession.ModeId != "nightmare-otherworld")
                throw new ArgumentException("원본 토큰 경제와 현재 공유 유닛 장부가 다릅니다.");
            var elizabeth = units.Find(ElizabethCatalogId);
            var sourceOwner = originalObjects.Find(ElizabethCatalogId);
            var token = units.Find(Original323SourceMaterialRewardWorld.TokenCatalogId);
            var sourceToken = originalObjects.Find(Original323SourceMaterialRewardWorld.TokenCatalogId);
            if (elizabeth == null || sourceOwner == null ||
                sourceOwner.OriginalRawcode != ElizabethRawcode ||
                token == null || token.Grade != "재료" ||
                sourceToken == null ||
                sourceToken.OriginalRawcode != Original323SourceMaterialRewardWorld.TokenRawcode ||
                sourceToken.MappingStatus != "verified-source-unit-gambling-reward" ||
                sourceToken.Attack1 != null)
                throw new ArgumentException("엘리자베스 또는 행운의 토큰 원본 객체 매핑 불일치");
        }

        private bool Ready => !processing && economy.HasStarted &&
            combat.Game.Phase == SessionPhase.Running &&
            combat.PendingSimulationSeconds == 0 &&
            GameSession.ModeId == "nightmare-otherworld";

        public int OwnedTokenCount
        {
            get
            {
                int count = 0;
                foreach (var unit in combat.MobileUnitSnapshot())
                    if (unit.Unit.Id == Original323SourceMaterialRewardWorld.TokenCatalogId &&
                        !combat.IsStoryDeployed(unit.InstanceId)) count++;
                return count;
            }
        }

        public bool CanExchange => Ready && OwnedTokenCount >= ExchangeCost &&
            canResolveWisp(WispResourceIds.Special) &&
            canResolveWisp(WispResourceIds.Rare);

        // Q is automatically activated on mobile; a 2-second cast must
        // complete while its exact source-profile owner is still on the roster.
        // Cooldown starts on cast order. No phantom token wallet is created.
        public void AdvanceAutoElizabethQ()
        {
            if (!Ready) return;
            var live = new HashSet<long>();
            foreach (var unit in combat.MobileUnitSnapshot())
            {
                if (unit.Unit.Id != ElizabethCatalogId ||
                    combat.IsStoryDeployed(unit.InstanceId) ||
                    !SourceModelTwentyNinthSkillCatalog.HasExactApprovedSubset(combat.ProfileOf(unit.InstanceId)))
                    continue;
                live.Add(unit.InstanceId);
                ElizabethCast state;
                if (!casts.TryGetValue(unit.InstanceId, out state))
                {
                    state = new ElizabethCast();
                    casts.Add(unit.InstanceId, state);
                }
                if (state.Casting)
                {
                    if (combat.Time >= state.FinishAt)
                    {
                        AwardElizabethCast(unit);
                        state.Casting = false;
                    }
                }
                else if (combat.Time >= state.NextStartAt)
                {
                    state.Casting = true;
                    state.FinishAt = combat.Time + CastSeconds;
                    state.NextStartAt = combat.Time + CooldownSeconds;
                }
            }
            var missing = new List<long>();
            foreach (long id in casts.Keys)
                if (!live.Contains(id)) missing.Add(id);
            foreach (long id in missing) casts.Remove(id);
        }

        private void AwardElizabethCast(MobileUnitSnapshot owner)
        {
            processing = true;
            var spawned = new List<long>();
            var checkpointRandom = random as IOriginal323RollbackWispRandom;
            int checkpoint = checkpointRandom == null ? -1 : checkpointRandom.CaptureCheckpoint();
            long next = checked(sequence + 1);
            string receipt = receiptPrefix + "q:" + next;
            try
            {
                // Two independent 33% source rolls: bonus token and wood.
                bool extraToken = random.NextInclusive(1, 100) <= 33;
                bool extraWood = random.NextInclusive(1, 100) <= 33;
                int count = extraToken ? 2 : 1;
                for (int i = 0; i < count; i++)
                    spawned.Add(combat.SpawnVerifiedOriginalPositionCharacter(
                        Original323SourceMaterialRewardWorld.TokenCatalogId,
                        combat.Roster.Get(owner.InstanceId).X,
                        combat.Roster.Get(owner.InstanceId).Y));
                var grant = new ResourceBundle();
                if (extraWood) grant.Add("wood", 1);
                try { economy.Wallet.TryGrantOnce(receipt, grant); }
                catch (Exception observer)
                {
                    if (!economy.Wallet.HasCompletedTransaction(receipt)) throw;
                    PostCommitObserverFailed?.Invoke(observer);
                }
                sequence = next;
            }
            catch
            {
                if (!economy.Wallet.HasCompletedTransaction(receipt))
                {
                    for (int i = spawned.Count - 1; i >= 0; i--)
                        combat.RollbackUncommittedSpawn(spawned[i]);
                    if (checkpointRandom != null)
                        checkpointRandom.RestoreCheckpoint(checkpoint);
                }
                throw;
            }
            finally { processing = false; }
        }

        // Consumes three real LT-01 instances, never a separate token
        // currency. Both source reward pools must be operational before
        // removing units. The deferred Wisp service drains the granted
        // balance only after this whole unit transaction commits.
        public bool TryExchangeThree(out string rewardedWisp, out string reason)
        {
            rewardedWisp = null;
            reason = null;
            if (!Ready)
            {
                reason = "전투 정산 또는 원본 세션이 준비되지 않았습니다.";
                return false;
            }
            if (!canResolveWisp(WispResourceIds.Special) ||
                !canResolveWisp(WispResourceIds.Rare))
            {
                reason = "특별함·희귀함 위습의 원본 보상 풀이 모두 연결되지 않았습니다.";
                return false;
            }
            var materialIds = new List<long>(ExchangeCost);
            foreach (var unit in combat.MobileUnitSnapshot())
                if (unit.Unit.Id == Original323SourceMaterialRewardWorld.TokenCatalogId &&
                    !combat.IsStoryDeployed(unit.InstanceId))
                {
                    materialIds.Add(unit.InstanceId);
                    if (materialIds.Count == ExchangeCost) break;
                }
            if (materialIds.Count != ExchangeCost)
            {
                reason = "행운의 토큰 3개가 필요합니다.";
                return false;
            }

            processing = true;
            var checkpointRandom = random as IOriginal323RollbackWispRandom;
            int checkpoint = checkpointRandom == null ? -1 : checkpointRandom.CaptureCheckpoint();
            var beforeWallet = economy.Wallet.CaptureState();
            long next = checked(sequence + 1);
            string receipt = receiptPrefix + "exchange:" + next;
            bool consumed = false;
            try
            {
                using (var removal = combat.BeginOriginalUnitRemovalBatch(materialIds))
                {
                    string result = random.NextInclusive(1, 100) <= SpecialChancePercent
                        ? WispResourceIds.Special : WispResourceIds.Rare;
                    try
                    {
                        if (!economy.Wallet.TryGrantOnce(receipt,
                                new ResourceBundle().Add(result, 1)))
                            throw new InvalidOperationException("이미 처리된 토큰 교환 요청입니다.");
                    }
                    catch (Exception observer)
                    {
                        if (!economy.Wallet.HasCompletedTransaction(receipt)) throw;
                        PostCommitObserverFailed?.Invoke(observer);
                    }
                    removal.Commit();
                    consumed = true;
                    rewardedWisp = result;
                    sequence = next;
                    return true;
                }
            }
            catch
            {
                if (!consumed)
                {
                    // The staged batch never deleted source units before its
                    // Commit; reverse speculative Wisp balance/receipt together.
                    economy.Wallet.RestoreState(beforeWallet);
                    if (checkpointRandom != null)
                        checkpointRandom.RestoreCheckpoint(checkpoint);
                }
                throw;
            }
            finally { processing = false; }
        }
    }

    // The original e01A enters exactly one of B6/CSM, B7/CSN or B8/CSO.
    // The mobile choice is one atomic roster+wallet transaction per Wisp.
    public enum Original323TranscendWispOutcome
    {
        RewardBox,       // CSM: h0BR, "꾸러미 상자 - 초월"
        Kuma,            // CSN: h04S
        RayleighAndShip  // CSO: h05X + h060
    }

    public sealed class Original323KumaWispChoiceWorld
    {
        private readonly CharacterCombatRuntime combat;
        private readonly SharedEconomySession economy;
        private readonly string receiptNamespace =
            "original323:transcend-choice:" + Guid.NewGuid().ToString("N") + ":";
        private long sequence;
        private bool processing;

        public Original323KumaWispChoiceWorld(CharacterCombatRuntime world,
            SharedEconomySession session, CharacterUnitCatalog units,
            OriginalMapBaseStatCatalog objects)
        {
            combat = world ?? throw new ArgumentNullException(nameof(world));
            economy = session ?? throw new ArgumentNullException(nameof(session));
            if (units == null || objects == null ||
                !ReferenceEquals(combat.Roster, economy.Units) ||
                objects.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256 ||
                GameSession.ModeId != "nightmare-otherworld")
                throw new ArgumentException("초월위습 원본 월드·도감 불일치");

            var kuma = units.Find(Original323SourceMaterialRewardWorld.KumaCatalogId);
            var kumaSource = objects.Find(Original323SourceMaterialRewardWorld.KumaCatalogId);
            if (kuma == null || kuma.Grade != "초월함" ||
                kumaSource == null ||
                kumaSource.OriginalRawcode != Original323SourceMaterialRewardWorld.KumaRawcode ||
                kumaSource.MappingStatus != "verified-source-story-transcend-choice" ||
                kumaSource.Attack1 != null)
                throw new ArgumentException("CSN/B7 쿠마 객체 원본과 불일치");

            var box = units.Find(Original323SourceMaterialRewardWorld.RewardBoxCatalogId);
            if (box == null || box.CatalogEntryKind != "support-object" ||
                box.OriginalSourceRawcode != Original323SourceMaterialRewardWorld.RewardBoxRawcode ||
                !objects.HasNoncombatMapObject(
                    Original323SourceMaterialRewardWorld.RewardBoxRawcode,
                    Original323SourceMaterialRewardWorld.RewardBoxMapName))
                throw new ArgumentException("CSM/B6 초월 꾸러미 상자 원본과 불일치");

            var rayleigh = units.Find(Original323SourceMaterialRewardWorld.RayleighCatalogId);
            var rayleighSource = objects.Find(Original323SourceMaterialRewardWorld.RayleighCatalogId);
            var ship = units.Find(Original323SourceMaterialRewardWorld.PirateShipCatalogId);
            var shipSource = objects.Find(Original323SourceMaterialRewardWorld.PirateShipCatalogId);
            if (rayleigh == null || rayleigh.Name != "실버즈 레일리" ||
                rayleighSource == null ||
                rayleighSource.OriginalRawcode != Original323SourceMaterialRewardWorld.RayleighRawcode ||
                ship == null || (ship.Grade != "히든" && ship.Grade != "해적선") ||
                shipSource == null ||
                shipSource.OriginalRawcode != Original323SourceMaterialRewardWorld.PirateShipRawcode ||
                shipSource.MappingStatus != "verified-source-wisp-pirate-ship")
                throw new ArgumentException("CSO/B8 레일리·해적선 원본과 불일치");
        }

        public bool TryChooseKuma(string earnedWispResource, out long actorId)
        {
            actorId = 0;
            IReadOnlyList<long> actors;
            if (!TryChoose(earnedWispResource, Original323TranscendWispOutcome.Kuma,
                    out actors) || actors.Count != 1)
                return false;
            actorId = actors[0];
            return true;
        }

        public bool TryChoose(string earnedWispResource,
            Original323TranscendWispOutcome outcome, out IReadOnlyList<long> actorIds)
        {
            actorIds = Array.Empty<long>();
            if (processing || !economy.HasStarted ||
                combat.Game.Phase != SessionPhase.Running ||
                GameSession.ModeId != "nightmare-otherworld" ||
                (earnedWispResource != Original323SourceMaterialRewardWorld.PendingStoryWispResource &&
                 earnedWispResource != Original323SoloStoryScenario.PracticeTranscendWisp) ||
                economy.Wallet.Balance(earnedWispResource) < 1 ||
                !Enum.IsDefined(typeof(Original323TranscendWispOutcome), outcome))
                return false;

            string[] results;
            switch (outcome)
            {
                case Original323TranscendWispOutcome.RewardBox:
                    results = new[] { Original323SourceMaterialRewardWorld.RewardBoxCatalogId };
                    break;
                case Original323TranscendWispOutcome.Kuma:
                    results = new[] { Original323SourceMaterialRewardWorld.KumaCatalogId };
                    break;
                case Original323TranscendWispOutcome.RayleighAndShip:
                    results = new[] {
                        Original323SourceMaterialRewardWorld.RayleighCatalogId,
                        Original323SourceMaterialRewardWorld.PirateShipCatalogId
                    };
                    break;
                default:
                    return false;
            }
            processing = true;
            var spawned = new List<long>(results.Length);
            long next = checked(sequence + 1);
            string receipt = receiptNamespace + next;
            try
            {
                // Never spend the Wisp before both source objects exist.
                // A failure on the second ship/hero spawn removes the first.
                foreach (string catalogId in results)
                    spawned.Add(combat.SpawnVerifiedOriginalPositionCharacter(
                        catalogId,
                        Original323SourceMaterialRewardWorld.SourceStartX,
                        Original323SourceMaterialRewardWorld.SourceStartY));
                if (!economy.Wallet.TryExchangeOnce(receipt,
                    new ResourceBundle().Add(earnedWispResource, 1),
                    new ResourceBundle()))
                {
                    Rollback(spawned);
                    return false;
                }
                sequence = next;
                actorIds = spawned.AsReadOnly();
                return true;
            }
            catch
            {
                // Post-commit observer errors cannot resurrect a spent Wisp.
                if (economy.Wallet.HasCompletedTransaction(receipt))
                {
                    sequence = next;
                    actorIds = spawned.AsReadOnly();
                    return spawned.Count == results.Length;
                }
                Rollback(spawned);
                throw;
            }
            finally { processing = false; }
        }

        private void Rollback(IReadOnlyList<long> actorIds)
        {
            for (int i = actorIds.Count - 1; i >= 0; i--)
                combat.RollbackUncommittedSpawn(actorIds[i]);
        }
    }
}
