using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    // 항법 노드의 효과·비용·선행조건은 검증된 도감 데이터에서만 전달한다.
    // 임의 노드나 보상을 기본값으로 생성하지 않는다.
    public sealed class NavigationNode
    {
        public string Id { get; }
        public string RouteId { get; }
        public string[] RequiredNodeIds { get; }
        public NavigationNode(string id, string routeId, string[] requiredNodeIds)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(routeId))
                throw new ArgumentException("항법 노드와 경로 식별자가 필요합니다.");
            Id = id;
            RouteId = routeId;
            RequiredNodeIds = requiredNodeIds == null ? new string[0] : (string[])requiredNodeIds.Clone();
        }
    }

    public sealed class NavigationProgression
    {
        private readonly Dictionary<string, NavigationNode> nodes = new Dictionary<string, NavigationNode>();
        private readonly HashSet<string> acquired = new HashSet<string>();
        public string ActiveRouteId { get; private set; }
        public int AcquiredCount => acquired.Count;

        public NavigationProgression(IEnumerable<NavigationNode> verifiedNodes)
        {
            if (verifiedNodes == null) throw new ArgumentNullException(nameof(verifiedNodes));
            foreach (var node in verifiedNodes)
            {
                if (node == null) throw new ArgumentException("null 노드는 허용하지 않습니다.");
                nodes.Add(node.Id, node);
            }
            foreach (var node in nodes.Values)
                foreach (string prerequisite in node.RequiredNodeIds)
                    if (!nodes.ContainsKey(prerequisite) || nodes[prerequisite].RouteId != node.RouteId)
                        throw new ArgumentException("선행 노드는 같은 항법 경로에 존재해야 합니다.");
        }

        public void ChooseRoute(string routeId)
        {
            if (ActiveRouteId != null) throw new InvalidOperationException("항법 경로는 이미 선택되었습니다.");
            if (string.IsNullOrWhiteSpace(routeId)) throw new ArgumentException("항법 경로가 필요합니다.");
            bool found = false;
            foreach (var node in nodes.Values)
                if (node.RouteId == routeId) { found = true; break; }
            if (!found) throw new KeyNotFoundException("검증된 항법 경로가 없습니다.");
            ActiveRouteId = routeId;
        }

        public bool CanAcquire(string nodeId)
        {
            NavigationNode node;
            if (!nodes.TryGetValue(nodeId, out node) || node.RouteId != ActiveRouteId || acquired.Contains(nodeId))
                return false;
            foreach (var prerequisite in node.RequiredNodeIds)
                if (!acquired.Contains(prerequisite)) return false;
            return true;
        }

        public void Acquire(string nodeId)
        {
            if (!CanAcquire(nodeId)) throw new InvalidOperationException("노드 획득 조건을 충족하지 않았습니다.");
            acquired.Add(nodeId);
            // 비용 차감·보상 지급은 원작 규칙 검증 후 별도 서비스에서 원자적으로 처리한다.
        }

        public bool HasAcquired(string nodeId) => acquired.Contains(nodeId);
    }
}
