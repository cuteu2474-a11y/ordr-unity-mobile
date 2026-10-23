#!/usr/bin/env python3
"""Extract the four automatic Wisp reward pools from the pinned 2.323[C] JASS.

The W3X itself is never checked into git. This extractor fails closed when the
original function structure, weights, branch, or source fingerprint changes.
"""

import argparse
import hashlib
import json
from pathlib import Path
import re


JASS_SHA256 = "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d"
MAP_SHA256 = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"
POOL_SPEC = (
    ("e017", "CSE", "CSF", (("su", 100, 13, None),)),
    ("e016", "CSB", "CSC", (("st", 100, 33, None),)),
    ("e019", "CSH", "CSI", (("sv", 99, 42, "A0YE"), ("sw", 1, 8, None))),
    ("e015", "CR5", "CR6", (("ss", 100, 60, None),)),
)


def function(script, name):
    match = re.search(r"^function " + name + r" takes nothing returns nothing\r?\n.*?^endfunction$",
                      script, re.MULTILINE | re.DOTALL)
    if match is None:
        raise ValueError("Original function missing: " + name)
    return match.group(), script.count("\n", 0, match.start()) + 1


def decode(raw_hex):
    return bytes.fromhex(raw_hex).decode("ascii")


def extract(jass_bytes):
    digest = hashlib.sha256(jass_bytes).hexdigest()
    if digest != JASS_SHA256:
        raise ValueError("Unexpected war3map.j fingerprint " + digest)
    source = jass_bytes.decode("utf-8-sig").replace("\r\n", "\n")
    data = []
    for wisp, initializer, consumer, specs in POOL_SPEC:
        body, init_line = function(source, initializer)
        consumer_body, consumer_line = function(source, consumer)
        branches = []
        for pool, chance, expected_count, bonus in specs:
            matches = list(re.finditer(r"^call UnitPoolAddUnitType\(" + pool +
                                       r",\$([0-9A-Fa-f]{8}),(\d+)\)$", body, re.MULTILINE))
            if len(matches) != expected_count:
                raise ValueError(f"{initializer}/{pool}: expected {expected_count}, found {len(matches)}")
            entries = []
            for match in matches:
                if match.group(2) != "1":
                    raise ValueError("Unexpected pool weight in " + initializer)
                entries.append({"rawcode": decode(match.group(1)), "weight": 1,
                                "jassLine": init_line + body.count("\n", 0, match.start())})
            if len({entry["rawcode"] for entry in entries}) != expected_count:
                raise ValueError("Duplicate pool rawcode in " + initializer)
            if f"PlaceRandomUnit({pool},Player(JG)" not in consumer_body:
                raise ValueError("Consumer no longer rolls pool " + pool)
            branches.append({"poolVariable": pool, "chancePercent": chance,
                             "bonusAbilityRawcode": bonus, "entries": entries})
        if len(branches) == 2:
            if "if BFi(1,100)<=99 then" not in consumer_body or \
               "call UnitAddAbility(bj_lastCreatedUnit,$41305945)" not in consumer_body:
                raise ValueError("Rare Wisp 99/1 branch or A0YE application changed")
        if f"GetUnitTypeId(GetTriggerUnit())!=" not in consumer_body or \
           "call BdD(GetTriggerUnit())" not in consumer_body:
            raise ValueError("Wisp consumer lacks original single-use semantics")
        data.append({"wispRawcode": wisp, "initializerFunction": initializer,
                     "consumerFunction": consumer, "consumerJassLine": consumer_line,
                     "branches": branches})
    expected_constants = {
        "e017": "YT", "e016": "b6", "e019": "b5", "e015": "b4"
    }
    for rawcode, symbol in expected_constants.items():
        if not re.search(r"^constant integer " + symbol + r"=\$" +
                         rawcode.encode("ascii").hex().upper() + "$", source, re.MULTILINE):
            raise ValueError("Original Wisp rawcode constant mismatch " + rawcode)
    return {"gameVersion": "S2 2.323[C]", "sourceMapSha256": MAP_SHA256,
            "sourceJassSha256": digest, "source": "original war3map.j",
            "rollSelection": "unit-pool-weights", "wisps": data}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--jass", required=True, type=Path)
    parser.add_argument("--output", type=Path,
                        default=Path(__file__).resolve().parents[1] /
                        "data/original-2323C-wisp-pools.json")
    args = parser.parse_args()
    result = extract(args.jass.read_bytes())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("source:", result["sourceJassSha256"])
    for row in result["wisps"]:
        print(row["wispRawcode"], [(b["chancePercent"], len(b["entries"]))
                                   for b in row["branches"]])


if __name__ == "__main__":
    main()
