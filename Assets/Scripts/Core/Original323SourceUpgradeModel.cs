using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    // A standalone source model is deliberately separate from a native witness.
    public sealed class Original323SourceUpgradeStep
    {
        public string Rawcode { get; }
        public int Level { get; }
        public long Gold { get; }
        public string EffectSummary { get; }
        public ResourceBundle Cost => new ResourceBundle().Add("gold", Gold);
        public Original323SourceUpgradeStep(string rawcode, int level, long gold, string effect)
        {
            if (string.IsNullOrEmpty(rawcode) || level < 1 || gold <= 0 || string.IsNullOrEmpty(effect))
                throw new ArgumentException("원본 모델 강화 단계가 올바르지 않습니다.");
            Rawcode = rawcode; Level = level; Gold = gold; EffectSummary = effect;
        }
    }

    [Serializable]
    public sealed class Original323SourceUpgradeState
    {
        public int Version = 1;
        public Dictionary<string, int> Levels = new Dictionary<string, int>(StringComparer.Ordinal);
        public bool Level20BossDefeated;
    }

    public interface IOriginal323SourceUpgradeModel
    {
        int LevelOf(string rawcode);
        Original323SourceUpgradeStep NextStep(string rawcode);
        bool IsUnlocked(string rawcode, int nextLevel);
        bool TryApply(Original323SourceUpgradeStep step, int previousLevel);
        Original323SourceUpgradeState CaptureState();
        void RestoreState(Original323SourceUpgradeState state);
    }
}
