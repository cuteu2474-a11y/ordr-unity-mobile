using System;
using System.Collections.Generic;

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
}
