using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // W3X AOae source-data candidates. The game only applies these with the
    // existing independently VERIFIED current-rank, native target-mask,
    // immunity and buff-at-current-tick witness.
    public sealed class Original323NativeSpeedAuraCatalog : IOriginal323AuraCatalog
    {
        private static readonly IReadOnlyList<Original323SpecialAura> Empty =
            Array.AsReadOnly(new Original323SpecialAura[0]);
        private readonly Dictionary<string, IReadOnlyList<Original323SpecialAura>> entries =
            new Dictionary<string, IReadOnlyList<Original323SpecialAura>>(StringComparer.Ordinal);
        private readonly List<string> excluded = new List<string>();

        public int UnitCount { get { return entries.Count; } }
        public int MovementSlowDefinitionCount { get; private set; }
        public int AllyAttackSpeedDefinitionCount { get; private set; }
        public IReadOnlyList<string> ExcludedDefinitions { get { return excluded.AsReadOnly(); } }

        public IReadOnlyList<Original323SpecialAura> FindAll(string unitId)
        {
            IReadOnlyList<Original323SpecialAura> found;
            return unitId != null && entries.TryGetValue(unitId, out found)
                ? found : Empty;
        }

        private static string Code(Dictionary<string, object> row, string field)
        {
            string code = OrdrJson.RequiredString(row, field);
            if (code.Length != 4)
                throw new FormatException("원본 AOae 능력 rawcode가 네 글자가 아닙니다: " + field);
            return code;
        }

        private static double RequiredFinite(Dictionary<string, object> row, string field)
        {
            double? value = OrdrJson.Number(row, field);
            if (!value.HasValue || double.IsInfinity(value.Value) ||
                double.IsNaN(value.Value))
                throw new FormatException("원본 AOae 수치 검증 실패: " + field);
            return value.Value;
        }

        public static Original323NativeSpeedAuraCatalog FromJson(string json,
            OriginalMapBaseStatCatalog map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            var root = OrdrJson.Object(OrdrJson.Parse(json), "native speed aura audit");
            if (OrdrJson.RequiredString(root, "format") !=
                    "ordr-all-unit-aura-audit-v1" ||
                OrdrJson.RequiredString(root, "gameVersion") != "S2 2.323[C]" ||
                OrdrJson.RequiredString(root, "sourceW3xSha256") !=
                    Original323UncommonAbilityCatalog.SourceW3xSha256 ||
                map.SourceW3xSha256 != Original323UncommonAbilityCatalog.SourceW3xSha256 ||
                !Equals(OrdrJson.Value(root, "safeForUnconditionalLiveCombat"), false))
                throw new FormatException("원본 속도 오라 출처·실전 승인 경계 불일치");

            var objects = new Dictionary<string, Dictionary<string, object>>(
                StringComparer.Ordinal);
            foreach (object item in OrdrJson.Array(
                OrdrJson.Value(root, "nativeAuraObjects"), "native aura objects"))
            {
                var row = OrdrJson.Object(item, "aura object");
                string ability = Code(row, "abilityRawcode");
                if (objects.ContainsKey(ability))
                    throw new FormatException("원본 AOae 객체 코드 중복");
                objects.Add(ability, row);
            }

            var result = new Original323NativeSpeedAuraCatalog();
            var owners = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in OrdrJson.Array(
                OrdrJson.Value(root, "catalogUnits"), "aura owners"))
            {
                var row = OrdrJson.Object(item, "aura owner");
                string unitId = OrdrJson.RequiredString(row, "catalogUnitId");
                if (!owners.Add(unitId))
                    throw new FormatException("원본 속도 오라 소유자 중복: " + unitId);
                var sourceUnit = map.Find(unitId);
                if (sourceUnit == null ||
                    sourceUnit.OriginalRawcode != OrdrJson.String(row, "unitRawcode"))
                    throw new FormatException("원본 속도 오라 소유 유닛 ID 불일치: " + unitId);

                var available = new List<Original323SpecialAura>();
                object rawAbilities = OrdrJson.Value(row, "objectAuraCodes");
                if (rawAbilities == null) continue;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (object raw in OrdrJson.Array(rawAbilities, "objectAuraCodes"))
                {
                    var rawcode = raw as string;
                    if (rawcode == null || !seen.Add(rawcode))
                        throw new FormatException("원본 속도 오라 객체 목록 중복");
                    Dictionary<string, object> obj;
                    if (!objects.TryGetValue(rawcode, out obj))
                        throw new FormatException("원본 속도 오라 객체가 존재하지 않습니다: " + rawcode);
                    if (OrdrJson.RequiredString(obj, "parentAbility") != "AOae")
                        continue;

                    bool genuinelyOwned = false;
                    foreach (string ownerAbility in sourceUnit.OriginalAbilityRawcodes)
                        if (ownerAbility == rawcode) genuinelyOwned = true;
                    if (!genuinelyOwned)
                        throw new FormatException("유닛 객체가 보유하지 않은 속도 오라: " +
                            unitId + "/" + rawcode);
                    // No guessing of inherited W3A levels or missing AOae fields.
                    if (OrdrJson.String(obj, "source") != "Units\\AbilityData.slk" ||
                        OrdrJson.Value(obj, "w3aOverridesRequireMerge") != null)
                    {
                        result.excluded.Add(unitId + "/" + rawcode +
                            ":unresolved-original-overlay");
                        continue;
                    }

                    var ranks = new HashSet<int>();
                    foreach (object levelItem in OrdrJson.Array(
                        OrdrJson.Value(obj, "levels"), "AOae levels"))
                    {
                        var level = OrdrJson.Object(levelItem, "AOae level");
                        double rankNumber = RequiredFinite(level, "level");
                        if (rankNumber < 1 || rankNumber > int.MaxValue ||
                            rankNumber != Math.Floor(rankNumber) ||
                            !ranks.Add((int)rankNumber))
                            throw new FormatException("원본 AOae 레벨 오류");
                        var fields = OrdrJson.Object(OrdrJson.Value(level, "fields"), "AOae fields");
                        double movement = RequiredFinite(fields, "DataA");
                        double attackSpeed = RequiredFinite(fields, "DataB");
                        double range = RequiredFinite(fields, "Area");
                        double lifetime = RequiredFinite(fields, "Dur");
                        double heroLifetime = RequiredFinite(fields, "HeroDur");
                        double castRange = RequiredFinite(fields, "Rng");
                        string mask = OrdrJson.String(fields, "targs");
                        string buff = OrdrJson.String(fields, "BuffID");

                        bool safeFields = range > 0 && lifetime == 0 && heroLifetime == 0 &&
                            castRange == 0 && OrdrJson.Value(fields, "DataC") == null &&
                            OrdrJson.Value(fields, "DataD") == null &&
                            buff != null && buff.Length == 4 &&
                            !string.IsNullOrWhiteSpace(mask);
                        bool enemySlow = safeFields && movement < 0 && movement >= -1 &&
                            attackSpeed == 0 && mask == "air,enemies,ground";
                        bool allySpeed = safeFields && movement == 0 &&
                            attackSpeed > 0 && attackSpeed <= 1 &&
                            mask.Contains("friend") && !mask.Contains("enemies");

                        if (!enemySlow && !allySpeed)
                        {
                            result.excluded.Add(unitId + "/" + rawcode + "/" +
                                (int)rankNumber + ":unsupported-native-fields-or-targets");
                            continue;
                        }

                        var type = enemySlow ? CombatEffectType.MovementSlowPercent :
                            CombatEffectType.AttackSpeedBuff;
                        double sourceValue = enemySlow ? movement : attackSpeed;
                        // Status only: actual movement/attack cadence still requires
                        // native verified timing; do not change attack cooldown here.
                        var aura = new Original323SpecialAura(unitId,
                            sourceUnit.OriginalRawcode, rawcode, buff, "AOae",
                            type, Math.Abs(sourceValue) * 100, sourceValue,
                            range, mask, (int)rankNumber);
                        available.Add(aura);
                        if (enemySlow) result.MovementSlowDefinitionCount++;
                        else result.AllyAttackSpeedDefinitionCount++;
                    }
                }
                if (available.Count != 0)
                    result.entries.Add(unitId, available.AsReadOnly());
            }
            if (owners.Count != map.Count)
                throw new FormatException("원본 AOae 오라 자료와 도감 전체 수가 다릅니다.");
            return result;
        }
    }

    // Composes original source definitions from the existing AHad armor module
    // and the AOae movement/attack speed module. Pinned six-skill rows that
    // overlap the full original audit must not double-register an aura.
    public sealed class Original323CompositeAuraCatalog : IOriginal323AuraCatalog
    {
        private static readonly IReadOnlyList<Original323SpecialAura> Empty =
            Array.AsReadOnly(new Original323SpecialAura[0]);
        private readonly Dictionary<string, IReadOnlyList<Original323SpecialAura>> entries =
            new Dictionary<string, IReadOnlyList<Original323SpecialAura>>(StringComparer.Ordinal);

        public int UnitCount { get { return entries.Count; } }
        public int DefinitionCount { get; private set; }

        public Original323CompositeAuraCatalog(OriginalMapBaseStatCatalog map,
            IOriginal323AuraCatalog originalArmor,
            IOriginal323AuraCatalog nativeSpeed)
        {
            if (map == null || originalArmor == null || nativeSpeed == null)
                throw new ArgumentNullException(nameof(map));
            foreach (var unit in map.All)
            {
                var collected = new List<Original323SpecialAura>();
                var seen = new Dictionary<string, Original323SpecialAura>(StringComparer.Ordinal);
                foreach (IOriginal323AuraCatalog catalog in
                    new[] { originalArmor, nativeSpeed })
                {
                    foreach (var aura in catalog.FindAll(unit.UnitId))
                    {
                        if (aura.UnitId != unit.UnitId ||
                            aura.UnitRawcode != unit.OriginalRawcode)
                            throw new FormatException("원본 오라 조합의 유닛/객체 ID가 다릅니다.");
                        string identity = aura.AbilityRawcode + "/" + aura.BuffRawcode +
                            "/" + aura.Level;
                        Original323SpecialAura before;
                        if (seen.TryGetValue(identity, out before))
                        {
                            if (before.Type != aura.Type ||
                                Math.Abs(before.Amount - aura.Amount) > 0.000001 ||
                                Math.Abs(before.Radius - aura.Radius) > 0.000001 ||
                                Math.Abs(before.SignedRawObjectValue -
                                    aura.SignedRawObjectValue) > 0.000001 ||
                                before.TargetMask != aura.TargetMask)
                                throw new FormatException(
                                    "원본 W3X의 동일 오라 효과가 서로 불일치합니다: " + identity);
                            continue;
                        }
                        seen.Add(identity, aura);
                        collected.Add(aura);
                        DefinitionCount++;
                    }
                }
                if (collected.Count > 0)
                    entries.Add(unit.UnitId, collected.AsReadOnly());
            }
        }

        public IReadOnlyList<Original323SpecialAura> FindAll(string unitId)
        {
            IReadOnlyList<Original323SpecialAura> found;
            return unitId != null && entries.TryGetValue(unitId, out found)
                ? found : Empty;
        }
    }
}
