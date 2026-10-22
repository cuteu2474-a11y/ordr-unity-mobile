#!/usr/bin/env python3
"""Check C-01 JASS callback anchors against the exact user-provided original.

The source JASS is intentionally not committed; --jass points to an
authorized locally extracted war3map.j with the pinned SHA-256.
"""
import argparse
import hashlib
import json
from pathlib import Path


def verify(jass_path, evidence):
    script = Path(jass_path).read_bytes()
    if hashlib.sha256(script).hexdigest() != evidence["sourceWar3mapJSha256"]:
        raise ValueError("Original JASS fingerprint mismatch")
    lines = script.decode("utf-8-sig").splitlines()
    if len(lines) != evidence["sourceLineCount"]:
        raise ValueError("Original JASS line count mismatch")
    count = 0
    for ability in evidence["commonAbilities"]:
        for point in ability["checks"]:
            idx = point["line"]
            if not (1 <= idx <= len(lines)) or point["contains"] not in lines[idx-1]:
                raise ValueError("%s original callback anchor mismatch on line %d" %
                                 (ability["rawcode"], idx))
            count += 1
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--jass", required=True, type=Path)
    args = parser.parse_args()
    path = Path(__file__).resolve().parents[1] / "data" / (
        "original-2323C-common-ability-jass-evidence.json")
    evidence = json.loads(path.read_text(encoding="utf-8"))
    print("PASS: %d exact original JASS callback anchors" %
          verify(args.jass, evidence))


if __name__ == "__main__":
    main()
