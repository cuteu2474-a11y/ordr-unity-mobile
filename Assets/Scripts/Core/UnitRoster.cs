using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
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
