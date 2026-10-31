#!/usr/bin/env python3
"""Reproduce ten old script-callback NATIVE object inputs from pinned W3X.

No binary is copied to git; field values do not certify Warcraft engine parity.
"""
import argparse
import hashlib
import json
from pathlib import Path

from build_original_map_base_stats import open_archive
from extract_original_unit_slk_stats import parse_sylk_bytes

ROOT = Path(__file__).resolve().parents[1]
W3X_SHA = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"
JASS_SHA = "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d"
ABILITY_SHA = "23e9eb93d805a63cc76bfb617189331aef1e40c0c0ec69860bd7399a3bef2eda"
OWNER_SHA = "fbd47be22d1482d63df15d70d7a8fa38f7f26b288443a5d74aa056104aef139d"
OUTPUT = ROOT / "data/original-2323C-legacy-10-native-followups.json"
REPORT = ROOT / "docs/기존-10명-원본-후속효과.md"
TARGETS = {
    "U-04": ("e0KD", "A0VS", "Bnm"),
    "U-13": ("e0KD", "A0VS", "Bnp"),
    "S-03": ("e00K", "A089", "Bnt"),
    "R-13": ("e01G", "A14X", "Bo9"),
    "R-26": ("h0C5", "A14Y", "BpX"),
    "R-30": ("e01P", "A150", "Bpd"),
    "R-31": ("e01Q", "A151", "Bpf"),
    "H-16": ("e031", "A156", "Bq3"),
    "L-33": ("e04X", None, "Btd"),
    "RN-04": ("e0LE", "A10I", "CCg"),
}
FIELDS = ("code", "targs1", "DataA1", "DataB1", "DataC1", "DataD1",
          "Area1", "Rng1", "Cast1", "Dur1", "HeroDur1", "Cool1",
          "UnitID1", "BuffID1", "levels")


def checked(raw, expected, name):
    if hashlib.sha256(raw).hexdigest() != expected:
        raise ValueError("Incorrect pinned " + name)
    return raw


def build(w3x):
    checked(w3x.read_bytes(), W3X_SHA, "W3X")
    with open_archive(w3x) as archive:
        jass = checked(archive.read_file("war3map.j"), JASS_SHA, "JASS").decode("utf-8-sig")
        abilities = parse_sylk_bytes(checked(archive.read_file(r"Units\AbilityData.slk"),
                                         ABILITY_SHA, "AbilityData"))
        owners = parse_sylk_bytes(checked(archive.read_file(r"Units\UnitAbilities.slk"),
                                      OWNER_SHA, "UnitAbilities"))
    source = json.loads((ROOT / "data/original-2323C-attack-skill-batch.json").read_text())
    by_id = {u["catalogId"]: u for u in source["units"]}
    result = []
    for unit_id, (dummy, ability, callback) in TARGETS.items():
        jass_callback = by_id[unit_id]["callback"]
        if jass_callback["function"] != callback or by_id[unit_id]["rawcode"] not in owners:
            raise ValueError("Old callback identity differs: " + unit_id)
        dummy_codes = owners[dummy]["abilList"].split(",")
        if ability is not None and ability not in dummy_codes and unit_id != "R-26":
            raise ValueError("Missing native ability on dummy: " + unit_id)
        if unit_id == "R-26" and ("$41313459" not in jass or "A12Y" not in
                                      owners["e01L"]["abilList"]):
            raise ValueError("R-26 two different inferno dummy sources changed")
        info = abilities.get(ability, {}) if ability else {}
        line = {
            "catalogId": unit_id,
            "ownerRawcode": by_id[unit_id]["rawcode"],
            "sourceCallback": callback,
            "nativeDummyRawcode": dummy,
            "dummyAbilityRawcodes": dummy_codes,
            "nativeAbilityRawcode": ability,
            "nativeAbilityFields": {k: info[k] for k in FIELDS
                                    if k in info and info[k] not in ("", "_")},
            "sourceJassOperations": jass_callback["sourceOperations"],
            "verifiedFinalWarcraftOutput": False,
        }
        if ability == "A0VS":
            if not any("IssuePointOrderLoc(Vr,\"carrionswarm\",Id)" in x
                       for x in line["sourceJassOperations"]):
                raise ValueError("Rankyaku original cast is not at own location")
        result.append(line)

    waves = json.loads((ROOT / "data/original-2323C-monster-wave-script-evidence.json").read_text())
    wave_codes = {e["rawcode"] for w in waves["waves"] for e in w["normalAndBossSpawns"]}
    without_a04r = [code for code in wave_codes if
                    "A04R" not in owners[code]["abilList"].split(",")]
    if len(wave_codes) != 68 or without_a04r:
        raise ValueError("All 68 original wave types must have initial A04R")
    if "call Bd8(RN,15)" not in jass or "call IssueTargetOrderById(JI,OrderId(\"faeriefire\"),RN)" not in jass:
        raise ValueError("CCg source debuff operations changed")
    if owners["e04X"]["abilList"].split(",") != ["Aloc", "Avul"]:
        raise ValueError("Cracker's helper is not a visual-only dummy")
    if info and len(result) != 10:
        raise ValueError("Old batch was not ten characters")
    return {
        "format": "ordr-legacy-10-native-followups-v1",
        "sourceW3xSha256": W3X_SHA,
        "sourceJassSha256": JASS_SHA,
        "runtimeAuthorization": "MODEL_ONLY",
        "verifiedFullWarcraftNativeParity": False,
        "waveSourceA04RInitialLevel": 1,
        "waveTypesWithA04R": sorted(wave_codes),
        "waveTypesWithA04RCount": len(wave_codes),
        "sourceA04RLevelMax": 26,
        "sourceSentinelA04RIncrease": 15,
        "units": result,
    }


