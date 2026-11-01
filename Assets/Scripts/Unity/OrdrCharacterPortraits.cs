using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ordr.Mobile.Unity
{
    // Optional Resources assets are supplied ONLY by the private APK build.
    // The public repository and public CI intentionally contain no portrait PNGs.
    public static class OrdrCharacterPortraits
    {
        private const string ResourcesPrefix = "CharacterPortraits/";
        private static readonly Dictionary<string, Sprite> Cache =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        public static Sprite Find(string catalogUnitId)
        {
            if (string.IsNullOrEmpty(catalogUnitId)) return null;
            Sprite portrait;
            if (Cache.TryGetValue(catalogUnitId, out portrait)) return portrait;
            // Catalog IDs come from the trusted unit registry, not file paths or URLs.
            if (!IsSafeCatalogId(catalogUnitId)) return null;
            portrait = Resources.Load<Sprite>(ResourcesPrefix + catalogUnitId);
            Cache.Add(catalogUnitId, portrait);
            return portrait;
        }

        private static bool IsSafeCatalogId(string id)
        {
            if (id.Length < 4 || id.Length > 8) return false;
            for (int i = 0; i < id.Length; i++)
            {
                char value = id[i];
                if (!((value >= 'A' && value <= 'Z') ||
                      (value >= '0' && value <= '9') || value == '-'))
                    return false;
            }
            return true;
        }
    }
}
