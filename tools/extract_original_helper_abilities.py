#!/usr/bin/env python3
"""Extract ONLY source-verified help-cow A04R/area ability evidence.

W3X/JASS binary files stay local, never in Git. The output contains no
guessed cast range, mana cost, cooldown, Warcraft damage reduction formula,
or baseline enemy ability level.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct

JASS_SHA = "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d"
W3A_SHA = "aa938f02c22884f38c5e31bcd6229a35e917a8fab0128331fdab88980cfd50e0"
MAP_SHA = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"


def read_i32(raw, pos):
    if pos + 4 > len(raw):
        raise ValueError("Truncated .w3a")
    return struct.unpack_from("<i", raw, pos)[0]


def read_objects(raw):
    if read_i32(raw, 0) != 2:
        raise ValueError("Unexpected .w3a format version")
    offset = 4
    records = []
    for table in ("original", "custom"):
        count = read_i32(raw, offset)
        offset += 4
        if count < 0 or count > 100000:
            raise ValueError("Invalid ability count")
        for _ in range(count):
            if offset + 12 > len(raw):
                raise ValueError("Truncated object entry")
            old_id = raw[offset:offset+4].decode("ascii")
            new_id = raw[offset+4:offset+8].decode("latin1")
            offset += 8
            modifications = read_i32(raw, offset)
            offset += 4
            if modifications < 0 or modifications > 100000:
                raise ValueError("Invalid modifier count")
            fields = []
            for _ in range(modifications):
                if offset + 16 > len(raw):
                    raise ValueError("Truncated ability field")
                field = raw[offset:offset+4].decode("ascii")
                value_type, level, data_pointer = struct.unpack_from("<iii", raw, offset+4)
                offset += 16
                if value_type == 3:
                    end = raw.find(b"\x00", offset)
                    if end < 0:
                        raise ValueError("Unterminated string field")
                    value = raw[offset:end].decode("utf-8", errors="replace")
                    offset = end+1
                elif value_type in (0, 1, 2):
                    if offset + 4 > len(raw):
                        raise ValueError("Truncated numeric field")
                    value = struct.unpack_from("<f" if value_type in (1, 2)
                                               else "<i", raw, offset)[0]
                    offset += 4
                else:
                    raise ValueError("Unknown field type")
                if offset + 4 > len(raw):
                    raise ValueError("Missing end object identifier")
                end_object = raw[offset:offset+4]
                offset += 4
                if end_object != b"\x00\x00\x00\x00":
                    raise ValueError("Unexpected w3a end marker")
                fields.append((field, value_type, level, data_pointer, value))
            records.append((table, old_id, new_id, fields))
    if offset != len(raw):
        raise ValueError("Trailing bytes in verified w3a")
    return records


def extract(script_bytes, ability_bytes):
    if hashlib.sha256(script_bytes).hexdigest() != JASS_SHA:
        raise ValueError("Unexpected original JASS fingerprint")
    if hashlib.sha256(ability_bytes).hexdigest() != W3A_SHA:
        raise ValueError("Unexpected original W3A fingerprint")
    jass = script_bytes.decode("utf-8-sig").replace("\r\n", "\n")
    anchors = [
        "constant integer XL=$41303452",
        "function Bd8 takes unit Cm,integer Hk returns nothing",
        "call SetUnitAbilityLevel(Cm,XL,GetUnitAbilityLevel(Cm,XL)+Hk)",
        "call Bcb(GetTriggerUnit(),GetEnumUnit(),$F4240,1.,1.,ATTACK_TYPE_CHAOS,DAMAGE_TYPE_UNIVERSAL)",
        "call Bd8(GetEnumUnit(),5)",
        "call Bcb(GetTriggerUnit(),GetEnumUnit(),$1E8480,1.,1.,ATTACK_TYPE_CHAOS,DAMAGE_TYPE_UNIVERSAL)",
        "call Bd8(GetEnumUnit(),8)",
        "call BiZ(Cl,625,GetSpellTargetLoc(),Condition(function CJf),function CJg)",
        "call BiZ(Cl,625,GetSpellTargetLoc(),Condition(function CJf),function CJh)",
    ]
    for anchor in anchors:
        if anchor not in jass:
            raise ValueError("Missing source evidence: " + anchor)

    records = read_objects(ability_bytes)
    targets = [row for row in records if row[1] == "A04R" or row[2] == "A04R"]
    if len(targets) != 1:
        raise ValueError("A04R object count does not match original")
    ability = targets[0]
    fields = ability[3]
    isr2 = [(level, value) for field, typ, level, pointer, value in fields
            if field == "isr2" and typ == 2 and pointer == 2]
    if len(isr2) != 22:
        raise ValueError("Expected exactly 22 isr2 level overrides")
    if sorted(level for level, _ in isr2) != list(range(5, 27)):
        raise ValueError("Unexpected modified levels")
    for level, value in isr2:
        expected = struct.unpack("<f", struct.pack("<f", (1-level)*0.01))[0]
        if value != expected:
            raise ValueError(f"isr2 level {level} differs from pinned map")
    if any(rec[1] in ("A0BZ", "A07T") or rec[2] in ("A0BZ", "A07T")
           for rec in records):
        raise ValueError("Cast ability object evidence changed unexpectedly")

    return {
        "gameVersion": "S2 2.323[C]",
        "sourceMapSha256": MAP_SHA,
        "sourceJassSha256": JASS_SHA,
        "sourceW3aSha256": W3A_SHA,
        "abilityRawcode": "A04R",
        "fieldId": "isr2",
        "sourceFieldMeaning": "DAMAGE_REDUCTION_ISR2",
        "sourceTable": ability[0],
        "sourceLevelOverrides": [
            {"level": level, "value": round(value, 6)} for level, value in sorted(isr2)
        ],
        "jassMechanics": {
            "function": "Bd8",
            "onMissingAbility": "UnitAddAbility then GetUnitAbilityLevel",
            "abilityLevelIncreaseA07TLevel1": 5,
            "abilityLevelIncreaseA07TLevel2": 8,
            "isTimedStun": False
        },
        "castObjectDataA0BZPresentInW3a": False,
        "castObjectDataA07TPresentInW3a": False,
        "originalCastRanges": None,
        "originalManaCosts": None,
        "originalCooldownSeconds": None,
        "originalEnemyStartingA04RLevels": None,
        "engineDamageApplicationOfIsr2Verified": False,
        "sourceNote": "Original JASS + W3A map overrides; no inferred defaults."
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--jass", type=Path, required=True)
    parser.add_argument("--w3a", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=(
        Path(__file__).resolve().parents[1] /
        "data/original-2323C-helper-A04R-evidence.json"))
    args = parser.parse_args()
    result = extract(args.jass.read_bytes(), args.w3a.read_bytes())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    print("Verified:", result["abilityRawcode"],
          len(result["sourceLevelOverrides"]), "isr2 levels")


if __name__ == "__main__":
    main()
