using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    public enum Original323StoryNativeRewardKind
    {
        Research, TraitPoints, AncientShip, HeroExperience, GreenBloodSkill
    }

    public sealed class Original323StoryNativeReward
    {
        public int Stage { get; }
        public Original323StoryNativeRewardKind Kind { get; }
        public string OriginalRawcode { get; }
        public int Amount { get; }
        public string Receipt { get; }
        public bool Applied { get; internal set; }
        internal Original323StoryNativeReward(int stage,
            Original323StoryNativeRewardKind kind,
            string rawcode, int amount)
        {
            Stage = stage;
            Kind = kind;
            OriginalRawcode = rawcode;
            Amount = amount;
            Receipt = "story:native:" + stage + ":" + kind + ":" + rawcode;
        }
    }

    // Each implemented native subsystem acknowledges a transaction only
    // after it has been applied to real mobile game state. No fake gain,
    // extra RNG draw, altered map actor or fabricated owned unit.
    public interface IOriginal323StoryNativeRewardPort
    {
        bool TryApplySourceNativeReward(Original323StoryNativeReward award);
    }

    // JASS 2.323[C] story victory functions CNz..CRG.
    // A queued entitlement is NOT the same as an executed native effect:
    // no false "hero XP" or ancient ship actors without real target mapping.
    public sealed class Original323StoryNativeRewardQueue
    {
        private readonly List<Original323StoryNativeReward> awards =
            new List<Original323StoryNativeReward>();
        private readonly HashSet<int> recordedStages = new HashSet<int>();
        public IReadOnlyList<Original323StoryNativeReward> Awards => awards.AsReadOnly();
        public int PendingCount
        {
            get
            {
                int count = 0;
                foreach (var award in awards) if (!award.Applied) count++;
                return count;
            }
        }
        public bool HasRecordedStage(int stage) => recordedStages.Contains(stage);

        public void RecordCommittedClear(int stage)
        {
            if (stage < 1 || stage > 14)
                throw new ArgumentOutOfRangeException(nameof(stage));
            if (!recordedStages.Add(stage)) return;
            switch (stage)
            {
                case 5: Add(stage, Original323StoryNativeRewardKind.Research,
                    "R01N", 1); break;
                case 8: Add(stage, Original323StoryNativeRewardKind.Research,
                    "R01O", 1); break;
                case 9:
                    Add(stage, Original323StoryNativeRewardKind.Research, "R01P", 1);
                    Add(stage, Original323StoryNativeRewardKind.AncientShip, "h05Y", 1);
                    break;
                case 10: Add(stage, Original323StoryNativeRewardKind.Research,
                    "R01Q", 1); break;
                case 12: Add(stage, Original323StoryNativeRewardKind.Research,
                    "R01S", 1); break;
                case 14: Add(stage, Original323StoryNativeRewardKind.GreenBloodSkill,
                    "A13A", 1); break;
            }
            if (stage >= 8 && stage <= 13)
                Add(stage, Original323StoryNativeRewardKind.TraitPoints, "BdL", 1);
            if (stage == 13 || stage == 14)
                Add(stage, Original323StoryNativeRewardKind.HeroExperience, "Hp[VT]", 300);
        }

        private void Add(int stage, Original323StoryNativeRewardKind kind,
            string rawcode, int amount) =>
            awards.Add(new Original323StoryNativeReward(stage, kind, rawcode, amount));

        public int ApplyPending(IOriginal323StoryNativeRewardPort port)
        {
            if (port == null) return 0;
            int applied = 0;
            foreach (var award in awards)
            {
                if (award.Applied) continue;
                if (!port.TryApplySourceNativeReward(award)) continue;
                // The port guarantees a durable native outcome before true.
                award.Applied = true;
                applied++;
            }
            return applied;
        }
    }

    // Original CIB registers ONLY hero-type units with A07O in Hp[VT[player]].
    // The solo outcome credits live, player-owned pinned source heroes. Its
    // receipt-backed XP is now read as native levels/attribute growth by the
    // source-model attack resolver; it is not an invented generic XP system.
    public sealed class Original323MobileStoryHeroExperienceWorld :
        IOriginal323StoryNativeRewardPort
    {
        private const string ExperiencePrefix = "story:hero-xp:actor:";
        private readonly CharacterCombatRuntime combat;
        private readonly SharedEconomySession economy;
        private readonly OriginalMapBaseStatCatalog objects;
        public int LastAwardedHeroCount { get; private set; }

        public Original323MobileStoryHeroExperienceWorld(
            CharacterCombatRuntime runtime, SharedEconomySession session,
            OriginalMapBaseStatCatalog originalObjects)
        {
            combat = runtime ?? throw new ArgumentNullException(nameof(runtime));
            economy = session ?? throw new ArgumentNullException(nameof(session));
            objects = originalObjects ?? throw new ArgumentNullException(nameof(originalObjects));
            if (!ReferenceEquals(combat.Roster, economy.Units) ||
                objects.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256 ||
                GameSession.ModeId != "nightmare-otherworld")
                throw new ArgumentException("스토리 경험치 원본 객체·공유 로스터가 일치하지 않습니다.");
        }

        // Original JASS CIB: ability A07O AND UNIT_TYPE_HERO. The source
        // object's uppercase H-prefix identifies the native hero unit class.
        public static bool IsSourceStoryHero(OriginalMapCharacterBasics source)
        {
            if (source == null || string.IsNullOrEmpty(source.OriginalRawcode) ||
                source.OriginalRawcode[0] != 'H') return false;
            foreach (string ability in source.OriginalAbilityRawcodes)
                if (ability == "A07O") return true;
            return false;
        }

        public long ExperienceOf(long instanceId)
        {
            if (instanceId <= 0) return 0;
            return economy.Wallet.Balance(ExperiencePrefix + instanceId);
        }

        // This source event is distinct from story trait-point awards. The
        // original Bz6 consumes FOUR points from Shirahoshi to unlock Bz7;
        // a mere balance of four points must never trigger it.
        private Func<long, bool> originalShirahoshiTraitWitness;
        public void ConnectOriginalShirahoshiFourPointTraitWitness(Func<long, bool> witness)
        {
            if (originalShirahoshiTraitWitness != null || witness == null)
                throw new InvalidOperationException(
                    "원본 시라호시 4포인트 특성 근거는 한 번만 연결할 수 있습니다.");
            originalShirahoshiTraitWitness = witness;
        }

        // Only currently owned, actually mapped source heroes get live
        // growth. This is a read-through of the durable XP wallet, so
        // post-commit observers, reloads and repeated reward callbacks
        // cannot leave a stale level or double-apply an attribute bonus.
        public Original323HeroLevelState LevelStateOf(long instanceId)
        {
            string catalogId, kind, activeForm;
            if (!combat.TryGetOwnedSourceHeroIdentity(instanceId,
                out catalogId, out kind, out activeForm) ||
                kind == "support-object") return null;
            var source = objects.Find(catalogId);
            if (!IsSourceStoryHero(source)) return null;
            string rawcode = activeForm ?? source.OriginalRawcode;
            bool shiraTrait = originalShirahoshiTraitWitness != null &&
                originalShirahoshiTraitWitness(instanceId);
            return Original323HeroLevelRules.Evaluate(
                rawcode, ExperienceOf(instanceId), shiraTrait);
        }

        public int LevelOf(long instanceId)
        {
            var state = LevelStateOf(instanceId);
            return state == null ? 0 : state.Level;
        }

        public int AdditionalBasicAttackDamageOf(long instanceId)
        {
            var state = LevelStateOf(instanceId);
            return state == null ? 0 : state.AdditionalBasicAttackDamage;
        }

        public int AddedAttackSpeedPercentOf(long instanceId)
        {
            var state = LevelStateOf(instanceId);
            return state == null ? 0 : state.AddedAttackSpeedPercent;
        }

        public bool TryApplySourceNativeReward(Original323StoryNativeReward award)
        {
            if (award == null ||
                award.Kind != Original323StoryNativeRewardKind.HeroExperience ||
                (award.Stage != 13 && award.Stage != 14) ||
                award.OriginalRawcode != "Hp[VT]" || award.Amount != 300 ||
                award.Receipt != "story:native:" + award.Stage +
                    ":HeroExperience:Hp[VT]" ||
                !economy.HasStarted || combat.Game.Phase != SessionPhase.Running)
                return false;
            if (economy.Wallet.HasCompletedTransaction(award.Receipt))
                return true;
            var bundle = new ResourceBundle();
            int recipients = 0;
            foreach (var owned in combat.MobileUnitSnapshot())
            {
                // A transformed mapped hero still qualifies when its actual
                // source form has both the original hero type and A07O.
                // Unmapped forms and ordinary attackers never inherit XP.
                if (owned.Unit == null ||
                    owned.Unit.CatalogEntryKind == "support-object" ||
                    !IsSourceStoryHero(objects.Find(owned.Unit.Id)))
                    continue;
                bundle.Add(ExperiencePrefix + owned.InstanceId, award.Amount);
                recipients++;
            }
            try
            {
                if (!economy.Wallet.TryGrantOnce(award.Receipt, bundle) &&
                    !economy.Wallet.HasCompletedTransaction(award.Receipt))
                    return false;
            }
            catch
            {
                if (!economy.Wallet.HasCompletedTransaction(award.Receipt))
                    throw;
            }
            LastAwardedHeroCount = recipients;
            return true;
        }
    }
}
