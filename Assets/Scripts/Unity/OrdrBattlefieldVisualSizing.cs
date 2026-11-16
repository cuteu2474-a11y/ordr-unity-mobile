using UnityEngine;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Unity
{
    // Shared, presentation-only sizes for the main battlefield.
    // Never feed these dimensions into path movement, world positions,
    // attack range, enemy collision, HP, or gameplay targeting.
    public static class OrdrBattlefieldVisualSizing
    {
        public const float EnemyDisplayMultiplier = 1.45f;
        public const float RoadDisplayMultiplier = 1.45f;

        private const float OriginalRoadWidth = 110f;
        private const float OriginalRoadBorderPadding = 7f;
        private const float OriginalRoadShoulderPadding = 20f;
        private const float OriginalArrowDiameter = 52f;
        private const float OriginalSpawnMarkerDiameter = 115f;

        public static float LaneWidth =>
            OriginalRoadWidth * RoadDisplayMultiplier;
        public static float BorderWidth =>
            (OriginalRoadWidth + OriginalRoadBorderPadding) * RoadDisplayMultiplier;
        public static float ShoulderWidth =>
            (OriginalRoadWidth + OriginalRoadShoulderPadding) * RoadDisplayMultiplier;
        public static float ArrowDiameter =>
            OriginalArrowDiameter * RoadDisplayMultiplier;
        public static float SpawnMarkerDiameter =>
            OriginalSpawnMarkerDiameter * RoadDisplayMultiplier;

        // Preserve the previous boss/regular visual distinction and Bonney's
        // in-game shrink effect, enlarging only the final drawn sprites.
        public static float EnemyDiameter(float markerDiameterWorldUnits,
            CharacterEnemyKind kind, bool bonneyReduced)
        {
            float roleScale = kind == CharacterEnemyKind.Boss ? 1.32f : 1.03f;
            return Mathf.Max(1f, markerDiameterWorldUnits) * roleScale *
                EnemyDisplayMultiplier * (bonneyReduced ? 0.60f : 1f);
        }
    }
}
