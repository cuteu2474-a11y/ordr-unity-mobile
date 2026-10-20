using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    public enum MissionEventType { UnitFusion, EnemyDefeated, StoryCompleted, ResourceSpent }
    public enum MissionStatus { Active, Completed, Claimed }

    // 실제 미션 종류·요구 수량·보상은 검증된 원작 데이터로만 주입한다.
    public sealed class PersonalMissionDefinition
    {
        public string Id { get; }
        public MissionEventType EventType { get; }
        public string TargetId { get; }
        public int RequiredCount { get; }
        public bool OtherworldOnly { get; }
        public string SourceUrl { get; }
        public PersonalMissionDefinition(string id, MissionEventType eventType, string targetId,
            int requiredCount, bool otherworldOnly, string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(targetId))
                throw new ArgumentException("미션 ID와 목표 ID가 필요합니다.");
            if (requiredCount <= 0) throw new ArgumentOutOfRangeException(nameof(requiredCount));
            Uri uri;
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                throw new ArgumentException("출처 URL이 필요합니다.");
            Id = id; EventType = eventType; TargetId = targetId;
            RequiredCount = requiredCount; OtherworldOnly = otherworldOnly; SourceUrl = sourceUrl;
        }
    }

    public sealed class PersonalMissionEngine
    {
        private readonly bool otherworldEnabled;
        public PersonalMissionDefinition ActiveMission { get; private set; }
        public MissionStatus Status { get; private set; }
        public int Progress { get; private set; }

        public PersonalMissionEngine(bool otherworldEnabled)
        {
            this.otherworldEnabled = otherworldEnabled;
        }

        public void Assign(PersonalMissionDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (ActiveMission != null) throw new InvalidOperationException("기존 미션을 종료해야 합니다.");
            if (definition.OtherworldOnly && !otherworldEnabled)
                throw new InvalidOperationException("이세계모드 전용 미션입니다.");
            ActiveMission = definition;
            Progress = 0;
            Status = MissionStatus.Active;
        }

        public bool Record(MissionEventType eventType, string targetId, int count)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (ActiveMission == null || Status != MissionStatus.Active ||
                ActiveMission.EventType != eventType ||
                !string.Equals(ActiveMission.TargetId, targetId, StringComparison.Ordinal))
                return false;
            int remaining = ActiveMission.RequiredCount - Progress;
            Progress += Math.Min(remaining, count);
            if (Progress == ActiveMission.RequiredCount) Status = MissionStatus.Completed;
            return true;
        }

        // 보상은 외부 거래 서비스에서 원자적으로 지급한 뒤 확정한다.
        public void ConfirmRewardGranted()
        {
            if (ActiveMission == null || Status != MissionStatus.Completed)
                throw new InvalidOperationException("완료된 미션만 보상 지급을 확정할 수 있습니다.");
            Status = MissionStatus.Claimed;
        }

        public void ClearClaimed()
        {
            if (ActiveMission == null || Status != MissionStatus.Claimed)
                throw new InvalidOperationException("보상 수령 후에만 미션을 비울 수 있습니다.");
            ActiveMission = null;
            Progress = 0;
        }

        // 리롤 비용·횟수·추첨 규칙은 외부에서 확인한 뒤 호출한다.
        public void ReplaceAfterVerifiedReroll(PersonalMissionDefinition replacement, int currentRound)
        {
            if (!OtherworldMissionRules.IsRerollRoundEligible(currentRound))
                throw new InvalidOperationException("10라운드부터 개인 미션 리롤이 불가능합니다.");
            if (ActiveMission == null || Status != MissionStatus.Active)
                throw new InvalidOperationException("진행 중인 미션만 리롤할 수 있습니다.");
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            if (replacement.OtherworldOnly && !otherworldEnabled)
                throw new InvalidOperationException("이세계모드 전용 미션입니다.");
            ActiveMission = replacement;
            Progress = 0;
            Status = MissionStatus.Active;
        }
    }
}
