#!/usr/bin/env python3
"""Audit original helper damage targets and every shared A04R (Bd8) caller.

Usage:
    PYTHONPATH=/tmp/ordr-mpyq python3 tools/extract_original_helper_damage_jass.py \
      --w3x '/path/to/ORDR_S2_2.323[C].w3x'

By default compares the result to the committed evidence; --output writes
the regenerated evidence to a chosen file. The original map is NOT copied.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

from build_original_map_base_stats import open_archive


ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / "data/original-2323C-helper-damage-jass.json"
MAP_SHA = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"
JASS_SHA = "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d"


def require_fragment(body, snippet, function):
    if snippet not in body:
        raise ValueError(f"원본 JASS {function}에서 근거 구문 누락: {snippet}")


def function_body(jass, name):
    match = re.search(rf"(?m)^function {re.escape(name)} takes .*?^endfunction$",
                      jass, re.DOTALL | re.MULTILINE)
    if match is None:
        raise ValueError("원본 JASS 함수 누락: " + name)
    return match.group(0)


def inspect_jass(jass):
    source = {
        "CJb": function_body(jass, "CJb"),
        "CJf": function_body(jass, "CJf"),
        "CJg": function_body(jass, "CJg"),
        "CJh": function_body(jass, "CJh"),
        "CJi": function_body(jass, "CJi"),
        "Bd8": function_body(jass, "Bd8"),
    }
    require_fragment(source["CJb"],
                     "call UnitDamagePointLoc(Cm,0.,450.,Ie,V0,ATTACK_TYPE_CHAOS,DAMAGE_TYPE_UNIVERSAL)",
                     "CJb")
    require_fragment(source["CJb"], "set V0=7000000.", "CJb")
    require_fragment(source["CJb"], "set V0=10000000.", "CJb")
    # No enemy-only predicate: native UnitDamagePointLoc decides victim set.
    if "IsUnitEnemy" in source["CJb"] or "Condition(" in source["CJb"]:
        raise ValueError("A0BZ 원본 대상 필터가 변경되었습니다")
    require_fragment(source["CJf"],
                     "return IsUnitEnemy(GetFilterUnit(),GetOwningPlayer(GetTriggerUnit()))",
                     "CJf")
    require_fragment(source["CJi"],
                     "call BiZ(Cl,625,GetSpellTargetLoc(),Condition(function CJf),function CJg)",
                     "CJi")
    require_fragment(source["CJi"],
                     "call BiZ(Cl,625,GetSpellTargetLoc(),Condition(function CJf),function CJh)",
                     "CJi")
    for name, raw_damage, delta in (("CJg", "$F4240", 5),
                                    ("CJh", "$1E8480", 8)):
        require_fragment(source[name],
                         f"call Bcb(GetTriggerUnit(),GetEnumUnit(),{raw_damage},1.,1.,ATTACK_TYPE_CHAOS,DAMAGE_TYPE_UNIVERSAL)",
                         name)
        require_fragment(source[name], f"call Bd8(GetEnumUnit(),{delta})", name)
    require_fragment(source["Bd8"], "call UnitAddAbility(Cm,XL)", "Bd8")
    require_fragment(source["Bd8"],
                     "call SetUnitAbilityLevel(Cm,XL,GetUnitAbilityLevel(Cm,XL)+Hk)",
                     "Bd8")

    calls = []
    current_function = ""
    for line_number, line in enumerate(jass.splitlines(), 1):
        start = re.match(r"^function (\w+) takes ", line)
        if start:
            current_function = start.group(1)
        match = re.match(r"^call Bd8\(.+,([0-9]+)\)$", line.strip())
        if match:
            calls.append({"function": current_function,
                          "line": line_number,
                          "levelIncrease": int(match.group(1))})
    if len(calls) != 17 or [c["function"] for c in calls if c["function"] in
                             ("CJg", "CJh")] != ["CJg", "CJh"]:
        raise ValueError("원본 Bd8 호출 개수 또는 도움소 호출자가 변경되었습니다")
    return {
        "gameVersion": "S2 2.323[C]",
        "sourceMapSha256": MAP_SHA,
        "sourceJassSha256": JASS_SHA,
        "A0BZ": {
            "function": "CJb",
            "native": "UnitDamagePointLoc",
            "radius": 450,
            "explicitEnemyFilter": False,
            "allNativeVictimsModeled": False,
            "mustVerifyNativeVictimCoverage": True,
        },
        "A07T": {
            "function": "CJi",
            "targetPredicate": "CJf: IsUnitEnemy(GetFilterUnit(),GetOwningPlayer(GetTriggerUnit()))",
            "radius": 625,
            "callbacks": ["CJg", "CJh"],
            "A04RLevelIncrease": [5, 8],
        },
        "sharedA04R": {
            "function": "Bd8",
            "numberOfCallSites": len(calls),
            "outsideHelperCallSites": len(calls) - 2,
            "callSites": calls,
            "warcraftIsr2EffectFormulaVerified": False,
        },
    }


def extract(w3x):
    actual_map_hash = hashlib.sha256(w3x.read_bytes()).hexdigest()
    if actual_map_hash != MAP_SHA:
        raise ValueError("원본 W3X 해시 불일치: " + actual_map_hash)
    with open_archive(w3x) as archive:
        raw = archive.read_file("war3map.j")
    if hashlib.sha256(raw).hexdigest() != JASS_SHA:
        raise ValueError("원본 JASS 해시 불일치")
    return inspect_jass(raw.decode("utf-8-sig").replace("\r\n", "\n"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--w3x", type=Path, required=True)
    parser.add_argument("--profile", type=Path, default=EVIDENCE)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = extract(args.w3x)
    if args.output:
        args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                               encoding="utf-8")
        print("원본 도움소 광역/A04R JASS 근거 저장:", args.output)
    else:
        expected = json.loads(args.profile.read_text(encoding="utf-8"))
        if expected != result:
            raise ValueError("커밋된 도움소 JASS 근거와 원본 W3X 불일치")
        print("원본 도움소 광역/A04R JASS 검증 성공:",
              result["sharedA04R"]["numberOfCallSites"], "Bd8 호출")


if __name__ == "__main__":
    main()
