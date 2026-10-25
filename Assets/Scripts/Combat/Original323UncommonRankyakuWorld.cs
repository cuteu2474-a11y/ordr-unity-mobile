using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // A single verified line intersection AFTER original line geometry,
    // target rules, resistance, amplification and HP quantization. Never use
    // the displayed "150 magic" as an assumed final health reduction.
    public sealed class Original323RankyakuLineHit
    {
        public string TargetId { get; private set; }
        public long FinalHpDamage { get; private set; }
        public string SourceEvidence { get; private set; }

        public Original323RankyakuLineHit(string targetId, long hpDamage,
            string evidence)
        {
            if (string.IsNullOrWhiteSpace(targetId) || hpDamage < 0 ||
                string.IsNullOrWhiteSpace(evidence))
                throw new ArgumentException("원본 람각 대상/최종 피해/증거 누락");
            TargetId = targetId;
            FinalHpDamage = hpDamage;
            SourceEvidence = evidence;
        }
    }

    // Geometric source must be the Warcraft attack/line collision, not a
    // mobile "radius 150" approximation. Prepare and Resolve are read-only:
    // the runtime commits all validated targets through CombatResolution.
    public interface IOriginal323RankyakuVerifiedRules
    {
        bool CanAttempt(OriginalUncommonProc proc, string attackerId,
            CombatEnemy primary, out string sourceEvidence);
        bool TryResolveCompleteLine(OriginalUncommonProc proc,
            CharacterCombatRuntime world, string attackerId,
            string originalPrimaryTargetId,
            out IReadOnlyList<Original323RankyakuLineHit> hits,
            out string sourceEvidence);
    }

    public sealed class Original323UncommonRankyakuWorld :
        IOriginalUncommonVerifiedProcWorld
    {
        private readonly IOriginal323RankyakuVerifiedRules rules;
        public Original323UncommonRankyakuWorld(
            IOriginal323RankyakuVerifiedRules verified)
        {
            rules = verified ?? throw new ArgumentNullException(nameof(verified));
        }

        public static bool IsSupported(OriginalUncommonProc proc)
        {
            return proc != null && proc.AbilityRawcode == "A07I" &&
                proc.ProcId == "A07I:rankyaku" &&
                proc.Kind == OriginalUncommonProcKind.LineMagic &&
                proc.ChancePerMillion == 100000 && proc.BonusDamage == 150 &&
                ((proc.UnitId == "U-04" &&
                  proc.OriginalUnitRawcode == "h00M") ||
                 (proc.UnitId == "U-13" &&
                  proc.OriginalUnitRawcode == "h00A"));
        }

        public bool SupportsCompleteEffect(OriginalUncommonProc proc)
        {
            return IsSupported(proc);
        }

        public bool CanAffectTarget(OriginalUncommonProc proc, string attackerId,
            CombatEnemy target)
        {
            string proof;
            return IsSupported(proc) && target != null && target.IsAlive &&
                !string.IsNullOrWhiteSpace(attackerId) &&
                rules.CanAttempt(proc, attackerId, target, out proof) &&
                !string.IsNullOrWhiteSpace(proof);
        }

        public bool TryApplyCompleteEffect(OriginalUncommonProc proc,
            CharacterCombatRuntime world, string attackerId, string targetId,
            out string originalProof)
        {
            originalProof = null;
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!IsSupported(proc)) return false;
            IReadOnlyList<Original323RankyakuLineHit> hits;
            string evidence;
            if (!rules.TryResolveCompleteLine(proc, world, attackerId,
                targetId, out hits, out evidence) ||
                hits == null || hits.Count == 0 ||
                string.IsNullOrWhiteSpace(evidence))
                return false;
            long applied;
            if (!world.TryCommitOriginalUncommonLine(attackerId, targetId,
                proc.ProcId, hits, out applied))
                return false;
            originalProof = "W3X A07I Rankyaku / " + evidence +
                " / hits=" + hits.Count +
                " / actual total HP=" + applied.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
    }

    // Explicit composition; do not let registering Rankyaku accidentally
    // authorize Lucci/Fukuro crit, Robin's multihit or unsupported Bash.
    public sealed class Original323UncommonEffectRouter :
        IOriginalUncommonVerifiedProcWorld
    {
        private readonly IReadOnlyList<IOriginalUncommonVerifiedProcWorld> handlers;

        public Original323UncommonEffectRouter(
            params IOriginalUncommonVerifiedProcWorld[] completeHandlers)
        {
            if (completeHandlers == null || completeHandlers.Length == 0)
                throw new ArgumentException("검증된 원본 효과 처리기가 필요합니다.");
            var copy = new List<IOriginalUncommonVerifiedProcWorld>();
            foreach (var handler in completeHandlers)
            {
                if (handler == null || copy.Contains(handler))
                    throw new ArgumentException("중복·누락된 원본 효과 처리기");
                copy.Add(handler);
            }
            handlers = copy.AsReadOnly();
        }

        private IOriginalUncommonVerifiedProcWorld Resolve(OriginalUncommonProc proc)
        {
            IOriginalUncommonVerifiedProcWorld match = null;
            foreach (var handler in handlers)
            {
                if (!handler.SupportsCompleteEffect(proc)) continue;
                if (match != null)
                    throw new InvalidOperationException(
                        "원본 스킬에 둘 이상의 검증 실행기가 겹칩니다: " +
                        (proc == null ? "<null>" : proc.ProcId));
                match = handler;
            }
            return match;
        }

        public bool SupportsCompleteEffect(OriginalUncommonProc proc)
        {
            return Resolve(proc) != null;
        }

        public bool CanAffectTarget(OriginalUncommonProc proc,
            string attackerId, CombatEnemy target)
        {
            var handler = Resolve(proc);
            return handler != null &&
                handler.CanAffectTarget(proc, attackerId, target);
        }

        public bool TryApplyCompleteEffect(OriginalUncommonProc proc,
            CharacterCombatRuntime world, string attackerId, string targetId,
            out string proof)
        {
            proof = null;
            var handler = Resolve(proc);
            return handler != null &&
                handler.TryApplyCompleteEffect(proc, world, attackerId,
                    targetId, out proof);
        }
    }
}
