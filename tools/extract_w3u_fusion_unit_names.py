#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Read original Warcraft III unit object-name evidence for fusion rawcodes.

Extract war3map.w3u (and optionally war3map.wts) from the SAME verified
ORDR S2 2.323[C] map. The script never runs Warcraft, Unity or gameplay tests.

Usage:
  python tools/extract_w3u_fusion_unit_names.py \
    --w3u "war3map.w3u" --wts "war3map.wts" \
    --backlog data/original-fusion-rawcode-backlog.json \
    --units data/units.json \
    --output data/original-w3u-fusion-unit-name-evidence.json

Output is AUDIT/CANDIDATE data, not an IVerifiedUnitRawcodeMap. Names may be
aliases, a base game name, or duplicate names across different grades.
"""
import argparse
import hashlib
import json
import pathlib
import re
import struct
import unicodedata
from collections import defaultdict

COLOR_CODE = re.compile(r"\|c[0-9A-Fa-f]{8}|\|r")
WTS_REF = re.compile(r"^TRIGSTR_(\d+)$")
WTS_ENTRY = re.compile(r"(?ms)^\s*STRING\s+(\d+)\s*\{(.*?)^\s*\}")


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def decode_text(data):
    for encoding in ("utf-8", "cp949"):
        try:
            return data.decode(encoding)
        except UnicodeDecodeError:
            continue
    return data.decode("latin-1")


def normalize(name):
    # Only layout/colour normalization. Do not remove grade or name suffixes.
    return unicodedata.normalize("NFC", " ".join(
        COLOR_CODE.sub("", name).replace("|n", " ").split()))


class BinaryReader:
    def __init__(self, data):
        self.data = data
        self.offset = 0

    def take(self, size):
        if size < 0 or self.offset + size > len(self.data):
            raise ValueError("Truncated or invalid .w3u at offset %d" % self.offset)
        result = self.data[self.offset:self.offset + size]
        self.offset += size
        return result

    def number(self):
        return struct.unpack("<i", self.take(4))[0]

    def unit_rawcode(self):
        return self.take(4).decode("latin-1")

    def cstring(self):
        end = self.data.find(b"\x00", self.offset)
        if end < 0 or end - self.offset > 1_048_576:
            raise ValueError("Invalid .w3u string at offset %d" % self.offset)
        value = self.data[self.offset:end]
        self.offset = end + 1
        return decode_text(value)


def read_wts(path):
    if path is None:
        return {}
    text = decode_text(path.read_bytes())
    result = {}
    for match in WTS_ENTRY.finditer(text):
        key = int(match.group(1))
        if key in result:
            raise ValueError("Duplicate WTS STRING: %d" % key)
        result[key] = match.group(2).strip("\r\n")
    return result


def read_w3u(path):
    reader = BinaryReader(path.read_bytes())
    version = reader.number()
    if version not in (1, 2):
        raise ValueError("Unsupported unit object file version: %s" % version)
    objects = []
    for table_index, table_name in enumerate(("modifiedOriginal", "custom")):
        count = reader.number()
        if count < 0 or count > 500_000:
            raise ValueError("Invalid .w3u table count: %d" % count)
        for _ in range(count):
            based_on = reader.unit_rawcode()
            custom = reader.unit_rawcode()
            rawcode = based_on if table_index == 0 else custom
            if table_index == 0 and custom != "\x00" * 4:
                raise ValueError("Original table has an unexpected custom rawcode")
            if not rawcode.strip("\x00"):
                raise ValueError("Object rawcode is empty")
            modification_count = reader.number()
            if modification_count < 0 or modification_count > 100_000:
                raise ValueError("Invalid .w3u modification count")
            modifications = []
            for _ in range(modification_count):
                field = reader.unit_rawcode()
                value_type = reader.number()
                if value_type == 0:
                    value = reader.number()
                elif value_type in (1, 2):
                    value = struct.unpack("<f", reader.take(4))[0]
                elif value_type == 3:
                    value = reader.cstring()
                else:
                    raise ValueError("Unsupported .w3u field type %d for %s" %
                                     (value_type, field))
                end_marker = reader.take(4)
                modifications.append({
                    "fieldRawcode": field,
                    "valueType": value_type,
                    "value": value,
                    "endMarkerHex": end_marker.hex(),
                })
            name_fields = [m["value"] for m in modifications
                           if m["fieldRawcode"] == "unam" and m["valueType"] == 3]
            objects.append({
                "rawcode": rawcode,
                "baseRawcode": based_on,
                "objectTable": table_name,
                "objectNameFields": name_fields,
                "modifiedFieldCount": modification_count,
            })
    if reader.offset != len(reader.data):
        raise ValueError(".w3u has trailing bytes: unexpected file layout")
    if len({x["rawcode"] for x in objects}) != len(objects):
        raise ValueError("Duplicate unit rawcode in original .w3u")
    return version, objects


def extract(w3u, wts, backlog, units):
    source_rawcodes = {x["rawcode"] for x in backlog["entries"]}
    names = defaultdict(list)
    for entry in units["entries"]:
        names[normalize(entry["name"])].append(entry["id"])
    version, objects = read_w3u(w3u)
    strings = read_wts(wts)
    seen = set()
    out = []
    for obj in objects:
        raw = obj["rawcode"]
        if raw not in source_rawcodes:
            continue
        seen.add(raw)
        name_candidates = []
        for raw_name in obj["objectNameFields"]:
            ref = WTS_REF.fullmatch(raw_name)
            resolved = strings.get(int(ref.group(1))) if ref else raw_name
            known = resolved is not None
            normalized = normalize(resolved) if known else None
            name_candidates.append({
                "originalObjectField": raw_name,
                "resolvedName": resolved,
                "wtsResolved": bool(ref and known),
                "unresolvedWtsReference": bool(ref and not known),
                "exactNameCatalogCandidates": names.get(normalized, []) if known else [],
            })
        out.append({
            "rawcode": raw,
            "baseRawcode": obj["baseRawcode"],
            "objectTable": obj["objectTable"],
            "modifiedFieldCount": obj["modifiedFieldCount"],
            "objectNameEvidence": name_candidates,
            "objectDataVerified": False,
            "usableForAutomaticFusion": False,
        })
    return {
        "gameVersion": "S2 2.323[C]",
        "scope": "original-w3u-object-name-candidates-only",
        "sourceW3uSha256": sha256(w3u),
        "sourceWtsSha256": sha256(wts) if wts else None,
        "sourceFusionRegistrySha256": backlog["sourceSha256"],
        "sourceUnitsCatalogVersion": units["catalogVersion"],
        "objectFileVersion": version,
        "fusionRawcodeCount": len(source_rawcodes),
        "fusionRawcodesPresentInW3u": len(seen),
        "fusionRawcodesAbsentFromW3u": sorted(source_rawcodes - seen),
        "entries": sorted(out, key=lambda x: x["rawcode"]),
        "notes": [
            "An original-name field or an exact catalog-name candidate is NOT authoritative object mapping.",
            "An absent rawcode may be an unmodified base unit or stored in different map data.",
            "Never populate IVerifiedUnitRawcodeMap from this file without original W3X/object review.",
            "All duplicate-name candidate IDs remain unresolved until manually disambiguated.",
        ],
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--w3u", type=pathlib.Path, required=True)
    parser.add_argument("--wts", type=pathlib.Path)
    parser.add_argument("--backlog", type=pathlib.Path,
                        default=pathlib.Path("data/original-fusion-rawcode-backlog.json"))
    parser.add_argument("--units", type=pathlib.Path,
                        default=pathlib.Path("data/units.json"))
    parser.add_argument("--output", type=pathlib.Path,
                        default=pathlib.Path("data/original-w3u-fusion-unit-name-evidence.json"))
    args = parser.parse_args()
    backlog = json.loads(args.backlog.read_text(encoding="utf-8"))
    units = json.loads(args.units.read_text(encoding="utf-8"))
    if backlog["gameVersion"] != "S2 2.323[C]" or units["gameVersion"] != "S2 2.323[C]":
        raise ValueError("Original fusion and unit catalog versions do not match")
    result = extract(args.w3u, args.wts, backlog, units)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    print("Original .w3u source rawcodes:", result["fusionRawcodesPresentInW3u"])
    print("Remain absent from .w3u:", len(result["fusionRawcodesAbsentFromW3u"]))
    print("Evidence only; automatic fusion mapping remains disabled.")


if __name__ == "__main__":
    main()
