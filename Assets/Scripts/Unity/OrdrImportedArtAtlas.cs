using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ordr.Mobile.Unity
{
    // Renders the five approved ImportedArt textures already bundled in
    // Assets/Resources. Grid slices are bounded UVs, NEVER a whole sprite
    // sheet displayed as one unit/building. No texture readback or new art.
    // This class changes presentation only; it cannot affect combat or loot.
    public static class OrdrImportedArtAtlas
    {
        public const string UnitSheetResource = "ImportedArt/units-sheet";
        public const string BuildingSheetResource = "ImportedArt/buildings-sheet";
        public const string PanelResource = "ImportedArt/ui-panel";

        private static Texture2D unitSheet;
        private static Texture2D buildingSheet;
        private static Texture2D panel;
        private static bool unitsLoaded, buildingsLoaded, panelLoaded;
        private static readonly Dictionary<int, Sprite> UnitCells =
            new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> BuildingCells =
            new Dictionary<int, Sprite>();

        public static Texture2D PanelBackground
        {
            get
            {
                if (!panelLoaded)
                {
                    panel = Resources.Load<Texture2D>(PanelResource);
                    panelLoaded = true;
                }
                return panel;
            }
        }

        // Only identities unambiguously depicted in the supplied source.
        // Other units retain their local, cached, online or generic portrait;
        // never relabel a random sheet character as an unrelated fighter.
        public static Sprite FindUnitPortrait(string catalogId)
        {
            int cell;
            switch (catalogId)
            {
                case "LI-06": cell = 0; break; // Alvida
                case "C-02": cell = 2; break; // Zoro
                case "X-01": cell = 3; break; // Morgan
                case "C-07": cell = 4; break; // Buggy
                case "S-27": cell = 5; break; // Captain Kuro
                case "U-12": cell = 8; break; // Hatchan
                case "S-16": cell = 9; break; // Arlong
                default: return null;
            }
            if (!unitsLoaded)
            {
                unitSheet = Resources.Load<Texture2D>(UnitSheetResource);
                unitsLoaded = true;
            }
            return GridSprite(unitSheet, 5, 2, cell, UnitCells, "ORDR sheet unit ");
        }

        // Four columns x four rows, 14 authored building drawings. The
        // sequence is a visual assignment to the 14 story stages, not a
        // claim that the drawing matches an original W3X building model.
        public static Sprite StoryBuilding(int stage)
        {
            if (stage < 1 || stage > 14) return null;
            if (!buildingsLoaded)
            {
                buildingSheet = Resources.Load<Texture2D>(BuildingSheetResource);
                buildingsLoaded = true;
            }
            return GridSprite(buildingSheet, 4, 4, stage - 1,
                BuildingCells, "ORDR story building ");
        }

        private static Sprite GridSprite(Texture2D sheet, int columns, int rows,
            int index, Dictionary<int, Sprite> cache, string name)
        {
            if (sheet == null || index < 0 || index >= columns * rows) return null;
            Sprite sprite;
            if (cache.TryGetValue(index, out sprite)) return sprite;
            int column = index % columns;
            int rowFromTop = index / columns;
            // Integer boundaries partition even odd-sized sheets exactly;
            // Sprite.Create reads the selected rectangle from the GPU texture,
            // without requiring TextureImporter.isReadable.
            int left = Mathf.RoundToInt(sheet.width * column / (float)columns);
            int right = Mathf.RoundToInt(sheet.width * (column + 1) / (float)columns);
            int bottom = Mathf.RoundToInt(sheet.height *
                (rows - rowFromTop - 1) / (float)rows);
            int top = Mathf.RoundToInt(sheet.height *
                (rows - rowFromTop) / (float)rows);
            if (right <= left || top <= bottom) return null;
            sprite = Sprite.Create(sheet,
                new Rect(left, bottom, right - left, top - bottom),
                new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = name + index;
            cache.Add(index, sprite);
            return sprite;
        }

        public static Rect TextureUv(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null)
                throw new ArgumentNullException(nameof(sprite));
            Rect r = sprite.rect;
            return new Rect(r.x / sprite.texture.width,
                r.y / sprite.texture.height,
                r.width / sprite.texture.width,
                r.height / sprite.texture.height);
        }
    }
}
