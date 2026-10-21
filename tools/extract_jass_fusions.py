#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Extract ordered Warcraft III fusion registrations from ORDR S2 2.323[C] war3map.j.

Requires Python 3.8+ only. No game execution, test suite or Unity build.
Usage:
    python tools/extract_jass_fusions.py --jass path/to/war3map.j \
        --output data/original-jass-fusion-registry.json

Extract the map's war3map.j from W3X/MPQ first. Never interpret a four-byte
Warcraft object rawcode as a project's character catalog ID without a
verified object-table mapping. Never execute a fusion solely from this data.
"""
import argparse
import collections
import hashlib
import json
import pathlib
import re
import sys

START_PATTERN = re.compile(r"^function CWR takes ")
REGISTER = re.compile(
    r"^call (BGG|BGH|BGI)\(\$([0-9A-Fa-f]{8})"
    r"(?:,\$([0-9A-Fa-f]{8}),(\d+))?\)$"
)


def decode_rawcode(hex_value):
    raw = bytes.fromhex(hex_value)
    if len(raw) != 4:
        raise ValueError("Expected four-byte rawcode")
    return raw.decode("latin-1")


def entry(tag_hex, raw_hex, count_text):
    if raw_hex is None or count_text is None:
        raise ValueError("Incomplete BGI/BGH entry")
    count = int(count_text)
    if count <= 0:
        raise ValueError("Invalid count")
    return {
        "kind": decode_rawcode(tag_hex),
        "rawcode": decode_rawcode(raw_hex),
        "count": count,
    }


def extract(source_path, include_test=False):
    content = source_path.read_bytes()
    lines = content.decode("utf-8-sig").splitlines()
    begin = next((i for i, line in enumerate(lines) if START_PATTERN.match(line)), None)
    if begin is None:
        raise ValueError("CWR fusion registry function not found")
    end = next((i for i in range(begin + 1, len(lines))
                if lines[i].strip() == "endfunction"), None)
    if end is None:
        raise ValueError("CWR function has no endfunction")

    registrations = []
    current = None
    for index in range(begin + 1, end):
        text = lines[index].strip()
        if not text.startswith("call BGG(") and not text.startswith("call BGH(") \
                and not text.startswith("call BGI("):
            continue
        match = REGISTER.fullmatch(text)
        if match is None:
            raise ValueError("Unrecognized registration at line %d: %s" %
                             (index + 1, text))
        operation, first_hex, second_hex, count_text = match.groups()
        if operation == "BGG":
            if current is not None and not current["results"]:
                raise ValueError("Recipe without result near line %d" % (index + 1))
            current = {
                "abilityRawcode": decode_rawcode(first_hex),
                "jassLine": index + 1,
                "results": [],
                "conditions": [],
            }
            registrations.append(current)
        elif current is None:
            raise ValueError("BGI/BGH without preceding BGG")
        elif operation == "BGH":
            current["results"].append(entry(first_hex, second_hex, count_text))
        else:
            # Every BGI is an ordered condition/evaluation instruction.
            # UBAN and KING are active restrictions; UPUN is a special
            # presentation/operation tag. None is a result creation BGH.
            current["conditions"].append(entry(first_hex, second_hex, count_text))

    if any(not x["results"] for x in registrations):
        raise ValueError("Some recipes have no BGH result")
    if not include_test:
        registrations = [r for r in registrations if r["abilityRawcode"] != "TEST"]

    kinds = collections.Counter(c["kind"] for r in registrations
                                for c in r["conditions"])
    return {
        "gameVersion": "S2 2.323[C]",
        "source": source_path.name,
        "sourceSha256": hashlib.sha256(content).hexdigest(),
        "registryFunction": "CWR",
        "startLine": begin + 1,
        "endLine": end + 1,
        "registrationCount": len(registrations),
        "conditionKindCounts": dict(sorted(kinds.items())),
        "registrations": registrations,
        "notes": [
            "BGG starts a registration, BGH supplies a result, BGI supplies ordered conditions.",
            "UNIT/GOLD/WOOD/ITEM/IFCT/SKPT/CHCT/RMAX/PICK and other BGI tags "
            "may require specialised JASS handlers.",
            "UBAN, KING and UPUN are BGI conditions/operation metadata, not item rewards.",
            "Rawcodes cannot be mapped to catalog IDs without Warcraft object data.",
            "Never consume units/currency until the corresponding JASS condition, "
            "progression check, map mode and execution transaction are implemented.",
        ],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--jass", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--include-test-fixture", action="store_true")
    args = parser.parse_args()
    data = extract(args.jass, args.include_test_fixture)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    print("Extracted %s non-executed registrations; output: %s" %
          (data["registrationCount"], args.output))
    print("Source script SHA256: %s" % data["sourceSha256"])
    print("Every BGI entry remains in its original order.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
