"""Link map objects using original JASS results and their catalog labels.

Resolve ambiguous same-name objects by a unique fusion result. Also recover
objects with different display titles when the original BjB label points to
the same rawcode/ability as the original CWR fusion registry, or when an
explicitly reviewed name alias matches a unique object and exact map grade.
None of these identity links proves skill behavior or final combat damage.
"""
import argparse
from collections import Counter, defaultdict
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

EXPECTED_MAP_GRADE = {
    "히든": "히든",
    "초월함": "초월",
    "영원한": "영원",
    "불멸의": "불멸",
}

# Reviewed display-name aliases within the SAME extracted W3X. Each row pins
# both rawcode and literal CampaignUnitStrings name, including its grade.
# These are object-identity aliases, NOT JASS skill/attack verification. In
# particular "니카(스네이크)" is intentionally omitted: no supported object
# identity was found in this source map.
REVIEWED_MAP_NAME_ALIASES = {
    "U-01": ("h00O", "니코 로빈 - 안흔함"),
    "U-05": ("h00K", "포트거스.D.에이스 - 안흔함"),
    "U-07": ("h00N", "우솝 저격왕 - 안흔함"),
    "U-08": ("h00G", "쵸파 럼블볼강화 - 안흔함"),
    "S-02": ("h01C", "카포네 갱 벳지 - 특별"),
    "S-06": ("h00R", "로브 루치 - 특별"),
    "S-09": ("h00B", "겟코 모리아 - 특별"),
    "S-19": ("h01J", "포트거스.D.에이스 - 특별"),
    "S-28": ("h017", "바솔로뮤 쿠마 - 특별"),
    "S-30": ("h00Y", "유스타스 키드 - 특별"),
    "R-02": ("h01Q", "몽키.D.거프 - 희귀"),
    "R-04": ("h01L", "돈키호테 도플라밍고 - 희귀"),
    "R-05": ("h02L", "트라팔가 로우 - 희귀"),
    "R-06": ("h01X", "루피 기어서드 - 희귀"),
    "R-11": ("h04H", "쥬라클 미호크 - 희귀"),
    "R-12": ("h01W", "바르토로메오 - 희귀"),
    "R-14": ("h01U", "바질 호킨스 - 희귀"),
    "R-19": ("h01O", "네펠타리 비비 - 희귀"),
    "R-28": ("h020", "마인 오즈 - 희귀"),
    "R-31": ("h02A", "엠포리오 이완코브 - 희귀"),
    "R-34": ("h02E", "다이아몬드 죠즈 - 희귀"),
    "R-37": ("h02D", "유스타스 키드 - 희귀"),
    "R-41": ("h05K", "페로나 - 희귀"),
    "R-42": ("h01M", "보아 핸콕 - 희귀"),
    "L-02": ("h02P", "나미 도둑고양이 - 전설"),
    "L-04": ("h02W", "몽키.D.드래곤 - 전설"),
    "L-06": ("h033", "빈스모크 레이쥬 - 전설"),
    "L-07": ("h03A", "실버즈 레일리 - 전설"),
    "L-09": ("h02R", "로브 루치 - 전설"),
    "L-12": ("h02N", "겟코 모리아 - 전설"),
    "L-13": ("h02Z", "바르토로메오 - 전설"),
    "L-14": ("h08S", "샬롯 브륄레 - 전설"),
    "L-16": ("h034", "상디 슈트코스 - 전설"),
    "L-22": ("h038", "시저 크라운 - 전설"),
    "L-23": ("h039", "금사자 시키 - 전설"),
    "L-24": ("h02O", "포트거스.D.에이스 - 전설"),
    "L-27": ("h02S", "조로 죽음의경지 - 전설"),
    "L-32": ("h030", "바솔로뮤 쿠마 - 전설"),
    "L-33": ("h03H", "샬롯 크래커 - 전설"),
    "L-35": ("h087", "아마츠키 토키 - 전설"),
    "L-36": ("h032", "보아 핸콕 - 전설"),
    "L-37": ("h031", "후지토라 잇쇼우 - 전설"),
    "T-01": ("h05S", "돈키호테 도플라밍고 - 변화"),
    "T-02": ("h05W", "네펠타리 비비 - 변화"),
    "T-05": ("h07J", "캐럿 스론 - 변화"),
    "LI-01": ("h05G", "패트릭 레드필드 - 제한"),
    "LI-07": ("h05E", "갓 에넬 - 제한"),
    "LI-08": ("h07I", "샬롯 카타쿠리 - 제한"),
    "RN-02": ("h06V", "우즈마키 나루토 - 랜덤전용"),
    "RN-11": ("h06Y", "카미조 토우마 - 랜덤전용"),
    "RN-12": ("h071", "쿠로사키 이치고 - 랜덤전용"),
    "MY-02": ("h0BF", "카마도 네즈코 - 신비"),
    "MY-04": ("h09W", "나미카제 미나토 - 신비"),
    "MY-05": ("h08I", "쿠치키 뱌쿠야 - 신비"),
}

