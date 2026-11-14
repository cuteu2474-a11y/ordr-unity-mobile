using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    [Serializable]
    public sealed class UnitRosterEntryState
    {
        public long InstanceId;
        public string UnitId;
        public double X;
        public double Y;
    }

    [Serializable]
    public sealed class UnitRosterState
    {
        public int Version = 1;
        public long NextInstanceId = 1;
        public bool HasSelection;
        public long SelectedInstanceId;
        public List<UnitRosterEntryState> Units = new List<UnitRosterEntryState>();
    }

    // 유닛의 생성·선택·배치를 위한 최소 상태 저장소. 소환 비용/확률은 아직 연결하지 않는다.
    public sealed class UnitRoster
    {
        private readonly Dictionary<long, UnitState> units = new Dictionary<long, UnitState>();
        private long nextId = 1;
        public long? SelectedId { get; private set; }
        public int Count => units.Count;

        public long Add(string verifiedUnitId, double x, double y)
        {
            if (string.IsNullOrWhiteSpace(verifiedUnitId)) throw new ArgumentException("유닛 식별자가 필요합니다.");
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y))
                throw new ArgumentOutOfRangeException(nameof(x));
            if (nextId == long.MaxValue)
                throw new OverflowException("유닛 인스턴스 ID가 초과됐습니다.");
            long id = nextId++;
            units.Add(id, new UnitState(id, verifiedUnitId, x, y));
            return id;
        }

        public void Select(long id)
        {
            if (!units.ContainsKey(id)) throw new KeyNotFoundException("존재하지 않는 유닛입니다.");
            SelectedId = id;
        }

        public void MoveSelected(double x, double y)
        {
            if (!SelectedId.HasValue) throw new InvalidOperationException("선택된 유닛이 없습니다.");
            Move(SelectedId.Value, x, y);
        }

        // 자동 전투/드래그 조작은 현재 UI 선택 상태를 바꾸지 않고 좌표를 갱신한다.
        public void Move(long id, double x, double y)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y))
                throw new ArgumentOutOfRangeException(nameof(x));
            UnitState unit;
            if (!units.TryGetValue(id, out unit))
                throw new KeyNotFoundException("존재하지 않는 유닛입니다.");
            units[id] = new UnitState(unit.InstanceId, unit.UnitId, x, y);
        }

        public UnitState Get(long id) => units[id];

        // Stable instance identity is required by fusion receipts, spells,
        // story dispatch and scheduled attacks. Never replay Add() on load.
        public UnitRosterState CaptureState()
        {
            var state = new UnitRosterState {
                NextInstanceId = nextId,
                HasSelection = SelectedId.HasValue,
                SelectedInstanceId = SelectedId.GetValueOrDefault()
            };
            foreach (var unit in units.Values)
                state.Units.Add(new UnitRosterEntryState {
                    InstanceId = unit.InstanceId, UnitId = unit.UnitId,
                    X = unit.X, Y = unit.Y
                });
            state.Units.Sort((a, b) => a.InstanceId.CompareTo(b.InstanceId));
            return state;
        }

        // Build a completely independent roster, then hand it to the new
        // combat world. Bad saves cannot partially mutate a live roster.
        public static UnitRoster Restore(UnitRosterState state)
        {
            if (state == null || state.Version != 1 || state.Units == null ||
                state.NextInstanceId <= 0)
                throw new FormatException("유닛 저장 형식이 지원되지 않습니다.");
            var roster = new UnitRoster();
            foreach (var entry in state.Units)
            {
                if (entry == null || entry.InstanceId <= 0 ||
                    entry.InstanceId >= state.NextInstanceId ||
                    string.IsNullOrWhiteSpace(entry.UnitId) ||
                    double.IsNaN(entry.X) || double.IsInfinity(entry.X) ||
                    double.IsNaN(entry.Y) || double.IsInfinity(entry.Y) ||
                    roster.units.ContainsKey(entry.InstanceId))
                    throw new FormatException("유닛 ID·좌표·중복 저장 상태가 잘못됐습니다.");
                roster.units.Add(entry.InstanceId, new UnitState(entry.InstanceId,
                    entry.UnitId, entry.X, entry.Y));
            }
            if (state.HasSelection)
            {
                if (state.SelectedInstanceId <= 0 ||
                    !roster.units.ContainsKey(state.SelectedInstanceId))
                    throw new FormatException("선택된 유닛의 원본 ID가 저장에 없습니다.");
                roster.SelectedId = state.SelectedInstanceId;
            }
            else if (state.SelectedInstanceId != 0)
                throw new FormatException("유닛 선택 플래그가 잘못됐습니다.");
            roster.nextId = state.NextInstanceId;
            return roster;
        }

        public bool Remove(long id)
        {
            bool removed = units.Remove(id);
            if (SelectedId == id) SelectedId = null;
            return removed;
        }
    }

    public readonly struct UnitState
    {
        public readonly long InstanceId;
        public readonly string UnitId;
        public readonly double X;
        public readonly double Y;
        public UnitState(long instanceId, string unitId, double x, double y)
        { InstanceId = instanceId; UnitId = unitId; X = x; Y = y; }
    }
}
