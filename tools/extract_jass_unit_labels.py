#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Extract original unit rawcode -> crafting abilities and names from BjB.

Reads original ORDR S2 2.323[C] war3map.j. The original map also uses unit
rawcodes without Bi9 labels; these are NOT assigned invented catalog IDs.

Usage:
  python tools/extract_jass_unit_labels.py --jass war3map.j \
      --output data/original-unit-rawcode-label-evidence.json

Optional reconciliation:
  --links data/original-unit-rawcode-label-links.json

This is source-data extraction only, not a Unity/gameplay test.
"""
import argparse
import hashlib
import json
import pathlib
import re
import sys

BI8 = re.compile(r'^call Bi8\(\$([0-9A-Fa-f]{8}),\$([0-9A-Fa-f]{8})\)$')
BI9 = re.compile(r'^call Bi9\(\$([0-9A-Fa-f]{8}),"([^"]*)"\)$')


def rawcode(hextext):
    return bytes.fromhex(hextext).decode("latin-1")


def extract(script):
    data = script.read_bytes()
    lines = data.decode("utf-8-sig").splitlines()
    start = next((i for i, line in enumerate(lines)
                  if line.startswith("function BjB takes ")), None)
    if start is None:
        raise ValueError("Original BjB registration function not found.")
    end = next((i for i in range(start + 1, len(lines))
                if lines[i].strip() == "endfunction"), None)
    if end is None:
        raise ValueError("BjB function has no endfunction.")

    catalog = {}
    for idx in range(start + 1, end):
        line = lines[idx].strip()
        relation = BI8.fullmatch(line)
        label = BI9.fullmatch(line)
        if relation:
            unit = rawcode(relation.group(1))
            ability = rawcode(relation.group(2))
            item = catalog.setdefault(unit, {"rawcode": unit, "abilities": [],
                                             "labels": [], "firstJassLine": idx + 1})
            if ability not in item["abilities"]:
                item["abilities"].append(ability)
        elif label:
            unit = rawcode(label.group(1))
            item = catalog.setdefault(unit, {"rawcode": unit, "abilities": [],
                                             "labels": [], "firstJassLine": idx + 1})
            item["labels"].append(label.group(2))
        elif line.startswith("call Bi8(") or line.startswith("call Bi9("):
            raise ValueError("Unexpected BjB registration at line %d: %s" %
                             (idx + 1, line))

    entries = sorted(catalog.values(), key=lambda x: x["rawcode"])
    return {
        "gameVersion": "S2 2.323[C]",
        "sourceJassSha256": hashlib.sha256(data).hexdigest(),
        "jassFunction": "BjB",
        "unitRawcodeCount": len(entries),
        "labelledUnitCount": sum(bool(x["labels"]) for x in entries),
        "entries": entries,
        "notes": ["Bi8 provides rawcode->recipe ability links.",
                  "Bi9 provides an original map label/alias for some units.",
                  "A label alone does not verify the full Warcraft object data."],
    }


def check_links(result, links_path):
    links = json.loads(links_path.read_text(encoding="utf-8"))
    by_raw = {row["rawcode"]: row for row in result["entries"]}
    failures = []
    for item in links["mappings"]:
        original = by_raw.get(item["rawcode"])
        if original is None:
            failures.append("Missing unit rawcode: " + item["rawcode"])
            continue
        if item["abilityRawcode"] not in original["abilities"]:
            failures.append("Mismatched ability: " + item["rawcode"])
        if not original["labels"]:
            failures.append("Unlabelled unit: " + item["rawcode"])
    if failures:
        raise ValueError("Incorrect source mapping: " + "; ".join(failures))
    return len(links["mappings"])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--jass", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--links", type=pathlib.Path, default=None)
    args = parser.parse_args()
    extracted = extract(args.jass)
    if args.links is not None:
        count = check_links(extracted, args.links)
        print("Source-backed unit link records: %d" % count)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(extracted, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    print("BjB unit rawcodes: %d, with labels: %d" %
          (extracted["unitRawcodeCount"], extracted["labelledUnitCount"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
