"""Join same-name map objects only when JASS registered one fusion result.

Preserves every alternate rawcode; never verifies form behavior or damage.
"""
import argparse
from collections import Counter, defaultdict
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def reconcile(stats, registry, labels):
    if stats["gameVersion"] != registry["gameVersion"] or \
            stats["gameVersion"] != labels["gameVersion"] or \
            stats["sourceW3xSha256"] != labels["sourceMapSha256"]:
        raise ValueError("JASS and map identities do not share a source W3X/version")
    results = defaultdict(set)
    for reg in registry["registrations"]:
        for hit in reg["results"]:
            if hit["kind"] == "UNIT" and hit["count"] == 1:
                results[hit["rawcode"]].add(reg["abilityRawcode"])
    label_index = {}
    for item in labels["mappings"]:
        if item["catalogUnitId"] in label_index:
            raise ValueError("Duplicate catalog label: " + item["catalogUnitId"])
        label_index[item["catalogUnitId"]] = item
    objects = {item["rawcode"]: item for item in stats["mapObjects"]}
    used = {row["originalRawcode"] for row in stats["catalogEntries"]
            if row["originalRawcode"]}
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
