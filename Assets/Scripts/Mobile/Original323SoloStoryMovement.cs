using System;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // Offline single-player translation of original JASS CWL/CWM (60274-60295).
    // The entry is manually requested, never an automatic combat relocation.
    // This is a source-derived MOBILE MODEL, not a live Warcraft observation.
    public sealed class Original323SoloStoryMovement : IOriginal323StoryUnitMovement
    {
        public const double SoloStoryX = -7088.0; // Lo[0] => Bs center
        public const double SoloStoryY = -4784.0;
        public const double SoloStartX = -3776.0;
        public const double SoloStartY = 4480.0;

        private readonly CharacterCombatRuntime combat;
        private readonly OriginalMapBaseStatCatalog map;
        private readonly Func<bool> hasUnrepresentedMissionOrBoss;

        public Original323SoloStoryMovement(CharacterCombatRuntime world,
            OriginalMapBaseStatCatalog originalMap,
            Func<bool> additionalLiveMissionOrBoss = null)
        {
            combat = world ?? throw new ArgumentNullException(nameof(world));
            map = originalMap ?? throw new ArgumentNullException(nameof(originalMap));
            hasUnrepresentedMissionOrBoss = additionalLiveMissionOrBoss;
        }

        private bool IsCombatUnit(long instanceId)
        {
            if (instanceId <= 0 || combat.Game.Phase != SessionPhase.Running)
                return false;
            foreach (var unit in combat.MobileUnitSnapshot())
            {
                if (unit.InstanceId != instanceId) continue;
                var original = map.Find(unit.Unit.Id);
                // CWL checks ownership and Aloc, NOT the presence of an
                // ordinary attack. Support units are allowed in the original.
                if (original == null || string.IsNullOrWhiteSpace(original.OriginalRawcode) ||
                    original.OriginalAbilityRawcodes == null) return false;
                foreach (var ability in original.OriginalAbilityRawcodes)
                    if (ability == "Aloc") return false;
                return true;
            }
            return false;
        }

        // CWL: the live boss and mission counters un[player]/lN[player]
        // block entering the story area, but not exiting via CWM.
        private bool BossOrMissionIsAlive()
        {
            if (hasUnrepresentedMissionOrBoss != null &&
                hasUnrepresentedMissionOrBoss()) return true;
            foreach (var enemy in combat.MobileEnemySnapshot())
                if (enemy.Kind == CharacterEnemyKind.Boss ||
                    enemy.Kind == CharacterEnemyKind.Enraged ||
                    enemy.Kind == CharacterEnemyKind.Challenge)
                    return true;
            return false;
        }

        public bool CanDispatch(long instanceId) =>
            IsCombatUnit(instanceId) && !BossOrMissionIsAlive();

        public bool CanRecall(long instanceId) => IsCombatUnit(instanceId);

        public bool TryDispatch(long instanceId)
        {
            if (!CanDispatch(instanceId)) return false;
            combat.MoveCharacter(instanceId, SoloStoryX, SoloStoryY);
            return true;
        }

        public bool TryRecall(long instanceId)
        {
            if (!CanRecall(instanceId)) return false;
            combat.MoveCharacter(instanceId, SoloStartX, SoloStartY);
            return true;
        }
    }
}
