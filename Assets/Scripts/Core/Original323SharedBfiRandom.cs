using System;

namespace Ordr.Mobile.Core
{
    // Distinct from Warcraft's GetRandomInt and PlaceRandomUnit native RNG.
    // Confirmations are supplied by a source-backed ORIGINAL event observer.
    // This replay does not discover the original native seed on its own.
    public enum Original323ScriptRandomPurpose
    {
        MonsterDeathGold,
        RandomWispResource,
        RareWispBranch,
        Gambling,
        OtherOriginalScriptCall
    }

    // Native Warcraft unit pool selection must be implemented independently.
    // The source JASS PlaceRandomUnit() does NOT advance its custom BFh state.
    public interface IOriginal323NativeUnitPoolRandom : IOriginalWispRandom
    {
    }

    public interface IOriginal323ScriptRandomDrawWitness
    {
        // Report an observed original-script draw in GLOBAL chronological
        // order, including any unrelated BFi calls. Source order is essential.
        bool TryConfirmOriginalDraw(long nextOrdinal,
            Original323ScriptRandomPurpose purpose, int previousState,
            int observedNextState, int minimum, int maximum,
            int expectedOriginalResult, out string evidence);
    }

    public sealed class Original323SharedBfiSnapshot
    {
        public int State { get; private set; }
        public long DrawCount { get; private set; }

        public Original323SharedBfiSnapshot(int state, long drawCount)
        {
            if (state == 0 || drawCount < 0)
                throw new ArgumentOutOfRangeException(nameof(state));
            State = state;
            DrawCount = drawCount;
        }
    }

    // Exactly ONE instance per original game session. Other systems obtain
    // a purpose-labelled view via ForPurpose; the underlying H6 state stays
    // shared across all BFi calls. A session may NOT use guessed seeds.
    public sealed class Original323SharedBfiRandom
    {
        public const int OriginalSeedMin = 1;
        public const int OriginalSeedMax = 1000000;
        public const int Multiplier = 69069;
        public const int Increment = 1;
        public const int Divisor = int.MaxValue;

        private sealed class Lane : IOriginalWispRandom
        {
            private readonly Original323SharedBfiRandom shared;
            private readonly Original323ScriptRandomPurpose purpose;
            public Lane(Original323SharedBfiRandom source,
                Original323ScriptRandomPurpose label)
            {
                shared = source;
                purpose = label;
            }
            public int NextInclusive(int minimum, int maximum)
            {
                return shared.NextForPurpose(purpose, minimum, maximum);
            }
        }

        private readonly IOriginal323ScriptRandomDrawWitness witness;
        private int state;
        private long draws;

        public long DrawCount { get { return draws; } }

        public Original323SharedBfiRandom(int observedNativeSeed,
            string originalSeedEvidence,
            IOriginal323ScriptRandomDrawWitness originalObserver)
        {
            if (observedNativeSeed < OriginalSeedMin ||
                observedNativeSeed > OriginalSeedMax ||
                !TrustworthyEvidence(originalSeedEvidence))
                throw new ArgumentException(
                    "원본 GetRandomInt(1,1000000)의 관측된 초기 시드가 필요합니다.");
            witness = originalObserver ??
                throw new ArgumentNullException(nameof(originalObserver));
            state = observedNativeSeed;
        }

        // For a resumed save, original H6 and total draw count must be read
        // from the same authoritative session, never a newly picked seed.
        public static Original323SharedBfiRandom FromObservedState(
            Original323SharedBfiSnapshot originalState,
            string originalStateEvidence,
            IOriginal323ScriptRandomDrawWitness originalObserver)
        {
            if (originalState == null ||
                !TrustworthyEvidence(originalStateEvidence))
                throw new ArgumentException(
                    "원본 난수 상태와 시점의 관측 증빙이 필요합니다.");
            var result = new Original323SharedBfiRandom(
                OriginalSeedMin, originalStateEvidence, originalObserver);
            result.state = originalState.State;
            result.draws = originalState.DrawCount;
            return result;
        }

        public Original323SharedBfiSnapshot Snapshot()
        {
            return new Original323SharedBfiSnapshot(state, draws);
        }

        public IOriginalWispRandom ForPurpose(Original323ScriptRandomPurpose purpose)
        {
            if (!Enum.IsDefined(typeof(Original323ScriptRandomPurpose), purpose))
                throw new ArgumentOutOfRangeException(nameof(purpose));
            return new Lane(this, purpose);
        }

        // Read-only prediction: useful for death-roll reconciliation; does
        // NOT advance shared state or claim a native observation.
        public bool TryPreviewNext(int minimum, int maximum,
            out int originalResult, out int newState)
        {
            originalResult = 0;
            newState = state;
            if (state == 0 || minimum > maximum ||
                (long)maximum - minimum >= int.MaxValue ||
                draws == long.MaxValue)
                return false;

            // Warcraft's integer operations are 32-bit. Keep signed wrap
            // and signed remainder; never use arbitrary modulus-normalization
            // or clamp an out-of-range BFh result to a convenient range.
            int multiplied = unchecked(state * Multiplier + Increment);
            newState = multiplied % Divisor;
            float nextReal = (float)newState / (float)Divisor;
            float scaled = nextReal * ((float)((long)maximum - minimum + 1L));
            if (float.IsNaN(scaled) || float.IsInfinity(scaled) ||
                scaled <= int.MinValue || scaled >= int.MaxValue)
                return false;
            originalResult = checked((int)scaled + minimum);
            return true;
        }

        public int NextForPurpose(Original323ScriptRandomPurpose purpose,
            int minimum, int maximum)
        {
            if (!Enum.IsDefined(typeof(Original323ScriptRandomPurpose), purpose))
                throw new ArgumentOutOfRangeException(nameof(purpose));
            int result, next;
            if (!TryPreviewNext(minimum, maximum, out result, out next))
                throw new InvalidOperationException(
                    "원본 난수 상태 또는 구간을 안전하게 재현할 수 없습니다.");
            string evidence;
            if (!witness.TryConfirmOriginalDraw(draws + 1, purpose,
                state, next, minimum, maximum, result, out evidence) ||
                !TrustworthyEvidence(evidence))
                throw new InvalidOperationException(
                    "원본 공통 난수의 순서/결과가 검증되지 않았습니다.");
            state = next;
            draws++;
            return result;
        }

        private static bool TrustworthyEvidence(string evidence)
        {
            return !string.IsNullOrWhiteSpace(evidence) &&
                evidence.IndexOf("TEST_ONLY", StringComparison.OrdinalIgnoreCase) < 0 &&
                evidence.IndexOf("TEST_FIXTURE", StringComparison.OrdinalIgnoreCase) < 0 &&
                evidence.IndexOf("MODEL_ONLY", StringComparison.OrdinalIgnoreCase) < 0 &&
                evidence.IndexOf("SYNTHETIC", StringComparison.OrdinalIgnoreCase) < 0 &&
                evidence.IndexOf("MOCK_ONLY", StringComparison.OrdinalIgnoreCase) < 0;
        }
    }
}
