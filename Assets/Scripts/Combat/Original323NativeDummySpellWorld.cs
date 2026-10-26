using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Original W3X file SHA-256:
    // 0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c
    // Units/AbilityData.slk: A0VS (AUcs), A086 (ANfl).
    // Units/UnitAbilities.slk: e0KD owns both, plus Aloc/Avul.
    // The raw native fields are NOT pre-armor/post-immunity HP damage.
    public sealed class Original323NativeDummyAbilityFacts
    {
        public string AbilityRawcode { get; private set; }
        public string ParentWarcraftAbility { get; private set; }
        public string DummyUnitRawcode { get { return "e0KD"; } }
        public string Order { get; private set; }
        public string WarcraftTargetMask { get; private set; }
        public double Area1 { get; private set; }
        public double CastRange1 { get; private set; }
        public double DataA1 { get; private set; }
        public double DataB1 { get; private set; }
        public double DataC1 { get; private set; }
        public double DataD1 { get; private set; }
        public int? MaxNativeTargets { get; private set; }

        internal Original323NativeDummyAbilityFacts(string rawcode,
            string parent, string order, string targets,
            double area, double range, double a, double b,
            double c, double d, int? maxTargets)
        {
            AbilityRawcode = rawcode; ParentWarcraftAbility = parent;
            Order = order; WarcraftTargetMask = targets;
            Area1 = area; CastRange1 = range;
            DataA1 = a; DataB1 = b; DataC1 = c; DataD1 = d;
            MaxNativeTargets = maxTargets;
        }
    }

    public static class Original323NativeDummyAbilityCatalog
    {
        public static bool TryGet(Original323AttackDummySpellCommand command,
            out Original323NativeDummyAbilityFacts facts)
        {
            facts = null;
            if (command == null) return false;
            Original323AttackDummySpellCommand original;
            if (!Original323ScriptAttackEffectCoordinator.TryCreateOriginalSpell(
                command.AttackerRawcode, out original) ||
                command.DummyUnitRawcode != "e0KD" ||
                original.OriginalScriptFunction != command.OriginalScriptFunction ||
                original.OriginalAbilityRawcode != command.OriginalAbilityRawcode ||
                original.WarcraftOrder != command.WarcraftOrder ||
                original.OrderKind != command.OrderKind ||
                original.UseAttackerFacing != command.UseAttackerFacing ||
                !command.SpawnAtAttackerLocation ||
                !command.StoreAttackerPlayerSlotAsDummyUserData)
                return false;
            if (command.OriginalAbilityRawcode == "A0VS")
            {
                // AUcs/Carrion Swarm: Area=200, Range=1700;
                // DataA=150, B=70000000, C=800, D=250.
                // DataB is NOT a unit count and NOT final HP damage.
                facts = new Original323NativeDummyAbilityFacts(
                    "A0VS", "AUcs", "carrionswarm", "ground,air",
                    200, 1700, 150, 70000000, 800, 250, null);
                return true;
            }
            if (command.OriginalAbilityRawcode == "A086")
            {
                // ANfl/Forked Lightning: Area=125, Range=1750;
                // DataA=1500, B=4 targets, C=1700, D=500.
                facts = new Original323NativeDummyAbilityFacts(
                    "A086", "ANfl", "forkedlightning",
                    "air,ground,enemy,neutral,organic",
                    125, 1750, 1500, 4, 1700, 500, 4);
                return true;
            }
            return false;
        }
    }

    // Already verified final HP damage AFTER original target collision,
    // Warcraft immunity/defense, ability behavior and final HP conversion.
    public sealed class Original323NativeDummySpellHit
    {
        public string TargetId { get; private set; }
        public long FinalHpDamage { get; private set; }
        public string OriginalNativeHitEvidence { get; private set; }

        public Original323NativeDummySpellHit(string targetId,
            long finalHpDamage, string evidence)
        {
            if (string.IsNullOrWhiteSpace(targetId) ||
                finalHpDamage < 0 ||
                string.IsNullOrWhiteSpace(evidence))
                throw new ArgumentException("원본 주문 타격 대상·최종 체력 피해·증빙 누락");
            TargetId = targetId; FinalHpDamage = finalHpDamage;
            OriginalNativeHitEvidence = evidence;
        }
    }

    // Read-only source resolver must observe the actual completed Warcraft
    // dummy order and its hit targets. This adapter MUST NOT invent a 2D
    // radius, calculate raw DataA as HP loss, skip spell immunity, reorder
    // targets or apply damage itself.
    public interface IOriginal323NativeDummySpellHitWitness
    {
        bool TryResolveObservedNativeSpell(
            Original323AttackDummySpellCommand originalOrder,
            Original323NativeDummyAbilityFacts originalObject,
            CharacterCombatRuntime combat, string attackerId,
            string originalAttackedEnemyId,
            out IReadOnlyList<Original323NativeDummySpellHit> confirmedHits,
            out string originalCastEvidence);
    }

    // Concrete runtime adapter for the existing CombatResolution path.
    // Successful observed casts with NO HP hits are valid (miss / immunity).
    // No source witness -> no HP changes.
    public sealed class Original323ObservedAttackDummySpellWorld :
        IOriginal323VerifiedAttackDummySpellWorld
    {
        private readonly IOriginal323NativeDummySpellHitWitness source;

        public Original323ObservedAttackDummySpellWorld(
            IOriginal323NativeDummySpellHitWitness originalSource)
        {
            source = originalSource ??
                throw new ArgumentNullException(nameof(originalSource));
        }

        public bool TryApplyOriginalDummySpell(
            Original323AttackDummySpellCommand sourceOrder,
            CharacterCombatRuntime liveCombat,
            string attackerInstanceId, string attackedEnemyId,
            out string originalApplicationEvidence)
        {
            originalApplicationEvidence = null;
            Original323NativeDummyAbilityFacts facts;
            if (liveCombat == null ||
                !Original323NativeDummyAbilityCatalog.TryGet(
                    sourceOrder, out facts)) return false;

            IReadOnlyList<Original323NativeDummySpellHit> hits;
            string proof;
            if (!source.TryResolveObservedNativeSpell(sourceOrder, facts,
                liveCombat, attackerInstanceId, attackedEnemyId,
                out hits, out proof) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(proof) ||
                hits == null)
                return false;

            long actualHp;
            if (!liveCombat.TryCommitOriginalDummySpellHits(sourceOrder,
                attackerInstanceId, attackedEnemyId, hits, out actualHp))
                return false;
            originalApplicationEvidence = proof +
                " / 원본 타격 대상 " + hits.Count +
                " / 체력 피해 " + actualHp;
            return true;
        }
    }
}
