using System;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // Pinned native script evidence:
    // w7 -> EVENT_PLAYER_UNIT_ATTACKED for victim player slots 5 and 6;
    // w6 dispatches by GetUnitTypeId(GetAttacker()).
    // Bnm / h00M / 블루노; Bnp / h00A / 후쿠로;
    // Bnr / h00Q / 에넬. Each uses BFi(1,100)<=10.
    public enum Original323DummySpellOrderKind
    {
        PointAtAttackerLocation,
        TargetAttackedEnemy
    }

    public sealed class Original323AttackDummySpellCommand
    {
        public string AttackerRawcode { get; private set; }
        public string OriginalScriptFunction { get; private set; }
        public string HumanOwnerName { get; private set; }
        public string HumanSkillName { get; private set; }
        public string DummyUnitRawcode { get { return "e0KD"; } }
        public string OriginalAbilityRawcode { get; private set; }
        public string WarcraftOrder { get; private set; }
        public Original323DummySpellOrderKind OrderKind { get; private set; }
        public bool SpawnAtAttackerLocation { get { return true; } }
        public bool StoreAttackerPlayerSlotAsDummyUserData { get { return true; } }
        public bool UseAttackerFacing { get; private set; }

        internal Original323AttackDummySpellCommand(string attackerRawcode,
            string originalFunction, string ownerName, string skillName,
            string ability, string order, Original323DummySpellOrderKind orderKind,
            bool useAttackerFacing)
        {
            AttackerRawcode = attackerRawcode;
            OriginalScriptFunction = originalFunction;
            HumanOwnerName = ownerName; HumanSkillName = skillName;
            OriginalAbilityRawcode = ability; WarcraftOrder = order;
            OrderKind = orderKind; UseAttackerFacing = useAttackerFacing;
        }
    }

    // The world MUST use the actual verified e0KD ability behavior:
    // spell geometry, target filters, defense/immunity, damage, ordering,
    // summon lifetime and original Warcraft unit ownership. No guessed
    // splash damage or direct HP subtraction is allowed in this bridge.
    public interface IOriginal323VerifiedAttackDummySpellWorld
    {
        bool TryApplyOriginalDummySpell(
            Original323AttackDummySpellCommand sourceOrder,
            CharacterCombatRuntime liveCombat,
            string attackerInstanceId, string attackedEnemyId,
            out string originalApplicationEvidence);
    }

    public sealed class Original323AttackDummySpellResult
    {
        public bool Triggered { get; private set; }
        public bool Applied { get; private set; }
        public string HumanSkillName { get; private set; }
        public string Evidence { get; private set; }

        internal Original323AttackDummySpellResult(bool triggered, bool applied,
            string skillName, string evidence)
        {
            Triggered = triggered; Applied = applied;
            HumanSkillName = skillName; Evidence = evidence;
        }
    }

    // Links original on-ATTACKED script RNG to the correct original Warcraft
    // dummy spell ORDER. This replaces neither WC3 native critical chance nor
    // a pending original cast/damage resolver.
    public sealed class Original323ScriptAttackEffectCoordinator
    {
        private readonly Original323ScriptAttackProcRolls rolls;
        private readonly IOriginal323VerifiedAttackDummySpellWorld world;

        public Original323ScriptAttackEffectCoordinator(
            Original323ScriptAttackProcRolls verifiedScriptRolls,
            IOriginal323VerifiedAttackDummySpellWorld verifiedWorld)
        {
            rolls = verifiedScriptRolls ??
                throw new ArgumentNullException(nameof(verifiedScriptRolls));
            world = verifiedWorld ??
                throw new ArgumentNullException(nameof(verifiedWorld));
        }

        public static bool Supports(string originalAttackerRawcode)
        {
            return originalAttackerRawcode == "h00M" ||
                   originalAttackerRawcode == "h00A" ||
                   originalAttackerRawcode == "h00Q";
        }

        public static bool TryCreateOriginalSpell(string ownerRawcode,
            out Original323AttackDummySpellCommand command)
        {
            command = null;
            // JASS Bnm / Bnp: CreateUnitAtLoc(HY, 'e0KD', attackerLoc,
            // attackerFacing); IssuePointOrderLoc(dummy, "carrionswarm", attackerLoc).
            if (ownerRawcode == "h00M" || ownerRawcode == "h00A")
            {
                command = new Original323AttackDummySpellCommand(ownerRawcode,
                    ownerRawcode == "h00M" ? "Bnm" : "Bnp",
                    ownerRawcode == "h00M" ? "블루노" : "후쿠로",
                    "범위 충격파", "A0VS", "carrionswarm",
                    Original323DummySpellOrderKind.PointAtAttackerLocation,
                    true);
                return true;
            }
            // JASS Bnr: CreateUnitAtLoc(HY, 'e0KD', attackerLoc,
            // bj_UNIT_FACING); IssueTargetOrder(dummy, "forkedlightning",
            // GetTriggerUnit()), which is the monster being attacked.
            if (ownerRawcode == "h00Q")
            {
                command = new Original323AttackDummySpellCommand(ownerRawcode,
                    "Bnr", "에넬", "갈래 번개", "A086", "forkedlightning",
                    Original323DummySpellOrderKind.TargetAttackedEnemy, false);
                return true;
            }
            return false;
        }

        public bool TryDispatch(string attackerRawcode, string attackerInstanceId,
            string attackedEnemyId, long attackEventSequence,
            CharacterCombatRuntime liveCombat,
            out Original323AttackDummySpellResult result)
        {
            result = null;
            Original323AttackDummySpellCommand command;
            if (liveCombat == null || attackEventSequence <= 0 ||
                string.IsNullOrWhiteSpace(attackerInstanceId) ||
                string.IsNullOrWhiteSpace(attackedEnemyId) ||
                !TryCreateOriginalSpell(attackerRawcode, out command))
                return false;
            bool triggered;
            if (!rolls.TryObserveAndRoll(command.OriginalScriptFunction,
                attackerInstanceId, attackedEnemyId, attackEventSequence,
                out triggered))
                return false;
            if (!triggered)
            {
                result = new Original323AttackDummySpellResult(false, false,
                    command.HumanSkillName, "원본 10% 발동 추첨 실패");
                return true;
            }

            string evidence;
            bool applied = false;
            try
            {
                applied = world.TryApplyOriginalDummySpell(
                    command, liveCombat, attackerInstanceId,
                    attackedEnemyId, out evidence);
            }
            catch (NotSupportedException ex)
            {
                evidence = ex.Message;
            }

            // No fabricated "cast success" when the original native damage,
            // target immunity or dummy order remains unresolved.
            if (!applied ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
            {
                result = new Original323AttackDummySpellResult(true, false,
                    command.HumanSkillName,
                    string.IsNullOrWhiteSpace(evidence) ?
                    "원본 더미 주문 실행 공급자 미완성" : evidence);
                return true;
            }

            result = new Original323AttackDummySpellResult(true, true,
                command.HumanSkillName, evidence);
            return true;
        }
    }
}
