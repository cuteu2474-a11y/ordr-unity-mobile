using System;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // A reconstructed, unpublished dependency graph. It deliberately does
    // NOT turn on title-screen continue; story/creeps, RNG, forms, research
    // and source-model auxiliary effects still require their own owners.
    public sealed class OrdrUnpublishedCombatRestoreGraph
    {
        public OrdrRestoredCoreCheckpoint Core { get; private set; }
        public CharacterCombatRuntime Combat { get; private set; }
        public CombatWaveDirector Waves { get; private set; }

        internal OrdrUnpublishedCombatRestoreGraph(
            OrdrRestoredCoreCheckpoint core, CharacterCombatRuntime combat,
            CombatWaveDirector waves)
        {
            Core = core; Combat = combat; Waves = waves;
        }
    }

    public static class OrdrCombatRestoreGraphBuilder
    {
        // Configure must install source-backed attack providers, optional
        // resource catalog and original 65R lane/route BEFORE actor recovery.
        // Source plans must be recomputed from the pinned 2.323[C] catalog,
        // never copied from serialized game data.
        //
        // Any error discards the entire fresh world; no existing live runtime
        // or saved A/B file is changed by this method.
        public static OrdrUnpublishedCombatRestoreGraph Prepare(
            OrdrCoreCheckpointState save,
            CharacterUnitCatalog units, CharacterSkillCatalog profiles,
            IOriginalDamageResolver basicDamage,
            IVerifiedCharacterSkillDamageResolver skillDamage,
            ICombatProcRandom random,
            Action<CharacterCombatRuntime> configure,
            Func<int, VerifiedWavePlan> originalWavePlan)
        {
            if (units == null || profiles == null ||
                basicDamage == null || skillDamage == null ||
                random == null || configure == null ||
                originalWavePlan == null)
                throw new ArgumentNullException(
                    "전투 재구성에는 원본 도감·공격/스킬 계산기·난수·65R 웨이브 계획이 필요합니다.");
            var core = OrdrCoreCheckpoint.PrepareWorldRestore(save);
            if (core.Game.Phase != SessionPhase.Running)
                throw new NotSupportedException("종료된 라운드의 전투 재시작은 지원하지 않습니다.");
            var world = new CharacterCombatRuntime(units, profiles,
                basicDamage, skillDamage, random,
                core.Economy.Units, core.Game);
            configure(world);
            if (world.UsesSourceModelBasicAttacks != save.Combat.HasSourceModelBasicAttacks)
                throw new NotSupportedException("복원된 원본 평타 모드와 공격 공급자가 다릅니다.");
            // No SpawnCharacter(), BeginWave(), SpawnEnemy(), Start() or
            // wallet reward is run for a previously existing actor.
            world.RestoreEnemyWorldDraft(save.Combat);
            world.RestoreActorWorldDraft(save.Combat,
                save.Poneglyph, save.SharedObjectives);
            var waves = CombatWaveDirector.Restore(world, save.Waves,
                originalWavePlan);
            return new OrdrUnpublishedCombatRestoreGraph(core, world, waves);
        }
    }
}
