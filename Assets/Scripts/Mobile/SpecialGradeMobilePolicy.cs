using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Mobile
{
    // 2026-10-23 사용자 확정. 전투 기술과 재료 분해를 구분한다.
    public static class SpecialGradeMobilePolicy
    {
        public const string Capone = "S-02";
        public const string Law = "S-05";
        public const string Marco = "S-08";
        public const string Moria = "S-09";
        public const string Absalom = "S-17";
        public const string DockingFive = "S-32";

        public static bool IsManualOnly(string unitId)
        {
            return unitId == Capone;
        }

        public static bool UsesCurrentAttackTarget(string unitId)
        {
            return unitId == DockingFive;
        }

        public static bool UsesEnemyCenteredArea(string unitId)
        {
            return unitId == Marco;
        }

        // 후보는 매 시전 시점에 재구성한다. 합성·판매·파견 후에도 다시 평가한다.
        // DPS는 외부 LLM 대신 게임 안의 현재 스탯·효과 추정기에서 받는다.
        public static long? ChooseLawTarget(bool casterInStory,
            IEnumerable<LawDpsCandidate> candidates)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            LawDpsCandidate best = null;
            foreach (var item in candidates)
            {
                if (item == null) throw new ArgumentException("null 후보는 사용할 수 없습니다.");
                if (!item.CanReceiveAcceleration || item.InStory != casterInStory ||
                    !item.ExpectedDps.HasValue) continue;
                if (best == null || item.ExpectedDps.Value > best.ExpectedDps.Value ||
                    (item.ExpectedDps.Value == best.ExpectedDps.Value &&
                     item.InstanceId < best.InstanceId)) best = item;
            }
            return best == null ? (long?)null : best.InstanceId;
        }

        // 평타 무제한은 크립/해왕류 접근 자격을 해제하지 않는다.
        // 압살롬의 비행 여부는 해당 버전 객체/실제 폼에서 공급한다.
        public static bool CanEnterSideBattle(bool requiresAirMovement,
            bool verifiedAirMovement, bool otherOriginalConditionsSatisfied)
        {
            return otherOriginalConditionsSatisfied &&
                (!requiresAirMovement || verifiedAirMovement);
        }
    }

    public sealed class LawDpsCandidate
    {
        public long InstanceId { get; private set; }
        public bool InStory { get; private set; }
        public bool CanReceiveAcceleration { get; private set; }
        public double? ExpectedDps { get; private set; }

        public LawDpsCandidate(long id, bool inStory, bool canReceive, double? dps)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (dps.HasValue && (double.IsNaN(dps.Value) ||
                double.IsInfinity(dps.Value) || dps.Value < 0))
                throw new ArgumentOutOfRangeException(nameof(dps));
            InstanceId = id; InStory = inStory;
            CanReceiveAcceleration = canReceive; ExpectedDps = dps;
        }
    }

    // 기존 엔진의 아군 지정 시전 경로를 연결한다. 실제 자원/중복 가속/시전
    // 자격은 여기서 검증한다. 미연결이면 무료 버프를 만들지 않는다.
    public interface ISpecialGradeLawCastService
    {
        bool CanCastNow(long lawId, double time);
        bool TryEvaluateTarget(long lawId, long targetId, double time,
            out double expectedDps);
        bool TryCast(long lawId, long targetId, double time);
    }

    // 한 적에게 여러 모리아가 기여해도 싱글플레이 플레이어의 좀비 보상은
    // 한 번이다. 피해 0, 단순 디버프, 다른 적, 누수는 처치 참여가 아니다.
    public sealed class MoriaParticipationLedger
    {
        private readonly HashSet<string> participated =
            new HashSet<string>(StringComparer.Ordinal);

        public void Record(string enemyId, bool isMoria, long actualDamage,
            CharacterEnemyKind kind)
        {
            if (string.IsNullOrWhiteSpace(enemyId)) throw new ArgumentException(nameof(enemyId));
            if (actualDamage < 0) throw new ArgumentOutOfRangeException(nameof(actualDamage));
            if (isMoria && actualDamage > 0 && kind == CharacterEnemyKind.Line)
                participated.Add(enemyId);
        }

        public bool Complete(string enemyId)
        {
            return participated.Remove(enemyId);
        }

        public void Forget(string enemyId) { participated.Remove(enemyId); }
    }
}
