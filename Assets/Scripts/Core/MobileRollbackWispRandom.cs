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

    // One per mobile session: recruitment, resource exchange and lower-grade
    // sales use the same instance. Distribution is uniform, but the unknown
    // Warcraft native seed and unrelated global BFi calls are not claimed.
    public sealed class MobileRollbackWispRandom : IOriginal323RollbackWispRandom
    {
        private struct DrawRange
        {
            public int Minimum;
            public int Maximum;
            public DrawRange(int min, int max)
            { Minimum = min; Maximum = max; }
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
            history.Add(new DrawRange(minimum, maximum));
            return value;
        }

        public int CaptureCheckpoint() { return history.Count; }

        public void RestoreCheckpoint(int drawCount)
        {
            if (drawCount < 0 || drawCount > history.Count)
                throw new ArgumentOutOfRangeException(nameof(drawCount));
            var restored = new Random(seed);
            for (int i = 0; i < drawCount; i++)
                restored.Next(history[i].Minimum, history[i].Maximum + 1);
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
