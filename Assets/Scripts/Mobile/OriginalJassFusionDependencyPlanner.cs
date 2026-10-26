using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Mobile
{
    public sealed class OriginalJassFusionDependencyStep
    {
        public int Depth { get; private set; }
        public string Rawcode { get; private set; }
        public string Name { get; private set; }
        public int Count { get; private set; }
        public string Source { get; private set; }
        public int SpecialCount { get; private set; }
        public bool DepthLimited { get; private set; }

        internal OriginalJassFusionDependencyStep(int depth, string raw,
            string name, int count, string source, int special, bool limited)
        {
            Depth = depth; Rawcode = raw; Name = name;
            Count = count; Source = source; SpecialCount = special;
            DepthLimited = limited;
        }
    }

    // Read-only source CWR dependency route. NEVER executes a recipe.
    public sealed class OriginalJassFusionDependencyPlanner
    {
        private readonly OriginalJassFusionCatalog source;
        private readonly OriginalFusionObjectPreviewCatalog names;
        private readonly Dictionary<string, List<OriginalJassFusionRegistration>> origins =
            new Dictionary<string, List<OriginalJassFusionRegistration>>(StringComparer.Ordinal);

        public OriginalJassFusionDependencyPlanner(OriginalJassFusionCatalog recipes,
            OriginalFusionObjectPreviewCatalog labels)
        {
            source = recipes ?? throw new ArgumentNullException(nameof(recipes));
            names = labels ?? throw new ArgumentNullException(nameof(labels));
            foreach (var r in source.All)
            {
                if (r.Results.Count != 1 || r.Results[0].Kind != "UNIT" ||
                    r.Results[0].Count != 1) continue;
                string raw = r.Results[0].Rawcode;
                List<OriginalJassFusionRegistration> list;
                if (!origins.TryGetValue(raw, out list))
                {
                    list = new List<OriginalJassFusionRegistration>();
                    origins.Add(raw, list);
                }
                list.Add(r);
            }
        }

        public IReadOnlyList<OriginalJassFusionDependencyStep> Build(
            string ability, int maxDepth)
        {
            if (maxDepth < 1 || maxDepth > 6)
                throw new ArgumentOutOfRangeException(nameof(maxDepth));
            var recipe = source.FindByAbility(ability);
            if (recipe == null) throw new KeyNotFoundException("원본에 없는 조합 능력");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in recipe.Results)
                if (item.Kind == "UNIT") seen.Add(item.Rawcode);
            var result = new List<OriginalJassFusionDependencyStep>();
            Expand(recipe, 0, 1, maxDepth, seen, result);
            return result.AsReadOnly();
        }

        private void Expand(OriginalJassFusionRegistration recipe, int depth,
            int times, int limit, HashSet<string> seen,
            List<OriginalJassFusionDependencyStep> lines)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var input in recipe.OrderedConditions)
            {
                if (input.Kind != "UNIT") continue;
                int previous;
                if (!counts.TryGetValue(input.Rawcode, out previous))
                {
                    order.Add(input.Rawcode);
                    previous = 0;
                }
                counts[input.Rawcode] = checked(previous + input.Count);
            }
            foreach (var raw in order)
            {
                int amount = checked(times * counts[raw]);
                List<OriginalJassFusionRegistration> found;
                bool one = origins.TryGetValue(raw, out found) && found.Count == 1;
                bool cycle = seen.Contains(raw);
                var maker = one && !cycle ? found[0] : null;
                int special = 0;
                bool hasChildren = false;
                if (maker != null)
                    foreach (var c in maker.OrderedConditions)
                    {
                        if (c.Kind == "UNIT") hasChildren = true;
                        else if (c.Kind != "GOLD" && c.Kind != "WOOD") special++;
                    }
                var name = names.Find(raw);
                lines.Add(new OriginalJassFusionDependencyStep(depth, raw,
                    name == null ? "원본 객체 미확인" : name.DisplayName,
                    amount, maker == null ? null : maker.AbilityRawcode,
                    special, maker != null && hasChildren && depth + 1 >= limit));
                if (maker == null || depth + 1 >= limit) continue;
                var next = new HashSet<string>(seen, StringComparer.Ordinal);
                next.Add(raw);
                Expand(maker, depth + 1, amount, limit, next, lines);
            }
        }
    }
}
