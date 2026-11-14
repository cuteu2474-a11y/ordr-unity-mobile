using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Ordr.Mobile.Core
{
    // Only the local mobile fallback can undo speculative BFi calls. An
    // authoritative observed Warcraft/H6 stream must never be rewound here.
    public interface IOriginal323RollbackWispRandom : IOriginalWispRandom
    {
        int CaptureCheckpoint();
        void RestoreCheckpoint(int drawCount);
    }

    [Serializable]
    public sealed class MobileRandomDrawState
    {
        public int Minimum;
        public int Maximum;
        public int Result;
    }

    [Serializable]
    public sealed class MobileRollbackWispRandomState
    {
        public int Version = 1;
        public int Seed;
        public List<MobileRandomDrawState> Draws = new List<MobileRandomDrawState>();
    }

    // One per mobile session: recruitment, resource exchange and lower-grade
    // sales use the same instance. Distribution is uniform, but the unknown
    // Warcraft native seed and unrelated global BFi calls are not claimed.
    public sealed class MobileRollbackWispRandom : IOriginal323RollbackWispRandom
    {
        private struct DrawRange
        {
            public int Minimum;
            public int Maximum;
            public int Result;
            public DrawRange(int min, int max, int result)
            { Minimum = min; Maximum = max; Result = result; }
        }

        private readonly int seed;
        private readonly List<DrawRange> history = new List<DrawRange>();
        private Random random;

        public MobileRollbackWispRandom() : this(CreateSeed()) { }

        // Explicit seed is useful for deterministic non-original unit tests.
        public MobileRollbackWispRandom(int sessionSeed)
        {
            seed = sessionSeed;
            random = new Random(seed);
        }

        public int NextInclusive(int minimum, int maximum)
        {
            if (minimum > maximum || maximum == int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            int value = random.Next(minimum, maximum + 1);
            history.Add(new DrawRange(minimum, maximum, value));
            return value;
        }

        // Complete portable history. Unity JsonUtility persists fields rather
        // than System.Random internals; a saved seed is never trusted without
        // replay-checking every original draw/range before publication.
        public MobileRollbackWispRandomState CaptureState()
        {
            var result = new MobileRollbackWispRandomState { Seed = seed };
            foreach (var draw in history)
                result.Draws.Add(new MobileRandomDrawState {
                    Minimum = draw.Minimum, Maximum = draw.Maximum,
                    Result = draw.Result
                });
            return result;
        }

        public static MobileRollbackWispRandom Restore(MobileRollbackWispRandomState state)
        {
            if (state == null || state.Version != 1 || state.Draws == null ||
                state.Draws.Count > 1000000)
                throw new FormatException("모바일 난수 저장 형식이 유효하지 않습니다.");
            var replay = new MobileRollbackWispRandom(state.Seed);
            foreach (var draw in state.Draws)
            {
                if (draw == null || draw.Maximum == int.MaxValue ||
                    draw.Minimum > draw.Maximum ||
                    draw.Result < draw.Minimum || draw.Result > draw.Maximum)
                    throw new FormatException("모바일 난수 호출 범위가 유효하지 않습니다.");
                if (replay.NextInclusive(draw.Minimum, draw.Maximum) != draw.Result)
                    throw new FormatException("저장된 난수 결과가 시드와 다릅니다.");
            }
            return replay;
        }

        public int CaptureCheckpoint() { return history.Count; }

        public void RestoreCheckpoint(int drawCount)
        {
            if (drawCount < 0 || drawCount > history.Count)
                throw new ArgumentOutOfRangeException(nameof(drawCount));
            var restored = new Random(seed);
            for (int i = 0; i < drawCount; i++)
            {
                var entry = history[i];
                if (restored.Next(entry.Minimum, entry.Maximum + 1) != entry.Result)
                    throw new FormatException("난수 임시 롤백 기록이 손상됐습니다.");
            }
            if (drawCount < history.Count)
                history.RemoveRange(drawCount, history.Count - drawCount);
            random = restored;
        }

        private static int CreateSeed()
        {
            var bytes = new byte[4];
            using (var generator = RandomNumberGenerator.Create())
                generator.GetBytes(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }
    }
}
