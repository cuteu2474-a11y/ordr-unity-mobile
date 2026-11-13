#!/usr/bin/env python3
"""Rebuild tooltip inventory derivatives without fabricating combat authority.

Only the documented skill/passive counts in the native-runtime snapshot are
reconciled here. All actual connected effects and original verification flags
remain unchanged. The skill research queue is then regenerated from the
updated runtime metadata and the authoritative tooltip profiles.
"""
import argparse
import json
from pathlib import Path

from classify_character_skills import build as classify_skills


ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data"
RUNTIME = DATA / "unit-runtime-status.json"
QUEUE = DATA / "original-2323C-skill-research-queue.json"


def read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def regenerate():
    profiles = read(DATA / "character-skill-profiles.json")
    units = read(DATA / "units.json")
    objects = read(DATA / "original-2323C-map-base-stats.json")
    runtime = read(RUNTIME)
    indexed = {p["unitId"]: p for p in profiles["profiles"]}
    if len(indexed) != len(profiles["profiles"]):
        raise ValueError("Duplicate tooltip unit ID")
    known = {e["catalogId"] for e in runtime["entries"]}
    if not set(indexed) <= known or len(known) != len(runtime["entries"]):
        raise ValueError("Tooltip/runtime unit IDs do not match")

    for row in runtime["entries"]:
        profile = indexed.get(row["catalogId"], {})
        row["documentedSkills"] = len(profile.get("skills", []))
        row["documentedPassives"] = len(profile.get("passives", []))
        # Never change connectedModelEffects or fullUnitImplementationVerified.
    runtime["summary"]["documentedSkillEntries"] = sum(
        row["documentedSkills"] for row in runtime["entries"])
    runtime["summary"]["documentedPassiveEntries"] = sum(
        row["documentedPassives"] for row in runtime["entries"])
    queue = classify_skills(units, profiles, objects, runtime)
    return runtime, queue


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true",
                        help="Update both generated outputs; default is read-only verification")
    args = parser.parse_args()
    runtime, queue = regenerate()
    for path, value in ((RUNTIME, runtime), (QUEUE, queue)):
        desired = json.dumps(value, ensure_ascii=False, indent=2) + "\n"
        if args.write:
            path.write_text(desired, encoding="utf-8", newline="\n")
            print("Updated", path.relative_to(ROOT))
        elif not path.is_file() or path.read_text(encoding="utf-8") != desired:
            parser.exit(1, "Out-of-date tooltip export: " +
                        str(path.relative_to(ROOT)) + "\n")
    print("Tooltip exports consistent:",
          runtime["summary"]["documentedSkillEntries"], "skills /",
          runtime["summary"]["documentedPassiveEntries"], "passives")


if __name__ == "__main__":
    main()
