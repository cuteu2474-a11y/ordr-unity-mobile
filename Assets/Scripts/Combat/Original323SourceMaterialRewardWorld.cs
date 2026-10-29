using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

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

    // Converts source-confirmed noncombat awards into the SAME runtime roster
    // used for CWR UNIT materials. Does not invent story victories, shop
    // purchases, gift boxes, native item costs, or an unobserved random roll.
    public sealed class Original323SourceMaterialRewardWorld
    {
        public const string KumaRawcode = "h04S";
        public const string KumaCatalogId = "KM-01";
        public const string TokenRawcode = "h06G";
        public const string TokenCatalogId = "LT-01";
        public const string StoryWispRawcode = "e01A";
        public const string PendingStoryWispResource = "source:story-transcend-wisp";
        // CSN creates its player-owned result at the player-zero start.
        public const double SourceStartX = -3776.0;
        public const double SourceStartY = 4480.0;

        private readonly CharacterCombatRuntime combat;
        private readonly SharedEconomySession economy;
        private readonly IOriginal323SourceMaterialEventWitness witness;
        private bool inFlight;
        private long nextChoice;
        private readonly string choiceReceiptPrefix =
            "original323:story-choice-kuma:" + Guid.NewGuid().ToString("N") + ":";

        public Original323SourceMaterialRewardWorld(CharacterCombatRuntime world,
            SharedEconomySession session, CharacterUnitCatalog units,
            OriginalMapBaseStatCatalog originalObjects,
            IOriginal323SourceMaterialEventWitness verifiedSource)
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
            RequireIdentity(units, originalObjects,
                TokenCatalogId, TokenRawcode, "재료",
                "verified-source-unit-gambling-reward");
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
                    return economy.Wallet.TryGrantOnce(receipt,
                        new ResourceBundle().Add(PendingStoryWispResource, 1));
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
            if (!Ready() || PendingTranscendWisps < 1) return false;
            inFlight = true;
            long spawned = 0;
            try
            {
                long next = checked(nextChoice + 1);
                string receipt = choiceReceiptPrefix + next;
                spawned = combat.SpawnVerifiedOriginalPositionCharacter(KumaCatalogId,
                    SourceStartX, SourceStartY);
                bool debited = economy.Wallet.TryExchangeOnce(receipt,
                    new ResourceBundle().Add(PendingStoryWispResource, 1),
                    new ResourceBundle());
                if (!debited)
                {
                    combat.RollbackUncommittedSpawn(spawned);
                    return false;
                }
                nextChoice = next;
                resultId = spawned;
                return true;
            }
            catch
            {
                // A wallet post-commit exception cannot duplicate the result:
                // the empty payout produces no Granted callback in this path.
                if (spawned != 0) combat.RollbackUncommittedSpawn(spawned);
                throw;
            }
            finally { inFlight = false; }
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
}
