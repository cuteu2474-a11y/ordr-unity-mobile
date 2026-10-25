using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Object definitions, NOT authorization to apply them. All ranks are
    // candidates; the target rules must establish the source's CURRENT rank,
    // native flat-armor interpretation and actual buff presence at this tick.
    public sealed class Original323ArmorAuraCatalog : IOriginal323AuraCatalog
    {
        private readonly Dictionary<string, IReadOnlyList<Original323SpecialAura>> entries =
            new Dictionary<string, IReadOnlyList<Original323SpecialAura>>(StringComparer.Ordinal);
        private readonly List<string> excluded = new List<string>();
        private static readonly IReadOnlyList<Original323SpecialAura> Empty =
            Array.AsReadOnly(new Original323SpecialAura[0]);

        public int UnitCount => entries.Count;
        public int ArmorDefinitionCount { get; private set; }
        public IReadOnlyList<string> ExcludedDefinitions => excluded.AsReadOnly();

        public IReadOnlyList<Original323SpecialAura> FindAll(string unitId)
        {
            IReadOnlyList<Original323SpecialAura> result;
            return unitId != null && entries.TryGetValue(unitId, out result) ? result : Empty;
        }

        private static double RequiredNumber(Dictionary<string, object> row, string key)
        {
            double? value = OrdrJson.Number(row, key);
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
                throw new FormatException("원본 오라 숫자 미확인: " + key);
            return value.Value;
        }

        private static string Code(Dictionary<string, object> row, string key)
        {
            string value = OrdrJson.RequiredString(row, key);
            if (value.Length != 4) throw new FormatException("원본 오라 rawcode 오류: " + key);
            return value;
        }

        public static Original323ArmorAuraCatalog FromJson(string json,
            OriginalMapBaseStatCatalog map, Original323SpecialAuraCatalog existingSix = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var root = OrdrJson.Object(OrdrJson.Parse(json), "aura audit");
            if (OrdrJson.RequiredString(root, "format") != "ordr-all-unit-aura-audit-v1" ||
                OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "sourceW3xSha256") !=
                    Original323UncommonAbilityCatalog.SourceW3xSha256 ||
                map.SourceW3xSha256 != Original323UncommonAbilityCatalog.SourceW3xSha256 ||
                !Equals(OrdrJson.Value(root, "safeForUnconditionalLiveCombat"), false))
                throw new FormatException("전 유닛 오라 원본 버전/검증 경계 불일치");

            var objects = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            foreach (object item in OrdrJson.Array(OrdrJson.Value(root, "nativeAuraObjects"), "nativeAuraObjects"))
            {
                var row = OrdrJson.Object(item, "aura object");
                string code = Code(row, "abilityRawcode");
                if (objects.ContainsKey(code)) throw new FormatException("원본 오라 객체 중복: " + code);
                objects.Add(code, row);
            }

            var result = new Original323ArmorAuraCatalog();
            var seenUnits = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in OrdrJson.Array(OrdrJson.Value(root, "catalogUnits"), "catalogUnits"))
            {
                var unit = OrdrJson.Object(item, "aura owner");
                string id = OrdrJson.RequiredString(unit, "catalogUnitId");
                if (!seenUnits.Add(id)) throw new FormatException("오라 소유 유닛 중복: " + id);
                var owner = map.Find(id);
                if (owner == null || owner.OriginalRawcode != OrdrJson.String(unit, "unitRawcode"))
                    throw new FormatException("오라 소유 원본 유닛 불일치: " + id);
                var definitions = new List<Original323SpecialAura>();
                object rawCodes = OrdrJson.Value(unit, "objectAuraCodes");
                if (rawCodes == null)
                    result.excluded.Add(id + ":unknown-object-ability-list");
                else
                {
                    var seenCodes = new HashSet<string>(StringComparer.Ordinal);
                    foreach (object raw in OrdrJson.Array(rawCodes, "objectAuraCodes"))
                    {
                        string code = raw as string;
                        if (code == null || !seenCodes.Add(code) || !objects.ContainsKey(code))
                            throw new FormatException("원본 오라 능력 연결 오류: " + id);
                        bool owned = false;
                        foreach (string ability in owner.OriginalAbilityRawcodes)
                            if (ability == code) owned = true;
                        if (!owned) throw new FormatException("원본 객체에 없는 오라: " + id + "/" + code);
                        var aura = objects[code];
                        if (OrdrJson.RequiredString(aura, "parentAbility") != "AHad") continue;
                        // Never flatten a W3A overlay, inherit missing fields,
                        // or reinterpret positive armor as enemy reduction.
                        if (OrdrJson.String(aura, "source") != "Units\\AbilityData.slk" ||
                            OrdrJson.Value(aura, "w3aOverridesRequireMerge") != null)
                        {
                            result.excluded.Add(id + "/" + code + ":unresolved-object-overlay");
                            continue;
                        }
                        var ranks = new HashSet<int>();
                        foreach (object levelItem in OrdrJson.Array(OrdrJson.Value(aura, "levels"), "levels"))
                        {
                            var rank = OrdrJson.Object(levelItem, "aura level");
                            double rankValue = RequiredNumber(rank, "level");
                            if (rankValue < 1 || rankValue > int.MaxValue || Math.Floor(rankValue) != rankValue ||
                                !ranks.Add((int)rankValue)) throw new FormatException("원본 오라 레벨 오류");
                            var fields = OrdrJson.Object(OrdrJson.Value(rank, "fields"), "aura fields");
                            double amount = RequiredNumber(fields, "DataA");
                            double radius = RequiredNumber(fields, "Area");
                            string mask = OrdrJson.String(fields, "targs");
                            string buff = OrdrJson.String(fields, "BuffID");
                            double? mode = OrdrJson.Number(fields, "DataB");
                            if (amount >= 0 || radius <= 0 || string.IsNullOrWhiteSpace(mask) ||
                                buff == null || buff.Length != 4 ||
                                RequiredNumber(fields, "Dur") != 0 || RequiredNumber(fields, "HeroDur") != 0 ||
                                (mode.HasValue && mode.Value != 0))
                            {
                                result.excluded.Add(id + "/" + code + "/" + (int)rankValue + ":unsupported-or-inactive-fields");
                                continue;
                            }
                            definitions.Add(new Original323SpecialAura(id, owner.OriginalRawcode,
                                code, buff, "AHad", CombatEffectType.ArmorReduction,
                                -amount, amount, radius, mask, (int)rankValue, mode));
                            result.ArmorDefinitionCount++;
                        }
                    }
                }
                // Preserve the previously connected movement/attack auras.
                // The two armor entries now use the same data-driven path.
                if (existingSix != null)
                    foreach (var aura in existingSix.FindAll(id))
                        if (aura.Type != CombatEffectType.ArmorReduction) definitions.Add(aura);
                if (definitions.Count != 0) result.entries.Add(id, definitions.AsReadOnly());
            }
            return result;
        }
    }
}
