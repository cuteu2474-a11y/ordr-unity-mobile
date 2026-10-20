using System;

namespace Ordr.Mobile.Core
{
    /// <summary>
    /// Seeded PRNG for test harnesses only.
    /// This is NOT claimed to match the original game's random algorithm.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private uint state;

        public DeterministicRandom(uint seed)
        {
            state = seed == 0 ? 0x6D2B79F5u : seed;
        }

        public uint NextUInt()
        {
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }
    }
}
