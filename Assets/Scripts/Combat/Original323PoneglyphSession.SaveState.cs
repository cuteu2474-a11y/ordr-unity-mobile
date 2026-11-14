using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    [Serializable]
    public sealed class Original323PoneglyphAbilityState
    {
        public long UnitId;
        public string Rawcode;
        public bool Present;
    }

    [Serializable]
    public sealed class Original323PoneglyphState
    {
        public int Version = 1;
        public bool[] Missions = new bool[8];
        public List<string> AcceptedEvents = new List<string>();
        public List<Original323PoneglyphAbilityState> AbilityChanges =
            new List<Original323PoneglyphAbilityState>();
        public List<string> RevealedChestIds = new List<string>();
        public int SharedCompletedCount;
        public bool InterpreterActive;
        public bool EverCompleted;
        public bool HasActiveUnit;
        public long ActiveUnitId;
        public int ActiveDurationSeconds;
        public double StartedAt;
        public double FinishesAt;
        public int AttemptCount;
        public string LastMessage;
    }

    public sealed partial class Original323PoneglyphSession
    {
        public Original323PoneglyphState CaptureState()
        {
            var state = new Original323PoneglyphState {
                SharedCompletedCount = SharedCompletedCount,
                InterpreterActive = InterpreterActive,
                EverCompleted = EverCompleted,
                HasActiveUnit = ActiveUnitId.HasValue,
                ActiveUnitId = ActiveUnitId.GetValueOrDefault(),
                ActiveDurationSeconds = ActiveDurationSeconds,
                StartedAt = StartedAt, FinishesAt = FinishesAt,
                AttemptCount = AttemptCount, LastMessage = LastMessage
            };
            Array.Copy(completedMissions, state.Missions, 8);
            state.AcceptedEvents.AddRange(acceptedSourceEvents);
            state.AcceptedEvents.Sort(StringComparer.Ordinal);
            foreach (var unit in abilityOverrides)
                foreach (var item in unit.Value)
                    state.AbilityChanges.Add(new Original323PoneglyphAbilityState {
                        UnitId = unit.Key, Rawcode = item.Key, Present = item.Value
                    });
            state.AbilityChanges.Sort((a,b) => {
                int order = a.UnitId.CompareTo(b.UnitId);
                return order != 0 ? order : string.CompareOrdinal(a.Rawcode,b.Rawcode);
            });
            state.RevealedChestIds.AddRange(revealedChestIds);
            state.RevealedChestIds.Sort(StringComparer.Ordinal);
            ValidateState(state, double.MaxValue);
            return state;
        }

        public static void ValidateState(Original323PoneglyphState state,
            double logicalTime)
        {
            if (state == null || state.Version != 1 ||
                state.Missions == null || state.Missions.Length != 8 ||
                state.AcceptedEvents == null || state.AbilityChanges == null ||
                state.RevealedChestIds == null ||
                state.AcceptedEvents.Count > 100000 ||
                state.AbilityChanges.Count > 8192 || state.RevealedChestIds.Count > 8192 ||
                state.SharedCompletedCount < 0 || state.SharedCompletedCount > 8 ||
                state.AttemptCount < 0 || string.IsNullOrWhiteSpace(state.LastMessage) ||
                state.HasActiveUnit != (state.ActiveUnitId > 0) ||
                (state.HasActiveUnit && (state.EverCompleted ||
                    state.SharedCompletedCount != 8 || state.AttemptCount == 0 ||
                    state.ActiveDurationSeconds != (state.InterpreterActive ? 50 : 150))) ||
                (!state.HasActiveUnit && state.ActiveDurationSeconds != 0) ||
                (state.EverCompleted && (state.SharedCompletedCount != 8 ||
                    state.AttemptCount == 0)) ||
                (state.RevealedChestIds.Count > 0 && !state.EverCompleted) ||
                double.IsNaN(logicalTime) || double.IsInfinity(logicalTime) ||
                logicalTime < 0 || double.IsNaN(state.StartedAt) ||
                double.IsInfinity(state.StartedAt) || double.IsNaN(state.FinishesAt) ||
                double.IsInfinity(state.FinishesAt) || state.StartedAt < 0 ||
                state.FinishesAt < 0 ||
                (state.HasActiveUnit && (state.StartedAt > logicalTime ||
                    Math.Abs(state.FinishesAt - state.StartedAt -
                        state.ActiveDurationSeconds) > 1e-7)))
                throw new FormatException("포네그리프 해독·공유 임무 저장 상태가 잘못됐습니다.");
            int completed = 0;
            foreach (bool value in state.Missions) if (value) completed++;
            if (completed != state.SharedCompletedCount ||
                state.AcceptedEvents.Count < completed)
                throw new FormatException("포네그리프 공유 미션 영수증이 부족합니다.");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in state.AcceptedEvents)
                if (string.IsNullOrWhiteSpace(id) || !unique.Add(id))
                    throw new FormatException("공유 미션 이벤트 ID가 중복됐습니다.");
            unique.Clear();
            foreach (var item in state.AbilityChanges)
                if (item == null || item.UnitId <= 0 || item.Rawcode == null ||
                    item.Rawcode.Length != 4 ||
                    !unique.Add(item.UnitId + ":" + item.Rawcode))
                    throw new FormatException("해독 유닛 기술 상태가 중복됐습니다.");
            unique.Clear();
            foreach (var id in state.RevealedChestIds)
                if (string.IsNullOrWhiteSpace(id) || !unique.Add(id))
                    throw new FormatException("해독 보물 위치가 중복됐습니다.");
        }

        public static Original323PoneglyphSession Restore(
            Original323PoneglyphState state, double logicalTime)
        {
            ValidateState(state, logicalTime);
            var result = new Original323PoneglyphSession {
                SharedCompletedCount = state.SharedCompletedCount,
                InterpreterActive = state.InterpreterActive,
                EverCompleted = state.EverCompleted,
                ActiveUnitId = state.HasActiveUnit ?
                    (long?)state.ActiveUnitId : null,
                ActiveDurationSeconds = state.ActiveDurationSeconds,
                StartedAt = state.StartedAt, FinishesAt = state.FinishesAt,
                AttemptCount = state.AttemptCount,
                LastMessage = state.LastMessage
            };
            Array.Copy(state.Missions, result.completedMissions, 8);
            foreach (var id in state.AcceptedEvents)
                result.acceptedSourceEvents.Add(id);
            foreach (var entry in state.AbilityChanges)
                result.RecordVerifiedAbilityState(entry.UnitId, entry.Rawcode,
                    entry.Present);
            foreach (var id in state.RevealedChestIds)
                result.revealedChestIds.Add(id);
            return result;
        }
    }
}
