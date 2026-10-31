#!/usr/bin/env python3
"""Inspect the next thirty units against the pinned original JASS, without authorizing runtime effects.

Usage: python tools/audit_next_thirty_callbacks.py --jass /path/to/war3map.j
The source is intentionally not modified or committed.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

from audit_attack_skill_batch import JASS_SHA, code_only, functions, inspect_function

ROOT = Path(__file__).resolve().parents[1]
REVIEW = ROOT / "data/original-2323C-next-30-source-review.json"
OUTPUT = ROOT / "data/original-2323C-next-30-callback-audit.json"
REGISTER = re.compile(r"\bcall\s+w5\s*\(\s*\$([0-9a-fA-F]{8})\s*,\s*function\s+(\w+)\s*\)")
HEX_RAW = re.compile(r"\$([0-9a-fA-F]{8})")


def extract_registrations(source):
    """Use actual code, not strings/comments, to avoid false registrations."""
    result = {}
    for match in REGISTER.finditer(code_only(source)):
        try:
            rawcode = bytes.fromhex(match.group(1)).decode("ascii")
        except UnicodeDecodeError as exc:
            raise ValueError("Non-ASCII unit rawcode") from exc
        result.setdefault(rawcode, set()).add(match.group(2))
    return result


def inspect(path, review):
    raw = path.read_bytes()
    if hashlib.sha256(raw).hexdigest() != JASS_SHA:
        raise ValueError("Original JASS SHA mismatch; refuse inferred effects")
    source = raw.decode("utf-8-sig").replace("\r\n", "\n")
    table = functions(source)
    registrations = extract_registrations(source)
    entries = review["entries"]
    if review["runtimeAuthorization"] is not False or len(entries) != 30:
        raise ValueError("Unreviewed next-thirty source gate")
    if len({row["id"] for row in entries}) != 30:
        raise ValueError("Duplicate catalog ID")
    results = []
    for row in entries:
        rawcode = row["rawcode"]
        callbacks = sorted(registrations.get(rawcode, ())) if isinstance(rawcode, str) else []
        if len(callbacks) > 1:
            raise ValueError("Ambiguous attack callbacks: " + row["id"])
        dependencies = []
        if callbacks:
            callback = callbacks[0]
            if callback not in table:
                raise ValueError("Registered callback missing: " + callback)
            # One direct dependency layer only; do not claim full call closure.
            record = inspect_function(callback, table)
            dependencies = [inspect_function(n, table)
                            for n in record["customDependencies"]]
        else:
            record = None
        results.append({
            "id": row["id"], "name": row["name"], "rawcode": row["rawcode"],
            "documentedSkills": row["documentedSkills"],
            "attackCallback": record, "directDependencies": dependencies,
            "otherAbilityTriggersVerified": False,
            "runtimeAuthorization": False,
            "unresolved": ["Native ability triggers and cast handlers",
                           "Complete dependency graph and dummy effects",
                           "Original target, timing, immunity and damage outcomes"],
        })
    return {
        "format": "ordr-next-30-callback-audit-v1",
        "sourceJassSha256": JASS_SHA, "unitCount": len(results),
        "runtimeAuthorization": False,
        "note": "An absent w5 callback does not mean an absent active or passive skill.",
        "entries": results,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--jass", type=Path, required=True)
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    review = json.loads(REVIEW.read_text(encoding="utf-8"))
    result = inspect(args.jass, review)
    encoded = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.verify:
        if not OUTPUT.exists() or OUTPUT.read_text(encoding="utf-8") != encoded:
            raise SystemExit("Next-thirty callback audit is stale")
    else:
        OUTPUT.write_text(encoded, encoding="utf-8")
    print("Audited 30 units; original callback registration only; runtime authorization false")


if __name__ == "__main__":
    main()
