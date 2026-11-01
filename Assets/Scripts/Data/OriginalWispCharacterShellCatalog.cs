using System;
using System.Collections.Generic;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Data
{
    // Source-only identity shells for all automatic Wisp reward entries.
    // No invented damage, skills, names, or playable unit mappings.
    public sealed class OriginalWispCharacterShell
    {
        public string WispId { get; private set; }
        public string OriginalRawcode { get; private set; }
        public int BranchChancePercent { get; private set; }
        public string BonusAbilityRawcode { get; private set; }
        internal OriginalWispCharacterShell(string wisp, string rawcode, int chance, string bonus)
        {
            WispId = wisp; OriginalRawcode = rawcode;
            BranchChancePercent = chance; BonusAbilityRawcode = bonus;
        }
    }

    public sealed class OriginalWispCharacterShellCatalog
    {
        private readonly Dictionary<string, List<OriginalWispCharacterShell>> byWisp =
            new Dictionary<string, List<OriginalWispCharacterShell>>(StringComparer.Ordinal);

        public IReadOnlyList<OriginalWispCharacterShell> ForWisp(string id)
        {
            List<OriginalWispCharacterShell> list;
            return id != null && byWisp.TryGetValue(id, out list)
                ? list.AsReadOnly() : new OriginalWispCharacterShell[0];
        }

        public static OriginalWispCharacterShellCatalog FromSourceJson(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var root = OrdrJson.Object(OrdrJson.Parse(json), "original Wisp shell source");
            if (OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "sourceMapSha256") !=
                    OriginalWispRewardCatalog.ExpectedMapSha256 ||
                OrdrJson.RequiredString(root, "sourceJassSha256") !=
                    OriginalWispRewardCatalog.ExpectedJassSha256)
                throw new FormatException("원본 위습 소환 대상 버전 불일치");
            var catalog = new OriginalWispCharacterShellCatalog();
            foreach (object item in OrdrJson.Array(OrdrJson.Value(root, "wisps"), "wisps"))
            {
                var row = OrdrJson.Object(item, "wisp");
                string id = "wisp:" + OrdrJson.RequiredString(row, "wispRawcode");
                if (!WispResourceIds.IsAuto(id) || catalog.byWisp.ContainsKey(id))
                    throw new FormatException("위습 대상 종류 중복 또는 미확인");
                var shells = new List<OriginalWispCharacterShell>();
                int totalChance = 0;
                var branches = OrdrJson.Array(OrdrJson.Value(row, "branches"), "branches");
                foreach (object branchObject in branches)
                {
                    var branch = OrdrJson.Object(branchObject, "branch");
                    double chance = OrdrJson.Number(branch, "chancePercent") ?? -1;
                    if (chance <= 0 || chance > 100 || chance != Math.Floor(chance))
                        throw new FormatException("원본 위습 확률 오류");
                    totalChance += (int)chance;
                    string bonus = OrdrJson.String(branch, "bonusAbilityRawcode");
                    foreach (object entryObject in OrdrJson.Array(
                        OrdrJson.Value(branch, "entries"), "entries"))
                    {
                        var entry = OrdrJson.Object(entryObject, "entry");
                        string rawcode = OrdrJson.RequiredString(entry, "rawcode");
                        if (rawcode.Length != 4)
                            throw new FormatException("원본 위습 대상 코드 오류");
                        shells.Add(new OriginalWispCharacterShell(id, rawcode, (int)chance, bonus));
                    }
                }
                if (totalChance != 100) throw new FormatException("위습 분기 확률 합계 오류");
                catalog.byWisp.Add(id, shells);
            }
            if (catalog.byWisp.Count != 4)
                throw new FormatException("위습 4종 소환 대상이 모두 필요합니다.");
            return catalog;
        }
    }
}
