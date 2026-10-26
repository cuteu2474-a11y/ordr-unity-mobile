using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Native ACac (Command Aura) as exported from original war3map.w3a.
    // Cac1 is raw bonus weapon-damage percentage, NOT post-armor final HP.
    // Object ownership alone never approves live application or stacking.
    public sealed class Original323NativeCommandAuraCatalog : IOriginal323AuraCatalog
    {
        private static readonly IReadOnlyList<Original323SpecialAura> Empty =
            Array.AsReadOnly(new Original323SpecialAura[0]);
        private readonly Dictionary<string, IReadOnlyList<Original323SpecialAura>> byUnit =
            new Dictionary<string, IReadOnlyList<Original323SpecialAura>>(StringComparer.Ordinal);
        private readonly List<string> exclusions = new List<string>();

        public int UnitCount { get { return byUnit.Count; } }
        public int NativeAttackPowerDefinitions { get; private set; }
        public IReadOnlyList<string> ExcludedDefinitions
        {
            get { return exclusions.AsReadOnly(); }
        }

        public IReadOnlyList<Original323SpecialAura> FindAll(string unitId)
        {
            IReadOnlyList<Original323SpecialAura> found;
            return unitId != null && byUnit.TryGetValue(unitId, out found) ?
                found : Empty;
        }

        private static string Code(Dictionary<string, object> row, string field)
        {
            string raw = OrdrJson.RequiredString(row, field);
            if (raw.Length != 4)
                throw new FormatException("원본 ACac 오라 ID 형식 불일치: " + field);
            return raw;
        }

        private static double Number(Dictionary<string, object> row, string key)
        {
            double? number = OrdrJson.Number(row, key);
            if (!number.HasValue ||
                double.IsNaN(number.Value) ||
                double.IsInfinity(number.Value))
                throw new FormatException("원본 ACac 오라 숫자가 확인되지 않습니다: " + key);
            return number.Value;
        }

        private static bool SupportedMask(string mask)
        {
            if (string.IsNullOrWhiteSpace(mask)) return false;
            var allowed = new HashSet<string>(StringComparer.Ordinal) {
                "air", "invulnerable", "organic", "self",
                "ground", "vulnerable", "friend", "notself"
            };
            var parts = mask.Split(',');
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string part in parts)
                if (!allowed.Contains(part) || !seen.Add(part))
                    return false;
            // A self-only native buff is legitimate. 'notself' by itself
            // is not an owner/ally selector and cannot be guessed as one.
            return seen.Contains("self") || seen.Contains("friend");
        }

        private static bool PlainPercentageCommandAura(
            Dictionary<string, object> fields, double bonus, double area,
            string mask, string buff)
        {
            return bonus > 0 && bonus <= 1 && area > 0 && area <= 2000 &&
                SupportedMask(mask) &&
                buff != null && buff.Length == 4 &&
                Number(fields, "adur") == 0 &&
                Number(fields, "ahdu") == 0 &&
                OrdrJson.Number(fields, "Ear1") == null &&
                Number(fields, "Ear2") == 1 &&
                Number(fields, "Ear3") == 1 &&
                Number(fields, "Ear4") == 0 &&
                OrdrJson.Number(fields, "Had1") == null &&
                OrdrJson.Number(fields, "Had2") == null &&
                OrdrJson.Number(fields, "Oae1") == null &&
                OrdrJson.Number(fields, "Oae2") == null;
        }

        public static Original323NativeCommandAuraCatalog FromJson(string json,
            OriginalMapBaseStatCatalog verifiedMap)
        {
            if (verifiedMap == null)
                throw new ArgumentNullException(nameof(verifiedMap));
            var root = OrdrJson.Object(OrdrJson.Parse(json), "original ACac audit");
            if (OrdrJson.RequiredString(root, "format") !=
                    "ordr-all-unit-aura-audit-v1" ||
                OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "sourceW3xSha256") !=
                    Original323UncommonAbilityCatalog.SourceW3xSha256 ||
                verifiedMap.SourceW3xSha256 !=
                    Original323UncommonAbilityCatalog.SourceW3xSha256 ||
                !Equals(OrdrJson.Value(root, "safeForUnconditionalLiveCombat"), false))
                throw new FormatException(
                    "원본 쵸파 공격력 오라의 맵 지문 또는 실행승인 경계 불일치");

            var objects = new Dictionary<string, Dictionary<string, object>>(
                StringComparer.Ordinal);
            foreach (object raw in OrdrJson.Array(
                OrdrJson.Value(root, "nativeAuraObjects"), "ACac native objects"))
            {
                var source = OrdrJson.Object(raw, "original object");
                string ability = Code(source, "abilityRawcode");
                if (objects.ContainsKey(ability))
                    throw new FormatException("원본 능력 객체가 중복됐습니다: " + ability);
                objects.Add(ability, source);
            }

            var foundUnits = new HashSet<string>(StringComparer.Ordinal);
            var result = new Original323NativeCommandAuraCatalog();
            foreach (object raw in OrdrJson.Array(
                OrdrJson.Value(root, "catalogUnits"), "ACac unit owners"))
            {
                var row = OrdrJson.Object(raw, "original owner");
                string unitId = OrdrJson.RequiredString(row, "catalogUnitId");
                if (!foundUnits.Add(unitId))
                    throw new FormatException("원본 캐릭터 소유 오라가 중복됐습니다");
                var owner = verifiedMap.Find(unitId);
                if (owner == null ||
                    owner.OriginalRawcode != OrdrJson.String(row, "unitRawcode"))
                    throw new FormatException("원본 캐릭터와 W3A 객체 연결 불일치: " + unitId);

                var definitions = new List<Original323SpecialAura>();
                var seenCodes = new HashSet<string>(StringComparer.Ordinal);
                var rawCodes = OrdrJson.Value(row, "objectAuraCodes");
                if (rawCodes == null)
                {
                    result.exclusions.Add(unitId + ":unknown-original-aura-objects");
                    continue;
                }
                foreach (object candidate in OrdrJson.Array(rawCodes,
                    "current owner aura codes"))
                {
                    string code = candidate as string;
                    if (code == null || !seenCodes.Add(code))
                        throw new FormatException("원본 소유 능력 목록에 중복 또는 누락");
                    Dictionary<string, object> obj;
                    if (!objects.TryGetValue(code, out obj))
                        throw new FormatException("원본 W3A 능력 객체 미발견: " + code);
                    if (OrdrJson.RequiredString(obj, "parentAbility") != "ACac")
                        continue;
                    bool ownedInW3x = false;
                    foreach (string original in owner.OriginalAbilityRawcodes)
                        if (original == code) ownedInW3x = true;
                    if (!ownedInW3x)
                        throw new FormatException(
                            "캐릭터가 보유하지 않은 공격력 오라: " + unitId + "/" + code);
                    if (OrdrJson.String(obj, "source") != "war3map.w3a" ||
                        OrdrJson.Value(obj, "w3aOverridesRequireMerge") != null)
                    {
                        result.exclusions.Add(unitId + "/" + code +
                            ":incomplete-w3a-inheritance");
                        continue;
                    }
                    var levels = new HashSet<int>();
                    foreach (object rawLevel in OrdrJson.Array(
                        OrdrJson.Value(obj, "levels"), "ACac ranks"))
                    {
                        var level = OrdrJson.Object(rawLevel, "source rank");
                        double nativeRank = Number(level, "level");
                        if (nativeRank < 1 || nativeRank > int.MaxValue ||
                            nativeRank != Math.Floor(nativeRank) ||
                            !levels.Add((int)nativeRank))
                            throw new FormatException("중복·비정상 원본 명령 오라 레벨");
                        var f = OrdrJson.Object(OrdrJson.Value(level, "fields"),
                            "original W3A ACac fields");
                        double rawPercent = Number(f, "Cac1");
                        double range = Number(f, "aare");
                        string mask = OrdrJson.String(f, "atar");
                        string buff = OrdrJson.String(f, "abuf");
                        if (!PlainPercentageCommandAura(f, rawPercent,
                            range, mask, buff))
                        {
                            // 10,000 / 20,000 flat-looking Cac1 fields,
                            // negative debuffs, 0-area dummy abilities and
                            // global/player effects are not percentages.
                            result.exclusions.Add(unitId + "/" + code + "/" +
                                ((int)nativeRank) + ":not-simple-native-percent-buff");
                            continue;
                        }
                        var aura = new Original323SpecialAura(unitId,
                            owner.OriginalRawcode, code, buff, "ACac",
                            CombatEffectType.AttackPowerBuff,
                            Math.Round(rawPercent * 100, 4,
                                MidpointRounding.AwayFromZero),
                            rawPercent, range, mask, (int)nativeRank);
                        definitions.Add(aura);
                        result.NativeAttackPowerDefinitions++;
                    }
                }
                if (definitions.Count > 0)
                    result.byUnit.Add(unitId, definitions.AsReadOnly());
            }
            if (foundUnits.Count != verifiedMap.Count)
                throw new FormatException("ACac 원본 전체 유닛 연결 수량 불일치");
            return result;
        }
    }
}