REVIEWED_MAP_GRADE = {
    "안흔함": "안흔함", "특별함": "특별", "희귀함": "희귀",
    "전설": "전설", "변화": "변화", "제한됨": "제한",
    "랜덤": "랜덤전용", "신비함": "신비",
}


def reconcile(stats, registry, labels):
    if stats["gameVersion"] != registry["gameVersion"] or \
            stats["gameVersion"] != labels["gameVersion"] or \
            stats["sourceW3xSha256"] != labels["sourceMapSha256"] or \
            stats["sourceW3xSha256"] != registry["sourceSha256"]:
        raise ValueError("JASS and map identities do not share a source W3X/version")
    results = defaultdict(set)
    for reg in registry["registrations"]:
        for hit in reg["results"]:
            if hit["kind"] == "UNIT" and hit["count"] == 1:
                results[hit["rawcode"]].add(reg["abilityRawcode"])
    label_index = {}
    label_rawcodes = {}
    for item in labels["mappings"]:
        if item["catalogUnitId"] in label_index:
            raise ValueError("Duplicate catalog label: " + item["catalogUnitId"])
        if item["rawcode"] in label_rawcodes:
            raise ValueError("Duplicate label rawcode: " + item["rawcode"])
        label_index[item["catalogUnitId"]] = item
        label_rawcodes[item["rawcode"]] = item["catalogUnitId"]
    objects = {item["rawcode"]: item for item in stats["mapObjects"]}
    used = {row["originalRawcode"] for row in stats["catalogEntries"]
            if row["originalRawcode"]}

    # JASS ET04 produces h0CJ. The literal-name match h0C7 is a non-attacking
    # Aloc helper, not the unit obtained from the original fusion.
    # This override is pinned to both source objects and exact JASS evidence.
    ryuma = next((r for r in stats["catalogEntries"] if r["catalogId"] == "E-03"), None)
    helper, fighter = objects.get("h0C7"), objects.get("h0CJ")
    label = label_index.get("E-03")
    if (ryuma is None or helper is None or fighter is None or label is None or
            label["rawcode"] != "h0CJ" or label["abilityRawcode"] != "ET04" or
            "ET04" not in results.get("h0CJ", set()) or
            helper["mapName"] != "류마 - 영원" or
            helper["attack1"] is not None or
            "Aloc" not in helper["originalAbilityList"].split(",") or
            fighter["mapName"] != "시모츠키 류마 - 영원" or
            fighter["mapGradeSuffix"] != "영원" or fighter["attack1"] is None):
        raise ValueError("E-03 original helper/fusion-result evidence changed")
    if ryuma["originalRawcode"] == "h0C7":
        if ryuma["mappingStatus"] != "unique-map-name-and-grade" or "h0CJ" in used:
            raise ValueError("E-03 object result is not uniquely resolvable")
        used.remove("h0C7")
        ryuma.update({
            "mappingStatus": "verified-jass-fusion-result",
            "originalRawcode": "h0CJ",
            "candidateRawcodes": [],
            "alternateRawcodes": ["h0C7"],
            "sourceJassFusionAbilityRawcodes": ["ET04"],
            "sourceMapName": fighter["mapName"],
            "attack1": fighter["attack1"],
            "attack2": fighter["attack2"],
            "baseStats": fighter["baseStats"],
            "originalAbilityList": fighter["originalAbilityList"],
        })
        used.add("h0CJ")
    elif (ryuma["originalRawcode"] != "h0CJ" or
          ryuma["mappingStatus"] != "verified-jass-fusion-result" or
          ryuma.get("alternateRawcodes") != ["h0C7"] or
          ryuma.get("sourceJassFusionAbilityRawcodes") != ["ET04"] or
          ryuma["sourceMapName"] != fighter["mapName"] or
          ryuma["attack1"] != fighter["attack1"] or
          ryuma["baseStats"] != fighter["baseStats"]):
        raise ValueError("E-03 previously reconciled mapping drifted")

    for row in stats["catalogEntries"]:
        if row["mappingStatus"] != "ambiguous-name-and-grade":
            continue
        candidates = row["candidateRawcodes"]
        matches = [code for code in candidates if results.get(code)]
        if len(matches) != 1:
            continue  # Never guess by damage, similarity, or ordering.
        code = matches[0]
        label = label_index.get(row["catalogId"])
        if label and (label["rawcode"] != code or
                      label["abilityRawcode"] not in results[code]):
            raise ValueError("JASS fusion/label conflict for " + row["catalogId"])
        if code in used:
            raise ValueError("Map rawcode already linked: " + code)
        original = objects[code]
        row.update({
            "mappingStatus": "verified-jass-fusion-result",
            "originalRawcode": code,
            "candidateRawcodes": [],
            "alternateRawcodes": [candidate for candidate in candidates if candidate != code],
            "sourceJassFusionAbilityRawcodes": sorted(results[code]),
            "sourceMapName": original["mapName"],
            "attack1": original["attack1"],
            "attack2": original["attack2"],
            "baseStats": original["baseStats"],
            "originalAbilityList": original["originalAbilityList"],
        })
        used.add(code)

    for row in stats["catalogEntries"]:
        if row["mappingStatus"] != "unmatched":
            continue
        label = label_index.get(row["catalogId"])
        if not label:
            continue  # The character name alone cannot establish an object ID.
        code = label["rawcode"]
        original = objects.get(code)
        # A BjB label alone is not sufficient; the precise CWR ability must
        # also create this one unit rawcode in the same source W3X.
        if original is None or label["abilityRawcode"] not in results.get(code, set()):
            continue
        if code in used:
            raise ValueError("Map rawcode already linked: " + code)
        suffix = original["mapGradeSuffix"]
        expected = EXPECTED_MAP_GRADE.get(row["grade"])
        if expected is None or (suffix is not None and suffix != expected):
            raise ValueError("JASS label conflicts with map grade: " + row["catalogId"])
        row.update({
            "mappingStatus": "verified-jass-label-and-fusion-result",
            "originalRawcode": code,
            "candidateRawcodes": [],
            "sourceJassFusionAbilityRawcodes": [label["abilityRawcode"]],
            "sourceMapName": original["mapName"],
            "attack1": original["attack1"],
            "attack2": original["attack2"],
            "baseStats": original["baseStats"],
            "originalAbilityList": original["originalAbilityList"],
        })
        used.add(code)

    by_catalog_id = {row["catalogId"]: row for row in stats["catalogEntries"]}
    for catalog_id, (code, exact_map_name) in REVIEWED_MAP_NAME_ALIASES.items():
        row = by_catalog_id.get(catalog_id)
        if row is None:
            raise ValueError("Reviewed map alias is not in the catalog: " + catalog_id)
        original = objects.get(code)
        if original is None or original["mapName"] != exact_map_name:
            raise ValueError("Reviewed alias does not match original source: " + catalog_id)
        if original["mapGradeSuffix"] != REVIEWED_MAP_GRADE.get(row["grade"]):
            raise ValueError("Reviewed alias grade conflicts with catalog: " + catalog_id)
        if row["mappingStatus"] == "reviewed-map-name-alias-and-grade":
            if row["originalRawcode"] != code or row["sourceMapName"] != exact_map_name:
                raise ValueError("Previously reviewed alias has drifted: " + catalog_id)
            continue
        if row["mappingStatus"] != "unmatched":
            raise ValueError("Reviewed alias conflicts with existing mapping: " + catalog_id)
        if code in used:
            raise ValueError("Reviewed alias rawcode already used: " + code)
        row.update({
            "mappingStatus": "reviewed-map-name-alias-and-grade",
            "originalRawcode": code,
            "candidateRawcodes": [],
            "sourceMapName": original["mapName"],
            "attack1": original["attack1"],
            "attack2": original["attack2"],
            "baseStats": original["baseStats"],
            "originalAbilityList": original["originalAbilityList"],
        })
        used.add(code)
    summary = stats["summary"]
    counts = Counter(row["mappingStatus"] for row in stats["catalogEntries"])
    summary["mappingStatusCounts"] = dict(sorted(counts.items()))
    summary["linkedCatalogCharacters"] = sum(bool(row["originalRawcode"])
                                             for row in stats["catalogEntries"])
    summary["unresolvedCatalogCharacters"] = sum(not row["originalRawcode"]
                                               for row in stats["catalogEntries"])
    summary["linkedWithAttack1"] = sum(row["attack1"] is not None
                                       for row in stats["catalogEntries"])
    return stats


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stats", type=Path,
                        default=ROOT / "data/original-2323C-map-base-stats.json")
    parser.add_argument("--registry", type=Path,
                        default=ROOT / "data/original-jass-fusion-registry.json")
    parser.add_argument("--labels", type=Path,
                        default=ROOT / "data/original-unit-rawcode-label-links.json")
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    read = lambda p: json.loads(p.read_text(encoding="utf-8"))
    updated = reconcile(read(args.stats), read(args.registry), read(args.labels))
    content = json.dumps(updated, ensure_ascii=False, indent=2) + "\n"
    if args.verify:
        if args.stats.read_text(encoding="utf-8") != content:
            raise ValueError("JASS fusion source object links are stale")
        print("PASS: linked JASS fusion object identities")
    else:
        args.stats.write_text(content, encoding="utf-8")
        print("Linked", updated["summary"]["linkedCatalogCharacters"])


if __name__ == "__main__":
    main()
