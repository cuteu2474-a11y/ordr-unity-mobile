using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // W3X summon/satellite objects are not part of the 277-character catalog.
    // Reuse the audited native aura definitions for independent attack actors,
    // never assigning these effects to their creator or the retail observer.
    public sealed class Original323MapObjectAuraCatalog : IOriginal323AuraCatalog
    {
        private static readonly IReadOnlyList<Original323SpecialAura> Empty =
            Array.AsReadOnly(new Original323SpecialAura[0]);
        private readonly Dictionary<string, IReadOnlyList<Original323SpecialAura>> byRawcode =
            new Dictionary<string, IReadOnlyList<Original323SpecialAura>>(StringComparer.Ordinal);

        public int ObjectCount => byRawcode.Count;
        public int DefinitionCount { get; private set; }

        public IReadOnlyList<Original323SpecialAura> FindAll(string rawcode)
        {
            IReadOnlyList<Original323SpecialAura> found;
            return rawcode != null && byRawcode.TryGetValue(rawcode, out found) ? found : Empty;
        }

        public static Original323MapObjectAuraCatalog FromJson(string json,
            OriginalMapBaseStatCatalog map)
        {
            if (map == null || map.SourceW3xSha256 !=
                Original323UncommonAbilityCatalog.SourceW3xSha256)
                throw new ArgumentException("원본 맵 객체 스탯이 필요합니다.");
            var root = OrdrJson.Object(OrdrJson.Parse(json), "map object auras");
            if (OrdrJson.RequiredString(root, "format") != "ordr-all-unit-aura-audit-v1" ||
                OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "sourceW3xSha256") != map.SourceW3xSha256 ||
                !Equals(OrdrJson.Value(root, "safeForUnconditionalLiveCombat"), false))
                throw new FormatException("소환 객체 오라 감사 지문/실전 승인 경계 불일치");

            var definitions = new Dictionary<string, Dictionary<string, object>>(
                StringComparer.Ordinal);
            foreach (object raw in OrdrJson.Array(OrdrJson.Value(root, "nativeAuraObjects"), "objects"))
            {
                var obj = OrdrJson.Object(raw, "native object");
                string code = OrdrJson.RequiredString(obj, "abilityRawcode");
                if (code.Length != 4 || definitions.ContainsKey(code))
                    throw new FormatException("소환 객체 네이티브 오라 코드 오류");
                definitions.Add(code, obj);
            }

            var catalog = new Original323MapObjectAuraCatalog();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (object raw in OrdrJson.Array(OrdrJson.Value(root, "mapObjectAuraOwners"), "map owners"))
            {
                var owner = OrdrJson.Object(raw, "map owner");
                string unit = OrdrJson.RequiredString(owner, "unitRawcode");
                if (unit.Length != 4 || !seen.Add(unit))
                    throw new FormatException("소환 객체 중복/잘못된 rawcode");
                var auras = new List<Original323SpecialAura>();
                var seenAbility = new HashSet<string>(StringComparer.Ordinal);
                foreach (object item in OrdrJson.Array(
                    OrdrJson.Value(owner, "abilityRawcodesInObjectOrder"), "object abilities"))
                {
                    string ability = item as string;
                    Dictionary<string, object> obj;
                    if (ability == null || !seenAbility.Add(ability) ||
                        !definitions.TryGetValue(ability, out obj))
                        throw new FormatException("소환 객체 오라 소유/정의 불일치: " + unit);
                    string parent = OrdrJson.RequiredString(obj, "parentAbility");
                    string source = OrdrJson.String(obj, "source");
                    if (OrdrJson.Value(obj, "w3aOverridesRequireMerge") != null ||
                        (parent != "AHad" && parent != "AOae" && parent != "ACac") ||
                        (parent == "ACac" ? source != "war3map.w3a" :
                            source != "Units\\AbilityData.slk")) continue;

                    foreach (object rankRaw in OrdrJson.Array(OrdrJson.Value(obj, "levels"), "ranks"))
                    {
                        var rank = OrdrJson.Object(rankRaw, "rank");
                        double? number = OrdrJson.Number(rank, "level");
                        // No guessed upgraded levels or native inheritance.
                        if (!number.HasValue || number.Value != 1) continue;
                        var fields = OrdrJson.Object(OrdrJson.Value(rank, "fields"), "fields");
                        Original323SpecialAura result = ReadFirstLevel(unit, ability, parent, fields);
                        if (result == null) continue;
                        auras.Add(result);
                        catalog.DefinitionCount++;
                    }
                }
                if (auras.Count > 0) catalog.byRawcode.Add(unit, auras.AsReadOnly());
            }
            return catalog;
        }

        private static Original323SpecialAura ReadFirstLevel(string unit, string ability,
            string parent, Dictionary<string, object> f)
        {
            if (parent == "AHad")
            {
                double? amount = OrdrJson.Number(f, "DataA");
                double? mode = OrdrJson.Number(f, "DataB");
                double? area = OrdrJson.Number(f, "Area");
                string mask = OrdrJson.String(f, "targs");
                string buff = OrdrJson.String(f, "BuffID");
                if (!amount.HasValue || amount.Value >= 0 ||
                    !area.HasValue || area.Value <= 0 || buff == null || buff.Length != 4 ||
                    mask != "air,enemies,ground" ||
                    (mode.HasValue && mode.Value != 0) ||
                    OrdrJson.Number(f, "Dur") != 0 || OrdrJson.Number(f, "HeroDur") != 0)
                    return null;
                return new Original323SpecialAura(unit, unit, ability, buff, parent,
                    CombatEffectType.ArmorReduction, -amount.Value, amount.Value,
                    area.Value, mask, 1, mode);
            }
            if (parent == "AOae")
            {
                double? move = OrdrJson.Number(f, "DataA");
                double? attack = OrdrJson.Number(f, "DataB");
                double? area = OrdrJson.Number(f, "Area");
                string mask = OrdrJson.String(f, "targs");
                string buff = OrdrJson.String(f, "BuffID");
                if (!move.HasValue || !attack.HasValue || !area.HasValue ||
                    area.Value <= 0 || buff == null || buff.Length != 4 ||
                    OrdrJson.Number(f, "Dur") != 0 || OrdrJson.Number(f, "HeroDur") != 0 ||
                    OrdrJson.Number(f, "Rng") != 0 ||
                    OrdrJson.Value(f, "DataC") != null || OrdrJson.Value(f, "DataD") != null)
                    return null;
                bool slow = move.Value < 0 && move.Value >= -1 && attack.Value == 0 &&
                    mask == "air,enemies,ground";
                bool haste = move.Value == 0 && attack.Value > 0 && attack.Value <= 1 &&
                    (mask == "air,invulnerable,self,ground,vulnerable,friend" ||
                     mask == "air,invulnerable,notself,ground,vulnerable,friend" ||
                     mask == "invulnerable,self");
                if (!slow && !haste) return null;
                return new Original323SpecialAura(unit, unit, ability, buff, parent,
                    slow ? CombatEffectType.MovementSlowPercent : CombatEffectType.AttackSpeedBuff,
                    Math.Abs(slow ? move.Value : attack.Value) * 100,
                    slow ? move.Value : attack.Value, area.Value, mask);
            }
            // ACac W3A is admitted only when every non-power native data
            // field is explicitly known to be inert, as for catalog units.
            double? percent = OrdrJson.Number(f, "Cac1");
            double? radius = OrdrJson.Number(f, "aare");
            string targets = OrdrJson.String(f, "atar");
            string buffId = OrdrJson.String(f, "abuf");
            if (!percent.HasValue || percent.Value <= 0 || percent.Value > 1 ||
                !radius.HasValue || radius.Value <= 0 || radius.Value > 2000 ||
                buffId == null || buffId.Length != 4 ||
                (targets != "air,invulnerable,organic,self,ground,vulnerable,friend" &&
                 targets != "air,invulnerable,organic,notself,ground,vulnerable,friend") ||
                OrdrJson.Number(f, "adur") != 0 || OrdrJson.Number(f, "ahdu") != 0 ||
                OrdrJson.Number(f, "Ear1") != null || OrdrJson.Number(f, "Ear2") != 1 ||
                OrdrJson.Number(f, "Ear3") != 1 || OrdrJson.Number(f, "Ear4") != 0 ||
                OrdrJson.Number(f, "Had1") != null || OrdrJson.Number(f, "Had2") != null ||
                OrdrJson.Number(f, "Oae1") != null || OrdrJson.Number(f, "Oae2") != null)
                return null;
            return new Original323SpecialAura(unit, unit, ability, buffId, parent,
                CombatEffectType.AttackPowerBuff,
                Math.Round(percent.Value * 100, 4, MidpointRounding.AwayFromZero),
                percent.Value, radius.Value, targets);
        }
    }
}
