using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // Exact original AEbl-family W3A object rows: ability ownership,
    // maximum cast distance and cooldown. These are NOT creep approvals.
    public sealed class Original323CreepBlinkAbility
    {
        public string CatalogId { get; private set; }
        public string Rawcode { get; private set; }
        public double MaximumDistance { get; private set; }
        public double OriginalCooldownSeconds { get; private set; }

        internal Original323CreepBlinkAbility(string catalogId, string rawcode,
            double distance, double cooldown)
        {
            CatalogId = catalogId; Rawcode = rawcode;
            MaximumDistance = distance;
            OriginalCooldownSeconds = cooldown;
        }
    }

    public static class Original323CreepBlinkAbilityCatalog
    {
        private static readonly Original323CreepBlinkAbility[] entries = {
            new Original323CreepBlinkAbility("R-05", "A05C", 1500, 5),
            new Original323CreepBlinkAbility("R-17", "A038", 1500, 5),
            new Original323CreepBlinkAbility("R-30", "A038", 1500, 5),
            new Original323CreepBlinkAbility("R-39", "A053", 1500, 3),
            new Original323CreepBlinkAbility("L-08", "A05Q", 99999, 15),
            new Original323CreepBlinkAbility("L-29", "A0WF", 1500, 5),
            new Original323CreepBlinkAbility("L-32", "A098", 99999, 8),
            new Original323CreepBlinkAbility("H-13", "A0GP", 1500, 5),
            new Original323CreepBlinkAbility("T-04", "A0GP", 1500, 5),
            new Original323CreepBlinkAbility("LI-01", "A0W7", 1500, 3.5),
            new Original323CreepBlinkAbility("LI-04", "A0UT", 99999, 3),
            new Original323CreepBlinkAbility("LI-05", "A0ID", 2000, 5),
            new Original323CreepBlinkAbility("LI-07", "A0NT", 99999, 10),
            new Original323CreepBlinkAbility("TR-05", "A0IX", 99999, 8),
            new Original323CreepBlinkAbility("TR-07", "A0RB", 99999, 4.5),
            new Original323CreepBlinkAbility("TR-26", "A0MF", 3500, 2),
            new Original323CreepBlinkAbility("I-10", "A0QQ", 2000, 5),
            new Original323CreepBlinkAbility("RN-01", "A0YH", 2000, 8),
            new Original323CreepBlinkAbility("MY-04", "A0VT", 2500, 0.1),
            new Original323CreepBlinkAbility("MY-05", "A0W9", 800, 0.1),
            new Original323CreepBlinkAbility("MY-09", "A0Z8", 99999, 5)
        };
        private static readonly Dictionary<string, Original323CreepBlinkAbility> byId =
            Build();

        private static Dictionary<string, Original323CreepBlinkAbility> Build()
        {
            var result = new Dictionary<string, Original323CreepBlinkAbility>(
                StringComparer.Ordinal);
            foreach (var row in entries) result.Add(row.CatalogId, row);
            return result;
        }

        public static int Count => entries.Length;
        public static bool TryGet(string catalogId,
            out Original323CreepBlinkAbility row) =>
            byId.TryGetValue(catalogId ?? "", out row);
        public static IReadOnlyList<Original323CreepBlinkAbility> All =>
            Array.AsReadOnly(entries);
    }

    // A native witness owns the original movement order, actual ability,
    // mana/health/point debit, native cast timing, cooldown and pathing.
    // Preview MUST be a side-effect-free source proof that THIS actor can
    // cast THIS rawcode to THIS reachable attack landing for THIS creep.
    // The cast is not a free roster.Move or an approximate ranged strike.
    public interface IOriginal323CreepVerifiedBlinkRuntime
    {
        bool TryPreviewOriginalBlink(long actorId, string originalAbilityRawcode,
            Original323CreepEncounter target, out double landX,
            out double landY, out string evidence);
        bool TryIssueOriginalBlink(long actorId, string originalAbilityRawcode,
            string enemyId, double landingX, double landingY,
            string evidence);
        Original323CreepPreparationProgress ObserveOriginalBlink(
            long actorId, string originalAbilityRawcode, string enemyId);
    }

    // Independent SOURCE proof for real attack after arrival. A movement
    // capability alone cannot satisfy target flags, immunity or pathing.
    public interface IOriginal323CreepVerifiedAttackPosition
    {
        // A direct relocation without an ability has its own native
        // movement/path proof. A proposed blink landing CANNOT pass here.
        bool TryConfirmDirectAttackPlacement(long actorId, string catalogId,
            Original323CreepEncounter target, out double x, out double y,
            out string evidence);

        // Proof of attacking AFTER native AEbl completion, not proof of
        // eligibility to move immediately or immunity bypass.
        bool TryConfirmOriginalBlinkLanding(long actorId, string catalogId,
            Original323CreepEncounter target, double x, double y,
            out string evidence);
    }

    // Bridges the 21 documented movement abilities into the existing manual
    // creep dispatch, with no invented skill, free cooldown or new roster.
    // The native blink runtime remains REQUIRED; no production fixture
    // implements it without source evidence.
    public sealed class Original323CreepVerifiedMobilityAuthority :
        IOriginal323CreepConditionalDispatchAuthority,
        IOriginal323CreepAbilityTicketLifecycle
    {
        private readonly CharacterCombatRuntime combat;
        private readonly IOriginal323CreepVerifiedBlinkRuntime nativeBlink;
        private readonly IOriginal323CreepVerifiedAttackPosition verifiedAttack;
        private sealed class IssuedBlink
        {
            public Original323CreepPreparationPlan Plan;
            public bool Completed;
        }
        private readonly Dictionary<long, IssuedBlink> issued =
            new Dictionary<long, IssuedBlink>();

        public void ForgetIssuedOriginalAbility(long instanceId)
        {
            issued.Remove(instanceId);
        }

        public Original323CreepVerifiedMobilityAuthority(
            CharacterCombatRuntime combat,
            IOriginal323CreepVerifiedBlinkRuntime sourceBlink,
            IOriginal323CreepVerifiedAttackPosition sourceAttackPosition)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            nativeBlink = sourceBlink ??
                throw new ArgumentNullException(nameof(sourceBlink));
            verifiedAttack = sourceAttackPosition ??
                throw new ArgumentNullException(nameof(sourceAttackPosition));
        }

        private bool TryOwnedBlink(long id,
            out Original323CreepBlinkAbility ability)
        {
            ability = null;
            if (id <= 0 || combat.Game.Phase != SessionPhase.Running ||
                !combat.IsOwnedCombatCharacter(id))
                return false;
            UnitState owned;
            try { owned = combat.Roster.Get(id); }
            catch (KeyNotFoundException) { return false; }
            return Original323CreepBlinkAbilityCatalog.TryGet(owned.UnitId,
                out ability);
        }

        public bool TryResolveAttackPlacement(long id,
            Original323CreepEncounter target, out double x, out double y)
        {
            x = y = 0;
            Original323CreepBlinkAbility ability;
            if (!TryOwnedBlink(id, out ability) ||
                target == null || target.State != Original323CreepEncounterState.Alive)
                return false;
            // Completed native AEbl is NOT equivalent to a direct path.
            // It authorizes its exact approved landing only while the
            // original per-instance creep dispatch ticket is still live.
            IssuedBlink cast;
            if (combat.IsPreparingCreepDispatch(id) &&
                issued.TryGetValue(id, out cast) && cast.Completed &&
                cast.Plan.CatalogId == ability.CatalogId &&
                cast.Plan.EnemyId == target.EnemyId &&
                cast.Plan.Stage == target.OriginalObject.Stage &&
                cast.Plan.HasApprovedLanding)
            {
                string attackProof;
                double bx = cast.Plan.ApprovedLandingX;
                double by = cast.Plan.ApprovedLandingY;
                if (verifiedAttack.TryConfirmOriginalBlinkLanding(
                        id, ability.CatalogId, target, bx, by,
                        out attackProof) &&
                    Original323BossRoundRules.TrustedRuntimeEvidence(attackProof))
                {
                    x = bx; y = by;
                    return true;
                }
            }
            string evidence;
            if (!verifiedAttack.TryConfirmDirectAttackPlacement(
                    id, ability.CatalogId, target, out x, out y,
                    out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;
            return Finite(x) && Finite(y);
        }

        private static bool Finite(double value) =>
            !double.IsInfinity(value) && !double.IsNaN(value);

        public bool TryPreviewPreparation(long id, string catalogId,
            Original323CreepEncounter target,
            out Original323CreepPreparationPlan plan)
        {
            plan = null;
            Original323CreepBlinkAbility ability;
            if (!TryOwnedBlink(id, out ability) ||
                catalogId != ability.CatalogId ||
                target == null || target.State != Original323CreepEncounterState.Alive)
                return false;

            double x, y;
            string evidence;
            if (!nativeBlink.TryPreviewOriginalBlink(id, ability.Rawcode,
                    target, out x, out y, out evidence) ||
                !Finite(x) || !Finite(y) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;
            // Original AEbl maximum range is a hard *upper* bound, not an
            // invitation to use 99999 as universal attack range.
            UnitState from = combat.Roster.Get(id);
            double dx = x - from.X, dy = y - from.Y;
            if (Math.Abs(dx) > ability.MaximumDistance ||
                Math.Abs(dy) > ability.MaximumDistance ||
                dx * dx + dy * dy >
                    ability.MaximumDistance * ability.MaximumDistance)
                return false;
            // The source must ALSO prove that the exact landing can attack
            // the creep. A validated cast alone proves only travel.
            string attackEvidence;
            if (!verifiedAttack.TryConfirmOriginalBlinkLanding(id,
                    catalogId, target, x, y, out attackEvidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(attackEvidence))
                return false;
            plan = new Original323CreepPreparationPlan(id, catalogId,
                target.EnemyId, target.OriginalObject.Stage, ability.Rawcode,
                Original323CreepPreparationKind.MovementSkill, evidence, x, y);
            return true;
        }

        public bool TryBeginPreparation(Original323CreepPreparationPlan plan)
        {
            if (plan == null ||
                plan.Kind != Original323CreepPreparationKind.MovementSkill)
                return false;
            Original323CreepBlinkAbility ability;
            if (!TryOwnedBlink(plan.InstanceId, out ability) ||
                ability.CatalogId != plan.CatalogId ||
                ability.Rawcode != plan.OriginalAbilityRawcode)
                return false;

            // The original runtime must independently recheck live target,
            // resources, cooldown and path at the moment of native issue.
            // It MAY return false, but must never debit without issuing.
            if (!plan.HasApprovedLanding)
                return false;
            if (!nativeBlink.TryIssueOriginalBlink(
                    plan.InstanceId, ability.Rawcode, plan.EnemyId,
                    plan.ApprovedLandingX, plan.ApprovedLandingY,
                    plan.SourceEvidence))
                return false;
            issued[plan.InstanceId] = new IssuedBlink { Plan = plan };
            return true;
        }

        public Original323CreepPreparationProgress ObservePreparation(
            Original323CreepPreparationPlan plan)
        {
            if (plan == null) return Original323CreepPreparationProgress.Failed;
            Original323CreepBlinkAbility ability;
            if (!TryOwnedBlink(plan.InstanceId, out ability) ||
                ability.CatalogId != plan.CatalogId ||
                ability.Rawcode != plan.OriginalAbilityRawcode)
                return Original323CreepPreparationProgress.Failed;
            var result = nativeBlink.ObserveOriginalBlink(
                plan.InstanceId, ability.Rawcode, plan.EnemyId);
            IssuedBlink active;
            if (issued.TryGetValue(plan.InstanceId, out active) &&
                Object.ReferenceEquals(active.Plan, plan))
            {
                if (result == Original323CreepPreparationProgress.Completed)
                    active.Completed = true;
                else if (result == Original323CreepPreparationProgress.Failed)
                    issued.Remove(plan.InstanceId);
            }
            return result;
        }
    }
}
