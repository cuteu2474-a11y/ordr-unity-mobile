using System;

namespace Ordr.Mobile.Core
{
    // CSK, war3map.j SHA-256 96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d
    // Original resource lane: consume one e0IX, roll 1..100, 67% wood 1,
    // otherwise gold 75 * X3. This is NOT the trait-engineering or Vivi lane.
    public sealed class OriginalRandomWispExchangeOutcome
    {
        public string RewardResourceId { get; private set; }
        public long RewardAmount { get; private set; }

        internal OriginalRandomWispExchangeOutcome(string resourceId, long amount)
        {
            RewardResourceId = resourceId;
            RewardAmount = amount;
        }
    }

    // Produces the original 1..100 uniform distribution, NOT the exact
    // Warcraft RNG stream/seed. A verified JASS-compatible RNG can replace it.
    public sealed class MobileUniformWispRandom : IOriginalWispRandom
    {
        private readonly Random random = new Random();

        public int NextInclusive(int minimum, int maximum)
        {
            if (minimum > maximum || maximum == int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            return random.Next(minimum, maximum + 1);
        }
    }

    public sealed class OriginalRandomWispResourceExchange
    {
        public const string OfferId = "original:e0IX:resource_exchange";
        public const int LumberChancePercent = 67;
        private readonly EconomyWallet wallet;
        private readonly GameSession game;
        private readonly IOriginalWispRandom random;

        public OriginalRandomWispResourceExchange(EconomyWallet sharedWallet,
            GameSession session, IOriginalWispRandom sessionRandom)
        {
            wallet = sharedWallet ?? throw new ArgumentNullException(nameof(sharedWallet));
            game = session ?? throw new ArgumentNullException(nameof(session));
            random = sessionRandom ?? throw new ArgumentNullException(nameof(sessionRandom));
        }

        // GameSession.CurrentWave is progressed only by the verified wave
        // director. Before wave one, no round->X3 mapping has been approved:
        // reject instead of paying an invented "round zero" gold amount.
        public bool CanSpend =>
            game.Phase == SessionPhase.Running && game.CurrentWave > 0 &&
            wallet.Balance(WispResourceIds.Random) >= 1;

        // Only an explicit UI confirmation may call this method. One wallet
        // TryExchange atomically debits the Wisp and credits the result.
        public OriginalRandomWispExchangeOutcome SpendOne()
        {
            if (!CanSpend) return null;
            int originalRoll = random.NextInclusive(1, 100);
            if (originalRoll < 1 || originalRoll > 100)
                throw new InvalidOperationException("랜덤위습 원본 난수 범위가 올바르지 않습니다.");

            string resourceId = originalRoll <= LumberChancePercent ? "wood" : "gold";
            long amount = resourceId == "wood" ? 1L : checked((long)game.CurrentWave * 75L);
            ResourceBundle cost = new ResourceBundle().Add(WispResourceIds.Random, 1);
            ResourceBundle reward = new ResourceBundle().Add(resourceId, amount);
            if (!wallet.TryExchange(cost, reward)) return null;

            return new OriginalRandomWispExchangeOutcome(resourceId, amount);
        }
    }
}
