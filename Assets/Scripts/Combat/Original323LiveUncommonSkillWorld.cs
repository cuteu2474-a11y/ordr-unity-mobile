using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Captured from the CURRENT, source-observed Warcraft enemy instance.
    // This is not an object-catalog inference of hero, stun resistance,
    // abilities, damage reduction, immunity, or current buffs.
    public sealed class Original323ObservedBashTarget
    {
        public string EnemyId { get; private set; }
        public string OriginalUnitRawcode { get; private set; }
        public string OriginalW3xSha256 { get; private set; }
        public string LiveInstanceEvidence { get; private set; }
        public bool WarcraftHero { get; private set; }
        public bool BashImmune { get; private set; }

        public Original323ObservedBashTarget(string enemyId,
            string originalRawcode, string originalW3xSha256,
            bool warcraftHero, bool bashImmune, string currentNativeEvidence)
        {
            if (string.IsNullOrWhiteSpace(enemyId) ||
                originalRawcode == null || originalRawcode.Length != 4 ||
                string.IsNullOrWhiteSpace(originalW3xSha256) ||
                string.IsNullOrWhiteSpace(currentNativeEvidence))
                throw new ArgumentException(
                    "브룩/타시기/하찌의 현재 원본 적 객체·영웅·면역 증거가 필요합니다.");
            EnemyId = enemyId;
            OriginalUnitRawcode = originalRawcode;
            OriginalW3xSha256 = originalW3xSha256;
            WarcraftHero = warcraftHero;
            BashImmune = bashImmune;
            LiveInstanceEvidence = currentNativeEvidence;
        }
    }

    // Must independently resolve the actual live unit object, native hero
    // flag and current Bash immunity at BOTH pre-hit and proc impact.
    // A guessed mapping from mobile EnemyKind is not an implementation.
    public interface IOriginal323LiveBashTargetWitness
    {
        bool TryObserveCurrent(CombatEnemy liveTarget,
            out Original323ObservedBashTarget current);
    }

    // Shared target inspection for Brook's A07P and the separate AHbh
    // implementations of Ace, Tashigi and Hachi. This only supplies target
    // eligibility; extra PHYSICAL damage still needs an approved resolver.
    public sealed class Original323LiveBashTargetRules :
        IOriginal323UncommonBashTargetRules
    {
        private readonly Original323MonsterObjectAbilityCatalog originalObjects;
        private readonly IOriginal323LiveBashTargetWitness witness;

        public Original323LiveBashTargetRules(
            Original323MonsterObjectAbilityCatalog sourceObjects,
            IOriginal323LiveBashTargetWitness liveWitness)
        {
            originalObjects = sourceObjects ??
                throw new ArgumentNullException(nameof(sourceObjects));
            witness = liveWitness ??
                throw new ArgumentNullException(nameof(liveWitness));
            if (originalObjects.Count != 100)
                throw new ArgumentException(
                    "원본 몬스터 객체 100종 검증 자료가 필요합니다.",
                    nameof(sourceObjects));
        }

        public bool TryInspect(CombatEnemy enemy, out bool warcraftHero,
            out bool bashImmune, out string sourceEvidence)
        {
            warcraftHero = false;
            bashImmune = true;
            sourceEvidence = null;
            if (enemy == null || !enemy.IsAlive) return false;

            Original323ObservedBashTarget observed;
            if (!witness.TryObserveCurrent(enemy, out observed) ||
                observed == null ||
                observed.EnemyId != enemy.Id ||
                observed.OriginalW3xSha256 !=
                    Original323VerifiedMonsterWaveCatalog.SourceW3xSha256 ||
                !originalObjects.HasObject(observed.OriginalUnitRawcode))
                return false;

            // Object *membership* is shown only for source diagnostics.
            // Starting A08Q/Amim/Avul and mobile Boss/Story labels must
            // NEVER be used to assume the live hero or immunity flags.
            warcraftHero = observed.WarcraftHero;
            bashImmune = observed.BashImmune;
            sourceEvidence = "S2 2.323[C] live Bash flags / unit " +
                observed.OriginalUnitRawcode + " / " +
                observed.LiveInstanceEvidence;
            return true;
        }
    }

    // A player may have a source witness for one character before another.
    // Assemble exact original abilities independently; no "all four proofs
    // or none" requirement and no accidental authorization of criticals.
    public static class Original323VerifiedUncommonSkillAssembly
    {
        public static IOriginalUncommonVerifiedProcWorld Create(
            IOriginal323UncommonBashTargetRules bashTargets,
            IOriginal323UncommonBonusDamageRules bonusDamage = null,
            IOriginal323RankyakuVerifiedRules rankyaku = null,
            IOriginal323RobinVerifiedVolleyRules robin = null)
        {
            if (bashTargets == null && bonusDamage != null)
                throw new ArgumentException(
                    "추가 물리 피해에는 현재 원본 대상/면역 검증이 필요합니다.",
                    nameof(bashTargets));
            var parts = new List<IOriginalUncommonVerifiedProcWorld>();
            if (bashTargets != null)
            {
                parts.Add(new Original323UncommonBashWorld(bashTargets));
                if (bonusDamage != null)
                    parts.Add(new Original323UncommonBonusBashWorld(
                        bashTargets, bonusDamage));
            }
            if (rankyaku != null)
                parts.Add(new Original323UncommonRankyakuWorld(rankyaku));
            if (robin != null)
                parts.Add(new Original323RobinMultiTargetWorld(robin));
            if (parts.Count == 0)
                throw new ArgumentException(
                    "원본 스킬 실행을 검증한 공급자가 하나 이상 필요합니다.");
            return parts.Count == 1 ? parts[0] :
                new Original323UncommonEffectRouter(parts.ToArray());
        }
    }
}
