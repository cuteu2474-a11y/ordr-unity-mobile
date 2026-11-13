using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;
using UnityEngine;

namespace Ordr.Mobile.Unity
{
    // Read-only display groups. Identical names at different grades/forms are
    // never merged: only the catalog ID identifies a group.
    public sealed class MobileOwnedUnitGroup
    {
        public CatalogUnit Unit { get; private set; }
        public IReadOnlyList<long> InstanceIds { get; private set; }
        internal MobileOwnedUnitGroup(CatalogUnit unit, List<long> ids)
        {
            Unit = unit;
            ids.Sort();
            InstanceIds = ids.AsReadOnly();
        }
        public bool Contains(long? id)
        {
            if (!id.HasValue) return false;
            foreach (long candidate in InstanceIds) if (candidate == id.Value) return true;
            return false;
        }
        public long NextSelection(long? selected)
        {
            for (int i = 0; i < InstanceIds.Count; i++)
                if (InstanceIds[i] == selected)
                    return InstanceIds[(i + 1) % InstanceIds.Count];
            return InstanceIds[0];
        }
    }

    public static class MobileOwnedUnitFormation
    {
        public static IReadOnlyList<MobileOwnedUnitGroup> Group(
            IReadOnlyList<MobileUnitSnapshot> owned, Func<long, bool> include)
        {
            if (owned == null) throw new ArgumentNullException(nameof(owned));
            if (include == null) throw new ArgumentNullException(nameof(include));
            var members = new Dictionary<string, List<long>>(StringComparer.Ordinal);
            var units = new Dictionary<string, CatalogUnit>(StringComparer.Ordinal);
            foreach (var entry in owned)
            {
                if (!include(entry.InstanceId)) continue;
                List<long> ids;
                if (!members.TryGetValue(entry.Unit.Id, out ids))
                {
                    ids = new List<long>();
                    members.Add(entry.Unit.Id, ids);
                    units.Add(entry.Unit.Id, entry.Unit);
                }
                ids.Add(entry.InstanceId);
            }
            var keys = new List<string>(members.Keys);
            keys.Sort(StringComparer.Ordinal);
            var result = new List<MobileOwnedUnitGroup>();
            foreach (string key in keys) result.Add(new MobileOwnedUnitGroup(units[key], members[key]));
            return result.AsReadOnly();
        }
    }

    // Screen-space formation only. No UnitState, placement, targeting, AoE
    // radius or combat coordinates are written by this layout.
    public sealed class MobileOwnedFormationLayout
    {
        public Rect[] Cells { get; private set; }
        public Rect Footer { get; private set; }
        public int Capacity { get; private set; }
        public int Page { get; private set; }
        public int Pages { get; private set; }
        public int Start { get { return Page * Capacity; } }

        // Portraits occupy the upper half, not the name/count strip.
        // All geometry is screen-space and shared by the SpriteRenderer and
        // IMGUI; it never changes combat coordinates or hitboxes.
        public static float PortraitDiameterPixels(Rect cell, float scale)
        {
            if (cell.width <= 0f || cell.height <= 0f || scale <= 0f)
                throw new ArgumentOutOfRangeException(nameof(cell));
            return Mathf.Min(94f * scale, cell.height * .52f, cell.width * .70f);
        }

        public static Rect PortraitRect(Rect cell, float scale)
        {
            float diameter = PortraitDiameterPixels(cell, scale);
            return new Rect(cell.center.x - diameter * .5f,
                cell.y + Mathf.Max(3f * scale, cell.height * .035f),
                diameter, diameter);
        }

        public static Rect NameRect(Rect cell, float scale)
        {
            if (cell.width <= 0f || cell.height <= 0f || scale <= 0f)
                throw new ArgumentOutOfRangeException(nameof(cell));
            float inset = Mathf.Min(4f * scale, cell.width * .04f);
            float y = cell.y + cell.height * .59f;
            return new Rect(cell.x + inset, y,
                cell.width - inset * 2f,
                Mathf.Max(1f, cell.yMax - y - 2f * scale));
        }

        public static Rect QuantityRect(Rect cell, float scale)
        {
            if (cell.width <= 0f || cell.height <= 0f || scale <= 0f)
                throw new ArgumentOutOfRangeException(nameof(cell));
            float width = Mathf.Min(52f * scale, cell.width * .46f);
            float height = Mathf.Min(30f * scale, cell.height * .28f);
            return new Rect(cell.xMax - width - 2f * scale,
                cell.y + 2f * scale, width, height);
        }

        public static MobileOwnedFormationLayout Calculate(Rect battle, float scale,
            int groupCount, int requestedPage)
        {
            if (battle.width <= 0 || battle.height <= 0 || scale <= 0 ||
                float.IsNaN(scale) || float.IsInfinity(scale) || groupCount < 0)
                throw new ArgumentOutOfRangeException(nameof(battle));
            float inset = 6f * scale, gap = 4f * scale;
            var content = new Rect(battle.x + inset, battle.y + 37f * scale,
                battle.width - inset * 2f, battle.height - 89f * scale);
            if (content.width <= 0 || content.height <= 0)
                throw new ArgumentOutOfRangeException(nameof(battle));
            // Balanced slots: smaller portraits, larger names and prominent
            // quantity badges. Paging continues to cover every owned group.
            const float preferredCellSize = 132f;
            int columns = Mathf.Clamp(Mathf.FloorToInt(
                (content.width + gap) / (preferredCellSize * scale + gap)), 1, 3);
            int maxRows = Mathf.Clamp(Mathf.FloorToInt(
                (content.height + gap) / (preferredCellSize * scale + gap)), 1, 2);
            int capacity = columns * maxRows;
            int pages = groupCount == 0 ? 1 : (groupCount - 1) / capacity + 1;
            int page = Mathf.Clamp(requestedPage, 0, pages - 1);
            int count = Math.Min(capacity, groupCount - page * capacity);
            int rows = count == 0 ? 0 : (count - 1) / columns + 1;
            float side = Mathf.Min(preferredCellSize * scale,
                (content.width - gap * (columns - 1)) / columns,
                (content.height - gap * Math.Max(0, rows - 1)) / Math.Max(1, rows));
            float width = side, height = side;
            var cells = new Rect[count];
            float top = content.center.y - (rows * height + Math.Max(0, rows - 1) * gap) * .5f;
            for (int i = 0; i < count; i++)
            {
                int row = i / columns, column = i % columns;
                int rowCount = Math.Min(columns, count - row * columns);
                float left = content.center.x - (rowCount * width + (rowCount - 1) * gap) * .5f;
                cells[i] = new Rect(left + column * (width + gap), top + row * (height + gap), width, height);
            }
            return new MobileOwnedFormationLayout
            {
                Cells = cells, Capacity = capacity, Page = page, Pages = pages,
                Footer = new Rect(battle.x + inset, battle.yMax - 43f * scale,
                    battle.width - inset * 2f, 40f * scale)
            };
        }
    }
}
