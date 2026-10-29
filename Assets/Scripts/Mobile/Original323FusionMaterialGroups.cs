using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // CWR material buckets are distinct from the unit's real object identity.
    // CHy registers H099 and H0B2 under H08T through BH0. H08T is NOT
    // another free actor, a third Luffy, or a result-object alias.
    public interface IOriginalJassFusionMaterialGroups
    {
        bool TryGetMaterialGroup(string groupRawcode, out IReadOnlyList<string> catalogIds);
    }

    public sealed class Original323FusionMaterialGroups : IOriginalJassFusionMaterialGroups
    {
        private readonly IReadOnlyList<string> luffy;
        public Original323FusionMaterialGroups(OriginalMapBaseStatCatalog original)
        {
            if (original == null || original.SourceW3xSha256 !=
                Ordr.Mobile.Core.OriginalCommonSaleRules.OriginalMapSha256 ||
                original.Find("TR-08")?.OriginalRawcode != "H099" ||
                original.Find("TR-14")?.OriginalRawcode != "H0B2")
                throw new ArgumentException("원본 루피 조합 재료 분류가 일치하지 않습니다.");
            luffy = Array.AsReadOnly(new[] { "TR-08", "TR-14" });
        }
        public bool TryGetMaterialGroup(string rawcode, out IReadOnlyList<string> catalogIds)
        {
            catalogIds = rawcode == "H08T" ? luffy : null;
            return catalogIds != null;
        }
    }
}
