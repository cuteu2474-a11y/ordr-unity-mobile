#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Unpack the self-contained W3X CWR fusion registry into Unity-readable JSON.

Requires Python 3 standard library. No W3X installation required for this
archived snapshot; for precise original JASS line positions use
extract_jass_fusions.py against war3map.j instead.

Usage:
  python tools/unpack_ordered_jass_fusions.py

This command only reconstructs data; it does not execute game fusions.
"""
import argparse
import base64
import hashlib
import json
import lzma
import pathlib
import re
import sys

DEFAULT_SHA256 = "1daece775feb1e3e320cc8587d55e9a889b71d545621048294ec3a5ef78dae44"
MAP_SHA256 = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"
ATOM = re.compile(r"^[A-Za-z0-9]{4}$")


def decode_instruction(spec):
    parts = spec.split("/")
    if len(parts) != 3 or any(ATOM.fullmatch(value) is None for value in parts[:2]):
        raise ValueError("Invalid CWR instruction: " + spec)
    if not parts[2].isdigit() or int(parts[2]) <= 0:
        raise ValueError("Invalid CWR instruction amount: " + spec)
    return {"kind": parts[0], "rawcode": parts[1], "count": int(parts[2])}


def unpack(input_path, output_path):
    raw = input_path.read_text(encoding="utf-8")
    payload = "".join(line.strip() for line in raw.splitlines()
                      if line.strip() and not line.startswith("#"))
    text = lzma.decompress(base64.b64decode(payload, validate=True))
    digest = hashlib.sha256(text).hexdigest()
    if digest != DEFAULT_SHA256:
        raise ValueError("Archived ordered JASS registry failed SHA256 integrity verification.")

    recipes = []
    seen = set()
    for line in text.decode("utf-8").splitlines():
        parts = line.split("|")
        if len(parts) != 3 or not ATOM.fullmatch(parts[0]):
            raise ValueError("Invalid CWR registration: " + line)
        ability, result, conditions = parts
        if ability in seen:
            raise ValueError("Duplicate CWR ability ID: " + ability)
        seen.add(ability)
        recipes.append({
            "abilityRawcode": ability,
            "jassLine": None,
            "results": [decode_instruction(result)],
            "conditions": [decode_instruction(item) for item in conditions.split(",") if item],
        })
    if len(recipes) != 266 or sum(r["abilityRawcode"] == "TEST" for r in recipes) != 1:
        raise ValueError("Unexpected original CWR registry count or TEST fixture.")

    result = {
        "gameVersion": "S2 2.323[C]",
        "source": "ORDR_S2_2.323[C].w3x:war3map.j/CWR",
        "sourceSha256": MAP_SHA256,
        "archiveTextSha256": digest,
        "registryFunction": "CWR",
        "registrationCount": len(recipes),
        "jassLineNumbersPreserved": False,
        "conditionsInOriginalOrder": True,
        "registrations": recipes,
        "warning": "UNIT names are unverified without object data. "
                   "BGI includes restrictions and special operations; never craft "
                   "from this dataset without verified game-state handlers.",
    }
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n",
                           encoding="utf-8")
    return result


def main():
    root = pathlib.Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=pathlib.Path, default=root /
                        "data/original-jass-fusion-ordered.xz.b64")
    parser.add_argument("--output", type=pathlib.Path, default=root /
                        "data/original-jass-fusion-registry.json")
    options = parser.parse_args()
    result = unpack(options.input, options.output)
    print("Original CWR ordered entries: %d, including one TEST fixture." %
          result["registrationCount"])
    print("Generated Unity JSON: %s" % options.output)
    print("No crafting transactions have been executed.")


if __name__ == "__main__":
    sys.exit(main())
