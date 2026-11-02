using System;

namespace Ordr.Mobile.Core
{
    // The original script and native pool are independent, persistent lanes.
    // Partial failures keep the earlier draw and never offer a free reroll.
    public sealed class Original323EvolutionDrawSession
    {
        private readonly IOriginalWispRandom script;
        private readonly IOriginal323NativeUnitPoolRandom native;
        private int? uncommon;
        private int? special;
        public bool IsPrepared => uncommon.HasValue && special.HasValue;

        public Original323EvolutionDrawSession(IOriginalWispRandom scriptRandom,
            IOriginal323NativeUnitPoolRandom nativeRandom)
        {
            script = scriptRandom ?? throw new ArgumentNullException(nameof(scriptRandom));
            native = nativeRandom ?? throw new ArgumentNullException(nameof(nativeRandom));
            if (ReferenceEquals(script, native))
                throw new ArgumentException("원본 스크립트/네이티브 추첨은 별도입니다.");
        }

        public void Prepare()
        {
            if (!uncommon.HasValue)
            {
                int n = script.NextInclusive(0, Original323EvolutionSourceRules.UncommonPool.Count - 1);
                if (n < 0 || n >= Original323EvolutionSourceRules.UncommonPool.Count)
                    throw new InvalidOperationException("원본 안흔함 난수 범위가 잘못됐습니다.");
                uncommon = n;
            }
            if (!special.HasValue)
            {
                int n = native.NextInclusive(0, Original323EvolutionSourceRules.SpecialPool.Count - 1);
                if (n < 0 || n >= Original323EvolutionSourceRules.SpecialPool.Count)
                    throw new InvalidOperationException("원본 특별함 난수 범위가 잘못됐습니다.");
                special = n;
            }
        }

        public Original323EvolutionPoolUnit Uncommon
        {
            get
            {
                if (!IsPrepared) throw new InvalidOperationException("진화체 추첨이 완료되지 않았습니다.");
                return Original323EvolutionSourceRules.UncommonPool[uncommon.Value];
            }
        }
        public Original323EvolutionPoolUnit Special
        {
            get
            {
                if (!IsPrepared) throw new InvalidOperationException("진화체 추첨이 완료되지 않았습니다.");
                return Original323EvolutionSourceRules.SpecialPool[special.Value];
            }
        }
    }
}