def render(data):
    lines = ["# 이전 10명 보조 주문의 실제 원본 객체 데이터", "",
             "> W3X/JASS SHA를 다시 검사하여 추출했습니다. 아래의 `원본 객체 수치`와 "
             "`워크래프트 실제 피격 결과`는 다릅니다. 실행은 `MODEL_ONLY`이며 실기 완전 동등성이 아닙니다.", "",
             "| 캐릭터 | JASS | 보조 주문 | 원본 객체 수치 | 연습 연결 결과 |",
             "|---|---|---|---|---|"]
    meanings = {
        "U-04": "공격자 자기 위치로 주문, 모바일 직선 방향은 현재 타깃 정책",
        "U-13": "공격자 자기 위치로 주문, 모바일 직선 방향은 현재 타깃 정책",
        "S-03": "e00K 위치에서 적 이동속도 -5% / 반경 450 오라",
        "R-13": "반경 425 / 일반 대상 기절 0.9초 / 추가 주문 피해 0",
        "R-26": "h0C5에 A14Y를 추가해 시전: 반경 405 / 기절 0.95초 / 추가 피해 0",
        "R-30": "반경 485 / 기절 1.15초 / 추가 주문 피해 0",
        "R-31": "반경 500 / 기절 1.2초 / 추가 주문 피해 0",
        "H-16": "반경 415 / 기절 1.7초 / 추가 주문 피해 0",
        "L-33": "e04X는 Aloc·Avul뿐. JASS의 20000+250000 이외 추가 네이티브 피해 없음",
        "RN-04": "A10I 방어력 -50 / 60초 및 Bd8(A04R)+15 (웨이브 68종 초기 레벨1)",
    }
    for row in data["units"]:
        fields = row["nativeAbilityFields"]
        detail = ", ".join(f"{key}={fields[key]}" for key in
                           ("DataA1", "DataB1", "Area1", "Dur1", "HeroDur1", "UnitID1")
                           if key in fields) or "위치·무적 전용"
        lines.append(f"| {row['catalogId']} | `{row['sourceCallback']}` | "
                     f"`{row['nativeAbilityRawcode'] or '없음'}` | {detail} | "
                     f"{meanings[row['catalogId']]} |")
    lines += ["", "## 남은 원작 결과 검증", "",
              "- 워크래프트 AUin 기절 실제 적용 시점/대상 타입/면역, 특히 보스 저항",
              "- AIsr(A04R) isr2 값을 실제 피해 계산에 적용하는 Warcraft 네이티브 경로",
              "- e00K 오라 소환물의 수명, A089 중첩 충돌과 실제 이동 틱",
              "- Afae 페어리 파이어의 시전 시작/적용 지연과 기존 방어력 감소 중첩",
              "- A0VS 원래 명령은 공격자 자기 위치 대상. 캐릭터의 실제 Warcraft facing 자료 없이는 "
              "투사체 방향/명중 동등성을 확정하지 않습니다.", "",
              "검증된 다섯 AUin의 `DataA1=0`을 별도의 공격 피해로 더하지 않습니다.", ""]
    return "\n".join(lines)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--w3x", type=Path, required=True)
    p.add_argument("--verify", action="store_true")
    a = p.parse_args()
    result = build(a.w3x)
    outputs = [(OUTPUT, json.dumps(result, ensure_ascii=False, indent=2) + "\n"),
               (REPORT, render(result))]
    for path, content in outputs:
        if a.verify:
            if path.read_text(encoding="utf-8") != content:
                raise ValueError("Stale native receipt: " + str(path))
        else:
            path.write_text(content, encoding="utf-8")
    print("Pinned source verified: 10 old followups, 5 AUin, 68 wave A04R")


if __name__ == "__main__":
    main()
