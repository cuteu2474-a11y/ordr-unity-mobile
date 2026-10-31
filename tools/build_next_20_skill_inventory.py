#!/usr/bin/env python3
"""Reconcile the next twenty unit skill leads without inventing JASS effects.

This is an object-ability/tooltip inventory, NOT a verified skill extraction.
The pinned original .w3x/war3map.j is required to resolve native abilities,
callback registrations and final damage/target/trigger behavior.
"""
import argparse
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
TARGETS = (
    "U-02", "U-06", "U-07", "U-08", "U-10", "U-11", "U-14",
    "S-04", "S-05", "S-08", "S-09", "S-12", "S-15", "S-17",
    "S-18", "S-19", "S-20", "S-22", "S-27", "S-28",
)
W3X_SHA = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"
JASS_SHA = "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d"
DATA_FILE = ROOT / "data/original-2323C-next-20-skill-inventory.json"
DOC_FILE = ROOT / "docs/다음-20명-스킬-원본근거.md"


def load(name):
    return json.loads((ROOT / "data" / name).read_text(encoding="utf-8"))


def create():
    source = load("original-2323C-skill-research-queue.json")
    status = load("unit-runtime-status.json")
    missing = load("original-2323C-missing-skill-profile-evidence.json")
    profiles = load("character-skill-profiles.json")
    if missing["sourceW3xSha256"] != W3X_SHA:
        raise ValueError("Original object source changed")
    by_source = {x["catalogId"]: x for x in source["units"]}
    by_status = {x["catalogId"]: x for x in status["entries"]}
    by_missing = {x["catalogUnitId"]: x for x in missing["characters"]}
    by_profile = {x["unitId"]: x for x in profiles["profiles"]}
    rows = []
    for uid in TARGETS:
        unit = by_source[uid]
        state = by_status[uid]
        ability_codes = unit["objectAbilityRawcodes"]
        # This file is an immutable PRE-EXTRACTION object-lead inventory.
        # Later model implementation is tracked in unit-runtime-status.json,
        # so nonempty connected effects must not rewrite historical claims.
        if (unit["mapUnitRawcode"] != state["originalRawcode"] or
                unit["unitName"] != state["name"] or
                unit["grade"] != state["grade"] or
                unit["connectedModelEffects"] != state["connectedModelEffects"]):
            raise ValueError("Runtime status mismatch: " + uid)
        if not (unit["mapUnitRawcode"] and ability_codes):
            raise ValueError("Object evidence missing: " + uid)
        profile = by_profile.get(uid)
        evidence = by_missing.get(uid)
        if (profile is None) == (evidence is None):
            raise ValueError("Tooltip/evidence classification mismatch: " + uid)
        if evidence and (evidence["mapUnitRawcode"] != unit["mapUnitRawcode"] or
                         evidence["originalAbilityRawcodesInMapOrder"] != ability_codes or
                         evidence["originalSkillJassTriggersResolved"] or
                         evidence["originalAbilityEffectsResolved"]):
            raise ValueError("Unresolved source evidence changed: " + uid)
        rows.append({
            "catalogId": uid,
            "name": unit["unitName"],
            "grade": unit["grade"],
            "originalUnitRawcode": unit["mapUnitRawcode"],
            "objectAbilityRawcodesInMapOrder": ability_codes,
            "lowFrequencyAbilityLeadsNotVerifiedAsCombatSkills": (
                evidence["lessFrequentlyObservedRawcodesForResearchOnly"]
                if evidence else []),
            "tooltipProfileAvailable": profile is not None,
            "tooltipSkillsNotJassVerified": profile["skills"] if profile else [],
            "tooltipSourceUrl": profile["sourceUrl"] if profile else evidence["webDexUrl"],
            "connectedModelEffectCount": 0,
            "attackOrCastCallbackFromOriginalJassVerified": False,
            "nativeAbilityEffectsVerified": False,
            "readyForFullSkillImplementation": False,
            "nextSourceWork": [
                "원본 JASS w5/w6/w7 및 개별 시전 등록·간접 호출 경로 감사",
                "원본 W3A 능력·보조 소환물과 범위/주기/확률/대상/피해 검증",
                "원본 동등성 여부와 개발 모델 실행 권한을 별도로 판정",
            ],
        })
    count = sum(bool(r["tooltipProfileAvailable"]) for r in rows)
    if len(rows) != 20 or count != 1:
        raise ValueError("Target and source profile count changed")
    result = {
        "format": "ordr-next-20-skill-object-leads-v1",
        "sourceW3xSha256": W3X_SHA,
        "pinnedJassSha256NotRereadInThisInventory": JASS_SHA,
        "runtimeAuthorization": False,
        "scope": "20 unit rawcodes, W3X object ability IDs and available tooltip details only",
        "importantLimit": "Original W3X/JASS source bytes are not in the repository; ability IDs do not establish their combat behavior.",
        "summary": {"unitCount": 20, "objectAbilitiesMapped": 20,
                    "tooltipProfiles": count, "tooltipProfilesMissing": 20 - count,
                    "originalAttackCastCallbacksVerifiedHere": 0,
                    "fullyImplementableWithoutFurtherOriginalExtraction": 0},
        "units": rows,
    }
    lines = [
        "# 다음 20명 스킬 원본 근거 추출표",
        "",
        "> 후속 분석에서 고정 원본 W3X/JASS를 직접 재추출했습니다. **[원본 JASS·네이티브 능력 분석 결과](다음-20명-원본-JASS-능력-분석.md)**가 최신 근거입니다. "
        "아래 수치는 재추출 전에 확보한 예비 입력 자료의 범위입니다.",
        "",
        "> 원본 W3X의 `UnitAbilities.slk`에서 확보했던 **유닛의 보유 능력 ID**를 재대조한 결과입니다. "
        "이는 실제 스킬·발동 확률·피해가 추출됐다는 뜻이 아닙니다.",
        "",
        "- 대상: **20명**, 원본 객체/능력 ID 매칭 **20명**",
        "- 도감 스킬 상세 확보 **1명(특별함 로우)** / 미확보 **19명**",
        "- 이번 표에서 새로 확인한 JASS 발동 함수 **0명**, 실제 스킬 실행 승인 **0명**",
        "- 원본 W3X SHA256: `" + W3X_SHA + "`",
        "- 원본 JASS SHA256(기존 고정 기준): `" + JASS_SHA + "`",
        "- 주의: 원본 `.w3x`/`war3map.j` 파일은 현재 저장소에 없으므로 "
        "JASS 콜백·네이티브 능력 효과까지 재추출할 수 없습니다.",
        "",
        "| ID | 유닛 | 원본 객체 | 보유 능력 ID(객체 순서) | 조사 우선 코드¹ | 도감 스킬 |",
        "|---|---|---|---|---|---|",
    ]
    for row in rows:
        codes = ", ".join("`" + x + "`" for x in row["objectAbilityRawcodesInMapOrder"])
        leads = ", ".join("`" + x + "`" for x in row["lowFrequencyAbilityLeadsNotVerifiedAsCombatSkills"]) or "—"
        tooltip = "1개(부분)" if row["tooltipProfileAvailable"] else "미확보"
        lines.append(f'| {row["catalogId"]} | {row["name"]} | `{row["originalUnitRawcode"]}` | {codes} | {leads} | {tooltip} |')
    lines += [
        "",
        "¹ 출현 빈도가 낮아 먼저 살펴볼 후보일 뿐 **전투 스킬로 검증되지 않았습니다**. "
        "조합·공통 시스템·무적 능력이 섞여 있을 수 있습니다.",
        "",
        "## 현재 확인 가능한 스킬 상세: S-05 트라팔가 로우",
        "",
        "- 도감 명칭: **선택 아군 가속** / 수동 시전 / 선택한 아군 유닛",
        "- 공격속도 **+100%**, 지속 **7초**, 재사용 대기 **50초**",
        "- 다른 공격속도 가속 효과와 중첩 제한으로 기록됨",
        "- **도감 설명일 뿐 JASS rawcode·실제 시전 조건·중첩 공식은 미검증**",
        "",
        "## 20명 실제 스킬 구현 전에 더 뽑아야 할 정보",
        "",
        "1. 고정 원본 `ORDR_S2_2.323[C].w3x`로부터 `war3map.j`와 `war3map.w3a`를 다시 확보하고 SHA 검증.",
        "2. 20명 각각의 공격 선언(`w5`), 스킬/시전 등록(`w6`, `w7` 등), "
        "능력 레벨·보조 주문·소환물을 호출 그래프까지 추적.",
        "3. 발동 확률/순서, 실제 타깃, 피해 타입·면역·지속시간, 중첩, "
        "보스·스토리 적용을 원본 출처와 함께 기록.",
        "4. 검증된 경로만 공통 실행 엔진에 등록하고, 나머지는 `MODEL_ONLY`/미구현으로 분리.",
        "",
        "재생성: `python3 tools/build_next_20_skill_inventory.py` / 검증: `--verify`.",
        "",
    ]
    return result, "\n".join(lines)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--verify", action="store_true")
    args = p.parse_args()
    evidence, report = create()
    outputs = ((DATA_FILE, json.dumps(evidence, ensure_ascii=False, indent=2) + "\n"),
               (DOC_FILE, report))
    for path, content in outputs:
        if args.verify:
            if not path.exists() or path.read_text(encoding="utf-8") != content:
                raise SystemExit("Stale skill inventory: " + str(path))
        else:
            path.write_text(content, encoding="utf-8")
    print("20 units mapped; 1 partial tooltip, 19 missing; 0 new JASS callbacks verified")


if __name__ == "__main__":
    main()
