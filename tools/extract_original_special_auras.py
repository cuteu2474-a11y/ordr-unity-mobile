#!/usr/bin/env python3
"""Extract six special-grade aura objects, not guessed final combat outcomes."""
import argparse
import hashlib
import json
from pathlib import Path
import struct

from build_original_map_base_stats import open_archive, parse_sylk_bytes
from extract_original_helper_abilities import read_objects
from extract_original_robin_multishot import MAP_SHA, SOURCES, section

ROOT = Path(__file__).resolve().parents[1]
PROFILE = ROOT / "data/original-2323C-special-six-aura-evidence.json"
# ID, owner, ability, parent, buff, signed object field, radius, targets.
PINNED = [
    ("S-10", "h016", "A08N", "AHad", "B011", -3, 800, "air,enemies,ground"),
    ("S-26", "h01E", "A0EO", "AHad", "B02G", -3, 800, "air,enemies,ground"),
    ("S-14", "h01F", "A08F", "AOae", "B012", -.05, 800, "air,enemies,ground"),
    ("S-29", "h01A", "A08E", "AOae", "B001", -.05, 850, "air,enemies,ground"),
    ("S-30", "h00Y", "A08H", "AOae", "B004", -.05, 800, "air,enemies,ground"),
    ("S-25", "h01D", "A0EM", "ACac", "B00V", .07, 850,
     "air,invulnerable,organic,self,ground,vulnerable,friend"),
]


def inspect_source(abilities, owners, weapons, strings, overrides):
    result = []
    for ident, owner, code, parent, buff, value, radius, targets in PINNED:
        if code not in owners[owner]["abilList"].split(","):
            raise ValueError("Missing owner ability: " + owner)
        found = [r for r in overrides if r[1] == code or r[2] == code]
        if code == "A0EM":
            if len(found) != 1 or found[0][1:3] != ("ACac", "A0EM"):
                raise ValueError("A0EM must come from its original W3A record")
            fields = found[0][3]
            def field(name, level, pointer):
                rows = [x[4] for x in fields if (x[0], x[2], x[3]) == (name, level, pointer)]
                if len(rows) != 1:
                    raise ValueError("Missing/duplicate A0EM field: " + name)
                return rows[0]
            raw = field("Cac1", 1, 1)
            expected_float = struct.unpack("<f", struct.pack("<f", value))[0]
            if (raw != expected_float or field("aare", 1, 0) != radius or
                field("abuf", 1, 0) != buff or field("atar", 1, 0) != targets or
                field("Ear2", 1, 2) != 1 or field("Ear3", 1, 3) != 1 or
                field("Ear4", 1, 4) != 0):
                raise ValueError("Original A0EM field changed")
            source = {"table": "war3map.w3a", "parent": parent,
                      "modifications": [list(x) for x in fields]}
        else:
            if found:
                raise ValueError("Unexpected SLK aura override: " + code)
            row = abilities[code]
            if (row["code"] != parent or row["BuffID1"] != buff or
                float(row["DataA1"]) != value or float(row["Area1"]) != radius or
                row["targs1"] != targets or row["levels"] != "1"):
                raise ValueError("Original SLK aura field changed: " + code)
            if parent == "AOae" and float(row["DataB1"]) != 0:
                raise ValueError("Unexpected attack-speed modifier: " + code)
            raw = float(row["DataA1"])
            source = {"table": "Units\\AbilityData.slk", "fields": row,
                      "strings": section(strings, code)}
        result.append({
            "catalogUnitId": ident, "unitRawcode": owner, "abilityRawcode": code,
            "parentAbility": parent, "buffRawcode": buff,
            "signedRawObjectValue": raw, "radius": radius, "targetMask": targets,
            "originalAbilityList": owners[owner]["abilList"],
            "objectWeaponFields": weapons[owner], "abilitySource": source,
        })
    return result


def build(path):
    if hashlib.sha256(path.read_bytes()).hexdigest() != MAP_SHA:
        raise ValueError("Wrong original W3X fingerprint")
    with open_archive(path) as archive:
        blobs = {name: archive.read_file(name) for name in SOURCES}
    for name, digest in SOURCES.items():
        if blobs[name] is None or hashlib.sha256(blobs[name]).hexdigest() != digest:
            raise ValueError("Wrong original file fingerprint: " + name)
    rows = inspect_source(
        parse_sylk_bytes(blobs["Units\\AbilityData.slk"]),
        parse_sylk_bytes(blobs["Units\\UnitAbilities.slk"]),
        parse_sylk_bytes(blobs["Units\\UnitWeapons.slk"]),
        blobs["Units\\CampaignAbilityStrings.txt"].decode("utf-8-sig"),
        read_objects(blobs["war3map.w3a"]),
    )
    return {"gameVersion": "S2 2.323[C]", "sourceW3xSha256": MAP_SHA,
            "sourceFiles": [{"path": n, "sha256": s} for n, s in SOURCES.items()],
            "entries": rows, "liveTargetEligibilityVerified": False,
            "nativeStackingAndLingerVerified": False,
            "nativeAttackBuffFormulaVerified": False,
            "safeForUnconditionalLiveCombat": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--w3x", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = build(args.w3x)
    if args.output:
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n",
                               encoding="utf-8")
    elif json.loads(PROFILE.read_text(encoding="utf-8")) != report:
        raise ValueError("Committed aura evidence differs from original W3X")
    print("Six aura objects verified against original W3X; live mechanics remain separately gated.")


if __name__ == "__main__":
    main()
