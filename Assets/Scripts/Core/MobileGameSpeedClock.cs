using System;

namespace Ordr.Mobile.Core
{
    // One conversion boundary from wall-clock frames to the authoritative
    // combat clock. The original game's timings are expressed in combat
    // seconds and are never rewritten when the user selects 2x or 4x.
    //
    // This is session-local UI playback, not a gameplay buff, passive,
    // enemy slow, source-native attack-speed modification or save reward.
    public sealed class MobileGameSpeedClock
    {
        // A render stall / mobile app resume cannot fast-forward several
        // real-world seconds in one tick. Source events remain ordered.
        public const double MaximumRealFrameSeconds = 0.25;
        public int Multiplier { get; private set; } = 1;

        public void SetMultiplier(int value)
        {
            if (value != 1 && value != 2 && value != 4)
                throw new ArgumentOutOfRangeException(nameof(value),
                    "게임 배속은 1배·2배·4배만 지원합니다.");
            Multiplier = value;
        }

        public double Scale(double unscaledDeltaSeconds, bool canAdvance)
        {
            if (double.IsNaN(unscaledDeltaSeconds) ||
                double.IsInfinity(unscaledDeltaSeconds) ||
                unscaledDeltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(unscaledDeltaSeconds),
                    "실시간 프레임 간격이 올바르지 않습니다.");
            if (!canAdvance) return 0;
            return Math.Min(unscaledDeltaSeconds, MaximumRealFrameSeconds) *
                Multiplier;
        }
    }
}
