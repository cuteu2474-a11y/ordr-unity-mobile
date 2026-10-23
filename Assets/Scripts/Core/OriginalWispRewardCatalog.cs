using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Core
{
    // Supplied by the game RNG. Tests may inject a fixed sequence; the test RNG
    // must never be mistaken for Warcraft's BFh/BFi random state.
    public interface IOriginalWispRandom
    {
        int NextInclusive(int minimum, int maximum);
    }

    public sealed class OriginalWispRewardCatalog : IVerifiedWispRoller
    {
        public const string ExpectedMapSha256 =
            "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c";
        public const string ExpectedJassSha256 =
            "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d";
        // Hash of the exact machine-extracted JSON after LF normalization.
        public const string ExpectedCatalogSha256 =
            "2783b95e34f28ceadf76be5aee0d6d13c532bc2e27daf1920cd4569637cf5cff";

        private sealed class Branch
        {
            public int ChancePercent;
            public string BonusAbility;
            public readonly List<string> Units = new List<string>();
        }

        private readonly Dictionary<string, List<Branch>> byWisp =
            new Dictionary<string, List<Branch>>(StringComparer.Ordinal);
        private readonly IOriginalWispRandom random;

        private OriginalWispRewardCatalog(IOriginalWispRandom source)
        {
            random = source;
        }

        public bool IsVerified(string id) => id != null && byWisp.ContainsKey(id);

        public WispReward Roll(string id)
        {
            List<Branch> branches;
            if (!byWisp.TryGetValue(id ?? "", out branches))
                throw new InvalidOperationException("원본에 없는 자동 위습입니다.");
            Branch chosen = branches.Count == 1 ? branches[0] : null;
            if (chosen == null)
            {
                // Only CSI (rare Wisp) calls BFi(1,100) before selecting
                // its pool. CSF/CSC/CR6 call PlaceRandomUnit directly.
                int roll = CheckedRandom(1, 100);
                foreach (var branch in branches)
                {
                    if (roll <= branch.ChancePercent) { chosen = branch; break; }
                    roll -= branch.ChancePercent;
                }
            }
            if (chosen == null) throw new InvalidOperationException("위습 확률표 오류");
            int index = CheckedRandom(0, chosen.Units.Count - 1);
            return new WispReward(id, chosen.Units[index], chosen.BonusAbility);
        }

        private int CheckedRandom(int minimum, int maximum)
        {
            int result = random.NextInclusive(minimum, maximum);
            if (result < minimum || result > maximum)
                throw new InvalidOperationException("원본 위습 난수 범위 위반");
            return result;
        }

        public static OriginalWispRewardCatalog FromOriginalJassJson(string json,
            IOriginalWispRandom originalRandom)
        {
            if (originalRandom == null) throw new ArgumentNullException(nameof(originalRandom));
            if (json == null) throw new ArgumentNullException(nameof(json));
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(json.Replace("\r\n", "\n")));
                var hex = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest) hex.Append(value.ToString("x2"));
                if (hex.ToString() != ExpectedCatalogSha256)
                    throw new FormatException("원본 JASS에서 추출한 위습 JSON 내용이 변경됐습니다.");
            }
            var doc = OrdrJson.Object(OrdrJson.Parse(json), "original Wisp pools");
            if (OrdrJson.RequiredString(doc, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(doc, "sourceMapSha256") != ExpectedMapSha256 ||
                OrdrJson.RequiredString(doc, "sourceJassSha256") != ExpectedJassSha256 ||
                OrdrJson.RequiredString(doc, "rollSelection") != "unit-pool-weights")
                throw new FormatException("위습 보상표가 검증된 원본 2.323[C]와 다릅니다.");

            var result = new OriginalWispRewardCatalog(originalRandom);
            foreach (object element in OrdrJson.Array(OrdrJson.Value(doc, "wisps"), "wisps"))
            {
                var row = OrdrJson.Object(element, "wisp");
                string rawcode = OrdrJson.RequiredString(row, "wispRawcode");
                string id = "wisp:" + rawcode;
                string initializer = OrdrJson.RequiredString(row, "initializerFunction");
                string consumer = OrdrJson.RequiredString(row, "consumerFunction");
                string expectedInitializer, expectedConsumer;
                int[] expectedCounts, expectedChances;
                switch (id)
                {
                    case WispResourceIds.Uncommon:
                        expectedInitializer = "CSE"; expectedConsumer = "CSF";
                        expectedCounts = new[] { 13 }; expectedChances = new[] { 100 }; break;
                    case WispResourceIds.Special:
                        expectedInitializer = "CSB"; expectedConsumer = "CSC";
                        expectedCounts = new[] { 33 }; expectedChances = new[] { 100 }; break;
                    case WispResourceIds.Rare:
                        expectedInitializer = "CSH"; expectedConsumer = "CSI";
                        expectedCounts = new[] { 42, 8 }; expectedChances = new[] { 99, 1 }; break;
                    case WispResourceIds.Legendary:
                        expectedInitializer = "CR5"; expectedConsumer = "CR6";
                        expectedCounts = new[] { 60 }; expectedChances = new[] { 100 }; break;
                    default: throw new FormatException("미확인 위습 ID " + id);
                }
                if (initializer != expectedInitializer || consumer != expectedConsumer ||
                    result.byWisp.ContainsKey(id))
                    throw new FormatException("원본 위습 함수 또는 ID 중복");
                var branches = OrdrJson.Array(OrdrJson.Value(row, "branches"), "branches");
                if (branches.Count != expectedCounts.Length)
                    throw new FormatException("원본 위습 분기 수 불일치");
                var pool = new List<Branch>();
                for (int i = 0; i < branches.Count; i++)
                {
                    var branch = OrdrJson.Object(branches[i], "branch");
                    int chance = RequiredInt(branch, "chancePercent");
                    string ability = OrdrJson.String(branch, "bonusAbilityRawcode");
                    if (chance != expectedChances[i] ||
                        ability != (id == WispResourceIds.Rare && i == 0 ? "A0YE" : null))
                        throw new FormatException("원본 희귀위습 확률 또는 추가 능력 오류");
                    var entries = OrdrJson.Array(OrdrJson.Value(branch, "entries"), "entries");
                    if (entries.Count != expectedCounts[i])
                        throw new FormatException("원본 위습 보상 풀 개수 오류");
                    var part = new Branch { ChancePercent = chance, BonusAbility = ability };
                    var unique = new HashSet<string>(StringComparer.Ordinal);
                    foreach (object item in entries)
                    {
                        var unit = OrdrJson.Object(item, "pool unit");
                        string rawUnit = OrdrJson.RequiredString(unit, "rawcode");
                        if (rawUnit.Length != 4 || RequiredInt(unit, "weight") != 1 ||
                            !unique.Add(rawUnit))
                            throw new FormatException("원본 위습 유닛 코드/가중치 오류");
                        part.Units.Add(rawUnit);
                    }
                    pool.Add(part);
                }
                result.byWisp.Add(id, pool);
            }
            if (result.byWisp.Count != 4)
                throw new FormatException("원본 자동 위습 4종 전체 목록이 필요합니다.");
            return result;
        }

        private static int RequiredInt(Dictionary<string, object> row, string name)
        {
            double number;
            if (!OrdrJson.TryNumber(row, name, out number) ||
                number < 0 || number > int.MaxValue || number != Math.Floor(number))
                throw new FormatException("원본 위습 정수 필드 오류: " + name);
            return (int)number;
        }
    }
}
